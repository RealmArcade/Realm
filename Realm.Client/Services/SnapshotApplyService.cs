using System;
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

public class SnapshotApplyService
{
	private const int MaximumQueuedDeltas = 512;

	private const int MaximumBacklogSequences = SnapshotTimeline.MaximumDelaySequences;

	/// <summary>
	///     The widest run of descriptions this screen asks the match to send again. A wider run is a stall rather than a
	///     datagram that went missing, and a baseline is the only thing that puts a screen that far behind back.
	/// </summary>
	private const int MaximumRepairSequences = 8;

	private const int RepairRequestIntervalMilliseconds = 120;

	/// <summary>
	///     The least and the most time a description that arrived ahead of a missing one is held before the screen gives up
	///     on the run and asks for the world instead. The grace has to cover the repair's whole journey - the request out,
	///     the cooldown the authority keeps between repairs, and the description back - so it is taken from the link rather
	///     than fixed: a run held for less than its repair takes is a run thrown away and paid for with a baseline.
	/// </summary>
	private const int MinimumRepairGraceMilliseconds = 250;

	private const int MaximumRepairGraceMilliseconds = 1200;

	/// <summary>
	///     How long a description may go missing before this screen asks about it. It is the depth the presentation buffer
	///     already covers plus two intervals, because past that the screen draws its newest description and gets no closer
	///     to the match: the acknowledgement that would have reported the hole is written by a description arriving, so a
	///     stream that stops entirely reports nothing at all.
	/// </summary>
	private const int MinimumSilenceMilliseconds = 250;

	private const int MaximumSilenceMilliseconds = 1000;

	/// <summary>
	///     How many silent windows pass before the screen stops asking for the description it expected and asks for the
	///     world instead. A delta names only what changed since the one before it, so a run the match no longer holds is
	///     answered by a baseline and by nothing else.
	/// </summary>
	private const int SilentWindowsBeforeBaseline = 2;

	private readonly List<WorldSnapshot> _queuedDeltas = new();

	private int _queuedDeltaHead;

	private ulong _lastBaselineRequestTime;

	private bool _baselineRequestDeferred;

	private ulong _repairDeadlineMilliseconds;
	private int _repairFirstSequence = -1;
	private ulong _lastRepairRequestTime;

	private ulong _lastDescriptionArrivalMilliseconds;
	private ulong _lastSilenceRequestMilliseconds;
	private int _silentWindows;

	public void Accept(WorldSnapshot snapshot)
	{
		// A description that arrived, whatever this screen goes on to make of it, is the answer to a watch that asked
		// about a stream that had stopped.
		_lastDescriptionArrivalMilliseconds = Time.GetTicksMsec();
		_silentWindows = 0;

		if (_baselineRequestDeferred) RequestBaseline();

		Entity worldEntity = GameContext.NetworkService.FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkState>(worldEntity))
		{
			RequestBaseline();
			return;
		}
		GameContext.World.Get<NetworkState>(worldEntity).LastSnapshotReceivedTime = Time.GetTicksMsec();
		ProcessSnapshot(snapshot);

		Entity currentWorldEntity = GameContext.NetworkService.FindWorldEntity();
		if (currentWorldEntity == Entity.Null || !GameContext.World.Has<NetworkState>(currentWorldEntity)) return;
		if (!GameContext.World.Get<NetworkState>(currentWorldEntity).HasReceivedInitialBaseline) RequestBaseline();
	}

	public void RequestBaseline()
	{
		ulong now = Time.GetTicksMsec();
		// An ask the cooldown refused is delivered on the next description rather than dropped: a repair refused a moment
		// ago is still owed one now, and dropping it left nothing in flight and nothing asking again.
		if (now - _lastBaselineRequestTime < (ulong)MatchTiming.BaselineRequestCooldownMilliseconds)
		{
			_baselineRequestDeferred = true;
			return;
		}

		_lastBaselineRequestTime = now;
		_baselineRequestDeferred = false;
		GameContext.Host?.RequestSnapshotBaseline();
	}

	private void ProcessSnapshot(WorldSnapshot snapshot)
	{
		if (MultiplayerSyncDiagnostics.IsRecording) MultiplayerSyncDiagnostics.RecordClientSeenSequence(snapshot.Sequence);
		Entity worldEntity = GameContext.NetworkService.FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkState>(worldEntity)) return;

		ref var networkState = ref GameContext.World.Get<NetworkState>(worldEntity);

		// Descriptions held behind a run that never arrived are a world this screen cannot reach by applying them: the
		// run was asked for, and once the grace to answer it has passed the only thing left is the world itself.
		if (QueuedDeltaCount > 0 && HasWaitedPastRepairGrace())
		{
			ClearQueuedDeltas();
			RequestBaseline();
			return;
		}

		if (snapshot.IsBaseline)
		{
			if (snapshot.Sequence <= networkState.LastReceivedBaselineSeq) return;

			// A baseline travels on the same reliable stream as the deltas, so it can arrive after deltas that already
			// describe a newer world. Applying its units would move every replica backwards, but it is still the base the
			// deltas in flight are written against, so it is recorded and the deltas waiting for it are applied here.
			if (snapshot.Sequence <= networkState.LastAppliedSnapshotSequence)
			{
				networkState.LastReceivedBaselineSeq = snapshot.Sequence;
				DrainQueuedDeltas(ref networkState);
				return;
			}

			// The first baseline of a match describes a world this screen has never been told about: a description left over
			// from a previous match would be sampled as the newest one held for whichever identifier it was filed under.
			if (!networkState.HasReceivedInitialBaseline) GameContext.SnapshotTimeline.Clear();

			networkState.LastReceivedBaselineSeq = snapshot.Sequence;
			networkState.HasReceivedInitialBaseline = true;
			networkState.LastAppliedSnapshotSequence = snapshot.Sequence;
			// A baseline restates the whole world, so every sequence before it is superseded rather than missing. Keeping the
			// earlier mask reported the peer as still missing state it had just been given, and repaired it again each
			// cooldown.
			while (_queuedDeltaHead < _queuedDeltas.Count && _queuedDeltas[_queuedDeltaHead].Sequence <= snapshot.Sequence) _queuedDeltaHead++;
			CompactQueuedDeltas();
			ForgetRepairRun();
			_appliedSnapshots.Reset();
			GameContext.NetworkService.ApplySnapshotToWorld(snapshot);

			DrainQueuedDeltas(ref networkState);
			return;
		}

		// A description already behind this screen is not missing from it: it arrived after the world moved past it, so
		// its content is superseded by what was applied in its place. Recording it keeps the acknowledgement about what
		// this screen is still owed rather than about the order the descriptions happened to be used in - a repair that
		// arrives too late heals the mask it was asked for, and the hole is not reported forever.
		if (snapshot.Sequence <= networkState.LastAppliedSnapshotSequence)
		{
			_appliedSnapshots.MarkApplied(snapshot.Sequence);
			return;
		}

		int sequencesSkipped = snapshot.Sequence - networkState.LastAppliedSnapshotSequence - 1;

		// A queue deeper than the buffer is a screen that stopped reading, not a link that is merely late: applying it in
		// one frame costs more the deeper it is, whereas a baseline is a few kilobytes and puts the screen where the
		// authority is. The queued descriptions are the tail of the same backlog, so they go with it.
		if (sequencesSkipped >= MaximumBacklogSequences)
		{
			ClearQueuedDeltas();
			ForgetRepairRun();
			RequestBaseline();
			return;
		}

		// A delta names the oldest world state it assumes, not the newest baseline the host has started sending. An exact
		// match was required here once, so every delta built on a baseline still in flight was queued until it arrived -
		// with a 138 KB baseline on the reliable channel that stalled the client once per baseline interval. A delta is
		// absolute per unit, so a base the client has already superseded is harmless.
		if (!networkState.HasReceivedInitialBaseline || snapshot.BaseSequence > networkState.LastReceivedBaselineSeq)
		{
			QueueDelta(snapshot);
			RequestBaseline();
			return;
		}

		// A run this screen was never told is not lateness it can wait out. A delta states the units it mentions as they
		// are at that moment and mentions only what changed since the description before it, so one applied over a hole
		// leaves exactly those units behind that only the missing descriptions mentioned. The run is asked for by name
		// and this description is held behind it until it arrives or the grace to answer it runs out.
		if (sequencesSkipped > 0)
		{
			HoldForRepair(snapshot, networkState.LastAppliedSnapshotSequence + 1, sequencesSkipped);
			return;
		}

		networkState.LastAppliedSnapshotSequence = snapshot.Sequence;
		GameContext.NetworkService.ApplySnapshotToWorld(snapshot);
		DrainQueuedDeltas(ref networkState);
		if (QueuedDeltaCount == 0) ForgetRepairRun();
	}

	/// <summary>
	///     Asks about a stream of descriptions that has stopped. Nothing else in this screen can: every other recovery path
	///     here is entered by a description arriving, so a run that simply ends leaves the picture frozen on the newest
	///     description this screen holds until the match is declared lost tens of seconds later.
	/// </summary>
	public void ObserveDescriptionSilence()
	{
		if (GameContext.Host == null) return;
		if (GameContext.NetworkService == null || GameContext.NetworkService.IsPaused) return;

		int newestSequence = GameContext.SnapshotTimeline.NewestSequence;
		if (newestSequence < 0) return;

		ulong now = Time.GetTicksMsec();
		int silenceMilliseconds = ResolveSilenceMilliseconds();
		if (now - _lastDescriptionArrivalMilliseconds < (ulong)silenceMilliseconds) return;
		if (now - _lastSilenceRequestMilliseconds < (ulong)silenceMilliseconds) return;

		_lastSilenceRequestMilliseconds = now;
		_silentWindows++;

		if (_silentWindows >= SilentWindowsBeforeBaseline)
		{
			_silentWindows = 0;
			RequestBaseline();
			return;
		}

		// The description this screen is owed next is the one the match published after the newest it holds. A run the
		// match no longer holds is answered by a baseline of its own, so this asks by name first and only rebuilds when
		// naming it has not worked.
		GameContext.Host.RequestSnapshotRepair(newestSequence + 1, 1);
	}

	/// <summary>
	///     How long this link may leave this screen without a description before the silence is a fault rather than the
	///     buffer doing its job. The buffer already covers the delay it was sized to, so the wait is that depth and no more.
	/// </summary>
	private static int ResolveSilenceMilliseconds()
	{
		float bufferMilliseconds = GameContext.SnapshotTimeline.DelayMilliseconds;
		float silenceMilliseconds = bufferMilliseconds + MatchTiming.SnapshotIntervalSeconds * 2000f;
		return Math.Clamp((int)MathF.Ceiling(silenceMilliseconds), MinimumSilenceMilliseconds, MaximumSilenceMilliseconds);
	}

	/// <summary>
	///     How many descriptions are still waiting to be applied. The queue is consumed from its head rather than by
	///     removing the first element, because every held description is several kilobytes and shifting a list of them was
	///     work done once per description on the tick that could least afford it.
	/// </summary>
	private int QueuedDeltaCount => _queuedDeltas.Count - _queuedDeltaHead;

	private void ClearQueuedDeltas()
	{
		_queuedDeltas.Clear();
		_queuedDeltaHead = 0;
	}

	private void CompactQueuedDeltas()
	{
		if (_queuedDeltaHead == 0) return;

		_queuedDeltas.RemoveRange(0, _queuedDeltaHead);
		_queuedDeltaHead = 0;
	}

	private void DrainQueuedDeltas(ref NetworkState networkState)
	{
		if (QueuedDeltaCount > 0
		    && _queuedDeltas[^1].Sequence - networkState.LastAppliedSnapshotSequence > MaximumBacklogSequences)
		{
			ClearQueuedDeltas();
			RequestBaseline();
			return;
		}

		while (_queuedDeltaHead < _queuedDeltas.Count)
		{
			WorldSnapshot nextDelta = _queuedDeltas[_queuedDeltaHead];
			if (nextDelta.BaseSequence > networkState.LastReceivedBaselineSeq) break;

			if (nextDelta.Sequence <= networkState.LastAppliedSnapshotSequence)
			{
				_queuedDeltaHead++;
				continue;
			}

			// Held descriptions are only usable in the order they were written: each one states the units it mentions and
			// mentions only what changed since the one before it. One applied across a hole describes a world the match
			// never had, so the queue waits for the head to be contiguous with what this screen applied.
			if (nextDelta.Sequence != networkState.LastAppliedSnapshotSequence + 1) break;

			_queuedDeltaHead++;
			networkState.LastAppliedSnapshotSequence = nextDelta.Sequence;
			GameContext.NetworkService.ApplySnapshotToWorld(nextDelta);
		}

		CompactQueuedDeltas();
	}

	private AppliedSequenceWindow _appliedSnapshots;
	private int _lastAcknowledgedSequence = -1;

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

	private void HoldForRepair(WorldSnapshot snapshot, int firstMissingSequence, int missingCount)
	{
		QueueDelta(snapshot);

		ulong now = Time.GetTicksMsec();
		if (_repairDeadlineMilliseconds == 0) _repairDeadlineMilliseconds = now + ResolveRepairGraceMilliseconds();

		if (_repairFirstSequence == firstMissingSequence)
		{
			if (now - _lastRepairRequestTime < RepairRequestIntervalMilliseconds) return;
		}
		else if (missingCount > MaximumRepairSequences)
		{
			RequestBaseline();
			return;
		}

		_repairFirstSequence = firstMissingSequence;
		_lastRepairRequestTime = now;
		GameContext.Host?.RequestSnapshotRepair(firstMissingSequence, missingCount);
	}

	private static ulong ResolveRepairGraceMilliseconds()
	{
		NetworkLatencyService? latency = GameContext.Host != null ? GameContext.NetworkLatencyService : null;
		if (latency == null || !latency.HasSample) return MinimumRepairGraceMilliseconds;

		float graceMilliseconds = MatchTiming.SnapshotRepairCooldownMilliseconds
		                          + latency.SmoothedRoundTripMilliseconds
		                          + MatchTiming.SnapshotIntervalSeconds * 1000f;
		return (ulong)Math.Clamp(graceMilliseconds, MinimumRepairGraceMilliseconds, MaximumRepairGraceMilliseconds);
	}

	private bool HasWaitedPastRepairGrace()
	{
		if (_repairDeadlineMilliseconds == 0) return false;
		if (Time.GetTicksMsec() < _repairDeadlineMilliseconds) return false;

		ForgetRepairRun();
		return true;
	}

	private void ForgetRepairRun()
	{
		_repairDeadlineMilliseconds = 0;
		_repairFirstSequence = -1;
	}

	private void QueueDelta(WorldSnapshot snapshot)
	{
		if (QueuedDeltaCount >= MaximumQueuedDeltas)
		{
			_queuedDeltaHead++;
			if (_queuedDeltaHead >= MaximumQueuedDeltas) CompactQueuedDeltas();
		}

		int insertIndex = _queuedDeltas.Count;
		while (insertIndex > _queuedDeltaHead && _queuedDeltas[insertIndex - 1].Sequence > snapshot.Sequence) insertIndex--;
		_queuedDeltas.Insert(insertIndex, snapshot);
	}

	public void Clear()
	{
		ClearQueuedDeltas();
		_appliedSnapshots.Reset();
		_lastAcknowledgedSequence = -1;
		_lastBaselineRequestTime = 0;
		_baselineRequestDeferred = false;
		_lastRepairRequestTime = 0;
		_lastDescriptionArrivalMilliseconds = 0;
		_lastSilenceRequestMilliseconds = 0;
		_silentWindows = 0;
		ForgetRepairRun();
	}
}
