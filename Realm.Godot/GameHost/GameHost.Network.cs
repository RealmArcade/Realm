using System;
using Arch.Core;
using Godot;
using MemoryPack;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.MapAPI;
using System.Collections.Generic;

public partial class GameHost
{
	private NetworkService _networkService;
	public bool IsPaused { get; private set; } = false;
	public int ResumeCountdownSeconds { get; private set; } = -1;
	private float _resumeCountdownTimer = 0f;
	private bool _countdownForcedByHost = false;
	private readonly System.Collections.Generic.Dictionary<int, bool> _playerReadyStates = new();
	private readonly System.Collections.Generic.Dictionary<int, bool> _disallowedPausePeers = new();

	public int GetOwnerPeerId(Entity unitEntity) => _networkService.GetOwnerPeerId(unitEntity);

	private int GetServerEntityId(Entity localEntity) =>
		_networkService.GetServerEntityId(localEntity.Id);

	public bool TryGetLocalEntity(int serverEntityId, out Entity localEntity)
	{
		if (_networkService == null)
		{
			localEntity = Entity.Null;
			return false;
		}
		return _networkService.TryGetLocalEntity(serverEntityId, out localEntity);
	}

	public void SetBackupResources(float gold, float wood, float stone) =>
		_networkService.SetBackupResources(gold, wood, stone);

	public void KillUnitDeferredExternal(Unit3D unit)
	{
		CallDeferred("KillUnitDeferred", unit);
	}

	private void QueueClientCommand(string commandType, List<int> unitIds, Vector3 targetPos, int targetEntityId, string argString)
	{
		var targetNumerics = new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z);
		var (_, payload) = _networkService.BuildClientCommand(commandType, unitIds, targetNumerics, targetEntityId, argString);
		RpcId(1, nameof(SubmitCommand), payload);
	}

	private void UpdateClientTick(float fDelta)
	{
		_commandSendTimer += fDelta;
		if (_commandSendTimer >= 0.05f)
		{
			_commandSendTimer = 0f;
			foreach (var payload in _networkService.GetUnacknowledgedCommandPayloads())
			{
				RpcId(1, nameof(SubmitCommand), payload);
			}
			var camera = GetViewport().GetCamera3D();
			if (camera != null)
			{
				var pos = new NetworkVector3(camera.GlobalPosition);
				RpcId(1, nameof(UpdateClientCamera), MemoryPackSerializer.Serialize(pos));
			}
		}
		EcsWorld?.Mutate<Realm.Ecs.Components.Core.NetworkState>(_worldEntity, (ref Realm.Ecs.Components.Core.NetworkState netState) =>
			netState.DynamicInterpolationFactor = _networkService.ComputeDynamicInterpolationFactor());

		if (EcsWorld != null && EcsWorld.IsAlive(_worldEntity) && EcsWorld.Has<WorldState>(_worldEntity))
		{
			var state = EcsWorld.Get<WorldState>(_worldEntity);
			float elapsed = state.GameElapsedTime + fDelta;
			float timer = state.TimeOfDayTimer;
			int index = state.TimeOfDayIndex;

			if (state.DayNightCycleEnabled)
			{
				timer += fDelta;
				if (timer >= TimeOfDayCycleDuration)
				{
					timer -= TimeOfDayCycleDuration;
				}

				float progress = timer / TimeOfDayCycleDuration;
				index = (int)(progress * 4f) % 4;

				if (!IsMapEditorMode)
				{
					UpdateDayNightVisuals(progress);
				}
			}
			EcsWorld.Set(_worldEntity, new WorldState(elapsed, index, timer, state.DayNightCycleEnabled));
		}

		var query = Realm.Ecs.Common.QueryCache.AllInterpolationTargetQuery;
		_simulationService.SetDelta(fDelta);
		EcsWorld.Query(in query, _simulationService.InterpolationQueryDelegate);
		UpdateVisualNodesFromEcs(fDelta);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void SubmitCommand(byte[] payload)
	{
		if (!Multiplayer.IsServer()) return;
		var sw = System.Diagnostics.Stopwatch.StartNew();
		int senderId = Multiplayer.GetRemoteSenderId();
		var cmd = MemoryPackSerializer.Deserialize<NetworkCommand>(payload);
		GD.Print($"[SERVER_CMD_RECEIVED] Peer={senderId} CommandType={cmd.CommandType} Units={string.Join(",", cmd.UnitEntityIds)} Target={cmd.TargetPosition.ToGodot()}");

		var result = _networkService.ExecuteServerCommand(senderId, cmd, AllUnits, AllProps);

		foreach (int entityId in result.StopVelocityEntityIds)
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

		foreach (int entityId in result.HoldVelocityEntityIds)
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

		if (result.NeedsBuildUnit && UnitRegistry.TryGetValue(result.BuildUnitType, out var meta))
		{
			var playerOwner = _peerIdToPlayerEntityMap[result.BuildPeerOwner].AsPlayerEntity(EcsWorld);
			string targetModel = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : result.BuildUnitType;
			string modelPath = GetFallbackModelPath(targetModel, true);
			var bldEntity = CreateEcsUnit(result.BuildUnitType, meta.Name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, 0f, result.BuildPosition, playerOwner);
			SpawnUnit3D(bldEntity, result.BuildUnitType, modelPath, result.BuildPosition, true, false);
		}

		if (result.NeedsSpellEffect)
		{
			string spellId = result.SpellId;
			Vector3 position = result.SpellPosition;
			IUnit caster = null;
			if (cmd.UnitEntityIds.Count > 0)
			{
				var casterEntity = _networkService.FindServerEntity(cmd.UnitEntityIds[0], AllUnits);
				if (casterEntity != Entity.Null && EcsWorld.IsAlive(casterEntity))
				{
					caster = GetUnitWrapper(casterEntity);
				}
			}
			var casterEnt = caster != null ? ((IEcsEntityWrapper)caster).Entity : Entity.Null;
			OnSpellCast?.Invoke(caster, spellId, new System.Numerics.Vector3(position.X, position.Y, position.Z));

			var def = GetAbilityDefinition(spellId);
			if (def != null)
			{
				if (def.Damage > 0f)
				{
					float aoe = def.AreaOfEffectRadius > 0f ? def.AreaOfEffectRadius : 4.0f;
					_simulationService.DealSpellDamageAOE(new System.Numerics.Vector3(position.X, position.Y, position.Z), aoe, def.Damage, casterEnt);
				}
				else if (def.Healing > 0f)
				{
					float aoe = def.AreaOfEffectRadius > 0f ? def.AreaOfEffectRadius : 4.0f;
					_simulationService.HealAOE(new System.Numerics.Vector3(position.X, position.Y, position.Z), aoe, def.Healing);
				}
			}
			InGameHUD.Instance?.RefreshUI(SelectedUnits);
			if (LobbyManager.Instance != null)
			{
				foreach (var p in LobbyManager.Instance.PlayerList)
				{
					if (p.PeerId == _localPeerId || p.PeerId < 0) continue;
					QueueOrSendPacket(p.PeerId, nameof(PlaySpellEffect), spellId, position);
				}
			}
		}

		RpcId(senderId, nameof(AcknowledgeCommand), cmd.CommandId);
		sw.Stop();
		float responseCpuMs = (float)sw.Elapsed.TotalMilliseconds;
		float adjustedResponseMs = responseCpuMs + _trackerLastTickDelay;
		if (_trackerApiDurations != null)
		{
			_trackerApiDurations.Add(adjustedResponseMs);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void AcknowledgeCommand(int commandId)
	{
		_networkService.AcknowledgeCommand(commandId);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void UpdateClientCamera(byte[] payload)
	{
		int senderId = Multiplayer.GetRemoteSenderId();
		var pos = MemoryPackSerializer.Deserialize<NetworkVector3>(payload);
		_networkService.RecordClientCameraPosition(senderId, pos.ToGodot());
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void ReceiveSnapshot(byte[] payload)
	{
		if (Multiplayer.IsServer()) return;
		_networkService.RecordSnapshotReceived(payload);

		foreach (var snap in _networkService.FlushPendingUnitSpawns())
		{
			SpawnUnitFromSnapshot(snap);
		}

		foreach (var localEntity in _networkService.FlushPendingUnitKills())
		{
			if (GameHost.TryGetUnit3D(localEntity, out var unit3D))
			{
				CallDeferred("KillUnitDeferred", unit3D);
			}
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void PlaySpellEffect(string spellId, Vector3 position)
	{
		var def = GetAbilityDefinition(spellId);
		if (def != null)
		{
			_fxService.SpawnAbilityEffect(this, def, position);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientSpawnArrowProjectile(Vector3 start, Vector3 target)
	{
		_fxService.SpawnArrowProjectile(this, start, target);
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
		if (_worldEntity == Entity.Null || !EcsWorld.Has<NetworkMappingState>(_worldEntity)) return;
		var mapping = EcsWorld.Get<NetworkMappingState>(_worldEntity);
		if (mapping.ServerToClientEntityMap.TryGetValue(castleServerEntityId, out var localEntity))
		{
			if (EcsWorld.IsAlive(localEntity))
			{
				if (!EcsWorld.Has<ProductionQueue>(localEntity))
				{
					EcsWorld.Add(localEntity, new ProductionQueue());
				}
				ref var prod = ref EcsWorld.Get<ProductionQueue>(localEntity);
				prod.UnitIds.Clear();
				prod.UnitIds.AddRange(unitIds);
				prod.CurrentProgress = currentProgress;
				prod.BuildTime = buildTime;
			}
		}
	}

	private void UpdateServerSnapshotTick(float fDelta)
	{
		var snapshots = _networkService.BuildServerSnapshots(_localPeerId, AllUnits);
		foreach (var (peerId, payload) in snapshots)
		{
			QueueOrSendPacket(peerId, nameof(ReceiveSnapshot), payload);
		}

		foreach (var kvp in _peerIdToPlayerEntityMap)
		{
			int peerId = kvp.Key;
			if (peerId != _localPeerId)
			{
				if (EcsWorld.IsAlive(kvp.Value) && EcsWorld.TryGet<PlayerResources>(kvp.Value, out var res))
				{
					float gold = res.Value.TryGetValue(_goldResourceId, out var g) ? g : 0;
					float wood = res.Value.TryGetValue(_woodResourceId, out var w) ? w : 0;
					float stone = res.Value.TryGetValue(_stoneResourceId, out var s) ? s : 0;
					RpcId(peerId, nameof(SyncPlayerResources), gold, wood, stone);
				}
			}
		}

		foreach (var unit in AllUnits)
		{
			if (EcsWorld.IsAlive(unit.Entity) && EcsWorld.Has<ProductionQueue>(unit.Entity))
			{
				var prod = EcsWorld.Get<ProductionQueue>(unit.Entity);
				foreach (var kvp in _peerIdToPlayerEntityMap)
				{
					int peerId = kvp.Key;
					if (peerId != _localPeerId)
					{
						RpcId(peerId, nameof(SyncProductionQueue), unit.Entity.Id, prod.UnitIds.ToArray(), prod.CurrentProgress, prod.BuildTime);
					}
				}
			}
		}
	}

	private void SpawnUnitFromSnapshot(UnitSnapshot snap)
	{
		var entity = _networkService.SpawnUnitFromSnapshot(snap, GetFallbackModelPath, out string modelPath, out bool isEnemy);
		if (entity == Entity.Null) return;
		SpawnUnit3D(entity, snap.UnitId, modelPath, snap.Position.ToGodot(), snap.IsBuilding, isEnemy);
	}

	private void QueueOrSendPacket(int peerId, string funcName, params object[] args)
	{
		if (_networkService.IsSpectatorWithDelay(peerId))
		{
			_networkService.QueueSpectatorDelayedPacket(peerId, funcName, args);
		}
		else
		{
			SendPacketImmediate(peerId, funcName, args);
		}
	}

	private void ProcessDelayedSpectatorPackets()
	{
		var readyPackets = _networkService.FlushReadySpectatorPackets();
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
			IsPaused = !IsPaused;
			if (!IsPaused)
			{
				ResumeCountdownSeconds = -1;
			}
			UpdatePauseUI();
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RequestPause(int peerId)
	{
		if (!Multiplayer.IsServer()) return;

		if (_disallowedPausePeers.TryGetValue(peerId, out bool disallowed) && disallowed)
		{
			return;
		}

		if (ResumeCountdownSeconds >= 0)
		{
			ResumeCountdownSeconds = -1;
			_countdownForcedByHost = false;
			IsPaused = true;
			Rpc(nameof(BroadcastPauseState), true, peerId, false);
			return;
		}

		IsPaused = !IsPaused;
		if (IsPaused)
		{
			ResumeCountdownSeconds = -1;
			_countdownForcedByHost = false;
			_playerReadyStates.Clear();
			Rpc(nameof(BroadcastPauseState), true, peerId, false);
		}
		else
		{
			StartCountdown(peerId == 1);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RequestToggleReady(int peerId, bool ready)
	{
		if (!Multiplayer.IsServer()) return;
		if (!IsPaused) return;

		_playerReadyStates[peerId] = ready;

		var serializedReady = System.Text.Json.JsonSerializer.Serialize(_playerReadyStates);
		Rpc(nameof(BroadcastReadyStates), serializedReady);

		if (CheckAllPlayersReady())
		{
			StartCountdown(false);
		}
		else if (ResumeCountdownSeconds >= 0 && !_countdownForcedByHost)
		{
			ResumeCountdownSeconds = -1;
			Rpc(nameof(BroadcastCountdownCancel));
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RequestSetDisallowPause(int peerId, bool disallowed)
	{
		if (!Multiplayer.IsServer()) return;
		int senderId = Multiplayer.GetRemoteSenderId();
		if (senderId != 1) return;

		_disallowedPausePeers[peerId] = disallowed;

		var serializedDisallowed = System.Text.Json.JsonSerializer.Serialize(_disallowedPausePeers);
		Rpc(nameof(BroadcastDisallowedPausePeers), serializedDisallowed);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RequestForceResume()
	{
		if (!Multiplayer.IsServer()) return;
		int senderId = Multiplayer.GetRemoteSenderId();
		if (senderId != 1) return;

		StartCountdown(true);
	}

	private void StartCountdown(bool forcedByHost)
	{
		ResumeCountdownSeconds = 5;
		_resumeCountdownTimer = 0f;
		_countdownForcedByHost = forcedByHost;
		Rpc(nameof(BroadcastCountdownState), 5, forcedByHost);
	}

	private bool CheckAllPlayersReady()
	{
		if (LobbyManager.Instance == null) return true;
		foreach (var player in LobbyManager.Instance.PlayerList)
		{
			if (player.Team == "Spectator") continue;
			_playerReadyStates.TryGetValue(player.PeerId, out bool ready);
			if (!ready) return false;
		}
		return true;
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void BroadcastPauseState(bool paused, int pausedByPeerId, bool instantResume)
	{
		IsPaused = paused;
		if (paused)
		{
			ResumeCountdownSeconds = -1;
		}
		else if (instantResume)
		{
			ResumeCountdownSeconds = -1;
		}
		UpdatePauseUI();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void BroadcastReadyStates(string serializedReadyStates)
	{
		var states = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<int, bool>>(serializedReadyStates);
		_playerReadyStates.Clear();
		foreach (var kvp in states)
		{
			_playerReadyStates[kvp.Key] = kvp.Value;
		}
		UpdatePauseUI();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void BroadcastDisallowedPausePeers(string serializedDisallowed)
	{
		var states = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<int, bool>>(serializedDisallowed);
		_disallowedPausePeers.Clear();
		foreach (var kvp in states)
		{
			_disallowedPausePeers[kvp.Key] = kvp.Value;
		}
		UpdatePauseUI();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void BroadcastCountdownState(int seconds, bool forcedByHost)
	{
		ResumeCountdownSeconds = seconds;
		_countdownForcedByHost = forcedByHost;
		UpdatePauseUI();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void BroadcastCountdownCancel()
	{
		ResumeCountdownSeconds = -1;
		_countdownForcedByHost = false;
		UpdatePauseUI();
	}

	public void GetPlayerReadyState(int peerId, out bool ready)
	{
		_playerReadyStates.TryGetValue(peerId, out ready);
	}

	public void GetPlayerDisallowPause(int peerId, out bool disallowed)
	{
		_disallowedPausePeers.TryGetValue(peerId, out disallowed);
	}

	private void UpdatePauseUI()
	{
		InGameHUD.Instance?.UpdatePauseUI();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void SyncWorldState(float gameElapsedTime, int timeOfDayIndex, float timeOfDayTimer, bool dayNightCycleEnabled)
	{
		if (EcsWorld != null && _worldEntity != Entity.Null && EcsWorld.IsAlive(_worldEntity))
		{
			EcsWorld.SetOrAdd(_worldEntity, new WorldState(gameElapsedTime, timeOfDayIndex, timeOfDayTimer, dayNightCycleEnabled));
			if (dayNightCycleEnabled && TimeOfDayCycleDuration > 0)
			{
				float progress = timeOfDayTimer / TimeOfDayCycleDuration;
				if (!IsMapEditorMode)
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
			_environmentService?.ApplyPresetById(this, presetId);
		}
		else
		{
			_environmentService?.TransitionToPreset(this, presetId, durationSeconds);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void SyncWeatherRpc(string weatherType)
	{
		_environmentService?.SetCurrentWeather(weatherType);
		InGameHUD.Instance?.ApplyWeatherEffects(weatherType);
	}

	public void HandlePeerReconnected(int oldPeerId, int newPeerId, int slot)
	{
		if (!Multiplayer.IsServer()) return;

		GD.Print($"[GameHost] HandlePeerReconnected: OldPeer={oldPeerId}, NewPeer={newPeerId}, Slot={slot}");

		Entity playerEntity = Entity.Null;
		if (_peerIdToPlayerEntityMap.TryGetValue(oldPeerId, out var existingEntity))
		{
			playerEntity = existingEntity;
			_peerIdToPlayerEntityMap.Remove(oldPeerId);
		}
		else if (_peerIdToPlayerEntityMap.TryGetValue(newPeerId, out var directEntity))
		{
			playerEntity = directEntity;
		}

		if (playerEntity != Entity.Null)
		{
			_peerIdToPlayerEntityMap[newPeerId] = playerEntity;
		}

		if (_worldEntity != Entity.Null && EcsWorld != null && EcsWorld.Has<NetworkMappingState>(_worldEntity))
		{
			var mapping = EcsWorld.Get<NetworkMappingState>(_worldEntity);
			if (playerEntity != Entity.Null)
			{
				mapping.PeerIdToPlayerEntityMap.Remove(oldPeerId);
				mapping.PeerIdToPlayerEntityMap[newPeerId] = playerEntity;
			}
		}

		if (_worldEntity != Entity.Null && EcsWorld != null && EcsWorld.Has<WorldState>(_worldEntity))
		{
			var ws = EcsWorld.Get<WorldState>(_worldEntity);
			RpcId(newPeerId, nameof(SyncWorldState), ws.GameElapsedTime, ws.TimeOfDayIndex, ws.TimeOfDayTimer, ws.DayNightCycleEnabled);
		}

		var baselinePayload = _networkService.BuildExplicitBaselineSnapshot(newPeerId, AllUnits);
		if (baselinePayload != null && baselinePayload.Length > 0)
		{
			RpcId(newPeerId, nameof(ReceiveSnapshot), baselinePayload);
		}

		if (playerEntity != Entity.Null && EcsWorld != null && EcsWorld.IsAlive(playerEntity) && EcsWorld.TryGet<PlayerResources>(playerEntity, out var res))
		{
			float gold = res.Value.TryGetValue(_goldResourceId, out var g) ? g : 0;
			float wood = res.Value.TryGetValue(_woodResourceId, out var w) ? w : 0;
			float stone = res.Value.TryGetValue(_stoneResourceId, out var s) ? s : 0;
			RpcId(newPeerId, nameof(SyncPlayerResources), gold, wood, stone);
		}

		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit) && EcsWorld != null && EcsWorld.IsAlive(unit.Entity) && EcsWorld.Has<ProductionQueue>(unit.Entity))
			{
				var prod = EcsWorld.Get<ProductionQueue>(unit.Entity);
				RpcId(newPeerId, nameof(SyncProductionQueue), unit.Entity.Id, prod.UnitIds.ToArray(), prod.CurrentProgress, prod.BuildTime);
			}
		}

		if (IsPaused)
		{
			RpcId(newPeerId, nameof(BroadcastPauseState), true, 1, false);
			var serializedReady = System.Text.Json.JsonSerializer.Serialize(_playerReadyStates);
			RpcId(newPeerId, nameof(BroadcastReadyStates), serializedReady);
		}
	}

	public void OnClientReconnected(int slot)
	{
		_localPeerId = Multiplayer.GetUniqueId();
		_networkService.LocalPeerId = _localPeerId;
		_networkService.MarkClientEnteredMultiplayer();
		_networkService.ResetReconnectionState();

		if (LobbyManager.Instance != null && LobbyManager.Instance.PlayerList.Count > 0)
		{
			foreach (var p in LobbyManager.Instance.PlayerList)
			{
				if (p.Slot == slot || p.PeerId == _localPeerId)
				{
					if (_peerIdToPlayerEntityMap.TryGetValue(_localPeerId, out var myEntity))
					{
						_playerEntity = myEntity;
					}
				}
			}
		}

		if (_worldEntity != Entity.Null && EcsWorld != null && EcsWorld.Has<NetworkMappingState>(_worldEntity))
		{
			var mapping = EcsWorld.Get<NetworkMappingState>(_worldEntity);
			mapping.PlayerEntity = _playerEntity;
			mapping.EnemyPlayerEntity = _enemyPlayerEntity;
			if (_playerEntity != Entity.Null)
			{
				mapping.PeerIdToPlayerEntityMap[_localPeerId] = _playerEntity;
			}
		}

		InGameHUD.Instance?.RefreshUI(SelectedUnits);
		GD.Print($"[GameHost] OnClientReconnected completed for LocalPeerId={_localPeerId}, Slot={slot}");
	}
}
