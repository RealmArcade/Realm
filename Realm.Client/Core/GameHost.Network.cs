using System;
using System.Collections.Generic;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Services;
using Realm.Shared.Network;

namespace Realm.Client.Core;

public partial class GameHost
{
	private int _pendingSelectUnitServerId = -1;

	private int GetServerEntityId(Entity localEntity) =>
		GameContext.NetworkService.GetServerEntityId(localEntity.Id);

	public bool TryGetLocalEntity(int serverEntityId, out Entity localEntity)
	{
		if (GameContext.NetworkService == null)
		{
			localEntity = Entity.Null;
			return false;
		}

		return GameContext.NetworkService.TryGetLocalEntity(serverEntityId, out localEntity);
	}

	public void SetBackupResources(float gold, float wood, float stone) =>
		GameContext.NetworkService.SetBackupResources(gold, wood, stone);

	public void KillUnitDeferredExternal(Unit3D unit)
	{
		Callable.From(() => KillUnitDeferred(unit)).CallDeferred();
	}

	private void RetireUnitFromView(Unit3D unit)
	{
		if (!GodotObject.IsInstanceValid(unit) || !AllUnits.Contains(unit)) return;

		RemoveUnitFromCollections(unit);
		GameContext.SimulationService.DestroyUnitEntityDomain(unit.Entity);
		unit.QueueFree();
		GameContext.InGameHud?.RefreshUI(SelectedUnits);
	}

	public void SubmitCheatOrder(string cheatKey, IEnumerable<Entity> selection)
	{
		if (string.IsNullOrWhiteSpace(cheatKey)) return;
		if (GameContext.ServerSession == null || GameContext.NetworkService == null) return;

		var unitIds = new List<int>();
		if (selection != null)
		{
			foreach (Entity entity in selection)
			{
				if (entity == Entity.Null) continue;

				int serverId = GetServerEntityId(entity);
				if (serverId > 0) unitIds.Add(serverId);
			}
		}

		var (commandId, payload) = GameContext.NetworkService.BuildClientCommand("cheat", unitIds, System.Numerics.Vector3.Zero, 0, cheatKey);
		if (commandId <= 0) return;
		GameContext.ServerSession.Submit(payload);
	}

	public void SubmitConsumableOrder(UnitId building, string cardId, System.Numerics.Vector3 targetPosition)
	{
		if (string.IsNullOrWhiteSpace(cardId)) return;
		if (GameContext.ServerSession == null || GameContext.NetworkService == null) return;
		if (!building.IsValid) return;

		Entity localBuilding = new Entity(building.Value);
		if (!GameContext.World.IsAlive(localBuilding)) return;

		int serverBuildingId = GetServerEntityId(localBuilding);
		if (serverBuildingId <= 0) return;

		var unitIds = new List<int> { serverBuildingId };
		var (commandId, payload) = GameContext.NetworkService.BuildClientCommand("use_consumable", unitIds, targetPosition, 0, cardId);
		if (commandId <= 0) return;
		GameContext.ServerSession.Submit(payload);
	}

	private int SubmitOrderCommand(string commandType, List<int> unitIds, Vector3 targetPos, int targetEntityId, string argString)
	{
		for (int index = unitIds.Count - 1; index >= 0; index--)
		{
			if (unitIds[index] > 0) continue;

			unitIds.RemoveAt(index);
		}

		if (unitIds.Count == 0) return 0;

		var targetNumerics = new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z);
		var (commandId, payload) = GameContext.NetworkService.BuildClientCommand(commandType, unitIds, targetNumerics, targetEntityId, argString);
		if (commandId <= 0) return 0;

		GameContext.ServerSession?.Submit(payload);
		return commandId;
	}

	private void PreviewRouteForOrder(int commandId, List<int> serverEntityIds, Vector3 targetPosition)
	{
		if (commandId <= 0 || GameContext.World == null) return;

		int localPlayerIndex = LocalPlayerIndex;
		if (localPlayerIndex < 0) return;

		var target = new System.Numerics.Vector3(targetPosition.X, targetPosition.Y, targetPosition.Z);
		for (int index = 0; index < serverEntityIds.Count; index++)
		{
			if (!TryGetLocalEntity(serverEntityIds[index], out Entity localEntity)) continue;
			if (!GameContext.World.IsAlive(localEntity)) continue;
			if (!GameContext.World.Has<UnitOwnerPlayer>(localEntity)) continue;
			if (GameContext.World.Get<UnitOwnerPlayer>(localEntity).PlayerIndex != localPlayerIndex) continue;

			GameContext.World.SetOrAdd(localEntity, new PreviewMoveTo
			{
				Target = target,
				CommandId = commandId,
				IssuedSequence = GameContext.NetworkService?.LastAppliedSnapshotSequenceForDiagnostics ?? -1
			});
		}
	}

	public const int PreviewLifetimeSequences = 20;

	private readonly List<Entity> _previewRouteEntitiesToClear = new();
	private EntityCallback<PreviewMoveTo>? _collectPreviewRouteToClear;
	private int _previewCommandIdToClear;

	private void ClearPreviewRoute(int commandId)
	{
		if (commandId <= 0 || GameContext.World == null) return;

		_previewCommandIdToClear = commandId;
		_previewRouteEntitiesToClear.Clear();
		_collectPreviewRouteToClear ??= CollectPreviewRouteToClear;
		GameContext.World.Query(in QueryCache.AllPreviewMoveToQuery, _collectPreviewRouteToClear);
		for (int index = 0; index < _previewRouteEntitiesToClear.Count; index++)
		{
			GameContext.World.Remove<PreviewMoveTo>(_previewRouteEntitiesToClear[index]);
		}

		_previewRouteEntitiesToClear.Clear();
	}

	private void CollectPreviewRouteToClear(Entity entity, ref PreviewMoveTo preview)
	{
		if (preview.CommandId == _previewCommandIdToClear) _previewRouteEntitiesToClear.Add(entity);
	}

	private readonly List<Entity> _expiredPreviewRouteEntities = new();
	private EntityCallback<PreviewMoveTo>? _collectExpiredPreviewRoute;
	private int _previewSweepSequence;
	private int _previewLifetimeSequences = PreviewLifetimeSequences;

	private static int ResolvePreviewLifetimeSequences()
	{
		NetworkLatencyService? latency = GameContext.Host != null ? GameContext.NetworkLatencyService : null;
		if (latency == null || !latency.HasSample) return PreviewLifetimeSequences;

		float roundTripSequences = latency.SmoothedRoundTripMilliseconds / 1000f / MatchTiming.SnapshotIntervalSeconds;
		return Math.Max(PreviewLifetimeSequences, (int)MathF.Ceiling(roundTripSequences * 2f));
	}

	private void ExpirePreviewRoutes()
	{
		if (GameContext.World == null) return;

		int sequence = GameContext.NetworkService?.LastAppliedSnapshotSequenceForDiagnostics ?? -1;
		if (sequence < 0) return;

		_previewSweepSequence = sequence;
		_previewLifetimeSequences = ResolvePreviewLifetimeSequences();
		_expiredPreviewRouteEntities.Clear();
		_collectExpiredPreviewRoute ??= CollectExpiredPreviewRoute;
		GameContext.World.Query(in QueryCache.AllPreviewMoveToQuery, _collectExpiredPreviewRoute);
		for (int index = 0; index < _expiredPreviewRouteEntities.Count; index++)
		{
			GameContext.World.Remove<PreviewMoveTo>(_expiredPreviewRouteEntities[index]);
		}

		_expiredPreviewRouteEntities.Clear();
	}

	private void CollectExpiredPreviewRoute(Entity entity, ref PreviewMoveTo preview)
	{
		if (preview.IssuedSequence >= 0 && _previewSweepSequence - preview.IssuedSequence > _previewLifetimeSequences) _expiredPreviewRouteEntities.Add(entity);
	}

	public void SubmitCommandFloodForHarness(string commandType, List<int> unitIds, Vector3 targetPos, int count)
	{
		var targetNumerics = new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z);
		for (int index = 0; index < count; index++)
		{
			var (floodCommandId, payload) = GameContext.NetworkService.BuildClientCommand(commandType, unitIds, targetNumerics, 0, string.Empty);
			if (floodCommandId <= 0) return;
			GameContext.ServerSession?.Submit(payload);
		}
	}

	private void SubmitRemoteCommand(string commandType, List<int> unitIds, Vector3 targetPos, int targetEntityId, string argString)
	{
		for (int index = unitIds.Count - 1; index >= 0; index--)
		{
			if (unitIds[index] > 0) continue;

			unitIds.RemoveAt(index);
		}

		if (unitIds.Count == 0) return;

		var targetNumerics = new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z);
		var (commandId, payload) = GameContext.NetworkService.BuildClientCommand(commandType, unitIds, targetNumerics, targetEntityId, argString);
		if (commandId <= 0) return;
		GameContext.ServerSession?.Submit(payload);
	}

	public int GetLocalPlayerIndexForHarness() => LocalPlayerIndex;

	public int GetServerEntityIdForHarness(int localEntityId) => GameContext.NetworkService.GetServerEntityId(localEntityId);

	public void IssueRawCommandForHarness(string commandType, List<int> serverEntityIds, Vector3 targetPosition, int targetEntityId = 0, string argString = "")
	{
		SubmitOrderCommand(commandType, serverEntityIds, targetPosition, targetEntityId, argString);
	}

	private void UpdateClientWorldState(float fDelta)
	{
		var res = GameContext.EnvironmentService.UpdateClientWorldStateDomain(fDelta, GameContext.SessionState.IsMapEditorMode, advanceSimulationClock: true);
		if (res.UpdateVisuals)
		{
			UpdateClientWorldStateViewLogic(res.Progress);
		}
	}

	private void UpdateClientWorldStateViewLogic(float progress)
	{
		UpdateDayNightVisuals(progress);
	}

	private void UpdateClientNavMesh()
	{
		UpdateNavMeshRefresh();
	}

	private void UpdateClientTick(float fDelta)
	{
		CapturePreviousStateSnapshots();
		_fDelta = fDelta;

		UpdateClientNavMesh();

		PollVFXQueue();
		TickConstructionSystem(fDelta, isAuthoritative: false);
		ProcessVisualAndEnvironment(fDelta);

		GameContext.NetworkService.UpdateClientTickDomain();
		GameContext.NetworkLatencyService.Update(fDelta);

		GameContext.SnapshotTimeline.Advance(fDelta);

		FlushPresentationForCursor();

		FlushUnitViewRemovalsForCursor();

		ExpirePreviewRoutes();

		GameContext.SimulationService.StepMovementPlanOnly(fDelta);

		UpdateClientWorldState(fDelta);

		var query = QueryCache.AllLatestDescriptionQuery;
		GameContext.SimulationService.SetDelta(fDelta);
		GameContext.World.Query(in query, GameContext.SimulationService.PresentationQueryDelegate);
		UpdateVisualNodesFromEcs(fDelta);
		UpdateBuildingPreview();

		RecordGameplayTickIfNeeded();
	}

	public void SendSnapshotAck(int sequence, ulong appliedMask)
	{
		int roundTripMilliseconds = Mathf.RoundToInt(GameContext.NetworkLatencyService?.SmoothedRoundTripMilliseconds ?? 0f);
		GameContext.ServerSession?.Acknowledge(sequence, appliedMask, roundTripMilliseconds);
	}

	public void RequestSnapshotBaseline() => GameContext.ServerSession?.RequestBaseline();

	private void ApplyReceivedSnapshot(WorldSnapshot snapshot)
	{
		GameContext.SnapshotApply.Accept(snapshot);
		FlushReplicatedSnapshotEvents();
		MultiplayerSyncDiagnostics.RecordClientPresentation();
	}

	private const double ReplicatedUnitSpawnBudgetMilliseconds = 4.0;

	private double _replicatedUnitSpawnBudgetRemainingMilliseconds;

	public void FlushReplicatedSnapshotEvents()
	{
		long spawnStarted = System.Diagnostics.Stopwatch.GetTimestamp();
		int spawned = 0;
		while ((_replicatedUnitSpawnBudgetRemainingMilliseconds > 0 || spawned == 0)
		       && GameContext.NetworkService.TryTakePendingUnitSpawn(out UnitSnapshot pendingSpawn))
		{
			SpawnUnitFromSnapshot(pendingSpawn);
			spawned++;
			_replicatedUnitSpawnBudgetRemainingMilliseconds -= (System.Diagnostics.Stopwatch.GetTimestamp() - spawnStarted)
			                                                   * 1000.0
			                                                   / System.Diagnostics.Stopwatch.Frequency;
			spawnStarted = System.Diagnostics.Stopwatch.GetTimestamp();
		}

		int removalSequence = GameContext.NetworkService.LastAppliedSnapshotSequenceForDiagnostics;
		foreach (var localEntity in GameContext.NetworkService.FlushPendingUnitKills())
		{
			if (TryGetUnit3D(localEntity, out var unit3D)) QueueUnitViewRemoval(unit3D, removalSequence, retire: false);
		}

		foreach (var localEntity in GameContext.NetworkService.FlushPendingUnitDespawns())
		{
			if (TryGetUnit3D(localEntity, out var unit3D)) QueueUnitViewRemoval(unit3D, removalSequence, retire: true);
		}

		if (spawned > 0)
		{
			GameContext.NetworkService.ReapplyAbilityStatesToUnits();
		}
	}

	private readonly record struct PendingUnitViewRemoval(Unit3D Unit, int Sequence, bool Retire);

	private readonly List<PendingUnitViewRemoval> _pendingUnitViewRemovals = new();

	private void QueueUnitViewRemoval(Unit3D unit3D, int sequence, bool retire)
	{
		if (!GodotObject.IsInstanceValid(unit3D)) return;
		if (sequence < 0) sequence = 0;

		for (int index = 0; index < _pendingUnitViewRemovals.Count; index++)
		{
			if (!ReferenceEquals(_pendingUnitViewRemovals[index].Unit, unit3D)) continue;
			if (retire) _pendingUnitViewRemovals[index] = new PendingUnitViewRemoval(unit3D, sequence, true);
			return;
		}

		_pendingUnitViewRemovals.Add(new PendingUnitViewRemoval(unit3D, sequence, retire));
	}

	private void FlushUnitViewRemovalsForCursor()
	{
		if (_pendingUnitViewRemovals.Count == 0) return;

		float cursor = GameContext.SnapshotTimeline.CursorSequence;
		if (cursor < 0f) return;

		int kept = 0;
		for (int index = 0; index < _pendingUnitViewRemovals.Count; index++)
		{
			PendingUnitViewRemoval removal = _pendingUnitViewRemovals[index];
			if (removal.Sequence > cursor)
			{
				_pendingUnitViewRemovals[kept++] = removal;
				continue;
			}

			if (!GodotObject.IsInstanceValid(removal.Unit)) continue;

			Unit3D unit = removal.Unit;
			if (removal.Retire) Callable.From(() => RetireUnitFromView(unit)).CallDeferred();
			else Callable.From(() => KillUnitDeferred(unit)).CallDeferred();
		}

		if (kept < _pendingUnitViewRemovals.Count) _pendingUnitViewRemovals.RemoveRange(kept, _pendingUnitViewRemovals.Count - kept);
	}

	public void RefreshReplicatedOverlayUi()
	{
		GameContext.InGameHud?.InvalidateReplicatedOverlays();
	}

	private void SpawnUnitFromSnapshot(UnitSnapshot snap)
	{
		var entity = GameContext.NetworkService.SpawnUnitFromSnapshot(snap, GetFallbackModelPath, out string modelPath, out int playerIndex);
		if (entity == Entity.Null) return;
		var unit3D = SpawnUnit3D(entity, snap.UnitId, modelPath, new Vector3(snap.Position.X, snap.Position.Y, snap.Position.Z), snap.IsBuilding, playerIndex);
		unit3D.Rotation = new Vector3(0f, snap.RotationY, 0f);
		if (_pendingSelectUnitServerId == snap.EntityId && GodotObject.IsInstanceValid(unit3D))
		{
			ClearSelection();
			SelectUnit(unit3D);
			GameContext.InGameHud?.RefreshUI(SelectedUnits);
			_pendingSelectUnitServerId = -1;
		}
	}

	public void NetworkPingMinimap(Vector3 position)
	{
		AddMinimapPing(position);
	}

	public void TogglePauseRequest()
	{
		ClientServerSession? session = GameContext.ServerSession;
		if (session != null && session.RequestPause(!GameContext.NetworkService.IsPaused)) return;

		GD.PushWarning("[GameHost] There is no match to ask for a pause: nothing is running this world's clock.");
	}

	private void UpdatePauseUI()
	{
		GameContext.InGameHud?.UpdatePauseUI();
	}

	public void SyncEnvironmentPresetRpc(string presetId, float durationSeconds)
	{
		if (durationSeconds <= 0.001f)
		{
			GameContext.EnvironmentService?.ApplyPresetById(presetId);
		}
		else
		{
			GameContext.EnvironmentService?.TransitionToPreset(presetId, durationSeconds);
		}
	}

	public void SyncWeatherRpc(string weatherType)
	{
		GameContext.EnvironmentService?.SetCurrentWeather(weatherType);
		GameContext.InGameHud?.ApplyWeatherEffects(weatherType);
	}

	public void ClientRegisterAbility(string abilityId, string displayName, string tooltip, string iconPath, bool isInstant)
	{
		if (GameContext.RegistryService.RegisterAbility(abilityId, displayName, tooltip, iconPath, isInstant))
		{
			GameContext.InGameHud?.RefreshUI(SelectedUnits);
		}
	}

	public void ClientShowFeedbackText(string text, Vector3 color)
	{
		if (GameContext.InGameHud == null) return;

		var textColor = new Color(color.X, color.Y, color.Z);
		Callable.From(() => GameContext.InGameHud?.ShowFeedbackText(text, textColor)).CallDeferred();
	}

	public void ClientStartCountdownTimer(float duration, string label)
	{
		Callable.From(() => GameContext.InGameHud?.StartCountdownTimer(duration, label)).CallDeferred();
	}

	public void ClientStopCountdownTimer()
	{
		Callable.From(() => GameContext.InGameHud?.StopCountdownTimer()).CallDeferred();
	}

	public void ClientSelectUnit(int serverEntityId)
	{
		Callable.From(() =>
		{
			if (TryGetLocalEntity(serverEntityId, out Entity targetEntity)
				&& TryGetUnit3D(targetEntity, out var unit3D)
				&& GodotObject.IsInstanceValid(unit3D))
			{
				ClearSelection();
				SelectUnit(unit3D);
				GameContext.InGameHud?.RefreshUI(SelectedUnits);
			}
			else
			{
				_pendingSelectUnitServerId = serverEntityId;
			}
		}).CallDeferred();
	}

	public void ClientClearSelection()
	{
		ClearSelectionInternal();
	}
}
