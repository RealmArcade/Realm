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

/// <summary>answering a peer that could not apply a description.</summary>
public partial class ListenServerHost
	{
	/// <summary>
	///     Answers a screen that received a description it could not apply. The description is not sent again: it arrived,
	///     and whatever this screen could not make of it would be made again of the same bytes. What it carried is state the
	///     screen does not hold, so the match forgets what it was told and describes all of it once more.
	/// </summary>
	private void HandleDescriptionRepeatRequest(int peerId)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		simulation.RequestDescriptionRepeat(peerId);
		_log.Write($"peer {peerId} could not apply a description; it is told about every unit and text again next tick");
	}

	/// <summary>
	///     Answers a peer that names a run of descriptions it never received. The run is only answered while this match still
	///     holds every one of them: a run wider than the ring, or one the ring has already passed, says the peer stopped
	///     reading rather than that a datagram went missing, and nothing short of a baseline puts that screen back.
	/// </summary>
	private void HandleSnapshotRepairRequest(int peerId, ReadOnlySpan<byte> payload)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		SnapshotRepairRequest request;
		try
		{
			request = MemoryPackSerializer.Deserialize<SnapshotRepairRequest>(payload);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
		{
			_log.Write($"peer {peerId} sent a repair request that could not be read: {exception.Message}");
			return;
		}

		if (request.Count <= 0 || request.Count > SnapshotFrameRing.Capacity || request.FirstSequence <= 0) return;

		NoteLostDescriptions(peerId, in request);

		long tick = simulation.TickCount;

		// Only a repeat of the run this peer was last answered for is suppressed by the cooldown. A different run is a
		// different hole: swallowing it left the peer waiting out its repair grace for descriptions the match had
		// decided not to send, and it paid for one lost description with a whole world.
		if (IsRepairRepeat(peerId, request.FirstSequence, tick)) return;

		Span<int> missingSequences = stackalloc int[request.Count];
		for (int index = 0; index < missingSequences.Length; index++) missingSequences[index] = request.FirstSequence + index;

		if (TryResendMissingDescriptions(peerId, missingSequences))
		{
			RecordRepairSent(peerId, request.FirstSequence, tick);
			_log.Write($"peer {peerId} asked for {request.Count} description(s) from sequence {request.FirstSequence}; they are sent again");
			return;
		}

		if (!TryClaimBaselineRequest(peerId, tick)) return;

		LagProbe.Count(LagEvent.AuthorityBaselinesBecausePeerAsked);
		simulation.RequestBaseline(peerId);
		_log.Write($"peer {peerId} asked for a run this match no longer holds; it is described from scratch");
	}

	/// <summary>
	///     Sends each of these descriptions again, exactly as the peer was first sent them. Every one is checked before any
	///     is sent, because half a repaired run is a peer told the wrong thing about where the world is.
	/// </summary>
	private bool TryResendMissingDescriptions(int peerId, ReadOnlySpan<int> missingSequences)
	{
		if (missingSequences.Length == 0) return false;
		if (!_frameRings.TryGetValue(peerId, out SnapshotFrameRing? ring)) return false;

		for (int index = 0; index < missingSequences.Length; index++)
		{
			if (!ring.TryGet(missingSequences[index], out _)) return false;
		}

		for (int index = 0; index < missingSequences.Length; index++)
		{
			ring.TryGet(missingSequences[index], out byte[] frame);

			// A repair is asked for once and answered over the stream that cannot lose it: the peer has already shown
			// that it is missing state, and losing the repair as well would cost another round trip to learn the same.
			_transport.Send(peerId, NetworkFrameKind.Snapshot, frame, FrameDelivery.Reliable);
		}

		LagProbe.Count(LagEvent.AuthorityRepairDescriptionsSent, missingSequences.Length);
		return true;
	}

	/// <summary>
	///     Counts the descriptions a peer says never reached it, and arms the repeat only once the loss is a rate rather
	///     than an incident. A request naming a hole is the only loss measurement on this side - a screen heals the
	///     acknowledgement mask with every repair it receives, so a link that loses a quarter of its datagrams can answer
	///     with a mask that reports nothing missing at all - but one lost description out of hundreds is a link that is
	///     working. Arming a repeat for it sent a quarter of a healthy link's descriptions twice, which that screen then
	///     discarded as older than what it already held.
	/// </summary>
	private void NoteLostDescriptions(int peerId, in SnapshotRepairRequest request)
	{
		if (request.Reason != SnapshotRepairReason.WireLoss) return;

		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		long tick = simulation.TickCount;
		if (!_lossWindowStartedTickPerPeer.TryGetValue(peerId, out long windowStarted) || tick - windowStarted > LossyPeerMemoryTicks)
		{
			_lossWindowStartedTickPerPeer[peerId] = tick;
			_descriptionsLostByPeer[peerId] = 0;
		}

		// An ask is one incident whatever width it names: a screen joining the match names the whole run it was never
		// sent, and counting that run as a dozen separate losses armed the repeat for a link that had lost nothing.
		int incidents = _descriptionsLostByPeer[peerId] + 1;
		_descriptionsLostByPeer[peerId] = incidents;
		if (incidents < LossyPeerMinimumLostDescriptions) return;

		_peersThatLoseDescriptions.Add(peerId);
	}

	/// <summary>
	///     Whether this peer is asking again for the run it was last answered for, while that answer is still in flight.
	///     A run the match has not been asked for yet is a new hole and is answered however recently the last repair was
	///     sent, because a peer can only meet holes as fast as its link loses datagrams.
	/// </summary>
	private bool IsRepairRepeat(int peerId, int firstSequence, long tick)
	{
		if (!_lastRepairSentTickPerPeer.TryGetValue(peerId, out long lastRepairTick)) return false;
		if (!_lastRepairFirstSequencePerPeer.TryGetValue(peerId, out int lastFirstSequence)) return false;
		if (lastFirstSequence != firstSequence) return false;
		return tick - lastRepairTick < RepairCooldownTicks;
	}

	private void RecordRepairSent(int peerId, int firstSequence, long tick)
	{
		_lastRepairSentTickPerPeer[peerId] = tick;
		_lastRepairFirstSequencePerPeer[peerId] = firstSequence;
	}

	/// <summary>
	///     The ring this peer's descriptions are kept in, created the first time the peer is described. A ring belongs to
	///     the peer rather than to the seat it was published in, so no seat taken or given up elsewhere can address it.
	/// </summary>
	private SnapshotFrameRing FrameRingFor(int peerId)
	{
		if (_frameRings.TryGetValue(peerId, out SnapshotFrameRing? ring)) return ring;

		ring = new SnapshotFrameRing(peerId);
		_frameRings[peerId] = ring;
		return ring;
	}
}
