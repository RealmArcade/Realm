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
	/// <summary>
	/// The speed above which an authoritative sample is a grouping or script event rather than locomotion. It has to sit
	/// below the speed a snap can imply: a snap that is not already explained by an outage happens inside
	/// <see cref="SnapshotGapToleranceTicks"/> ticks, so a <see cref="SnapThresholdMeters"/> difference there is at least
	/// 20 m/s. The previous 25 m/s bound was therefore unreachable and every authoritative jump of four to five metres
	/// was reported without a cause, even though no unit in the game approaches 12 m/s.
	/// </summary>
	public const float AuthoritativeJumpSpeedMetersPerSecond = 12.0f;
	/// <summary>
	/// How far the authoritative order target may sit from the predicted one before the prediction is treated as stale.
	/// The authority scatters a group order across its formation (2.2 m spacing), so a smaller tolerance would discard
	/// every multi-unit prediction the moment its snapshot arrived. A scripted or overridden order moves units far
	/// beyond the formation envelope.
	/// </summary>
	public const float AuthoritativeOrderToleranceMeters = 4.0f;
	/// <summary>
	/// How many ticks may pass between two authoritative samples before a snap is attributed to the outage instead of to
	/// prediction. A healthy stream delivers a sample every tick, and the injected one-way delay makes a sample about
	/// 4.5 ticks old at the client, so any gap above this means at least one snapshot never arrived and the client was
	/// reconciling against a stale position for the whole gap.
	/// </summary>
	public const int SnapshotGapToleranceTicks = 6;
	public const float ImpliedSnapshotIntervalSeconds = 1.0f / 30.0f;

	/// <summary>
	/// How many prediction ticks may pass before an unacknowledged order is rolled back. The authority normally answers
	/// within one one-way delay plus a tick, so three seconds only expires orders that were dropped, not merely delayed.
	/// </summary>
	public const int UnacknowledgedCommandTimeoutTicks = 90;
	/// <summary>
	/// How many ticks an unacknowledged order may predict before drift alone can expire it. One one-way delay plus the
	/// acknowledgement is about five ticks, so this only fires for an order the authority never received at all.
	/// </summary>
	public const int UnacknowledgedCommandMinimumTicks = 20;
	/// <summary>
	/// How far an unacknowledged order may lead the latest authoritative sample. It is half the snap threshold so the
	/// order is dropped a full snapshot interval before the lead would force a snap, which is what keeps a lost command
	/// from being reported as an unexplained snap.
	/// </summary>
	public const float UnacknowledgedCommandDriftLimitMeters = 2.0f;
	/// <summary>
	/// The furthest a predicted order may ever lead the latest authoritative sample. It sits above the authority's 2.2 m
	/// group formation scatter, so a normal group order is not clipped, and below the snap threshold, so dropping the
	/// order always leaves the difference small enough for a bounded correction instead of a snap. This clamp does not
	/// depend on command bookkeeping, so an order whose acknowledgement or expiry record was lost still cannot walk the
	/// unit away from the authority.
	/// </summary>
	public const float PredictionLeadLimitMeters = 3.0f;
	/// <summary>
	/// The furthest a predicted unit may move in a single tick. Prediction runs the shared movement step, which performs
	/// off-mesh recovery and waypoint repairs that legitimately teleport the authority's unit between two of its ticks;
	/// on the client the same repair would move a unit several metres in one tick and the next authoritative sample would
	/// have to snap it back. Any displacement above this bound is reverted and the predicted order is dropped.
	/// </summary>
	public const float MaximumPredictionStepMeters = 1.0f;

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
	private readonly Vector3[] _stepStartPositions = new Vector3[EcsWorld.MAX_ENTITIES];
	private readonly bool[] _hasStepStartPosition = new bool[EcsWorld.MAX_ENTITIES];
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
	private int _previousAppliedSequence = -1;
	private int _lastSnapshotSequence = -1;
	private int _lastReconcileTick = -1;
	private int _reconcileOutageTicks;
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
	public long MaximumPredictionTickAllocatedBytes { get; private set; }
	public long MaximumSharedMovementTickAllocatedBytes { get; private set; }
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

				// A build order is not movement: the authority picks the worker and arbitrates the slot, so predicting a
				// walk to the placement point would invent a destination the authority never ordered. The order is still
				// registered for acknowledgement and rollback; only the movement prediction is withheld.
				if (kind == PredictedOrderKind.Build) continue;

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
		try
		{
			_lastPredictedTick++;
			TrackLocallyOwnedUnits(world);
			PruneUntrackedEntities(world);

			long predictionWorkBefore = GC.GetAllocatedBytesForCurrentThread();
			long sharedMovementBytes = 0;

			if (host.HasAuthority)
			{
				// The authority already integrated these units this tick, so prediction must not integrate them a second
				// time. It still records the authoritative sample so the invariant predicted == authoritative is visible.
				SampleAuthoritativeFromWorld(world);
			}
			else
			{
				AdoptAuthoritativeOrders(world);
				CaptureStepStartPositions(world);

				// The shared movement step is the same code the authority runs, so its allocations are reported apart
				// from prediction's own: only the latter is this service's to keep allocation free.
				long movementBefore = GC.GetAllocatedBytesForCurrentThread();
				GameContext.SimulationService.StepPredictedMovement(delta);
				sharedMovementBytes = GC.GetAllocatedBytesForCurrentThread() - movementBefore;

				RevertImplausibleSteps(world);
				ApplyBoundedCorrections(world, delta);
				ClampPredictionLead(world);
			}

			ExpireUnacknowledgedCommands(world);

			long predictionWorkBytes = GC.GetAllocatedBytesForCurrentThread() - predictionWorkBefore - sharedMovementBytes;
			if (sharedMovementBytes > MaximumSharedMovementTickAllocatedBytes) MaximumSharedMovementTickAllocatedBytes = sharedMovementBytes;
			if (predictionWorkBytes > MaximumPredictionTickAllocatedBytes) MaximumPredictionTickAllocatedBytes = predictionWorkBytes;
			if (_lastPredictedTick % AllocationSampleIntervalTicks == 0)
			{
				PredictionDiagnostics.RecordAllocation(_lastPredictedTick, predictionWorkBytes, sharedMovementBytes);
			}

			PredictionDiagnostics.RecordClientTick(world, _lastPredictedTick, this);
		}
		finally
		{
			EndOperation();
		}
	}

	/// <summary>
	/// Rolls back an order the authority never acknowledged. An order is expired once it has waited the full timeout, or
	/// as soon as it has waited out the acknowledgement round trip and the unit has drifted further from the
	/// authoritative sample than a correction may travel in one snapshot interval. The second condition matters under
	/// packet loss: without it a lost order keeps predicting for three seconds, which carries the unit far enough that
	/// the eventual correction exceeds the snap threshold and is reported as an unexplained snap.
	/// </summary>
	private void ExpireUnacknowledgedCommands(EcsWorld world)
	{
		for (int slot = 0; slot < PendingCommandCapacity; slot++)
		{
			int commandId = _pendingCommandIds[slot];
			if (commandId <= 0) continue;
			int waitedTicks = _lastPredictedTick - _pendingCommandRegisteredTicks[slot];
			if (waitedTicks < UnacknowledgedCommandMinimumTicks) continue;
			bool timedOut = waitedTicks >= UnacknowledgedCommandTimeoutTicks;

			int unitCount = _pendingCommandUnitCounts[slot];
			int stillPending = 0;
			for (int index = 0; index < unitCount; index++)
			{
				int entityId = _pendingCommandUnits[slot][index];
				if (_commandIds[entityId] != commandId) continue;

				var entity = new Entity(entityId);
				if (!world.IsAlive(entity)) continue;
				if (world.Has<UnitOwnerPlayer>(entity) && world.Get<UnitOwnerPlayer>(entity).PlayerIndex != _localUnitPlayerIndex) continue;

				float driftMeters = 0f;
				if (!timedOut)
				{
					if (!_hasAuthoritativeSample[entityId] || !world.Has<Position>(entity))
					{
						stillPending++;
						continue;
					}

					driftMeters = Vector3.Distance(world.Get<Position>(entity).Value, _authoritativePositions[entityId]);
					if (driftMeters <= UnacknowledgedCommandDriftLimitMeters)
					{
						stillPending++;
						continue;
					}
				}

				ClearPredictedOrder(world, entity);
				_commandIds[entityId] = 0;
				ExpiredOrderCount++;
				string reason = timedOut
					? "the authority never acknowledged the order"
					: $"the authority never acknowledged the order and the unit had drifted {driftMeters:F1} m";
				PredictionDiagnostics.RecordRollback(commandId, entityId, reason, _lastSnapshotSequence);
			}

			if (stillPending > 0) continue;
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

	private void CaptureStepStartPositions(EcsWorld world)
	{
		for (int index = 0; index < _trackedCount; index++)
		{
			int entityId = _trackedEntityIds[index];
			var entity = new Entity(entityId);
			if (!world.IsAlive(entity) || !world.Has<Position>(entity))
			{
				_hasStepStartPosition[entityId] = false;
				continue;
			}

			_stepStartPositions[entityId] = world.Get<Position>(entity).Value;
			_hasStepStartPosition[entityId] = true;
		}
	}

	/// <summary>
	/// Reverts a prediction step that moved the unit further than any unit can travel in one tick and drops the order
	/// responsible. The shared movement step repairs an off-mesh waypoint by moving the unit onto the navmesh, which the
	/// client must not adopt inside prediction: the authority's next sample would snap it straight back.
	/// </summary>
	private void RevertImplausibleSteps(EcsWorld world)
	{
		for (int index = 0; index < _trackedCount; index++)
		{
			int entityId = _trackedEntityIds[index];
			if (!_hasStepStartPosition[entityId]) continue;
			_hasStepStartPosition[entityId] = false;

			var entity = new Entity(entityId);
			if (!world.IsAlive(entity) || !world.Has<Position>(entity)) continue;

			Vector3 start = _stepStartPositions[entityId];
			Vector3 moved = world.Get<Position>(entity).Value;
			float stepMeters = Vector3.Distance(moved, start);
			if (stepMeters <= MaximumPredictionStepMeters) continue;

			world.Set(entity, new Position(start));
			PredictionDiagnostics.RecordCorrection(entityId, moved, start, _authoritativePositions[entityId], _lastSnapshotSequence);

			bool droppedOrder = world.Has<PredictedMoveTo>(entity);
			int commandId = _commandIds[entityId];
			ClearPredictedOrder(world, entity);
			_commandIds[entityId] = 0;
			if (!droppedOrder) continue;
			ExpiredOrderCount++;
			PredictionDiagnostics.RecordRollback(commandId, entityId, $"a predicted step of {stepMeters:F2} m in one tick was reverted", _lastSnapshotSequence);
		}
	}

	/// <summary>
	/// Drops any predicted order that has carried the unit further from the authority than the lead limit. Prediction is
	/// allowed to lead by the formation scatter and the one-way delay, never by enough that the next authoritative sample
	/// has to snap it back.
	/// </summary>
	private void ClampPredictionLead(EcsWorld world)
	{
		for (int index = 0; index < _trackedCount; index++)
		{
			int entityId = _trackedEntityIds[index];
			var entity = new Entity(entityId);
			if (!world.IsAlive(entity) || !world.Has<Position>(entity) || !world.Has<PredictedMoveTo>(entity)) continue;
			if (!_hasAuthoritativeSample[entityId]) continue;

			float leadMeters = Vector3.Distance(world.Get<Position>(entity).Value, _authoritativePositions[entityId]);
			if (leadMeters <= PredictionLeadLimitMeters) continue;

			int commandId = _commandIds[entityId];
			ClearPredictedOrder(world, entity);
			_commandIds[entityId] = 0;
			ExpiredOrderCount++;
			PredictionDiagnostics.RecordRollback(commandId, entityId, $"prediction led the authority by {leadMeters:F1} m", _lastSnapshotSequence);
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
				ForgetEntity(entityId);
				continue;
			}

			bool isLocalOwned = localPlayerIndex >= 0
				&& world.Has<UnitOwnerPlayer>(entity)
				&& world.Get<UnitOwnerPlayer>(entity).PlayerIndex == localPlayerIndex;
			if (!isLocalOwned)
			{
				_tracked[entityId] = false;
				ForgetEntity(entityId);
				PredictionDiagnostics.RecordPredictionStopped(entityId, "ownership changed or unit is not locally controlled");
				continue;
			}

			_trackedEntityIds[kept++] = entityId;
		}

		_trackedCount = kept;
	}

	/// <summary>
	/// Drops every prediction record for an entity id. Entity ids are recycled, so a unit that dies and a new unit that
	/// reuses its id must not inherit the dead unit's authoritative sample: the new spawn would otherwise be reconciled
	/// against a position it never had and snapped several metres on its first snapshot.
	/// </summary>
	private void ForgetEntity(int entityId)
	{
		_hasAuthoritativeSample[entityId] = false;
		_authoritativeSequences[entityId] = -1;
		_authoritativePositions[entityId] = Vector3.Zero;
		_previousAuthoritativePositions[entityId] = Vector3.Zero;
		_commandIds[entityId] = 0;
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

		// How many client ticks passed without any snapshot being applied. A loss burst delivers several snapshots
		// back to back, and the authority's position in the first of them has already moved by everything the client
		// missed, so the outage - not the per-unit sample gap - is what explains a large difference at that moment.
		_reconcileOutageTicks = _lastReconcileTick >= 0 ? _lastPredictedTick - _lastReconcileTick : 0;
		_previousAppliedSequence = _lastSnapshotSequence;
		_lastReconcileTick = _lastPredictedTick;
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
		int previousSequence = _authoritativeSequences[entityId];

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

		int sampleGapTicks = hasPrevious && previousSequence > 0 && _reconcileSequence > previousSequence
			? _reconcileSequence - previousSequence
			: 1;
		int streamGapTicks = _previousAppliedSequence > 0 ? _reconcileSequence - _previousAppliedSequence : 1;
		int effectiveGapTicks = Math.Max(sampleGapTicks, Math.Max(streamGapTicks, _reconcileOutageTicks));
		float sampleGapSeconds = MathF.Max(effectiveGapTicks * ImpliedSnapshotIntervalSeconds, 0.0001f);
		float impliedSpeed = hasPrevious
			? Vector3.Distance(authoritativePosition, previousAuthoritative) / sampleGapSeconds
			: float.PositiveInfinity;

		// Three causes make a snap the honest response: the authority moved faster than any unit can, the client went
		// without an authoritative sample long enough that unbounded travel is explained by the outage itself, or this is
		// the first sample the unit ever had, which is a spawn or the start of prediction rather than a prediction error.
		float preSnapErrorMeters = MathF.Sqrt(errorSquared);
		bool isFirstSample = !hasPrevious;
		bool isTeleport = hasPrevious && impliedSpeed > AuthoritativeJumpSpeedMetersPerSecond;
		bool recoveredFromGap = hasPrevious && effectiveGapTicks >= SnapshotGapToleranceTicks;

		if (isFirstSample || isTeleport || recoveredFromGap)
		{
			world.Set(entity, new Position(authoritativePosition));
			string reason = isFirstSample
				? $"first authoritative sample for the unit, {preSnapErrorMeters:F2} m from where the client spawned it"
				: (isTeleport
					? $"authoritative teleport at {impliedSpeed:F1} m/s, {preSnapErrorMeters:F2} m off"
					: $"recovered from a {effectiveGapTicks} tick snapshot outage, {preSnapErrorMeters:F2} m off");
			PredictionDiagnostics.RecordSnap(entityId, _reconcileSequence, predicted, authoritativePosition, true, reason);
			return;
		}

		world.Set(entity, new Position(authoritativePosition));
		PredictionDiagnostics.RecordSnap(entityId, _reconcileSequence, predicted, authoritativePosition, false, $"prediction error reached {preSnapErrorMeters:F2} m with a fresh sample and no stated cause");
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
		MaximumPredictionTickAllocatedBytes = 0;
		MaximumSharedMovementTickAllocatedBytes = 0;
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
