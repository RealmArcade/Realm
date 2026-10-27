using System;
using System.Collections.Generic;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
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

	/// <summary>
	///     Hands the authority a cheat the player typed, with what they had selected, which is what a cheat that reaches
	///     a unit acts on. What a cheat does is the match's to decide, so this is the only way one reaches the world.
	/// </summary>
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

		var (_, payload) = GameContext.NetworkService.BuildClientCommand("cheat", unitIds, System.Numerics.Vector3.Zero, 0, cheatKey);
		GameContext.ServerSession.Submit(payload);
	}

	/// <summary>
	///     Hands the authority a purchase of one of the map's consumables, aimed at a place on the board. What the card
	///     costs, which unit is close enough to receive it and whether that unit has room are the match's to decide, so a
	///     purchase leaves this screen as an order rather than as a change it makes to its own copy of the world.
	/// </summary>
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
		var (_, payload) = GameContext.NetworkService.BuildClientCommand("use_consumable", unitIds, targetPosition, 0, cardId);
		GameContext.ServerSession.Submit(payload);
	}

	private void SubmitOrderCommand(string commandType, List<int> unitIds, Vector3 targetPos, int targetEntityId, string argString)
	{
		var targetNumerics = new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z);
		var (commandId, payload) = GameContext.NetworkService.BuildClientCommand(commandType, unitIds, targetNumerics, targetEntityId, argString);
		RegisterPredictedCommand(commandId, unitIds, commandType, targetNumerics);

		GameContext.ServerSession?.Submit(payload);
	}

	/// <summary>
	/// Submits commands without registering them for prediction. The harness uses this to exercise the authority's command
	/// budget: a flood must be refused by the authority, and registering hundreds of predicted orders locally would
	/// measure the client's rollback bookkeeping instead of the authority's rate limit.
	/// </summary>
	public void SubmitCommandFloodForHarness(string commandType, List<int> unitIds, Vector3 targetPos, int count)
	{
		var targetNumerics = new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z);
		for (int index = 0; index < count; index++)
		{
			var (_, payload) = GameContext.NetworkService.BuildClientCommand(commandType, unitIds, targetNumerics, 0, string.Empty);
			GameContext.ServerSession?.Submit(payload);
		}
	}

	private void SubmitRemoteCommand(string commandType, List<int> unitIds, Vector3 targetPos, int targetEntityId, string argString)
	{
		var targetNumerics = new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z);
		var (commandId, payload) = GameContext.NetworkService.BuildClientCommand(commandType, unitIds, targetNumerics, targetEntityId, argString);
		RegisterPredictedCommand(commandId, unitIds, commandType, targetNumerics);
		GameContext.ServerSession?.Submit(payload);
	}

	private void RegisterPredictedCommand(int commandId, List<int> serverEntityIds, string commandType, System.Numerics.Vector3 targetPosition)
	{
		if (!GameSettings.MovementPrediction || GameContext.MovementPredictionService == null) return;
		if (GameContext.World == null) return;

		int localPlayerIndex = LocalPlayerIndex;
		if (localPlayerIndex < 0) return;

		_predictedCommandEntities.Clear();
		for (int index = 0; index < serverEntityIds.Count; index++)
		{
			if (!TryResolveLocalEntity(serverEntityIds[index], out Entity localEntity)) continue;
			if (!GameContext.World.IsAlive(localEntity)) continue;
			if (!GameContext.World.Has<UnitOwnerPlayer>(localEntity)) continue;
			if (GameContext.World.Get<UnitOwnerPlayer>(localEntity).PlayerIndex != localPlayerIndex) continue;
			_predictedCommandEntities.Add(localEntity.Id);
		}

		if (_predictedCommandEntities.Count == 0) return;
		GameContext.MovementPredictionService.RegisterPredictedCommand(commandId, _predictedCommandEntities);
		GameContext.MovementPredictionService.ApplyPredictedOrder(
			GameContext.World,
			ResolvePredictedOrderKind(commandType),
			System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_predictedCommandEntities),
			targetPosition,
			commandType.EndsWith("_queued", StringComparison.Ordinal));
	}

	private bool TryResolveLocalEntity(int serverEntityId, out Entity localEntity)
	{
		if (TryGetLocalEntity(serverEntityId, out localEntity)) return true;

		localEntity = Entity.Null;
		return false;
	}

	private static PredictedOrderKind ResolvePredictedOrderKind(string commandType)
	{
		return commandType switch
		{
			"move" => PredictedOrderKind.Move,
			"attack_move" => PredictedOrderKind.AttackMove,
			"attack_move_queued" => PredictedOrderKind.AttackMove,
			"gather" => PredictedOrderKind.Gather,
			"build" => PredictedOrderKind.Build,
			"stop" => PredictedOrderKind.Stop,
			_ => PredictedOrderKind.None
		};
	}

	private readonly List<int> _predictedCommandEntities = new();

	public int GetLocalPlayerIndexForHarness() => LocalPlayerIndex;

	public int GetServerEntityIdForHarness(int localEntityId) => GameContext.NetworkService.GetServerEntityId(localEntityId);

	public void IssueRawCommandForHarness(string commandType, List<int> serverEntityIds, Vector3 targetPosition, int targetEntityId = 0, string argString = "")
	{
		SubmitOrderCommand(commandType, serverEntityIds, targetPosition, targetEntityId, argString);
	}

	private void UpdateClientWorldState(float fDelta)
	{
		// The client's own copy of the clock keeps running: what the authority decides about the hour arrives as a
		// presentation request, and this is the fall of light between those requests.
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
		// A peer refreshes the mesh only for the obstacle set it had to receive after its first bake - the map's props and
		// pre-placed buildings. A building that appears mid-match is carved on both sides (HandleBuildingSpawn), and baking
		// it into the geometry on one side only is what left the two meshes with a different polygon split.
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

		GameContext.MovementPredictionService?.Advance(GameContext.World, fDelta);

		UpdateClientWorldState(fDelta);

		var query = QueryCache.AllInterpolationTargetQuery;
		GameContext.SimulationService.SetDelta(fDelta);
		GameContext.World.Query(in query, GameContext.SimulationService.InterpolationQueryDelegate);
		UpdateVisualNodesFromEcs(fDelta);
		UpdateBuildingPreview();

		RecordGameplayTickIfNeeded();
	}

	public void SendSnapshotAck(int sequence, ulong appliedMask) => GameContext.ServerSession?.Acknowledge(sequence);

	public void RequestSnapshotBaseline() => GameContext.ServerSession?.RequestBaseline();

	private void ApplyReceivedSnapshot(WorldSnapshot snapshot)
	{
		GameContext.NetworkService.RecordSnapshotReceived(snapshot);
		FlushReplicatedSnapshotEvents();
		MultiplayerSyncDiagnostics.RecordClientPresentation();
	}

	public void FlushReplicatedSnapshotEvents()
	{
		var pendingSpawns = GameContext.NetworkService.FlushPendingUnitSpawns();
		for (int i = 0; i < pendingSpawns.Count; i++)
		{
			SpawnUnitFromSnapshot(pendingSpawns[i]);
		}

		foreach (var localEntity in GameContext.NetworkService.FlushPendingUnitKills())
		{
			if (TryGetUnit3D(localEntity, out var unit3D))
			{
				Callable.From(() => KillUnitDeferred(unit3D)).CallDeferred();
			}
		}

		foreach (var localEntity in GameContext.NetworkService.FlushPendingUnitDespawns())
		{
			if (TryGetUnit3D(localEntity, out var unit3D))
			{
				Callable.From(() => RetireUnitFromView(unit3D)).CallDeferred();
			}
		}

		if (pendingSpawns.Count > 0)
		{
			GameContext.NetworkService.ReapplyAbilityStatesToUnits();
		}
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

	/// <summary>
	///     Asks the match to pause or resume. The clock belongs to the process running the match, so this is a request:
	///     what the screen shows afterwards is the state that process answered with.
	/// </summary>
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
			Entity targetEntity = Entity.Null;
			if (!TryGetLocalEntity(serverEntityId, out targetEntity))
			{
				foreach (var unit in AllUnits)
				{
					if (GodotObject.IsInstanceValid(unit) && unit.Entity.Id == serverEntityId)
					{
						targetEntity = unit.Entity;
						break;
					}
				}
			}

			if (targetEntity != Entity.Null && TryGetUnit3D(targetEntity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
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
