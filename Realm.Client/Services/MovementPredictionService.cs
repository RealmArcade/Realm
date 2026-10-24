using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Services;
using Realm.Client.UI;

namespace Realm.Client.Services;

/// <summary>
/// Describes the locally predicted form of a movement order issued by the local player.
/// </summary>
public enum PredictedOrderKind
{
	None = 0,
	Move = 1,
	AttackMove = 2,
	Gather = 3,
	Build = 4,
	Stop = 5
}

public class MovementPredictionService
{
	public const float ActiveCorrectionDeadbandMeters = 2.0f;
	public const float SnapThresholdMeters = 4.0f;
	public const float CorrectionRatePerSecond = 14.0f;
	public const float SettledCorrectionRatePerSecond = 30.0f;
	public const float SettledErrorMeters = 0.05f;
	public const float AuthoritativeJumpSpeedMetersPerSecond = 25.0f;
	/// <summary>
	/// How far the authoritative order target may sit from the predicted one before the prediction is treated as stale.
	/// The authority scatters a group order across its formation (2.2 m spacing), so a smaller tolerance would discard
	/// every multi-unit prediction the moment its snapshot arrived. A scripted or overridden order moves units far
	/// beyond the formation envelope.
	/// </summary>
	public const float AuthoritativeOrderToleranceMeters = 4.0f;
	public const float ImpliedSnapshotIntervalSeconds = 1.0f / 30.0f;

	/// <summary>
	/// How many prediction ticks may pass before an unacknowledged order is rolled back. The authority normally answers
	/// within one one-way delay plus a tick, so three seconds only expires orders that were dropped, not merely delayed.
	/// </summary>
	public const int UnacknowledgedCommandTimeoutTicks = 90;

	private const int PendingCommandCapacity = 64;
	private const int MaximumCommandUnitCount = 32;
	private const int MaximumTrackedUnitCount = 512;
	private const int AllocationSampleIntervalTicks = 30;

	private readonly Vector3[] _authoritativePositions = new Vector3[EcsWorld.MAX_ENTITIES];
	private readonly Vector3[] _previousAuthoritativePositions = new Vector3[EcsWorld.MAX_ENTITIES];
	private readonly int[] _authoritativeSequences = new int[EcsWorld.MAX_ENTITIES];
	private readonly int[] _commandIds = new int[EcsWorld.MAX_ENTITIES];
	private readonly bool[] _tracked = new bool[EcsWorld.MAX_ENTITIES];
	private readonly bool[] _hasAuthoritativeSample = new bool[EcsWorld.MAX_ENTITIES];
	private readonly int[] _trackedEntityIds = new int[MaximumTrackedUnitCount];
	private readonly EntityCallback<Position, UnitOwnerPlayer> _localUnitQueryDelegate;
	private int _localUnitPlayerIndex = -1;

	private readonly int[] _pendingCommandIds = new int[PendingCommandCapacity];
	private readonly int[] _pendingCommandUnitCounts = new int[PendingCommandCapacity];
	private readonly int[] _pendingCommandRegisteredTicks = new int[PendingCommandCapacity];
	private readonly int[][] _pendingCommandUnits = new int[PendingCommandCapacity][];

	private int _trackedCount;
	private int _pendingCommandCursor;
	private int _reconcileSequence = -1;
	private int _lastSnapshotSequence = -1;
	private int _lastPredictedTick;
	private bool _isReconciling;

	private long _operationStartTimestamp;
	private float _lastOperationCostMs;
	private bool _hasPendingOperationCost;

	public MovementPredictionService()
	{
		for (int index = 0; index < PendingCommandCapacity; index++)
		{
			_pendingCommandUnits[index] = new int[MaximumCommandUnitCount];
		}

		_localUnitQueryDelegate = CollectLocalUnitAction;
	}

	public bool IsPredictiveFrameContext { get; private set; }
	public bool IsPredictionEnabled => GameSettings.MovementPrediction;
	public bool HasPredictedUnits => _trackedCount > 0;
	public int SnapshotOrderingViolations { get; private set; }
	public int ExpiredOrderCount { get; private set; }
	public long MaximumTickAllocatedBytes { get; private set; }
	public int TrackedUnitCount => _trackedCount;
	public int LastSnapshotSequence => _lastSnapshotSequence;
	public int LastPredictedTick => _lastPredictedTick;

	public ReadOnlySpan<int> TrackedEntityIds => new ReadOnlySpan<int>(_trackedEntityIds, 0, _trackedCount);

	public bool IsEntityTracked(int entityId)
	{
		if (entityId <= 0 || entityId >= EcsWorld.MAX_ENTITIES) return false;
		return _tracked[entityId];
	}

	public void ApplyPredictedOrder(EcsWorld world, PredictedOrderKind kind, ReadOnlySpan<int> localEntityIds, Vector3 targetPosition, bool isQueued)
	{
		if (!IsPredictionEnabled || world == null || kind == PredictedOrderKind.None) return;

		BeginOperation();
		IsPredictiveFrameContext = true;
		try
		{
			for (int index = 0; index < localEntityIds.Length; index++)
			{
				int entityId = localEntityIds[index];
				var entity = new Entity(entityId);
				if (!world.IsAlive(entity) || !world.Has<Position>(entity)) continue;

				MarkTracked(entityId);
				if (isQueued && HasActiveOrder(world, entity)) continue;
				ClearPredictedOrder(world, entity);

				if (kind == PredictedOrderKind.Stop) continue;

				world.SetOrAdd(entity, new PredictedMoveTo(targetPosition, (int)kind));
			}
		}
		finally
		{
			IsPredictiveFrameContext = false;
			EndOperation();
		}
	}

	public void GetAuthoritativeSample(int entityId, out Vector3 position, out int sequence)
	{
		if (entityId <= 0 || entityId >= EcsWorld.MAX_ENTITIES || !_hasAuthoritativeSample[entityId])
		{
			position = Vector3.Zero;
			sequence = -1;
			return;
		}

		position = _authoritativePositions[entityId];
		sequence = _authoritativeSequences[entityId];
	}

	public bool TryConsumeOperationCost(out float costMs)
	{
		costMs = _lastOperationCostMs;
		bool hasMeasurement = _hasPendingOperationCost;
		_hasPendingOperationCost = false;
		return hasMeasurement;
	}

	private void BeginOperation()
	{
		_operationStartTimestamp = Stopwatch.GetTimestamp();
	}

	private void EndOperation()
	{
		long elapsedTicks = Stopwatch.GetTimestamp() - _operationStartTimestamp;
		_lastOperationCostMs = (float)(elapsedTicks * 1000.0 / Stopwatch.Frequency);
		_hasPendingOperationCost = true;
	}

	public void RegisterPredictedCommand(int commandId, IReadOnlyList<int> localEntityIds)
	{
		if (!IsPredictionEnabled || localEntityIds == null || localEntityIds.Count == 0) return;

		int slot = _pendingCommandCursor;
		_pendingCommandCursor = (slot + 1) % PendingCommandCapacity;
		int storedCount = Math.Min(localEntityIds.Count, MaximumCommandUnitCount);
		_pendingCommandIds[slot] = commandId;
		_pendingCommandUnitCounts[slot] = storedCount;
		_pendingCommandRegisteredTicks[slot] = _lastPredictedTick;
		for (int index = 0; index < storedCount; index++)
		{
			int entityId = localEntityIds[index];
			_pendingCommandUnits[slot][index] = entityId;
			_commandIds[entityId] = commandId;
			MarkTracked(entityId);
		}

		PredictionDiagnostics.RecordCommandRegistered(commandId, localEntityIds);
	}

	public void ApplyLocalPrediction(EcsWorld world, IEnumerable<int> unitEntityIds, Vector3 targetPos)
	{
		if (!IsPredictionEnabled || world == null) return;

		BeginOperation();
		IsPredictiveFrameContext = true;
		try
		{
			foreach (int entityId in unitEntityIds)
			{
				var entity = new Entity(entityId);
				if (!world.IsAlive(entity)) continue;
				MarkTracked(entityId);
			}
		}
		finally
		{
			IsPredictiveFrameContext = false;
			EndOperation();
		}
	}

	public void Tick(EcsWorld world, float delta)
	{
		var host = GameContext.Host;
		if (!IsPredictionEnabled || world == null || host == null) return;
		if (host.ObservingPlayerIndex <= 0) return;

		BeginOperation();
		long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
		try
		{
			_lastPredictedTick++;
			TrackLocallyOwnedUnits(world);
			PruneUntrackedEntities(world);

			if (host.HasAuthority)
			{
				// The authority already integrated these units this tick, so prediction must not integrate them a second
				// time. It still records the authoritative sample so the invariant predicted == authoritative is visible.
				SampleAuthoritativeFromWorld(world);
			}
			else
			{
				AdoptAuthoritativeOrders(world);
				GameContext.SimulationService.StepPredictedMovement(delta);
				ApplyBoundedCorrections(world, delta);
			}

			ExpireUnacknowledgedCommands(world);

			long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
			if (allocatedBytes > MaximumTickAllocatedBytes) MaximumTickAllocatedBytes = allocatedBytes;
			if (_lastPredictedTick % AllocationSampleIntervalTicks == 0)
			{
				PredictionDiagnostics.RecordAllocation(_lastPredictedTick, allocatedBytes);
			}

			PredictionDiagnostics.RecordClientTick(world, _lastPredictedTick, this);
		}
		finally
		{
			EndOperation();
		}
	}

	private void ExpireUnacknowledgedCommands(EcsWorld world)
	{
		for (int slot = 0; slot < PendingCommandCapacity; slot++)
		{
			int commandId = _pendingCommandIds[slot];
			if (commandId <= 0) continue;
			if (_lastPredictedTick - _pendingCommandRegisteredTicks[slot] < UnacknowledgedCommandTimeoutTicks) continue;

			int unitCount = _pendingCommandUnitCounts[slot];
			for (int index = 0; index < unitCount; index++)
			{
				int entityId = _pendingCommandUnits[slot][index];
				var entity = new Entity(entityId);
				if (!world.IsAlive(entity)) continue;
				if (world.Has<UnitOwnerPlayer>(entity) && world.Get<UnitOwnerPlayer>(entity).PlayerIndex != _localUnitPlayerIndex) continue;

				ClearPredictedOrder(world, entity);
				_commandIds[entityId] = 0;
				ExpiredOrderCount++;
				PredictionDiagnostics.RecordRollback(commandId, entityId, "the authority never acknowledged the order", _lastSnapshotSequence);
			}

			_pendingCommandIds[slot] = 0;
			_pendingCommandUnitCounts[slot] = 0;
		}
	}

	private void SampleAuthoritativeFromWorld(EcsWorld world)
	{
		for (int index = 0; index < _trackedCount; index++)
		{
			int entityId = _trackedEntityIds[index];
			var entity = new Entity(entityId);
			if (!world.IsAlive(entity) || !world.Has<Position>(entity)) continue;

			Vector3 position = world.Get<Position>(entity).Value;
			_previousAuthoritativePositions[entityId] = _hasAuthoritativeSample[entityId] ? _authoritativePositions[entityId] : position;
			_authoritativePositions[entityId] = position;
			_authoritativeSequences[entityId] = 0;
			_hasAuthoritativeSample[entityId] = true;
		}
	}

	/// <summary>
	/// Drops a predicted order when the authority has published a different order for the same unit. Prediction is only
	/// allowed to lead the authority, never to outlive it: without this, a script or AI order that lands on a locally
	/// controlled unit stays invisible until the stale predicted order completes, which is exactly the permanent
	/// divergence prediction must not introduce.
	/// </summary>
	private void AdoptAuthoritativeOrders(EcsWorld world)
	{
		for (int index = 0; index < _trackedCount; index++)
		{
			int entityId = _trackedEntityIds[index];
			var entity = new Entity(entityId);
			if (!world.IsAlive(entity) || !world.Has<PredictedMoveTo>(entity) || !world.Has<MoveTo>(entity)) continue;

			Vector3 predictedTarget = world.Get<PredictedMoveTo>(entity).Target;
			Vector3 authoritativeTarget = world.Get<MoveTo>(entity).Target;
			if (Vector3.Distance(predictedTarget, authoritativeTarget) <= AuthoritativeOrderToleranceMeters) continue;

			ClearPredictedOrder(world, entity);
			_commandIds[entityId] = 0;
			PredictionDiagnostics.RecordRollback(0, entityId, "the authority published a different order", _lastSnapshotSequence);
		}
	}

	private void ApplyBoundedCorrections(EcsWorld world, float delta)
	{
		if (_trackedCount == 0) return;

		float movingFraction = Math.Min(1f, CorrectionRatePerSecond * delta);
		float settledFraction = Math.Min(1f, SettledCorrectionRatePerSecond * delta);
		for (int index = 0; index < _trackedCount; index++)
		{
			int entityId = _trackedEntityIds[index];
			var entity = new Entity(entityId);
			if (!world.IsAlive(entity) || !world.Has<Position>(entity)) continue;
			if (!_hasAuthoritativeSample[entityId]) continue;

			Vector3 predicted = world.Get<Position>(entity).Value;
			Vector3 authoritative = _authoritativePositions[entityId];
			Vector3 difference = predicted - authoritative;
			float errorSquared = difference.LengthSquared();
			if (!float.IsFinite(errorSquared)) continue;

			bool hasMovementOrder = world.Has<PredictedMoveTo>(entity);
			float deadband = hasMovementOrder ? ActiveCorrectionDeadbandMeters : SettledErrorMeters;
			if (errorSquared <= deadband * deadband) continue;

			Vector3 corrected = Vector3.Lerp(predicted, authoritative, hasMovementOrder ? movingFraction : settledFraction);
			world.Set(entity, new Position(corrected));
			PredictionDiagnostics.RecordCorrection(entityId, predicted, corrected, authoritative, _lastSnapshotSequence);
		}
	}

	private void TrackLocallyOwnedUnits(EcsWorld world)
	{
		_localUnitPlayerIndex = GameContext.Host?.ObservingPlayerIndex ?? -1;
		if (_localUnitPlayerIndex < 0) return;

		world.Query(in QueryCache.AllPositionAndUnitOwnerPlayerNoneDeadQuery, _localUnitQueryDelegate);
	}

	private void CollectLocalUnitAction(Entity entity, ref Position position, ref UnitOwnerPlayer owner)
	{
		if (owner.PlayerIndex != _localUnitPlayerIndex) return;
		if (_tracked[entity.Id]) return;
		MarkTracked(entity.Id);
	}

	private void PruneUntrackedEntities(EcsWorld world)
	{
		int localPlayerIndex = _localUnitPlayerIndex;
		int kept = 0;
		for (int index = 0; index < _trackedCount; index++)
		{
			int entityId = _trackedEntityIds[index];
			var entity = new Entity(entityId);
			if (!world.IsAlive(entity))
			{
				_tracked[entityId] = false;
				continue;
			}

			bool isLocalOwned = localPlayerIndex >= 0
				&& world.Has<UnitOwnerPlayer>(entity)
				&& world.Get<UnitOwnerPlayer>(entity).PlayerIndex == localPlayerIndex;
			if (!isLocalOwned)
			{
				_tracked[entityId] = false;
				PredictionDiagnostics.RecordPredictionStopped(entityId, "ownership changed or unit is not locally controlled");
				continue;
			}

			_trackedEntityIds[kept++] = entityId;
		}

		_trackedCount = kept;
	}

	private void MarkTracked(int entityId)
	{
		if (entityId <= 0 || entityId >= EcsWorld.MAX_ENTITIES) return;
		if (_tracked[entityId]) return;
		if (_trackedCount >= MaximumTrackedUnitCount) return;

		_tracked[entityId] = true;
		_trackedEntityIds[_trackedCount++] = entityId;
	}

	public void BeginSnapshotReconcile(int sequence)
	{
		if (!IsPredictionEnabled) return;

		if (sequence <= _lastSnapshotSequence)
		{
			SnapshotOrderingViolations++;
			PredictionDiagnostics.RecordSnapshotOrderingViolation(sequence, _lastSnapshotSequence);
			return;
		}

		_lastSnapshotSequence = sequence;
		_reconcileSequence = sequence;
		_isReconciling = true;
	}

	public void ReconcileAuthoritativePosition(EcsWorld world, int entityId, Vector3 authoritativePosition)
	{
		if (!IsPredictionEnabled || world == null || !_isReconciling) return;
		if (entityId <= 0 || entityId >= EcsWorld.MAX_ENTITIES) return;

		var entity = new Entity(entityId);
		if (!world.IsAlive(entity)) return;

		bool hasPrevious = _hasAuthoritativeSample[entityId];
		Vector3 previousAuthoritative = _previousAuthoritativePositions[entityId];

		_previousAuthoritativePositions[entityId] = authoritativePosition;
		_authoritativePositions[entityId] = authoritativePosition;
		_authoritativeSequences[entityId] = _reconcileSequence;
		_hasAuthoritativeSample[entityId] = true;

		if (!_tracked[entityId]) return;
		if (!world.Has<Position>(entity)) return;

		Vector3 predicted = world.Get<Position>(entity).Value;
		Vector3 difference = predicted - authoritativePosition;
		float errorSquared = difference.LengthSquared();
		if (!float.IsFinite(errorSquared))
		{
			world.Set(entity, new Position(authoritativePosition));
			PredictionDiagnostics.RecordSnap(entityId, _reconcileSequence, predicted, authoritativePosition, false, "non-finite predicted position");
			return;
		}

		if (errorSquared <= SnapThresholdMeters * SnapThresholdMeters) return;

		float impliedSpeed = hasPrevious
			? Vector3.Distance(authoritativePosition, previousAuthoritative) / MathF.Max(ImpliedSnapshotIntervalSeconds, 0.0001f)
			: float.PositiveInfinity;
		bool explained = hasPrevious && impliedSpeed > AuthoritativeJumpSpeedMetersPerSecond;

		if (explained)
		{
			world.Set(entity, new Position(authoritativePosition));
			PredictionDiagnostics.RecordSnap(entityId, _reconcileSequence, predicted, authoritativePosition, true, $"authoritative teleport at {impliedSpeed:F1} m/s");
			return;
		}

		world.Set(entity, new Position(authoritativePosition));
		PredictionDiagnostics.RecordSnap(entityId, _reconcileSequence, predicted, authoritativePosition, false, "prediction error above snap threshold");
	}

	public void EndSnapshotReconcile()
	{
		_isReconciling = false;
	}

	public void RollbackRejectedCommand(EcsWorld world, int commandId, int[] rejectedServerEntityIds, string reason)
	{
		if (!IsPredictionEnabled || world == null) return;

		int slot = -1;
		for (int index = 0; index < PendingCommandCapacity; index++)
		{
			if (_pendingCommandIds[index] != commandId) continue;
			slot = index;
			break;
		}

		if (slot < 0) return;

		var networkService = GameContext.NetworkService;
		int unitCount = _pendingCommandUnitCounts[slot];
		int clearedCount = 0;
		for (int index = 0; index < unitCount; index++)
		{
			int entityId = _pendingCommandUnits[slot][index];
			var entity = new Entity(entityId);
			if (!world.IsAlive(entity)) continue;

			int serverEntityId = networkService?.GetServerEntityId(entityId) ?? -1;
			bool rejected = rejectedServerEntityIds.Length == 0 || serverEntityId < 0;
			for (int rejectedIndex = 0; rejectedIndex < rejectedServerEntityIds.Length; rejectedIndex++)
			{
				if (rejectedServerEntityIds[rejectedIndex] != serverEntityId) continue;
				rejected = true;
				break;
			}

			if (!rejected) continue;

			ClearPredictedOrder(world, entity);
			_commandIds[entityId] = 0;
			PredictionDiagnostics.RecordRollback(commandId, entityId, reason, _lastSnapshotSequence);
			clearedCount++;
		}

		_pendingCommandIds[slot] = 0;
		_pendingCommandUnitCounts[slot] = 0;
	}

	public void OnCommandAccepted(int commandId)
	{
		for (int index = 0; index < PendingCommandCapacity; index++)
		{
			if (_pendingCommandIds[index] != commandId) continue;
			_pendingCommandIds[index] = 0;
			_pendingCommandUnitCounts[index] = 0;
		}

		PredictionDiagnostics.RecordCommandAccepted(commandId);
	}

	public bool HasPredictedOrder(EcsWorld world, Entity entity)
	{
		return world != null && world.Has<PredictedMoveTo>(entity);
	}

	private static bool HasActiveOrder(EcsWorld world, Entity entity)
	{
		return world.Has<PredictedMoveTo>(entity)
			|| world.Has<MoveTo>(entity)
			|| world.Has<AttackMove>(entity)
			|| world.Has<Follow>(entity)
			|| world.Has<Gatherer>(entity)
			|| world.Has<Patrol>(entity)
			|| world.Has<Realm.Ecs.Components.Resources.BuildTask>(entity);
	}

	private static void ClearPredictedOrder(EcsWorld world, Entity entity)
	{
		if (world.Has<PredictedMoveTo>(entity)) world.Remove<PredictedMoveTo>(entity);
		if (world.Has<PathFollow>(entity)) world.Remove<PathFollow>(entity);
	}

	public int ResolveCommandId(int entityId)
	{
		if (entityId <= 0 || entityId >= EcsWorld.MAX_ENTITIES) return 0;
		return _commandIds[entityId];
	}

	public void Clear()
	{
		_trackedCount = 0;
		_pendingCommandCursor = 0;
		_pendingCommandRegisteredTicks.AsSpan().Clear();
		SnapshotOrderingViolations = 0;
		ExpiredOrderCount = 0;
		MaximumTickAllocatedBytes = 0;
		_reconcileSequence = -1;
		_lastSnapshotSequence = -1;
		_lastPredictedTick = 0;
		_isReconciling = false;
		IsPredictiveFrameContext = false;
		Array.Clear(_tracked);
		Array.Clear(_hasAuthoritativeSample);
		Array.Clear(_commandIds);
		Array.Clear(_pendingCommandIds);
		Array.Clear(_pendingCommandUnitCounts);
	}
}
