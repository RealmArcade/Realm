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

		var (commandId, payload) = GameContext.NetworkService.BuildClientCommand("cheat", unitIds, System.Numerics.Vector3.Zero, 0, cheatKey);
		if (commandId <= 0) return;
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
		var (commandId, payload) = GameContext.NetworkService.BuildClientCommand("use_consumable", unitIds, targetPosition, 0, cardId);
		if (commandId <= 0) return;
		GameContext.ServerSession.Submit(payload);
	}

	/// <summary>
	///     Hands the authority one order and answers which order it was, or nothing when it could not be authored.
	/// </summary>
	private int SubmitOrderCommand(string commandType, List<int> unitIds, Vector3 targetPos, int targetEntityId, string argString)
	{
		// An entity the authority never described has no authoritative id, so it cannot be part of an order. Dropping it
		// here keeps every order path honest: what is named is exactly what the match has.
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

	/// <summary>
	///     Draws the route the order just sent is asking for, on the tick it was asked for. The authority decides what an
	///     order means - which unit goes where, and whether it is legal at all - so this is only ever a route to look at:
	///     the planning pass that reads it writes a path and no position. The match's own answer replaces it, because a
	///     unit the authority has given a destination to is planned from that instead.
	/// </summary>
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

	/// <summary>
	///     How many sequences a drawn route waits for the match to answer the order that drew it. An order is answered
	///     within a round trip, so this is several times what any playable link needs; it exists only so a route the match
	///     never speaks about cannot outlive the click that drew it.
	/// </summary>
	public const int PreviewLifetimeSequences = 20;

	private readonly List<Entity> _previewRouteEntitiesToClear = new();
	private EntityCallback<PreviewMoveTo>? _collectPreviewRouteToClear;
	private int _previewCommandIdToClear;

	/// <summary>
	///     Takes back the routes an order drew, which is what a refused order calls for: the units were never going
	///     anywhere, and a route left on screen would say they were.
	/// </summary>
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

	/// <summary>
	///     Takes back the routes of orders the match has not spoken about within <see cref="PreviewLifetimeSequences" />.
	///     A click that the match accepted without moving anything - the unit was already on the destination, or the order
	///     waits behind another - draws a route nothing will ever supersede, and the planning pass would keep producing it
	///     on every tick.
	/// </summary>
	private void ExpirePreviewRoutes()
	{
		if (GameContext.World == null) return;

		int sequence = GameContext.NetworkService?.LastAppliedSnapshotSequenceForDiagnostics ?? -1;
		if (sequence < 0) return;

		_previewSweepSequence = sequence;
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
		if (preview.IssuedSequence >= 0 && _previewSweepSequence - preview.IssuedSequence > PreviewLifetimeSequences) _expiredPreviewRouteEntities.Add(entity);
	}

	/// <summary>
	/// Submits commands without drawing a route for any of them. The harness uses this to exercise the authority's command
	/// budget: a flood must be refused by the authority, and planning hundreds of routes locally would measure this
	/// screen's pathfinding instead of the authority's rate limit.
	/// </summary>
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

		// The match clock is advanced once per tick and every replicated unit is placed from it below, so this is the only
		// thing that moves one: the client renders the authority's own timeline from a little behind it instead of chasing
		// the newest description it has.
		GameContext.SnapshotTimeline.Advance(fDelta);

		// Everything the map asked this screen to show is shown from the same clock as the units it is about, so an
		// effect and the unit that produced it are drawn at the same moment rather than a presentation delay apart.
		FlushPresentationForCursor();

		// A route the match never answered is taken back before the planning pass sees it, so a click that moved nothing
		// stops drawing rather than being planned again every tick.
		ExpirePreviewRoutes();

		// Routes are planned for the units this client controls and none of them are walked: what a unit is drawn doing
		// comes from the descriptions below, and this only gives it a path to draw and a direction to face.
		GameContext.SimulationService.StepMovementPlanOnly(fDelta);

		UpdateClientWorldState(fDelta);

		var query = QueryCache.AllLatestDescriptionQuery;
		GameContext.SimulationService.SetDelta(fDelta);
		GameContext.World.Query(in query, GameContext.SimulationService.PresentationQueryDelegate);
		UpdateVisualNodesFromEcs(fDelta);
		UpdateBuildingPreview();

		RecordGameplayTickIfNeeded();
	}

	public void SendSnapshotAck(int sequence, ulong appliedMask) => GameContext.ServerSession?.Acknowledge(sequence, appliedMask);

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
			// A selection the authority named is a server entity id, so it is resolved only through the mapping that keeps
			// the two domains apart. Matching it against the client's own ids instead compared a server id with an
			// unrelated client id, and in the first seconds of a match - where both start low and count up - it selected
			// whatever unit happened to reuse the number. A unit this screen has not been told about yet is selected when
			// its spawn arrives, which is what the pending id is for.
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
