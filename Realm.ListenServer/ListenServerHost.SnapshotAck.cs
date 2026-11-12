using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using MemoryPack;
using Realm.Ecs;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using Realm.Shared;
using Realm.Shared.Diagnostics;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Simulation;

namespace Realm.ListenServer;

/// <summary>snapshot acknowledgements, pacing and stalled peers.</summary>
public partial class ListenServerHost
	{
	private void HandleSnapshotAck(int peerId, ReadOnlySpan<byte> payload)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		// A peer that answers is a peer that is reading, whatever else the answer says: the count of descriptions it has
		// not answered starts over here, which is what tells the gate below that a screen has caught up.
		if (_unacknowledgedPublicationsPerPeer.ContainsKey(peerId)) _unacknowledgedPublicationsPerPeer[peerId] = 0;

		SnapshotAck ack;
		try
		{
			ack = MemoryPackSerializer.Deserialize<SnapshotAck>(payload);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
		{
			_log.Write($"peer {peerId} sent a snapshot acknowledgement that could not be read: {exception.Message}");
			return;
		}

		// A peer that reports an older sequence than it did before is a restarted screen rather than one going
		// backwards, so it is taken at the value it now holds.
		if (!_acknowledgedSequencePerPeer.TryGetValue(peerId, out int acknowledgedSequence) || ack.Sequence >= acknowledgedSequence)
		{
			// Answering with a description this peer had already acknowledged says it is still reading but not that it has
			// got any further, so only a sequence it had not reported before counts as progress.
			if (ack.Sequence > acknowledgedSequence) _lastAcknowledgedProgressTickPerPeer[peerId] = simulation.TickCount;
			_acknowledgedSequencePerPeer[peerId] = ack.Sequence;
		}

		_roundTripMillisecondsPerPeer[peerId] = Math.Max(0, ack.RoundTripMilliseconds);

		LogAcknowledgedSequence(peerId, ack);

		if (!_firstSnapshotSequencePerPeer.TryGetValue(peerId, out int firstDescribedSequence)) return;

		int describedSequences = ack.Sequence - firstDescribedSequence;
		if (describedSequences <= 0) return;

		// Only sequences this peer was actually described with can be missing: the mask cannot claim a bit for a
		// sequence the match never sent, or every acknowledgement would describe the peer from scratch.
		int coveredBits = Math.Min(64, describedSequences + 1);
		ulong expected = coveredBits >= 64 ? ulong.MaxValue : (1UL << coveredBits) - 1UL;
		ulong missing = expected & ~ack.AppliedMask;
		if ((ack.AppliedMask & expected) == expected) return;

		int missingCount = 0;
		int missingBits = 0;
		Span<int> missingSequences = stackalloc int[SnapshotFrameRing.Capacity];
		for (int bit = 0; bit < 64; bit++)
		{
			if ((missing & (1UL << bit)) == 0) continue;

			missingBits++;
			if (missingCount < missingSequences.Length) missingSequences[missingCount++] = ack.Sequence - bit;
		}

		// A repair answered once is not asked for again on every answer that still reports the same hole: the
		// description is already on its way, and asking again each time spent the request budget on nothing.
		long tick = simulation.TickCount;
		int oldestMissingSequence = missingCount > 0 ? missingSequences[missingCount - 1] : 0;

		// An acknowledgement describes the world a whole round trip ago, so the holes it still reports are usually the
		// ones the peer has since asked for by name. Answering each of them again puts descriptions on the reliable
		// stream that the peer already holds, and on a slow link that queue is what every description behind it waits on.
		if (_lastRepairSentTickPerPeer.TryGetValue(peerId, out long lastRepairTick) && tick - lastRepairTick < RepairCooldownTicks) return;

		if (missingBits <= SnapshotFrameRing.Capacity && TryResendMissingDescriptions(peerId, missingSequences.Slice(0, missingCount)))
		{
			RecordRepairSent(peerId, oldestMissingSequence, tick);
			_log.Write($"peer {peerId} is missing {missingBits} description(s); the missing ones are sent again");
			return;
		}

		if (!TryClaimBaselineRequest(peerId, tick)) return;

		LagProbe.Count(LagEvent.AuthorityBaselinesBecauseAcknowledgementMissed);
		simulation.RequestBaseline(peerId);
	}

	private void LogAcknowledgedSequence(int peerId, in SnapshotAck ack)
	{
		if (_lastLoggedAckSequencePerPeer.TryGetValue(peerId, out long lastLogged) && ack.Sequence - lastLogged < AckReportInterval) return;

		_lastLoggedAckSequencePerPeer[peerId] = ack.Sequence;
		_log.Write($"peer {peerId} acknowledged sequence {ack.Sequence} with mask {ack.AppliedMask:x16} across a round trip of {ack.RoundTripMilliseconds} ms");
	}

	/// <summary>
	///     Whether this peer needs the world stated again rather than described again. The safety refresh used to run every
	///     ten seconds whatever the peer was saying, which put a hundred kilobytes on that peer's reliable stream for a
	///     screen whose every acknowledgement was proving continuity, and held everything queued behind it while it
	///     travelled. What is left is the case it was written for: a peer that has stopped acknowledging cannot say whether
	///     it is still holding a world, and only a description from scratch puts that beyond doubt.
	/// </summary>
	private bool IsRefreshDue(int peerId, long tick)
	{
		// A peer that has not answered once is not a peer that has stopped answering: it is a screen that is still
		// loading the world it was handed, and it cannot acknowledge until it has applied something. Counting that load
		// as a stall described the whole world to every screen joining in turn, once a second for as long as it loaded,
		// which is where both the baseline traffic and the delay of joining came from.
		if (!_acknowledgedSequencePerPeer.ContainsKey(peerId)) return false;

		return _lastAcknowledgedProgressTickPerPeer.TryGetValue(peerId, out long lastProgressTick) && tick - lastProgressTick >= SnapshotRefreshStallTicks;
	}

	private bool TryClaimBaselineRequest(int peerId, long tick)
	{
		if (_lastBaselineRequestTickPerPeer.TryGetValue(peerId, out long lastRequestTick) && tick - lastRequestTick < BaselineRequestCooldownTicks) return false;

		_lastBaselineRequestTickPerPeer[peerId] = tick;
		return true;
	}

	/// <summary>
	///     Drops a peer that has stopped reading altogether, as opposed to one that has merely fallen behind. A peer that is
	///     behind is still described on every tick: a description is a few hundred bytes, while the only thing a skip can
	///     escalate to is a baseline, which is over a hundred times larger and is paid again on every cooldown. A skip also
	///     leaves a sequence the peer was never sent, which its acknowledgement reports as a hole no ring can fill, so the
	///     description that would have filled the hole is replaced by the baseline the skip was meant to avoid.
	/// </summary>
	private void DropPeerIfItStoppedReading(int peerId, long tick)
	{
		if (!_unacknowledgedPublicationsPerPeer.TryGetValue(peerId, out int unacknowledged)) return;

		if (unacknowledged < 0)
		{
			if (!_seatedAtTickPerPeer.TryGetValue(peerId, out long seatedAtTick)) return;
			if (tick - seatedAtTick < NeverAnsweredGraceTicks) return;

			_log.Write($"peer {peerId} has been described for {NeverAnsweredGraceTicks / MatchTiming.SimulationTickRateHz}s without answering once; it is measured as a peer that is behind");
			_unacknowledgedPublicationsPerPeer[peerId] = 0;
			unacknowledged = 0;
		}

		_unacknowledgedPublicationsPerPeer[peerId] = unacknowledged + 1;

		if (unacknowledged < MaximumUnacknowledgedBeforeDisconnect) return;

		_log.Write($"peer {peerId} has left {unacknowledged} descriptions unanswered; the match drops it");
		_seatedPeers.Remove(peerId);
		_transport.Disconnect(peerId);
	}

	private const long AckReportInterval = 100;

	private const long BaselineRequestCooldownTicks = MatchTiming.BaselineRequestCooldownTicks;

	private const int NeverAnsweredGraceTicks = MatchTiming.SimulationTickRateHz * 45;

	private const int MaximumUnacknowledgedBeforeDisconnect = MatchTiming.SnapshotRateHz * 15;

	private const long RepairCooldownTicks = MatchTiming.SnapshotRepairCooldownTicks;

	private readonly Dictionary<int, long> _lastRepairSentTickPerPeer = new();

	private readonly Dictionary<int, int> _lastRepairFirstSequencePerPeer = new();

	private long _unreliableSnapshotCount;
	private long _reliableSnapshotCount;
	private long _fragmentedSnapshotCount;
}
