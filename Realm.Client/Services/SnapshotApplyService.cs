using System.Collections.Generic;
using Godot;
using MemoryPack;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Testing;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Shared.Network;

namespace Realm.Client.Services;

/// <summary>
///     The path a description takes from the wire into the world this screen holds: which descriptions are worth
///     applying, which have to wait for the baseline they were written against, and what this peer tells the authority it
///     has applied. It owns the only copy of that bookkeeping, so the transport can hand over a frame and forget it,
///     and the acknowledgement always names exactly the sequences the apply path reached.
///     Applying a unit, a component or a loot entry belongs to <see cref="NetworkService" />, which owns the mapping
///     between the authority's identifiers and this screen's entities; this service decides the order and the timing of
///     that work rather than what it does.
/// </summary>
public class SnapshotApplyService
{
	private const int MaximumQueuedDeltas = 512;

	/// <summary>
	///     How far behind the last applied description a queued description may be before the queue is answered with a
	///     baseline instead of being applied. A description states a change against what this peer was last told, so a deep
	///     queue cannot be skipped - but replaying it in one frame is the work that produced the hitch which built it, and
	///     the world it describes is already a moment in the past. A baseline is self-contained and re-anchors this screen
	///     in one round trip, which is why the backlog is answered rather than drained.
	///     It is the deepest presentation delay rather than a figure of its own: a screen that has missed that much of the
	///     stream is further behind than the buffer it renders from is ever meant to cover, so the two bounds are the same
	///     decision and stating one beside the other would let a tuning of the buffer leave this behind.
	/// </summary>
	private const int MaximumBacklogSequences = SnapshotTimeline.MaximumDelaySequences;

	private readonly List<WorldSnapshot> _queuedDeltas = new();

	private ulong _lastBaselineRequestTime;

	public void Accept(byte[] payload) => Accept(MemoryPackSerializer.Deserialize<WorldSnapshot>(SnapshotWireFormat.Decode(payload)));

	public void Accept(WorldSnapshot snapshot)
	{
		Entity worldEntity = GameContext.NetworkService.FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkState>(worldEntity)) return;
		GameContext.World.Get<NetworkState>(worldEntity).LastSnapshotReceivedTime = Time.GetTicksMsec();
		ProcessSnapshot(snapshot);

		Entity currentWorldEntity = GameContext.NetworkService.FindWorldEntity();
		if (currentWorldEntity == Entity.Null || !GameContext.World.Has<NetworkState>(currentWorldEntity)) return;
		if (!GameContext.World.Get<NetworkState>(currentWorldEntity).HasReceivedInitialBaseline) RequestBaseline();
	}

	/// <summary>
	///     Asks the authority to describe this peer from scratch. A peer that has lost its place can only be repaired by
	///     a baseline, and this is the one place that asks. It is throttled on the same policy the authority grants one on
	///     rather than a figure of its own: the two clocks were half a second here and a second there, so the client asked
	///     twice as often as the match would answer and half of every repair was spent on a request already in flight.
	/// </summary>
	public void RequestBaseline()
	{
		ulong now = Time.GetTicksMsec();
		if (now - _lastBaselineRequestTime < (ulong)MatchTiming.BaselineRequestCooldownMilliseconds) return;

		_lastBaselineRequestTime = now;
		GameContext.Host?.RequestSnapshotBaseline();
	}

	private void ProcessSnapshot(WorldSnapshot snapshot)
	{
		if (MultiplayerSyncDiagnostics.IsRecording) MultiplayerSyncDiagnostics.RecordClientSeenSequence(snapshot.Sequence);
		Entity worldEntity = GameContext.NetworkService.FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkState>(worldEntity)) return;

		ref var networkState = ref GameContext.World.Get<NetworkState>(worldEntity);

		if (snapshot.IsBaseline)
		{
			if (snapshot.Sequence <= networkState.LastReceivedBaselineSeq) return;

			// A baseline is larger than a delta and travels on the same reliable stream, so it can arrive after deltas that
			// already describe a newer world. Applying it would move every replicated unit backwards, and a unit presented
			// from a newer description would then be moved to positions this screen has already passed. The baseline is
			// stale, not useful.
			if (snapshot.Sequence <= networkState.LastAppliedSnapshotSequence)
			{
				// The baseline is stale for the units, but it is still the base every later delta is written against, so it
				// is recorded and the deltas that were waiting for it are applied here. Returning without draining left them
				// queued until some future baseline arrived, which on a delayed link was several seconds of a frozen world.
				networkState.LastReceivedBaselineSeq = snapshot.Sequence;
				DrainQueuedDeltas(ref networkState);
				return;
			}

			// The first baseline of a match describes a world this screen has never been told about, so nothing held from
			// a previous match can be sampled against it: the sequences of the two matches overlap and a description left
			// over from the first would be treated as the newest one held for whichever identifier it was filed under.
			if (!networkState.HasReceivedInitialBaseline) GameContext.SnapshotTimeline.Clear();

			networkState.LastReceivedBaselineSeq = snapshot.Sequence;
			networkState.HasReceivedInitialBaseline = true;
			networkState.LastAppliedSnapshotSequence = snapshot.Sequence;
			_queuedDeltas.RemoveAll(d => d.Sequence <= snapshot.Sequence);
			// A baseline restates the whole world, so every sequence before it is superseded rather than missing. Keeping
			// the earlier mask left the hole that caused this baseline in the acknowledgement window for another two
			// seconds, which reported the peer as still missing state it had just been given and repaired it again on
			// every cooldown.
			_appliedSnapshots.Reset();
			GameContext.NetworkService.ApplySnapshotToWorld(snapshot);

			DrainQueuedDeltas(ref networkState);
			return;
		}

		if (snapshot.Sequence <= networkState.LastAppliedSnapshotSequence) return;

		// The authority describes every seated peer on every tick, so the sequences this screen is sent are contiguous and
		// one it never applied is a description that did not arrive rather than one it was never meant to see.
		int sequencesSkipped = snapshot.Sequence - networkState.LastAppliedSnapshotSequence - 1;

		// A queue deeper than the buffer is a screen that stopped reading, not a link that is merely late: applying it in
		// one frame costs more the deeper it is and leaves the world one frame closer to the moment it describes, whereas a
		// baseline is a few kilobytes and puts the screen where the authority is. The queued descriptions are the tail of
		// the same backlog, so they go with it.
		if (sequencesSkipped >= MaximumBacklogSequences)
		{
			_queuedDeltas.Clear();
			RequestBaseline();
			return;
		}

		// A delta names the oldest world state it assumes, not the newest baseline the host has started sending. It used
		// to require an exact match, so every delta the host built on a baseline that was still in flight was queued until
		// that baseline arrived: with a 138 KB baseline on the reliable channel that stalled the client for one burst per
		// baseline interval, which is what the rendered transforms showed as a lag that grew and healed once a second. A
		// delta is absolute per unit, so a base the client has already superseded is harmless.
		if (!networkState.HasReceivedInitialBaseline || snapshot.BaseSequence > networkState.LastReceivedBaselineSeq)
		{
			// A delta the client cannot place is a delta whose base it never applied. Only waiting for one used to be
			// enough because the base could not move without the baseline that moved it, but a baseline that is superseded
			// in flight leaves the client queuing against a base nobody sent it any more: the match then ran for seconds
			// on a frozen sequence, which is a world nothing on this screen moves against. Asking for the world the deltas
			// are written against bounds the recovery to one round trip.
			QueueDelta(snapshot);
			RequestBaseline();
			return;
		}

		networkState.LastAppliedSnapshotSequence = snapshot.Sequence;
		GameContext.NetworkService.ApplySnapshotToWorld(snapshot);
		DrainQueuedDeltas(ref networkState);

		// A delta names the last baseline it was written against and never the last delta, so a description that did not
		// arrive leaves this screen applying a change over a world it never received. One sequence missing is a datagram
		// the unreliable stream dropped, and the acknowledgement already reports it: the mask it carries leaves that
		// sequence's bit clear and the authority describes the hole once its own cooldown allows. Asking as well repaired
		// the same hole twice - a full description and a reliable frame of it per peer per second on a link losing three
		// percent - for state the ack was about to report. A gap of two or more is not a dropped datagram but a stall, and
		// asking for the world is the one thing that puts the screen back where the authority is rather than leaving it to
		// apply changes over a hole it will not be told about until the next baseline. A delta that could not be placed at
		// all never reaches this point: it is answered by the branch above, which knows the base it is missing.
		if (sequencesSkipped > 1) RequestBaseline();
	}

	/// <summary>
	/// Applies every queued delta that the client now has the base for, in sequence order. A burst arrives when a lost
	/// baseline is finally delivered, and the deltas behind it are still newer than what the client has applied, so
	/// leaving them queued would discard the world state the transport went to the trouble of recovering.
	/// </summary>
	private void DrainQueuedDeltas(ref NetworkState networkState)
	{
		while (_queuedDeltas.Count > 0)
		{
			WorldSnapshot nextDelta = _queuedDeltas[0];
			if (nextDelta.BaseSequence > networkState.LastReceivedBaselineSeq) break;
			_queuedDeltas.RemoveAt(0);
			if (nextDelta.Sequence <= networkState.LastAppliedSnapshotSequence) continue;

			networkState.LastAppliedSnapshotSequence = nextDelta.Sequence;
			GameContext.NetworkService.ApplySnapshotToWorld(nextDelta);
		}
	}

	private AppliedSequenceWindow _appliedSnapshots;
	private int _lastAcknowledgedSequence = -1;

	/// <summary>
	///     How many sequences may pass between two acknowledgements. One per applied description is thirty reliable frames a
	///     second from every peer, each stating what the next one states again; two still reports a hole within a fifteenth of
	///     a second. A description this peer could not place is answered at once, because that answer is what the match
	///     repairs from.
	/// </summary>
	private const int AcknowledgementIntervalSequences = 2;

	public void Acknowledge(int sequence, bool fullyApplied)
	{
		if (GameContext.Host == null) return;

		if (fullyApplied && MultiplayerSyncDiagnostics.ConsumeSnapshotDiscardRequest())
		{
			fullyApplied = false;
			MultiplayerSyncDiagnostics.RecordDiscardedSnapshotSequence(sequence);
		}

		if (fullyApplied) _appliedSnapshots.MarkApplied(sequence);
		else _appliedSnapshots.Advance(sequence);
		if (_appliedSnapshots.Highest < 0) return;

		int highest = _appliedSnapshots.Highest;
		if (fullyApplied && highest - _lastAcknowledgedSequence < AcknowledgementIntervalSequences) return;

		_lastAcknowledgedSequence = highest;
		GameContext.Host.SendSnapshotAck(highest, _appliedSnapshots.BuildMask());
	}

	private void QueueDelta(WorldSnapshot snapshot)
	{
		if (_queuedDeltas.Count >= MaximumQueuedDeltas) _queuedDeltas.RemoveAt(0);

		int insertIndex = _queuedDeltas.Count;
		while (insertIndex > 0 && _queuedDeltas[insertIndex - 1].Sequence > snapshot.Sequence) insertIndex--;
		_queuedDeltas.Insert(insertIndex, snapshot);
	}

	/// <summary>
	///     Forgets the match. A queue and an acknowledgement window left over from the previous one name sequences that
	///     belong to a world this screen is no longer holding, and a delta queued against them would be applied to it.
	/// </summary>
	public void Clear()
	{
		_queuedDeltas.Clear();
		_appliedSnapshots.Reset();
		_lastAcknowledgedSequence = -1;
		_lastBaselineRequestTime = 0;
	}
}
