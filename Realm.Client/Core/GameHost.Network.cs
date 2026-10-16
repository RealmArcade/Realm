using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using Arch.Core;
using Godot;
using MemoryPack;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Resources;
using System.Linq;
using Realm.Ecs.Services;
using Realm.MapAPI;

namespace Realm.Client.Core;

public partial class GameHost
{
	private int _pendingSelectUnitServerId = -1;
	public int GetOwnerPeerId(Entity unitEntity) => GameContext.NetworkService.GetOwnerPeerId(unitEntity);

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

	private void QueueClientCommand(string commandType, List<int> unitIds, Vector3 targetPos, int targetEntityId, string argString)
	{
		var targetNumerics = new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z);
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
		bool isEditorMode = GameContext.EditorService.IsMapEditorMode;
		var res = GameContext.EnvironmentService.UpdateClientWorldStateDomain(fDelta, isEditorMode);
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
		_commandSendTimer += fDelta;
		if (_commandSendTimer >= 0.05f)
		{
			_commandSendTimer = 0f;
			UpdateClientTickViewLogic();
		}

		GameContext.NetworkService.UpdateClientTickDomain();

		UpdateClientWorldState(fDelta);

		var query = QueryCache.AllInterpolationTargetQuery;
		GameContext.SimulationService.SetDelta(fDelta);
		GameContext.World.Query(in query, GameContext.SimulationService.InterpolationQueryDelegate);
		UpdateVisualNodesFromEcs(fDelta);
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
		var longPeerMap = new Dictionary<long, Entity>();
		foreach (var kvp in _peerIdToPlayerEntityMap) longPeerMap[kvp.Key] = kvp.Value;

		var domainRes = GameContext.NetworkService.ProcessServerCommandBuildDomain(result, stringUnitRegistry, longPeerMap);
		if (domainRes.Success)
		{
			var godotPos = new Vector3(domainRes.BuildPosition.X, domainRes.BuildPosition.Y, domainRes.BuildPosition.Z);
			var bldEntity = CreateEcsUnit(domainRes.BuildUnitType, domainRes.Name, domainRes.MaxHp, domainRes.Damage, domainRes.Range, domainRes.Armor, 0f, godotPos, domainRes.PlayerOwner);
			ProcessServerCommandBuildViewLogic(bldEntity, domainRes.BuildUnitType, domainRes.TargetModel, godotPos);
		}
	}

	private void ProcessServerCommandBuildViewLogic(Entity bldEntity, string buildUnitType, string targetModel, Vector3 buildPosition)
	{
		string modelPath = GetFallbackModelPath(targetModel, true);
		SpawnUnit3D(bldEntity, buildUnitType, modelPath, buildPosition, true, false);
	}

	private IUnit GetSpellCaster(NetworkCommand cmd)
	{
		var casterEntity = GameContext.NetworkService.GetSpellCasterEntityDomain(cmd, AllUnits);
		return casterEntity != Entity.Null ? GameContext.SimulationService.GetUnitWrapper(casterEntity) : null;
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

	private void ProcessServerCommandSpell(NetworkCommand cmd, ServerCommandResult result)
	{
		if (!result.NeedsSpellEffect) return;

		string spellId = result.SpellId;
		Vector3 position = result.SpellPosition;
		var caster = GetSpellCaster(cmd);
		var casterEnt = caster != null ? ((IEcsEntityWrapper)caster).Entity : Entity.Null;

		OnSpellCast?.Invoke(caster, spellId, new System.Numerics.Vector3(position.X, position.Y, position.Z));

		ApplySpellEffect(spellId, position, casterEnt);
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
		BroadcastSpellEffect(spellId, position);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void SubmitCommand(byte[] payload)
	{
		if (!Multiplayer.IsServer()) return;
		var sw = Stopwatch.StartNew();
		int senderId = Multiplayer.GetRemoteSenderId();
		var cmd = MemoryPackSerializer.Deserialize<NetworkCommand>(payload);
		GD.Print($"[SERVER_CMD_RECEIVED] Peer={senderId} CommandType={cmd.CommandType} Units={string.Join(",", cmd.UnitEntityIds)} Target={cmd.TargetPosition.ToGodot()}");

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
		if (Multiplayer.IsServer()) return;
		GameContext.NetworkService.RecordSnapshotReceived(payload);

		foreach (var snap in GameContext.NetworkService.FlushPendingUnitSpawns())
		{
			SpawnUnitFromSnapshot(snap);
		}

		foreach (var localEntity in GameContext.NetworkService.FlushPendingUnitKills())
		{
			if (TryGetUnit3D(localEntity, out var unit3D))
			{
				Callable.From(() => KillUnitDeferred(unit3D)).CallDeferred();
			}
		}
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
	public void ClientSpawnArrowProjectile(Vector3 start, Vector3 target)
	{
		GameContext.FXService.SpawnArrowProjectile(this, start, target);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void SyncPlayerResources(float gold, float wood, float stone)
	{
		((IGameAPI)this).Gold = gold;
		((IGameAPI)this).Wood = wood;
		((IGameAPI)this).Stone = stone;
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void SyncProductionQueue(int castleServerEntityId, string[] unitIds, float currentProgress, float buildTime)
	{
		GameContext.NetworkService.SyncProductionQueue(castleServerEntityId, unitIds, currentProgress, buildTime);
	}

	private void SyncServerPlayerResources()
	{
		var resources = GameContext.NetworkService.GetServerPlayerResources(_peerIdToPlayerEntityMap, _localPeerId);
		foreach (var res in resources)
		{
			RpcId(res.PeerId, nameof(SyncPlayerResources), res.Gold, res.Wood, res.Stone);
		}
	}

	private void SyncServerProductionQueues()
	{
		var entityList = new List<Entity>();
		foreach (var unit in AllUnits)
		{
			entityList.Add(unit.Entity);
		}

		var queues = GameContext.NetworkService.GetServerProductionQueues(entityList);
		foreach (var q in queues)
		{
			foreach (var kvp in _peerIdToPlayerEntityMap)
			{
				int peerId = kvp.Key;
				if (peerId != _localPeerId)
				{
					RpcId(peerId, nameof(SyncProductionQueue), q.EntityId, q.UnitIds, q.CurrentProgress, q.BuildTime);
				}
			}
		}
	}

	private void UpdateServerSnapshotTick(float fDelta)
	{
		var snapshots = GameContext.NetworkService.BuildServerSnapshots(_localPeerId, AllUnits);
		foreach (var (peerId, payload) in snapshots)
		{
			QueueOrSendPacket(peerId, nameof(ReceiveSnapshot), payload);
		}

		SyncServerPlayerResources();
		SyncServerProductionQueues();
	}

	private void SpawnUnitFromSnapshot(UnitSnapshot snap)
	{
		var entity = GameContext.NetworkService.SpawnUnitFromSnapshot(snap, GetFallbackModelPath, out string modelPath, out bool isEnemy);
		if (entity == Entity.Null) return;
		int playerSlot = 0;
		if (LobbyManager.Instance != null && LobbyManager.Instance.PlayerList.Count > 0)
		{
			var matchPlayer = LobbyManager.Instance.PlayerList.Find(x => x.PeerId == snap.OwnerPlayerEntityId);
			if (matchPlayer != null) playerSlot = matchPlayer.Slot;
		}
		var unit3D = SpawnUnit3D(entity, snap.UnitId, modelPath, snap.Position.ToGodot(), snap.IsBuilding, isEnemy, false, playerSlot);
		if (_pendingSelectUnitServerId == snap.EntityId && GodotObject.IsInstanceValid(unit3D))
		{
			ClearSelection();
			SelectUnit(unit3D);
			InGameHUD.Instance?.RefreshUI(SelectedUnits);
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
		return GameContext.NetworkService.ProcessCheckAllPlayersReady(LobbyManager.Instance?.PlayerList);
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
		InGameHUD.Instance?.UpdatePauseUI();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void SyncWorldState(float gameElapsedTime, int timeOfDayIndex, float timeOfDayTimer, bool dayNightCycleEnabled)
	{
		bool isWorldAlive = GameContext.EnvironmentService.SyncWorldStateDomain(gameElapsedTime, timeOfDayIndex, timeOfDayTimer, dayNightCycleEnabled);
		if (isWorldAlive)
		{
			if (dayNightCycleEnabled && GameContext.EnvironmentService.TimeOfDayCycleDuration > 0)
			{
				float progress = timeOfDayTimer / GameContext.EnvironmentService.TimeOfDayCycleDuration;
				if (!GameContext.EditorService.IsMapEditorMode)
				{
					UpdateDayNightVisuals(progress);
				}
			}
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void SyncEnvironmentPresetRpc(string presetId, float durationSeconds)
	{
		if (durationSeconds <= 0.001f)
		{
			GameContext.EnvironmentService?.ApplyPresetById(this, presetId);
		}
		else
		{
			GameContext.EnvironmentService?.TransitionToPreset(this, presetId, durationSeconds);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void SyncWeatherRpc(string weatherType)
	{
		GameContext.EnvironmentService?.SetCurrentWeather(weatherType);
		InGameHUD.Instance?.ApplyWeatherEffects(weatherType);
	}

	private void SyncReconnectedWorldState(int newPeerId)
	{
		if (GameContext.NetworkService.TryGetReconnectedWorldState(out float gameElapsedTime, out int timeOfDayIndex, out float timeOfDayTimer, out bool dayNightCycleEnabled))
		{
			RpcId(newPeerId, nameof(SyncWorldState), gameElapsedTime, timeOfDayIndex, timeOfDayTimer, dayNightCycleEnabled);
		}
	}

	private void SendReconnectedBaselineSnapshot(int newPeerId)
	{
		var baselinePayload = GameContext.NetworkService.BuildExplicitBaselineSnapshot(newPeerId, AllUnits);
		if (baselinePayload != null && baselinePayload.Length > 0)
		{
			RpcId(newPeerId, nameof(ReceiveSnapshot), baselinePayload);
		}
	}

	private void SyncReconnectedPlayerResources(int newPeerId, Entity playerEntity)
	{
		if (GameContext.NetworkService.TryGetReconnectedPlayerResources(playerEntity, out float gold, out float wood, out float stone))
		{
			RpcId(newPeerId, nameof(SyncPlayerResources), gold, wood, stone);
		}
	}

	private void SyncReconnectedProductionQueues(int newPeerId)
	{
		var queues = GameContext.NetworkService.GetServerProductionQueues(AllUnits.Where(GodotObject.IsInstanceValid).Select(u => u.Entity));
		foreach (var (entityId, unitIds, currentProgress, buildTime) in queues)
		{
			RpcId(newPeerId, nameof(SyncProductionQueue), entityId, unitIds, currentProgress, buildTime);
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
		SyncReconnectedWorldState(newPeerId);
		SendReconnectedBaselineSnapshot(newPeerId);
		SyncReconnectedPlayerResources(newPeerId, playerEntity);
		SyncReconnectedProductionQueues(newPeerId);
		SyncReconnectedCountdown(newPeerId);
		RegisterReconnectedAbilities(newPeerId);
		SyncReconnectedPauseState(newPeerId);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientRegisterAbility(string abilityId, string displayName, string tooltip, string iconPath, bool isInstant)
	{
		if (GameContext.RegistryService.RegisterAbility(abilityId, displayName, tooltip, iconPath, isInstant))
		{
			InGameHUD.Instance?.RefreshUI(SelectedUnits);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientShowFeedbackText(string text, Vector3 color)
	{
		if (InGameHUD.Instance != null)
		{
			var gColor = new Color(color.X, color.Y, color.Z);
			Callable.From(() => InGameHUD.Instance?.ShowFeedbackText(text, gColor)).CallDeferred();
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
		Callable.From(() => InGameHUD.Instance?.StartCountdownTimer(duration, label)).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientStopCountdownTimer()
	{
		Callable.From(() => InGameHUD.Instance?.StopCountdownTimer()).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientPanCameraTo(Vector3 position, float duration)
	{
		Callable.From(() => { PanCameraInternal(position, duration); }).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientSelectUnit(int serverEntityId)
	{
		Callable.From(() =>
		{
			if (TryGetLocalEntity(serverEntityId, out var localEnt) && TryGetUnit3D(localEnt, out var unit3D))
			{
				ClearSelection();
				SelectUnit(unit3D);
				InGameHUD.Instance?.RefreshUI(SelectedUnits);
			}
			else
			{
				_pendingSelectUnitServerId = serverEntityId;
			}
		}).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientGameOver(bool isVictory)
	{
		GameContext.NetworkService.SetGameOver();
		Callable.From(() => UIManager.Instance?.TransitionTo(GameScreen.GameOver, isVictory)).CallDeferred();
	}

	public void OnClientReconnected(int slot)
	{
		_localPeerId = Multiplayer.GetUniqueId();
		GameContext.NetworkService.LocalPeerId = _localPeerId;
		GameContext.NetworkService.MarkClientEnteredMultiplayer();
		GameContext.NetworkService.ResetReconnectionState();

		if (LobbyManager.Instance != null && LobbyManager.Instance.PlayerList.Count > 0)
		{
			_playerEntity = GameContext.NetworkService.AssignReconnectedPlayerEntity(slot, LobbyManager.Instance.PlayerList, _localPeerId, _peerIdToPlayerEntityMap);
		}

		GameContext.NetworkService.UpdateReconnectedClientMapping(_playerEntity, _enemyPlayerEntity, _localPeerId);

		InGameHUD.Instance?.RefreshUI(SelectedUnits);
		GD.Print($"[GameHost] OnClientReconnected completed for LocalPeerId={_localPeerId}, Slot={slot}");
	}
}