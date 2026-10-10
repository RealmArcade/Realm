using Arch.Core;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Client;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.VFX;
using Realm.MapAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Realm.Client.Core;

public partial class GameHost : Node3D, IGameAPI
{
	public Camera3D MainCamera;
	public Node MainNode;

	public static GameHost Instance;
	public string ActiveMapName = "";

	private static readonly JsonSerializerOptions Options = new()
	{
		PropertyNameCaseInsensitive = true,
		IncludeFields = true,
		Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
	};

	private AudioService _audioService;
	private FXService _fxService;
	private SaveLoadService _saveLoadService;
	private EditorService _editorService;
	private ReplayService _replayService;
	private SimulationService _simulationService;
	private ShroudService _shroudService;
	private UnitSpawnService _unitSpawnService;
	private WorldInitService _worldInitService;
	private MapPropertiesLoader _mapPropertiesLoader;
	private MapEditorTerrainImportService _terrainImportService;
	private CheatService _cheatService;
	private EnvironmentService _environmentService;
	private SpectatorService _spectatorService;
	private Realm.Client.Services.ModelOptimization.ModelOptimizerService _modelOptimizerService;
	private TerrainNavMeshService _terrainNavMeshService;
	private Realm.Client.Services.MetadataService _metadataService;
	private Realm.Client.Services.MapUpgradeService _mapUpgradeService;
	private Realm.Client.Services.MapStorageService _mapStorageService;
	private Realm.Client.Services.MapSaveDataService _mapSaveDataService;

	public CheatService CheatService => _cheatService;
	public EnvironmentService EnvironmentService => _environmentService;
	public SpectatorService SpectatorService => _spectatorService;
	public ShroudService ShroudService => _shroudService;
	public Realm.Client.Services.ModelOptimization.ModelOptimizerService ModelOptimizerService => _modelOptimizerService;
	public Realm.Client.Services.MetadataService MetadataService => _metadataService;
	public Realm.Client.Services.MapUpgradeService MapUpgradeService => _mapUpgradeService;
	public Realm.Client.Services.MapStorageService MapStorageService => _mapStorageService;
	public Realm.Client.Services.MapSaveDataService MapSaveDataService => _mapSaveDataService;

	public bool UnlimitedPowerEnabled = false;
	public bool GigachadEnabled = false;

	private float _fDelta;
	internal DefinitionManager DefinitionManager => ServiceLocator.Get<DefinitionManager>();
	
	public Entity PlayerEntity => _playerEntity;
	public Entity EnemyEntity => _enemyPlayerEntity;
	

	private bool _multiplayerActive => Multiplayer.MultiplayerPeer != null && Multiplayer.MultiplayerPeer is not OfflineMultiplayerPeer;
	private int _localPeerId
	{
		get => _networkService?.LocalPeerId ?? 1;
		set { if (_networkService != null) _networkService.LocalPeerId = value; }
	}

	private int _nextCommandId
	{
		get => EcsWorld?.GetFieldOrDefault<NetworkState, int>(_worldEntity, s => s.NextCommandId, 1) ?? 1;
		set => EcsWorld?.Mutate(_worldEntity, (ref NetworkState s) => s.NextCommandId = value);
	}

	private float _commandSendTimer
	{
		get => _networkService?.CommandSendTimer ?? 0f;
		set { if (_networkService != null) _networkService.CommandSendTimer = value; }
	}

	private int _snapshotSequence
	{
		get => EcsWorld?.GetFieldOrDefault<NetworkState, int>(_worldEntity, s => s.SnapshotSequence) ?? 0;
		set => EcsWorld?.Mutate(_worldEntity, (ref NetworkState s) => s.SnapshotSequence = value);
	}

	private int _lastReceivedBaselineSeq
	{
		get => EcsWorld?.GetFieldOrDefault<NetworkState, int>(_worldEntity, s => s.LastReceivedBaselineSeq, -1) ?? -1;
		set => EcsWorld?.Mutate(_worldEntity, (ref NetworkState s) => s.LastReceivedBaselineSeq = value);
	}

	private bool _hasReceivedInitialBaseline
	{
		get => EcsWorld?.GetFieldOrDefault<NetworkState, bool>(_worldEntity, s => s.HasReceivedInitialBaseline) ?? false;
		set => EcsWorld?.Mutate(_worldEntity, (ref NetworkState s) => s.HasReceivedInitialBaseline = value);
	}

	private int _lastAppliedSnapshotSequence
	{
		get => EcsWorld?.GetFieldOrDefault<NetworkState, int>(_worldEntity, s => s.LastAppliedSnapshotSequence, -1) ?? -1;
		set => EcsWorld?.Mutate(_worldEntity, (ref NetworkState s) => s.LastAppliedSnapshotSequence = value);
	}

	private ulong _lastSnapshotReceivedTime
	{
		get => _networkService?.LastSnapshotReceivedTime ?? 0;
		set { if (_networkService != null) _networkService.LastSnapshotReceivedTime = value; }
	}

	private bool _wasClientInMultiplayer => _networkService?.WasClientInMultiplayer ?? false;
	public bool IsConnectionLost => _networkService?.IsConnectionLost ?? false;

	private System.Collections.Generic.Dictionary<int, Entity> _peerIdToPlayerEntityMap
		=> EcsWorld?.GetFieldOrDefault<NetworkMappingState, System.Collections.Generic.Dictionary<int, Entity>>(_worldEntity, s => s.PeerIdToPlayerEntityMap);

	private System.Collections.Generic.Dictionary<int, Entity> _serverToClientEntityMap
		=> EcsWorld?.GetFieldOrDefault<NetworkMappingState, System.Collections.Generic.Dictionary<int, Entity>>(_worldEntity, s => s.ServerToClientEntityMap);

	private System.Collections.Generic.Dictionary<int, int> _clientToServerEntityMap
		=> EcsWorld?.GetFieldOrDefault<NetworkMappingState, System.Collections.Generic.Dictionary<int, int>>(_worldEntity, s => s.ClientToServerEntityMap);
	public World EcsWorld => ServiceLocator.Get<WorldAccessor>().Current;
	public Entity WorldEntity => _worldEntity;
	public List<Realm.Client.Unit3D> SelectedUnits { get; set; } = new List<Realm.Client.Unit3D>();
	public List<Realm.Client.Unit3D> AllUnits { get; set; } = new List<Realm.Client.Unit3D>();
	public List<Realm.Client.Prop3D> AllProps { get; set; } = new List<Realm.Client.Prop3D>();
	public List<Decal> AllDecals { get; set; } = new List<Decal>();
	public List<ProceduralVfxInstance3D> AllVfx { get; set; } = new List<ProceduralVfxInstance3D>();
	private readonly List<Realm.Client.Unit3D> _castlesList = new();

	public static readonly Dictionary<Entity, Realm.Client.Unit3D> EntityToUnit3D = new();
	public static Dictionary<Entity, Realm.Client.Prop3D> EntityToProp3D { get; set; } = new();
	public static Dictionary<Entity, ProceduralVfxInstance3D> EntityToVfx3D { get; set; } = new();
	public static Dictionary<string, VfxAttachmentConfig> VfxRegistry
	{
		get => ServiceLocator.Get<RegistryService>().VfxRegistry;
		set => ServiceLocator.Get<RegistryService>().VfxRegistry = value;
	}

	public static bool TryGetUnit3D(Entity entity, out Realm.Client.Unit3D unit)
	{
		return EntityToUnit3D.TryGetValue(entity, out unit);
	}

	public static bool TryGetProp3D(Entity entity, out Realm.Client.Prop3D prop)
	{
		return EntityToProp3D.TryGetValue(entity, out prop);
	}

	public static bool TryGetVfx3D(Entity entity, out ProceduralVfxInstance3D vfx)
	{
		return EntityToVfx3D.TryGetValue(entity, out vfx);
	}

	private Entity _playerEntity
	{
		get => EcsWorld?.GetFieldOrDefault<NetworkMappingState, Entity>(_worldEntity, s => s.PlayerEntity, Entity.Null) ?? Entity.Null;
		set => EcsWorld?.Mutate<NetworkMappingState>(_worldEntity, (ref NetworkMappingState s) => s.PlayerEntity = value);
	}

	private Entity _enemyPlayerEntity
	{
		get => EcsWorld?.GetFieldOrDefault<NetworkMappingState, Entity>(_worldEntity, s => s.EnemyPlayerEntity, Entity.Null) ?? Entity.Null;
		set => EcsWorld?.Mutate<NetworkMappingState>(_worldEntity, (ref NetworkMappingState s) => s.EnemyPlayerEntity = value);
	}

	public int LocalPlayerIndex
	{
		get
		{
			if (_multiplayerActive && Realm.Client.Network.LobbyManager.Instance != null && Realm.Client.Network.LobbyManager.Instance.PlayerList.Count > 0)
			{
				var p = Realm.Client.Network.LobbyManager.Instance.PlayerList.Find(x => x.PeerId == _localPeerId);
				if (p != null) return p.Slot;
			}
			return 0;
		}
	}

	public Entity GetPlayerEntityForPlayerIndex(int playerIndex)
	{
		var pe = TryGetMultiplayerEntity(playerIndex);
		if (pe != Entity.Null) return pe;

		if (playerIndex == 0) return _playerEntity;
		if (_enemyPlayerEntity != Entity.Null && EcsWorld.IsAlive(_enemyPlayerEntity)) return _enemyPlayerEntity;

		return _playerEntity;
	}

	private Entity TryGetMultiplayerEntity(int playerIndex)
	{
		if (!_multiplayerActive || Realm.Client.Network.LobbyManager.Instance == null || Realm.Client.Network.LobbyManager.Instance.PlayerList.Count <= 0) return Entity.Null;
		var p = Realm.Client.Network.LobbyManager.Instance.PlayerList.Find(x => x.Slot == playerIndex);
		if (p == null || _peerIdToPlayerEntityMap?.TryGetValue(p.PeerId, out var pe) != true || !EcsWorld.IsAlive(pe)) return Entity.Null;
		return pe;
	}

	public bool IsPlayerEnemy(int playerIndex)
	{
		return NetworkService.ArePlayerIndicesEnemies(LocalPlayerIndex, playerIndex);
	}

	private Entity _worldEntity
	{
		get => ServiceLocator.Get<WorldAccessor>().WorldEntity;
		set => ServiceLocator.Get<WorldAccessor>().WorldEntity = value;
	}

	private int _replayTickCounter
	{
		get => EcsWorld?.GetFieldOrDefault<ReplayState, int>(_worldEntity, s => s.ReplayTickCounter) ?? 0;
		set => EcsWorld?.Mutate<ReplayState>(_worldEntity, (ref ReplayState s) => s.ReplayTickCounter = value);
	}
	private System.Diagnostics.Stopwatch _trackerTickStopwatch
	{
		get => ServiceLocator.Get<PerformanceTrackingService>().TrackerTickStopwatch;
		set => ServiceLocator.Get<PerformanceTrackingService>().TrackerTickStopwatch = value;
	}
	private System.Diagnostics.Stopwatch _trackerIntervalStopwatch
	{
		get => ServiceLocator.Get<PerformanceTrackingService>().TrackerIntervalStopwatch;
		set => ServiceLocator.Get<PerformanceTrackingService>().TrackerIntervalStopwatch = value;
	}
	private List<float> _trackerTickDurations
	{
		get => ServiceLocator.Get<PerformanceTrackingService>().TrackerTickDurations;
		set => ServiceLocator.Get<PerformanceTrackingService>().TrackerTickDurations = value;
	}
	private List<float> _trackerApiDurations
	{
		get => ServiceLocator.Get<PerformanceTrackingService>().TrackerApiDurations;
		set => ServiceLocator.Get<PerformanceTrackingService>().TrackerApiDurations = value;
	}
	private float _trackerLastTickDelay
	{
		get => ServiceLocator.Get<PerformanceTrackingService>().TrackerLastTickDelay;
		set => ServiceLocator.Get<PerformanceTrackingService>().TrackerLastTickDelay = value;
	}
	private bool _isResettingForReplay
	{
		get => ServiceLocator.Get<ReplayService>().IsResettingForReplay;
		set => ServiceLocator.Get<ReplayService>().IsResettingForReplay = value;
	}
	public string? ActiveSpellTargeting
	{
		get => _inputService?.ActiveSpellTargeting;
		set { if (_inputService != null) _inputService.ActiveSpellTargeting = value; }
	}

	public string? ActiveCommandTargeting
	{
		get => _inputService?.ActiveCommandTargeting;
		set { if (_inputService != null) _inputService.ActiveCommandTargeting = value; }
	}

	public Realm.Client.Prop3D? SelectedProp { get; set; }

	public string? ActiveBuildingPlacementType
	{
		get => _inputService?.ActiveBuildingPlacementType;
		set { if (_inputService != null) _inputService.ActiveBuildingPlacementType = value; }
	}

	public int CycleSelectionIndex
	{
		get => _inputService?.GetCycleSelectionIndex(SelectedUnits.Count) ?? 0;
		set => _inputService?.SetCycleSelectionIndex(value, SelectedUnits.Count);
	}

	public bool HasWeaponsUpgrade
	{
		get => EcsWorld?.GetFieldOrDefault<PlayerUpgrades, bool>(_playerEntity, u => u.WeaponsUpgrade) ?? false;
		set => EcsWorld?.Mutate(_playerEntity, (ref PlayerUpgrades u) => u.WeaponsUpgrade = value);
	}

	public bool HasShieldsUpgrade
	{
		get => EcsWorld?.GetFieldOrDefault<PlayerUpgrades, bool>(_playerEntity, u => u.ShieldsUpgrade) ?? false;
		set => EcsWorld?.Mutate(_playerEntity, (ref PlayerUpgrades u) => u.ShieldsUpgrade = value);
	}

	public bool HasHarvestingUpgrade
	{
		get => EcsWorld?.GetFieldOrDefault<PlayerUpgrades, bool>(_playerEntity, u => u.HarvestingUpgrade) ?? false;
		set => EcsWorld?.Mutate(_playerEntity, (ref PlayerUpgrades u) => u.HarvestingUpgrade = value);
	}


	private MeshInstance3D? _buildingPreviewMesh { get; set; }


	public bool IsMapEditorMode
	{
		get => _editorService.IsMapEditorMode;
		set => _editorService.IsMapEditorMode = value;
	}
	public bool IsLoadingMap
	{
		get => _saveLoadService.IsLoadingMap;
		set => _saveLoadService.IsLoadingMap = value;
	}
	public bool IsGameOver
	{
		get => ServiceLocator.Get<WorldAccessor>().IsGameOver;
		set => ServiceLocator.Get<WorldAccessor>().IsGameOver = value;
	}
	private Realm.Client.RuntimeTerrain _groundTerrain { get; set; }
	public Realm.Client.RuntimeTerrain GroundTerrain
	{
		get
		{
			if (_groundTerrain == null && (IsMapEditorMode || IsLoadingMap || (Realm.Client.Network.LobbyManager.Instance != null && Realm.Client.Network.LobbyManager.Instance.IsGameStarted)))
			{
				CreateGround();
			}
			return _groundTerrain;
		}
		private set
		{
			_groundTerrain = value;
			if (value != null && _editorService != null)
			{
				_editorService.SetTerrainSplatMap(value.SplatMap, value.CliffSplatMap);
			}
		}
	}
	private MeshInstance3D? _brushIndicatorMesh { get; set; }
	private MeshInstance3D? _cameraBoundsOverlayMesh { get; set; }
	private List<Vector3> _pathingVerticesCache { get; set; } = new();
	private List<Color> _pathingColorsCache { get; set; } = new();
	private List<int> _pathingIndicesCache { get; set; } = new();
	private bool _pathingOverlayVisible { get; set; } = true;
	public bool PathingOverlayVisible
	{
		get => _pathingOverlayVisible;
		set
		{
			_pathingOverlayVisible = value;
			UpdatePathingOverlay();
		}
	}
	public enum EditorTool
	{
		None,
		Raise,
		Lower,
		Height,
		Smooth,
		Plateau,
		PaintTexture,
		PlaceUnit,
		PlaceProp,
		PlaceDecal,
		PlaceVfx,
		DeleteObject,
		SelectMove,
		Eyedropper,
		Noise,
		Ramp,
		PlacePropClump,
		FloodFill,
		SelectArea,
		PasteArea,
		PaintPathing,
		FloodFillPathing,
		DrawCoordinate,
		Water,
		Measure
	}
	private EditorTool _activeEditorTool { get => _editorService.ActiveEditorTool; set => _editorService.ActiveEditorTool = value; }
	public EditorTool ActiveEditorTool
	{
		get => _activeEditorTool;
		set
		{
			FlushTerrainMeshAndPhysics();
			_activeEditorTool = value;
			_editorService?.SetIsPastingObject(false);
			if (value != EditorTool.SelectArea && value != EditorTool.PasteArea)
			{
				HideSelectionHighlight();
			}
			RebuildAllCoordinatePersistentMeshes();
			UpdatePathingOverlay();
		}
	}
	public string ActivePlaceId { get; set; } = ""; // "soldier", "tree", etc.

	public struct EditorCoordinate
	{
		public string Name;
		public float MinX;
		public float MinZ;
		public float MaxX;
		public float MaxZ;
	}

	public List<EditorCoordinate> EditorCoordinates { get => _editorService.EditorCoordinates; set => _editorService.EditorCoordinates = value; }
	public string GetTerrainStatusString(Vector3 hitPos)
	{
		return _editorService.GetTerrainStatusString(hitPos, ActiveEditorTool.ToString(), ActivePlaceId);
	}

	public void LoadMapProperties(string path)
	{
		_mapPropertiesLoader?.LoadMapProperties(_worldEntity, path);
	}

	public bool ImportTerrainFromMinimap(
		string selectedPath,
		out float[,] smoothedHeights,
		out TerrainSplatWeights[,] splatMap,
		out List<(float X, float Y, float Z, float Rot, float Scale)> treePositions)
	{
		smoothedHeights = null;
		splatMap = null;
		treePositions = null;
		if (_terrainImportService == null)
		{
			return false;
		}
		return _terrainImportService.ImportTerrain(_worldEntity, selectedPath, out smoothedHeights, out splatMap, out treePositions);
	}
	public static float MIN_BRUSH_RADIUS => Realm.Client.Services.EditorService.MIN_BRUSH_RADIUS;
	public static float MAX_BRUSH_RADIUS => Realm.Client.Services.EditorService.MAX_BRUSH_RADIUS;
	public static float MIN_BRUSH_STRENGTH => Realm.Client.Services.EditorService.MIN_BRUSH_STRENGTH;
	public static float MAX_BRUSH_STRENGTH => Realm.Client.Services.EditorService.MAX_BRUSH_STRENGTH;
	public static float MIN_PLACEMENT_SCALE => Realm.Client.Services.EditorService.MIN_PLACEMENT_SCALE;
	public static float MAX_PLACEMENT_SCALE => Realm.Client.Services.EditorService.MAX_PLACEMENT_SCALE;
	public static float MIN_CLUMP_COUNT => Realm.Client.Services.EditorService.MIN_CLUMP_COUNT;
	public const float MAX_CLUMP_COUNT = Realm.Client.Services.EditorService.MAX_CLUMP_COUNT;
	public const float MIN_CLUMP_SCALE = 0.0f;
	public const float MAX_CLUMP_SCALE = 1.0f;

	public static float MIN_CLUMP_DENSITY => MIN_CLUMP_COUNT;
	public const float MAX_CLUMP_DENSITY = MAX_CLUMP_COUNT;
	public const float MIN_CLUMP_SCALE_VAR = MIN_CLUMP_SCALE;
	public const float MAX_CLUMP_SCALE_VAR = MAX_CLUMP_SCALE;

	public bool PlaceUnitIsEnemy = false;
	private float _editorBrushRadius = 2.0f;
	public float EditorBrushRadius
	{
		get => _editorBrushRadius;
		set => _editorBrushRadius = Mathf.Clamp(value, MIN_BRUSH_RADIUS, MAX_BRUSH_RADIUS);
	}
	private float _editorBrushStrength = 3.0f;
	public float EditorBrushStrength
	{
		get => _editorBrushStrength;
		set => _editorBrushStrength = Mathf.Clamp(value, MIN_BRUSH_STRENGTH, MAX_BRUSH_STRENGTH);
	}
	public int EditorPaintTextureIndex = 3;
	public int EditorCliffPaintTextureIndex = 1;
	public bool EditorSnapToGrid = false;
	public float EditorPlacementRotation { get => _editorService.EditorPlacementRotation; set => _editorService.EditorPlacementRotation = value; }
	private float _editorPlacementScale { get => _editorService.EditorPlacementScale; set => _editorService.EditorPlacementScale = value; }
	public float EditorPlacementScale
	{
		get => _editorPlacementScale;
		set => _editorPlacementScale = Mathf.Clamp(value, MIN_PLACEMENT_SCALE, MAX_PLACEMENT_SCALE);
	}
	public enum GridOverlayMode { Off, Grid, Polar, Both }
	public GridOverlayMode EditorGridMode { get => _editorService.EditorGridMode; set => _editorService.EditorGridMode = value; }
	public bool EditorGridVisible => EditorGridMode == GridOverlayMode.Grid || EditorGridMode == GridOverlayMode.Both;
	public bool EditorCameraBoundsVisible { get => _editorService.EditorCameraBoundsVisible; set => _editorService.EditorCameraBoundsVisible = value; }
	public bool EditorDisableShadows { get => _editorService.EditorDisableShadows; set => _editorService.EditorDisableShadows = value; }
	public float EditorCameraBoundsLeft
	{
		get => _editorService.GetCameraBoundsLeft(_worldEntity);
		set => _editorService.SetCameraBoundsLeft(_worldEntity, value);
	}

	public float EditorCameraBoundsRight
	{
		get => _editorService.GetCameraBoundsRight(_worldEntity);
		set => _editorService.SetCameraBoundsRight(_worldEntity, value);
	}

	public float EditorCameraBoundsTop
	{
		get => _editorService.GetCameraBoundsTop(_worldEntity);
		set => _editorService.SetCameraBoundsTop(_worldEntity, value);
	}

	public float EditorCameraBoundsBottom
	{
		get => _editorService.GetCameraBoundsBottom(_worldEntity);
		set => _editorService.SetCameraBoundsBottom(_worldEntity, value);
	}
	public MirrorMode EditorMirrorMode
	{
		get => EcsWorld?.GetFieldOrDefault<EditorState, MirrorMode>(_worldEntity, s => s.MirrorMode, MirrorMode.None) ?? MirrorMode.None;
		set
		{
			EcsWorld?.Mutate<EditorState>(_worldEntity, (ref EditorState s) => s.MirrorMode = value);
			if (value == MirrorMode.Rotational)
			{
				EditorPolarRadialStep = 360.0f / Mathf.Max(1, EditorSymmetryFolds);
				GroundTerrain?.SetPolarRadialStep(EditorPolarRadialStep);
			}
			else
			{
				EditorPolarRadialStep = 360.0f / Mathf.Max(1, EditorPolarSpokeFolds);
				GroundTerrain?.SetPolarRadialStep(EditorPolarRadialStep);
			}
			UpdateGridOverlayVisibility();
		}
	}

	public Vector2 EditorSymmetryPivot
	{
		get => _editorService?.SymmetryPivot ?? Vector2.Zero;
		set
		{
			if (_editorService != null) _editorService.SymmetryPivot = value;
			UpdateSymmetryPivotVisuals();
		}
	}

	public int EditorSymmetryFolds
	{
		get => _editorService?.SymmetryFolds ?? 4;
		set
		{
			if (_editorService != null) _editorService.SymmetryFolds = value;
			if (EditorMirrorMode == MirrorMode.Rotational)
			{
				EditorPolarRadialStep = 360.0f / Mathf.Max(1, value);
				GroundTerrain?.SetPolarRadialStep(EditorPolarRadialStep);
			}
		}
	}

	public int EditorPolarSpokeFolds { get => _editorService.EditorPolarSpokeFolds; set => _editorService.EditorPolarSpokeFolds = value; }
	public bool EditorPolarOverlayVisible { get => _editorService.EditorPolarOverlayVisible; set => _editorService.EditorPolarOverlayVisible = value; }
	public float EditorPolarRingSpacing { get => _editorService.EditorPolarRingSpacing; set => _editorService.EditorPolarRingSpacing = value; }
	public float EditorPolarRadialStep { get => _editorService.EditorPolarRadialStep; set => _editorService.EditorPolarRadialStep = value; }

	public Vector3? EditorTapeMeasureStart { get => _editorService.EditorTapeMeasureStart; set => _editorService.EditorTapeMeasureStart = value; }
	public Vector3? EditorTapeMeasureEnd;
	public bool EditorTapeMeasureActive = false;

	public bool EditorBrushIsSquare = true;

	private float _editorClumpCount = 5.0f;
	public float EditorClumpCount
	{
		get => _editorClumpCount;
		set => _editorClumpCount = Mathf.Clamp(value, MIN_CLUMP_COUNT, MAX_CLUMP_COUNT);
	}

	public float EditorClumpDensity
	{
		get => EditorClumpCount;
		set => EditorClumpCount = value;
	}

	private float _editorClumpScale = 0.3f;
	public float EditorClumpScale
	{
		get => _editorClumpScale;
		set => _editorClumpScale = Mathf.Clamp(value, MIN_CLUMP_SCALE, MAX_CLUMP_SCALE);
	}

	public float EditorClumpScaleVar
	{
		get => EditorClumpScale;
		set => EditorClumpScale = value;
	}
	private bool _editorClumpMode = false;
	public bool EditorClumpMode
	{
		get => _editorClumpMode;
		set
		{
			_editorClumpMode = value;
			UpdateBrushMesh();
		}
	}

	public bool EditorRandomRotation = false;
	public bool EditorRandomScale = false;
	public string EditorSkyboxPath
	{
		get => _editorService.GetSkyboxPath(_worldEntity);
		set => _editorService.SetSkyboxPath(_worldEntity, value);
	}

	public bool EditorHasUnsavedChanges
	{
		get => _editorService.GetHasUnsavedChanges(_worldEntity);
		set => _editorService.SetHasUnsavedChanges(_worldEntity, value);
	}

	public bool EditorBlockMode
	{
		get => _editorService.GetBlockMode(_worldEntity);
		set => _editorService.SetBlockMode(_worldEntity, value);
	}

	public float EditorBlockLevelHeight
	{
		get => _editorService.GetBlockLevelHeight(_worldEntity);
		set => _editorService.SetBlockLevelHeight(_worldEntity, value);
	}

	private float _editorExactHeight = 0.0f;
	public float EditorExactHeight
	{
		get => _editorExactHeight;
		set => _editorExactHeight = Math.Clamp(value, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
	}

	public WaterType EditorWaterMode
	{
		get => _editorService.GetWaterMode(_worldEntity);
		set => _editorService.SetWaterMode(_worldEntity, value);
	}

	public byte ActiveWaterProfileIndex
	{
		get => _editorService.GetWaterProfileIndex(_worldEntity);
		set => _editorService.SetWaterProfileIndex(_worldEntity, value);
	}

	public float EditorWaterHeight = 0.9f;

	private Node? _hoveredEditorObject;
	private MeshInstance3D? _selectionHighlightMesh;
	private MeshInstance3D? _coordinatePreviewMesh { get; set; }
	private MeshInstance3D? _coordinateSelectionOutlineMesh { get; set; }
	private List<MeshInstance3D> _coordinatePersistentMeshes { get; set; } = new();



	public void GenerateNewRandomPlacementRotationAndScale()
	{
		_editorService.GenerateNewRandomPlacementRotationAndScale();
	}
	public bool PasteOptionTextures 
	{
		get => _editorService.PasteOptionTextures;
		set => _editorService.PasteOptionTextures = value;
	}
	public bool PasteOptionHeights
	{
		get => _editorService.PasteOptionHeights;
		set => _editorService.PasteOptionHeights = value;
	}
	public bool PasteOptionEntities
	{
		get => _editorService.PasteOptionEntities;
		set => _editorService.PasteOptionEntities = value;
	}
	public bool PasteOptionPathing
	{
		get => _editorService.PasteOptionPathing;
		set
		{
			_editorService.PasteOptionPathing = value;
			UpdatePathingOverlay();
		}
	}
	public float EditorPasteRotation
	{
		get => _editorService.EditorPasteRotation;
		set => _editorService.EditorPasteRotation = value;
	}
	public PasteReflection EditorPasteReflection
	{
		get => _editorService.EditorPasteReflection;
		set => _editorService.EditorPasteReflection = value;
	}

	public Node SelectedEditorObject
	{
		get => _selectedEditorObject;
		set
		{
			if (_selectedEditorObject == value) return;
			if (GodotObject.IsInstanceValid(_selectedEditorObject))
			{
				if (_selectedEditorObject is Realm.Client.Unit3D oldUnit)
				{
					oldUnit.IsSelected = false;
				}
				else if (_selectedEditorObject is Realm.Client.Prop3D oldProp)
				{
					oldProp.IsSelected = false;
				}
				else if ((_selectedEditorObject as Decal ?? FindDecalInParentChain(_selectedEditorObject)) is Decal oldDecal)
				{
					UpdateDecalSelectionRing(oldDecal, false);
				}
				else if (_selectedEditorObject is ProceduralVfxInstance3D oldVfx)
				{
					oldVfx.IsSelected = false;
				}
			}
			_selectedEditorObject = value;
			if (GodotObject.IsInstanceValid(_selectedEditorObject))
			{
				if (_selectedEditorObject is Realm.Client.Unit3D newUnit)
				{
					newUnit.IsSelected = true;
				}
				else if (_selectedEditorObject is Realm.Client.Prop3D newProp)
				{
					newProp.IsSelected = true;
				}
				else if ((_selectedEditorObject as Decal ?? FindDecalInParentChain(_selectedEditorObject)) is Decal newDecal)
				{
					UpdateDecalSelectionRing(newDecal, true);
				}
				else if (_selectedEditorObject is ProceduralVfxInstance3D newVfx)
				{
					newVfx.IsSelected = true;
				}
			}
			else
			{
				foreach (var decal in AllDecals)
				{
					if (GodotObject.IsInstanceValid(decal))
					{
						UpdateDecalSelectionRing(decal, false);
					}
				}
				foreach (var vfx in AllVfx)
				{
					if (GodotObject.IsInstanceValid(vfx))
					{
						vfx.IsSelected = false;
					}
				}
			}
			Realm.Client.UI.MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
			UpdateEditorCoverageOverlay();
		}
	}
	private Node? _selectedEditorObject { get; set; }
	private bool _isDraggingObject { get; set; }
	private Vector3 _dragObjectStartPos { get; set; }
	private Vector3 _dragObjectStartRot { get; set; }
	private Vector3 _dragObjectStartScale { get; set; }
	private bool _dragObjectStartIsEnemy { get; set; }
	private Vector3 _dragObjectStartHitPos
	{
		get => _editorService.DragObjectStartHitPos;
		set => _editorService.DragObjectStartHitPos = value;
	}
	private Vector2 _dragStartMousePos
	{
		get => _editorService.DragStartMousePos;
		set => _editorService.DragStartMousePos = value;
	}
	private Vector3 _dragStartGroundPos
	{
		get => _editorService.DragStartGroundPos;
		set => _editorService.DragStartGroundPos = value;
	}
	private bool _dragObjectHasMoved
	{
		get => _editorService.DragObjectHasMoved;
		set => _editorService.DragObjectHasMoved = value;
	}
	private Node3D _editorPreviewNode { get; set; }
	private string _editorPreviewType = "";
	private string _editorPreviewId = "";
	private bool _editorPreviewIsEnemy;
	public List<Realm.Client.Services.FXService.MinimapPing> ActivePings => Realm.Client.Services.ServiceLocator.Get<Realm.Client.Services.FXService>().ActivePings;
	public bool ActivePingMode
	{
		get => _inputService?.ActivePingMode ?? false;
		set { if (_inputService != null) _inputService.ActivePingMode = value; }
	}




	public enum AssetCategory
	{
		Glb,
		Animations,
		Audio,
		Sfx,
		Music,
		Textures,
		NoiseTextures,
		Noise,
		Decals,
		VfxSpritesheets,
		Vfx,
		Skyboxes,
		Ribbons,
		RibbonTextures,
		Icons,
		Ui,
		Shaders
	}

	public enum GlbSubCategory
	{
		Units,
		Buildings,
		Resources,
		Props,
		Projectiles,
		Character,
		Characters,
		Building,
		Resource,
		Environment,
		Prop,
		Projectile
	}

	public static int GetUnitPathingFlags(UnitMetadata meta)
	{
		return Instance?._unitSpawnService?.GetUnitPathingFlags(meta) ?? 8;
	}


	public int MaxPopulation
	{
		get => EcsWorld?.GetFieldOrDefault<PlayerPopulation, int>(_playerEntity, p => p.Max) ?? 0;
		private set => EcsWorld?.Mutate<PlayerPopulation>(_playerEntity, (ref PlayerPopulation p) =>
			EcsWorld.Set(_playerEntity, new PlayerPopulation(p.Current, value)));
	}

	public int CurrentPopulation
	{
		get => EcsWorld?.GetFieldOrDefault<PlayerPopulation, int>(_playerEntity, p => p.Current) ?? 0;
		set => EcsWorld?.Mutate<PlayerPopulation>(_playerEntity, (ref PlayerPopulation p) =>
			EcsWorld.Set(_playerEntity, new PlayerPopulation(value, p.Max)));
	}

	public float GameElapsedTime
	{
		get => EcsWorld?.GetFieldOrDefault<WorldState, float>(_worldEntity, s => s.GameElapsedTime) ?? 0f;
		private set => EcsWorld?.Mutate<WorldState>(_worldEntity, (ref WorldState s) =>
			EcsWorld.Set(_worldEntity, new WorldState(value, s.TimeOfDayIndex, s.TimeOfDayTimer, s.DayNightCycleEnabled)));
	}

	public int TimeOfDayIndex
		=> EcsWorld?.GetFieldOrDefault<WorldState, int>(_worldEntity, s => s.TimeOfDayIndex) ?? 0;

	private float TimeOfDayTimer
	{
		get => EcsWorld?.GetFieldOrDefault<WorldState, float>(_worldEntity, s => s.TimeOfDayTimer) ?? 0f;
		set => EcsWorld?.Mutate<WorldState>(_worldEntity, (ref WorldState s) =>
			EcsWorld.Set(_worldEntity, new WorldState(s.GameElapsedTime, s.TimeOfDayIndex, value, s.DayNightCycleEnabled)));
	}
	public static float TimeOfDayCycleDuration => Realm.Client.Services.ServiceLocator.Get<Realm.Client.Services.EnvironmentService>().TimeOfDayCycleDuration;

	public float GetPlayerSpellCooldown(string abilityId)
	{
		if (EcsWorld == null || _playerEntity == Entity.Null || !EcsWorld.IsAlive(_playerEntity)) return 0f;
		if (EcsWorld.Has<SpellCooldowns>(_playerEntity))
		{
			var scd = EcsWorld.Get<SpellCooldowns>(_playerEntity).Value;
			if (scd != null && scd.TryGetValue(abilityId, out float val)) return val;
		}
		return 0f;
	}

	public void SetPlayerSpellCooldown(string abilityId, float cooldown)
	{
		if (EcsWorld == null || _playerEntity == Entity.Null || !EcsWorld.IsAlive(_playerEntity)) return;
		if (EcsWorld.Has<SpellCooldowns>(_playerEntity))
		{
			var scd = EcsWorld.Get<SpellCooldowns>(_playerEntity).Value;
			if (scd != null) scd[abilityId] = cooldown;
		}
	}
	public static float ResourceCap => Realm.Client.Services.ServiceLocator.Get<Realm.Ecs.Services.WorldAccessor>().ResourceCap;



	public static Dictionary<StringName, UnitMetadata> UnitRegistry => Realm.Client.Services.ServiceLocator.TryGet<Realm.Client.Services.RegistryService>()?.UnitRegistry ?? new();
	public static Dictionary<StringName, UnitMetadata> BuildingRegistry => Realm.Client.Services.ServiceLocator.TryGet<Realm.Client.Services.RegistryService>()?.BuildingRegistry ?? new();
	public static Dictionary<StringName, PropMetadata> PropRegistry => Realm.Client.Services.ServiceLocator.TryGet<Realm.Client.Services.RegistryService>()?.PropRegistry ?? new();
	public static Dictionary<StringName, ResourceMetadata> ResourceRegistry => Realm.Client.Services.ServiceLocator.TryGet<Realm.Client.Services.RegistryService>()?.ResourceRegistry ?? new();
	public static Dictionary<StringName, WeaponMetadata> WeaponRegistry => Realm.Client.Services.ServiceLocator.TryGet<Realm.Client.Services.RegistryService>()?.WeaponRegistry ?? new();
	public static Dictionary<StringName, AttachmentMetadata> AttachmentRegistry => Realm.Client.Services.ServiceLocator.TryGet<Realm.Client.Services.RegistryService>()?.AttachmentRegistry ?? new();
	public static Dictionary<StringName, ItemMetadata> ItemRegistry => Realm.Client.Services.ServiceLocator.TryGet<Realm.Client.Services.RegistryService>()?.ItemRegistry ?? new();

	public static bool TryGetUnitOrBuildingMetadata(StringName objectId, out UnitMetadata meta)
	{
		meta = default;
		if (objectId.IsEmpty) return false;
		if (UnitRegistry.TryGetValue(objectId, out meta)) return true;
		if (BuildingRegistry != null && BuildingRegistry.TryGetValue(objectId, out meta)) return true;
		return false;
	}

	public static bool TryGetUnitOrBuildingMetadata(string? objectId, out UnitMetadata meta)
	{
		meta = default;
		if (string.IsNullOrEmpty(objectId)) return false;
		return TryGetUnitOrBuildingMetadata((StringName)objectId, out meta);
	}

	public string GetFallbackModelPath(string unitId, bool isBuilding)
	{
		return _unitSpawnService.GetFallbackModelPath(unitId, isBuilding);
	}

	private float _goldBackup
	{
		get => EcsWorld?.GetFieldOrDefault<ReplayState, float>(_worldEntity, s => s.GoldBackup, 500f) ?? 500f;
		set => EcsWorld?.Mutate<ReplayState>(_worldEntity, (ref ReplayState s) => s.GoldBackup = value);
	}

	private float _woodBackup
	{
		get => EcsWorld?.GetFieldOrDefault<ReplayState, float>(_worldEntity, s => s.WoodBackup, 400f) ?? 400f;
		set => EcsWorld?.Mutate<ReplayState>(_worldEntity, (ref ReplayState s) => s.WoodBackup = value);
	}

	private float _stoneBackup
	{
		get => EcsWorld?.GetFieldOrDefault<ReplayState, float>(_worldEntity, s => s.StoneBackup, 200f) ?? 200f;
		set => EcsWorld?.Mutate<ReplayState>(_worldEntity, (ref ReplayState s) => s.StoneBackup = value);
	}

	private IMapScript? _activeMapScript { get => Realm.Client.Services.ServiceLocator.TryGet<Realm.Client.Services.MapScriptService>()?.ActiveMapScript; set { var s = Realm.Client.Services.ServiceLocator.TryGet<Realm.Client.Services.MapScriptService>(); if (s != null) s.ActiveMapScript = value; } }
	public static string? PendingMapScriptPath { get => Realm.Client.Services.ServiceLocator.TryGet<Realm.Client.Services.MapScriptService>()?.PendingMapScriptPath; set { var s = Realm.Client.Services.ServiceLocator.TryGet<Realm.Client.Services.MapScriptService>(); if (s != null) s.PendingMapScriptPath = value; } }
	private System.Runtime.Loader.AssemblyLoadContext? _mapScriptLoadContext { get => Realm.Client.Services.ServiceLocator.TryGet<Realm.Client.Services.MapScriptService>()?.MapScriptLoadContext; set { var s = Realm.Client.Services.ServiceLocator.TryGet<Realm.Client.Services.MapScriptService>(); if (s != null) s.MapScriptLoadContext = value; } }

	private class MapScriptLoadContext : System.Runtime.Loader.AssemblyLoadContext
	{
		public MapScriptLoadContext() : base(isCollectible: true)
		{
			Resolving += OnResolving;
		}

		private System.Reflection.Assembly? OnResolving(System.Runtime.Loader.AssemblyLoadContext context, System.Reflection.AssemblyName assemblyName)
		{
			if (assemblyName.Name == "Realm.MapAPI")
			{
				foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
				{
					if (asm.GetName().Name == "Realm.MapAPI")
						return asm;
				}
			}
			return null;
		}
	}

	private bool DayNightCycleEnabled
	{
		get => EcsWorld?.GetFieldOrDefault<WorldState, bool>(_worldEntity, s => s.DayNightCycleEnabled, true) ?? true;
		set => EcsWorld?.Mutate<WorldState>(_worldEntity, (ref WorldState s) =>
			EcsWorld.Set(_worldEntity, new WorldState(s.GameElapsedTime, s.TimeOfDayIndex, s.TimeOfDayTimer, value)));
	}

	public event Action<IUnit>? OnUnitCreated;
	public event Action<IUnit, IUnit?>? OnUnitDied;
	public event Action<IUnit, IUnit, float>? OnUnitDamaged;
	public event Action<IUnit?, string, System.Numerics.Vector3>? OnSpellCast;
	public event Action<string, IUnit?>? OnPlayerChatMessage;
	public event Action<IUnit>? OnUnitSelected;
	public event Action<IUnit, IUnit>? OnUnitAttacked;

	public void TriggerPlayerChatMessage(string message)
	{
		IUnit? selected = null;
		if (SelectedUnits.Count > 0 && EcsWorld.IsAlive(SelectedUnits[0].Entity))
		{
			selected = GetUnitWrapper(SelectedUnits[0].Entity);
		}
		OnPlayerChatMessage?.Invoke(message, selected);
	}

	public void TriggerKillUnit(Realm.Client.Unit3D unit, bool executeDespawnShader = true, bool playDeathAnimation = true)
	{
		KillUnit(unit, executeDespawnShader, playDeathAnimation);
	}

	private void InitializePlayerResources(Entity playerEntity)
	{
		var resourcesDict = new Dictionary<ResourceId, int>
		{
			{ ServiceLocator.Get<PlayerResourceService>().GoldResourceId, 500 },
			{ ServiceLocator.Get<PlayerResourceService>().WoodResourceId, 400 },
			{ ServiceLocator.Get<PlayerResourceService>().StoneResourceId, 200 }
		};
		EcsWorld.Add(playerEntity, new PlayerResources(resourcesDict));
	}

	private void SetupPlayerEntityComponents(Entity playerEntity)
	{
		EcsWorld.Add(playerEntity, new PlayerPopulation(0, 0));
		EcsWorld.Add(playerEntity, new SpellCooldowns(new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)));
		EcsWorld.Add(playerEntity, new PlayerUpgrades(false, false, false));
	}

	private void SetupWorldEntityComponents()
	{
		int width = GroundTerrain != null ? GroundTerrain.Width : 128;
		int depth = GroundTerrain != null ? GroundTerrain.Depth : 128;
		float quadSize = GroundTerrain != null ? GroundTerrain.QuadSize : 2.0f;
		float cellSize = GroundTerrain != null ? GroundTerrain.CellSize : TerrainState.DefaultCellSize;
		var cells = GroundTerrain != null ? GroundTerrain.Cells : null;
		int[,] pathingCodes = GroundTerrain != null ? GroundTerrain.PathingCodes : null;
		DotRecast.Detour.DtNavMesh navMesh = GroundTerrain != null ? GroundTerrain.NavMesh : null;
		DotRecast.Detour.DtNavMeshQuery navMeshQuery = GroundTerrain != null ? GroundTerrain.NavMeshQuery : null;

		_worldEntity = _worldInitService.SetupWorldEntityComponents(
			width, depth, quadSize, cellSize,
			cells, pathingCodes, navMesh, navMeshQuery
		);
	}

	private int GetOwnerPeerId(bool isEnemy)
	{
		if (!isEnemy) return _localPeerId;
		
		if (_worldEntity != Entity.Null && EcsWorld.Has<NetworkMappingState>(_worldEntity))
		{
			foreach (var kvp in EcsWorld.Get<NetworkMappingState>(_worldEntity).PeerIdToPlayerEntityMap)
				if (kvp.Key != _localPeerId) return kvp.Key;
		}
		return -1;
	}

	private Entity GetPlayerOwner(int ownerPeerId, bool actualIsEnemy)
	{
		if (_peerIdToPlayerEntityMap != null && _peerIdToPlayerEntityMap.TryGetValue(ownerPeerId, out var pe) && EcsWorld.IsAlive(pe))
			return pe;
		if (actualIsEnemy && _enemyPlayerEntity != Entity.Null && EcsWorld.IsAlive(_enemyPlayerEntity))
			return _enemyPlayerEntity;
		return _playerEntity;
	}

	private List<IResourceNode> GetCachedResourceNodes()
	{
		var list = new List<IResourceNode>();
		foreach (var prop in AllProps)
		{
			if (GodotObject.IsInstanceValid(prop))
			{
				if (prop.PropId == "goldmine" || prop.PropId == "tree" || prop.PropId == "rock")
				{
					list.Add(new ResourceNode_WasmRuntime(prop));
				}
			}
		}
		return list;
	}

public static void EnsureMapProjectFiles(string mapDir)
	{
		string csprojPath = System.IO.Path.Combine(mapDir, "MapScript.csproj");
		string libDir = System.IO.Path.Combine(mapDir, "lib");
		System.IO.Directory.CreateDirectory(libDir);

		string vscodeDir = System.IO.Path.Combine(mapDir, ".vscode");
		System.IO.Directory.CreateDirectory(vscodeDir);
		string vscodeSettingsPath = System.IO.Path.Combine(vscodeDir, "settings.json");

		string projectRoot = PathUtils.GetProjectRoot();
		string templateDir = PathUtils.FindPath("MapTemplate");
		if (!System.IO.Directory.Exists(templateDir))
			templateDir = System.IO.Path.Combine(projectRoot, "..", "MapTemplate");

		CopyTemplateFile(PathUtils.FindPath("MapTemplate/.vscode/settings.json"), System.IO.Path.Combine(templateDir, ".vscode", "settings.json"), vscodeSettingsPath);
		
		string repoRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectRoot, ".."));
		EnsureMapApiDllsCopied(repoRoot, templateDir, libDir);

		CopyTemplateFile(PathUtils.FindPath("MapTemplate/MapScript.csproj"), System.IO.Path.Combine(templateDir, "MapScript.csproj"), csprojPath);
		CopyTemplateFile(PathUtils.FindPath("MapTemplate/Directory.Build.targets"), System.IO.Path.Combine(templateDir, "Directory.Build.targets"), System.IO.Path.Combine(mapDir, "Directory.Build.targets"));

		RestoreDotnetProject(csprojPath, mapDir);
	}

	private static void CopyTemplateFile(string primaryPath, string fallbackPath, string destPath)
	{
		string srcPath = System.IO.File.Exists(primaryPath) ? primaryPath : fallbackPath;
		if (System.IO.File.Exists(srcPath))
		{
			System.IO.File.WriteAllText(destPath, System.IO.File.ReadAllText(srcPath));
		}
	}

	private static void EnsureMapApiDllsCopied(string repoRoot, string templateDir, string libDir)
	{
		string sourceDll = System.IO.Path.Combine(repoRoot, "Realm.MapAPI", "bin", "Release", "net10.0", "Realm.MapAPI.dll");
		string sourceXml = System.IO.Path.Combine(repoRoot, "Realm.MapAPI", "bin", "Release", "net10.0", "Realm.MapAPI.xml");

		if (!System.IO.File.Exists(sourceDll))
		{
			sourceDll = System.IO.Path.Combine(repoRoot, "Realm.MapAPI", "bin", "Debug", "net10.0", "Realm.MapAPI.dll");
			sourceXml = System.IO.Path.Combine(repoRoot, "Realm.MapAPI", "bin", "Debug", "net10.0", "Realm.MapAPI.xml");
		}

		if (TryCopyMapApiDll(sourceDll, sourceXml, libDir)) return;

		string templateLib = System.IO.Path.Combine(templateDir, "lib");
		if (TryCopyMapApiDll(System.IO.Path.Combine(templateLib, "Realm.MapAPI.dll"), System.IO.Path.Combine(templateLib, "Realm.MapAPI.xml"), libDir)) return;

		string mapApiCsproj = System.IO.Path.Combine(repoRoot, "Realm.MapAPI", "Realm.MapAPI.csproj");
		if (!System.IO.File.Exists(mapApiCsproj)) return;

		GD.Print("Realm.MapAPI.dll not found. Auto-building Realm.MapAPI...");
		using var buildProcess = new System.Diagnostics.Process();
		buildProcess.StartInfo.FileName = "dotnet";
		buildProcess.StartInfo.Arguments = $"build \"{mapApiCsproj}\" -c Debug";
		buildProcess.StartInfo.CreateNoWindow = true;
		buildProcess.StartInfo.UseShellExecute = false;
		buildProcess.Start();
		buildProcess.WaitForExit();

		TryCopyMapApiDll(System.IO.Path.Combine(repoRoot, "Realm.MapAPI", "bin", "Debug", "net10.0", "Realm.MapAPI.dll"), System.IO.Path.Combine(repoRoot, "Realm.MapAPI", "bin", "Debug", "net10.0", "Realm.MapAPI.xml"), libDir);
	}

	private static bool TryCopyMapApiDll(string dllPath, string xmlPath, string libDir)
	{
		if (!System.IO.File.Exists(dllPath)) return false;

		CopyIfDifferentBytes(dllPath, System.IO.Path.Combine(libDir, "Realm.MapAPI.dll"));
		if (System.IO.File.Exists(xmlPath))
		{
			CopyIfDifferentBytes(xmlPath, System.IO.Path.Combine(libDir, "Realm.MapAPI.xml"));
			CopyIfDifferentBytes(xmlPath, System.IO.Path.Combine(libDir, "Realm.EditorAPI.xml"));
		}
		string pdbPath = System.IO.Path.ChangeExtension(dllPath, ".pdb");
		if (System.IO.File.Exists(pdbPath))
			CopyIfDifferentBytes(pdbPath, System.IO.Path.Combine(libDir, "Realm.MapAPI.pdb"));
		return true;
	}

	private static void CopyIfDifferentBytes(string src, string dst)
	{
		if (System.IO.File.Exists(dst))
		{
			var sInfo = new System.IO.FileInfo(src);
			var dInfo = new System.IO.FileInfo(dst);
			if (sInfo.Length == dInfo.Length && System.IO.File.ReadAllBytes(src).AsSpan().SequenceEqual(System.IO.File.ReadAllBytes(dst)))
				return;
		}
		System.IO.File.Copy(src, dst, true);
	}

	private static void RestoreDotnetProject(string csprojPath, string mapDir)
	{
		try
		{
			using var restoreProcess = new System.Diagnostics.Process();
			restoreProcess.StartInfo.FileName = "dotnet";
			restoreProcess.StartInfo.Arguments = $"restore \"{csprojPath}\"";
			restoreProcess.StartInfo.WorkingDirectory = mapDir;
			restoreProcess.StartInfo.CreateNoWindow = true;
			restoreProcess.StartInfo.UseShellExecute = false;
			restoreProcess.Start();
			restoreProcess.WaitForExit(10000);
		}
		catch { }
	}

	public void CreateFloatingTextInternal(string text, Vector3 position, Color color, float duration)
	{
		Callable.From(() =>
		{
			var label = new Label3D();
			label.Text = text;
			label.Modulate = color;
			label.OutlineModulate = Colors.Black;
			label.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
			label.Position = new Vector3(position.X, position.Y + 1.5f, position.Z);
			label.FontSize = 48;
			AddChild(label);

			var tween = CreateTween();
			if (tween != null)
			{
				tween.SetParallel(true);
				tween.TweenProperty(label, "position", label.Position + new Vector3(0, 2.0f, 0), duration);
				tween.TweenProperty(label, "modulate:a", 0.0f, duration);
				tween.Chain().TweenCallback(Callable.From(label.QueueFree));
			}
		}).CallDeferred();
	}

	private Dictionary<int, Label3D> _staticTextLabels { get; set; } = new();
	private int _nextStaticTextHandle { get; set; } = 1;

	public void PanCameraInternal(Vector3 position, float duration)
	{
		Callable.From(() =>
		{
			var camera = MainCamera ?? GetTree().Root.GetNodeOrNull<Camera3D>("Main/Camera3D");
			if (camera == null) return;

			var targetPos = new Vector3(position.X, camera.Position.Y, position.Z + 15.0f);
			if (duration <= 0.05f)
			{
				camera.Position = targetPos;
			}
			else
			{
				var tween = CreateTween();
				tween?.TweenProperty(camera, "position", targetPos, duration);
			}
		}).CallDeferred();
	}

	private int _nextTimerHandle { get => Realm.Client.Services.ServiceLocator.Get<Realm.Client.Services.TimerService>().NextTimerHandle; set => Realm.Client.Services.ServiceLocator.Get<Realm.Client.Services.TimerService>().NextTimerHandle = value; }
	private Dictionary<int, (float Interval, float Remaining, bool Repeating, Action Callback)> _scheduledTimers => Realm.Client.Services.ServiceLocator.Get<Realm.Client.Services.TimerService>().ScheduledTimers;

	private static Random Rng => Realm.Client.Services.ServiceLocator.Get<Realm.Client.Services.RandomService>().Rng;

	private void SyncPlayerResourceEcs(int playerIndex, float goldAmount)
	{
		if (Realm.Client.Network.LobbyManager.Instance != null && _peerIdToPlayerEntityMap != null)
		{
			var p = Realm.Client.Network.LobbyManager.Instance.PlayerList.Find(x => x.Slot == playerIndex);
			if (p != null && _peerIdToPlayerEntityMap.TryGetValue(p.PeerId, out var pe) && EcsWorld.IsAlive(pe))
			{
				if (EcsWorld.TryGet<PlayerResources>(pe, out var res))
				{
					res.Value[ServiceLocator.Get<PlayerResourceService>().GoldResourceId] = (int)Math.Max(0f, goldAmount);
				}
			}
		}
	}

	public event Action<IUnit, int>? OnUnitEnterZone;
	public event Action<int>? OnPlayerLeft;

	private void TickZoneTriggers()
	{
		if (OnUnitEnterZone == null || EcsWorld == null || !EcsWorld.IsAlive(_worldEntity) || !EcsWorld.Has<ScriptZonesState>(_worldEntity)) return;

		var zones = EcsWorld.Get<ScriptZonesState>(_worldEntity).Zones;
		if (zones.Count == 0) return;

		var positionQuery = Realm.Ecs.Common.QueryCache.AllPositionAndDefinitionIdNoneDeadQuery;
		EcsWorld.Query(in positionQuery, (Entity entity, ref Position posComp) =>
		{
			if (!EcsWorld.Has<OccupiedZones>(entity)) EcsWorld.Add(entity, new OccupiedZones(new HashSet<int>()));
			CheckUnitZones(entity, new Vector3(posComp.Value.X, posComp.Value.Y, posComp.Value.Z), EcsWorld.Get<OccupiedZones>(entity).ZoneIds, zones);
		});
	}

	private void CheckUnitZones(Entity entity, Vector3 pos, HashSet<int> occupiedZones, List<ZoneBounds> zones)
	{
		for (int i = 0; i < zones.Count; i++)
		{
			ref ZoneBounds z = ref System.Runtime.InteropServices.CollectionsMarshal.AsSpan(zones)[i];
			bool inside = pos.X >= z.MinX && pos.X <= z.MaxX && pos.Z >= z.MinZ && pos.Z <= z.MaxZ;

			if (inside && occupiedZones.Add(i) && TryGetUnit3D(entity, out _))
				OnUnitEnterZone?.Invoke(GetUnitWrapper(entity), i);
			else if (!inside)
				occupiedZones.Remove(i);
		}
	}


	public void NotifyPlayerLeft(int playerIndex)
	{
		OnPlayerLeft?.Invoke(playerIndex);
	}

	private void TickScheduledTimers(float delta)
	{
		if (_scheduledTimers.Count == 0) return;


		Span<int> keysBuffer = stackalloc int[Math.Min(_scheduledTimers.Count, 64)];
		int keyCount = 0;
		foreach (int k in _scheduledTimers.Keys)
		{
			if (keyCount < keysBuffer.Length)
				keysBuffer[keyCount++] = k;
		}

		List<int>? toRemove = null;
		for (int i = 0; i < keyCount; i++)
		{
			int handle = keysBuffer[i];
			if (!_scheduledTimers.TryGetValue(handle, out var entry)) continue;

			float remaining = entry.Remaining - delta;
			if (remaining <= 0f)
			{
				entry.Callback.Invoke();

				if (entry.Repeating)
				{
					_scheduledTimers[handle] = (entry.Interval, entry.Interval + remaining, true, entry.Callback);
				}
				else
				{
					toRemove ??= new List<int>();
					toRemove.Add(handle);
				}
			}
			else
			{
				_scheduledTimers[handle] = (entry.Interval, remaining, entry.Repeating, entry.Callback);
			}
		}

		if (toRemove != null)
		{
			foreach (int h in toRemove)
				_scheduledTimers.Remove(h);
		}
	}

	private static string SafeGlobalizePath(string path)
	{
		if (string.IsNullOrEmpty(path)) return path;
		if (MapAssetManager.IsGodotEngineRunning)
		{
			try
			{
				return Godot.ProjectSettings.GlobalizePath(path);
			}
			catch { }
		}

		if (path.StartsWith("user://"))
		{
			return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user", path.Substring("user://".Length));
		}
		if (path.StartsWith("res://"))
		{
			return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path.Substring("res://".Length));
		}
		return path;
	}

	public static string? ResolveMapDirectory(string mapNameOrPath, string? version = null)
	{
		if (string.IsNullOrWhiteSpace(mapNameOrPath)) return null;

		string norm = mapNameOrPath.Replace('\\', '/').Trim();
		string? explicitPath = TryResolveExplicitPath(norm);
		if (explicitPath != null) return explicitPath;

		string? targetVersion = !string.IsNullOrWhiteSpace(version) ? version : Realm.Client.Network.LobbyManager.Instance?.ActiveMapVersion;
		string? manifestPath = MapAssetManager.FindManifestPath(norm, targetVersion) ?? MapAssetManager.FindManifestPath(norm, null);

		if (!string.IsNullOrEmpty(manifestPath) && System.IO.File.Exists(manifestPath))
			return System.IO.Path.GetDirectoryName(manifestPath);

		return CheckMapDir(norm) ?? CheckMapDir(norm.ToLowerInvariant());
	}

	private static string? TryResolveExplicitPath(string norm)
	{
		if (!norm.StartsWith("user://") && !norm.StartsWith("res://") && !System.IO.Path.IsPathRooted(norm)) return null;
		
		string global = norm.StartsWith("user://") || norm.StartsWith("res://") ? SafeGlobalizePath(norm) : norm;
		if (System.IO.Directory.Exists(global)) return global;
		if (System.IO.File.Exists(global)) return System.IO.Path.GetDirectoryName(global);
		
		return null;
	}

	private static string? CheckMapDir(string name)
	{
		string resDir = SafeGlobalizePath($"res://Maps/{name}");
		if (System.IO.Directory.Exists(resDir)) return resDir;

		string userDir = SafeGlobalizePath($"user://maps/{name}");
		if (System.IO.Directory.Exists(userDir)) return userDir;

		return null;
	}

	public void LoadUnitMetadata(string mapName = null)
	{
		ResetAbilityCatalog();
		mapName = !string.IsNullOrEmpty(mapName) ? mapName : (!string.IsNullOrEmpty(ActiveMapName) ? ActiveMapName : Realm.Client.Services.MapWorkspaceService.DefaultWorkspaceFolder);
		ActiveMapName = mapName;
		LocalizationManager.CurrentMapName = mapName;
		LocalizationManager.SetupTranslations();

		string? resolvedDir = ResolveMapDirectory(mapName);
		string path = resolvedDir != null ? System.IO.Path.Combine(resolvedDir, "metadata.json") : (mapName.StartsWith("user://") || mapName.StartsWith("res://") || System.IO.Path.IsPathRooted(mapName) ? System.IO.Path.Combine(mapName, "metadata.json") : $"res://Maps/{mapName}/metadata.json");
		if (resolvedDir != null) CurrentMapDirectory = resolvedDir;

		var metadata = (_metadataService ?? Realm.Client.Services.MetadataService.Instance).LoadMetadata(path);

		UnitRegistry.Clear();
		BuildingRegistry.Clear();
		PropRegistry.Clear();
		ResourceRegistry.Clear();
		WeaponRegistry.Clear();
		AttachmentRegistry.Clear();
		ItemRegistry.Clear();
		VfxRegistry.Clear();

		if (metadata.Templates != null) LoadMetadataTemplates(metadata.Templates);

		Realm.Client.Prop3D.ClearModelPathCache();
	}

	private void LoadMetadataTemplates(TemplateContainer templates)
	{
		LoadWeaponAndAttachmentTemplates(templates);
		LoadItemTemplates(templates);
		LoadUnitTemplates(templates);
		LoadBuildingTemplates(templates);
		LoadResourceAndPropTemplates(templates);
		
		if (templates.Abilities != null && templates.Abilities.Count > 0) RegisterCustomAbilities(templates.Abilities);
		foreach (var cfg in templates.Vfx) if (!string.IsNullOrEmpty(cfg.VfxId)) VfxRegistry[cfg.VfxId] = cfg;
	}

	private void LoadWeaponAndAttachmentTemplates(TemplateContainer templates)
	{
		foreach (var meta in templates.Weapons) if (!string.IsNullOrEmpty(meta.TemplateID)) WeaponRegistry[(StringName)meta.TemplateID] = meta;
		foreach (var meta in templates.Attachments) if (!string.IsNullOrEmpty(meta.AttachmentId)) AttachmentRegistry[(StringName)meta.AttachmentId] = meta;
	}

	private void LoadItemTemplates(TemplateContainer templates)
	{
		foreach (var meta in templates.Items)
		{
			if (string.IsNullOrEmpty(meta.TemplateID)) continue;
			ItemRegistry[(StringName)meta.TemplateID] = meta;
			if (meta.TemplateID.StartsWith("item/", StringComparison.OrdinalIgnoreCase)) ItemRegistry[(StringName)meta.TemplateID.Substring(5)] = meta;
		}
	}

	private void LoadUnitTemplates(TemplateContainer templates)
	{
		foreach (var meta in templates.Units)
		{
			if (string.IsNullOrEmpty(meta.TemplateID)) continue;
			if (meta.Scale <= 0f) meta.Scale = 1.0f;
			UnitRegistry[(StringName)meta.TemplateID] = meta;
			if (meta.TemplateID.StartsWith("unit/", StringComparison.OrdinalIgnoreCase)) UnitRegistry[(StringName)meta.TemplateID.Substring(5)] = meta;
		}
	}

	private void LoadBuildingTemplates(TemplateContainer templates)
	{
		foreach (var meta in templates.Buildings)
		{
			if (string.IsNullOrEmpty(meta.TemplateID)) continue;
			if (meta.Scale <= 0f) meta.Scale = 1.5f;
			BuildingRegistry[(StringName)meta.TemplateID] = meta;
			if (meta.TemplateID.StartsWith("building/", StringComparison.OrdinalIgnoreCase)) BuildingRegistry[(StringName)meta.TemplateID.Substring(9)] = meta;
		}
	}

	private void LoadResourceAndPropTemplates(TemplateContainer templates)
	{
		foreach (var meta in templates.Resources)
		{
			if (string.IsNullOrEmpty(meta.TemplateID)) continue;
			if (meta.Scale <= 0f) meta.Scale = 2.75f;
			if (meta.PathingType == 0) meta.PathingType = 255;
			ResourceRegistry[(StringName)meta.TemplateID] = meta;
		}
		foreach (var meta in templates.Props)
		{
			if (string.IsNullOrEmpty(meta.TemplateID)) continue;
			if (meta.Scale <= 0f) meta.Scale = 1.25f;
			if (meta.PathingType == 0) meta.PathingType = 255;
			PropRegistry[(StringName)meta.TemplateID] = meta;
		}
	}

	public void SaveAttachmentDefaultsToMetadata(string fileName, AttachmentMetadata meta)
	{
		try
		{
			string dir = !string.IsNullOrEmpty(CurrentMapDirectory) ? CurrentMapDirectory : Godot.ProjectSettings.GlobalizePath(Realm.Client.UI.MapEditorHUD.TempWorkspaceGodotPath);
			var metadata = Realm.Shared.Services.MapFileService.LoadMetadata(dir);
			metadata.Templates ??= new TemplateContainer();
			metadata.Templates.Attachments ??= new List<AttachmentMetadata>();

			int existingIndex = metadata.Templates.Attachments.FindIndex(a => string.Equals(a.AttachmentId, meta.AttachmentId, StringComparison.OrdinalIgnoreCase) || string.Equals(a.AttachmentId, fileName, StringComparison.OrdinalIgnoreCase));
			if (existingIndex >= 0)
			{
				metadata.Templates.Attachments[existingIndex] = meta;
			}
			else
			{
				if (string.IsNullOrEmpty(meta.AttachmentId))
				{
					meta.AttachmentId = fileName;
				}
				metadata.Templates.Attachments.Add(meta);
			}

			Realm.Shared.Services.MapFileService.SaveMetadata(dir, metadata);
			LoadUnitMetadata(dir);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[GameHost] SaveAttachmentDefaultsToMetadata error: {ex.Message}");
		}
	}

	public void SaveUnitAnimationsToMetadata(string unitId, Dictionary<string, List<UnitAnimationEntry>> animations)
	{
		try
		{
			string dir = !string.IsNullOrEmpty(CurrentMapDirectory) ? CurrentMapDirectory : Godot.ProjectSettings.GlobalizePath(Realm.Client.UI.MapEditorHUD.TempWorkspaceGodotPath);
			var metadata = Realm.Shared.Services.MapFileService.LoadMetadata(dir);
			var unit = metadata.GetUnit(unitId);
			if (unit != null)
			{
				unit.Animations = animations;
				Realm.Shared.Services.MapFileService.SaveMetadata(dir, metadata);
				LoadUnitMetadata(dir);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[GameHost] SaveUnitAnimationsToMetadata error: {ex.Message}");
		}
	}

	private void LoadMapScript(string mapName)
	{
		_activeMapScript = null;
		IsGameOver = false;

		if (string.IsNullOrEmpty(PendingMapScriptPath))
		{
			PendingMapScriptPath = ResolveWasmPath(mapName);
		}

		if (!string.IsNullOrEmpty(PendingMapScriptPath))
		{
			LoadPendingMapScript();
		}
		else
		{
			LoadEmbeddedMapScript(mapName);
		}

		if (_activeMapScript == null) _activeMapScript = new EmptyMapScript();
	}

	private string? ResolveWasmPath(string mapName)
	{
		string norm = mapName.Replace('\\', '/');
		string? checkDir = ResolveMapDirectory(mapName) ?? ResolveExplicitWasmDir(norm);

		if (string.IsNullOrEmpty(checkDir) || !System.IO.Directory.Exists(checkDir)) return null;

		var allWasm = System.IO.Directory.GetFiles(checkDir, "*.wasm", System.IO.SearchOption.AllDirectories)
			.Where(f => !f.Contains("native") && !f.Contains("obj")).ToList();
		
		if (allWasm.Count == 0) return null;

		return FindBestWasmMatch(allWasm);
	}

	private string? ResolveExplicitWasmDir(string norm)
	{
		if (norm.StartsWith("user://") || norm.StartsWith("res://") || System.IO.Path.IsPathRooted(norm))
			return norm.StartsWith("user://") || norm.StartsWith("res://") ? ProjectSettings.GlobalizePath(norm) : norm;
		return null;
	}

	private string? FindBestWasmMatch(System.Collections.Generic.List<string> allWasm)
	{
		return allWasm.FirstOrDefault(f => f.Contains("publish") && System.IO.Path.GetFileName(f).Equals("MapScript.wasm", StringComparison.OrdinalIgnoreCase))
			?? allWasm.FirstOrDefault(f => f.Contains("publish"))
			?? allWasm.FirstOrDefault(f => System.IO.Path.GetFileName(f).Equals("MapScript.wasm", StringComparison.OrdinalIgnoreCase))
			?? allWasm.OrderByDescending(System.IO.File.GetLastWriteTimeUtc).FirstOrDefault();
	}

	private void LoadPendingMapScript()
	{
		if (_mapScriptLoadContext != null)
		{
			_mapScriptLoadContext.Unload();
			_mapScriptLoadContext = null;
		}

		try
		{
			GD.Print($"[{DateTime.Now:HH:mm:ss}] GameHost LoadMapScript: PendingMapScriptPath={PendingMapScriptPath}");
			if (PendingMapScriptPath.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase))
			{
				_activeMapScript = new WasmRuntime(PendingMapScriptPath, ResolveWasmName(PendingMapScriptPath));
			}
			else
			{
				_mapScriptLoadContext = new MapScriptLoadContext();
				using var fs = new System.IO.FileStream(PendingMapScriptPath, System.IO.FileMode.Open, System.IO.FileAccess.Read);
				var asm = _mapScriptLoadContext.LoadFromStream(fs);
				_activeMapScript = FindMapScriptInAssembly(asm);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to load pending map script from {PendingMapScriptPath}: {ex.Message}");
		}
		finally
		{
			PendingMapScriptPath = null;
		}
	}

	private string ResolveWasmName(string path)
	{
		string name = System.IO.Path.GetFileNameWithoutExtension(path);
		if (!name.Equals("MapScript", StringComparison.OrdinalIgnoreCase)) return name;

		string parentDir = System.IO.Path.GetDirectoryName(path);
		string[] ignore = { "bin", "Release", "net10.0", "wasi-wasm", "publish" };
		
		while (!string.IsNullOrEmpty(parentDir))
		{
			string folderName = System.IO.Path.GetFileName(parentDir);
			if (!string.IsNullOrEmpty(folderName) && !ignore.Contains(folderName, StringComparer.OrdinalIgnoreCase))
				return folderName;
			parentDir = System.IO.Path.GetDirectoryName(parentDir);
		}
		return name;
	}

	private void LoadEmbeddedMapScript(string mapName)
	{
		string searchName = mapName?.Replace("_", "").ToLower() ?? string.Empty;
		if (string.IsNullOrEmpty(searchName)) return;

		foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
		{
			_activeMapScript = FindMapScriptInAssembly(assembly, searchName);
			if (_activeMapScript != null) break;
		}
	}

	private IMapScript? FindMapScriptInAssembly(System.Reflection.Assembly asm, string? searchName = null)
	{
		Type?[]? types = GetAssemblyTypesSafe(asm);
		if (types == null) return null;

		foreach (var t in types)
		{
			if (!IsValidMapScriptType(t, searchName)) continue;
			
			try { return (IMapScript?)Activator.CreateInstance(t); }
			catch { }
		}
		return null;
	}

	private Type?[]? GetAssemblyTypesSafe(System.Reflection.Assembly asm)
	{
		try { return asm.GetTypes(); }
		catch (System.Reflection.ReflectionTypeLoadException ex) { return ex.Types; }
		catch { return null; }
	}

	private bool IsValidMapScriptType(Type? t, string? searchName)
	{
		if (t == null || !typeof(IMapScript).IsAssignableFrom(t) || t.IsInterface || t.IsAbstract || typeof(IWasmRuntime).IsAssignableFrom(t) || t.GetConstructor(Type.EmptyTypes) == null) return false;
		if (searchName != null && !t.Name.ToLower().Contains(searchName) && !searchName.Contains(t.Name.ToLower())) return false;
		return true;
	}

	private List<Realm.Client.Unit3D>[] _controlGroups { get; set; } = new List<Realm.Client.Unit3D>[10];
	public List<Realm.Client.Unit3D>[] ControlGroups => _controlGroups;
	private double[] _lastGroupPressTime { get; set; } = new double[10];


	private bool _isDragging { get; set; }
	private Vector2 _dragStart { get; set; }
	private Vector2 _dragEnd { get; set; }
	private float DragThreshold { get; set; } = 8f;

	public override void _Ready()
	{
		MainNode = GetTree().Root.GetNodeOrNull("Main");
		MainCamera = GetTree().Root.GetNodeOrNull<Camera3D>("Main/Camera3D");

		GD.Print($"[GAMEHOST_READY] GameHost _Ready starting");
		Instance = this;
		GameSettings.ApplyGraphicsSettings(this);
		if (Realm.Client.Network.LobbyManager.Instance != null)
		{
			Realm.Client.Network.LobbyManager.Instance.ServerChatCommandReceived += HandleServerChatCommand;
		}

		ReinitializeEcsAndServices();
	}

	private void InitializeGameEcs()
	{
		ResolveServices();

		if (System.OperatingSystem.IsWindows()) Realm.Client.VSCodeManager.Instance.StartInstallIfNeeded();

		InitializeTracking();

		CreateGround();
		EnsurePropMultiMeshManager();
		SetupWorldEntityComponents();

		if (GroundTerrain != null) _editorService.SetTerrainSplatMap(GroundTerrain.SplatMap, GroundTerrain.CliffSplatMap);

		SetupSkybox();
		UpdateDayNightVisuals(0.0f);

		InitializeResourceIds();

		InitializeSimulationService();

		if (_multiplayerActive) InitializeMultiplayerPlayers();
		else InitializeSingleplayerPlayers();
		
		EnsureFallbackPlayers();

		string rawMapName = Realm.Client.Network.LobbyManager.Instance?.ActiveMapName ?? "";
		if (!string.IsNullOrEmpty(rawMapName)) LoadUnitMetadata(rawMapName);

		ProcessMapLoadingAndScript(rawMapName);

		StartReplayRecordingIfNeeded(rawMapName);

		if (!_isResettingForReplay) SchedulePostInitializationTasks();

		_shroudService.Initialize(MainNode);
	}

	private void InitializeTracking()
	{
		if (Multiplayer.MultiplayerPeer != null && !Multiplayer.IsServer()) return;

		_trackerTickDurations = new List<float>(100000);
		_trackerApiDurations = new List<float>(10000);
		_trackerIntervalStopwatch.Start();
	}

	private void EnsurePropMultiMeshManager()
	{
		if (GetNodeOrNull<Realm.Client.PropMultiMeshManager>("PropMultiMeshManager") != null) return;
		
		var propMultiMeshManager = new Realm.Client.PropMultiMeshManager();
		AddChild(propMultiMeshManager);
	}

	private void InitializeResourceIds()
	{
		ServiceLocator.Get<PlayerResourceService>().GoldResourceId = "gold".AsResourceId(DefinitionManager);
		ServiceLocator.Get<PlayerResourceService>().WoodResourceId = "wood".AsResourceId(DefinitionManager);
		ServiceLocator.Get<PlayerResourceService>().StoneResourceId = "stone".AsResourceId(DefinitionManager);
	}

	private void InitializeSimulationService()
	{
		_simulationService.SetRuntimeReferences(AllUnits, AllProps, _castlesList, DefinitionManager, ServiceLocator.Get<PlayerResourceService>().GoldResourceId, ServiceLocator.Get<PlayerResourceService>().WoodResourceId, ServiceLocator.Get<PlayerResourceService>().StoneResourceId, GroundTerrain);
		_simulationService.Initialize();
		_simulationService.EditorHeightProvider = p => _editorService.GetTerrainHeightAt(new Vector3(p.X, p.Y, p.Z));

		SetupSimulationServiceCallbacks();
	}

	private void SetupSimulationServiceCallbacks()
	{
		_simulationService.OnArrowProjectileRequested = (start, target) => SpawnArrowProjectile(new Vector3(start.X, start.Y, start.Z), new Vector3(target.X, target.Y, target.Z));
		_simulationService.OnWeaponProjectileRequested = (start, target, weaponId, targetEnt) => SpawnWeaponProjectile(new Vector3(start.X, start.Y, start.Z), new Vector3(target.X, target.Y, target.Z), weaponId, targetEnt);
		_simulationService.OnDamageFlashRequested = entity => { if (TryGetUnit3D(entity, out var unit3D)) CallDeferred(nameof(FlashDamageUnit), unit3D); };
		_simulationService.OnHealEffectRequested = (start, target) => SpawnHealVisualEffect(new Vector3(start.X, start.Y, start.Z), new Vector3(target.X, target.Y, target.Z));
		_simulationService.OnHealFlashRequested = entity => { if (TryGetUnit3D(entity, out var unit3D)) CallDeferred(nameof(FlashHealUnit), unit3D); };
		
		_simulationService.OnKillUnitRequested = entity =>
		{
			_warnedNonFinitePositions.Remove(entity);
			if (EcsWorld.IsAlive(entity) && TryGetUnit3D(entity, out var unit3D)) CallDeferred(nameof(KillUnit), unit3D);
		};
		
		_simulationService.OnPropDepleted = entity => { if (TryGetProp3D(entity, out var prop3D)) CallDeferred(nameof(DepleteProp), prop3D); };
		_simulationService.OnResourceHarvested = entity => { if (TryGetProp3D(entity, out var prop3D)) prop3D.TriggerImpulse(0.35f, 0.45f); };
		
		SetupSimulationCombatCallbacks();
		SetupSimulationProductionCallbacks();
	}

	private void SetupSimulationCombatCallbacks()
	{
		_simulationService.OnUnitDamagedCallback = HandleUnitDamaged;
		_simulationService.OnUnitHealedCallback = HandleUnitHealed;
		_simulationService.OnUnitAttackedCallback = HandleUnitAttacked;
		_simulationService.OnUnderAttackAlertRequested = HandleUnderAttackAlert;
	}

	private void HandleUnitDamaged(Entity targetEntity, Entity attackerEntity, float damage)
	{
		if (!EcsWorld.IsAlive(targetEntity)) return;
		
		IUnit attackerWrapper = EcsWorld.IsAlive(attackerEntity) ? GetUnitWrapper(attackerEntity) : null;
		OnUnitDamaged?.Invoke(GetUnitWrapper(targetEntity), attackerWrapper, damage);
		
		if (TryGetUnit3D(targetEntity, out var targetUnit3D))
		{
			_fxService.SpawnDamageNumber(this, targetUnit3D.GlobalPosition, damage);
			_audioService?.PlayUnitSound(targetUnit3D.UnitId, UnitSoundEvent.Wounded, targetUnit3D.GlobalPosition);
			targetUnit3D.TriggerImpulse(0.5f, 0.35f);
		}
	}

	private void HandleUnitHealed(Entity targetEntity, Entity healerEntity, float healAmount)
	{
		if (EcsWorld.IsAlive(targetEntity) && TryGetUnit3D(targetEntity, out var targetUnit3D))
			_fxService.SpawnHealNumber(this, targetUnit3D.GlobalPosition, healAmount);
	}

	private void HandleUnitAttacked(Entity attackerEntity, Entity targetEntity)
	{
		if (!EcsWorld.IsAlive(attackerEntity) || !EcsWorld.IsAlive(targetEntity)) return;
		
		if (TryGetUnit3D(attackerEntity, out var attackerUnit3D)) attackerUnit3D.PlayAnimation("Attack");
		OnUnitAttacked?.Invoke(GetUnitWrapper(attackerEntity), GetUnitWrapper(targetEntity));
	}

	private void HandleUnderAttackAlert(string unitId)
	{
		string alertMsg = unitId == "castle" ? "⚠️ YOUR CASTLE IS UNDER ATTACK!" : $"⚠️ {unitId.ToUpper()} is under attack!";
		Realm.Client.UI.InGameHUD.Instance?.CallDeferred(nameof(Realm.Client.UI.InGameHUD.ShowFeedbackText), alertMsg, new Color(1.0f, 0.2f, 0.1f));
		Realm.Client.UI.UIManager.Instance?.CallDeferred(nameof(Realm.Client.UI.UIManager.PlayWarningSound));
	}

	private void SetupSimulationProductionCallbacks()
	{
		_simulationService.OnSpawnUnitFromProductionRequested = (unitId, position, isEnemy, buildingEntity, isFromQueue) =>
			SpawnUnitFromProduction(unitId, position, isEnemy, buildingEntity, isFromQueue);
			
		_simulationService.GetProductionBuildTime = unitId =>
			UnitRegistry.TryGetValue(unitId, out var meta) ? meta.ProductionTime : 5f;
			
		_simulationService.OnClearUnitOrdersRequested = ClearUnitOrders;
		_simulationService.OnStopGatheringMovementRequested = StopGatheringMovement;
		_simulationService.OnUiRefreshRequested = () => Realm.Client.UI.InGameHUD.Instance?.RefreshUI(SelectedUnits);
		
		_simulationService.OnResourceDepositedForPlayer = (resType, carry) =>
		{
			string resTypeUpper = resType.ToUpper();
			Realm.Client.UI.InGameHUD.Instance?.CallDeferred(nameof(Realm.Client.UI.InGameHUD.ShowFeedbackText), $"+{carry:F0} {resTypeUpper} deposited", new Color(0.2f, 0.9f, 0.4f));
		};
		
		_simulationService.OnProductionCompleted = unitToSpawn =>
		{
			string displayName = UnitRegistry.TryGetValue(unitToSpawn, out var nm) ? nm.Name : unitToSpawn.ToUpper();
			Realm.Client.UI.InGameHUD.Instance?.CallDeferred(nameof(Realm.Client.UI.InGameHUD.ShowFeedbackText), $"✓ {displayName} training complete!", new Color(0.3f, 0.9f, 0.4f));
		};
	}

	private void InitializeMultiplayerPlayers()
	{
		_localPeerId = Multiplayer.GetUniqueId();
		if (!Multiplayer.IsServer()) _networkService.MarkClientEnteredMultiplayer();
		if (Multiplayer is SceneMultiplayer sceneMultiplayer) sceneMultiplayer.ServerRelay = false;

		if (Realm.Client.Network.LobbyManager.Instance != null && Realm.Client.Network.LobbyManager.Instance.PlayerList.Count > 0)
			SetupLobbyPlayers();
		else
			SetupDefaultPlayers();
	}

	private void SetupLobbyPlayers()
	{
		int pIdx = 0;
		foreach (var p in Realm.Client.Network.LobbyManager.Instance.PlayerList)
		{
			var playerEntity = CreatePlayerEntity(p.Name);
			_peerIdToPlayerEntityMap[p.PeerId] = playerEntity;
			
			if (p.PeerId == _localPeerId) _playerEntity = playerEntity;
			else _enemyPlayerEntity = playerEntity;
			
			UpdateScriptPlayerState(pIdx, p.Name);
			pIdx++;
		}
	}

	private void InitializeSingleplayerPlayers()
	{
		SetupDefaultPlayers();
	}

	private void SetupDefaultPlayers()
	{
		_playerEntity = CreatePlayerEntity("Horaid_Topa");
		_peerIdToPlayerEntityMap[1] = _playerEntity;

		_enemyPlayerEntity = CreatePlayerEntity("Enemy_AI");
		_peerIdToPlayerEntityMap[-1] = _enemyPlayerEntity;

		UpdateScriptPlayerState(0, "Horaid_Topa");
		UpdateScriptPlayerState(1, "Enemy_AI");
	}

	private Entity CreatePlayerEntity(string name)
	{
		var playerEntity = EcsWorld.Create();
		EcsWorld.Add(playerEntity, new Player());
		EcsWorld.Add(playerEntity, new Name(name));
		InitializePlayerResources(playerEntity);
		SetupPlayerEntityComponents(playerEntity);
		return playerEntity;
	}

	private void UpdateScriptPlayerState(int pIdx, string name)
	{
		if (!EcsWorld.Has<ScriptPlayersState>(_worldEntity)) return;
		
		var players = EcsWorld.Get<ScriptPlayersState>(_worldEntity).Players;
		if (pIdx < players.Length)
		{
			players[pIdx].Name = name;
			players[pIdx].Active = true;
		}
	}

	private void EnsureFallbackPlayers()
	{
		if (_playerEntity == Entity.Null)
		{
			_playerEntity = CreatePlayerEntity("Horaid_Topa");
			_peerIdToPlayerEntityMap[1] = _playerEntity;
			UpdateScriptPlayerState(0, "Horaid_Topa");
		}

		if (_enemyPlayerEntity == Entity.Null)
		{
			_enemyPlayerEntity = CreatePlayerEntity("Enemy_AI");
			_peerIdToPlayerEntityMap[-1] = _enemyPlayerEntity;
			UpdateScriptPlayerState(1, "Enemy_AI");
		}
	}

	private void ProcessMapLoadingAndScript(string rawMapName)
	{
		if (!ShouldProcessMapLoading()) return;

		if (ShouldLoadCustomTerrain())
			LoadCustomTerrain(rawMapName);

		if (ShouldLoadAndRunMapScript())
			ExecuteMapScriptLoading(rawMapName);
	}

	private bool ShouldProcessMapLoading()
	{
		bool isGameStarted = Realm.Client.Network.LobbyManager.Instance?.IsGameStarted ?? false;
		return isGameStarted || IsMapEditorMode;
	}

	private bool ShouldLoadCustomTerrain()
	{
		return !IsMapEditorMode && !IsLoadingMap;
	}

	private bool ShouldLoadAndRunMapScript()
	{
		if (IsLoadingMap) return false;
		
		bool isGameStarted = Realm.Client.Network.LobbyManager.Instance?.IsGameStarted ?? false;
		return !isGameStarted || IsServerActive() || IsMapEditorMode;
	}

	private void ExecuteMapScriptLoading(string rawMapName)
	{
		LoadMapScript(rawMapName);
		if (_activeMapScript != null && !IsMapEditorMode) 
			_activeMapScript.Initialize(this);
		RebakeNavMesh();
	}

	private void LoadCustomTerrain(string rawMapName)
	{
		string targetPath = ResolveCustomTerrainPath(rawMapName);
		
		if (!string.IsNullOrEmpty(targetPath) && (targetPath.StartsWith("user://") || targetPath.StartsWith("res://")))
			targetPath = ProjectSettings.GlobalizePath(targetPath);

		if (!string.IsNullOrEmpty(targetPath) && System.IO.File.Exists(targetPath))
			LoadMapFromFile(targetPath, false, true);
	}

	private string ResolveCustomTerrainPath(string rawMapName)
	{
		string? resolvedDir = ResolveMapDirectory(rawMapName);
		if (!string.IsNullOrEmpty(resolvedDir))
		{
			CurrentMapDirectory = resolvedDir;
			string checkTerrain = System.IO.Path.Combine(resolvedDir, "terrain.json");
			if (System.IO.File.Exists(checkTerrain)) return checkTerrain;
			return string.Empty;
		}
		
		return TryResolveExplicitCustomTerrainPath(rawMapName) ?? TryResolveFallbackCustomTerrainPath(rawMapName);
	}

	private string? TryResolveExplicitCustomTerrainPath(string rawMapName)
	{
		string normalizedRawMapName = rawMapName.Replace('\\', '/');
		if (!normalizedRawMapName.StartsWith("user://") && !normalizedRawMapName.StartsWith("res://") && !System.IO.Path.IsPathRooted(normalizedRawMapName))
			return null;
			
		string checkDir = normalizedRawMapName;
		if (normalizedRawMapName.StartsWith("user://") || normalizedRawMapName.StartsWith("res://"))
			checkDir = ProjectSettings.GlobalizePath(normalizedRawMapName);
			
		if (System.IO.Directory.Exists(checkDir))
			return System.IO.Path.Combine(checkDir, "terrain.json");
			
		return null;
	}
	
	private string TryResolveFallbackCustomTerrainPath(string rawMapName)
	{
		string normalizedMapName = rawMapName.ToLower().Trim();
		string mapDir = $"res://Maps/{normalizedMapName}";
		if (System.IO.Directory.Exists(ProjectSettings.GlobalizePath(mapDir)))
			return $"res://Maps/{normalizedMapName}/terrain.json";
			
		return string.Empty;
	}

	private void StartReplayRecordingIfNeeded(string rawMapName)
	{
		bool isGameStarted = Realm.Client.Network.LobbyManager.Instance != null && Realm.Client.Network.LobbyManager.Instance.IsGameStarted;
		if (!isGameStarted || IsMapEditorMode || ReplayPlaybackManager.Instance.IsPlayingReplay || !GameSettings.RecordReplays) return;

		string replayDir = ProjectSettings.GlobalizePath("user://replays");
		string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
		string replayPath = System.IO.Path.Combine(replayDir, $"replay_{timestamp}.rep");
		
		_replayService.StartRecording(replayPath, rawMapName, Realm.Client.Network.LobbyManager.Instance?.PlayerList);
		GD.Print($"[ReplayRecorder] Started recording to {replayPath}");
	}

	private void SchedulePostInitializationTasks()
	{
		var timer = GetTree().CreateTimer(0.1f);
		timer.Timeout += () =>
		{
			if (Realm.Client.UI.InGameHUD.Instance != null)
			{
				Realm.Client.UI.InGameHUD.Instance.Gold = _goldBackup;
				Realm.Client.UI.InGameHUD.Instance.Wood = _woodBackup;
				Realm.Client.UI.InGameHUD.Instance.Stone = _stoneBackup;
				Realm.Client.UI.InGameHUD.Instance.RefreshUI(SelectedUnits);
			}

			if (ReplayPlaybackManager.Instance.IsPlayingReplay)
				ReplayPlaybackManager.Instance.ApplyInitialFrame();
		};
	}

	public void ResetWorldAndState()
	{
		ReplayPlaybackManager.Instance.StopReplay();
		StopRecording();

		ClearNodeCollection(AllUnits);
		SelectedUnits.Clear();
		ClearNodeCollection(AllProps);
		ClearNodeCollection(AllDecals);

		_castlesList.Clear();
		ActivePings.Clear();
		ClearAllBuildQueueGhosts();

		if (_controlGroups != null)
		{
			for (int i = 0; i < _controlGroups.Length; i++)
			{
				_controlGroups[i]?.Clear();
			}
		}

		EntityToUnit3D.Clear();
		EntityToProp3D.Clear();
		_activeMapScript = null;

		ReinitializeEcsAndServices();
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
		EntityToUnit3D.Clear();
		EntityToProp3D.Clear();
		_activeMapScript = null;

		ServiceLocator.DisposeAndRecreateServices();
		ResolveServices();

		// Do not trigger lazy GroundTerrain creation here: InitializeGameEcs()
		// builds the ground exactly once (CreateGround at Realm.Client.Core.GameHost.cs:2751).
		// Triggering the getter here previously built the whole 128x128 mesh/water/navmesh
		// a second time on every _Ready, stalling the main thread at startup.
		if (_groundTerrain != null)
		{
			_editorService.SetTerrainSplatMap(_groundTerrain.SplatMap, _groundTerrain.CliffSplatMap);
		}

		InitializeGameEcs();
	}

	public void StopRecording()
	{
		_replayService?.StopRecording();
	}

	public override void _ExitTree()
	{
		LogGameStabilitySummary();

		if (Realm.Client.Network.LobbyManager.Instance != null)
			Realm.Client.Network.LobbyManager.Instance.ServerChatCommandReceived -= HandleServerChatCommand;

		StopRecording();
		ServiceLocator.Dispose();
		Instance = null;
	}

	private void LogGameStabilitySummary()
	{
		if ((Multiplayer.MultiplayerPeer == null || Multiplayer.IsServer()) && _trackerTickDurations != null && _trackerTickDurations.Count >= 30)
		{
			HostStabilityTracker.AddGameSummary(new GameStabilitySummary
			{
				AvgTickMs = HostStabilityTracker.CalculateAverage(_trackerTickDurations),
				MedianTickMs = HostStabilityTracker.CalculateMedian(_trackerTickDurations),
				MaxTickMs = HostStabilityTracker.CalculateMax(_trackerTickDurations),
				AvgApiMs = HostStabilityTracker.CalculateAverage(_trackerApiDurations),
				MedianApiMs = HostStabilityTracker.CalculateMedian(_trackerApiDurations),
				MaxApiMs = HostStabilityTracker.CalculateMax(_trackerApiDurations)
			});
		}
	}

	private void HandleServerChatCommand(int slot, string message)
	{
		if (!IsServerActive()) return;

		IUnit? heroUnit = null;
		foreach (var u in AllUnits)
		{
			if (GodotObject.IsInstanceValid(u) && u.Player == slot && EcsWorld != null && EcsWorld.IsAlive(u.Entity) && !EcsWorld.Has<Dead>(u.Entity))
			{
				heroUnit = GetUnitWrapper(u.Entity);
				break;
			}
		}

		OnPlayerChatMessage?.Invoke(message, heroUnit);
	}

	private void CreateGround()
	{
		ClearExistingGround();

		if (IsMapEditorMode || IsLoadingMap)
		{
			CreateEditableGround();
			return;
		}

		if (!ShouldCreateRuntimeGround()) return;

		string? rawMapName = Realm.Client.Network.LobbyManager.Instance?.ActiveMapName;
		if (string.IsNullOrEmpty(rawMapName)) return;

		string terrainPath = ResolveTerrainPath(rawMapName);
		
		CreateRuntimeGround(terrainPath);
	}

	private void ClearExistingGround()
	{
		var toRemove = new List<Node>();
		foreach (var child in GetChildren())
		{
			if (child.Name.ToString().StartsWith("Ground")) toRemove.Add(child);
		}
		
		foreach (var child in toRemove)
		{
			RemoveChild(child);
			child.QueueFree();
		}
		GroundTerrain = null;
	}

	private void CreateEditableGround()
	{
		var terrainNode = new Realm.Client.EditableTerrain();
		terrainNode.Name = "Ground";
		AddChild(terrainNode);
		GroundTerrain = terrainNode;
	}

	private bool ShouldCreateRuntimeGround()
	{
		return Realm.Client.Network.LobbyManager.Instance != null && Realm.Client.Network.LobbyManager.Instance.IsGameStarted;
	}

	private string ResolveTerrainPath(string rawMapName)
	{
		string? resolvedDir = ResolveMapDirectory(rawMapName);
		if (!string.IsNullOrEmpty(resolvedDir))
			return System.IO.Path.Combine(resolvedDir, "terrain.json");

		string normalizedRawMapName = rawMapName.Replace('\\', '/');
		string explicitPath = TryGetExplicitTerrainPath(normalizedRawMapName);
		if (!string.IsNullOrEmpty(explicitPath)) return explicitPath;

		return FallbackTerrainPath(rawMapName);
	}

	private string TryGetExplicitTerrainPath(string normalizedRawMapName)
	{
		if (!normalizedRawMapName.StartsWith("user://") && !normalizedRawMapName.StartsWith("res://") && !System.IO.Path.IsPathRooted(normalizedRawMapName))
			return string.Empty;

		string checkDir = normalizedRawMapName;
		if (normalizedRawMapName.StartsWith("user://") || normalizedRawMapName.StartsWith("res://"))
			checkDir = ProjectSettings.GlobalizePath(normalizedRawMapName);

		return System.IO.Directory.Exists(checkDir) ? System.IO.Path.Combine(checkDir, "terrain.json") : string.Empty;
	}

	private string FallbackTerrainPath(string rawMapName)
	{
		string normalizedMapName = rawMapName.ToLower().Trim();
		string mapDir = $"res://Maps/{normalizedMapName}";
		if (System.IO.Directory.Exists(ProjectSettings.GlobalizePath(mapDir)))
			return $"res://Maps/{normalizedMapName}/terrain.json";

		string userDir = ProjectSettings.GlobalizePath($"user://maps/{normalizedMapName}");
		if (System.IO.Directory.Exists(userDir))
			return $"user://maps/{normalizedMapName}/terrain.json";

		return $"res://Maps/{normalizedMapName}/terrain.json";
	}

	private void CreateRuntimeGround(string terrainPath)
	{
		var activeTerrainNode = new Realm.Client.RuntimeTerrain();
		activeTerrainNode.Name = "Ground";
		AddChild(activeTerrainNode);
		GroundTerrain = activeTerrainNode;

		string targetPath = terrainPath.StartsWith("user://") || terrainPath.StartsWith("res://") 
			? ProjectSettings.GlobalizePath(terrainPath) 
			: terrainPath;

		if (System.IO.File.Exists(targetPath))
			LoadMapFromFile(terrainPath, true, false);
	}

	public void SetSkyboxTexture(string path)
	{
		var worldEnv = GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
		if (worldEnv == null || worldEnv.Environment == null) return;

		string fullPath = GetSkyboxFullPath(path);
		Texture2D? skyTexture = LoadSkyboxTexture(fullPath);

		if (skyTexture != null)
		{
			worldEnv.Environment.Sky = new Sky { SkyMaterial = new PanoramaSkyMaterial { Panorama = skyTexture } };
			worldEnv.Environment.BackgroundMode = Godot.Environment.BGMode.Sky;
			EditorSkyboxPath = path;
		}
	}

	private string GetSkyboxFullPath(string path)
	{
		if (path.StartsWith("res://") || System.IO.File.Exists(path)) return path;

		string wsPath = Realm.Client.Services.MapWorkspaceService.GetActiveWorkspacePath();
		string fullPath = System.IO.Path.Combine(wsPath, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
		if (System.IO.File.Exists(fullPath)) return fullPath;

		fullPath = System.IO.Path.Combine(wsPath, "Assets", "skyboxes", System.IO.Path.GetFileName(path));
		if (System.IO.File.Exists(fullPath)) return fullPath;

		string rtexName = System.IO.Path.GetFileNameWithoutExtension(path) + ".rtex";
		string rtexCandidate = System.IO.Path.Combine(wsPath, "Assets", "skyboxes", rtexName);
		if (System.IO.File.Exists(rtexCandidate)) return rtexCandidate;

		return path;
	}

	private Texture2D? LoadSkyboxTexture(string fullPath)
	{
		if (fullPath.StartsWith("res://"))
		{
			return ResourceLoader.Exists(fullPath) ? GD.Load<Texture2D>(fullPath) : null;
		}

		if (!System.IO.File.Exists(fullPath)) return null;

		if (fullPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			byte[]? webpBytes = Realm.Shared.Textures.RtexFile.GetLayer(System.IO.File.ReadAllBytes(fullPath), 0);
			if (webpBytes != null && webpBytes.Length > 0)
			{
				var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
				if (img.LoadWebpFromBuffer(webpBytes) != Error.Ok) img.LoadPngFromBuffer(webpBytes);
				return ImageTexture.CreateFromImage(img);
			}
			return null;
		}

		var standardImg = Image.LoadFromFile(fullPath);
		return standardImg != null ? ImageTexture.CreateFromImage(standardImg) : null;
	}

	private void SetupSkybox()
	{
		SetSkyboxTexture(EditorSkyboxPath);
	}
	
	private Realm.Client.Prop3D FindProp3DInParentChain(Node node)
	{
		while (node != null)
		{
			if (node is Realm.Client.Prop3D prop)
			{
				return prop;
			}
			node = node.GetParent();
		}
		return null;
	}

	private void RemoveIfHas<T>(Entity entity) where T : struct
	{
		if (EcsWorld.Has<T>(entity)) EcsWorld.Remove<T>(entity);
	}

	public void ClearUnitOrders(Entity entity)
	{
		RemoveIfHas<MoveTo>(entity);
		RemoveIfHas<PathFollow>(entity);
		RemoveIfHas<AttackTarget>(entity);
		RemoveIfHas<Realm.Ecs.Components.Movement.AttackMove>(entity);
		RemoveIfHas<Realm.Ecs.Components.Movement.HoldPosition>(entity);
		RemoveIfHas<Realm.Ecs.Components.Movement.Follow>(entity);
		RemoveIfHas<Patrol>(entity);
		RemoveIfHas<HealingTarget>(entity);
		RemoveIfHas<WaypointQueue>(entity);
		RemoveIfHas<Gatherer>(entity);
		RemoveIfHas<Realm.Ecs.Components.Resources.BuildTask>(entity);
		RemoveIfHas<Realm.Ecs.Components.Resources.BuildQueue>(entity);
	}

	private void StopGatheringMovement(Entity entity)
	{
		if (EcsWorld.IsAlive(entity))
		{
			if (EcsWorld.Has<MoveTo>(entity)) EcsWorld.Remove<MoveTo>(entity);
		}
	}

	private Realm.Client.Prop3D FindNearbyResourceNode(Vector3 pos, string type, float radius)
	{
		Realm.Client.Prop3D closest = null;
		float closestDist = radius;
		foreach (var prop in AllProps)
		{
			if (GodotObject.IsInstanceValid(prop))
			{
				string pType = prop.PropId switch
				{
					"goldmine" => "gold",
					"tree" => "wood",
					"rock" => "stone",
					_ => null
				};
				
				if (pType == type)
				{
					float d = pos.DistanceTo(prop.GlobalPosition);
					if (d < closestDist)
					{
						closestDist = d;
						closest = prop;
					}
				}
			}
		}
		return closest;
	}

	public void IssueGatherCommand(Realm.Client.Prop3D prop, bool isQueued = false)
	{
		if (SelectedUnits.Count == 0 || prop == null || !GodotObject.IsInstanceValid(prop)) return;

		string? resType = GetPropResourceType(prop.PropId);
		if (resType == null) return;

		SpawnTargetIndicator(prop.GlobalPosition, new Color(0.9f, 0.8f, 0.1f));
		Realm.Client.UI.InGameHUD.Instance?.ShowFeedbackText($"Gathering {resType.ToUpper()} from {prop.PropId}", new Color(0.9f, 0.8f, 0.1f));

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			IssueMultiplayerGatherCommand(prop, isQueued);
			return;
		}

		IssueLocalGatherCommand(prop, resType, isQueued);
	}

	private string? GetPropResourceType(string propId)
	{
		return propId switch
		{
			"goldmine" => "gold",
			"tree" => "wood",
			"rock" => "stone",
			_ => null
		};
	}

	private void IssueMultiplayerGatherCommand(Realm.Client.Prop3D prop, bool isQueued)
	{
		var targetIds = new List<int>();
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy || unit.UnitId != "worker") continue;
			targetIds.Add(GetServerEntityId(unit.Entity));
		}
		QueueClientCommand(isQueued ? "gather_queued" : "gather", targetIds, prop.GlobalPosition, GetServerEntityId(prop.Entity), "");
	}

	private void IssueLocalGatherCommand(Realm.Client.Prop3D prop, string resType, bool isQueued)
	{
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy || unit.UnitId != "worker") continue;

			if (isQueued && _inputService != null && _inputService.IsUnitActive(unit.Entity))
			{
				_inputService.EnqueueCommand(unit.Entity, "gather", new System.Numerics.Vector3(prop.GlobalPosition.X, prop.GlobalPosition.Y, prop.GlobalPosition.Z), prop.Entity);
			}
			else
			{
				if (!isQueued) ClearUnitOrders(unit.Entity);

				EcsWorld.SetOrAdd(unit.Entity, new Gatherer(resType, prop.Entity));
				EcsWorld.SetOrAdd(unit.Entity, new MoveTo(new System.Numerics.Vector3(prop.GlobalPosition.X, prop.GlobalPosition.Y, prop.GlobalPosition.Z)));
			}
		}
	}

	private Entity CreateEcsUnit(string id, string name, float hp, float damage, float range, float armor, float speed, Vector3 pos, Realm.Ecs.Common.PlayerEntity owner)
	{
		UnitMetadata regMeta = null;
		if (!UnitRegistry.TryGetValue(id, out regMeta)) TryGetUnitOrBuildingMetadata(id, out regMeta);

		var entity = regMeta != null 
			? CreateEcsUnitEntityWithMeta(id, name, hp, damage, range, armor, speed, pos, owner, regMeta)
			: _unitSpawnService.CreateEcsUnitEntity(id, name, hp, damage, range, armor, speed, 15.0f, false, 1.5f, 8, pos, owner, _playerEntity, HasShieldsUpgrade, HasWeaponsUpgrade, null, 0f, 0f, 0f, 0f, 0f, "unarmored", 0f, 0f, 0f, "normal", 0f, 1f, "None", 0f, 0f, 0f, 1f, 0.5f, 0.25f, false, 0, "Ground");

		if (regMeta != null) ApplyUnitAttributes(entity, regMeta);

		OnUnitCreated?.Invoke(GetUnitWrapper(entity));
		return entity;
	}

	private Entity CreateEcsUnitEntityWithMeta(string id, string name, float hp, float damage, float range, float armor, float speed, Vector3 pos, Realm.Ecs.Common.PlayerEntity owner, UnitMetadata meta)
	{
		return _unitSpawnService.CreateEcsUnitEntity(
			id, name, hp, damage, range, armor, speed,
			meta.ScanRadius > 0 ? meta.ScanRadius : 15.0f, meta.IsHero,
			meta.AttackCooldown > 0 ? meta.AttackCooldown : 1.5f, GetUnitPathingFlags(meta), pos, owner,
			_playerEntity, HasShieldsUpgrade, HasWeaponsUpgrade, meta.Targets,
			meta.HpRegen, meta.HpRegenCombatDelay, meta.MaxMana, meta.ManaRegen,
			meta.RatedArmor, !string.IsNullOrEmpty(meta.ArmorType) ? meta.ArmorType : "unarmored",
			meta.FlatArmorPenetration, meta.PercentArmorPenetration, meta.DamageVariance,
			!string.IsNullOrEmpty(meta.DamageType) ? meta.DamageType : (!string.IsNullOrEmpty(meta.AttackType) ? meta.AttackType : "normal"),
			meta.CritChance, meta.CritMultiplier > 0f ? meta.CritMultiplier : 1f,
			meta.SplashType, meta.SplashInnerRadius, meta.SplashMediumRadius, meta.SplashOuterRadius,
			meta.SplashInnerRatio, meta.SplashMediumRatio, meta.SplashOuterRatio, meta.FriendlyFire,
			meta.PushPriority, !string.IsNullOrEmpty(meta.MovementType) ? meta.MovementType : "Ground"
		);
	}

	private void ApplyUnitAttributes(Entity entity, UnitMetadata meta)
	{
		if (meta.Strength != 0f || meta.Agility != 0f || meta.Vitality != 0f || meta.Intelligence != 0f || meta.Wisdom != 0f || meta.Fortune != 0f)
		{
			EcsWorld.Set(entity, new Realm.Ecs.Components.Stats.UnitAttributes(meta.Strength, meta.Agility, meta.Vitality, meta.Intelligence, meta.Wisdom, meta.Fortune));
			AttributeStatCalculator.RecalculateEntityStats(EcsWorld, entity);
		}
	}

	private void ApplyGigachadBuffsIfNeeded(Entity entity, bool isEnemy)
	{
		if (!GigachadEnabled || isEnemy) return;
		if (EcsWorld.Has<Health>(entity)) EcsWorld.Set(entity, new Health(9000f, 9000f));
		if (EcsWorld.Has<Attack>(entity))
		{
			var atk = EcsWorld.Get<Attack>(entity);
			EcsWorld.Set(entity, new Attack(9001f, atk.Range, atk.Cooldown, atk.CurrentCooldown));
		}
	}

	private void HandleBuildingSpawn(Entity entity, Realm.Client.Unit3D unit3D, string id, Vector3 pos)
	{
		EcsWorld.SetOrAdd(entity, new BuildingSpawnOffset(new System.Numerics.Vector3(0f, 0f, 8f)));
		float baseRadius = GetOrCalculateObstacleRadius(id, unit3D, true) * GetModelCollisionCircleRatio(GetModelAssetKey(unit3D));
		EcsWorld.SetOrAdd(entity, new Realm.Ecs.Components.Core.CollisionRadius(baseRadius));
		if (!IsMapEditorMode) CarveObstacle(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z), baseRadius);
	}

	private Realm.Client.Unit3D SpawnUnit3D(Entity entity, string id, string modelPath, Vector3 pos, bool isBuilding, bool isEnemy, bool isFromQueue = false, int player = -1, bool executeSpawnShader = false)
	{
		int playerIndex = player >= 0 ? player : 0;
		bool actualIsEnemy = player >= 0 ? NetworkService.ArePlayerIndicesEnemies(LocalPlayerIndex, playerIndex) : isEnemy;
		
		SetupUnitEntityComponents(entity, id, playerIndex, actualIsEnemy);

		var unit3D = CreateAndSetupUnit3DNode(entity, id, modelPath, pos, isBuilding, playerIndex, actualIsEnemy);

		if (isBuilding) HandleBuildingSpawn(entity, unit3D, id, pos);

		ApplyMapEditorTransforms(unit3D);

		unit3D.Visible = true;
		unit3D.UpdateLodVisibility();

		EntityToUnit3D[entity] = unit3D;
		AllUnits.Add(unit3D);

		UpdatePopulationStats(id, isEnemy, isFromQueue);
		if (id == "castle") _castlesList.Add(unit3D);

		if (executeSpawnShader) ExecuteSpawnShader(unit3D, id);

		return unit3D;
	}

	private void SetupUnitEntityComponents(Entity entity, string id, int playerIndex, bool actualIsEnemy)
	{
		EcsWorld.SetOrAdd(entity, new UnitFaction(actualIsEnemy));
		EcsWorld.SetOrAdd(entity, new UnitOwnerPlayer(playerIndex));
		EcsWorld.SetOrAdd(entity, new DefinitionId(id));
		ApplyGigachadBuffsIfNeeded(entity, actualIsEnemy);
	}

	private Realm.Client.Unit3D CreateAndSetupUnit3DNode(Entity entity, string id, string modelPath, Vector3 pos, bool isBuilding, int playerIndex, bool actualIsEnemy)
	{
		var unit3D = new Realm.Client.Unit3D { Entity = entity, UnitId = id, IsBuilding = isBuilding, Name = $"{id}_{entity.Id}", Player = playerIndex, IsEnemy = actualIsEnemy };

		if (!isBuilding && !IsMapEditorMode)
		{
			unit3D.CollisionLayer = 1;
			unit3D.CollisionMask = 0;
		}

		AddChild(unit3D);
		unit3D.Position = pos;
		unit3D.LoadModel(modelPath);
		unit3D.UpdatePlayerColorVisual();
		
		return unit3D;
	}

	private void ApplyMapEditorTransforms(Realm.Client.Unit3D unit3D)
	{
		if (!IsMapEditorMode) return;
		
		unit3D.RotationDegrees = new Vector3(0.0f, EditorPlacementRotation, 0.0f);
		unit3D.Scale *= EditorPlacementScale;
	}

	private void UpdatePopulationStats(string id, bool isEnemy, bool isFromQueue)
	{
		if (isEnemy || !UnitRegistry.TryGetValue(id, out var popMeta)) return;

		if (id == "castle") MaxPopulation += 20;
		if (!isFromQueue) CurrentPopulation += popMeta.PopCost;
	}

	private void ExecuteSpawnShader(Realm.Client.Unit3D unit3D, string id)
	{
		string spawnShader = GetModelSpawnShader(unit3D);
		if (string.IsNullOrEmpty(spawnShader)) spawnShader = GetModelSpawnShader(id);
		
		if (!string.IsNullOrEmpty(spawnShader)) SpawnDeathShaderManager.AnimateTransition(unit3D, spawnShader, true);
	}


	public override void _Process(double delta)
	{
		if (_shroudService != null)
		{
			float fDelta = (float)delta;
			bool isReplay = Realm.Client.ReplaySystem.ReplayPlaybackManager.Instance.IsPlayingReplay;
			bool isSpectator = Realm.Client.Network.LobbyManager.Instance != null && Realm.Client.Network.LobbyManager.Instance.LocalPlayer != null && Realm.Client.Network.LobbyManager.Instance.LocalPlayer.Team == "Spectator";
			int spectatorPerspective = Realm.Client.UI.InGameHUD.Instance?.LiveSpectatorPerspective ?? -1;
			_shroudService.Tick(fDelta, AllUnits, AllProps, AllDecals, spectatorPerspective, isReplay, isSpectator);
		}

		var worldEnv = MainNode?.GetNodeOrNull<WorldEnvironment>("WorldEnvironment") ?? GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
		_environmentService?.UpdateEnvironmentalFog(MainCamera, worldEnv);
		_environmentService?.UpdateWeatherParticlePosition(MainCamera);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!ShouldProcessPhysicsTick()) return;

		float fDelta = (float)delta;
		if (_wasClientInMultiplayer) UpdateConnectionStatus();

		if (ReplayPlaybackManager.Instance.IsPlayingReplay)
		{
			ProcessReplayTick(fDelta);
			return;
		}

		if (Multiplayer.MultiplayerPeer == null || Multiplayer.IsServer()) UpdatePauseCountdown(fDelta);
		if (IsPaused) return;

		DispatchPhysicsTick(fDelta);
	}

	private bool ShouldProcessPhysicsTick()
	{
		return Realm.Client.Network.LobbyManager.Instance?.IsGameStarted == true || IsMapEditorMode;
	}

	private void ProcessReplayTick(float fDelta)
	{
		ReplayPlaybackManager.Instance.Update(fDelta);
		UpdateVisualNodesFromEcs(fDelta);
	}

	private void DispatchPhysicsTick(float fDelta)
	{
		if (_multiplayerActive && !IsServerActive()) UpdateClientTick(fDelta);
		else if (IsMapEditorMode) ProcessMapEditorTick(fDelta);
		else ProcessGameplayTick(fDelta);
	}

	private void UpdatePauseCountdown(float fDelta)
	{
		if (ResumeCountdownSeconds < 0) return;

		_resumeCountdownTimer += fDelta;
		if (_resumeCountdownTimer < 1.0f) return;

		_resumeCountdownTimer -= 1.0f;
		ResumeCountdownSeconds--;

		bool isServer = Multiplayer.MultiplayerPeer != null && Multiplayer.IsServer();

		if (ResumeCountdownSeconds <= 0)
		{
			ResumeCountdownSeconds = -1;
			IsPaused = false;
			if (isServer) Rpc(nameof(BroadcastPauseState), false, -1, true);
			else UpdatePauseUI();
		}
		else
		{
			if (isServer) Rpc(nameof(BroadcastCountdownState), ResumeCountdownSeconds, _countdownForcedByHost);
			else UpdatePauseUI();
		}
	}

	private bool IsServerActive()
	{
		if (Multiplayer.MultiplayerPeer == null) return true;
		try
		{
			if (Multiplayer.MultiplayerPeer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected) return true;
			return Multiplayer.IsServer();
		}
		catch
		{
			return true;
		}
	}

	public void RebakeNavMesh()
	{
		if (IsServerActive() && GroundTerrain != null)
		{
			GroundTerrain.BakeNavMesh();
		}
	}

	public void CarveObstacle(System.Numerics.Vector3 pos, float radius)
	{
		if (EcsWorld != null && EcsWorld.IsAlive(WorldEntity) && EcsWorld.Has<TerrainState>(WorldEntity))
		{
			ref var state = ref EcsWorld.Get<TerrainState>(WorldEntity);
			_terrainNavMeshService?.CarveObstacle(ref state, pos, radius);
		}
	}

	public void UncarveObstacle(System.Numerics.Vector3 pos, float radius)
	{
		if (EcsWorld != null && EcsWorld.IsAlive(WorldEntity) && EcsWorld.Has<TerrainState>(WorldEntity))
		{
			ref var state = ref EcsWorld.Get<TerrainState>(WorldEntity);
			_terrainNavMeshService?.UncarveObstacle(ref state, pos, radius);
		}
	}
	private static System.Collections.Generic.Dictionary<string, float> ObstacleRadiusCache => Realm.Client.Services.ServiceLocator.Get<Realm.Ecs.Services.TerrainNavMeshService>().ObstacleRadiusCache;

	public float GetOrCalculateObstacleRadius(string id, Node3D node, bool isBuilding = false)
	{
		if (ObstacleRadiusCache.TryGetValue(id, out float cachedRadius))
		{
			return cachedRadius;
		}

		if (UnitRegistry.TryGetValue(id, out var meta) && meta.ObstacleRadius.HasValue)
		{
			float radius = meta.ObstacleRadius.Value;
			ObstacleRadiusCache[id] = radius;
			return radius;
		}

		string modelKey = node != null ? GetModelAssetKey(node) : GetModelAssetKey(id);
		if (!string.IsNullOrEmpty(modelKey) && ModelObstacleRadii.TryGetValue(modelKey, out float measuredRadius) && measuredRadius > 0f)
		{
			ObstacleRadiusCache[id] = measuredRadius;
			return measuredRadius;
		}

		float calculatedRadius = 0.5f;
		if (node != null)
		{
			calculatedRadius = CalculateNodeRadius(node);
		}

		ObstacleRadiusCache[id] = calculatedRadius;
		return calculatedRadius;
	}

	private float CalculateNodeRadius(Node node)
	{
		float maxRadius = 0.5f;

		foreach (var shapeNode in FindChildrenOfType<CollisionShape3D>(node))
		{
			if (shapeNode.Shape == null) continue;
			float r = GetShapeRadius(shapeNode.Shape) * Math.Max(shapeNode.Scale.X, shapeNode.Scale.Z);
			if (r > maxRadius) maxRadius = r;
		}

		foreach (var meshNode in FindChildrenOfType<MeshInstance3D>(node))
		{
			if (meshNode.Mesh == null) continue;
			float r = Math.Max(meshNode.Mesh.GetAabb().Size.X, meshNode.Mesh.GetAabb().Size.Z) * 0.5f * Math.Max(meshNode.Scale.X, meshNode.Scale.Z);
			if (r > maxRadius) maxRadius = r;
		}

		return maxRadius;
	}

	private float GetShapeRadius(Shape3D shape)
	{
		return shape switch
		{
			BoxShape3D box => Math.Max(box.Size.X, box.Size.Z) * 0.5f,
			CylinderShape3D cyl => cyl.Radius,
			SphereShape3D sphere => sphere.Radius,
			CapsuleShape3D capsule => capsule.Radius,
			_ => 0.5f
		};
	}

	/// <summary>
	///     Measures the horizontal footprint radius (max corner distance from the origin in the
	///     XZ plane) of a freshly instantiated model. Used at import time to persist an obstacle
	///     radius for custom map assets that have no code-side collision shapes.
	/// </summary>
	public float MeasureModelRadius(Node3D root)
	{
		if (root == null) return 0f;
		float maxRadius = 0f;
		MeasureModelRadiusRecursive(root, Transform3D.Identity, ref maxRadius);
		return maxRadius;
	}

	private void MeasureModelRadiusRecursive(Node3D node, Transform3D parentXform, ref float maxRadius)
	{
		var localXform = parentXform * node.Transform;
		if (node is MeshInstance3D meshNode && meshNode.Mesh != null)
		{
			var aabb = meshNode.Mesh.GetAabb();
			if (aabb.Size != Vector3.Zero)
			{
				for (int i = 0; i < 8; i++)
				{
					var corner = localXform * aabb.GetEndpoint(i);
					float r = Mathf.Sqrt(corner.X * corner.X + corner.Z * corner.Z);
					if (r > maxRadius) maxRadius = r;
				}
			}
		}
		foreach (var child in node.GetChildren())
		{
			if (child is Node3D child3D)
			{
				MeasureModelRadiusRecursive(child3D, localXform, ref maxRadius);
			}
		}
	}

	/// <summary>
	///     Returns true when the entity's pathing flags include the given capability
	///     (e.g. <see cref="TerrainPathingFlags.Flying"/>).
	/// </summary>
	internal bool IsPathingCapability(Entity entity, TerrainPathingFlags capability)
	{
		if (EcsWorld == null || !EcsWorld.IsAlive(entity) || !EcsWorld.Has<PathingFlags>(entity)) return false;
		int flags = EcsWorld.Get<PathingFlags>(entity).Value;
		return ((TerrainPathingFlags)flags & capability) != 0;
	}

	private static ImageTexture SharedShadowGradient { get; set; }

	/// <summary>
	///     Shared radial gradient used by flying-unit drop-shadow decals. One texture is
	///     generated once and reused by every unit to avoid per-unit allocations.
	/// </summary>
	public Texture2D GetSharedShadowGradient()
	{
		if (SharedShadowGradient != null && GodotObject.IsInstanceValid(SharedShadowGradient))
		{
			return SharedShadowGradient;
		}

		const int size = 256;
		var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				float dx = (x + 0.5f) / size - 0.5f;
				float dy = (y + 0.5f) / size - 0.5f;
				float dist = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
				float alpha = Mathf.Clamp(1f - dist, 0f, 1f);
				alpha *= alpha;
				img.SetPixel(x, y, new Color(0f, 0f, 0f, alpha));
			}
		}
		if (!img.HasMipmaps())
		{
			img.GenerateMipmaps();
		}
		SharedShadowGradient = ImageTexture.CreateFromImage(img);
		return SharedShadowGradient;
	}

	private List<T> FindChildrenOfType<T>(Node parent) where T : Node
	{
		var result = new List<T>();
		FindChildrenOfTypeRecursive(parent, result);
		return result;
	}

	private void FindChildrenOfTypeRecursive<T>(Node parent, List<T> result) where T : Node
	{
		if (parent is T typed)
		{
			result.Add(typed);
		}
		foreach (var child in parent.GetChildren())
		{
			FindChildrenOfTypeRecursive(child, result);
		}
	}

	private void UpdateConnectionStatus()
	{
		_networkService.UpdateConnectionStatus(_multiplayerActive, IsServerActive());
		if (_networkService.IsConnectionLost && !IsServerActive() && Realm.Client.Network.LobbyManager.Instance != null && !Realm.Client.Network.LobbyManager.Instance.IsReconnecting)
		{
			Realm.Client.Network.LobbyManager.Instance.TriggerReconnect();
		}
	}

	private void ProcessGameplayTick(float fDelta)
	{
		if (IsGameOver) return;

		float actualIntervalMs = BeginTickTracking();

		_fDelta = fDelta;

		ProcessCoreSimulation(fDelta);
		ProcessVisualAndEnvironment(fDelta);
		ProcessMiscTicks(fDelta);

		if (_multiplayerActive && Multiplayer.IsServer())
			UpdateServerSnapshotTick(fDelta);

		EndTickTracking(actualIntervalMs);
	}

	private float BeginTickTracking()
	{
		float actualIntervalMs = 0f;
		if (Multiplayer.MultiplayerPeer == null || Multiplayer.IsServer())
		{
			if (_trackerIntervalStopwatch.IsRunning)
			{
				actualIntervalMs = (float)_trackerIntervalStopwatch.Elapsed.TotalMilliseconds;
				_trackerIntervalStopwatch.Restart();
			}
			else
			{
				_trackerIntervalStopwatch.Start();
			}
			_trackerTickStopwatch.Restart();
		}
		return actualIntervalMs;
	}

	private void ProcessCoreSimulation(float fDelta)
	{
		_simulationService.TickEcs(fDelta);
		PollVFXQueue();
		TickConstructionSystem(fDelta);
		UpdateVisualNodesFromEcs(fDelta);
	}

	private void ProcessVisualAndEnvironment(float fDelta)
	{
		if (EcsWorld != null && EcsWorld.IsAlive(_worldEntity) && EcsWorld.Has<WorldState>(_worldEntity))
		{
			var state = EcsWorld.Get<WorldState>(_worldEntity);
			if (state.DayNightCycleEnabled && !IsMapEditorMode)
			{
				float progress = state.TimeOfDayTimer / TimeOfDayCycleDuration;
				UpdateDayNightVisuals(progress);
			}
		}

		if (_environmentService != null && _environmentService.IsTransitioning)
			_environmentService.UpdateTransition(this, fDelta);
			
		UpdateMinimapPings(fDelta);
	}

	private void ProcessMiscTicks(float fDelta)
	{
		TickScheduledTimers(fDelta);
		TickZoneTriggers();

		if (_activeMapScript != null) _activeMapScript.Update(this, fDelta);

		UpdateBuildingPreview();

		if (!ReplayPlaybackManager.Instance.IsPlayingReplay && GameSettings.RecordReplays && _replayService != null && _replayService.IsRecording)
			RecordGameplayTick();
	}

	private void EndTickTracking(float actualIntervalMs)
	{
		if (Multiplayer.MultiplayerPeer != null && !Multiplayer.IsServer()) return;

		_trackerTickStopwatch.Stop();
		float tickCpuMs = (float)_trackerTickStopwatch.Elapsed.TotalMilliseconds;
		float tickDelay = actualIntervalMs > 33.33f ? actualIntervalMs - 33.33f : 0f;
		
		float adjustedTickMs = tickCpuMs + tickDelay;
		if (_trackerTickDurations != null) _trackerTickDurations.Add(adjustedTickMs);
		
		_trackerLastTickDelay = tickDelay;
	}

	private void PollVFXQueue()
	{
		if (EcsWorld == null || !EcsWorld.IsAlive(_worldEntity) || !EcsWorld.Has<Realm.Ecs.Components.Core.VFXQueue>(_worldEntity)) return;

		ref var queue = ref EcsWorld.Get<Realm.Ecs.Components.Core.VFXQueue>(_worldEntity);
		if (queue.Requests.Count == 0) return;

		for (int i = 0; i < queue.Requests.Count; i++)
		{
			ProcessVFXRequest(queue.Requests[i]);
		}

		queue.Requests.Clear();
	}

	private void ProcessVFXRequest(Realm.Ecs.Components.Core.VFXRequest req)
	{
		var godotPos = new Vector3(req.Position.X, req.Position.Y, req.Position.Z);
		var godotTarget = new Vector3(req.TargetPosition.X, req.TargetPosition.Y, req.TargetPosition.Z);

		if (TryProcessAbilityVfx(req, godotPos)) return;
		if (TryProcessProjectileVfx(req, godotPos, godotTarget)) return;
		
		ProcessMiscVfx(req, godotPos, godotTarget);
	}

	private bool TryProcessAbilityVfx(Realm.Ecs.Components.Core.VFXRequest req, Vector3 godotPos)
	{
		var def = GetAbilityDefinition(req.EffectTypeId);
		if (def == null) return false;
		
		_fxService.SpawnAbilityEffect(this, def, godotPos);
		return true;
	}

	private bool TryProcessProjectileVfx(Realm.Ecs.Components.Core.VFXRequest req, Vector3 godotPos, Vector3 godotTarget)
	{
		if (req.EffectTypeId != "arrow" && !WeaponRegistry.ContainsKey(req.EffectTypeId) && !req.EffectTypeId.StartsWith("proj:")) return false;

		string weaponId = req.EffectTypeId.StartsWith("proj:") ? req.EffectTypeId.Substring(5) : req.EffectTypeId;
		SpawnWeaponProjectile(godotPos, godotTarget, weaponId, GetEntityById(req.EntityId));
		return true;
	}

	private void ProcessMiscVfx(Realm.Ecs.Components.Core.VFXRequest req, Vector3 godotPos, Vector3 godotTarget)
	{
		if (req.EffectTypeId == "heal") SpawnHealVisualEffect(godotPos, godotTarget);
		else if (req.EffectTypeId == "target_indicator") SpawnTargetIndicator(godotPos, new Color(req.Scale, 0.3f, 0.1f));
		else if (req.EffectTypeId == "damage_flash" || req.EffectTypeId == "heal_flash") TryProcessUnitFlash(req);
	}

	private void TryProcessUnitFlash(Realm.Ecs.Components.Core.VFXRequest req)
	{
		if (req.EntityId == -1) return;
		var unit = EntityToUnit3D.FirstOrDefault(kv => kv.Key.Id == req.EntityId).Value;
		if (unit == null || !GodotObject.IsInstanceValid(unit)) return;

		if (req.EffectTypeId == "damage_flash") _fxService.FlashDamageUnit(unit);
		else _fxService.FlashHealUnit(unit);
	}

	private Entity GetEntityById(int entityId)
	{
		if (entityId == -1) return Entity.Null;
		var unit = AllUnits.FirstOrDefault(u => u.Entity.Id == entityId);
		return unit != null ? unit.Entity : Entity.Null;
	}


	private void UpdateMinimapPings(float fDelta)
	{
		for (int i = ActivePings.Count - 1; i >= 0; i--)
		{
			var ping = ActivePings[i];
			ping.LifeTime += fDelta;
			if (ping.LifeTime >= ping.MaxLifeTime)
			{
				ActivePings.RemoveAt(i);
			}
			else
			{
				ActivePings[i] = ping;
			}
		}
	}

	private void UpdateDayNightVisuals(float progress)
	{
		_environmentService?.UpdateDayNightVisuals(this, progress);
	}

	public (int TimeOfDayIndex, float TimeOfDayTimer) SetTimeOfDay(int timeOfDayIndex)
	{
		return _environmentService?.SetTimeOfDay(this, _worldEntity, timeOfDayIndex, TimeOfDayCycleDuration) ?? (0, 0f);
	}

	public (int TimeOfDayIndex, float TimeOfDayTimer) CycleTimeOfDay()
	{
		return _environmentService?.CycleTimeOfDay(this, _worldEntity, TimeOfDayCycleDuration) ?? (0, 0f);
	}

	public string GetTimeOfDayName()
	{
		return _environmentService?.GetTimeOfDayName(TimeOfDayIndex) ?? "Unknown";
	}

	private void SpawnSpritesheetEffect(string texturePath, Vector3 worldPosition, int columns, int rows, float secondsPerFrame, float sizeInWorldUnits)
	{
		_fxService.SpawnSpritesheetEffect(this, texturePath, worldPosition, columns, rows, secondsPerFrame, sizeInWorldUnits);
	}

	private void FlashDamageUnit(Realm.Client.Unit3D unit)
	{
		_fxService.FlashDamageUnit(unit);
	}

	private void SpawnHealVisualEffect(Vector3 start, Vector3 target)
	{
		_fxService.SpawnHealVisualEffect(this, start, target);
	}

	private void FlashHealUnit(Realm.Client.Unit3D unit)
	{
		_fxService.FlashHealUnit(unit);
	}

	private void SpawnPing3DEffect(Vector3 position)
	{
		_fxService.SpawnPing3DEffect(this, position);
	}

	public void SpawnArrowProjectileForReplay(System.Numerics.Vector3 start, System.Numerics.Vector3 target)
	{
		_fxService.SpawnArrowProjectile(this, new Vector3(start.X, start.Y, start.Z), new Vector3(target.X, target.Y, target.Z));
	}
	public void SpawnWeaponProjectileForReplay(System.Numerics.Vector3 start, System.Numerics.Vector3 target, string? weaponId = null)
	{
		_fxService.SpawnWeaponProjectile(this, new Vector3(start.X, start.Y, start.Z), new Vector3(target.X, target.Y, target.Z), weaponId);
	}
	private void SpawnArrowProjectile(Vector3 start, Vector3 target)
	{
		SpawnWeaponProjectile(start, target, "arrow");
	}
	public void SpawnWeaponProjectile(Vector3 start, Vector3 target, string? weaponId = null, Entity targetEntity = default)
	{
		_fxService.SpawnWeaponProjectile(this, start, target, weaponId, targetEntity);
		if (_replayService != null && _replayService.IsRecording)
		{
			_replayService.RecordProjectile(weaponId ?? "arrow", new System.Numerics.Vector3(start.X, start.Y, start.Z), new System.Numerics.Vector3(target.X, target.Y, target.Z));
		}
		if (_multiplayerActive && IsServerActive())
		{
			Rpc(nameof(ClientSpawnArrowProjectile), start, target);
		}
	}

	private void SpawnTargetIndicator(Vector3 position, Color color)
	{
		_fxService.SpawnTargetIndicator(this, position, color);
	}

	public void AddMinimapPing(Vector3 position)
	{
		_fxService.AddMinimapPing(this, ActivePings, position);
	}

	private class EmptyMapScript : Realm.MapAPI.IMapScript
	{
		public void Initialize(Realm.MapAPI.IGameAPI api) {}
		public void Update(Realm.MapAPI.IGameAPI api, float delta) {}
	}
}
