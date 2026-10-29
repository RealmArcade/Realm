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

	private readonly List<WorldSnapshot> _queuedDeltas = new();

	private ulong _lastBaselineRequestTime;

	private bool _baselineRequestDeferred;

	public void Accept(byte[] payload) => Accept(MemoryPackSerializer.Deserialize<WorldSnapshot>(SnapshotWireFormat.Decode(payload)));

	public void Accept(WorldSnapshot snapshot)
	{
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

		if (snapshot.IsBaseline)
		{
			if (snapshot.Sequence <= networkState.LastReceivedBaselineSeq) return;

			if (snapshot.Sequence <= networkState.LastAppliedSnapshotSequence)
			{
				networkState.LastReceivedBaselineSeq = snapshot.Sequence;
				DrainQueuedDeltas(ref networkState);
				return;
			}

			if (!networkState.HasReceivedInitialBaseline) GameContext.SnapshotTimeline.Clear();

			networkState.LastReceivedBaselineSeq = snapshot.Sequence;
			networkState.HasReceivedInitialBaseline = true;
			networkState.LastAppliedSnapshotSequence = snapshot.Sequence;
			_queuedDeltas.RemoveAll(d => d.Sequence <= snapshot.Sequence);
			_appliedSnapshots.Reset();
			GameContext.NetworkService.ApplySnapshotToWorld(snapshot);

			DrainQueuedDeltas(ref networkState);
			return;
		}

		if (snapshot.Sequence <= networkState.LastAppliedSnapshotSequence) return;

		int sequencesSkipped = snapshot.Sequence - networkState.LastAppliedSnapshotSequence - 1;

		if (sequencesSkipped >= MaximumBacklogSequences)
		{
			_queuedDeltas.Clear();
			RequestBaseline();
			return;
		}

		if (!networkState.HasReceivedInitialBaseline || snapshot.BaseSequence > networkState.LastReceivedBaselineSeq)
		{
			QueueDelta(snapshot);
			RequestBaseline();
			return;
		}

		networkState.LastAppliedSnapshotSequence = snapshot.Sequence;
		GameContext.NetworkService.ApplySnapshotToWorld(snapshot);
		DrainQueuedDeltas(ref networkState);

		if (sequencesSkipped > 1) RequestBaseline();
	}

	private void DrainQueuedDeltas(ref NetworkState networkState)
	{
		if (_queuedDeltas.Count > 0
		    && _queuedDeltas[^1].Sequence - networkState.LastAppliedSnapshotSequence > MaximumBacklogSequences)
		{
			_queuedDeltas.Clear();
			RequestBaseline();
			return;
		}

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

	public void Clear()
	{
		_queuedDeltas.Clear();
		_appliedSnapshots.Reset();
		_lastAcknowledgedSequence = -1;
		_lastBaselineRequestTime = 0;
		_baselineRequestDeferred = false;
	}
}
