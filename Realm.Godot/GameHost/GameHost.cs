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
using Realm.Godot.ReplaySystem;
using Realm.MapAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Realm.Godot.Animation;
using Realm.Godot.Utils;
using Realm.Godot.VFX;

public partial class GameHost : Node3D, IGameAPI
{
	public Camera3D MainCamera { get; private set; }
	public Node MainNode { get; private set; }

	public static GameHost Instance { get; private set; }
	private readonly NavMeshPathfinder _pathfinder = new();
	public string ActiveMapName { get; private set; } = "";

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
	private Realm.Godot.Services.ModelOptimization.ModelOptimizerService _modelOptimizerService;
	private TerrainNavMeshService _terrainNavMeshService;
	private Realm.Godot.Services.MetadataService _metadataService;
	private Realm.Godot.Services.MapUpgradeService _mapUpgradeService;
	private Realm.Godot.Services.MapStorageService _mapStorageService;

	public CheatService CheatService => _cheatService;
	public EnvironmentService EnvironmentService => _environmentService;
	public SpectatorService SpectatorService => _spectatorService;
	public ShroudService ShroudService => _shroudService;
	public Realm.Godot.Services.ModelOptimization.ModelOptimizerService ModelOptimizerService => _modelOptimizerService;
	public Realm.Godot.Services.MetadataService MetadataService => _metadataService;
	public Realm.Godot.Services.MapUpgradeService MapUpgradeService => _mapUpgradeService;
	public Realm.Godot.Services.MapStorageService MapStorageService => _mapStorageService;

	public bool UnlimitedPowerEnabled { get; set; } = false;
	public bool GigachadEnabled { get; set; } = false;

	private float _fDelta;

	
	internal DefinitionManager DefinitionManager => _definitionManager;
	private DefinitionManager _definitionManager = null!;
	
	private ResourceId _goldResourceId;
	private ResourceId _woodResourceId;
	private ResourceId _stoneResourceId;
	
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




	public World EcsWorld { get; private set; }
	public Entity WorldEntity => _worldEntity;
	public List<Unit3D> SelectedUnits { get; } = new List<Unit3D>();
	public List<Unit3D> AllUnits { get; } = new List<Unit3D>();
	public List<Prop3D> AllProps { get; } = new List<Prop3D>();
	public List<Decal> AllDecals { get; } = new List<Decal>();
	public List<ProceduralVfxInstance3D> AllVfx { get; } = new List<ProceduralVfxInstance3D>();
	private readonly List<Unit3D> _castlesList = new();

	public static readonly Dictionary<Entity, Unit3D> EntityToUnit3D = new();
	public static readonly Dictionary<Entity, Prop3D> EntityToProp3D = new();
	public static readonly Dictionary<Entity, ProceduralVfxInstance3D> EntityToVfx3D = new();
	public static readonly Dictionary<string, VfxAttachmentConfig> VfxRegistry = new(StringComparer.OrdinalIgnoreCase);

	public static bool TryGetUnit3D(Entity entity, out Unit3D unit)
	{
		return EntityToUnit3D.TryGetValue(entity, out unit);
	}

	public static bool TryGetProp3D(Entity entity, out Prop3D prop)
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
			if (_multiplayerActive && LobbyManager.Instance != null && LobbyManager.Instance.PlayerList.Count > 0)
			{
				var p = LobbyManager.Instance.PlayerList.Find(x => x.PeerId == _localPeerId);
				if (p != null) return p.Slot;
			}
			return 0;
		}
	}

	public Entity GetPlayerEntityForPlayerIndex(int playerIndex)
	{
		if (_multiplayerActive && LobbyManager.Instance != null && LobbyManager.Instance.PlayerList.Count > 0)
		{
			var p = LobbyManager.Instance.PlayerList.Find(x => x.Slot == playerIndex);
			if (p != null && _peerIdToPlayerEntityMap != null && _peerIdToPlayerEntityMap.TryGetValue(p.PeerId, out var pe) && EcsWorld.IsAlive(pe))
			{
				return pe;
			}
		}

		if (playerIndex == 0)
		{
			return _playerEntity;
		}

		if (_enemyPlayerEntity != Entity.Null && EcsWorld.IsAlive(_enemyPlayerEntity))
		{
			return _enemyPlayerEntity;
		}

		return _playerEntity;
	}

	public bool IsPlayerEnemy(int playerIndex)
	{
		return NetworkService.ArePlayerIndicesEnemies(LocalPlayerIndex, playerIndex);
	}

	private Entity _worldEntity;

	private int _replayTickCounter
	{
		get => EcsWorld?.GetFieldOrDefault<ReplayState, int>(_worldEntity, s => s.ReplayTickCounter) ?? 0;
		set => EcsWorld?.Mutate<ReplayState>(_worldEntity, (ref ReplayState s) => s.ReplayTickCounter = value);
	}
	private System.Diagnostics.Stopwatch _trackerTickStopwatch = new System.Diagnostics.Stopwatch();
	private System.Diagnostics.Stopwatch _trackerIntervalStopwatch = new System.Diagnostics.Stopwatch();
	private List<float> _trackerTickDurations;
	private List<float> _trackerApiDurations;
	private float _trackerLastTickDelay;
	private bool _isResettingForReplay;
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

	public Prop3D? SelectedProp { get; private set; }

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


	private MeshInstance3D? _buildingPreviewMesh;


	public bool IsMapEditorMode { get; set; }
	public bool IsLoadingMap { get; set; }
	public bool IsGameOver { get; private set; }
	private RuntimeTerrain _groundTerrain;
	public RuntimeTerrain GroundTerrain
	{
		get
		{
			if (_groundTerrain == null && (IsMapEditorMode || IsLoadingMap || (LobbyManager.Instance != null && LobbyManager.Instance.IsGameStarted)))
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
				_editorService.SetTerrainSplatMap(value.SplatMap);
			}
		}
	}
	private MeshInstance3D? _brushIndicatorMesh;
	private MeshInstance3D? _cameraBoundsOverlayMesh;
	private readonly List<Vector3> _pathingVerticesCache = new();
	private readonly List<Color> _pathingColorsCache = new();
	private readonly List<int> _pathingIndicesCache = new();
	private bool _pathingOverlayVisible = true;
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
		DrawCoordinate
	}
	private EditorTool _activeEditorTool = EditorTool.None;
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

	public List<EditorCoordinate> EditorCoordinates { get; } = new();
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
	public const float MIN_BRUSH_RADIUS = 1.0f;
	public const float MAX_BRUSH_RADIUS = 20.0f;
	public const float MIN_BRUSH_STRENGTH = 0.0f;
	public const float MAX_BRUSH_STRENGTH = 10.0f;
	public const float MIN_PLACEMENT_SCALE = 0.25f;
	public const float MAX_PLACEMENT_SCALE = 5.0f;
	public const float MIN_CLUMP_COUNT = 1.0f;
	public const float MAX_CLUMP_COUNT = 20.0f;
	public const float MIN_CLUMP_SCALE = 0.0f;
	public const float MAX_CLUMP_SCALE = 1.0f;

	public const float MIN_CLUMP_DENSITY = MIN_CLUMP_COUNT;
	public const float MAX_CLUMP_DENSITY = MAX_CLUMP_COUNT;
	public const float MIN_CLUMP_SCALE_VAR = MIN_CLUMP_SCALE;
	public const float MAX_CLUMP_SCALE_VAR = MAX_CLUMP_SCALE;

	public bool PlaceUnitIsEnemy { get; set; } = false;
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
	public int EditorPaintTextureIndex { get; set; } = 3;
	public int EditorCliffPaintTextureIndex { get; set; } = 1;
	public bool EditorSnapToGrid { get; set; } = false;
	public float EditorPlacementRotation { get; set; } = 0.0f;
	private float _editorPlacementScale = 1.0f;
	public float EditorPlacementScale
	{
		get => _editorPlacementScale;
		set => _editorPlacementScale = Mathf.Clamp(value, MIN_PLACEMENT_SCALE, MAX_PLACEMENT_SCALE);
	}
	public enum GridOverlayMode { Off, Mesh }
	public GridOverlayMode EditorGridMode { get; set; } = GridOverlayMode.Off;
	public bool EditorGridVisible => EditorGridMode != GridOverlayMode.Off;
	public bool EditorCameraBoundsVisible { get; set; } = false;
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
		set => EcsWorld?.Mutate<EditorState>(_worldEntity, (ref EditorState s) => s.MirrorMode = value);
	}
	public bool EditorBrushIsSquare { get; set; } = true;

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

	public bool EditorRandomRotation { get; set; } = false;
	public bool EditorRandomScale { get; set; } = false;
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

	public WaterType EditorWaterMode
	{
		get => _editorService.GetWaterMode(_worldEntity);
		set => _editorService.SetWaterMode(_worldEntity, value);
	}

	private Node? _hoveredEditorObject;
	private MeshInstance3D? _selectionHighlightMesh;
	private MeshInstance3D? _coordinatePreviewMesh;
	private MeshInstance3D? _coordinateSelectionOutlineMesh;
	private readonly List<MeshInstance3D> _coordinatePersistentMeshes = new();



	public void GenerateNewRandomPlacementRotationAndScale()
	{
		_editorService.GenerateNewRandomPlacementRotationAndScale();
	}
	public bool PasteOptionTextures { get; set; } = true;
	public bool PasteOptionHeights { get; set; } = true;
	public bool PasteOptionEntities { get; set; } = true;
	private bool _pasteOptionPathing = true;
	public bool PasteOptionPathing
	{
		get => _pasteOptionPathing;
		set
		{
			_pasteOptionPathing = value;
			UpdatePathingOverlay();
		}
	}
	public float EditorPasteRotation { get; set; } = 0.0f;

	public Node SelectedEditorObject
	{
		get => _selectedEditorObject;
		set
		{
			if (_selectedEditorObject == value) return;
			if (GodotObject.IsInstanceValid(_selectedEditorObject))
			{
				if (_selectedEditorObject is Unit3D oldUnit)
				{
					oldUnit.IsSelected = false;
				}
				else if (_selectedEditorObject is Prop3D oldProp)
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
				if (_selectedEditorObject is Unit3D newUnit)
				{
					newUnit.IsSelected = true;
				}
				else if (_selectedEditorObject is Prop3D newProp)
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
			MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
			UpdateEditorCoverageOverlay();
		}
	}
	private Node? _selectedEditorObject;
	private bool _isDraggingObject;
	private Vector3 _dragObjectStartPos;
	private Vector3 _dragObjectStartRot;
	private Vector3 _dragObjectStartScale;
	private bool _dragObjectStartIsEnemy;
	private Vector3 _dragObjectStartHitPos;
	private Vector2 _dragStartMousePos;
	private Vector3 _dragStartGroundPos;
	private bool _dragObjectHasMoved;
	private Node3D _editorPreviewNode;
	private string _editorPreviewType = "";
	private string _editorPreviewId = "";
	private bool _editorPreviewIsEnemy;


	public struct MinimapPing
	{
		public Vector3 WorldPos;
		public float LifeTime;
		public float MaxLifeTime;
	}
	public List<MinimapPing> ActivePings { get; } = new List<MinimapPing>();
	public bool ActivePingMode
	{
		get => _inputService?.ActivePingMode ?? false;
		set { if (_inputService != null) _inputService.ActivePingMode = value; }
	}



	public struct AttachmentMetadata
	{
		public AttachmentMetadata()
		{
			Scale = 1.0f;
			PositionOffset = Vector3.Zero;
			RotationOffset = Vector3.Zero;
			DefaultHand = "RightHand";
			ChildVfxScale = Vector3.One;
		}

		public string AttachmentId { get; set; }
		public string Name { get; set; }
		public string ModelPath { get; set; }
		public float Scale { get; set; } = 1.0f;
		public Vector3 PositionOffset { get; set; }
		public Vector3 RotationOffset { get; set; }
		public string DefaultHand { get; set; } = "RightHand";
		public string? ChildVfxId { get; set; }
		public Vector3 ChildVfxPosition { get; set; }
		public Vector3 ChildVfxRotation { get; set; }
		public Vector3 ChildVfxScale { get; set; } = Vector3.One;
	}

	public struct HandAttachmentOrientation
	{
		public float PositionX { get; set; }
		public float PositionY { get; set; }
		public float PositionZ { get; set; }
		public float PitchX { get; set; }
		public float YawY { get; set; }
		public float RollZ { get; set; }
		public float Scale { get; set; }
		public float ScaleX { get; set; }
		public float ScaleY { get; set; }
		public float ScaleZ { get; set; }
		public float NormalOffset { get; set; }
		public string? ParentAttachmentId { get; set; }

		[JsonIgnore]
		public Vector3 Position => new Vector3(PositionX, PositionY, PositionZ);
		[JsonIgnore]
		public Vector3 RotationDegrees => new Vector3(PitchX, YawY, RollZ);
		[JsonIgnore]
		public Vector3 ScaleVector => new Vector3(
			ScaleX > 0.0001f ? ScaleX : (Scale > 0f ? Scale : 1.0f),
			ScaleY > 0.0001f ? ScaleY : (Scale > 0f ? Scale : 1.0f),
			ScaleZ > 0.0001f ? ScaleZ : (Scale > 0f ? Scale : 1.0f));
	}

	public struct UnitObjectAttachments
	{
		public List<Dictionary<string, HandAttachmentOrientation>>? right_hand { get; set; }
		public List<Dictionary<string, HandAttachmentOrientation>>? left_hand { get; set; }
		public List<Dictionary<string, HandAttachmentOrientation>>? chest { get; set; }
		public List<Dictionary<string, HandAttachmentOrientation>>? root { get; set; }
		public List<Dictionary<string, HandAttachmentOrientation>>? head { get; set; }
		public List<Dictionary<string, HandAttachmentOrientation>>? left_foot { get; set; }
		public List<Dictionary<string, HandAttachmentOrientation>>? right_foot { get; set; }
		public List<Dictionary<string, HandAttachmentOrientation>>? ground { get; set; }
		public List<Dictionary<string, HandAttachmentOrientation>>? center { get; set; }
		public List<Dictionary<string, HandAttachmentOrientation>>? overhead { get; set; }
		public List<Dictionary<string, HandAttachmentOrientation>>? pivot { get; set; }

		public List<Dictionary<string, HandAttachmentOrientation>>? GetSocketList(string socket)
		{
			if (string.IsNullOrEmpty(socket)) return right_hand;
			string s = socket.ToLowerInvariant().Replace("_", "").Replace(" ", "");
			return s switch
			{
				"ground" or "footprint" or "base" => ground,
				"center" or "centerofmass" => center,
				"overhead" or "top" or "crown" or "roof" => overhead,
				"pivot" or "origin" => pivot,
				"root" or "hips" => root,
				"chest" or "spine" => chest,
				"head" => head,
				"lefthand" => left_hand,
				"righthand" => right_hand,
				"leftfoot" => left_foot,
				"rightfoot" => right_foot,
				_ => right_hand
			};
		}

		public void SetSocketList(string socket, List<Dictionary<string, HandAttachmentOrientation>> list)
		{
			string s = (socket ?? "righthand").ToLowerInvariant().Replace("_", "").Replace(" ", "");
			switch (s)
			{
				case "ground":
				case "footprint":
				case "base":
					ground = list;
					break;
				case "center":
				case "centerofmass":
					center = list;
					break;
				case "overhead":
				case "top":
				case "crown":
				case "roof":
					overhead = list;
					break;
				case "pivot":
				case "origin":
					pivot = list;
					break;
				case "root":
				case "hips":
					root = list;
					break;
				case "chest":
				case "spine":
					chest = list;
					break;
				case "head":
					head = list;
					break;
				case "lefthand":
					left_hand = list;
					break;
				case "righthand":
					right_hand = list;
					break;
				case "leftfoot":
					left_foot = list;
					break;
				case "rightfoot":
					right_foot = list;
					break;
				default:
					right_hand = list;
					break;
			}
		}

		public List<Dictionary<string, HandAttachmentOrientation>>? GetBoneList(HumanoidBone bone)
		{
			return bone switch
			{
				HumanoidBone.LeftHand => left_hand,
				HumanoidBone.RightHand => right_hand,
				HumanoidBone.Chest or HumanoidBone.Spine => chest,
				HumanoidBone.Hips => root,
				HumanoidBone.Head => head,
				HumanoidBone.LeftFoot => left_foot,
				HumanoidBone.RightFoot => right_foot,
				_ => right_hand
			};
		}

		public void SetBoneList(HumanoidBone bone, List<Dictionary<string, HandAttachmentOrientation>> list)
		{
			switch (bone)
			{
				case HumanoidBone.LeftHand: left_hand = list; break;
				case HumanoidBone.RightHand: right_hand = list; break;
				case HumanoidBone.Chest:
				case HumanoidBone.Spine: chest = list; break;
				case HumanoidBone.Hips: root = list; break;
				case HumanoidBone.Head: head = list; break;
				case HumanoidBone.LeftFoot: left_foot = list; break;
				case HumanoidBone.RightFoot: right_foot = list; break;
				default: right_hand = list; break;
			}
		}

		public bool TryGetOrientation(HumanoidBone hand, string attachmentId, out HandAttachmentOrientation orientation)
		{
			var list = GetBoneList(hand);
			if (list != null && !string.IsNullOrEmpty(attachmentId))
			{
				string cleanId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
					? attachmentId
					: System.IO.Path.GetFileNameWithoutExtension(attachmentId);
				foreach (var dict in list)
				{
					if (dict != null)
					{
						foreach (var kvp in dict)
						{
							if (kvp.Key.Equals(attachmentId, StringComparison.OrdinalIgnoreCase) ||
								kvp.Key.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
								System.IO.Path.GetFileNameWithoutExtension(kvp.Key).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
							{
								orientation = kvp.Value;
								return true;
							}
						}
					}
				}
			}
			orientation = default;
			return false;
		}

		public void SetOrientation(HumanoidBone hand, string attachmentId, HandAttachmentOrientation orientation)
		{
			string cleanId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
				? attachmentId
				: System.IO.Path.GetFileNameWithoutExtension(attachmentId);
			var list = GetBoneList(hand);
			if (list == null)
			{
				list = new List<Dictionary<string, HandAttachmentOrientation>>();
				SetBoneList(hand, list);
			}
			UpdateList(list, cleanId, orientation);
		}

		public bool TryGetSocketOrientation(string socket, string attachmentId, out HandAttachmentOrientation orientation)
		{
			var list = GetSocketList(socket);
			if (list != null && !string.IsNullOrEmpty(attachmentId))
			{
				string cleanId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
					? attachmentId
					: System.IO.Path.GetFileNameWithoutExtension(attachmentId);
				foreach (var dict in list)
				{
					if (dict != null)
					{
						foreach (var kvp in dict)
						{
							if (kvp.Key.Equals(attachmentId, StringComparison.OrdinalIgnoreCase) ||
								kvp.Key.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
								System.IO.Path.GetFileNameWithoutExtension(kvp.Key).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
							{
								orientation = kvp.Value;
								return true;
							}
						}
					}
				}
			}
			orientation = default;
			return false;
		}

		public void SetSocketOrientation(string socket, string attachmentId, HandAttachmentOrientation orientation)
		{
			string cleanId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
				? attachmentId
				: System.IO.Path.GetFileNameWithoutExtension(attachmentId);
			var list = GetSocketList(socket);
			if (list == null)
			{
				list = new List<Dictionary<string, HandAttachmentOrientation>>();
				SetSocketList(socket, list);
			}
			UpdateList(list, cleanId, orientation);
		}

		private static void UpdateList(List<Dictionary<string, HandAttachmentOrientation>> list, string attachmentId, HandAttachmentOrientation orientation)
		{
			foreach (var dict in list)
			{
				if (dict != null)
				{
					foreach (var key in dict.Keys.ToList())
					{
						if (key.Equals(attachmentId, StringComparison.OrdinalIgnoreCase) ||
							System.IO.Path.GetFileNameWithoutExtension(key).Equals(attachmentId, StringComparison.OrdinalIgnoreCase))
						{
							if (string.Equals(dict[key].ParentAttachmentId, orientation.ParentAttachmentId, StringComparison.OrdinalIgnoreCase))
							{
								dict[key] = orientation;
								return;
							}
						}
					}
				}
			}
			list.Add(new Dictionary<string, HandAttachmentOrientation>(StringComparer.OrdinalIgnoreCase)
			{
				[attachmentId] = orientation
			});
		}

		public bool RemoveSocketAttachment(string socket, string attachmentId, string? parentAttachmentId = null)
		{
			var list = GetSocketList(socket);
			if (list == null || string.IsNullOrEmpty(attachmentId)) return false;
			string cleanId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
				? attachmentId
				: System.IO.Path.GetFileNameWithoutExtension(attachmentId);
			bool removed = false;
			for (int i = list.Count - 1; i >= 0; i--)
			{
				var dict = list[i];
				if (dict != null)
				{
					var keysToRemove = dict.Keys.Where(k =>
					{
						bool keyMatch = k.Equals(attachmentId, StringComparison.OrdinalIgnoreCase) ||
							k.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
							System.IO.Path.GetFileNameWithoutExtension(k).Equals(cleanId, StringComparison.OrdinalIgnoreCase);

						if (!string.IsNullOrEmpty(parentAttachmentId))
						{
							return keyMatch && string.Equals(dict[k].ParentAttachmentId, parentAttachmentId, StringComparison.OrdinalIgnoreCase);
						}

						bool isChildOfThis = dict[k].ParentAttachmentId != null &&
							(dict[k].ParentAttachmentId.Equals(attachmentId, StringComparison.OrdinalIgnoreCase) ||
							 dict[k].ParentAttachmentId.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
							 System.IO.Path.GetFileNameWithoutExtension(dict[k].ParentAttachmentId).Equals(cleanId, StringComparison.OrdinalIgnoreCase));

						return (keyMatch && string.IsNullOrEmpty(dict[k].ParentAttachmentId)) || isChildOfThis;
					}).ToList();

					foreach (var k in keysToRemove)
					{
						dict.Remove(k);
						removed = true;
					}
					if (dict.Count == 0)
					{
						list.RemoveAt(i);
					}
				}
			}
			return removed;
		}

		public UnitObjectAttachments Clone()
		{
			static List<Dictionary<string, HandAttachmentOrientation>>? CloneList(List<Dictionary<string, HandAttachmentOrientation>>? src)
			{
				if (src == null) return null;
				var res = new List<Dictionary<string, HandAttachmentOrientation>>(src.Count);
				foreach (var dict in src)
				{
					if (dict == null) continue;
					var d = new Dictionary<string, HandAttachmentOrientation>(dict, StringComparer.OrdinalIgnoreCase);
					res.Add(d);
				}
				return res;
			}

			return new UnitObjectAttachments
			{
				right_hand = CloneList(right_hand),
				left_hand = CloneList(left_hand),
				chest = CloneList(chest),
				root = CloneList(root),
				head = CloneList(head),
				left_foot = CloneList(left_foot),
				right_foot = CloneList(right_foot),
				ground = CloneList(ground),
				center = CloneList(center),
				overhead = CloneList(overhead),
				pivot = CloneList(pivot)
			};
		}

		public bool HasAny()
		{
			return (right_hand != null && right_hand.Count > 0) ||
				   (left_hand != null && left_hand.Count > 0) ||
				   (chest != null && chest.Count > 0) ||
				   (root != null && root.Count > 0) ||
				   (head != null && head.Count > 0) ||
				   (left_foot != null && left_foot.Count > 0) ||
				   (right_foot != null && right_foot.Count > 0) ||
				   (ground != null && ground.Count > 0) ||
				   (center != null && center.Count > 0) ||
				   (overhead != null && overhead.Count > 0) ||
				   (pivot != null && pivot.Count > 0);
		}
	}

	[JsonConverter(typeof(UnitAnimationEntryJsonConverter))]
	public struct UnitAnimationEntry
	{
		public string Animation { get; set; }
		public string? RightHandAttachment { get; set; }
		public string? LeftHandAttachment { get; set; }
	}

	public class UnitAnimationEntryJsonConverter : JsonConverter<UnitAnimationEntry>
	{
		public override UnitAnimationEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			if (reader.TokenType == JsonTokenType.String)
			{
				return new UnitAnimationEntry
				{
					Animation = reader.GetString() ?? string.Empty
				};
			}

			if (reader.TokenType == JsonTokenType.StartObject)
			{
				using var doc = JsonDocument.ParseValue(ref reader);
				var root = doc.RootElement;
				string anim = string.Empty;
				string? right = null;
				string? left = null;

				foreach (var prop in root.EnumerateObject())
				{
					if (prop.Name.Equals("Animation", StringComparison.OrdinalIgnoreCase) ||
						prop.Name.Equals("Name", StringComparison.OrdinalIgnoreCase) ||
						prop.Name.Equals("Path", StringComparison.OrdinalIgnoreCase))
					{
						anim = prop.Value.GetString() ?? string.Empty;
					}
					else if (prop.Name.Equals("RightHandAttachment", StringComparison.OrdinalIgnoreCase) ||
							 prop.Name.Equals("RightHand", StringComparison.OrdinalIgnoreCase) ||
							 prop.Name.Equals("AttachmentRight", StringComparison.OrdinalIgnoreCase))
					{
						right = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.GetString();
					}
					else if (prop.Name.Equals("LeftHandAttachment", StringComparison.OrdinalIgnoreCase) ||
							 prop.Name.Equals("LeftHand", StringComparison.OrdinalIgnoreCase) ||
							 prop.Name.Equals("AttachmentLeft", StringComparison.OrdinalIgnoreCase))
					{
						left = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.GetString();
					}
				}

				return new UnitAnimationEntry
				{
					Animation = anim,
					RightHandAttachment = right,
					LeftHandAttachment = left
				};
			}

			return default;
		}

		public override void Write(Utf8JsonWriter writer, UnitAnimationEntry value, JsonSerializerOptions options)
		{
			if (string.IsNullOrEmpty(value.RightHandAttachment) && string.IsNullOrEmpty(value.LeftHandAttachment))
			{
				writer.WriteStringValue(value.Animation ?? string.Empty);
			}
			else
			{
				writer.WriteStartObject();
				writer.WriteString("Animation", value.Animation ?? string.Empty);
				if (value.RightHandAttachment != null)
				{
					writer.WriteString("RightHandAttachment", value.RightHandAttachment);
				}
				else
				{
					writer.WriteNull("RightHandAttachment");
				}
				if (value.LeftHandAttachment != null)
				{
					writer.WriteString("LeftHandAttachment", value.LeftHandAttachment);
				}
				else
				{
					writer.WriteNull("LeftHandAttachment");
				}
				writer.WriteEndObject();
			}
		}
	}

	public struct UnitMetadata
	{
		public UnitMetadata()
		{
			Brightness = 0.5f;
			NormalizeLuminance = true;
			DespillPlayerColor = false;
		}

		public string UnitId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public float MaxHp { get; set; }
		public float Damage { get; set; }
		public float Range { get; set; }
		public float Armor { get; set; }
		public float Speed { get; set; }
		public float AttackCooldown { get; set; }
		public float ScanRadius { get; set; }
		public float CostGold { get; set; }
		public float CostWood { get; set; }
		public float CostStone { get; set; }
		public int   PopCost { get; set; }
		public float ProductionTime { get; set; }
		public string AttackType { get; set; }
		public string ArmorType { get; set; }
		public float GoldBounty { get; set; }
		public string ModelPath { get; set; }
		public string PortraitModelPath { get; set; }
		public float Scale { get; set; } = 1.0f;
		public float YOffset { get; set; }
		public float CollisionCircle { get; set; }
		public float Brightness { get; set; } = 0.5f;
		public string Tint { get; set; }
		public bool NormalizeLuminance { get; set; } = true;
		public bool IgnorePlayerColor { get; set; }
		public bool DespillPlayerColor { get; set; } = false;
		public string[]? BuildOptions { get; set; }
		public bool IsHero { get; set; }
		public string[]? Abilities { get; set; }
		public float XpBounty { get; set; }
		public string[]? PathingCapabilities { get; set; }
		public int PathingType { get; set; }
		public float? ObstacleRadius { get; set; }
		public string[]? Targets { get; set; }
		public string[]? Weapons { get; set; }
		public string? ProjectileModelPath { get; set; }
		public Dictionary<string, List<UnitAnimationEntry>>? Animations { get; set; }
		public UnitObjectAttachments? ObjectAttachments { get; set; }
		public UnitSoundsMetadata? Sounds { get; set; }
		public string[]? StartingItems { get; set; }
		public string[]? Upgrades { get; set; }
		public string[]? StatusEffects { get; set; }
		public string[]? SoundEvents { get; set; }
		public string SpawnShader { get; set; }
		public string DeathShader { get; set; }
		public string DespawnShader
		{
			get => DeathShader;
			set => DeathShader = value;
		}

		public bool TryGetObjectAttachment(HumanoidBone hand, string attachmentId, out HandAttachmentOrientation orientation)
		{
			if (ObjectAttachments.HasValue)
			{
				return ObjectAttachments.Value.TryGetOrientation(hand, attachmentId, out orientation);
			}
			orientation = default;
			return false;
		}

		public bool TryGetObjectAttachment(string socket, string attachmentId, out HandAttachmentOrientation orientation)
		{
			if (ObjectAttachments.HasValue)
			{
				return ObjectAttachments.Value.TryGetSocketOrientation(socket, attachmentId, out orientation);
			}
			orientation = default;
			return false;
		}

		public void SetObjectAttachment(HumanoidBone hand, string attachmentId, HandAttachmentOrientation orientation)
		{
			var atts = ObjectAttachments ?? new UnitObjectAttachments();
			atts.SetOrientation(hand, attachmentId, orientation);
			ObjectAttachments = atts;
		}

		public void SetObjectAttachment(string socket, string attachmentId, HandAttachmentOrientation orientation)
		{
			var atts = ObjectAttachments ?? new UnitObjectAttachments();
			atts.SetSocketOrientation(socket, attachmentId, orientation);
			ObjectAttachments = atts;
		}

		public bool RemoveObjectAttachment(string socket, string attachmentId, string? parentAttachmentId = null)
		{
			if (ObjectAttachments.HasValue)
			{
				var atts = ObjectAttachments.Value;
				bool removed = atts.RemoveSocketAttachment(socket, attachmentId, parentAttachmentId);
				ObjectAttachments = atts;
				return removed;
			}
			return false;
		}
	}

	public struct UnitSoundsMetadata
	{
		public string[]? OnSelect { get; set; }
		public string[]? OnMoveOrder { get; set; }
		public string[]? OnAttackOrder { get; set; }
		public string[]? OnWounded { get; set; }
		public string[]? OnDeath { get; set; }
		public string[]? OnReady { get; set; }
		public string[]? OnSpellCast { get; set; }
	}

	public struct WeaponMetadata
	{
		public WeaponMetadata()
		{
			OrientToTrajectory = true;
			RibbonTaper = true;
			RibbonAdditive = true;
			EmissionEnergy = 4f;
			FresnelPower = 3f;
			FresnelFactor = 1.5f;
			NoiseScale = 3f;
			ThresholdCutoff = 0.5f;
			ThresholdSmoothness = 0.1f;
			RibbonWidth = 0.4f;
			RibbonLifetime = 0.5f;
			EmissionMaskSource = "noise";
			ForwardAxisPreset = "-Z";
			MeshScaleOffset = Vector3.One;
			PointLightIntensity = 2.0f;
			PointLightRange = 6.0f;
		}

		public string WeaponId { get; set; }
		public string Name { get; set; }
		public float Damage { get; set; }
		public float Range { get; set; }
		public float AttackCooldown { get; set; }
		public string AttackType { get; set; }
		public float ProjectileSpeed { get; set; }
		public string VisualEffect { get; set; }
		public string AttackSound { get; set; }
		public string ProjectileModelPath { get; set; }
		public string ImpactVisualEffect { get; set; }
		public string ImpactSound { get; set; }

		public float ArcHeight { get; set; }
		public float HomingWeight { get; set; }
		public float TurnRateLimit { get; set; }
		public string EaseCurve { get; set; }
		public string SpeedCurve { get; set; }
		public float Acceleration { get; set; }
		public float MaxLifetime { get; set; }
		public float FailsafeRange { get; set; }
		public string ScaleCurve { get; set; }
		public Vector3 TumbleAngularVelocity { get; set; }
		public bool OrientToTrajectory { get; set; } = true;
		public string ForwardAxisPreset { get; set; } = "-Z";
		public Vector3 MeshRotationOffset { get; set; }
		public Vector3 MeshTranslationOffset { get; set; }
		public Vector3 MeshScaleOffset { get; set; } = Vector3.One;
		public float SpiralRadius { get; set; }
		public float SpiralFrequency { get; set; }
		public float ZigzagAmplitude { get; set; }
		public float ZigzagFrequency { get; set; }
		public int MaxBounces { get; set; }
		public int PierceCount { get; set; }

		public string ShaderEffectType { get; set; }
		public string EmissionMaskSource { get; set; } = "noise";
		public string BaseColor { get; set; }
		public string EmissionColor { get; set; }
		public float EmissionEnergy { get; set; } = 4f;
		public float FresnelPower { get; set; } = 3f;
		public string FresnelColor { get; set; }
		public float FresnelFactor { get; set; } = 1.5f;
		public float NoiseScale { get; set; } = 3f;
		public string NoiseTexture { get; set; }
		public Vector2 UvScrollSpeed1 { get; set; }
		public Vector2 UvScrollSpeed2 { get; set; }
		public float ThresholdCutoff { get; set; } = 0.5f;
		public float ThresholdSmoothness { get; set; } = 0.1f;

		public bool PointLightEnabled { get; set; }
		public string PointLightColor { get; set; }
		public float PointLightIntensity { get; set; } = 2.0f;
		public float PointLightRange { get; set; } = 6.0f;

		public string RibbonTexture { get; set; }
		public string RibbonColor { get; set; }
		public float RibbonWidth { get; set; } = 0.4f;
		public float RibbonLifetime { get; set; } = 0.5f;
		public bool RibbonTaper { get; set; } = true;
		public bool RibbonAdditive { get; set; } = true;
		public float RibbonScrollSpeed { get; set; }
		public Vector3 TrailOffset { get; set; }
	}

	public struct PropMetadata
	{
		public PropMetadata()
		{
			Brightness = 0.5f;
			NormalizeLuminance = true;
			IgnorePlayerColor = true;
			DespillPlayerColor = false;
		}

		public string UnitId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public string ModelPath { get; set; }
		public string PortraitModelPath { get; set; }
		public float Scale { get; set; } = 1.25f;
		public float YOffset { get; set; }
		public float CollisionCircle { get; set; }
		public float Brightness { get; set; } = 0.5f;
		public string Tint { get; set; }
		public bool NormalizeLuminance { get; set; } = true;
		public bool IgnorePlayerColor { get; set; } = true;
		public bool DespillPlayerColor { get; set; } = false;
		public int PathingType { get; set; }
		public string SpawnShader { get; set; }
		public string DeathShader { get; set; }
		public string DespawnShader
		{
			get => DeathShader;
			set => DeathShader = value;
		}
	}

	public struct ResourceMetadata
	{
		public ResourceMetadata()
		{
			Brightness = 0.5f;
			NormalizeLuminance = true;
			IgnorePlayerColor = true;
			DespillPlayerColor = false;
		}

		public string UnitId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public string ModelPath { get; set; }
		public string PortraitModelPath { get; set; }
		public float MaxCapacity { get; set; }
		public float HarvestRate { get; set; }
		public float GrowthRate { get; set; }
		public int MaxWorkers { get; set; }
		public float Scale { get; set; } = 2.75f;
		public float YOffset { get; set; }
		public float CollisionCircle { get; set; }
		public float Brightness { get; set; } = 0.5f;
		public string Tint { get; set; }
		public bool NormalizeLuminance { get; set; } = true;
		public bool IgnorePlayerColor { get; set; } = true;
		public bool DespillPlayerColor { get; set; } = false;
		public int PathingType { get; set; }
		public string SpawnShader { get; set; }
		public string DeathShader { get; set; }
		public string DespawnShader
		{
			get => DeathShader;
			set => DeathShader = value;
		}
	}

	public struct AbilityMetadata
	{
		public string AbilityId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public string AbilityType { get; set; }
		public string IconPath { get; set; }
		public float ManaCost { get; set; }
		public float Cooldown { get; set; }
		public float TargetRange { get; set; }
		public string? VisualEffect { get; set; }
		public string? CastSound { get; set; }
		public string[]? AppliedStatusEffects { get; set; }
		public float AreaOfEffectRadius { get; set; }
		public float Damage { get; set; }
		public float Healing { get; set; }
		public string? SummonedUnitId { get; set; }
		public int SummonCount { get; set; }
		public float SummonDuration { get; set; }
	}

	public struct UpgradeMetadata
	{
		public string UpgradeId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public float CostGold { get; set; }
		public float CostWood { get; set; }
		public float CostStone { get; set; }
		public float ResearchTime { get; set; }
		public string Requirement { get; set; }
		public int MaxLevel { get; set; }
		public string[]? AffectedUnitIds { get; set; }
		public float MaxHpBonus { get; set; }
		public float DamageBonus { get; set; }
		public float ArmorBonus { get; set; }
		public float SpeedBonus { get; set; }
	}

	public struct ItemMetadata
	{
		public string ItemId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public string ItemClass { get; set; }
		public float CostGold { get; set; }
		public string UseAbility { get; set; }
		public int ChargeCount { get; set; }
		public string CooldownLink { get; set; }
		public bool CanDrop { get; set; }
		public int ItemLevel { get; set; }
		public string IconPath { get; set; }
		public string[]? PassiveStatusEffects { get; set; }
		public string[]? GrantedWeapons { get; set; }
		public bool IsContainer { get; set; }
		public int ContainerSize { get; set; }
		public string Requirements { get; set; }
	}

	public struct TextureMetadata
	{
		public string Hash { get; set; }
		public int SwatchIndex { get; set; }
		public float ScaleFactor { get; set; }
		public string AssetType { get; set; }
		public int TextureSize { get; set; }
		public string NoiseConfig { get; set; }
		public float Brightness { get; set; }
		public string Tint { get; set; }
		public float RoughnessScale { get; set; }
		public float NormalScale { get; set; }
		public float HeightScale { get; set; }
		public float HeightOffset { get; set; }
		public float CrevicePower { get; set; }
		public string TileMode { get; set; }
		public float UvScale { get; set; }
		public float StochasticTileSize { get; set; }
		public float CrossFade { get; set; }
		public float Contrast { get; set; }
		public float Saturation { get; set; }
		public float Specular { get; set; }
		public float Roughness { get; set; }
		public float Metallic { get; set; }
	}

	public struct DecalMetadata
	{
		public string Hash { get; set; }
		public string Tint { get; set; }
		public float Brightness { get; set; }
		public float Contrast { get; set; }
		public float Saturation { get; set; }
		public float Opacity { get; set; }
		public float AlbedoMix { get; set; }
		public float NormalStrength { get; set; }
		public float Roughness { get; set; }
		public float Metallic { get; set; }
		public string BlendMode { get; set; }
		public string AssetType { get; set; }
		public string TextureNormal { get; set; }
		public string TextureOrm { get; set; }
		public string TextureEmission { get; set; }
		public float EmissionEnergy { get; set; }
		public bool AnimateOpacity { get; set; }
		public float OpacityPulseSpeed { get; set; }
		public float MinOpacity { get; set; }
		public float MaxOpacity { get; set; }
		public bool AnimateEmission { get; set; }
		public float EmissionPulseSpeed { get; set; }
		public float MinEmission { get; set; }
		public float MaxEmission { get; set; }
		public bool AnimateScale { get; set; }
		public float ScalePulseSpeed { get; set; }
		public float MinScaleRatio { get; set; }
		public float MaxScaleRatio { get; set; }
		public float UpperFade { get; set; }
		public float LowerFade { get; set; }
	}

	public struct VfxMetadata
	{
		public string Hash { get; set; }
		public int Columns { get; set; }
		public int Rows { get; set; }
		public float Fps { get; set; }
		public bool SubframeBlend { get; set; }
		public string AssetType { get; set; }
	}

	public struct GlbItemMetadata
	{
		public GlbItemMetadata() { }

		public string Hash { get; set; }
		public string DefaultAssetType { get; set; }
		public float MinY { get; set; }
		public float YOffset { get; set; }
		public float Scale { get; set; }
		public float CollisionCircleRatio { get; set; }
		public float CollisionRadius { get; set; }
		public float Brightness { get; set; }
		public float Contrast { get; set; }
		public float Saturation { get; set; }
		public bool NormalizeLuminance { get; set; }
		public bool DespillPlayerColor { get; set; } = false;
		public float RotX { get; set; }
		public float RotY { get; set; }
		public float RotZ { get; set; }
		public object WeaponLayers { get; set; }
		public string WeaponPreset { get; set; }
		public string WeaponRibbon { get; set; }
		public bool IgnorePlayerColor { get; set; }
		public string TeamColorMask { get; set; }
		public string SpawnShader { get; set; }
		public string DeathShader { get; set; }
		public string DespawnShader
		{
			get => DeathShader;
			set => DeathShader = value;
		}
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

	public const float TimeOfDayCycleDuration = 90f;

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



	public const float ResourceCap = ResourceConstants.ResourceCap;



	public static readonly Dictionary<string, UnitMetadata> UnitRegistry = new(StringComparer.OrdinalIgnoreCase);
	public static readonly Dictionary<string, UnitMetadata> BuildingRegistry = new(StringComparer.OrdinalIgnoreCase);
	public static readonly Dictionary<string, PropMetadata> PropRegistry = new(StringComparer.OrdinalIgnoreCase);
	public static readonly Dictionary<string, ResourceMetadata> ResourceRegistry = new(StringComparer.OrdinalIgnoreCase);
	public static readonly Dictionary<string, WeaponMetadata> WeaponRegistry = new(StringComparer.OrdinalIgnoreCase);
	public static readonly Dictionary<string, AttachmentMetadata> AttachmentRegistry = new(StringComparer.OrdinalIgnoreCase);
	public static readonly Dictionary<string, ItemMetadata> ItemRegistry = new(StringComparer.OrdinalIgnoreCase);

	public static bool TryGetUnitOrBuildingMetadata(string? unitId, out UnitMetadata meta)
	{
		meta = default;
		if (string.IsNullOrEmpty(unitId)) return false;

		if (UnitRegistry.TryGetValue(unitId, out meta)) return true;
		if (BuildingRegistry != null && BuildingRegistry.TryGetValue(unitId, out meta)) return true;

		string cleanId = System.IO.Path.GetFileNameWithoutExtension(unitId);
		if (UnitRegistry.TryGetValue(cleanId, out meta)) return true;
		if (BuildingRegistry != null && BuildingRegistry.TryGetValue(cleanId, out meta)) return true;

		foreach (var kvp in UnitRegistry)
		{
			if (kvp.Key.Equals(unitId, StringComparison.OrdinalIgnoreCase) ||
				kvp.Key.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
				System.IO.Path.GetFileNameWithoutExtension(kvp.Key).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
			{
				meta = kvp.Value;
				return true;
			}
		}

		if (BuildingRegistry != null)
		{
			foreach (var kvp in BuildingRegistry)
			{
				if (kvp.Key.Equals(unitId, StringComparison.OrdinalIgnoreCase) ||
					kvp.Key.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
					System.IO.Path.GetFileNameWithoutExtension(kvp.Key).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
				{
					meta = kvp.Value;
					return true;
				}
			}
		}

		return false;
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

	private IMapScript _activeMapScript;
	public static string? PendingMapScriptPath { get; set; }

	private System.Runtime.Loader.AssemblyLoadContext? _mapScriptLoadContext;

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

	public void TriggerKillUnit(Unit3D unit, bool executeDespawnShader = true, bool playDeathAnimation = true)
	{
		KillUnit(unit, executeDespawnShader, playDeathAnimation);
	}

	private void InitializePlayerResources(Entity playerEntity)
	{
		var resourcesDict = new Dictionary<ResourceId, int>
		{
			{ _goldResourceId, 500 },
			{ _woodResourceId, 400 },
			{ _stoneResourceId, 200 }
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

	float IGameAPI.Gold
	{
		get
		{
			if (EcsWorld != null && _playerEntity != Entity.Null && EcsWorld.IsAlive(_playerEntity) &&
				EcsWorld.TryGet<PlayerResources>(_playerEntity, out var res) &&
				res.Value.TryGetValue(_goldResourceId, out var val))
				return val;
			return _goldBackup;
		}
		set
		{
			if (EcsWorld != null && _playerEntity != Entity.Null && EcsWorld.IsAlive(_playerEntity))
				EcsWorld.Mutate<PlayerResources>(_playerEntity, (ref PlayerResources r) =>
				{
					if (r.Value.ContainsKey(_goldResourceId))
						r.Value[_goldResourceId] = (int)value;
				});
			else
				_goldBackup = value;
			InGameHUD.Instance?.RefreshUI(SelectedUnits);
		}
	}

	float IGameAPI.Wood
	{
		get
		{
			if (EcsWorld != null && _playerEntity != Entity.Null && EcsWorld.IsAlive(_playerEntity) &&
				EcsWorld.TryGet<PlayerResources>(_playerEntity, out var res) &&
				res.Value.TryGetValue(_woodResourceId, out var val))
				return val;
			return _woodBackup;
		}
		set
		{
			if (EcsWorld != null && _playerEntity != Entity.Null && EcsWorld.IsAlive(_playerEntity))
				EcsWorld.Mutate<PlayerResources>(_playerEntity, (ref PlayerResources r) =>
				{
					if (r.Value.ContainsKey(_woodResourceId))
						r.Value[_woodResourceId] = (int)value;
				});
			else
				_woodBackup = value;
			InGameHUD.Instance?.RefreshUI(SelectedUnits);
		}
	}

	float IGameAPI.Stone
	{
		get
		{
			if (EcsWorld != null && _playerEntity != Entity.Null && EcsWorld.IsAlive(_playerEntity) &&
				EcsWorld.TryGet<PlayerResources>(_playerEntity, out var res) &&
				res.Value.TryGetValue(_stoneResourceId, out var val))
				return val;
			return _stoneBackup;
		}
		set
		{
			if (EcsWorld != null && _playerEntity != Entity.Null && EcsWorld.IsAlive(_playerEntity))
				EcsWorld.Mutate<PlayerResources>(_playerEntity, (ref PlayerResources r) =>
				{
					if (r.Value.ContainsKey(_stoneResourceId))
						r.Value[_stoneResourceId] = (int)value;
				});
			else
				_stoneBackup = value;
			InGameHUD.Instance?.RefreshUI(SelectedUnits);
		}
	}

	float IGameAPI.GameElapsedTime => GameElapsedTime;

	IUnit IGameAPI.SpawnUnit(string unitTypeId, System.Numerics.Vector3 position, bool isEnemy, bool bypassPopulation, bool executeSpawnShader)
	{
		var pos = new Vector3(position.X, position.Y, position.Z);
		pos.Y = GetTerrainHeightAt(pos);
		bool isBuilding = false;
		if (!UnitRegistry.TryGetValue(unitTypeId, out var meta))
		{
			if (BuildingRegistry.TryGetValue(unitTypeId, out meta))
				isBuilding = true;
			else
				throw new ArgumentException($"Unit ID '{unitTypeId}' not found in registry.");
		}
		int ownerPeerId = _localPeerId;
		if (isEnemy)
		{
			ownerPeerId = -1; // Default to AI
			var mappingEntity = _worldEntity;
			if (mappingEntity != Entity.Null && EcsWorld.Has<NetworkMappingState>(mappingEntity))
			{
				var mapping = EcsWorld.Get<NetworkMappingState>(mappingEntity);
				foreach (var kvp in mapping.PeerIdToPlayerEntityMap)
				{
					if (kvp.Key != _localPeerId)
					{
						ownerPeerId = kvp.Key;
						break;
					}
				}
			}
		}
		bool actualIsEnemy = NetworkService.ArePeersEnemies(_localPeerId, ownerPeerId);
		Entity playerOwnerEntity = _playerEntity;
		if (_peerIdToPlayerEntityMap != null && _peerIdToPlayerEntityMap.TryGetValue(ownerPeerId, out var pe) && EcsWorld.IsAlive(pe))
		{
			playerOwnerEntity = pe;
		}
		else if (actualIsEnemy && _enemyPlayerEntity != Entity.Null && EcsWorld.IsAlive(_enemyPlayerEntity))
		{
			playerOwnerEntity = _enemyPlayerEntity;
		}
		var playerOwner = playerOwnerEntity.AsPlayerEntity(EcsWorld);
		
		string targetModel = meta.ModelPath;
		if (string.IsNullOrEmpty(targetModel))
		{
			throw new ArgumentException($"Unit ID '{unitTypeId}' has no assigned 3D model asset in registry.");
		}
		string modelPath = _unitSpawnService.GetFallbackModelPath(targetModel, isBuilding);

		string name = actualIsEnemy ? _unitSpawnService.GetEnemyUnitName(unitTypeId, meta.Name) : meta.Name;

		var entity = CreateEcsUnit(unitTypeId, name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, meta.Speed, pos, playerOwner);
		if (bypassPopulation)
		{
			EcsWorld.Add(entity, new BypassPopulationTag());
		}
		SpawnUnit3D(entity, unitTypeId, modelPath, pos, isBuilding, actualIsEnemy, bypassPopulation, -1, executeSpawnShader);
		
		return GetUnitWrapper(entity);
	}

	void IGameAPI.SpawnResourceNode(string resourceType, System.Numerics.Vector3 position, float amount)
	{
		var pos = new Vector3(position.X, position.Y, position.Z);
		var prop = SpawnPropExternal(resourceType, pos);
		if (prop != null)
		{
			prop.ResourceAmount = amount;
		}
	}

	IEnumerable<IUnit> IGameAPI.GetAllUnits()
	{
		var list = new List<IUnit>();
		foreach (var u in AllUnits)
		{
			if (GodotObject.IsInstanceValid(u) && EcsWorld.IsAlive(u.Entity))
			{
				list.Add(GetUnitWrapper(u.Entity));
			}
		}
		return list;
	}

	IEnumerable<IUnit> IGameAPI.GetUnitsInRadius(System.Numerics.Vector3 center, float radius)
	{
		var list = new List<IUnit>();
		var godotCenter = new Vector3(center.X, center.Y, center.Z);
		foreach (var u in AllUnits)
		{
			if (GodotObject.IsInstanceValid(u) && EcsWorld.IsAlive(u.Entity) && u.GlobalPosition.DistanceTo(godotCenter) <= radius)
			{
				list.Add(GetUnitWrapper(u.Entity));
			}
		}
		return list;
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

	int IGameAPI.ResourceNodeCount => GetCachedResourceNodes().Count;

	IResourceNode IGameAPI.GetResourceNode(int index)
	{
		var list = GetCachedResourceNodes();
		return (index >= 0 && index < list.Count) ? list[index] : null!;
	}

	void IGameAPI.ShowFeedbackText(string text, System.Numerics.Vector3 color)
	{
		if (InGameHUD.Instance != null)
		{
			var gColor = new Color(color.X, color.Y, color.Z);
			InGameHUD.Instance.CallDeferred(nameof(InGameHUD.ShowFeedbackText), text, gColor);
		}
	}


	void IGameAPI.TriggerVictory()
	{
		GD.Print("[GameHost] Victory triggered by map script!");
		IsGameOver = true;
		UIManager.Instance?.CallDeferred(nameof(UIManager.TransitionTo), (int)GameScreen.GameOver, true);
	}

	void IGameAPI.TriggerDefeat()
	{
		GD.Print("[GameHost] Defeat triggered by map script!");
		IsGameOver = true;
		UIManager.Instance?.CallDeferred(nameof(UIManager.TransitionTo), (int)GameScreen.GameOver, false);
	}

	IUnit? IGameAPI.GetCastle(bool isEnemy)
	{
		foreach (var u in AllUnits)
		{
			if (GodotObject.IsInstanceValid(u) && u.UnitId == "castle" && u.IsEnemy == isEnemy && EcsWorld.IsAlive(u.Entity) && !EcsWorld.Has<Dead>(u.Entity))
			{
				return GetUnitWrapper(u.Entity);
			}
		}
		return null;
	}

	void IGameAPI.SpawnTargetIndicator(System.Numerics.Vector3 position, System.Numerics.Vector3 color)
	{
		SpawnTargetIndicator(new Godot.Vector3(position.X, position.Y, position.Z), new Godot.Color(color.X, color.Y, color.Z));
	}

	int IGameAPI.MaxPopulation
	{
		get => MaxPopulation;
		set => MaxPopulation = value;
	}

	int IGameAPI.CurrentPopulation => CurrentPopulation;

	IUnit? IGameAPI.GetUnitById(int uniqueId)
	{
		foreach (var u in AllUnits)
		{
			if (GodotObject.IsInstanceValid(u) && u.Entity.Id == uniqueId && EcsWorld.IsAlive(u.Entity))
			{
				return GetUnitWrapper(u.Entity);
			}
		}
		return null;
	}

	IEnumerable<IUnit> IGameAPI.GetSelectedUnits()
	{
		var list = new List<IUnit>();
		foreach (var u in SelectedUnits)
		{
			if (GodotObject.IsInstanceValid(u) && EcsWorld.IsAlive(u.Entity))
			{
				list.Add(GetUnitWrapper(u.Entity));
			}
		}
		return list;
	}

	void IGameAPI.PingMinimap(System.Numerics.Vector3 position)
	{
		AddMinimapPing(new Vector3(position.X, position.Y, position.Z));
	}

	void IGameAPI.StartBuildingPlacement(string unitTypeId)
	{
		EnterBuildingPlacement(unitTypeId);
	}

	void IGameAPI.GenerateMapDirectory(string mapName, string? targetDirectory)
	{
		string parentDir = string.IsNullOrEmpty(targetDirectory) ? "user://maps" : targetDirectory;
		string globalParentDir = ProjectSettings.GlobalizePath(parentDir);
		string mapDir = System.IO.Path.Combine(globalParentDir, mapName);
		System.IO.Directory.CreateDirectory(mapDir);

		string projectRoot = PathUtils.GetProjectRoot();
		string templateDir = PathUtils.FindPath("MapTemplate");
		if (!System.IO.Directory.Exists(templateDir))
		{
			templateDir = System.IO.Path.Combine(projectRoot, "..", "MapTemplate");
		}
		string templateScriptPath = System.IO.Path.Combine(templateDir, "MapScript.cs");

		string scriptContent;
		if (System.IO.File.Exists(templateScriptPath))
		{
			scriptContent = System.IO.File.ReadAllText(templateScriptPath).Replace("__MAP_NAME__", mapName);
		}
		else
		{
			scriptContent = $@"namespace Realm.Maps;

using Realm.MapAPI;

public class {mapName} : IMapScript
{{
    public void Initialize(IGameAPI api)
    {{
    }}

    public void Update(IGameAPI api, float delta)
    {{
    }}
}}
";
		}
		System.IO.File.WriteAllText(System.IO.Path.Combine(mapDir, "MapScript.cs"), scriptContent);
		MapJsonFormatter.SaveFormattedJson(System.IO.Path.Combine(mapDir, "metadata.json"), "{}");
		MapJsonFormatter.SaveFormattedJson(System.IO.Path.Combine(mapDir, "terrain.json"), "{}");

		EnsureMapProjectFiles(mapDir);
	}

	void IGameAPI.WriteSavedData(string fileName, string content)
	{
		if (string.IsNullOrEmpty(fileName) || fileName.Contains("..") || fileName.Contains("/") || fileName.Contains("\\"))
		{
			GD.PrintErr($"[Sandbox block] Blocked invalid or traversal path: {fileName}");
			return;
		}

		string mapNameOnly = System.IO.Path.GetFileNameWithoutExtension(ActiveMapName);
		string targetDir = System.IO.Path.Combine(OS.GetUserDataDir(), "saved_data", mapNameOnly);
		System.IO.Directory.CreateDirectory(targetDir);

		string targetFile = System.IO.Path.Combine(targetDir, fileName);
		System.IO.File.WriteAllText(targetFile, content);
	}

	string IGameAPI.ReadSavedData(string fileName)
	{
		if (string.IsNullOrEmpty(fileName) || fileName.Contains("..") || fileName.Contains("/") || fileName.Contains("\\"))
		{
			GD.PrintErr($"[Sandbox block] Blocked invalid or traversal path: {fileName}");
			return string.Empty;
		}

		string mapNameOnly = System.IO.Path.GetFileNameWithoutExtension(ActiveMapName);
		string targetDir = System.IO.Path.Combine(OS.GetUserDataDir(), "saved_data", mapNameOnly);
		string targetFile = System.IO.Path.Combine(targetDir, fileName);

		if (!System.IO.File.Exists(targetFile))
		{
			return string.Empty;
		}

		return System.IO.File.ReadAllText(targetFile);
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
		{
			templateDir = System.IO.Path.Combine(projectRoot, "..", "MapTemplate");
		}
		string templateCsprojPath = PathUtils.FindPath("MapTemplate/MapScript.csproj");
		if (!System.IO.File.Exists(templateCsprojPath))
		{
			templateCsprojPath = System.IO.Path.Combine(templateDir, "MapScript.csproj");
		}

		string templateTargetsPath = PathUtils.FindPath("MapTemplate/Directory.Build.targets");
		if (!System.IO.File.Exists(templateTargetsPath))
		{
			templateTargetsPath = System.IO.Path.Combine(templateDir, "Directory.Build.targets");
		}

		string templateVscodeSettingsPath = PathUtils.FindPath("MapTemplate/.vscode/settings.json");
		if (!System.IO.File.Exists(templateVscodeSettingsPath))
		{
			templateVscodeSettingsPath = System.IO.Path.Combine(templateDir, ".vscode", "settings.json");
		}

		if (System.IO.File.Exists(templateVscodeSettingsPath))
		{
			string vscodeSettingsContent = System.IO.File.ReadAllText(templateVscodeSettingsPath);
			System.IO.File.WriteAllText(vscodeSettingsPath, vscodeSettingsContent);
		}

		string repoRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectRoot, ".."));
		string sourceDll = System.IO.Path.Combine(repoRoot, "Realm.MapAPI", "bin", "Release", "net10.0", "Realm.MapAPI.dll");
		string sourceXml = System.IO.Path.Combine(repoRoot, "Realm.MapAPI", "bin", "Release", "net10.0", "Realm.MapAPI.xml");

		if (!System.IO.File.Exists(sourceDll))
		{
			sourceDll = System.IO.Path.Combine(repoRoot, "Realm.MapAPI", "bin", "Debug", "net10.0", "Realm.MapAPI.dll");
			sourceXml = System.IO.Path.Combine(repoRoot, "Realm.MapAPI", "bin", "Debug", "net10.0", "Realm.MapAPI.xml");
		}

		bool TryCopyMapApiDll(string dllPath, string xmlPath)
		{
			if (System.IO.File.Exists(dllPath))
			{
				void CopyIfDifferent(string src, string dst)
				{
					if (System.IO.File.Exists(dst))
					{
						var sInfo = new System.IO.FileInfo(src);
						var dInfo = new System.IO.FileInfo(dst);
						if (sInfo.Length == dInfo.Length)
						{
							byte[] sBytes = System.IO.File.ReadAllBytes(src);
							byte[] dBytes = System.IO.File.ReadAllBytes(dst);
							if (sBytes.AsSpan().SequenceEqual(dBytes))
							{
								return;
							}
						}
					}
					System.IO.File.Copy(src, dst, true);
				}

				CopyIfDifferent(dllPath, System.IO.Path.Combine(libDir, "Realm.MapAPI.dll"));
				if (System.IO.File.Exists(xmlPath))
					CopyIfDifferent(xmlPath, System.IO.Path.Combine(libDir, "Realm.MapAPI.xml"));
				string pdbPath = System.IO.Path.ChangeExtension(dllPath, ".pdb");
				if (System.IO.File.Exists(pdbPath))
					CopyIfDifferent(pdbPath, System.IO.Path.Combine(libDir, "Realm.MapAPI.pdb"));
				return true;
			}
			return false;
		}

		if (!TryCopyMapApiDll(sourceDll, sourceXml))
		{
			// Fallback: try from MapTemplate/lib/ (populated by post-build target in Realm.MapAPI.csproj)
			string templateLib = System.IO.Path.Combine(templateDir, "lib");
			string templateDll = System.IO.Path.Combine(templateLib, "Realm.MapAPI.dll");
			string templateXml = System.IO.Path.Combine(templateLib, "Realm.MapAPI.xml");
			if (!TryCopyMapApiDll(templateDll, templateXml))
			{
				// Last resort: auto-build Realm.MapAPI to generate the DLL
				string mapApiCsproj = System.IO.Path.Combine(repoRoot, "Realm.MapAPI", "Realm.MapAPI.csproj");
				if (System.IO.File.Exists(mapApiCsproj))
				{
					GD.Print("Realm.MapAPI.dll not found. Auto-building Realm.MapAPI...");
					using var buildProcess = new System.Diagnostics.Process();
					buildProcess.StartInfo.FileName = "dotnet";
					buildProcess.StartInfo.Arguments = $"build \"{mapApiCsproj}\" -c Debug";
					buildProcess.StartInfo.CreateNoWindow = true;
					buildProcess.StartInfo.UseShellExecute = false;
					buildProcess.Start();
					buildProcess.WaitForExit();

					sourceDll = System.IO.Path.Combine(repoRoot, "Realm.MapAPI", "bin", "Debug", "net10.0", "Realm.MapAPI.dll");
					sourceXml = System.IO.Path.Combine(repoRoot, "Realm.MapAPI", "bin", "Debug", "net10.0", "Realm.MapAPI.xml");
					TryCopyMapApiDll(sourceDll, sourceXml);
				}
			}
		}

		if (System.IO.File.Exists(templateCsprojPath))
		{
			string csprojContent = System.IO.File.ReadAllText(templateCsprojPath);
			System.IO.File.WriteAllText(csprojPath, csprojContent);
		}

		string targetsPath = System.IO.Path.Combine(mapDir, "Directory.Build.targets");
		if (System.IO.File.Exists(templateTargetsPath))
		{
			string targetsContent = System.IO.File.ReadAllText(templateTargetsPath);
			System.IO.File.WriteAllText(targetsPath, targetsContent);
		}

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
		catch
		{
		}
	}

	event Action<IUnit>? IGameAPI.OnUnitCreated
	{
		add => OnUnitCreated += value;
		remove => OnUnitCreated -= value;
	}

	event Action<IUnit, IUnit?>? IGameAPI.OnUnitDied
	{
		add => OnUnitDied += value;
		remove => OnUnitDied -= value;
	}

	event Action<IUnit, IUnit, float>? IGameAPI.OnUnitDamaged
	{
		add => OnUnitDamaged += value;
		remove => OnUnitDamaged -= value;
	}

	event Action<IUnit?, string, System.Numerics.Vector3>? IGameAPI.OnSpellCast
	{
		add => OnSpellCast += value;
		remove => OnSpellCast -= value;
	}

	event Action<string, IUnit?>? IGameAPI.OnPlayerChatMessage
	{
		add => OnPlayerChatMessage += value;
		remove => OnPlayerChatMessage -= value;
	}

	event Action<IUnit>? IGameAPI.OnUnitSelected
	{
		add => OnUnitSelected += value;
		remove => OnUnitSelected -= value;
	}

	event Action<IUnit, int>? IGameAPI.OnUnitEnterZone
	{
		add => OnUnitEnterZone += value;
		remove => OnUnitEnterZone -= value;
	}

	event Action<IUnit, IUnit>? IGameAPI.OnUnitAttacked
	{
		add => OnUnitAttacked += value;
		remove => OnUnitAttacked -= value;
	}


	void IGameAPI.CreateFloatingText(string text, System.Numerics.Vector3 position, System.Numerics.Vector3 color, float duration)
	{
		Callable.From(() =>
		{
			var label = new Label3D();
			label.Text = text;
			label.Modulate = new Color(color.X, color.Y, color.Z);
			label.OutlineModulate = Colors.Black;
			label.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
			label.Position = new Vector3(position.X, position.Y + 1.5f, position.Z);
			label.FontSize = 48;
			AddChild(label);

			var tween = CreateTween();
			tween.SetParallel(true);
			tween.TweenProperty(label, "position", label.Position + new Vector3(0, 2.0f, 0), duration);
			tween.TweenProperty(label, "modulate:a", 0.0f, duration);
			tween.Chain().TweenCallback(Callable.From(label.QueueFree));
		}).CallDeferred();
	}

	void IGameAPI.SpawnVisualEffect(string effectTypeId, System.Numerics.Vector3 position, float scale)
	{
		Callable.From(() =>
		{
			var pos = new Vector3(position.X, position.Y, position.Z);
			var def = GetAbilityDefinition(effectTypeId);
			if (def != null)
			{
				_fxService.SpawnAbilityEffect(this, def, pos, scale);
			}
			else
			{
				_fxService.SpawnSpritesheetEffect(this, effectTypeId, pos + new Vector3(0, 0.5f, 0), 4, 4, 0.05f, scale * 6f);
			}
		}).CallDeferred();
	}

	void IGameAPI.AddBuff(IUnit unit, string buffId, float duration)
	{
		unit.AddBuff(buffId, duration);
	}

	void IGameAPI.RegisterBuffModifier(string buffId, string statName, bool isPercentage, float value)
	{
		var statId = new Realm.Ecs.Common.StatId(statName);
		var modType = isPercentage ? Realm.Ecs.Components.Stats.ModifierType.Percentage : Realm.Ecs.Components.Stats.ModifierType.Flat;
		var modifier = new Realm.Ecs.Components.Stats.StatModifier(statId, modType, value);
		if (!Realm.Ecs.Common.BuffRegistry.BuffModifiers.TryGetValue(buffId, out var list))
		{
			list = new System.Collections.Generic.List<Realm.Ecs.Components.Stats.StatModifier>();
			Realm.Ecs.Common.BuffRegistry.BuffModifiers[buffId] = list;
		}
		list.Add(modifier);
	}

	void IGameAPI.CastAbility(IUnit unit, string abilityId, System.Numerics.Vector3 targetPosition)
	{
		var godotPos = new Godot.Vector3(targetPosition.X, targetPosition.Y, targetPosition.Z);
		var def = GetAbilityDefinition(abilityId);
		var casterEnt = unit is IEcsEntityWrapper w ? w.Entity : Entity.Null;

		if (def != null)
		{
			_fxService.SpawnAbilityEffect(this, def, godotPos);
			if (def.Damage > 0f)
			{
				float aoe = def.AreaOfEffectRadius > 0f ? def.AreaOfEffectRadius : 4.0f;
				_simulationService.DealSpellDamageAOE(targetPosition, aoe, def.Damage, casterEnt);
			}
			else if (def.Healing > 0f)
			{
				float aoe = def.AreaOfEffectRadius > 0f ? def.AreaOfEffectRadius : 4.0f;
				_simulationService.HealAOE(targetPosition, aoe, def.Healing);
			}
			else
			{
				OnSpellCast?.Invoke(unit, abilityId, targetPosition);
			}
		}
		else
		{
			OnSpellCast?.Invoke(unit, abilityId, targetPosition);
		}
	}

	float IGameAPI.GetAbilityCooldown(IUnit unit, string abilityId)
	{
		if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			if (EcsWorld.Has<Realm.Ecs.Components.Core.Cooldowns>(wrapper.Entity))
			{
				var cds = EcsWorld.Get<Realm.Ecs.Components.Core.Cooldowns>(wrapper.Entity).Value;
				if (cds.TryGetValue(abilityId, out var val))
				{
					return val;
				}
			}
			if (EcsWorld.Has<Realm.Ecs.Components.Core.SpellCooldowns>(wrapper.Entity))
			{
				var scds = EcsWorld.Get<Realm.Ecs.Components.Core.SpellCooldowns>(wrapper.Entity).Value;
				if (scds != null && scds.TryGetValue(abilityId, out var val))
				{
					return val;
				}
			}
		}
		return 0f;
	}

	void IGameAPI.SetAbilityCooldown(IUnit unit, string abilityId, float cooldown)
	{
		if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			System.Collections.Generic.Dictionary<string, float> dict;
			if (EcsWorld.Has<Realm.Ecs.Components.Core.Cooldowns>(wrapper.Entity))
			{
				dict = EcsWorld.Get<Realm.Ecs.Components.Core.Cooldowns>(wrapper.Entity).Value;
			}
			else
			{
				dict = new System.Collections.Generic.Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
				EcsWorld.Add(wrapper.Entity, new Realm.Ecs.Components.Core.Cooldowns(dict));
			}
			dict[abilityId] = cooldown;

			if (EcsWorld.Has<Realm.Ecs.Components.Core.SpellCooldowns>(wrapper.Entity))
			{
				var scd = EcsWorld.Get<Realm.Ecs.Components.Core.SpellCooldowns>(wrapper.Entity).Value;
				if (scd != null) scd[abilityId] = cooldown;
			}
			else
			{
				var scdDict = new System.Collections.Generic.Dictionary<string, float>(StringComparer.OrdinalIgnoreCase) { [abilityId] = cooldown };
				EcsWorld.Add(wrapper.Entity, new Realm.Ecs.Components.Core.SpellCooldowns(scdDict));
			}
		}
	}

	void IGameAPI.RemoveBuff(IUnit unit, string buffId)
	{
		unit.RemoveBuff(buffId);
	}

	System.Collections.Generic.IEnumerable<string> IGameAPI.GetModifiers(IUnit unit)
	{
		return unit.GetModifiers();
	}

	void IGameAPI.SpawnProjectile(string projectileTypeId, System.Numerics.Vector3 start, System.Numerics.Vector3 target, float speed)
	{
		Callable.From(() =>
		{
			var startPos = new Vector3(start.X, start.Y, start.Z);
			if (_replayService != null && _replayService.IsRecording)
			{
				_replayService.RecordProjectile(projectileTypeId, start, target);
			}
			var targetPos = new Vector3(target.X, target.Y, target.Z);
			SpawnWeaponProjectile(startPos, targetPos, projectileTypeId);
		}).CallDeferred();
	}

	void IGameAPI.SetLeaderboardVisible(string title, bool visible)
	{
		Callable.From(() => InGameHUD.Instance?.SetLeaderboardVisible(title, visible)).CallDeferred();
	}

	void IGameAPI.SetLeaderboardValue(string label, string value)
	{
		Callable.From(() => InGameHUD.Instance?.SetLeaderboardValue(label, value)).CallDeferred();
	}

	void IGameAPI.ClearLeaderboard()
	{
		Callable.From(() => InGameHUD.Instance?.ClearLeaderboard()).CallDeferred();
	}

	void IGameAPI.StartCountdownTimer(float duration, string label)
	{
		Callable.From(() => InGameHUD.Instance?.StartCountdownTimer(duration, label)).CallDeferred();
	}

	void IGameAPI.StopCountdownTimer()
	{
		Callable.From(() => InGameHUD.Instance?.StopCountdownTimer()).CallDeferred();
	}

	void IGameAPI.ShakeCamera(float intensity, float duration)
	{
		Callable.From(() =>
		{
			var camera = MainCamera;
			if (camera == null) return;

			var startPos = camera.Position;
			var tween = CreateTween();
			float stepDuration = duration / 20f;
			for (int i = 0; i < 20; i++)
			{
				var offset = new Vector3(
					(float)(GD.RandRange(-1.0, 1.0) * intensity * 0.1),
					(float)(GD.RandRange(-1.0, 1.0) * intensity * 0.1),
					(float)(GD.RandRange(-1.0, 1.0) * intensity * 0.1)
				);
				tween.TweenProperty(camera, "position", startPos + offset, stepDuration);
			}
			tween.TweenProperty(camera, "position", startPos, stepDuration);
		}).CallDeferred();
	}

	void IGameAPI.PanCameraTo(System.Numerics.Vector3 position, float duration)
	{
		Callable.From(() =>
		{
			var camera = MainCamera;
			if (camera == null) return;

			var targetPos = new Vector3(position.X, camera.Position.Y, position.Z + 15.0f);
			var tween = CreateTween();
			tween.TweenProperty(camera, "position", targetPos, duration);
		}).CallDeferred();
	}

	void IGameAPI.SetTimeOfDay(float time)
	{
		Callable.From(() =>
		{

			float clampedTime = Mathf.Clamp(time, 0f, 24f);
			
			int index;
			if (clampedTime >= 5f && clampedTime < 6f) index = 3;
			else if (clampedTime >= 6f && clampedTime < 18f) index = 0;
			else if (clampedTime >= 18f && clampedTime < 20f) index = 1;
			else index = 2;

			float timer = (clampedTime / 24f) * TimeOfDayCycleDuration;
			UpdateDayNightVisuals(clampedTime / 24f);

			EcsWorld?.Mutate<WorldState>(_worldEntity, (ref WorldState state) =>
				EcsWorld.Set(_worldEntity, new WorldState(state.GameElapsedTime, index, timer, state.DayNightCycleEnabled)));
		}).CallDeferred();
	}

	void IGameAPI.SetDayNightCycleEnabled(bool enabled)
	{
		DayNightCycleEnabled = enabled;
	}

	void IGameAPI.SetUnitAnimation(IUnit unit, string animationName)
	{
		if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			if (GameHost.TryGetUnit3D(wrapper.Entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
			{
				unit3D.PlayAnimation(animationName);
			}
		}
	}

	void IGameAPI.SetUnitHandAttachment(IUnit unit, string hand, string? attachmentId)
	{
		if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			if (GameHost.TryGetUnit3D(wrapper.Entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
			{
				var boneHand = HumanoidBone.RightHand;
				if (!string.IsNullOrEmpty(hand) && (hand.Equals("LeftHand", StringComparison.OrdinalIgnoreCase) || hand.Equals("left", StringComparison.OrdinalIgnoreCase) || hand.Equals("hand_l", StringComparison.OrdinalIgnoreCase)))
				{
					boneHand = HumanoidBone.LeftHand;
				}
				unit3D.SetHandAttachment(boneHand, attachmentId);
			}
		}
	}

	void IGameAPI.KillUnit(IUnit unit, bool executeDespawnShader, bool playDeathAnimation)
	{
		if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			if (GameHost.TryGetUnit3D(wrapper.Entity, out var unit3D))
			{
				if (GodotObject.IsInstanceValid(unit3D))
				{
					if (!EcsWorld.Has<Dead>(wrapper.Entity))
					{
						EcsWorld.Add<Dead>(wrapper.Entity);
						Callable.From(() => KillUnit(unit3D, executeDespawnShader, playDeathAnimation)).CallDeferred();
					}
				}
			}
		}
	}

	void IGameAPI.DestroyUnit(IUnit unit, bool executeDespawnShader, bool playDeathAnimation)
	{
		if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			if (GameHost.TryGetUnit3D(wrapper.Entity, out var unit3D))
			{
				if (GodotObject.IsInstanceValid(unit3D))
				{
					SelectedUnits.Remove(unit3D);
					AllUnits.Remove(unit3D);
					EntityToUnit3D.Remove(wrapper.Entity);
					if (unit3D.UnitId == "castle")
					{
						_castlesList.Remove(unit3D);
					}
					if (unit3D.IsBuilding)
					{
						float radius = EcsWorld.Has<CollisionRadius>(wrapper.Entity) ? EcsWorld.Get<CollisionRadius>(wrapper.Entity).Value : 2.0f;
						var unitPos = EcsWorld.Has<Position>(wrapper.Entity) ? EcsWorld.Get<Position>(wrapper.Entity).Value : new System.Numerics.Vector3(unit3D.Position.X, unit3D.Position.Y, unit3D.Position.Z);
						UncarveObstacle(unitPos, radius);
					}
					if (_multiplayerActive)
					{
						if (_clientToServerEntityMap.TryGetValue(unit3D.Entity.Id, out int serverId))
						{
							_serverToClientEntityMap.Remove(serverId);
						}
						_clientToServerEntityMap.Remove(unit3D.Entity.Id);
					}
					int id = wrapper.Entity.Id;
					_unitWrapperCache.Remove(id);
					EcsWorld.Destroy(wrapper.Entity);

					unit3D.CollisionLayer = 0;
					unit3D.CollisionMask = 0;

					if (playDeathAnimation)
					{
						unit3D.PlayAnimation("Death");
					}

					string deathShader = executeDespawnShader ? GetModelDeathShader(unit3D.UnitId) : "";
					if (executeDespawnShader && string.IsNullOrEmpty(deathShader))
					{
						deathShader = GetModelDeathShader(unit3D);
					}

					if (!string.IsNullOrEmpty(deathShader))
					{
						SpawnDeathShaderManager.AnimateTransition(unit3D, deathShader, false, null, () =>
						{
							if (GodotObject.IsInstanceValid(unit3D)) unit3D.QueueFree();
						});
					}
					else if (playDeathAnimation)
					{
						var tween = CreateTween();
						tween.SetParallel(true);
						tween.TweenProperty(unit3D, "position:y", -3.0f, 1.0f);
						tween.TweenProperty(unit3D, "scale", Vector3.Zero, 1.0f);
						tween.Chain().TweenCallback(Callable.From(unit3D.QueueFree));
					}
					else
					{
						unit3D.QueueFree();
					}
				}
			}
		}
	}





	private int _nextTimerHandle;
	private readonly Dictionary<int, (float Interval, float Remaining, bool Repeating, Action Callback)> _scheduledTimers = new();

	private static readonly Random Rng = new();

	int IGameAPI.PlayerCount
	{
		get
		{
			if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true)
			{
				int count = 0;
				foreach (var p in playersState.Players)
				{
					if (p.Active) count++;
				}
				return Math.Max(1, count);
			}
			return 1;
		}
	}

	string IGameAPI.GetPlayerName(int playerIndex)
	{
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			return playersState.Players[playerIndex].Name;
		}
		return $"Player {playerIndex + 1}";
	}

	bool IGameAPI.IsPlayerActive(int playerIndex)
	{
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			return playersState.Players[playerIndex].Active;
		}
		return playerIndex == 0;
	}

	float IGameAPI.GetPlayerGold(int playerIndex)
	{
		if (playerIndex == 0) return ((IGameAPI)this).Gold;
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			return playersState.Players[playerIndex].Gold;
		}
		return 0f;
	}

	void IGameAPI.SetPlayerGold(int playerIndex, float amount)
	{
		if (playerIndex == 0) { ((IGameAPI)this).Gold = amount; return; }
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			playersState.Players[playerIndex].Gold = Math.Max(0f, amount);
		}
	}

	void IGameAPI.AdjustPlayerGold(int playerIndex, float delta)
	{
		if (playerIndex == 0) { ((IGameAPI)this).Gold += delta; return; }
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			playersState.Players[playerIndex].Gold = Math.Max(0f, playersState.Players[playerIndex].Gold + delta);
		}
	}

	IUnit IGameAPI.SpawnUnitForPlayer(string unitTypeId, System.Numerics.Vector3 position, int playerIndex, bool executeSpawnShader)
	{
		bool isEnemy = NetworkService.ArePlayerIndicesEnemies(LocalPlayerIndex, playerIndex);
		var unit = ((IGameAPI)this).SpawnUnit(unitTypeId, position, isEnemy, false, executeSpawnShader);
		unit.Player = playerIndex;
		return unit;
	}

	IEnumerable<IUnit> IGameAPI.GetUnitsOwnedByPlayer(int playerIndex)
	{
		foreach (var unit in ((IGameAPI)this).GetAllUnits())
		{
			if (unit.Player == playerIndex) yield return unit;
		}
	}

	void IGameAPI.TriggerPlayerDefeat(int playerIndex, string reason)
	{
		if (playerIndex == 0) ((IGameAPI)this).TriggerDefeat();
	}

	void IGameAPI.TriggerPlayerVictory(int playerIndex)
	{
		if (playerIndex == 0) ((IGameAPI)this).TriggerVictory();
	}

	void IGameAPI.BroadcastMessage(string message)
	{
		string formatted = $"[HOST BROADCAST] {message}";
		GD.Print(formatted);
		Realm.Godot.WasmRuntime.LogToConsole(formatted);
		((IGameAPI)this).ShowFeedbackText(message, new System.Numerics.Vector3(0.9f, 0.9f, 0.9f));
	}

	void IGameAPI.SendMessageToPlayer(int playerIndex, string message)
	{
		string formatted = $"[HOST MESSAGE P{playerIndex}] {message}";
		Realm.Godot.WasmRuntime.LogToConsole(formatted);
		if (playerIndex == 0) ((IGameAPI)this).ShowFeedbackText(message, new System.Numerics.Vector3(0.9f, 0.9f, 0.9f));
	}

	private event Action<int>? _onTimerExpired;
	event Action<int>? IGameAPI.OnTimerExpired
	{
		add => _onTimerExpired += value;
		remove => _onTimerExpired -= value;
	}

	int IGameAPI.ScheduleTimer(float delay)
	{
		int handle = _nextTimerHandle++;
		_scheduledTimers[handle] = (delay, delay, false, () => _onTimerExpired?.Invoke(handle));
		return handle;
	}

	int IGameAPI.ScheduleRepeatingTimer(float interval)
	{
		int handle = _nextTimerHandle++;
		_scheduledTimers[handle] = (interval, interval, true, () => _onTimerExpired?.Invoke(handle));
		return handle;
	}

	void IGameAPI.CancelTimer(int timerHandle)
	{
		_scheduledTimers.Remove(timerHandle);
	}

	int IGameAPI.RandomInt(int min, int max) => Rng.Next(min, max + 1);

	float IGameAPI.RandomFloat(float min, float max) => min + (float)Rng.NextDouble() * (max - min);

	System.Numerics.Vector3 IGameAPI.GetPlayerStartLocation(int playerIndex)
	{
		return System.Numerics.Vector3.Zero;
	}

	void IGameAPI.SetPlayerTeam(int playerIndex, int teamIndex)
	{
	}

	int IGameAPI.GetPlayerTeam(int playerIndex)
	{
		return 0;
	}

	void IGameAPI.SetPlayersAllied(int playerIndex, int otherPlayerIndex, bool allied)
	{
	}

	bool IGameAPI.IsPlayerComputer(int playerIndex)
	{
		if (playerIndex == 0) return false;
		if (_multiplayerActive && LobbyManager.Instance != null)
		{
			var p = LobbyManager.Instance.PlayerList.Find(x => x.Slot == playerIndex);
			if (p != null)
			{
				return p.PeerId < 0;
			}
		}
		return playerIndex == 1;
	}

	void IGameAPI.SetUnitColor(IUnit unit, System.Numerics.Vector3 color)
	{
		if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			if (GameHost.TryGetUnit3D(wrapper.Entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
				unit3D.ApplyModelTint(new Godot.Color(color.X, color.Y, color.Z));
		}
	}

	IEnumerable<IUnit> IGameAPI.GetUnitsInRadius(System.Numerics.Vector3 center, float radius, System.Func<IUnit, bool> filter)
	{
		var godotCenter = new Vector3(center.X, center.Y, center.Z);
		var list = new List<IUnit>();
		foreach (var u in AllUnits)
		{
			if (!GodotObject.IsInstanceValid(u) || !EcsWorld.IsAlive(u.Entity)) continue;
			if (u.GlobalPosition.DistanceTo(godotCenter) > radius) continue;
			var w = GetUnitWrapper(u.Entity);
			if (filter(w)) list.Add(w);
		}
		return list;
	}

	IEnumerable<IUnit> IGameAPI.GetUnitsOwnedByPlayer(int playerIndex, System.Func<IUnit, bool> filter)
	{
		var list = new List<IUnit>();
		foreach (var u in AllUnits)
		{
			if (!GodotObject.IsInstanceValid(u) || !EcsWorld.IsAlive(u.Entity)) continue;
			if (u.Player != playerIndex) continue;
			var w = GetUnitWrapper(u.Entity);
			if (filter(w)) list.Add(w);
		}
		return list;
	}

	void IGameAPI.IssueAttackMoveOrder(IUnit unit, System.Numerics.Vector3 destination)
	{
		unit.AttackMove(destination);
	}

	void IGameAPI.IssueCastOrder(IUnit caster, string abilityId, IUnit target)
	{
		caster.Attack(target);
	}

	void IGameAPI.IssueCastOrderAt(IUnit caster, string abilityId, System.Numerics.Vector3 position)
	{
		caster.AttackMove(position);
	}

	void IGameAPI.SetAbilityAutoCast(IUnit unit, string abilityId, bool active)
	{
	}

	void IGameAPI.SetPlayerComputerControlled(int playerIndex, bool isProxy)
	{
	}

	void IGameAPI.AddLeaderboardRow(string label, string value, System.Numerics.Vector3? color)
	{
		InGameHUD.Instance?.SetLeaderboardValue(label, value);
	}

	float IGameAPI.GetPlayerWood(int playerIndex)
	{
		if (playerIndex == 0) return ((IGameAPI)this).Wood;
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			return playersState.Players[playerIndex].Wood;
		}
		return 0f;
	}

	void IGameAPI.SetPlayerWood(int playerIndex, float amount)
	{
		if (playerIndex == 0) { ((IGameAPI)this).Wood = amount; return; }
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			playersState.Players[playerIndex].Wood = Math.Max(0f, amount);
		}
	}

	void IGameAPI.AdjustPlayerWood(int playerIndex, float delta)
	{
		if (playerIndex == 0) { ((IGameAPI)this).Wood += delta; return; }
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			playersState.Players[playerIndex].Wood = Math.Max(0f, playersState.Players[playerIndex].Wood + delta);
		}
	}

	void IGameAPI.SetUnitOwner(IUnit unit, int playerIndex)
	{
		unit.Player = playerIndex;
	}

	int IGameAPI.GetPlayerCurrentPopulation(int playerIndex)
	{
		if (playerIndex == 0) return ((IGameAPI)this).CurrentPopulation;
		return 0;
	}

	int IGameAPI.GetPlayerMaxPopulation(int playerIndex)
	{
		if (playerIndex == 0) return ((IGameAPI)this).MaxPopulation;
		return 200;
	}

	void IGameAPI.SetPlayerMaxPopulation(int playerIndex, int max)
	{
		if (playerIndex == 0) ((IGameAPI)this).MaxPopulation = max;
	}

	void IGameAPI.SetCountdownTimerLabel(string label)
	{
		InGameHUD.Instance?.UpdateCountdownLabel(label);
	}

	void IGameAPI.IssueAttackMoveOrderToPlayer(int playerIndex, System.Numerics.Vector3 destination)
	{
		foreach (var unit in ((IGameAPI)this).GetUnitsOwnedByPlayer(playerIndex))
		{
			if (!unit.IsDead && !unit.IsBuilding)
				unit.AttackMove(destination);
		}
	}

	int IGameAPI.CountUnitsOwnedByPlayer(int playerIndex)
	{
		int count = 0;
		foreach (var unit in ((IGameAPI)this).GetUnitsOwnedByPlayer(playerIndex))
		{
			if (!unit.IsDead)
				count++;
		}
		return count;
	}





	public event Action<IUnit, int>? OnUnitEnterZone;
	public event Action<int>? OnPlayerLeft;

	int IGameAPI.DefineZone(float minX, float minZ, float maxX, float maxZ)
	{
		if (EcsWorld?.TryGet<ScriptZonesState>(_worldEntity, out var zonesState) == true)
		{
			int handle = zonesState.Zones.Count;
			float cx = (minX + maxX) * 0.5f;
			float cz = (minZ + maxZ) * 0.5f;
			zonesState.Zones.Add(new ZoneBounds
			{
				MinX = minX,
				MinZ = minZ,
				MaxX = maxX,
				MaxZ = maxZ,
				Center = new System.Numerics.Vector3(cx, 0f, cz)
			});
			return handle;
		}
		return -1;
	}

	System.Numerics.Vector3 IGameAPI.GetZoneCenter(int zoneHandle)
	{
		if (EcsWorld?.TryGet<ScriptZonesState>(_worldEntity, out var zonesState) == true
			&& zoneHandle >= 0 && zoneHandle < zonesState.Zones.Count)
		{
			return zonesState.Zones[zoneHandle].Center;
		}
		return System.Numerics.Vector3.Zero;
	}

	private void TickZoneTriggers()
	{
		if (OnUnitEnterZone == null) return;
		if (EcsWorld == null || !EcsWorld.IsAlive(_worldEntity) || !EcsWorld.Has<ScriptZonesState>(_worldEntity)) return;

		var zones = EcsWorld.Get<ScriptZonesState>(_worldEntity).Zones;
		if (zones.Count == 0) return;

		var positionQuery = Realm.Ecs.Common.QueryCache.AllPositionAndDefinitionIdNoneDeadQuery;
		EcsWorld.Query(in positionQuery, (Entity entity, ref Position posComp) =>
		{
			var pos = new Vector3(posComp.Value.X, posComp.Value.Y, posComp.Value.Z);

			if (!EcsWorld.Has<OccupiedZones>(entity))
			{
				EcsWorld.Add(entity, new OccupiedZones(new HashSet<int>()));
			}
			var occupiedZones = EcsWorld.Get<OccupiedZones>(entity).ZoneIds;

			for (int i = 0; i < zones.Count; i++)
			{
				ref ZoneBounds z = ref System.Runtime.InteropServices.CollectionsMarshal.AsSpan(zones)[i];
				bool inside = pos.X >= z.MinX && pos.X <= z.MaxX && pos.Z >= z.MinZ && pos.Z <= z.MaxZ;

				if (inside && !occupiedZones.Contains(i))
				{
					occupiedZones.Add(i);
					if (TryGetUnit3D(entity, out _))
					{
						var wrapper = GetUnitWrapper(entity);
						OnUnitEnterZone?.Invoke(wrapper, i);
					}
				}
				else if (!inside)
				{
					occupiedZones.Remove(i);
				}
			}
		});
	}

	void IGameAPI.SetUnitRouteState(IUnit unit, int state)
	{
		unit.SetCustomData("__routeState", state);
	}

	int IGameAPI.GetUnitRouteState(IUnit unit)
	{
		string? data = unit.GetCustomData("__routeState");
		return int.TryParse(data, out int i) ? i : 0;
	}

	void IGameAPI.SetUnitLevel(IUnit unit, int level)
	{
		if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			EcsWorld.SetOrAdd(wrapper.Entity, new Realm.Ecs.Components.Meta.Level(level));
		}
	}

	int IGameAPI.GetPlayerKills(int playerIndex)
	{
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			return playersState.Players[playerIndex].KillCount;
		}
		return 0;
	}

	void IGameAPI.SetPlayerKills(int playerIndex, int kills)
	{
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			playersState.Players[playerIndex].KillCount = kills;
		}
	}

	void IGameAPI.IssueMoveOrder(IUnit unit, System.Numerics.Vector3 destination)
	{
		unit.MoveTo(destination);
	}

	void IGameAPI.SetUnitSpellImmune(IUnit unit, bool immune)
	{
		if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			if (immune)
			{
				if (!EcsWorld.Has<Realm.Ecs.Components.Tags.SpellImmune>(wrapper.Entity))
					EcsWorld.Add(wrapper.Entity, new Realm.Ecs.Components.Tags.SpellImmune());
			}
			else
			{
				if (EcsWorld.Has<Realm.Ecs.Components.Tags.SpellImmune>(wrapper.Entity))
					EcsWorld.Remove<Realm.Ecs.Components.Tags.SpellImmune>(wrapper.Entity);
			}
		}
	}

	void IGameAPI.SelectUnit(IUnit unit)
	{
		Callable.From(() =>
		{
			if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
			{
				if (GameHost.TryGetUnit3D(wrapper.Entity, out var unit3D))
				{
					if (GodotObject.IsInstanceValid(unit3D))
					{
						ClearSelection();
						SelectUnit(unit3D);
						InGameHUD.Instance?.RefreshUI(SelectedUnits);
					}
				}
			}
		}).CallDeferred();
	}

	void IGameAPI.ClearSelection()
	{
		Callable.From(() =>
		{
			ClearSelection();
			InGameHUD.Instance?.RefreshUI(SelectedUnits);
		}).CallDeferred();
	}

	bool IGameAPI.HasCoordinate(string coordinateName)
	{
		if (string.IsNullOrEmpty(coordinateName)) return false;
		string searchName = coordinateName.Trim();
		foreach (var r in EditorCoordinates)
		{
			if (r.Name.Equals(searchName, StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}

	System.Numerics.Vector3 IGameAPI.GetCoordinateMin(string coordinateName)
	{
		if (string.IsNullOrEmpty(coordinateName)) return System.Numerics.Vector3.Zero;
		string searchName = coordinateName.Trim();
		foreach (var r in EditorCoordinates)
		{
			if (r.Name.Equals(searchName, StringComparison.OrdinalIgnoreCase))
				return new System.Numerics.Vector3(r.MinX, 0f, r.MinZ);
		}
		return System.Numerics.Vector3.Zero;
	}

	System.Numerics.Vector3 IGameAPI.GetCoordinateMax(string coordinateName)
	{
		if (string.IsNullOrEmpty(coordinateName)) return System.Numerics.Vector3.Zero;
		string searchName = coordinateName.Trim();
		foreach (var r in EditorCoordinates)
		{
			if (r.Name.Equals(searchName, StringComparison.OrdinalIgnoreCase))
				return new System.Numerics.Vector3(r.MaxX, 0f, r.MaxZ);
		}
		return System.Numerics.Vector3.Zero;
	}

	bool IGameAPI.IsPositionInCoordinate(System.Numerics.Vector3 position, string coordinateName)
	{
		if (string.IsNullOrEmpty(coordinateName)) return false;
		string searchName = coordinateName.Trim();
		foreach (var r in EditorCoordinates)
		{
			if (r.Name.Equals(searchName, StringComparison.OrdinalIgnoreCase))
			{
				float minX = Math.Min(r.MinX, r.MaxX);
				float maxX = Math.Max(r.MinX, r.MaxX);
				float minZ = Math.Min(r.MinZ, r.MaxZ);
				float maxZ = Math.Max(r.MinZ, r.MaxZ);
				return position.X >= minX && position.X <= maxX && position.Z >= minZ && position.Z <= maxZ;
			}
		}
		return false;
	}

	void IGameAPI.AddUnitTypeAbility(string unitTypeId, string abilityId)
	{
		if (UnitRegistry.TryGetValue(unitTypeId, out var meta))
		{
			var abilities = meta.Abilities != null 
				? new List<string>(meta.Abilities) 
				: new List<string>();
			if (!abilities.Contains(abilityId))
			{
				abilities.Add(abilityId);
				meta.Abilities = abilities.ToArray();
				UnitRegistry[unitTypeId] = meta;
			}
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
		if (string.IsNullOrWhiteSpace(mapNameOrPath))
		{
			return null;
		}

		string norm = mapNameOrPath.Replace('\\', '/').Trim();
		if (norm.StartsWith("user://") || norm.StartsWith("res://") || System.IO.Path.IsPathRooted(norm))
		{
			string global = norm.StartsWith("user://") || norm.StartsWith("res://")
				? SafeGlobalizePath(norm)
				: norm;
			if (System.IO.Directory.Exists(global))
			{
				return global;
			}
			if (System.IO.File.Exists(global))
			{
				return System.IO.Path.GetDirectoryName(global);
			}
		}

		string? targetVersion = !string.IsNullOrWhiteSpace(version) ? version : LobbyManager.Instance?.ActiveMapVersion;
		string? manifestPath = MapAssetManager.FindManifestPath(norm, targetVersion)
			?? MapAssetManager.FindManifestPath(norm, null);

		if (!string.IsNullOrEmpty(manifestPath) && System.IO.File.Exists(manifestPath))
		{
			return System.IO.Path.GetDirectoryName(manifestPath);
		}

		string resDir = SafeGlobalizePath($"res://Maps/{norm}");
		if (System.IO.Directory.Exists(resDir))
		{
			return resDir;
		}

		string resLower = SafeGlobalizePath($"res://Maps/{norm.ToLowerInvariant()}");
		if (System.IO.Directory.Exists(resLower))
		{
			return resLower;
		}

		string userDir = SafeGlobalizePath($"user://maps/{norm}");
		if (System.IO.Directory.Exists(userDir))
		{
			return userDir;
		}

		string userLower = SafeGlobalizePath($"user://maps/{norm.ToLowerInvariant()}");
		if (System.IO.Directory.Exists(userLower))
		{
			return userLower;
		}

		return null;
	}

	public void LoadUnitMetadata(string mapName = null)
	{
		ResetAbilityCatalog();
		if (string.IsNullOrEmpty(mapName))
		{
			mapName = !string.IsNullOrEmpty(ActiveMapName) ? ActiveMapName : MapWorkspaceService.DefaultWorkspaceFolder;
		}
		ActiveMapName = mapName;
		LocalizationManager.CurrentMapName = mapName;
		LocalizationManager.SetupTranslations();

		string? resolvedDir = ResolveMapDirectory(mapName);
		string path;
		if (!string.IsNullOrEmpty(resolvedDir))
		{
			CurrentMapDirectory = resolvedDir;
			path = System.IO.Path.Combine(resolvedDir, "metadata.json");
		}
		else if (mapName.StartsWith("user://") || mapName.StartsWith("res://") || System.IO.Path.IsPathRooted(mapName))
		{
			path = System.IO.Path.Combine(mapName, "metadata.json");
		}
		else
		{
			path = $"res://Maps/{mapName}/metadata.json";
		}

		var metaService = _metadataService ?? Realm.Godot.Services.MetadataService.Instance;
		var metadata = metaService.LoadMetadata(path);

		var newUnits = new Dictionary<string, UnitMetadata>(StringComparer.OrdinalIgnoreCase);
		var newBuildings = new Dictionary<string, UnitMetadata>(StringComparer.OrdinalIgnoreCase);
		var newProps = new Dictionary<string, PropMetadata>(StringComparer.OrdinalIgnoreCase);
		var newResources = new Dictionary<string, ResourceMetadata>(StringComparer.OrdinalIgnoreCase);
		var newWeapons = new Dictionary<string, WeaponMetadata>(StringComparer.OrdinalIgnoreCase);
		var newAttachments = new Dictionary<string, AttachmentMetadata>(StringComparer.OrdinalIgnoreCase);
		var newItems = new Dictionary<string, ItemMetadata>(StringComparer.OrdinalIgnoreCase);
		var newVfx = new Dictionary<string, VfxAttachmentConfig>(StringComparer.OrdinalIgnoreCase);

		foreach (var meta in metadata.CustomWeapons)
		{
			if (!string.IsNullOrEmpty(meta.WeaponId))
				newWeapons[meta.WeaponId] = meta;
		}

		foreach (var meta in metadata.CustomAttachments)
		{
			if (!string.IsNullOrEmpty(meta.AttachmentId))
			{
				newAttachments[meta.AttachmentId] = meta;
			}
		}

		foreach (var meta in metadata.CustomItems)
		{
			if (!string.IsNullOrEmpty(meta.ItemId))
			{
				newItems[meta.ItemId] = meta;
			}
		}

		foreach (var meta in metadata.CustomUnits)
		{
			if (!string.IsNullOrEmpty(meta.UnitId))
			{
				var copy = meta;
				if (copy.Scale <= 0f) copy.Scale = 1.0f;
				newUnits[copy.UnitId] = copy;
			}
		}

		foreach (var meta in metadata.CustomBuildings)
		{
			if (!string.IsNullOrEmpty(meta.UnitId))
			{
				var copy = meta;
				if (copy.Scale <= 0f) copy.Scale = 1.5f;
				newBuildings[copy.UnitId] = copy;
			}
		}

		foreach (var meta in metadata.CustomResources)
		{
			if (!string.IsNullOrEmpty(meta.UnitId))
			{
				var copy = meta;
				if (copy.Scale <= 0f) copy.Scale = 2.75f;
				if (copy.PathingType == 0) copy.PathingType = 255;
				newResources[copy.UnitId] = copy;
			}
		}

		foreach (var meta in metadata.CustomProps)
		{
			if (!string.IsNullOrEmpty(meta.UnitId))
			{
				var copy = meta;
				if (copy.Scale <= 0f) copy.Scale = 1.25f;
				if (copy.PathingType == 0) copy.PathingType = 255;
				newProps[copy.UnitId] = copy;
			}
		}

		if (metadata.CustomAbilities.Count > 0)
		{
			RegisterCustomAbilities(metadata.CustomAbilities);
		}

		foreach (var cfg in metadata.CustomVfx)
		{
			if (!string.IsNullOrEmpty(cfg.VfxId))
			{
				newVfx[cfg.VfxId] = cfg;
			}
		}

		UnitRegistry.Clear();
		foreach (var kvp in newUnits) UnitRegistry[kvp.Key] = kvp.Value;

		BuildingRegistry.Clear();
		foreach (var kvp in newBuildings) BuildingRegistry[kvp.Key] = kvp.Value;

		PropRegistry.Clear();
		foreach (var kvp in newProps) PropRegistry[kvp.Key] = kvp.Value;

		ResourceRegistry.Clear();
		foreach (var kvp in newResources) ResourceRegistry[kvp.Key] = kvp.Value;

		WeaponRegistry.Clear();
		foreach (var kvp in newWeapons) WeaponRegistry[kvp.Key] = kvp.Value;

		AttachmentRegistry.Clear();
		foreach (var kvp in newAttachments) AttachmentRegistry[kvp.Key] = kvp.Value;

		ItemRegistry.Clear();
		foreach (var kvp in newItems) ItemRegistry[kvp.Key] = kvp.Value;

		VfxRegistry.Clear();
		foreach (var kvp in newVfx) VfxRegistry[kvp.Key] = kvp.Value;

		Prop3D.ClearModelPathCache();
	}

	public void SaveAttachmentDefaultsToMetadata(string fileName, AttachmentMetadata meta)
	{
		try
		{
			string dir = !string.IsNullOrEmpty(CurrentMapDirectory) ? CurrentMapDirectory : Godot.ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
			var assetsObj = Realm.Godot.Utils.MapAssetHelper.LoadUnionedAssets(dir) ?? new System.Text.Json.Nodes.JsonObject();
			var glbObj = assetsObj["glb"]?.AsObject();
			if (glbObj == null)
			{
				glbObj = new System.Text.Json.Nodes.JsonObject();
				assetsObj["glb"] = glbObj;
			}
			var attObj = glbObj["attachments"]?.AsObject();
			if (attObj == null)
			{
				attObj = new System.Text.Json.Nodes.JsonObject();
				glbObj["attachments"] = attObj;
			}

			var itemNode = attObj[fileName]?.AsObject() ?? new System.Text.Json.Nodes.JsonObject();
			itemNode["scale"] = meta.Scale;
			var posArr = new System.Text.Json.Nodes.JsonArray { meta.PositionOffset.X, meta.PositionOffset.Y, meta.PositionOffset.Z };
			itemNode["position_offset"] = posArr;
			var rotArr = new System.Text.Json.Nodes.JsonArray { meta.RotationOffset.X, meta.RotationOffset.Y, meta.RotationOffset.Z };
			itemNode["rotation_offset"] = rotArr;
			itemNode["default_hand"] = meta.DefaultHand ?? "RightHand";
			attObj[fileName] = itemNode;

			Realm.Godot.Utils.MapAssetHelper.SaveAssetsToManifest(dir, assetsObj, removeFromMetadata: true);
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
			string dir = !string.IsNullOrEmpty(CurrentMapDirectory) ? CurrentMapDirectory : Godot.ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
			string metaPath = System.IO.Path.Combine(dir, "metadata.json");
			if (!System.IO.File.Exists(metaPath)) return;

			string json = System.IO.File.ReadAllText(metaPath);
			var root = System.Text.Json.Nodes.JsonNode.Parse(json)?.AsObject();
			if (root == null) return;

			var unitsArr = root["CustomUnits"]?.AsArray();
			if (unitsArr != null)
			{
				for (int i = 0; i < unitsArr.Count; i++)
				{
					var uObj = unitsArr[i]?.AsObject();
					if (uObj != null && uObj["UnitId"]?.ToString() == unitId)
					{
						var animsJson = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(animations, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
						uObj["Animations"] = animsJson;
						break;
					}
				}
			}

			SaveLoadService.CleanMetadataJsonSchema(root);
			MapJsonFormatter.SaveFormattedJson(metaPath, root);
			LoadUnitMetadata(dir);
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

		string normalizedRaw = mapName.Replace('\\', '/');
		bool isCustomPath = normalizedRaw.StartsWith("user://") || normalizedRaw.StartsWith("res://") || System.IO.Path.IsPathRooted(normalizedRaw);

		if (string.IsNullOrEmpty(PendingMapScriptPath))
		{
			string? checkDir = ResolveMapDirectory(mapName);
			if (string.IsNullOrEmpty(checkDir) && isCustomPath)
			{
				checkDir = normalizedRaw;
				if (normalizedRaw.StartsWith("user://") || normalizedRaw.StartsWith("res://"))
				{
					checkDir = ProjectSettings.GlobalizePath(normalizedRaw);
				}
			}

			if (!string.IsNullOrEmpty(checkDir) && System.IO.Directory.Exists(checkDir))
			{
				string binDir = System.IO.Path.Combine(checkDir, "bin");
				if (System.IO.Directory.Exists(binDir))
				{
					var files = System.IO.Directory.GetFiles(binDir, "*.wasm", System.IO.SearchOption.AllDirectories)
						.Where(f => !f.Contains("native") && !f.Contains("obj"))
						.ToList();

					PendingMapScriptPath = files.FirstOrDefault(f => f.Contains("publish") && System.IO.Path.GetFileName(f).Equals("MapScript.wasm", StringComparison.OrdinalIgnoreCase))
						?? files.FirstOrDefault(f => f.Contains("publish"))
						?? files.FirstOrDefault(f => System.IO.Path.GetFileName(f).Equals("MapScript.wasm", StringComparison.OrdinalIgnoreCase))
						?? files.OrderByDescending(f => System.IO.File.GetLastWriteTimeUtc(f)).FirstOrDefault();
				}
				if (string.IsNullOrEmpty(PendingMapScriptPath))
				{
					var allWasm = System.IO.Directory.GetFiles(checkDir, "*.wasm", System.IO.SearchOption.AllDirectories)
						.Where(f => !f.Contains("native") && !f.Contains("obj"))
						.OrderByDescending(f => System.IO.File.GetLastWriteTimeUtc(f))
						.FirstOrDefault();
					PendingMapScriptPath = allWasm;
				}
			}
		}

		if (!string.IsNullOrEmpty(PendingMapScriptPath))
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
					string mapNameOnly = System.IO.Path.GetFileNameWithoutExtension(PendingMapScriptPath);
					if (mapNameOnly.Equals("MapScript", StringComparison.OrdinalIgnoreCase))
					{
						string parentDir = System.IO.Path.GetDirectoryName(PendingMapScriptPath);
						while (!string.IsNullOrEmpty(parentDir))
						{
							string folderName = System.IO.Path.GetFileName(parentDir);
							if (!string.IsNullOrEmpty(folderName) && 
								!folderName.Equals("bin", StringComparison.OrdinalIgnoreCase) && 
								!folderName.Equals("Release", StringComparison.OrdinalIgnoreCase) && 
								!folderName.Equals("net10.0", StringComparison.OrdinalIgnoreCase) && 
								!folderName.Equals("wasi-wasm", StringComparison.OrdinalIgnoreCase) && 
								!folderName.Equals("publish", StringComparison.OrdinalIgnoreCase))
							{
								mapNameOnly = folderName;
								break;
							}
							parentDir = System.IO.Path.GetDirectoryName(parentDir);
						}
					}
					_activeMapScript = new Realm.Godot.WasmRuntime(PendingMapScriptPath, mapNameOnly);
				}
				else
				{
					_mapScriptLoadContext = new MapScriptLoadContext();
					using var fs = new System.IO.FileStream(PendingMapScriptPath, System.IO.FileMode.Open, System.IO.FileAccess.Read);
					var asm = _mapScriptLoadContext.LoadFromStream(fs);

					foreach (var t in asm.GetExportedTypes())
					{
						if (typeof(IMapScript).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract && !typeof(IWasmRuntime).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null)
						{
							_activeMapScript = (IMapScript?)Activator.CreateInstance(t);
							if (_activeMapScript != null)
							{
								break;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[{DateTime.Now:HH:mm:ss}] GameHost LoadMapScript failed: {ex}");
				GD.PrintErr($"Failed to load pending map script from {PendingMapScriptPath}: {ex.Message}");
			}
			PendingMapScriptPath = null;
		}
		else
		{
			string searchName = mapName?.Replace("_", "").ToLower() ?? string.Empty;
			if (!string.IsNullOrEmpty(searchName))
			{
				foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
				{
					Type?[] types;
					try
					{
						types = assembly.GetTypes();
					}
					catch (System.Reflection.ReflectionTypeLoadException ex)
					{
						types = ex.Types;
					}
					catch (Exception)
					{
						continue;
					}
					if (types == null) continue;
					foreach (var type in types)
					{
						if (type == null) continue;
						if (typeof(IMapScript).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract && !typeof(IWasmRuntime).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) != null)
						{
							string typeName = type.Name.ToLower();
							if (typeName.Contains(searchName) || searchName.Contains(typeName))
							{
								try
								{
									_activeMapScript = (IMapScript)Activator.CreateInstance(type);
									break;
								}
								catch (Exception ex)
								{
									GD.PrintErr($"Failed to instantiate map script type {type.FullName}: {ex.Message}");
								}
							}
						}
					}
					if (_activeMapScript != null) break;
				}
			}
		}

		if (_activeMapScript == null)
		{
			_activeMapScript = new EmptyMapScript();
		}
	}

	private List<Unit3D>[] _controlGroups = new List<Unit3D>[10];
	public List<Unit3D>[] ControlGroups => _controlGroups;
	private double[] _lastGroupPressTime = new double[10];


	private bool _isDragging;
	private Vector2 _dragStart;
	private Vector2 _dragEnd;
	private const float DragThreshold = 8f;

	public override void _Ready()
	{
		MainNode = GetTree().Root.GetNodeOrNull("Main");
		MainCamera = GetTree().Root.GetNodeOrNull<Camera3D>("Main/Camera3D");

		GD.Print($"[GAMEHOST_READY] GameHost _Ready starting");
		Instance = this;
		GameSettings.ApplyGraphicsSettings(this);
		ReinitializeEcsAndServices();
	}

	private void InitializeServices()
	{
		// Services are initialized and resolved in DI, no-op or just configuration
	}

	private void InitializeGameEcs()
	{
		if (System.OperatingSystem.IsWindows())
		{
			VSCodeManager.Instance.StartInstallIfNeeded();
		}

		if (Multiplayer.MultiplayerPeer == null || Multiplayer.IsServer())
		{
			_trackerTickDurations = new List<float>(100000);
			_trackerApiDurations = new List<float>(10000);
			_trackerIntervalStopwatch.Start();
		}

		EcsWorld = ServiceLocator.Get<World>();
		CreateGround();
		if (GetNodeOrNull<PropMultiMeshManager>("PropMultiMeshManager") == null)
		{
			var propMultiMeshManager = new PropMultiMeshManager();
			AddChild(propMultiMeshManager);
		}
		SetupWorldEntityComponents();

		if (GroundTerrain != null)
			_editorService.SetTerrainSplatMap(GroundTerrain.SplatMap);

		SetupSkybox();

		UpdateDayNightVisuals(0.0f);
		_definitionManager = ServiceLocator.Get<DefinitionManager>();
		_goldResourceId = "gold".AsResourceId(_definitionManager);
		_woodResourceId = "wood".AsResourceId(_definitionManager);
		_stoneResourceId = "stone".AsResourceId(_definitionManager);

		_simulationService = ServiceLocator.Get<SimulationService>();
		_simulationService.SetRuntimeReferences(AllUnits, AllProps, _castlesList, _definitionManager, _goldResourceId, _woodResourceId, _stoneResourceId, GroundTerrain);
		_simulationService.Initialize();
		_simulationService.EditorHeightProvider = p => _editorService.GetTerrainHeightAt(new Vector3(p.X, p.Y, p.Z));

		_simulationService.OnArrowProjectileRequested = (start, target) => SpawnArrowProjectile(new Vector3(start.X, start.Y, start.Z), new Vector3(target.X, target.Y, target.Z));
		_simulationService.OnWeaponProjectileRequested = (start, target, weaponId, targetEnt) => SpawnWeaponProjectile(new Vector3(start.X, start.Y, start.Z), new Vector3(target.X, target.Y, target.Z), weaponId, targetEnt);
		_simulationService.OnDamageFlashRequested = entity =>
		{
			if (GameHost.TryGetUnit3D(entity, out var unit3D))
			{
				this.CallDeferred(nameof(FlashDamageUnit), unit3D);
			}
		};
		_simulationService.OnHealEffectRequested = (start, target) => SpawnHealVisualEffect(new Vector3(start.X, start.Y, start.Z), new Vector3(target.X, target.Y, target.Z));
		_simulationService.OnHealFlashRequested = entity =>
		{
			if (GameHost.TryGetUnit3D(entity, out var unit3D))
			{
				this.CallDeferred(nameof(FlashHealUnit), unit3D);
			}
		};
		_simulationService.OnKillUnitRequested = entity =>
		{
			_warnedNonFinitePositions.Remove(entity);
			if (EcsWorld.IsAlive(entity) && GameHost.TryGetUnit3D(entity, out var unit3D))
			{
				this.CallDeferred(nameof(KillUnit), unit3D);
			}
		};
		_simulationService.OnPropDepleted = entity =>
		{
			if (TryGetProp3D(entity, out var prop3D))
			{
				this.CallDeferred(nameof(DepleteProp), prop3D);
			}
		};
		_simulationService.OnUnitDamagedCallback = (targetEntity, attackerEntity, damage) =>
		{
			if (EcsWorld.IsAlive(targetEntity))
			{
				IUnit attackerWrapper = EcsWorld.IsAlive(attackerEntity)
					? GetUnitWrapper(attackerEntity)
					: null;
				OnUnitDamaged?.Invoke(GetUnitWrapper(targetEntity), attackerWrapper, damage);
				if (GameHost.TryGetUnit3D(targetEntity, out var targetUnit3D))
				{
					_fxService.SpawnDamageNumber(this, targetUnit3D.GlobalPosition, damage);
					_audioService?.PlayUnitSound(targetUnit3D.UnitId, UnitSoundEvent.Wounded, targetUnit3D.GlobalPosition);
				}
			}
		};
		_simulationService.OnUnitHealedCallback = (targetEntity, healerEntity, healAmount) =>
		{
			if (EcsWorld.IsAlive(targetEntity))
			{
				if (GameHost.TryGetUnit3D(targetEntity, out var targetUnit3D))
				{
					_fxService.SpawnHealNumber(this, targetUnit3D.GlobalPosition, healAmount);
				}
			}
		};
		_simulationService.OnUnitAttackedCallback = (attackerEntity, targetEntity) =>
		{
			if (EcsWorld.IsAlive(attackerEntity) && EcsWorld.IsAlive(targetEntity))
			{
				if (GameHost.TryGetUnit3D(attackerEntity, out var attackerUnit3D))
				{
					attackerUnit3D.PlayAnimation("Attack");
				}
				OnUnitAttacked?.Invoke(GetUnitWrapper(attackerEntity), GetUnitWrapper(targetEntity));
			}
		};
		_simulationService.OnUnderAttackAlertRequested = unitId =>
		{
			string alertMsg = unitId == "castle"
				? "⚠️ YOUR CASTLE IS UNDER ATTACK!"
				: $"⚠️ {unitId.ToUpper()} is under attack!";
			InGameHUD.Instance?.CallDeferred(nameof(InGameHUD.ShowFeedbackText), alertMsg, new Color(1.0f, 0.2f, 0.1f));
			UIManager.Instance?.CallDeferred(nameof(UIManager.PlayWarningSound));
		};
		_simulationService.OnSpawnUnitFromProductionRequested = (unitId, position, isEnemy, buildingEntity, isFromQueue) =>
			SpawnUnitFromProduction(unitId, position, isEnemy, buildingEntity, isFromQueue);
		_simulationService.GetProductionBuildTime = unitId =>
			UnitRegistry.TryGetValue(unitId, out var meta) ? meta.ProductionTime : 5f;
		_simulationService.OnClearUnitOrdersRequested = entity => ClearUnitOrders(entity);
		_simulationService.OnStopGatheringMovementRequested = entity => StopGatheringMovement(entity);
		_simulationService.OnUiRefreshRequested = () => InGameHUD.Instance?.RefreshUI(SelectedUnits);
		_simulationService.OnResourceDepositedForPlayer = (resType, carry) =>
		{
			string resTypeUpper = resType.ToUpper();
			InGameHUD.Instance?.CallDeferred(nameof(InGameHUD.ShowFeedbackText), $"+{carry:F0} {resTypeUpper} deposited", new Color(0.2f, 0.9f, 0.4f));
		};
		_simulationService.OnProductionCompleted = unitToSpawn =>
		{
			string displayName = UnitRegistry.TryGetValue(unitToSpawn, out var nm) ? nm.Name : unitToSpawn.ToUpper();
			InGameHUD.Instance?.CallDeferred(nameof(InGameHUD.ShowFeedbackText), $"✓ {displayName} training complete!", new Color(0.3f, 0.9f, 0.4f));
		};

		if (_multiplayerActive)
		{
			_localPeerId = Multiplayer.GetUniqueId();
			if (!Multiplayer.IsServer())
			{
				_networkService.MarkClientEnteredMultiplayer();
			}
			if (Multiplayer is SceneMultiplayer sceneMultiplayer)
			{
				sceneMultiplayer.ServerRelay = false;
			}

			if (LobbyManager.Instance != null && LobbyManager.Instance.PlayerList.Count > 0)
			{
				int pIdx = 0;
				foreach (var p in LobbyManager.Instance.PlayerList)
				{
					var playerEntity = EcsWorld.Create();
					EcsWorld.Add(playerEntity, new Player());
					EcsWorld.Add(playerEntity, new Name(p.Name));
					InitializePlayerResources(playerEntity);
					SetupPlayerEntityComponents(playerEntity);
					_peerIdToPlayerEntityMap[p.PeerId] = playerEntity;
					if (p.PeerId == _localPeerId)
					{
						_playerEntity = playerEntity;
					}
					else
					{
						_enemyPlayerEntity = playerEntity;
					}
					if (EcsWorld.Has<ScriptPlayersState>(_worldEntity))
					{
						var players = EcsWorld.Get<ScriptPlayersState>(_worldEntity).Players;
						if (pIdx < players.Length)
						{
							players[pIdx].Name = p.Name;
							players[pIdx].Active = true;
						}
					}
					pIdx++;
				}
			}
			else
			{
				_playerEntity = EcsWorld.Create();
				EcsWorld.Add(_playerEntity, new Player());
				EcsWorld.Add(_playerEntity, new Name("Horaid_Topa"));
				InitializePlayerResources(_playerEntity);
				SetupPlayerEntityComponents(_playerEntity);
				_peerIdToPlayerEntityMap[1] = _playerEntity;

				_enemyPlayerEntity = EcsWorld.Create();
				EcsWorld.Add(_enemyPlayerEntity, new Player());
				EcsWorld.Add(_enemyPlayerEntity, new Name("Enemy_AI"));
				InitializePlayerResources(_enemyPlayerEntity);
				SetupPlayerEntityComponents(_enemyPlayerEntity);
				_peerIdToPlayerEntityMap[-1] = _enemyPlayerEntity;

				if (EcsWorld.Has<ScriptPlayersState>(_worldEntity))
				{
					var players = EcsWorld.Get<ScriptPlayersState>(_worldEntity).Players;
					players[0].Name = "Horaid_Topa";
					players[0].Active = true;
					players[1].Name = "Enemy_AI";
					players[1].Active = true;
				}
			}
		}
		else
		{
			_playerEntity = EcsWorld.Create();
			EcsWorld.Add(_playerEntity, new Player());
			EcsWorld.Add(_playerEntity, new Name("Horaid_Topa"));
			InitializePlayerResources(_playerEntity);
			SetupPlayerEntityComponents(_playerEntity);
			_peerIdToPlayerEntityMap[1] = _playerEntity;

			_enemyPlayerEntity = EcsWorld.Create();
			EcsWorld.Add(_enemyPlayerEntity, new Player());
			EcsWorld.Add(_enemyPlayerEntity, new Name("Enemy_AI"));
			InitializePlayerResources(_enemyPlayerEntity);
			SetupPlayerEntityComponents(_enemyPlayerEntity);
			_peerIdToPlayerEntityMap[-1] = _enemyPlayerEntity;

			if (EcsWorld.Has<ScriptPlayersState>(_worldEntity))
			{
				var players = EcsWorld.Get<ScriptPlayersState>(_worldEntity).Players;
				players[0].Name = "Horaid_Topa";
				players[0].Active = true;
				players[1].Name = "Enemy_AI";
				players[1].Active = true;
			}
		}

		if (_playerEntity == Entity.Null)
		{
			_playerEntity = EcsWorld.Create();
			EcsWorld.Add(_playerEntity, new Player());
			EcsWorld.Add(_playerEntity, new Name("Horaid_Topa"));
			InitializePlayerResources(_playerEntity);
			SetupPlayerEntityComponents(_playerEntity);
			_peerIdToPlayerEntityMap[1] = _playerEntity;
			if (EcsWorld.Has<ScriptPlayersState>(_worldEntity))
			{
				var players = EcsWorld.Get<ScriptPlayersState>(_worldEntity).Players;
				if (players.Length > 0)
				{
					players[0].Name = "Horaid_Topa";
					players[0].Active = true;
				}
			}
		}

		if (_enemyPlayerEntity == Entity.Null)
		{
			_enemyPlayerEntity = EcsWorld.Create();
			EcsWorld.Add(_enemyPlayerEntity, new Player());
			EcsWorld.Add(_enemyPlayerEntity, new Name("Enemy_AI"));
			InitializePlayerResources(_enemyPlayerEntity);
			SetupPlayerEntityComponents(_enemyPlayerEntity);
			_peerIdToPlayerEntityMap[-1] = _enemyPlayerEntity;
			if (EcsWorld.Has<ScriptPlayersState>(_worldEntity))
			{
				var players = EcsWorld.Get<ScriptPlayersState>(_worldEntity).Players;
				if (players.Length > 1)
				{
					players[1].Name = "Enemy_AI";
					players[1].Active = true;
				}
			}
		}


		string rawMapName = LobbyManager.Instance?.ActiveMapName ?? "";
		string mapParamName = rawMapName;
		if (!string.IsNullOrEmpty(rawMapName))
		{
			LoadUnitMetadata(rawMapName);
		}

		bool isGameStarted = LobbyManager.Instance != null && LobbyManager.Instance.IsGameStarted;
		if (isGameStarted || IsMapEditorMode)
		{
			if (!IsMapEditorMode && !IsLoadingMap)
			{
				string customTerrainPath = "";
				string? resolvedDir = ResolveMapDirectory(rawMapName);
				if (!string.IsNullOrEmpty(resolvedDir))
				{
					CurrentMapDirectory = resolvedDir;
					string checkTerrain = System.IO.Path.Combine(resolvedDir, "terrain.json");
					if (System.IO.File.Exists(checkTerrain))
					{
						customTerrainPath = checkTerrain;
					}
				}
				else
				{
					string normalizedRawMapName = rawMapName.Replace('\\', '/');
					if (normalizedRawMapName.StartsWith("user://") || normalizedRawMapName.StartsWith("res://") || System.IO.Path.IsPathRooted(normalizedRawMapName))
					{
						string checkDir = normalizedRawMapName;
						if (normalizedRawMapName.StartsWith("user://") || normalizedRawMapName.StartsWith("res://"))
						{
							checkDir = ProjectSettings.GlobalizePath(normalizedRawMapName);
						}
						if (System.IO.Directory.Exists(checkDir))
						{
							customTerrainPath = System.IO.Path.Combine(checkDir, "terrain.json");
						}
					}

					if (string.IsNullOrEmpty(customTerrainPath))
					{
						string normalizedMapName = rawMapName.ToLower().Trim();
						string mapDir = $"res://Maps/{normalizedMapName}";
						if (System.IO.Directory.Exists(ProjectSettings.GlobalizePath(mapDir)))
						{
							customTerrainPath = $"res://Maps/{normalizedMapName}/terrain.json";
						}
					}
				}

				string targetPath = customTerrainPath;
				if (!string.IsNullOrEmpty(customTerrainPath) && (customTerrainPath.StartsWith("user://") || customTerrainPath.StartsWith("res://")))
				{
					targetPath = ProjectSettings.GlobalizePath(customTerrainPath);
				}

				if (!string.IsNullOrEmpty(targetPath) && System.IO.File.Exists(targetPath))
				{
					LoadMapFromFile(customTerrainPath, false, true);
				}
			}

			bool shouldRunMapScript = !isGameStarted || IsServerActive();
			if ((shouldRunMapScript || IsMapEditorMode) && !IsLoadingMap)
			{
				LoadMapScript(mapParamName);
				if (_activeMapScript != null && !IsMapEditorMode)
				{
					_activeMapScript.Initialize(this);
				}
				RebakeNavMesh();
			}
		}

		if (isGameStarted && !IsMapEditorMode && !ReplayPlaybackManager.Instance.IsPlayingReplay && GameSettings.RecordReplays)
		{
			string replayDir = ProjectSettings.GlobalizePath("user://replays");
			string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
			string replayPath = System.IO.Path.Combine(replayDir, $"replay_{timestamp}.rep");
			_replayService.StartRecording(
				replayPath, 
				mapParamName, 
				LobbyManager.Instance?.PlayerList
			);
			GD.Print($"[ReplayRecorder] Started recording to {replayPath}");
		}


		if (!_isResettingForReplay)
		{
			var timer = GetTree().CreateTimer(0.1f);
			timer.Timeout += () =>
			{
				if (InGameHUD.Instance != null)
				{
					InGameHUD.Instance.Gold = _goldBackup;
					InGameHUD.Instance.Wood = _woodBackup;
					InGameHUD.Instance.Stone = _stoneBackup;
					InGameHUD.Instance.RefreshUI(SelectedUnits);
				}

				if (ReplayPlaybackManager.Instance.IsPlayingReplay)
				{
					ReplayPlaybackManager.Instance.ApplyInitialFrame();
				}
			};
		}

		_shroudService.Initialize(MainNode);
	}

	public void ResetWorldAndState()
	{
		ReplayPlaybackManager.Instance.StopReplay();
		StopRecording();

		var unitsCopy = new List<Unit3D>(AllUnits);
		foreach (var unit in unitsCopy)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				unit.QueueFree();
			}
		}
		AllUnits.Clear();
		SelectedUnits.Clear();

		var propsCopy = new List<Prop3D>(AllProps);
		foreach (var prop in propsCopy)
		{
			if (GodotObject.IsInstanceValid(prop))
			{
				prop.QueueFree();
			}
		}
		AllProps.Clear();

		var decalsCopy = new List<Decal>(AllDecals);
		foreach (var decal in decalsCopy)
		{
			if (GodotObject.IsInstanceValid(decal))
			{
				decal.QueueFree();
			}
		}
		AllDecals.Clear();

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

		_editorService?.ResetAllState();
		_networkService?.Clear();
		_shroudService?.CleanUp();

		ReinitializeEcsAndServices();
	}

	private void ReinitializeEcsAndServices()
	{
		EntityToUnit3D.Clear();
		EntityToProp3D.Clear();
		_activeMapScript = null;

		EcsWorld?.Dispose();
		BuildDependencyInjection();
		ResolveServices();

		// Do not trigger lazy GroundTerrain creation here: InitializeGameEcs()
		// builds the ground exactly once (CreateGround at GameHost.cs:2751).
		// Triggering the getter here previously built the whole 128x128 mesh/water/navmesh
		// a second time on every _Ready, stalling the main thread at startup.
		if (_groundTerrain != null)
		{
			_editorService.SetTerrainSplatMap(_groundTerrain.SplatMap);
		}

		InitializeGameEcs();
	}

	public void StopRecording()
	{
		_replayService?.StopRecording();
	}

	public override void _ExitTree()
	{
		if ((Multiplayer.MultiplayerPeer == null || Multiplayer.IsServer()) && _trackerTickDurations != null && _trackerTickDurations.Count >= 30)
		{
			var summary = new GameStabilitySummary
			{
				AvgTickMs = HostStabilityTracker.CalculateAverage(_trackerTickDurations),
				MedianTickMs = HostStabilityTracker.CalculateMedian(_trackerTickDurations),
				MaxTickMs = HostStabilityTracker.CalculateMax(_trackerTickDurations),
				AvgApiMs = HostStabilityTracker.CalculateAverage(_trackerApiDurations),
				MedianApiMs = HostStabilityTracker.CalculateMedian(_trackerApiDurations),
				MaxApiMs = HostStabilityTracker.CalculateMax(_trackerApiDurations)
			};
			HostStabilityTracker.AddGameSummary(summary);
		}

		if (Instance == this) Instance = null;
		EntityToUnit3D.Clear();
		EntityToProp3D.Clear();
		_activeMapScript = null;

		EcsWorld?.Dispose();
		_networkService?.Clear();
		_shroudService?.CleanUp();
		StopRecording();
	}

	private void CreateGround()
	{
		var toRemove = new List<Node>();
		foreach (var child in GetChildren())
		{
			if (child.Name.ToString().StartsWith("Ground"))
			{
				toRemove.Add(child);
			}
		}
		foreach (var child in toRemove)
		{
			RemoveChild(child);
			child.QueueFree();
		}
		GroundTerrain = null;

		if (IsMapEditorMode || IsLoadingMap)
		{
			var terrainNode = new EditableTerrain();
			terrainNode.Name = "Ground";
			AddChild(terrainNode);
			GroundTerrain = terrainNode;
			return;
		}

		bool isGameStarted = LobbyManager.Instance != null && LobbyManager.Instance.IsGameStarted;
		if (!isGameStarted)
		{
			return;
		}

		string rawMapName = LobbyManager.Instance?.ActiveMapName;
		if (string.IsNullOrEmpty(rawMapName))
		{
			return;
		}

		string terrainPath = "";
		string? resolvedDir = ResolveMapDirectory(rawMapName);
		if (!string.IsNullOrEmpty(resolvedDir))
		{
			terrainPath = System.IO.Path.Combine(resolvedDir, "terrain.json");
		}
		else
		{
			string normalizedRawMapName = rawMapName.Replace('\\', '/');
			if (normalizedRawMapName.StartsWith("user://") || normalizedRawMapName.StartsWith("res://") || System.IO.Path.IsPathRooted(normalizedRawMapName))
			{
				string checkDir = normalizedRawMapName;
				if (normalizedRawMapName.StartsWith("user://") || normalizedRawMapName.StartsWith("res://"))
				{
					checkDir = ProjectSettings.GlobalizePath(normalizedRawMapName);
				}
				if (System.IO.Directory.Exists(checkDir))
				{
					terrainPath = System.IO.Path.Combine(checkDir, "terrain.json");
				}
			}

			if (string.IsNullOrEmpty(terrainPath))
			{
				string normalizedMapName = rawMapName.ToLower().Trim();
				string mapDir = $"res://Maps/{normalizedMapName}";
				string checkDir = ProjectSettings.GlobalizePath(mapDir);
				if (System.IO.Directory.Exists(checkDir))
				{
					terrainPath = $"res://Maps/{normalizedMapName}/terrain.json";
				}
				else
				{
					string userDir = ProjectSettings.GlobalizePath($"user://maps/{normalizedMapName}");
					if (System.IO.Directory.Exists(userDir))
					{
						terrainPath = $"user://maps/{normalizedMapName}/terrain.json";
					}
					else
					{
						terrainPath = $"res://Maps/{normalizedMapName}/terrain.json";
					}
				}
			}
		}

		var activeTerrainNode = new RuntimeTerrain();
		activeTerrainNode.Name = "Ground";
		AddChild(activeTerrainNode);
		GroundTerrain = activeTerrainNode;

		string targetPath = terrainPath;
		if (terrainPath.StartsWith("user://") || terrainPath.StartsWith("res://"))
		{
			targetPath = ProjectSettings.GlobalizePath(terrainPath);
		}

		if (System.IO.File.Exists(targetPath))
		{
			LoadMapFromFile(terrainPath, true, false);
		}
	}

	public void SetSkyboxTexture(string path)
	{
		var worldEnv = GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
		if (worldEnv != null)
		{
			var env = worldEnv.Environment;
			if (env != null)
			{
				string fullPath = path;
				if (!path.StartsWith("res://") && !System.IO.File.Exists(path))
				{
					string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
					fullPath = System.IO.Path.Combine(wsPath, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
					if (!System.IO.File.Exists(fullPath))
					{
						fullPath = System.IO.Path.Combine(wsPath, "Assets", "skyboxes", System.IO.Path.GetFileName(path));
					}
					if (!System.IO.File.Exists(fullPath))
					{
						string rtexName = System.IO.Path.GetFileNameWithoutExtension(path) + ".rtex";
						string rtexCandidate = System.IO.Path.Combine(wsPath, "Assets", "skyboxes", rtexName);
						if (System.IO.File.Exists(rtexCandidate))
						{
							fullPath = rtexCandidate;
						}
					}
				}

				Texture2D? skyTexture = null;
				if (fullPath.StartsWith("res://"))
				{
					if (ResourceLoader.Exists(fullPath))
					{
						skyTexture = GD.Load<Texture2D>(fullPath);
					}
				}
				else if (System.IO.File.Exists(fullPath))
				{
					if (fullPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
					{
						byte[] rtexBytes = System.IO.File.ReadAllBytes(fullPath);
						byte[]? webpBytes = Realm.Shared.Textures.RtexFile.GetLayer(rtexBytes, 0);
						if (webpBytes != null && webpBytes.Length > 0)
						{
							var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
							if (img.LoadWebpFromBuffer(webpBytes) != Error.Ok)
							{
								img.LoadPngFromBuffer(webpBytes);
							}
							skyTexture = ImageTexture.CreateFromImage(img);
						}
					}
					else
					{
						var img = Image.LoadFromFile(fullPath);
						if (img != null)
						{
							skyTexture = ImageTexture.CreateFromImage(img);
						}
					}
				}

				if (skyTexture != null)
				{
					var panoramaMaterial = new PanoramaSkyMaterial();
					panoramaMaterial.Panorama = skyTexture;
					var sky = new Sky();
					sky.SkyMaterial = panoramaMaterial;
					env.Sky = sky;
					env.BackgroundMode = Godot.Environment.BGMode.Sky;
					EditorSkyboxPath = path;
				}
			}
		}
	}

	private void SetupSkybox()
	{
		SetSkyboxTexture(EditorSkyboxPath);
	}
	
	private Prop3D FindProp3DInParentChain(Node node)
	{
		while (node != null)
		{
			if (node is Prop3D prop)
			{
				return prop;
			}
			node = node.GetParent();
		}
		return null;
	}

	public void ClearUnitOrders(Entity entity)
	{
		if (EcsWorld.Has<MoveTo>(entity)) EcsWorld.Remove<MoveTo>(entity);
		if (EcsWorld.Has<PathFollow>(entity)) EcsWorld.Remove<PathFollow>(entity);
		if (EcsWorld.Has<AttackTarget>(entity)) EcsWorld.Remove<AttackTarget>(entity);
		if (EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(entity)) EcsWorld.Remove<Realm.Ecs.Components.Movement.AttackMove>(entity);
		if (EcsWorld.Has<Realm.Ecs.Components.Movement.HoldPosition>(entity)) EcsWorld.Remove<Realm.Ecs.Components.Movement.HoldPosition>(entity);
		if (EcsWorld.Has<Realm.Ecs.Components.Movement.Follow>(entity)) EcsWorld.Remove<Realm.Ecs.Components.Movement.Follow>(entity);
		if (EcsWorld.Has<Patrol>(entity)) EcsWorld.Remove<Patrol>(entity);
		if (EcsWorld.Has<HealingTarget>(entity)) EcsWorld.Remove<HealingTarget>(entity);
		if (EcsWorld.Has<WaypointQueue>(entity)) EcsWorld.Remove<WaypointQueue>(entity);
		if (EcsWorld.Has<Gatherer>(entity)) EcsWorld.Remove<Gatherer>(entity);
		if (EcsWorld.Has<Realm.Ecs.Components.Resources.BuildTask>(entity)) EcsWorld.Remove<Realm.Ecs.Components.Resources.BuildTask>(entity);
		if (EcsWorld.Has<Realm.Ecs.Components.Resources.BuildQueue>(entity)) EcsWorld.Remove<Realm.Ecs.Components.Resources.BuildQueue>(entity);
	}

	private void StopGatheringMovement(Entity entity)
	{
		if (EcsWorld.IsAlive(entity))
		{
			if (EcsWorld.Has<MoveTo>(entity)) EcsWorld.Remove<MoveTo>(entity);
		}
	}

	private Prop3D FindNearbyResourceNode(Vector3 pos, string type, float radius)
	{
		Prop3D closest = null;
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

	public void IssueGatherCommand(Prop3D prop, bool isQueued = false)
	{
		if (SelectedUnits.Count == 0 || prop == null || !GodotObject.IsInstanceValid(prop)) return;

		string resType = prop.PropId switch
		{
			"goldmine" => "gold",
			"tree" => "wood",
			"rock" => "stone",
			_ => null
		};

		if (resType == null) return;

		SpawnTargetIndicator(prop.GlobalPosition, new Color(0.9f, 0.8f, 0.1f));
		InGameHUD.Instance?.ShowFeedbackText($"Gathering {resType.ToUpper()} from {prop.PropId}", new Color(0.9f, 0.8f, 0.1f));

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			var targetIds = new List<int>();
			foreach (var unit in SelectedUnits)
			{
				if (unit.IsBuilding || unit.IsEnemy || unit.UnitId != "worker") continue;
				targetIds.Add(GetServerEntityId(unit.Entity));
			}
			QueueClientCommand(isQueued ? "gather_queued" : "gather", targetIds, prop.GlobalPosition, GetServerEntityId(prop.Entity), "");
			return;
		}

		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy || unit.UnitId != "worker") continue;

			if (isQueued && _inputService != null && _inputService.IsUnitActive(unit.Entity))
			{
				_inputService.EnqueueCommand(unit.Entity, "gather", new System.Numerics.Vector3(prop.GlobalPosition.X, prop.GlobalPosition.Y, prop.GlobalPosition.Z), prop.Entity);
			}
			else
			{
				if (!isQueued)
				{
					ClearUnitOrders(unit.Entity);
				}

				var gatherer = new Gatherer(resType, prop.Entity);
				EcsWorld.SetOrAdd(unit.Entity, gatherer);

				var moveTo = new MoveTo(new System.Numerics.Vector3(prop.GlobalPosition.X, prop.GlobalPosition.Y, prop.GlobalPosition.Z));
				EcsWorld.SetOrAdd(unit.Entity, moveTo);
			}
		}
	}

	private Entity CreateEcsUnit(string id, string name, float hp, float damage, float range, float armor, float speed, Vector3 pos, Realm.Ecs.Common.PlayerEntity owner)
	{
		float scanRadius = 15.0f;
		float attackCooldown = 1.5f;
		bool isHero = false;
		int pathingFlags = 8;
		string[]? targets = null;
		if (UnitRegistry.TryGetValue(id, out var regMeta))
		{
			if (regMeta.ScanRadius > 0) scanRadius = regMeta.ScanRadius;
			if (regMeta.AttackCooldown > 0) attackCooldown = regMeta.AttackCooldown;
			isHero = regMeta.IsHero;
			pathingFlags = GetUnitPathingFlags(regMeta);
			targets = regMeta.Targets;
		}

		var entity = _unitSpawnService.CreateEcsUnitEntity(
			id, name, hp, damage, range, armor, speed, scanRadius, isHero, attackCooldown, pathingFlags, pos, owner,
			_playerEntity, HasShieldsUpgrade, HasWeaponsUpgrade, targets
		);

		OnUnitCreated?.Invoke(GetUnitWrapper(entity));
		return entity;
	}

	private Unit3D SpawnUnit3D(Entity entity, string id, string modelPath, Vector3 pos, bool isBuilding, bool isEnemy, bool isFromQueue = false, int player = -1, bool executeSpawnShader = false)
	{
		int playerIndex = player >= 0 ? player : 0;
		bool actualIsEnemy = player >= 0 ? NetworkService.ArePlayerIndicesEnemies(LocalPlayerIndex, playerIndex) : isEnemy;
		EcsWorld.SetOrAdd(entity, new UnitFaction(actualIsEnemy));

		EcsWorld.SetOrAdd(entity, new UnitOwnerPlayer(playerIndex));

		EcsWorld.SetOrAdd(entity, new DefinitionId(id));

		var unit3D = new Unit3D();
		unit3D.Entity = entity;
		unit3D.UnitId = id;
		unit3D.IsBuilding = isBuilding;
		unit3D.Name = $"{id}_{entity.Id}";
		unit3D.Player = playerIndex;
		unit3D.IsEnemy = actualIsEnemy;

		if (GigachadEnabled && !actualIsEnemy)
		{
			if (EcsWorld.Has<Health>(entity))
			{
				EcsWorld.Set(entity, new Health(9000f, 9000f));
			}
			if (EcsWorld.Has<Attack>(entity))
			{
				var atk = EcsWorld.Get<Attack>(entity);
				EcsWorld.Set(entity, new Attack(9001f, atk.Range, atk.Cooldown, atk.CurrentCooldown));
			}
		}

		if (!isBuilding && !IsMapEditorMode)
		{
			unit3D.CollisionLayer = 1;
			unit3D.CollisionMask = 0;
		}

		AddChild(unit3D);
		unit3D.Position = pos;
		unit3D.LoadModel(modelPath);
		unit3D.UpdatePlayerColorVisual();

		if (isBuilding)
		{
			var spawnOffset = new System.Numerics.Vector3(0f, 0f, 8f);
			EcsWorld.SetOrAdd(entity, new BuildingSpawnOffset(spawnOffset));

			float autoDetectedRadius = GetOrCalculateObstacleRadius(id, unit3D, isBuilding);
			string unitAssetKey = GetModelAssetKey(unit3D);
			float baseRadius = autoDetectedRadius * GetModelCollisionCircleRatio(unitAssetKey);
			EcsWorld.SetOrAdd(entity, new Realm.Ecs.Components.Core.CollisionRadius(baseRadius));

			if (!IsMapEditorMode)
			{
				CarveObstacle(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z), baseRadius);
			}
		}

		if (IsMapEditorMode)
		{
			unit3D.RotationDegrees = new Vector3(0.0f, EditorPlacementRotation, 0.0f);
			unit3D.Scale *= EditorPlacementScale;
		}

		EntityToUnit3D[entity] = unit3D;

		if (!isEnemy && UnitRegistry.TryGetValue(id, out var popMeta))
		{
			if (id == "castle")
			{
				MaxPopulation += 20;
			}
			if (!isFromQueue)
			{
				CurrentPopulation += popMeta.PopCost;
			}
		}

		AllUnits.Add(unit3D);
		if (id == "castle")
		{
			_castlesList.Add(unit3D);
		}

		if (executeSpawnShader)
		{
			string spawnShader = GetModelSpawnShader(unit3D);
			if (string.IsNullOrEmpty(spawnShader))
			{
				spawnShader = GetModelSpawnShader(id);
			}
			if (!string.IsNullOrEmpty(spawnShader))
			{
				SpawnDeathShaderManager.AnimateTransition(unit3D, spawnShader, true);
			}
		}

		return unit3D;
	}

	public override void _Process(double delta)
	{
		if (_shroudService != null)
		{
			float fDelta = (float)delta;
			bool isReplay = Realm.Godot.ReplaySystem.ReplayPlaybackManager.Instance.IsPlayingReplay;
			bool isSpectator = LobbyManager.Instance != null && LobbyManager.Instance.LocalPlayer != null && LobbyManager.Instance.LocalPlayer.Team == "Spectator";
			int spectatorPerspective = InGameHUD.Instance?.LiveSpectatorPerspective ?? -1;
			_shroudService.Tick(fDelta, AllUnits, AllProps, AllDecals, spectatorPerspective, isReplay, isSpectator);
		}

		var worldEnv = MainNode?.GetNodeOrNull<WorldEnvironment>("WorldEnvironment") ?? GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
		_environmentService?.UpdateEnvironmentalFog(MainCamera, worldEnv);
	}

	public override void _PhysicsProcess(double delta)
	{
		bool isGameStarted = LobbyManager.Instance != null && LobbyManager.Instance.IsGameStarted;
		if (!isGameStarted && !IsMapEditorMode)
		{
			return;
		}

		float fDelta = (float)delta;





		if (_wasClientInMultiplayer)
		{
			UpdateConnectionStatus();
		}

		if (ReplayPlaybackManager.Instance.IsPlayingReplay)
		{
			ReplayPlaybackManager.Instance.Update(fDelta);
			UpdateVisualNodesFromEcs(fDelta);
			return;
		}

		if (Multiplayer.MultiplayerPeer == null || Multiplayer.IsServer())
		{
			if (ResumeCountdownSeconds >= 0)
			{
				_resumeCountdownTimer += fDelta;
				if (_resumeCountdownTimer >= 1.0f)
				{
					_resumeCountdownTimer -= 1.0f;
					ResumeCountdownSeconds--;
					if (ResumeCountdownSeconds <= 0)
					{
						ResumeCountdownSeconds = -1;
						IsPaused = false;
						if (Multiplayer.MultiplayerPeer != null && Multiplayer.IsServer())
						{
							Rpc(nameof(BroadcastPauseState), false, -1, true);
						}
						else
						{
							UpdatePauseUI();
						}
					}
					else
					{
						if (Multiplayer.MultiplayerPeer != null && Multiplayer.IsServer())
						{
							Rpc(nameof(BroadcastCountdownState), ResumeCountdownSeconds, _countdownForcedByHost);
						}
						else
						{
							UpdatePauseUI();
						}
					}
				}
			}
		}

		if (IsPaused)
		{
			return;
		}

		if (_multiplayerActive && !IsServerActive())
		{
			UpdateClientTick(fDelta);
			return;
		}

		if (IsMapEditorMode)
		{
			ProcessMapEditorTick(fDelta);
			return;
		}

		ProcessGameplayTick(fDelta);
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

	private static readonly Dictionary<string, float> ObstacleRadiusCache = new();

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

		var shapes = FindChildrenOfType<CollisionShape3D>(node);
		foreach (var shapeNode in shapes)
		{
			if (shapeNode.Shape != null)
			{
				float r = 0.5f;
				if (shapeNode.Shape is BoxShape3D box)
				{
					r = Math.Max(box.Size.X, box.Size.Z) * 0.5f;
				}
				else if (shapeNode.Shape is CylinderShape3D cyl)
				{
					r = cyl.Radius;
				}
				else if (shapeNode.Shape is SphereShape3D sphere)
				{
					r = sphere.Radius;
				}
				else if (shapeNode.Shape is CapsuleShape3D capsule)
				{
					r = capsule.Radius;
				}
				
				r *= Math.Max(shapeNode.Scale.X, shapeNode.Scale.Z);
				if (r > maxRadius) maxRadius = r;
			}
		}

		var meshes = FindChildrenOfType<MeshInstance3D>(node);
		foreach (var meshNode in meshes)
		{
			if (meshNode.Mesh != null)
			{
				var aabb = meshNode.Mesh.GetAabb();
				float r = Math.Max(aabb.Size.X, aabb.Size.Z) * 0.5f;
				r *= Math.Max(meshNode.Scale.X, meshNode.Scale.Z);
				if (r > maxRadius) maxRadius = r;
			}
		}

		return maxRadius;
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

	private static ImageTexture _sharedShadowGradient;

	/// <summary>
	///     Shared radial gradient used by flying-unit drop-shadow decals. One texture is
	///     generated once and reused by every unit to avoid per-unit allocations.
	/// </summary>
	public Texture2D GetSharedShadowGradient()
	{
		if (_sharedShadowGradient != null && GodotObject.IsInstanceValid(_sharedShadowGradient))
		{
			return _sharedShadowGradient;
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
		_sharedShadowGradient = ImageTexture.CreateFromImage(img);
		return _sharedShadowGradient;
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
	}

	private void ProcessGameplayTick(float fDelta)
	{
		if (IsGameOver)
		{
			return;
		}

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

		_fDelta = fDelta;

		_simulationService.TickEcs(fDelta);
		PollVFXQueue();
		TickConstructionSystem(fDelta);
		UpdateVisualNodesFromEcs(fDelta);

		if (EcsWorld != null && EcsWorld.IsAlive(_worldEntity) && EcsWorld.Has<WorldState>(_worldEntity))
		{
			var state = EcsWorld.Get<WorldState>(_worldEntity);
			if (state.DayNightCycleEnabled && !IsMapEditorMode)
			{
				float progress = state.TimeOfDayTimer / TimeOfDayCycleDuration;
				UpdateDayNightVisuals(progress);
			}
		}

		UpdateMinimapPings(fDelta);

		TickScheduledTimers(fDelta);
		TickZoneTriggers();
		if (_activeMapScript != null)
		{
			_activeMapScript.Update(this, fDelta);
		}

		UpdateBuildingPreview();

		if (!ReplayPlaybackManager.Instance.IsPlayingReplay && GameSettings.RecordReplays && _replayService != null && _replayService.IsRecording)
		{
			RecordGameplayTick();
		}

		if (_multiplayerActive && Multiplayer.IsServer())
		{
			UpdateServerSnapshotTick(fDelta);
		}

		if (Multiplayer.MultiplayerPeer == null || Multiplayer.IsServer())
		{
			_trackerTickStopwatch.Stop();
			float tickCpuMs = (float)_trackerTickStopwatch.Elapsed.TotalMilliseconds;
			float tickDelay = 0f;
			if (actualIntervalMs > 33.33f)
			{
				tickDelay = actualIntervalMs - 33.33f;
			}
			float adjustedTickMs = tickCpuMs + tickDelay;
			if (_trackerTickDurations != null)
			{
				_trackerTickDurations.Add(adjustedTickMs);
			}
			_trackerLastTickDelay = tickDelay;
		}
	}

	private void PollVFXQueue()
	{
		if (EcsWorld == null || !EcsWorld.IsAlive(_worldEntity) || !EcsWorld.Has<Realm.Ecs.Components.Core.VFXQueue>(_worldEntity)) return;

		ref var queue = ref EcsWorld.Get<Realm.Ecs.Components.Core.VFXQueue>(_worldEntity);
		if (queue.Requests.Count == 0) return;

		for (int i = 0; i < queue.Requests.Count; i++)
		{
			var req = queue.Requests[i];
			var godotPos = new Vector3(req.Position.X, req.Position.Y, req.Position.Z);
			var godotTarget = new Vector3(req.TargetPosition.X, req.TargetPosition.Y, req.TargetPosition.Z);

			var def = GetAbilityDefinition(req.EffectTypeId);
			if (def != null)
			{
				_fxService.SpawnAbilityEffect(this, def, godotPos);
			}
			else if (req.EffectTypeId == "arrow" || WeaponRegistry.ContainsKey(req.EffectTypeId) || req.EffectTypeId.StartsWith("proj:"))
			{
				string weaponId = req.EffectTypeId.StartsWith("proj:") ? req.EffectTypeId.Substring(5) : req.EffectTypeId;
				Entity targetEnt = Entity.Null;
				if (req.EntityId != -1)
				{
					foreach (var u in AllUnits)
					{
						if (u.Entity.Id == req.EntityId)
						{
							targetEnt = u.Entity;
							break;
						}
					}
				}
				SpawnWeaponProjectile(godotPos, godotTarget, weaponId, targetEnt);
			}
			else if (req.EffectTypeId == "heal")
			{
				SpawnHealVisualEffect(godotPos, godotTarget);
			}
			else if (req.EffectTypeId == "target_indicator")
			{
				SpawnTargetIndicator(godotPos, new Color(req.Scale, 0.3f, 0.1f));
			}
			else if (req.EffectTypeId == "damage_flash" || req.EffectTypeId == "heal_flash")
			{
				if (req.EntityId != -1)
				{
					foreach (var kv in EntityToUnit3D)
					{
						if (kv.Key.Id == req.EntityId)
						{
							if (GodotObject.IsInstanceValid(kv.Value))
							{
								if (req.EffectTypeId == "damage_flash") _fxService.FlashDamageUnit(kv.Value);
								else _fxService.FlashHealUnit(kv.Value);
							}
							break;
						}
					}
				}
			}
		}

		queue.Requests.Clear();
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

	private void FlashDamageUnit(Unit3D unit)
	{
		_fxService.FlashDamageUnit(unit);
	}

	private void SpawnHealVisualEffect(Vector3 start, Vector3 target)
	{
		_fxService.SpawnHealVisualEffect(this, start, target);
	}

	private void FlashHealUnit(Unit3D unit)
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

	void IGameAPI.PlayWarningSound()
	{
		_audioService.PlayWarningSound();
	}

	void IGameAPI.PlayClickSound()
	{
		_audioService.PlayClickSound();
	}

	private class EmptyMapScript : Realm.MapAPI.IMapScript
	{
		public void Initialize(Realm.MapAPI.IGameAPI api) {}
		public void Update(Realm.MapAPI.IGameAPI api, float delta) {}
	}
}
