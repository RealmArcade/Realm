using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using MemoryPack;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Testing;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Shared.Diagnostics;
using Realm.Shared.Network;

namespace Realm.Client.Services;

public class SnapshotApplyService
{
	private const int MaximumQueuedDeltas = 512;

	/// <summary>
	///     How far behind the newest description this screen may fall before it rebuilds instead of catching up. It is the
	///     run the match still holds, so every hole this refuses to wait out is one the match could not have answered by
	///     name either: a narrower bound rebuilt the world for runs a handful of descriptions would have filled, and a
	///     wider one applied backlogs far deeper than any run the match could still answer by name.
	/// </summary>
	private const int MaximumBacklogSequences = MatchTiming.SnapshotHistoryDepth;

	/// <summary>
	///     The widest run of descriptions this screen asks the match to send again, which is exactly the run the match
	///     still holds. A wider run is a stall rather than a datagram that went missing, and a baseline is the only thing
	///     that puts a screen that far behind back. Asking for less than the match holds meant a hole the match could
	///     have answered with a dozen deltas was paid for with a whole world on the reliable stream.
	/// </summary>
	private const int MaximumRepairSequences = MatchTiming.SnapshotHistoryDepth;

	/// <summary>
	///     How often the same hole may be asked about again while its answer is still owed.
	/// </summary>
	private const int RepairRequestIntervalMilliseconds = 120;

	/// <summary>
	///     The least and the most time a description that arrived ahead of a missing one is held before the screen gives up
	///     on the run and asks for the world instead. The grace has to cover the repair's whole journey - the request out,
	///     the cooldown the authority keeps between repairs, and the description back - so it is taken from the link rather
	///     than fixed: a run held for less than its repair takes is a run thrown away and paid for with a baseline.
	/// </summary>
	private const int MinimumRepairGraceMilliseconds = 250;

	private const int MaximumRepairGraceMilliseconds = 2400;

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

	private const int MaximumTraceEntries = 4096;

	private readonly List<WorldSnapshot> _queuedDeltas = new();

	private int _queuedDeltaHead;

	private ulong _lastBaselineRequestTime;

	private bool _baselineRequestDeferred;
	private LagEvent? _deferredBaselineReason;

	private ulong _repairDeadlineMilliseconds;
	private ulong _repairHeldSinceMilliseconds;
	private int _repairFirstSequence = -1;
	private int _repairRequestedCount;
	private ulong _lastRepairRequestTime;

	private ulong _lastDescriptionArrivalMilliseconds;
	private long _previousDescriptionTimestamp;
	private ulong _lastSilenceRequestMilliseconds;
	private int _silentWindows;

	private readonly RepairTraceEntry[] _traceEntries = new RepairTraceEntry[MaximumTraceEntries];
	private int _traceCount;
	private ulong _traceStartMilliseconds;

	private enum RepairTraceKind : byte
	{
		Arrived,
		Behind,
		BaseAhead,
		BacklogTooDeep,
		Held,
		RepairRequested,
		RepairSuppressed,
		GraceExpired,
		Cleared,
		BaselineRequested,
		BaselineDeferred,
		BaselineApplied,
		Applied
	}

	private struct RepairTraceEntry
	{
		public ulong Milliseconds;
		public int Sequence;
		public int Detail;
		public RepairTraceKind Kind;
	}

	public void Accept(WorldSnapshot snapshot)
	{
		Trace(RepairTraceKind.Arrived, snapshot.Sequence, snapshot.IsBaseline ? snapshot.BaseSequence : 0);
		// A description that arrived, whatever this screen goes on to make of it, is the answer to a watch that asked
		// about a stream that had stopped.
		_lastDescriptionArrivalMilliseconds = Time.GetTicksMsec();
		_silentWindows = 0;

		if (LagProbe.IsEnabled)
		{
			LagProbe.Count(LagEvent.ClientDescriptionsArrived);
			long arrivalTimestamp = LagProbe.Timestamp();
			if (_previousDescriptionTimestamp != 0L) LagProbe.Add(LagStage.ClientDescriptionIntervalMilliseconds, LagProbe.ElapsedMilliseconds(_previousDescriptionTimestamp));
			_previousDescriptionTimestamp = arrivalTimestamp;
		}

		if (_baselineRequestDeferred) RequestBaseline(_deferredBaselineReason ?? LagEvent.ClientBaselinesBecauseDeferredRetry);

		Entity worldEntity = GameContext.NetworkService.FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkState>(worldEntity))
		{
			RequestBaseline(LagEvent.ClientBaselinesBecauseNoWorld);
			return;
		}
		GameContext.World.Get<NetworkState>(worldEntity).LastSnapshotReceivedTime = Time.GetTicksMsec();
		ProcessSnapshot(snapshot);

		Entity currentWorldEntity = GameContext.NetworkService.FindWorldEntity();
		if (currentWorldEntity == Entity.Null || !GameContext.World.Has<NetworkState>(currentWorldEntity)) return;
		if (!GameContext.World.Get<NetworkState>(currentWorldEntity).HasReceivedInitialBaseline) RequestBaseline(LagEvent.ClientBaselinesBecauseNoInitialBaseline);
	}



	public void RequestBaseline() => RequestBaseline(LagEvent.ClientBaselinesBecauseSessionAsked);

	private void RequestBaseline(LagEvent reason)
	{
		ulong now = Time.GetTicksMsec();
		// An ask the cooldown refused is delivered on the next description rather than dropped: a repair refused a moment
		// ago is still owed one now, and dropping it left nothing in flight and nothing asking again. The reason travels
		// with it, so the run is counted once, against the path that needed it, instead of once per description that
		// arrived while it waited.
		if (now - _lastBaselineRequestTime < (ulong)MatchTiming.BaselineRequestCooldownMilliseconds)
		{
			Trace(RepairTraceKind.BaselineDeferred, 0, (int)reason);
			_baselineRequestDeferred = true;
			_deferredBaselineReason = reason;
			return;
		}

		Trace(RepairTraceKind.BaselineRequested, 0, (int)reason);

		_lastBaselineRequestTime = now;
		_baselineRequestDeferred = false;
		_deferredBaselineReason = null;
		LagProbe.Count(reason);
		LagProbe.Count(LagEvent.ClientBaselinesRequested);
		GameContext.Host?.RequestSnapshotBaseline();
	}

	private void ProcessSnapshot(WorldSnapshot snapshot)
	{
		if (MultiplayerSyncDiagnostics.IsRecording) MultiplayerSyncDiagnostics.RecordClientSeenSequence(snapshot.Sequence);
		Entity worldEntity = GameContext.NetworkService.FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkState>(worldEntity)) return;

		ref var networkState = ref GameContext.World.Get<NetworkState>(worldEntity);

		// Descriptions held behind a run that never arrived are a world this screen cannot reach by applying them, so the
		// world itself is asked for. What is held is kept: the run asked for is still in flight, and a repair that lands a
		// frame after this abandoned it left the screen holding descriptions it had just thrown away - the hole it then
		// reported was one it had been sent. The baseline supersedes them when it arrives, and the run fills them when it
		// does, so nothing is lost by keeping them and a whole world can be saved by it.
		if (QueuedDeltaCount > 0 && HasWaitedPastRepairGrace())
		{
			Trace(RepairTraceKind.GraceExpired, snapshot.Sequence, QueuedDeltaCount);
			LagProbe.Count(LagEvent.ClientRepairRunsAbandoned);
			RequestBaseline(LagEvent.ClientBaselinesBecauseRepairGraceExpired);
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
			Trace(RepairTraceKind.BaselineApplied, snapshot.Sequence, 0);
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
			LagProbe.Count(LagEvent.ClientDescriptionsBehindApplied);
			Trace(RepairTraceKind.Behind, snapshot.Sequence, networkState.LastAppliedSnapshotSequence);
			return;
		}

		int sequencesSkipped = snapshot.Sequence - networkState.LastAppliedSnapshotSequence - 1;

		// A screen that has not been told the world yet has not missed anything: the match was describing a world this
		// screen had not joined, and counting that span as a gap reported a lossless link as having lost a hundred
		// descriptions before the first one it could apply.
		if (sequencesSkipped > 0 && networkState.HasReceivedInitialBaseline && networkState.LastAppliedSnapshotSequence > 0)
		{
			LagProbe.Count(LagEvent.ClientSequenceGaps);
			LagProbe.RecordWorst(LagEvent.ClientWorstSkippedSequences, sequencesSkipped);
		}

		// A queue deeper than the buffer is a screen that stopped reading, not a link that is merely late: applying it in
		// one frame costs more the deeper it is, whereas a baseline is a few kilobytes and puts the screen where the
		// authority is. The queued descriptions are the tail of the same backlog, so they go with it.
		//
		// A screen with no baseline yet has not fallen behind anything: the match describes worlds it has not been told
		// about, and every one of those used to be thrown away here as a backlog that was too deep, cutting a hole out of
		// a link that had lost nothing. Those descriptions are held until the baseline that states their base arrives.
		if (sequencesSkipped >= MaximumBacklogSequences && networkState.HasReceivedInitialBaseline)
		{
			Trace(RepairTraceKind.BacklogTooDeep, snapshot.Sequence, sequencesSkipped);
			ClearQueuedDeltas();
			ForgetRepairRun();
			RequestBaseline(LagEvent.ClientBaselinesBecauseBacklogTooDeep);
			return;
		}

		// A delta names the oldest world state it assumes, not the newest baseline the host has started sending. An exact
		// match was required here once, so every delta built on a baseline still in flight was queued until it arrived -
		// with a 138 KB baseline on the reliable channel that stalled the client once per baseline interval. A delta is
		// absolute per unit, so a base the client has already superseded is harmless.
		if (!networkState.HasReceivedInitialBaseline || snapshot.BaseSequence > networkState.LastReceivedBaselineSeq)
		{
			Trace(RepairTraceKind.BaseAhead, snapshot.Sequence, snapshot.BaseSequence);
			QueueDelta(snapshot);
			RequestBaseline(LagEvent.ClientBaselinesBecauseBaseAhead);
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
		Trace(RepairTraceKind.Applied, snapshot.Sequence, QueuedDeltaCount);

		// A run that has started to be answered is no longer a run this screen is waiting for: from here a hole that is
		// still missing is a new loss, and the rule that treats a hole inside an asked-for run as the same repair already
		// owed would suppress asking for it. That suppression is what kept a screen that had just been handed the first
		// half of its run waiting out the second half.
		if (_repairFirstSequence >= 0 && snapshot.Sequence >= _repairFirstSequence) ForgetRepairRun();

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
			RequestBaseline(LagEvent.ClientBaselinesBecauseSilence);
			return;
		}

		// The description this screen is owed next is the one the match published after the newest it holds. A run the
		// match no longer holds is answered by a baseline of its own, so this asks by name first and only rebuilds when
		// naming it has not worked.
		LagProbe.Count(LagEvent.ClientRepairsRequested);
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

	/// <summary>
	///     How many descriptions this screen already holds but has not applied, because a run below them is missing. It is
	///     what tells a stall caused by the link apart from one caused by this process: descriptions held here were
	///     delivered, so the link had nothing left to say.
	/// </summary>
	public int HeldDescriptionCount => QueuedDeltaCount;

	private int OldestQueuedDeltaSequence() => QueuedDeltaCount > 0 ? _queuedDeltas[_queuedDeltaHead].Sequence : -1;

	private void ClearQueuedDeltas()
	{
		if (QueuedDeltaCount > 0) Trace(RepairTraceKind.Cleared, OldestQueuedDeltaSequence(), QueuedDeltaCount);
		if (LagProbe.IsEnabled && QueuedDeltaCount > 0) LagProbe.Count(LagEvent.ClientQueuedDescriptionsCleared, QueuedDeltaCount);

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
			RequestBaseline(LagEvent.ClientBaselinesBecauseQueuedRunOverflow);
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
			LagProbe.Count(LagEvent.ClientDeltasAppliedFromBacklog);
			GameContext.NetworkService.ApplySnapshotToWorld(nextDelta);
		}

		CompactQueuedDeltas();
	}

	private AppliedSequenceWindow _appliedSnapshots;
	private int _lastAcknowledgedSequence = -1;

	private const int AcknowledgementIntervalSequences = 2;

	/// <summary>
	///     States that a description arrived, and how far this screen has got. A description counts as present once it has
	///     been applied, whatever its components: a unit this screen has not built yet is content it is still owed, and
	///     reporting the description as absent answered a spawn backlog by asking the match for the world again.
	/// </summary>
	public void Acknowledge(int sequence)
	{
		if (GameContext.Host == null) return;

		// The one absence this screen reports on purpose: a recording harness discarding a description it did apply, so
		// the repair path is exercised through a hole that is known rather than one that is hoped for.
		bool reportMissing = MultiplayerSyncDiagnostics.ConsumeSnapshotDiscardRequest();
		if (reportMissing) MultiplayerSyncDiagnostics.RecordDiscardedSnapshotSequence(sequence);

		if (reportMissing) _appliedSnapshots.Advance(sequence);
		else _appliedSnapshots.MarkApplied(sequence);
		if (_appliedSnapshots.Highest < 0) return;

		int highest = _appliedSnapshots.Highest;
		if (!reportMissing && highest - _lastAcknowledgedSequence < AcknowledgementIntervalSequences) return;

		_lastAcknowledgedSequence = highest;
		GameContext.Host.SendSnapshotAck(highest, _appliedSnapshots.BuildMask());
	}

	private void HoldForRepair(WorldSnapshot snapshot, int firstMissingSequence, int missingCount)
	{
		QueueDelta(snapshot);
		Trace(RepairTraceKind.Held, firstMissingSequence, missingCount);

		// The hole ends where the oldest description this screen already holds begins. Everything between them is waiting
		// to be applied, and asking for it again bought a duplicate of what it had, a resend as wide as the whole backlog
		// behind the hole, and another round trip before the run could be drawn. A backlog of nineteen with one description
		// missing at its head is a hole of one.
		int oldestHeldSequence = OldestQueuedDeltaSequence();
		if (oldestHeldSequence > 0 && oldestHeldSequence - firstMissingSequence < missingCount)
		{
			missingCount = Math.Max(1, oldestHeldSequence - firstMissingSequence);
		}

		ulong now = Time.GetTicksMsec();

		if (missingCount > MaximumRepairSequences && _repairFirstSequence != firstMissingSequence)
		{
			Trace(RepairTraceKind.RepairSuppressed, firstMissingSequence, missingCount);
			RequestBaseline(LagEvent.ClientBaselinesBecauseRepairRunTooWide);
			return;
		}

		// A hole that starts inside a run this screen has already asked for is that same repair still owed: the answer in
		// flight fills it, and asking again adds a request to a stream that is already losing them. This is the whole of
		// the difference between recovery traffic that scales with the loss rate and one that feeds itself - an answer that
		// arrives in part leaves a hole starting inside the run that was asked for, and every one of those used to be a
		// fresh request. A hole outside that run is a new loss and is asked about at once, because it is a buffer running dry
		// while a request waits that has nothing in it for that hole.
		if (_repairFirstSequence >= 0
		    && firstMissingSequence > _repairFirstSequence
		    && firstMissingSequence < _repairFirstSequence + _repairRequestedCount
		    && now < _repairDeadlineMilliseconds)
		{
			Trace(RepairTraceKind.RepairSuppressed, firstMissingSequence, missingCount);
			return;
		}

		if (_repairFirstSequence == firstMissingSequence && now - _lastRepairRequestTime < (ulong)RepairRequestIntervalMilliseconds)
		{
			Trace(RepairTraceKind.RepairSuppressed, firstMissingSequence, missingCount);
			return;
		}

		_repairFirstSequence = firstMissingSequence;
		_repairRequestedCount = missingCount;
		_lastRepairRequestTime = now;

		// The grace follows the last request rather than the first one, because it has to cover the journey of the answer
		// that is actually in flight: a retry sent a hundred milliseconds after the hold began used to inherit a deadline
		// set before it was asked for, so the answer it earned was abandoned the moment it arrived. It is capped from the
		// start of the hold so a link that never answers still falls back to the world instead of asking forever.
		if (_repairHeldSinceMilliseconds == 0) _repairHeldSinceMilliseconds = now;
		ulong journeyDeadline = now + ResolveRepairGraceMilliseconds();
		ulong holdDeadline = _repairHeldSinceMilliseconds + (ulong)MaximumRepairGraceMilliseconds;
		_repairDeadlineMilliseconds = Math.Min(journeyDeadline, holdDeadline);
		Trace(RepairTraceKind.RepairRequested, firstMissingSequence, missingCount);
		LagProbe.Count(LagEvent.ClientRepairsRequested);
		LagProbe.Count(LagEvent.ClientDeltasHeldForRepair, QueuedDeltaCount);
		GameContext.Host?.RequestSnapshotRepair(firstMissingSequence, missingCount);
	}

	private static ulong ResolveRepairGraceMilliseconds()
	{
		NetworkLatencyService? latency = GameContext.Host != null ? GameContext.NetworkLatencyService : null;
		if (latency == null || !latency.HasSample) return MinimumRepairGraceMilliseconds;

		// The grace has to cover the request crossing to the match, the cooldown the answer may wait out before it is
		// sent, the answer crossing back, and one retransmit of that answer: a repair asked for once over a link that
		// loses datagrams is a repair whose answer can itself be lost, and giving up on the world before that round
		// trip has had its chance is how one lost description used to cost a whole baseline.
		float graceMilliseconds = MatchTiming.SnapshotRepairCooldownMilliseconds
		                          + latency.SmoothedRoundTripMilliseconds * 2f
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
		_repairHeldSinceMilliseconds = 0;
		_repairFirstSequence = -1;
		_repairRequestedCount = 0;
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

	private void Trace(RepairTraceKind kind, int sequence, int detail)
	{
		if (!MultiplayerSyncDiagnostics.IsRecording) return;
		if (_traceCount >= _traceEntries.Length) return;

		ulong now = Time.GetTicksMsec();
		if (_traceStartMilliseconds == 0) _traceStartMilliseconds = now;
		_traceEntries[_traceCount++] = new RepairTraceEntry
		{
			Milliseconds = now - _traceStartMilliseconds,
			Sequence = sequence,
			Detail = detail,
			Kind = kind
		};
	}

	public void WriteRepairTrace(TextWriter writer)
	{
		writer.WriteLine("milliseconds kind sequence detail");
		for (int index = 0; index < _traceCount; index++)
		{
			RepairTraceEntry entry = _traceEntries[index];
			writer.WriteLine(FormattableString.Invariant($"{entry.Milliseconds} {entry.Kind} {entry.Sequence} {entry.Detail}"));
		}
	}

	public void Clear()
	{
		_traceCount = 0;
		_traceStartMilliseconds = 0;
		ClearQueuedDeltas();
		_appliedSnapshots.Reset();
		_lastAcknowledgedSequence = -1;
		_lastBaselineRequestTime = 0;
		_baselineRequestDeferred = false;
		_deferredBaselineReason = null;
		_lastRepairRequestTime = 0;
		_lastDescriptionArrivalMilliseconds = 0;
		_previousDescriptionTimestamp = 0L;
		_lastSilenceRequestMilliseconds = 0;
		_silentWindows = 0;
		ForgetRepairRun();
	}
}
