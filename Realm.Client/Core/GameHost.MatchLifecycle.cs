using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Realm.Ecs;
using Realm.Ecs.Replication;
using DotRecast.Detour;
using Godot;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Client.Utils;
using Realm.Client.VFX;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Environment = Godot.Environment;
using FileAccess = System.IO.FileAccess;
using Realm.Shared.Abilities;

namespace Realm.Client.Core;
public partial class GameHost
{
	private double[] _lastGroupPressTime { get; set; } = new double[10];

	private bool _isDragging { get; set; }
	private Vector2 _dragStart { get; set; }
	private Vector2 _dragEnd { get; set; }
	private float DragThreshold { get; set; } = 8f;

	public override void _Ready()
	{
		if (!MatchLoadTrace.IsTracing) MatchLoadTrace.Begin("scene");

		ProcessPerformanceOptimizer.EnforceOptimalPerformance();
		MainNode = GetTree().Root.GetNodeOrNull("Main");
		MainCamera = GetTree().Root.GetNodeOrNull<CameraControl>("Main/Camera3D");

		GameLog.LogToConsole($"[GAMEHOST_READY] GameHost _Ready starting");
		GameContext.RegisterHost(this);
		GameSettings.ApplyGraphicsSettings(this);
		ReinitializeEcsAndServices();
		MatchLoadTrace.Mark("scene_build");
		JoinAuthority();
		MatchLoadTrace.Mark("session_join");
	}

	public void AttachMatchSession()
	{
		if (_joinedSession != null) return;
		JoinAuthority();
		MatchLoadTrace.Mark("session_attached");
	}

	private bool _firstFrameTraced;

	private bool _simulationStartTraced;

	private const int WorldBuildStageCount = 7;

	private int _worldBuildStage;

	public bool IsBuildingMatchWorld { get; private set; }

	public bool HasPreparedMatchWorld { get; private set; }

	private bool _worldBuildRunning;

	public void BeginMatchWorldPreparation(string mapName)
	{
		if (IsBuildingMatchWorld) return;
		if (GameContext.Lobby?.IsGameStarted == true) return;
		if (string.IsNullOrWhiteSpace(mapName)) return;

		IsBuildingMatchWorld = true;
		_worldBuildStage = 0;
		var lobby = GameContext.Lobby;
		if (lobby == null) return;
		lobby.ActiveMapName = mapName;
		MatchLoadTrace.Begin("lobby_prebuild");
		HideWorldUntilMatchStarts();
		ClearWorldNodesForRebuild();
		ResetScreenScopeForWorldRebuild();
	}

	public bool AdvanceMatchWorldPreparation()
	{
		if (!IsBuildingMatchWorld) return false;
		if (_worldBuildStage >= WorldBuildStageCount)
		{
			FinishMatchWorldPreparation();
			return true;
		}

		RunWorldBuildStage(_worldBuildStage);
		_worldBuildStage++;

		if (_worldBuildStage < WorldBuildStageCount) return false;

		FinishMatchWorldPreparation();
		return true;
	}

	public void ShowPreparedMatchWorld()
	{
		_matchWorldVisible = true;
		if (MainCamera != null && GodotObject.IsInstanceValid(MainCamera)) MainCamera.Current = true;
		MatchLoadTrace.Mark("world_shown");
	}

	private bool _matchWorldVisible;

	public void DiscardPreparedMatchWorld()
	{
		bool hadWorld = IsBuildingMatchWorld || HasPreparedMatchWorld;
		if (!hadWorld) return;

		IsBuildingMatchWorld = false;
		HasPreparedMatchWorld = false;
		_worldBuildStage = WorldBuildStageCount;
		if (MainCamera != null && GodotObject.IsInstanceValid(MainCamera)) MainCamera.Current = true;

		MatchLoadTrace.Mark("world_discarded");
		MatchLoadTrace.WriteSummary("world_discarded");
	}

	public void StartMatchRecording() => StartReplayRecordingIfNeeded(GameContext.Lobby?.ActiveMapName ?? "");

	private void FinishMatchWorldPreparation()
	{
		IsBuildingMatchWorld = false;
		HasPreparedMatchWorld = true;
		MatchLoadTrace.Mark("world_built");
		MatchLoadTrace.WriteSummary("lobby_prebuild");
	}

	private void HideWorldUntilMatchStarts()
	{
		if (MainCamera != null && GodotObject.IsInstanceValid(MainCamera)) MainCamera.Current = false;
	}

	private void InitializeGameEcs()
	{
		_worldBuildRunning = true;
		try
		{
			for (int stage = 0; stage < WorldBuildStageCount; stage++) RunWorldBuildStage(stage);
		}
		finally
		{
			_worldBuildRunning = false;
		}
	}

	private void RunWorldBuildStage(int stage)
	{
		switch (stage)
		{
			case 0:
				ResolveServices();
				if (OperatingSystem.IsWindows()) GameContext.VSCode.StartInstallIfNeeded();
				InitializeTracking();
				break;
			case 1:
				CreateGround();
				EnsurePropMultiMeshManager();
				EnsureUnitOcclusionOutlineService();
				SetupWorldEntityComponents();
				MatchLoadTrace.Mark("ground_and_world");

				if (GroundTerrain != null) GameContext.EditorService.Surface.AttachSplatMaps(GroundTerrain.SplatMap, GroundTerrain.CliffSplatMap);

				SetupSkybox();
				UpdateDayNightVisuals(0.0f);
				break;
			case 2:
				InitializeResourceIds();
				InitializeSimulationService();
				MatchLoadTrace.Mark("simulation_services");

				if (_multiplayerActive) InitializeMultiplayerPlayers();
				else InitializeSingleplayerPlayers();

				EnsureFallbackPlayers();
				ConfigurePerspective();
				break;
			case 3:
				string rawMapName = GameContext.Lobby?.ActiveMapName ?? "";
				if (!string.IsNullOrEmpty(rawMapName)) LoadUnitMetadata(rawMapName);
				MatchLoadTrace.Mark("catalog_and_models");
				break;
			case 4:
				BeginLobbyMapWorldLoad(GameContext.Lobby?.ActiveMapName ?? "");
				break;
			case 5:
				InstantiateMapSpawnRequests();
				MatchLoadTrace.Mark("prop_instancing");
				break;
			case 6:
				CompleteMapWorldLoad();
				MatchLoadTrace.Mark("map_load");

				StartReplayRecordingIfNeeded(GameContext.Lobby?.ActiveMapName ?? "");

				if (!GameContext.ReplayService.IsResettingForReplay) SchedulePostInitializationTasks();
				MatchLoadTrace.Mark("post_initialization");

				GameContext.ShroudService.Initialize();
				break;
		}
	}

	private void InitializeTracking()
	{
		if (Multiplayer.MultiplayerPeer != null && !Multiplayer.IsServer()) return;

		GameContext.PerformanceTrackingService.TrackerTickDurations = new List<float>(100000);
		GameContext.PerformanceTrackingService.TrackerApiDurations = new List<float>(10000);
		GameContext.PerformanceTrackingService.TrackerIntervalStopwatch.Start();
	}

	private void EnsurePropMultiMeshManager()
	{
		if (GetNodeOrNull<PropMultiMeshManager>("PropMultiMeshManager") != null) return;

		var propMultiMeshManager = new PropMultiMeshManager();
		propMultiMeshManager.Name = "PropMultiMeshManager";
		AddChild(propMultiMeshManager);
	}

	private void EnsureUnitOcclusionOutlineService()
	{
		if (_unitOcclusionOutlineService != null && GodotObject.IsInstanceValid(_unitOcclusionOutlineService)) return;

		_unitOcclusionOutlineService = GetNodeOrNull<UnitOcclusionOutlineService>("UnitOcclusionOutlineService");
		if (_unitOcclusionOutlineService != null && GodotObject.IsInstanceValid(_unitOcclusionOutlineService)) return;

		_unitOcclusionOutlineService = new UnitOcclusionOutlineService { Name = "UnitOcclusionOutlineService" };
		AddChild(_unitOcclusionOutlineService);
	}

	private void InitializeResourceIds()
	{
		GameContext.Simulation.PlayerResourceService.GoldResourceId = "gold".AsResourceId(GameContext.Simulation.DefinitionManager);
		GameContext.Simulation.PlayerResourceService.WoodResourceId = "wood".AsResourceId(GameContext.Simulation.DefinitionManager);
		GameContext.Simulation.PlayerResourceService.StoneResourceId = "stone".AsResourceId(GameContext.Simulation.DefinitionManager);
	}

	private void InitializeSimulationService()
	{
		GameContext.SimulationService.SetRuntimeReferences(AllUnits, AllProps, GameContext.Simulation.DefinitionManager, GameContext.Simulation.PlayerResourceService.GoldResourceId, GameContext.Simulation.PlayerResourceService.WoodResourceId, GameContext.Simulation.PlayerResourceService.StoneResourceId);
		GameContext.SimulationService.Initialize();
		GameContext.SimulationService.EditorHeightProvider = p => GetTerrainHeightAt(new Vector3(p.X, p.Y, p.Z));

		SetupSimulationServiceCallbacks();
	}

	private void SetupSimulationServiceCallbacks()
	{
		GameContext.SimulationService.OnArrowProjectileRequested = (start, target) => SpawnArrowProjectile(new Vector3(start.X, start.Y, start.Z), new Vector3(target.X, target.Y, target.Z));
		GameContext.SimulationService.OnWeaponProjectileRequested = (start, target, weaponId, targetEnt) => SpawnWeaponProjectile(new Vector3(start.X, start.Y, start.Z), new Vector3(target.X, target.Y, target.Z), weaponId, targetEnt);
		GameContext.SimulationService.OnDamageFlashRequested = entity =>
		{
			if (TryGetUnit3D(entity, out var unit3D) && unit3D != null && unit3D.Visible && !unit3D.IsFlashingScale)
			{
				Callable.From(() => FlashDamageUnit(unit3D)).CallDeferred();
			}
		};
		GameContext.SimulationService.OnHealEffectRequested = (start, target) => SpawnHealVisualEffect(new Vector3(start.X, start.Y, start.Z), new Vector3(target.X, target.Y, target.Z));
		GameContext.SimulationService.OnHealFlashRequested = entity =>
		{
			if (TryGetUnit3D(entity, out var unit3D) && unit3D != null && unit3D.Visible && !unit3D.IsFlashingScale)
			{
				Callable.From(() => FlashHealUnit(unit3D)).CallDeferred();
			}
		};

		GameContext.SimulationService.OnKillUnitRequested = entity =>
		{
			GameContext.SimulationService.WarnedNonFinitePositions.Remove(entity);
			if (World.IsAlive(entity) && TryGetUnit3D(entity, out var unit3D)) Callable.From(() => KillUnit(unit3D)).CallDeferred();
		};

		GameContext.SimulationService.OnPropDepleted = entity =>
		{
			if (GameContext.World != null && World.IsAlive(entity) && World.Has<PropIdentity>(entity))
			{
				string propId = World.Get<PropIdentity>(entity).PropId;
				GameContext.PropMultiMesh?.MarkDirty(propId);
			}
			else
			{
				GameContext.PropMultiMesh?.MarkAllDirty();
			}
		};
		GameContext.SimulationService.OnResourceHarvested = entity =>
		{
			GameContext.PropMultiMesh?.SetInstanceCustomData(entity, new Color(1f, 0f, 0f, 0f));
		};

		SetupSimulationCombatCallbacks();
		SetupSimulationProductionCallbacks();
	}

	private void SetupSimulationCombatCallbacks()
	{
		GameContext.SimulationService.OnUnitDamagedCallback = HandleUnitDamaged;
		GameContext.SimulationService.OnUnitHealedCallback = HandleUnitHealed;
		GameContext.SimulationService.OnUnitAttackedCallback = HandleUnitAttacked;
	}

	private void HandleUnitDamaged(Entity targetEntity, Entity attackerEntity, float damage)
	{
		if (!World.IsAlive(targetEntity)) return;

		UnitId attackerId = (attackerEntity != Entity.Null && World.IsAlive(attackerEntity)) ? (UnitId)attackerEntity.Id : UnitId.Invalid;
		OnUnitDamaged?.Invoke((UnitId)targetEntity.Id, attackerId, damage);

		if (TryGetUnit3D(targetEntity, out var targetUnit3D))
		{
			GameContext.FXService.SpawnDamageNumber(this, targetUnit3D.GlobalPosition, damage);
			GameContext.AudioService?.PlayUnitSound(targetUnit3D.UnitId, UnitSoundEvent.Wounded, targetUnit3D.GlobalPosition);
			targetUnit3D.TriggerImpulse(0.5f, 0.35f);
		}
	}

	private void HandleUnitHealed(Entity targetEntity, Entity healerEntity, float healAmount)
	{
		if (World.IsAlive(targetEntity) && TryGetUnit3D(targetEntity, out var targetUnit3D))
			GameContext.FXService.SpawnHealNumber(this, targetUnit3D.GlobalPosition, healAmount);
	}

	private void HandleUnitAttacked(Entity attackerEntity, Entity targetEntity)
	{
		if (!World.IsAlive(attackerEntity) || !World.IsAlive(targetEntity)) return;

		if (TryGetUnit3D(attackerEntity, out var attackerUnit3D)) attackerUnit3D.PlayAnimation("Attack");
		OnUnitAttacked?.Invoke((UnitId)attackerEntity.Id, (UnitId)targetEntity.Id);
	}

	private void SetupSimulationProductionCallbacks()
	{
		GameContext.SimulationService.OnSpawnUnitFromProductionRequested = (unitId, position, buildingEntity, isFromQueue) =>
			SpawnUnitFromProduction(unitId, position, buildingEntity, isFromQueue);

		GameContext.SimulationService.GetProductionBuildTime = unitId =>
			UnitRegistry.TryGetValue(unitId, out var meta) ? meta.ProductionTime : 5f;

		GameContext.SimulationService.OnClearUnitOrdersRequested = ClearUnitOrders;
		GameContext.SimulationService.OnStopGatheringMovementRequested = StopGatheringMovement;
		GameContext.SimulationService.OnUiRefreshRequested = () => GameContext.InGameHud?.RefreshUI(SelectedUnits);

		GameContext.SimulationService.OnResourceDepositedForPlayer = (playerIndex, resType, carry) =>
		{
			if (!GameContext.PerspectiveFilter.CanObserve(playerIndex)) return;
			string resTypeUpper = resType.ToUpper();
			Callable.From(() => GameContext.InGameHud?.ShowFeedbackText($"+{carry:F0} {resTypeUpper} deposited", new Color(0.2f, 0.9f, 0.4f))).CallDeferred();
		};

		GameContext.SimulationService.OnProductionCompleted = (playerIndex, unitToSpawn) =>
		{
			if (!GameContext.PerspectiveFilter.CanObserve(playerIndex)) return;
			string displayName = UnitRegistry.TryGetValue(unitToSpawn, out var nm) ? nm.Name : unitToSpawn.ToUpper();
			Callable.From(() => GameContext.InGameHud?.ShowFeedbackText($"✓ {displayName} training complete!", new Color(0.3f, 0.9f, 0.4f))).CallDeferred();
		};
	}

	private void InitializeMultiplayerPlayers()
	{
		_localPeerId = Multiplayer.GetUniqueId();
		if (!Multiplayer.IsServer()) GameContext.NetworkService.MarkClientEnteredMultiplayer();

		if (GameContext.Lobby != null && GameContext.Lobby.PlayerList.Count > 0)
			SetupLobbyPlayers();
		else
			SetupDefaultPlayers();
	}

	private void SetupLobbyPlayers()
	{
		var playerEntities = World.Get<NetworkMappingState>(GameContext.Simulation.WorldEntity).PlayerEntities;
		_peerIdToPlayerEntityMap[0] = playerEntities[0];
		UpdateScriptPlayerState(0, "Neutral", active: true, isComputer: true, teamIndex: 0);

		var lobby = GameContext.Lobby;
		if (lobby == null) return;
		foreach (var p in lobby.PlayerList)
		{
			if (p.Slot >= 0 && p.Slot < playerEntities.Length)
			{
				var playerEntity = playerEntities[p.Slot];
				_peerIdToPlayerEntityMap[p.PeerId] = playerEntity;
				if (World.Has<Name>(playerEntity))
				{
					World.Set(playerEntity, new Name(p.Name));
				}
				UpdateScriptPlayerState(p.Slot, p.Name, active: true, isComputer: p.PeerId < 0, teamIndex: p.TeamIndex);
			}
		}

		GameContext.SimulationService?.RecalculatePlayerRelationships();
	}

	private void InitializeSingleplayerPlayers()
	{
		if (GameContext.Lobby != null && GameContext.Lobby.PlayerList.Count > 0)
			SetupLobbyPlayers();
		else
			SetupDefaultPlayers();
	}

	private void SetupDefaultPlayers()
	{
		var playerEntities = World.Get<NetworkMappingState>(GameContext.Simulation.WorldEntity).PlayerEntities;
		_peerIdToPlayerEntityMap[0] = playerEntities[0];
		_peerIdToPlayerEntityMap[1] = playerEntities[1];
		_peerIdToPlayerEntityMap[-1] = playerEntities[2];

		string localPlayerName = GameContext.Lobby?.PlayerDisplayName ?? TranslationServer.Translate("Guest");
		UpdateScriptPlayerState(0, "Neutral", active: true, isComputer: true, teamIndex: 0);
		UpdateScriptPlayerState(1, localPlayerName, active: true, isComputer: false, teamIndex: 1);
		UpdateScriptPlayerState(2, "Enemy_AI", active: true, isComputer: true, teamIndex: 2);

		GameContext.SimulationService?.RecalculatePlayerRelationships();
	}

	private void UpdateScriptPlayerState(int pIdx, string name, bool active = true, bool isComputer = false, int teamIndex = -1)
	{
		if (!World.Has<ScriptPlayersState>(GameContext.Simulation.WorldEntity)) return;

		ref var playersState = ref World.Get<ScriptPlayersState>(GameContext.Simulation.WorldEntity);
		if (playersState.IsValidSlot(pIdx))
		{
			playersState.SetName(pIdx, name);
			playersState.SetActive(pIdx, active);
			playersState.SetComputerControlled(pIdx, isComputer);
			if (teamIndex >= 0)
			{
				int teamNameStringId = Realm.Ecs.StringRegistry.GetOrAdd(teamIndex == 0 ? "Neutral" : $"Team {teamIndex}");
				playersState.SetTeam(pIdx, teamIndex, teamNameStringId);
			}

			var player = playersState.Get(pIdx);
			World.GetBuffer<ScriptPlayer>().Set(pIdx, in player);

			if (teamIndex >= 0 && World.Has<NetworkMappingState>(GameContext.Simulation.WorldEntity))
			{
				var playerEntities = World.Get<NetworkMappingState>(GameContext.Simulation.WorldEntity).PlayerEntities;
				if (pIdx < playerEntities.Length && playerEntities[pIdx] != Entity.Null && World.IsAlive(playerEntities[pIdx]))
				{
					int stringId = playersState.Get(pIdx).TeamNameStringId;
					if (World.Has<PlayerTeam>(playerEntities[pIdx]))
					{
						ref var pt = ref World.Get<PlayerTeam>(playerEntities[pIdx]);
						pt.TeamIndex = teamIndex;
						pt.TeamNameStringId = stringId;
					}
					else
					{
						World.Add(playerEntities[pIdx], new PlayerTeam(teamIndex, stringId));
					}
				}
			}
		}
	}

	private void EnsureFallbackPlayers()
	{
		if (GameContext.World != null && GameContext.Simulation.WorldEntity != Entity.Null && World.Has<NetworkMappingState>(GameContext.Simulation.WorldEntity))
		{
			var playerEntities = World.Get<NetworkMappingState>(GameContext.Simulation.WorldEntity).PlayerEntities;
			if (playerEntities != null && playerEntities.Length > 2)
			{
				_peerIdToPlayerEntityMap[0] = playerEntities[0];
				_peerIdToPlayerEntityMap[1] = playerEntities[1];
				_peerIdToPlayerEntityMap[-1] = playerEntities[2];
			}
		}
	}

	private void ApplyTestRandomSeedIfNeeded(string rawMapNameOrPath)
	{
		bool isSinglePlayerOrTest = (GameContext.Lobby?.IsSinglePlayer == true) || GameContext.SessionState.IsTestMode || !IsMultiplayerActive();
		if (!isSinglePlayerOrTest) return;

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string? resolvedDir = !string.IsNullOrEmpty(rawMapNameOrPath) ? ResolveMapDirectory(rawMapNameOrPath) : wsPath;
		string metadataPath = !string.IsNullOrEmpty(resolvedDir)
			? resolvedDir
			: (!string.IsNullOrEmpty(rawMapNameOrPath) ? rawMapNameOrPath : wsPath);

		var metadata = MapFileService.LoadMetadata(metadataPath);
		if (metadata.MapProperties?.TestRandomSeed != null)
		{
			GameContext.RandomService.SetSeed(metadata.MapProperties.TestRandomSeed);
		}
	}

	private string ResolveCustomTerrainPath(string rawMapName)
	{
		string? resolvedDir = ResolveMapDirectory(rawMapName);
		if (!string.IsNullOrEmpty(resolvedDir))
		{
			GameContext.MapStorageService.CurrentMapDirectory = resolvedDir;
			string checkTerrain = Path.Combine(resolvedDir, "terrain.json");
			if (File.Exists(checkTerrain)) return checkTerrain;
			return string.Empty;
		}

		return TryResolveExplicitCustomTerrainPath(rawMapName) ?? TryResolveFallbackCustomTerrainPath(rawMapName);
	}

	private string? TryResolveExplicitCustomTerrainPath(string rawMapName)
	{
		string normalizedRawMapName = rawMapName.Replace('\\', '/');
		if (!normalizedRawMapName.StartsWith("user://") && !normalizedRawMapName.StartsWith("res://") && !Path.IsPathRooted(normalizedRawMapName))
			return null;

		string checkDir = normalizedRawMapName;
		if (normalizedRawMapName.StartsWith("user://") || normalizedRawMapName.StartsWith("res://"))
			checkDir = ProjectSettings.GlobalizePath(normalizedRawMapName);

		if (Directory.Exists(checkDir))
			return Path.Combine(checkDir, "terrain.json");

		return null;
	}

	private string TryResolveFallbackCustomTerrainPath(string rawMapName)
	{
		string normalizedMapName = rawMapName.ToLower().Trim();
		string mapDir = $"res://Maps/{normalizedMapName}";
		if (Directory.Exists(ProjectSettings.GlobalizePath(mapDir)))
			return $"res://Maps/{normalizedMapName}/terrain.json";

		return string.Empty;
	}

	private void StartReplayRecordingIfNeeded(string rawMapName)
	{
		bool isGameStarted = GameContext.Lobby != null && GameContext.Lobby.IsGameStarted;
		if (!isGameStarted || GameContext.SessionState.IsMapEditorMode || GameContext.ReplayPlayback.IsPlayingReplay || !GameSettings.RecordReplays) return;

		string replayDir = ProjectSettings.GlobalizePath("user://replays");
		string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
		string replayPath = Path.Combine(replayDir, $"replay_{timestamp}.rep");

		GameContext.ReplayService.StartRecording(replayPath, rawMapName, GameContext.Lobby?.PlayerList);
		GameLog.LogToConsole($"[ReplayRecorder] Started recording to {replayPath}");
	}

	private void SchedulePostInitializationTasks()
	{
		var timer = GetTree().CreateTimer(0.1f);
		timer.Timeout += () =>
		{
			if (GameContext.InGameHud != null)
			{
				GameContext.InGameHud.Gold = _goldBackup;
				GameContext.InGameHud.Wood = _woodBackup;
				GameContext.InGameHud.Stone = _stoneBackup;
				GameContext.InGameHud.RefreshUI(SelectedUnits);
			}

			if (GameContext.ReplayPlayback.IsPlayingReplay)
				GameContext.ReplayPlayback.ApplyInitialFrame();
		};
	}

	public void ResetWorldAndState()
	{
		ClearWorldNodesForRebuild();
		ReinitializeEcsAndServices();
	}

	private void ClearWorldNodesForRebuild()
	{
		GameContext.ReplayPlayback.StopReplay();
		StopRecording();

		ClearNodeCollection(AllUnits);
		SelectedUnits.Clear();
		_activeImpulseUnits.Clear();
		ClearNodeCollection(AllProps);
		ClearNodeCollection(AllDecals);
		ClearNodeCollection(AllLootItems);
		ClearStaticText();

		GameContext.FXService.ActivePings.Clear();
		ClearAllBuildQueueGhosts();

		if (_controlGroups != null)
		{
			for (int i = 0; i < _controlGroups.Length; i++)
			{
				_controlGroups[i]?.Clear();
			}
		}

		EntityToUnit3D.Clear();
		EntityIdToUnit3D.Clear();
		PlayerToUnits.Clear();
		EntityToProp3D.Clear();
		_playerCameraBoundsOverrides.Clear();
		_playerHudVisibilityOverrides.Clear();
		LocalHudVisibilityOverrides.Clear();
		_cachedWorldEnvironment = null;
	}

	private void ClearNodeCollection<T>(List<T> collection) where T : Node
	{
		foreach (var item in collection)
		{
			if (GodotObject.IsInstanceValid(item)) item.QueueFree();
		}

		collection.Clear();
	}

	private void ReinitializeEcsAndServices()
	{
		ResetScreenScopeForWorldRebuild();
		InitializeGameEcs();
	}

	private void ResetScreenScopeForWorldRebuild()
	{
		EntityToUnit3D.Clear();
		EntityIdToUnit3D.Clear();
		PlayerToUnits.Clear();
		EntityToProp3D.Clear();
		ClearNodeCollection(AllLootItems);
		ClearStaticText();

		bool wasMapEditorMode = GameContext.SessionState.IsMapEditorMode;
		bool wasLoadingMap = GameContext.SaveLoadService.IsLoadingMap;
		bool wasProfilingEnabled = GameContext.PerformanceTrackingService?.IsFunctionProfilingEnabled ?? false;
		bool editorWorldIsSetAside = GameContext.SessionState.EditorWorldState != null;
		string preservedEditorDirectory = editorWorldIsSetAside ? GameContext.EditorService._editingDirectory : string.Empty;
		string? preservedEditorSourceFolder = editorWorldIsSetAside ? GameContext.EditorService.CurrentSourceFolder : null;

		GameContext.EndScreenScope();
		ResolveServices();

		GameContext.SessionState.IsMapEditorMode = wasMapEditorMode;
		if (!string.IsNullOrEmpty(preservedEditorDirectory))
		{
			GameContext.EditorService.SetActiveMapDirectory(preservedEditorDirectory);
			GameContext.EditorService.SetCurrentSourceFolder(preservedEditorSourceFolder);
		}
		else if (wasMapEditorMode)
		{
			GameContext.EditorService.EnsureEditingDirectoryDomain();
		}
		GameContext.SaveLoadService.IsLoadingMap = wasLoadingMap;
		if (wasProfilingEnabled)
		{
			var profiling = GameContext.PerformanceTrackingService;
			if (profiling != null) profiling.IsFunctionProfilingEnabled = true;
		}

		// Do not touch the lazy GroundTerrain getter here: InitializeGameEcs() creates the ground exactly once, and
		// triggering it here built the whole mesh, water and navigation mesh a second time on every _Ready.
		if (_groundTerrain != null)
		{
			GameContext.EditorService.Surface.AttachSplatMaps(_groundTerrain.SplatMap, _groundTerrain.CliffSplatMap);
		}
	}

}
