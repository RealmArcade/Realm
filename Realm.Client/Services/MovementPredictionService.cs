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
	/// The furthest a predicted order may ever lead the latest authoritative sample. It sits above the authority's 2.2 m
	/// group formation scatter, so a normal group order is not clipped, and below the snap threshold, so dropping the
	/// order always leaves the difference small enough for a bounded correction instead of a snap. This is the only bound
	/// that applies to every predicted order: it is measured against the authority's own sample, so it holds whether or
	/// not the client heard anything back about the command that started the order.
	/// </summary>
	public const float PredictionLeadLimitMeters = 3.0f;
	/// <summary>
	/// The furthest a predicted unit may move in a single tick. Prediction runs the shared movement step, which performs
	/// off-mesh recovery and waypoint repairs that legitimately teleport the authority's unit between two of its ticks;
	/// on the client the same repair would move a unit several metres in one tick and the next authoritative sample would
	/// have to snap it back. Any displacement above this bound is reverted and the predicted order is dropped.
	/// </summary>
	public const float MaximumPredictionStepMeters = 1.0f;

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
	private readonly Vector3[] _preTickAuthoritativePositions = new Vector3[EcsWorld.MAX_ENTITIES];
	private readonly int[] _preTickAuthoritativeSequences = new int[EcsWorld.MAX_ENTITIES];
	private readonly int[] _preTickCaptureTicks = new int[EcsWorld.MAX_ENTITIES];
	private readonly int[] _trackedEntityIds = new int[MaximumTrackedUnitCount];
	private readonly EntityCallback<Position, UnitOwnerPlayer> _localUnitQueryDelegate;
	private int _localUnitPlayerIndex = -1;

	private int _trackedCount;
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
		_localUnitQueryDelegate = CollectLocalUnitAction;
		Array.Fill(_preTickCaptureTicks, -1);
	}

	public bool IsPredictionEnabled => GameSettings.MovementPrediction;
	public bool HasPredictedUnits => _trackedCount > 0;
	public int SnapshotOrderingViolations { get; private set; }
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
				// walk to the placement point would invent a destination the authority never ordered. The unit is still
				// tracked, so a rejection naming it clears anything it was already predicting; only the walk is withheld.
				if (kind == PredictedOrderKind.Build) continue;

				world.SetOrAdd(entity, new PredictedMoveTo(targetPosition, (int)kind));
			}
		}
		finally
		{
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

	/// <summary>
	/// Anchors prediction for a unit the client has just been told about. A sample is otherwise only recorded on the
	/// apply path, and that path reconciles the units it can already map - so the snapshot that first describes a unit
	/// creates it without a sample, and a unit that then sits still is never carried by a delta at all. Prediction for
	/// such a unit had nothing to compare against, so neither the lead clamp nor the snap decision could run for it.
	/// </summary>
	public void AdoptSpawnedEntity(int entityId, Vector3 position, int sequence)
	{
		// Not gated on the prediction setting. Units are spawned from the map load, which happens before a settings
		// change can reach this service, and a sample that is skipped then is never recovered for a unit that stands
		// still: prediction would start on a unit whose authority it has never been told.
		if (entityId <= 0 || entityId >= EcsWorld.MAX_ENTITIES) return;

		_previousAuthoritativePositions[entityId] = position;
		_authoritativePositions[entityId] = position;
		_authoritativeSequences[entityId] = sequence;
		_hasAuthoritativeSample[entityId] = true;
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

	/// <summary>
	/// Records which command started each unit's predicted order. A rejection names the units it refused, and the client
	/// only needs the reverse lookup to know which order to clear; a command the authority never mentions needs no record
	/// at all, because <see cref="PredictionLeadLimitMeters"/> bounds every predicted order against the authority's own
	/// sample by distance rather than by waiting for an answer.
	/// </summary>
	public void RegisterPredictedCommand(int commandId, IReadOnlyList<int> localEntityIds)
	{
		if (!IsPredictionEnabled || localEntityIds == null || localEntityIds.Count == 0) return;

		for (int index = 0; index < localEntityIds.Count; index++)
		{
			int entityId = localEntityIds[index];
			_commandIds[entityId] = commandId;
			MarkTracked(entityId);
		}

		PredictionDiagnostics.RecordCommandRegistered(commandId, localEntityIds);
	}

	/// <summary>
	/// How often prediction runs, and therefore what every tick-counted bound in this service means: the snapshot gap
	/// tolerance and the settle deadline. The rate has to be the same for every role. It used to be
	/// fixed on a remote client and the frame rate on the host, so one constant meant 0.6 s on a 144 fps host and 3 s on a
	/// 60 fps client, and the settled correction ran at a different geometric rate on each.
	/// </summary>
	public const float PredictionTickIntervalSeconds = 1.0f / 30.0f;

	/// <summary>
	/// The most prediction ticks one frame may run. A long frame must not turn into a burst of prediction ticks that walks
	/// a unit through orders the authority has already answered.
	/// </summary>
	private const int MaximumCatchUpTicks = 3;

	private float _tickAccumulator;

	public void Advance(EcsWorld world, float frameDelta)
	{
		if (!IsPredictionEnabled || world == null) return;
		if (!float.IsFinite(frameDelta) || frameDelta <= 0f) return;

		_tickAccumulator += frameDelta;
		int executedTicks = 0;
		while (_tickAccumulator >= PredictionTickIntervalSeconds && executedTicks < MaximumCatchUpTicks)
		{
			_tickAccumulator -= PredictionTickIntervalSeconds;
			executedTicks++;
			Tick(world, PredictionTickIntervalSeconds);
		}

		if (_tickAccumulator > PredictionTickIntervalSeconds) _tickAccumulator = PredictionTickIntervalSeconds;
	}

	private void Tick(EcsWorld world, float delta)
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

			PositionWriteTrace.Mark(entity, PositionWriter.Prediction);
			world.Set(entity, new Position(start));
			PredictionDiagnostics.RecordCorrection(entityId, moved, start, _authoritativePositions[entityId], _lastSnapshotSequence);

			bool droppedOrder = world.Has<PredictedMoveTo>(entity);
			int commandId = _commandIds[entityId];
			ClearPredictedOrder(world, entity);
			_commandIds[entityId] = 0;
			if (!droppedOrder) continue;
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
			PredictionDiagnostics.RecordRollback(commandId, entityId, $"prediction led the authority by {leadMeters:F1} m", _lastSnapshotSequence);
		}
	}

	private void ApplyBoundedCorrections(EcsWorld world, float delta)
	{
		if (_trackedCount == 0) return;

		float movingFraction = Math.Min(1f, CorrectionRatePerSecond * delta);
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

			// A unit with no predicted order has nothing to lead, so the sample is the position and the renderer smooths
			// toward it. This used to be a rate of "30 per second" applied at a 1/30 s tick, which saturates to a full set
			// every time - the constant had no effect at all and the prose around it described a convergence that never
			// happened.
			Vector3 corrected;
			if (world.Has<PredictedMoveTo>(entity))
			{
				if (errorSquared <= ActiveCorrectionDeadbandMeters * ActiveCorrectionDeadbandMeters) continue;
				corrected = Vector3.Lerp(predicted, authoritative, movingFraction);
			}
			else
			{
				if (errorSquared <= SettledErrorMeters * SettledErrorMeters) continue;
				corrected = authoritative;
			}

			PositionWriteTrace.Mark(entity, PositionWriter.Prediction);
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
				// The unit is still alive, so its authoritative sample is still the position the authority last gave for
				// this id and stays valid. Dropping it here lost the anchor whenever the owner component was briefly
				// unresolved after a spawn, and prediction then ran again with nothing to clamp or expire against.
				_tracked[entityId] = false;
				_commandIds[entityId] = 0;
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

		// Several snapshots can be applied in one client tick - a loss burst or a baseline finally arriving delivers a
		// group of them at once. The unit's sample age that explains a large difference is the age before that group,
		// not the age of the member that happens to sit in front of this one: measuring member to member reported a
		// one-tick gap and therefore "no stated cause" for a snap that was really a second of missing data.
		if (_preTickCaptureTicks[entityId] != _lastPredictedTick)
		{
			_preTickCaptureTicks[entityId] = _lastPredictedTick;
			_preTickAuthoritativeSequences[entityId] = previousSequence;
			_preTickAuthoritativePositions[entityId] = previousAuthoritative;
		}

		int sampleGapSequence = _preTickAuthoritativeSequences[entityId];
		Vector3 sampleGapPosition = _preTickAuthoritativePositions[entityId];

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

		int sampleGapTicks = hasPrevious && sampleGapSequence > 0 && _reconcileSequence > sampleGapSequence
			? _reconcileSequence - sampleGapSequence
			: 1;
		int streamGapTicks = _previousAppliedSequence > 0 ? _reconcileSequence - _previousAppliedSequence : 1;
		int effectiveGapTicks = Math.Max(sampleGapTicks, Math.Max(streamGapTicks, _reconcileOutageTicks));
		float sampleGapSeconds = MathF.Max(effectiveGapTicks * ImpliedSnapshotIntervalSeconds, 0.0001f);
		float impliedSpeed = hasPrevious
			? Vector3.Distance(authoritativePosition, sampleGapPosition) / sampleGapSeconds
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
			PositionWriteTrace.Mark(entity, PositionWriter.Prediction);
			world.Set(entity, new Position(authoritativePosition));
			string reason = isFirstSample
				? $"first authoritative sample for the unit, {preSnapErrorMeters:F2} m from where the client spawned it"
				: (isTeleport
					? $"authoritative teleport at {impliedSpeed:F1} m/s, {preSnapErrorMeters:F2} m off"
					: $"recovered from a {effectiveGapTicks} tick snapshot outage, {preSnapErrorMeters:F2} m off");
			PredictionDiagnostics.RecordSnap(entityId, _reconcileSequence, predicted, authoritativePosition, true, reason);
			return;
		}

		PositionWriteTrace.Mark(entity, PositionWriter.Prediction);
		world.Set(entity, new Position(authoritativePosition));
		string unexplainedReason = $"prediction error reached {preSnapErrorMeters:F2} m with no stated cause (sample gap {sampleGapTicks} ticks, stream gap {streamGapTicks} ticks, reconcile outage {_reconcileOutageTicks} ticks, effective gap {effectiveGapTicks} ticks, implied {impliedSpeed:F1} m/s, previous authoritative {sampleGapPosition.X:F2},{sampleGapPosition.Y:F2},{sampleGapPosition.Z:F2} at sequence {sampleGapSequence}, authoritative {authoritativePosition.X:F2},{authoritativePosition.Y:F2},{authoritativePosition.Z:F2} at sequence {_reconcileSequence})";
		PredictionDiagnostics.RecordSnap(entityId, _reconcileSequence, predicted, authoritativePosition, false, unexplainedReason);
	}

	public void EndSnapshotReconcile()
	{
		_isReconciling = false;
	}

	/// <summary>
	/// Rolls back the predicted orders a rejected command left behind. A rejection that names units clears exactly the
	/// orders of those units; a rejection that names none refused the command itself, so every order this client
	/// attributed to that command is cleared.
	/// </summary>
	public void RollbackRejectedCommand(EcsWorld world, int commandId, int[] rejectedServerEntityIds, string reason)
	{
		if (!IsPredictionEnabled || world == null) return;

		if (rejectedServerEntityIds != null && rejectedServerEntityIds.Length > 0)
		{
			var networkService = GameContext.NetworkService;
			if (networkService == null) return;

			for (int index = 0; index < rejectedServerEntityIds.Length; index++)
			{
				if (!networkService.TryGetLocalEntity(rejectedServerEntityIds[index], out Entity rejectedEntity)) continue;
				ClearRejectedOrder(world, rejectedEntity, commandId, reason);
			}

			return;
		}

		for (int index = 0; index < _trackedCount; index++)
		{
			int entityId = _trackedEntityIds[index];
			if (_commandIds[entityId] != commandId) continue;
			ClearRejectedOrder(world, new Entity(entityId), commandId, reason);
		}
	}

	private void ClearRejectedOrder(EcsWorld world, Entity entity, int commandId, string reason)
	{
		if (!world.IsAlive(entity)) return;

		if (world.Has<PredictedMoveTo>(entity))
		{
			ClearPredictedOrder(world, entity);
			PredictionDiagnostics.RecordRollback(commandId, entity.Id, reason, _lastSnapshotSequence);
		}

		_commandIds[entity.Id] = 0;
	}

	public void OnCommandAccepted(int commandId)
	{
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
		SnapshotOrderingViolations = 0;
		MaximumPredictionTickAllocatedBytes = 0;
		MaximumSharedMovementTickAllocatedBytes = 0;
		_tickAccumulator = 0f;
		_reconcileSequence = -1;
		_lastSnapshotSequence = -1;
		_lastPredictedTick = 0;
		_isReconciling = false;
		Array.Clear(_tracked);
		Array.Clear(_hasAuthoritativeSample);
		Array.Clear(_commandIds);
		Array.Fill(_preTickCaptureTicks, -1);
		Array.Clear(_preTickAuthoritativeSequences);
	}
}
