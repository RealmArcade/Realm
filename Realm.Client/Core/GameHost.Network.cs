using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Realm.Ecs;
using Godot;
using MemoryPack;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.MapAPI;

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

	private void QueueClientCommand(string commandType, List<int> unitIds, Vector3 targetPos, int targetEntityId, string argString)
	{
		var targetNumerics = new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z);
		if (GameSettings.MovementPrediction && (commandType == "MoveTo" || commandType == "AttackMove" || commandType == "Move" || commandType == "Gather"))
		{
			GameContext.MovementPredictionService?.ApplyLocalPrediction(GameContext.World, unitIds, targetNumerics);
		}

		var (_, payload) = GameContext.NetworkService.BuildClientCommand(commandType, unitIds, targetNumerics, targetEntityId, argString);
		RpcId(1, nameof(SubmitCommand), payload);
	}

	private void SendUnacknowledgedCommands()
	{
		foreach (var payload in GameContext.NetworkService.GetUnacknowledgedCommandPayloads())
		{
			RpcId(1, nameof(SubmitCommand), payload);
		}
	}

	private void UpdateClientCameraPosition()
	{
		var camera = GetViewport().GetCamera3D();
		if (camera == null) return;
		var pos = new NetworkVector3(camera.GlobalPosition);
		RpcId(1, nameof(UpdateClientCamera), MemoryPackSerializer.Serialize(pos));
	}

	private void UpdateClientWorldState(float fDelta)
	{
		bool isEditorMode = GameContext.SessionState.IsMapEditorMode;
		bool advanceSimulationClock = !_multiplayerActive || Multiplayer.IsServer();
		var res = GameContext.EnvironmentService.UpdateClientWorldStateDomain(fDelta, isEditorMode, advanceSimulationClock);
		if (res.UpdateVisuals)
		{
			UpdateClientWorldStateViewLogic(res.Progress);
		}
	}

	private void UpdateClientWorldStateViewLogic(float progress)
	{
		UpdateDayNightVisuals(progress);
	}

	private void UpdateClientTick(float fDelta)
	{
		CapturePreviousStateSnapshots();

		_commandSendTimer += fDelta;
		if (_commandSendTimer >= 0.05f)
		{
			_commandSendTimer = 0f;
			UpdateClientTickViewLogic();
		}

		GameContext.NetworkService.UpdateClientTickDomain();

		if (GameSettings.MovementPrediction && GameContext.World != null)
		{
			int currentTickSeq = (int)(Time.GetTicksMsec() / 33);
			GameContext.MovementPredictionService?.RecordTick(currentTickSeq, GameContext.World);
		}

		UpdateClientWorldState(fDelta);

		var query = QueryCache.AllInterpolationTargetQuery;
		GameContext.SimulationService.SetDelta(fDelta);
		GameContext.World.Query(in query, GameContext.SimulationService.InterpolationQueryDelegate);
		UpdateVisualNodesFromEcs(fDelta);
		UpdateBuildingPreview();
	}

	private void UpdateClientTickViewLogic()
	{
		SendUnacknowledgedCommands();
		UpdateClientCameraPosition();
	}

	private void StopUnitVelocities(IEnumerable<int> entityIds)
	{
		foreach (int entityId in entityIds)
		{
			foreach (var unit in AllUnits)
			{
				if (unit.Entity.Id == entityId)
				{
					unit.Velocity = Vector3.Zero;
					break;
				}
			}
		}
	}

	private void ProcessServerCommandBuild(ServerCommandResult result)
	{
		var stringUnitRegistry = new Dictionary<string, UnitMetadata>();
		foreach (var kvp in UnitRegistry) stringUnitRegistry[kvp.Key.ToString()] = kvp.Value;
		foreach (var kvp in BuildingRegistry) stringUnitRegistry[kvp.Key.ToString()] = kvp.Value;
		var longPeerMap = new Dictionary<long, Entity>();
		foreach (var kvp in _peerIdToPlayerEntityMap) longPeerMap[kvp.Key] = kvp.Value;

		Entity buildWorkerEntity = Entity.Null;
		if (result.BuildWorkerServerEntityId >= 0)
		{
			buildWorkerEntity = GameContext.NetworkService.FindServerEntity(result.BuildWorkerServerEntityId, AllUnits);
			if (buildWorkerEntity == Entity.Null || !GameContext.World.IsAlive(buildWorkerEntity))
			{
				GD.PushWarning($"[Build] Rejected '{result.BuildUnitType}': worker server entity {result.BuildWorkerServerEntityId} is not a live unit on the host.");
				return;
			}
		}

		var domainRes = GameContext.NetworkService.ProcessServerCommandBuildDomain(result, stringUnitRegistry, longPeerMap, buildWorkerEntity);
		if (!domainRes.Success)
		{
			GD.PushWarning($"[Build] Rejected '{result.BuildUnitType}': build metadata is missing from the unit registry or the worker is not owned by the commanding peer.");
			return;
		}

		var godotPos = new Vector3(domainRes.BuildPosition.X, domainRes.BuildPosition.Y, domainRes.BuildPosition.Z);
		if (buildWorkerEntity != Entity.Null)
		{
			AssignBuildTaskToWorker(buildWorkerEntity, domainRes.BuildUnitType, domainRes.BuildPosition);
			GD.Print($"[Build] Worker {buildWorkerEntity.Id} started constructing '{domainRes.BuildUnitType}' at {domainRes.BuildPosition}.");
			return;
		}

		var bldEntity = CreateEcsUnit(domainRes.BuildUnitType, domainRes.Name, domainRes.MaxHp, domainRes.Damage, domainRes.Range, domainRes.Armor, 0f, godotPos, domainRes.PlayerOwner);
		ProcessServerCommandBuildViewLogic(bldEntity, domainRes.BuildUnitType, domainRes.TargetModel, godotPos, domainRes.PlayerOwner);
	}

	private void ProcessServerCommandBuildViewLogic(Entity bldEntity, string buildUnitType, string targetModel, Vector3 buildPosition, PlayerEntity playerOwner)
	{
		string modelPath = GetFallbackModelPath(targetModel, true);
		int playerIndex = (playerOwner.Value != Entity.Null && GameContext.World.Has<UnitOwnerPlayer>(playerOwner.Value))
			? GameContext.World.Get<UnitOwnerPlayer>(playerOwner.Value).PlayerIndex
			: 0;
		SpawnUnit3D(bldEntity, buildUnitType, modelPath, buildPosition, true, playerIndex);
	}

	private UnitId GetSpellCaster(NetworkCommand cmd)
	{
		var casterEntity = GameContext.NetworkService.GetSpellCasterEntityDomain(cmd, AllUnits);
		return casterEntity != Entity.Null ? (UnitId)casterEntity.Id : UnitId.Invalid;
	}

	private void ApplySpellEffect(string spellId, Vector3 position, Entity casterEnt)
	{
		var pos = new System.Numerics.Vector3(position.X, position.Y, position.Z);
		GameContext.SimulationService.ApplySpellEffectDomain(spellId, pos, casterEnt);
	}

	private void BroadcastSpellEffect(string spellId, Vector3 position)
	{
		var peers = GameContext.NetworkService.GetPeersToBroadcastSpellEffect(_localPeerId);
		foreach (var peerId in peers)
		{
			QueueOrSendPacket(peerId, nameof(PlaySpellEffect), spellId, position);
		}
	}

	private readonly Dictionary<int, int> _lastAppliedCommandIdPerPeer = new();

	private bool IsDuplicateCommand(int peerId, int commandId)
	{
		if (commandId <= 0) return false;

		if (_lastAppliedCommandIdPerPeer.TryGetValue(peerId, out int lastAppliedCommandId) && commandId <= lastAppliedCommandId) return true;

		_lastAppliedCommandIdPerPeer[peerId] = commandId;
		return false;
	}

	private void ProcessServerCommandSpell(NetworkCommand cmd, ServerCommandResult result)
	{
		if (!result.NeedsSpellEffect) return;

		string spellId = result.SpellId;
		Vector3 position = result.SpellPosition;
		var caster = GetSpellCaster(cmd);
		var casterEnt = GameContext.SimulationService.TryGetEntity(caster, out var ce) ? ce : Entity.Null;

		OnSpellCast?.Invoke(caster, spellId, new System.Numerics.Vector3(position.X, position.Y, position.Z));

		ApplySpellEffect(spellId, position, casterEnt);
		GameContext.InGameHud?.RefreshUI(SelectedUnits);
		BroadcastSpellEffect(spellId, position);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void SubmitCommand(byte[] payload)
	{
		if (!Multiplayer.IsServer()) return;
		var sw = Stopwatch.StartNew();
		int senderId = Multiplayer.GetRemoteSenderId();
		var cmd = MemoryPackSerializer.Deserialize<NetworkCommand>(payload);

		if (IsDuplicateCommand(senderId, cmd.CommandId))
		{
			RpcId(senderId, nameof(AcknowledgeCommand), cmd.CommandId);
			return;
		}

		var result = GameContext.NetworkService.ExecuteServerCommand(senderId, cmd, AllUnits, AllProps);

		StopUnitVelocities(result.StopVelocityEntityIds);
		StopUnitVelocities(result.HoldVelocityEntityIds);

		ProcessServerCommandBuild(result);
		ProcessServerCommandSpell(cmd, result);

		RpcId(senderId, nameof(AcknowledgeCommand), cmd.CommandId);
		sw.Stop();
		float responseCpuMs = (float)sw.Elapsed.TotalMilliseconds;
		float adjustedResponseMs = responseCpuMs + GameContext.PerformanceTrackingService.TrackerLastTickDelay;
		if (GameContext.PerformanceTrackingService.TrackerApiDurations != null)
		{
			GameContext.PerformanceTrackingService.TrackerApiDurations.Add(adjustedResponseMs);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void AcknowledgeCommand(int commandId)
	{
		GameContext.NetworkService.AcknowledgeCommand(commandId);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void UpdateClientCamera(byte[] payload)
	{
		int senderId = Multiplayer.GetRemoteSenderId();
		var pos = MemoryPackSerializer.Deserialize<NetworkVector3>(payload);
		GameContext.NetworkService.RecordClientCameraPosition(senderId, pos.ToGodot());
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void ReceiveSnapshot(byte[] payload)
	{
		ApplyReceivedSnapshot(payload);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ReceiveBaselineSnapshot(byte[] payload)
	{
		ApplyReceivedSnapshot(payload);
	}

	private void ApplyReceivedSnapshot(byte[] payload)
	{
		if (Multiplayer.IsServer()) return;
		GameContext.NetworkService.RecordSnapshotReceived(payload);


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

		MultiplayerSyncDiagnostics.RecordClientPresentation();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void PlaySpellEffect(string spellId, Vector3 position)
	{
		var def = GetAbilityDefinition(spellId);
		if (def != null)
		{
			GameContext.FXService.SpawnAbilityEffect(this, def, position);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientSpawnArrowProjectile(Vector3 start, Vector3 target, int ownerPlayerIndex = 0)
	{
		GameContext.FXService.SpawnArrowProjectile(this, start, target, ownerPlayerIndex);
	}

	public void RefreshReplicatedOverlayUi()
	{
		GameContext.InGameHud?.InvalidateReplicatedOverlays();
	}

	public void SetReplicatedEntityVisible(Entity entity, bool visible)
	{
		if (!TryGetUnit3D(entity, out var unit) || !GodotObject.IsInstanceValid(unit)) return;
		unit.Visible = visible;
	}

	private int _shadowVerificationTickCounter;

	private void RunShadowClientVerification()
	{
		_shadowVerificationTickCounter++;
		if (_shadowVerificationTickCounter % 60 != 0) return;

		byte[] payload = GameContext.NetworkService.BuildExplicitBaselineSnapshot(1, AllUnits);
		if (payload.Length == 0) return;
		ShadowClientVerifier.VerifyPayload(1, payload, true);
	}

	private void UpdateServerSnapshotTick(float fDelta)
	{
		var snapshots = GameContext.NetworkService.BuildServerSnapshots(_localPeerId, AllUnits);
		foreach (var (peerId, payload, isBaseline) in snapshots)
		{
			ShadowClientVerifier.VerifyPayload(peerId, payload, isBaseline);
			QueueOrSendPacket(peerId, isBaseline ? nameof(ReceiveBaselineSnapshot) : nameof(ReceiveSnapshot), payload);
		}
	}

	private void SpawnUnitFromSnapshot(UnitSnapshot snap)
	{
		var entity = GameContext.NetworkService.SpawnUnitFromSnapshot(snap, GetFallbackModelPath, out string modelPath, out int playerIndex);
		if (entity == Entity.Null) return;
		var unit3D = SpawnUnit3D(entity, snap.UnitId, modelPath, snap.Position.ToGodot(), snap.IsBuilding, playerIndex);
		unit3D.Rotation = new Vector3(0f, snap.RotationY, 0f);
		if (_pendingSelectUnitServerId == snap.EntityId && GodotObject.IsInstanceValid(unit3D))
		{
			ClearSelection();
			SelectUnit(unit3D);
			GameContext.InGameHud?.RefreshUI(SelectedUnits);
			_pendingSelectUnitServerId = -1;
		}
	}

	private void QueueOrSendPacket(int peerId, string funcName, params object[] args)
	{
		if (GameContext.NetworkService.IsSpectatorWithDelay(peerId))
		{
			GameContext.NetworkService.QueueSpectatorDelayedPacket(peerId, funcName, args);
		}
		else
		{
			SendPacketImmediate(peerId, funcName, args);
		}
	}

	private void ProcessDelayedSpectatorPackets()
	{
		var readyPackets = GameContext.NetworkService.FlushReadySpectatorPackets();
		foreach (var packet in readyPackets)
		{
			SendPacketImmediate(packet.PeerId, packet.FunctionName, packet.Arguments);
		}
	}

	private void SendPacketImmediate(int peerId, string funcName, object[] args)
	{
		if (funcName == nameof(ReceiveSnapshot))
		{
			RpcId(peerId, nameof(ReceiveSnapshot), (byte[])args[0]);
		}
		else if (funcName == nameof(ReceiveBaselineSnapshot))
		{
			RpcId(peerId, nameof(ReceiveBaselineSnapshot), (byte[])args[0]);
		}
		else if (funcName == nameof(PlaySpellEffect))
		{
			RpcId(peerId, nameof(PlaySpellEffect), (string)args[0], (Vector3)args[1]);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void NetworkPingMinimap(Vector3 position)
	{
		AddMinimapPing(position);
	}

	public void TogglePauseRequest()
	{
		if (_multiplayerActive)
		{
			RpcId(1, nameof(RequestPause), Multiplayer.GetUniqueId());
		}
		else
		{
			GameContext.NetworkService.ToggleLocalPause();
			UpdatePauseUI();
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RequestPause(int peerId)
	{
		GameContext.NetworkService.ProcessRequestPause(
			peerId,
			Multiplayer.IsServer(),
			(paused, byPeerId, instant) => Rpc(nameof(BroadcastPauseState), paused, byPeerId, instant),
			StartCountdown
		);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RequestToggleReady(int peerId, bool ready)
	{
		GameContext.NetworkService.ProcessRequestToggleReady(
			peerId, ready, Multiplayer.IsServer(),
			CheckAllPlayersReady,
			StartCountdown,
			(serialized) => Rpc(nameof(BroadcastReadyStates), serialized),
			() => Rpc(nameof(BroadcastCountdownCancel))
		);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RequestSetDisallowPause(int peerId, bool disallowed)
	{
		GameContext.NetworkService.ProcessRequestSetDisallowPause(
			peerId, disallowed, Multiplayer.IsServer(), Multiplayer.GetRemoteSenderId(),
			(serialized) => Rpc(nameof(BroadcastDisallowedPausePeers), serialized)
		);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RequestForceResume()
	{
		GameContext.NetworkService.ProcessRequestForceResume(
			Multiplayer.IsServer(), Multiplayer.GetRemoteSenderId(), StartCountdown
		);
	}

	private void StartCountdown(bool forcedByHost)
	{
		GameContext.NetworkService.ProcessStartCountdown(
			forcedByHost,
			(seconds, forced) => Rpc(nameof(BroadcastCountdownState), seconds, forced)
		);
	}

	private bool CheckAllPlayersReady()
	{
		return GameContext.NetworkService.ProcessCheckAllPlayersReady(GameContext.Lobby?.PlayerList);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void BroadcastPauseState(bool paused, int pausedByPeerId, bool instantResume)
	{
		GameContext.NetworkService.ProcessBroadcastPauseState(
			paused, instantResume, UpdatePauseUI
		);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void BroadcastReadyStates(string serializedReadyStates)
	{
		GameContext.NetworkService.ProcessBroadcastReadyStates(
			serializedReadyStates, UpdatePauseUI
		);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void BroadcastDisallowedPausePeers(string serializedDisallowed)
	{
		GameContext.NetworkService.ProcessBroadcastDisallowedPausePeers(
			serializedDisallowed, UpdatePauseUI
		);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void BroadcastCountdownState(int seconds, bool forcedByHost)
	{
		GameContext.NetworkService.ProcessBroadcastCountdownState(
			seconds, forcedByHost, UpdatePauseUI
		);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void BroadcastCountdownCancel()
	{
		GameContext.NetworkService.BroadcastCountdownCancelDomain();
		UpdatePauseUI();
	}

	public void GetPlayerReadyState(int peerId, out bool ready)
	{
		GameContext.NetworkService.GetPlayerReadyStateDomain(peerId, out ready);
	}

	public void GetPlayerDisallowPause(int peerId, out bool disallowed)
	{
		GameContext.NetworkService.GetPlayerDisallowPauseDomain(peerId, out disallowed);
	}

	private void UpdatePauseUI()
	{
		GameContext.InGameHud?.UpdatePauseUI();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
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

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void SyncWeatherRpc(string weatherType)
	{
		GameContext.EnvironmentService?.SetCurrentWeather(weatherType);
		GameContext.InGameHud?.ApplyWeatherEffects(weatherType);
	}

	private void SendReconnectedBaselineSnapshot(int newPeerId)
	{
		var baselinePayload = GameContext.NetworkService.BuildExplicitBaselineSnapshot(newPeerId, AllUnits);
		if (baselinePayload != null && baselinePayload.Length > 0)
		{
			RpcId(newPeerId, nameof(ReceiveSnapshot), baselinePayload);
		}
	}

	private void SyncReconnectedCountdown(int newPeerId)
	{
		if (GameContext.NetworkService.TryGetReconnectedCountdown(out float duration, out string text))
		{
			RpcId(newPeerId, nameof(ClientStartCountdownTimer), duration, text);
		}
	}

	private void RegisterReconnectedAbilities(int newPeerId)
	{
		foreach (var kvp in GameContext.RegistryService.AbilityDefinitions)
		{
			RpcId(newPeerId, nameof(ClientRegisterAbility), kvp.Key, kvp.Value.DisplayName, kvp.Value.Tooltip, kvp.Value.IconPath ?? "", kvp.Value.IsInstant);
		}
	}

	private void SyncReconnectedPauseState(int newPeerId)
	{
		if (!GameContext.NetworkService.IsPaused) return;

		RpcId(newPeerId, nameof(BroadcastPauseState), true, 1, false);
		var serializedReady = JsonSerializer.Serialize(GameContext.NetworkService.PlayerReadyStates);
		RpcId(newPeerId, nameof(BroadcastReadyStates), serializedReady);
	}

	public void HandlePeerReconnected(int oldPeerId, int newPeerId, int slot)
	{
		if (!Multiplayer.IsServer()) return;

		GD.Print($"[GameHost] HandlePeerReconnected: OldPeer={oldPeerId}, NewPeer={newPeerId}, Slot={slot}");

		Entity playerEntity = GameContext.NetworkService.ResolveReconnectedPlayerEntity(oldPeerId, newPeerId, _peerIdToPlayerEntityMap);
		GameContext.NetworkService.UpdateReconnectedNetworkMapping(oldPeerId, newPeerId, playerEntity);
		SendReconnectedBaselineSnapshot(newPeerId);
		SyncReconnectedCountdown(newPeerId);
		RegisterReconnectedAbilities(newPeerId);
		SyncReconnectedPauseState(newPeerId);
		SyncReconnectedCameraBounds(newPeerId, slot);
		SyncReconnectedHudVisibility(newPeerId, slot);
	}

	private void SyncReconnectedCameraBounds(int newPeerId, int slot)
	{
		if (_playerCameraBoundsOverrides.TryGetValue(slot, out var bounds))
		{
			RpcId(newPeerId, nameof(ClientSetCameraBounds), bounds.MinX, bounds.MinZ, bounds.MaxX, bounds.MaxZ);
		}
	}

	private void SyncReconnectedHudVisibility(int newPeerId, int slot)
	{
		if (_playerHudVisibilityOverrides.TryGetValue(slot, out var dict))
		{
			foreach (var kvp in dict)
			{
				RpcId(newPeerId, nameof(ClientSetHudElementVisible), (int)kvp.Key, kvp.Value);
			}
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientRegisterAbility(string abilityId, string displayName, string tooltip, string iconPath, bool isInstant)
	{
		if (GameContext.RegistryService.RegisterAbility(abilityId, displayName, tooltip, iconPath, isInstant))
		{
			GameContext.InGameHud?.RefreshUI(SelectedUnits);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientShowFeedbackText(string text, Vector3 color)
	{
		if (GameContext.InGameHud != null)
		{
			var gColor = new Color(color.X, color.Y, color.Z);
			Callable.From(() => GameContext.InGameHud?.ShowFeedbackText(text, gColor)).CallDeferred();
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void ClientCreateFloatingText(string text, Vector3 position, Vector3 color, float duration)
	{
		CreateFloatingTextInternal(text, position, new Color(color.X, color.Y, color.Z), duration);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientStartCountdownTimer(float duration, string label)
	{
		Callable.From(() => GameContext.InGameHud?.StartCountdownTimer(duration, label)).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientStopCountdownTimer()
	{
		Callable.From(() => GameContext.InGameHud?.StopCountdownTimer()).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientPanCameraTo(Vector3 position, float duration)
	{
		Callable.From(() => { PanCameraInternal(position, duration); }).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientShakeCamera(float intensity, float duration)
	{
		ShakeCameraInternal(intensity, duration);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientSetCameraBounds(float minX, float minZ, float maxX, float maxZ)
	{
		Callable.From(() => SetCameraBoundsInternal(minX, minZ, maxX, maxZ)).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientResetCameraBounds()
	{
		Callable.From(() => ResetCameraBoundsInternal()).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientSetHudElementVisible(int elementInt, bool visible)
	{
		Callable.From(() =>
		{
			HudElement element = (HudElement)elementInt;
			SetHudElementVisibleInternal(element, visible);
		}).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientSelectUnit(int serverEntityId)
	{
		Callable.From(() =>
		{
			Entity targetEntity = Entity.Null;
			if (!TryGetLocalEntity(serverEntityId, out targetEntity))
			{
				foreach (var u in AllUnits)
				{
					if (GodotObject.IsInstanceValid(u) && u.Entity.Id == serverEntityId)
					{
						targetEntity = u.Entity;
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

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientClearSelection()
	{
		ClearSelectionInternal();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientPingMinimap(Vector3 position)
	{
		Callable.From(() => AddMinimapPing(position)).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientPlayWarningSound()
	{
		Callable.From(() => GameContext.AudioService?.PlayWarningSound()).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientPlayClickSound()
	{
		Callable.From(() => GameContext.AudioService?.PlayClickSound()).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientStartBuildingPlacement(string unitTypeId)
	{
		Callable.From(() => EnterBuildingPlacement(unitTypeId)).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientGameOver(bool isVictory)
	{
		GameContext.NetworkService.SetGameOver();
		Callable.From(() => GameContext.UserInterface?.TransitionTo(GameScreen.GameOver, isVictory)).CallDeferred();
	}

	public void OnClientReconnected(int slot)
	{
		_localPeerId = Multiplayer.GetUniqueId();
		GameContext.NetworkService.LocalPeerId = _localPeerId;
		GameContext.NetworkService.MarkClientEnteredMultiplayer();
		GameContext.NetworkService.ResetReconnectionState();

		Entity reconnectedEntity = Entity.Null;
		if (GameContext.Lobby != null && GameContext.Lobby.PlayerList.Count > 0)
		{
			reconnectedEntity = GameContext.NetworkService.AssignReconnectedPlayerEntity(slot, GameContext.Lobby.PlayerList, _localPeerId, _peerIdToPlayerEntityMap);
		}

		GameContext.NetworkService.UpdateReconnectedClientMapping(reconnectedEntity, _localPeerId);

		if (IsMultiplayerActive() && !IsMultiplayerServerActive())
		{
			RpcId(1, nameof(ServerRequestDynamicPathingSync));
		}

		GameContext.InGameHud?.RefreshUI(SelectedUnits);
		GD.Print($"[GameHost] OnClientReconnected completed for LocalPeerId={_localPeerId}, Slot={slot}");
	}

	private readonly List<(float minX, float minZ, float maxX, float maxZ, int flags, bool blocked)> _dynamicPathingOverrides = new();
	private int _regionPathingSequence;
	private int _appliedRegionPathingSequence;

	private int NextRegionPathingSequence()
	{
		_regionPathingSequence++;
		_appliedRegionPathingSequence = _regionPathingSequence;
		return _regionPathingSequence;
	}

	public void ApplyRegionPathingInternal(float minX, float minZ, float maxX, float maxZ, int flags, bool blocked)
	{
		_dynamicPathingOverrides.Add((minX, minZ, maxX, maxZ, flags, blocked));
		var world = GameContext.World;
		var worldEntity = ECSContext.WorldEntity;
		if (world != null && world.IsAlive(worldEntity) && world.Has<TerrainState>(worldEntity))
		{
			ref var state = ref world.Get<TerrainState>(worldEntity);
			ECSContext.TerrainNavMeshService?.SetRegionPathing(ref state, minX, minZ, maxX, maxZ, flags, blocked);
			world.Set(worldEntity, state);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientUpdateRegionPathing(float minX, float minZ, float maxX, float maxZ, int flags, bool blocked, int sequence)
	{
		if (sequence <= _appliedRegionPathingSequence) return;

		_appliedRegionPathingSequence = sequence;
		ApplyRegionPathingInternal(minX, minZ, maxX, maxZ, flags, blocked);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ServerRequestDynamicPathingSync()
	{
		int senderId = Multiplayer.GetRemoteSenderId();
		if (senderId <= 1) return;

		var sb = new System.Text.StringBuilder();
		for (int i = 0; i < _dynamicPathingOverrides.Count; i++)
		{
			var o = _dynamicPathingOverrides[i];
			if (i > 0) sb.Append(';');
			sb.Append($"{o.minX:F2},{o.minZ:F2},{o.maxX:F2},{o.maxZ:F2},{o.flags},{(o.blocked ? 1 : 0)}");
		}
		RpcId(senderId, nameof(ClientSyncDynamicPathingOverrides), _regionPathingSequence, sb.ToString());
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientSyncDynamicPathingOverrides(int sequence, string serializedData)
	{
		if (sequence > _appliedRegionPathingSequence) _appliedRegionPathingSequence = sequence;
		if (string.IsNullOrEmpty(serializedData)) return;
		var parts = serializedData.Split(';', StringSplitOptions.RemoveEmptyEntries);
		foreach (var part in parts)
		{
			var tokens = part.Split(',');
			if (tokens.Length >= 6 &&
			    float.TryParse(tokens[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x1) &&
			    float.TryParse(tokens[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z1) &&
			    float.TryParse(tokens[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x2) &&
			    float.TryParse(tokens[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z2) &&
			    int.TryParse(tokens[4], out int flags) &&
			    int.TryParse(tokens[5], out int blockedInt))
			{
				ApplyRegionPathingInternal(x1, z1, x2, z2, flags, blockedInt != 0);
			}
		}
	}
}