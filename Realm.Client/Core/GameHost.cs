using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Realm.MapAPI;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Environment = Godot.Environment;
using FileAccess = System.IO.FileAccess;

namespace Realm.Client.Core;

public partial class GameHost : Node3D, IGameAPI
{
	public GameHost()
	{
		Instance = this;
	}

	public CameraControl MainCamera { get; set; }
	public Node MainNode { get; set; }

	public static GameHost Instance { get; set; }
	public int LocalPlayerIndex => GameContext.PerspectiveFilter.ControlledPlayerIndex;

		public (int minX, int minZ, int maxX, int maxZ) GetClampedCoordinateBounds(EditorCoordinate coord)
		{
			if (GroundTerrain == null) return (0, 0, 0, 0);
			int width = GroundTerrain.Width;
			int depth = GroundTerrain.Depth;
			float quadSize = GroundTerrain.QuadSize;

			int minX = Mathf.Clamp((int)Mathf.Round(coord.MinX / quadSize + width / 2.0f), 0, width);
			int minZ = Mathf.Clamp((int)Mathf.Round(coord.MinZ / quadSize + depth / 2.0f), 0, depth);
			int maxX = Mathf.Clamp((int)Mathf.Round(coord.MaxX / quadSize + width / 2.0f), 0, width);
			int maxZ = Mathf.Clamp((int)Mathf.Round(coord.MaxZ / quadSize + depth / 2.0f), 0, depth);

			return (minX, minZ, maxX, maxZ);
		}

	private float _fDelta { get; set; }
	private WorldEnvironment? _cachedWorldEnvironment;


	private bool _multiplayerActive => Multiplayer.MultiplayerPeer != null && Multiplayer.MultiplayerPeer is not OfflineMultiplayerPeer;

	private int _localPeerId
	{
		get => GameContext.NetworkService.LocalPeerId;
		set
		{
			if (GameContext.NetworkService != null) GameContext.NetworkService.LocalPeerId = value;
		}
	}

	public int LocalPeerId => _localPeerId;

	private const string SpectatorTeamName = "Spectator";

	private void ConfigurePerspective()
	{
		var filter = GameContext.PerspectiveFilter;

		if (ReplayPlaybackManager.Instance.IsPlayingReplay)
		{
			filter.ConfigureSpectator();
			return;
		}

		var lobbyPlayers = LobbyManager.Instance?.PlayerList;
		if (lobbyPlayers == null || lobbyPlayers.Count == 0)
		{
			filter.ConfigureParticipant(1);
			return;
		}

		int peerId = _localPeerId;
		var entry = _multiplayerActive
			? lobbyPlayers.Find(x => x.PeerId == peerId)
			: lobbyPlayers.Find(x => x.PeerId >= 0 && x.Team != SpectatorTeamName);

		if (entry == null)
		{
			if (_multiplayerActive)
				throw new InvalidOperationException($"Local peer {peerId} is not present in the lobby player list.");

			filter.ConfigureParticipant(1);
			return;
		}

		if (entry.Team == SpectatorTeamName) filter.ConfigureSpectator();
		else filter.ConfigureParticipant(entry.Slot);
	}


	private int _nextCommandId
	{
		get => GameContext.World?.GetFieldOrDefault<NetworkState, int>(ECSContext.WorldEntity, s => s.NextCommandId, 1) ?? 1;
		set => GameContext.World?.Mutate(ECSContext.WorldEntity, (ref NetworkState s) => s.NextCommandId = value);
	}

	private float _commandSendTimer
	{
		get => GameContext.NetworkService?.CommandSendTimer ?? 0f;
		set
		{
			if (GameContext.NetworkService != null) GameContext.NetworkService.CommandSendTimer = value;
		}
	}

	private int _snapshotSequence
	{
		get => GameContext.World?.GetFieldOrDefault<NetworkState, int>(ECSContext.WorldEntity, s => s.SnapshotSequence) ?? 0;
		set => GameContext.World?.Mutate(ECSContext.WorldEntity, (ref NetworkState s) => s.SnapshotSequence = value);
	}

	private int _lastReceivedBaselineSeq
	{
		get => GameContext.World?.GetFieldOrDefault<NetworkState, int>(ECSContext.WorldEntity, s => s.LastReceivedBaselineSeq, -1) ?? -1;
		set => GameContext.World?.Mutate(ECSContext.WorldEntity, (ref NetworkState s) => s.LastReceivedBaselineSeq = value);
	}

	private bool _hasReceivedInitialBaseline
	{
		get => GameContext.World?.GetFieldOrDefault<NetworkState, bool>(ECSContext.WorldEntity, s => s.HasReceivedInitialBaseline) ?? false;
		set => GameContext.World?.Mutate(ECSContext.WorldEntity, (ref NetworkState s) => s.HasReceivedInitialBaseline = value);
	}

	private int _lastAppliedSnapshotSequence
	{
		get => GameContext.World?.GetFieldOrDefault<NetworkState, int>(ECSContext.WorldEntity, s => s.LastAppliedSnapshotSequence, -1) ?? -1;
		set => GameContext.World?.Mutate(ECSContext.WorldEntity, (ref NetworkState s) => s.LastAppliedSnapshotSequence = value);
	}

	private ulong _lastSnapshotReceivedTime
	{
		get => GameContext.NetworkService?.LastSnapshotReceivedTime ?? 0;
		set
		{
			if (GameContext.NetworkService != null) GameContext.NetworkService.LastSnapshotReceivedTime = value;
		}
	}

	private bool _wasClientInMultiplayer => GameContext.NetworkService?.WasClientInMultiplayer ?? false;
	public bool IsConnectionLost => GameContext.NetworkService?.IsConnectionLost ?? false;

	private Dictionary<int, Entity> _peerIdToPlayerEntityMap
		=> GameContext.World?.GetFieldOrDefault<NetworkMappingState, Dictionary<int, Entity>>(ECSContext.WorldEntity, s => s.PeerIdToPlayerEntityMap);

	private Dictionary<int, Entity> _serverToClientEntityMap
		=> GameContext.World?.GetFieldOrDefault<NetworkMappingState, Dictionary<int, Entity>>(ECSContext.WorldEntity, s => s.ServerToClientEntityMap);

	private Dictionary<int, int> _clientToServerEntityMap
		=> GameContext.World?.GetFieldOrDefault<NetworkMappingState, Dictionary<int, int>>(ECSContext.WorldEntity, s => s.ClientToServerEntityMap);

	public List<Unit3D> SelectedUnits { get; set; } = new List<Unit3D>();
	private readonly List<Unit3D> _activeImpulseUnits = new();

	public void RegisterActiveImpulseUnit(Unit3D unit)
	{
		if (unit != null && !_activeImpulseUnits.Contains(unit))
		{
			_activeImpulseUnits.Add(unit);
		}
	}

	private void UpdateSelectedAndImpulseUnitVisuals(float fDelta)
	{
		for (int i = 0; i < SelectedUnits.Count; i++)
		{
			var selected = SelectedUnits[i];
			if (GodotObject.IsInstanceValid(selected))
			{
				selected.UpdateSelectedVisuals(fDelta);
			}
		}

		for (int i = _activeImpulseUnits.Count - 1; i >= 0; i--)
		{
			var unit = _activeImpulseUnits[i];
			if (!GodotObject.IsInstanceValid(unit) || !unit.HasActiveImpulse)
			{
				_activeImpulseUnits.RemoveAt(i);
				continue;
			}

			unit.UpdateImpulseVisuals(fDelta);
		}
	}

	public List<Unit3D> AllUnits { get; set; } = new List<Unit3D>();
	public List<Prop3D> AllProps { get; set; } = new List<Prop3D>();
	public List<Decal> AllDecals { get; set; } = new List<Decal>();
	public List<ProceduralVfxInstance3D> AllVfx { get; set; } = new List<ProceduralVfxInstance3D>();
	public List<LootItem3D> AllLootItems { get; set; } = new();
	public static Dictionary<int, LootItem_WasmRuntime> IdToLootItem { get; set; } = new();
	private readonly List<int> _expiredLootToDespawn = new();
	private readonly List<(int LootId, Entity UnitEntity)> _collectedLootToProcess = new();

	public static Dictionary<Entity, Unit3D> EntityToUnit3D { get; set; } = new();
	public static Dictionary<int, Unit3D> EntityIdToUnit3D { get; set; } = new();
	public static Dictionary<int, List<Unit3D>> PlayerToUnits { get; set; } = new();
	public static Dictionary<Entity, Prop3D> EntityToProp3D { get; set; } = new();
	public static Dictionary<Entity, ProceduralVfxInstance3D> EntityToVfx3D { get; set; } = new();

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

	public Entity GetPlayerEntityForPlayerIndex(int playerIndex)
	{
		if (ECSContext.WorldEntity != Entity.Null && GameContext.World != null && GameContext.World.IsAlive(ECSContext.WorldEntity) && GameContext.World.Has<NetworkMappingState>(ECSContext.WorldEntity))
		{
			var playerEntities = GameContext.World.Get<NetworkMappingState>(ECSContext.WorldEntity).PlayerEntities;
			if (playerEntities != null && playerIndex >= 0 && playerIndex < playerEntities.Length)
			{
				var pe = playerEntities[playerIndex];
				if (pe != Entity.Null && GameContext.World.IsAlive(pe))
					return pe;
			}
		}
		return TryGetMultiplayerEntity(playerIndex);
	}

	private Entity TryGetMultiplayerEntity(int playerIndex)
	{
		if (!_multiplayerActive || LobbyManager.Instance == null || LobbyManager.Instance.PlayerList.Count <= 0) return Entity.Null;
		var p = LobbyManager.Instance.PlayerList.Find(x => x.Slot == playerIndex);
		if (p == null || _peerIdToPlayerEntityMap?.TryGetValue(p.PeerId, out var pe) != true || !GameContext.World.IsAlive(pe)) return Entity.Null;
		return pe;
	}


	private int _replayTickCounter
	{
		get => GameContext.World?.GetFieldOrDefault<ReplayState, int>(ECSContext.WorldEntity, s => s.ReplayTickCounter) ?? 0;
		set => GameContext.World?.Mutate<ReplayState>(ECSContext.WorldEntity, (ref ReplayState s) => s.ReplayTickCounter = value);
	}


	public string? ActiveSpellTargeting
	{
		get => GameContext.InputService?.ActiveSpellTargeting;
		set
		{
			if (GameContext.InputService != null) GameContext.InputService.ActiveSpellTargeting = value;
		}
	}

	public string? ActiveCommandTargeting
	{
		get => GameContext.InputService?.ActiveCommandTargeting;
		set
		{
			if (GameContext.InputService != null) GameContext.InputService.ActiveCommandTargeting = value;
		}
	}

	public Prop3D? SelectedProp { get; set; }

	private Node3D? _sharedPropSelectionRing;
	private Node3D? _sharedPropHoverRing;
	public Entity SelectedPropEntity { get; private set; } = Entity.Null;
	public string SelectedPropId { get; private set; } = "";
	public Entity HoveredPropEntity { get; private set; } = Entity.Null;
	public string HoveredPropId { get; private set; } = "";

	public void SetSelectedPropEntity(Entity entity, string propId)
	{
		SelectedPropEntity = entity;
		SelectedPropId = propId;
		if (TryUpdatePropRingState(entity, out var pos, out var scale))
		{
			EnsurePropSelectionRing();
			if (_sharedPropSelectionRing != null)
			{
				_sharedPropSelectionRing.Position = pos;
				_sharedPropSelectionRing.Scale = Vector3.One * scale;
				_sharedPropSelectionRing.Visible = true;
			}
		}
		else
		{
			ClearSelectedPropEntity();
		}
	}

	public void ClearSelectedPropEntity()
	{
		SelectedPropEntity = Entity.Null;
		SelectedPropId = "";
		if (_sharedPropSelectionRing != null)
		{
			_sharedPropSelectionRing.Visible = false;
		}
	}

	public void UpdateSelectedPropRingPosition(Vector3 pos)
	{
		if (_sharedPropSelectionRing != null && _sharedPropSelectionRing.Visible)
		{
			_sharedPropSelectionRing.Position = pos;
		}
	}

	public void SetHoveredPropEntity(Entity entity, string propId)
	{
		HoveredPropEntity = entity;
		HoveredPropId = propId;
		if (TryUpdatePropRingState(entity, out var pos, out var scale))
		{
			EnsurePropHoverRing();
			if (_sharedPropHoverRing != null)
			{
				_sharedPropHoverRing.Position = pos;
				_sharedPropHoverRing.Scale = Vector3.One * scale;
				_sharedPropHoverRing.Visible = true;
			}
		}
		else
		{
			if (_sharedPropHoverRing != null)
			{
				_sharedPropHoverRing.Visible = false;
			}
		}
	}

	private bool TryUpdatePropRingState(Entity entity, out Vector3 pos, out float scale)
	{
		pos = Vector3.Zero;
		scale = 1f;
		if (entity != Entity.Null && GameContext.World != null && GameContext.World.IsAlive(entity) && GameContext.World.Has<Position>(entity))
		{
			var posComp = GameContext.World.Get<Position>(entity);
			pos = new Vector3(posComp.Value.X, posComp.Value.Y, posComp.Value.Z);
			scale = GameContext.World.Has<ModelScale>(entity) ? GameContext.World.Get<ModelScale>(entity).Value : 1f;
			return true;
		}
		return false;
	}

	private void EnsurePropSelectionRing()
	{
		if (_sharedPropSelectionRing != null && GodotObject.IsInstanceValid(_sharedPropSelectionRing)) return;
		_sharedPropSelectionRing = CreateRingNode(new Color(0.15f, 0.65f, 1.0f, 0.8f));
		_sharedPropSelectionRing.Name = "SharedPropSelectionRing";
		AddChild(_sharedPropSelectionRing);
		_sharedPropSelectionRing.Visible = false;
	}

	private void EnsurePropHoverRing()
	{
		if (_sharedPropHoverRing != null && GodotObject.IsInstanceValid(_sharedPropHoverRing)) return;
		_sharedPropHoverRing = CreateRingNode(new Color(1.0f, 0.85f, 0.2f, 0.6f));
		_sharedPropHoverRing.Name = "SharedPropHoverRing";
		AddChild(_sharedPropHoverRing);
		_sharedPropHoverRing.Visible = false;
	}

	private Node3D CreateRingNode(Color color)
	{
		var meshInst = new MeshInstance3D();
		meshInst.Name = "MeshInst_MeshInstance3D";
		var torus = new TorusMesh
		{
			InnerRadius = 0.95f,
			OuterRadius = 1.05f
		};
		meshInst.Mesh = torus;
		var mat = new StandardMaterial3D
		{
			AlbedoColor = color,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			EmissionEnabled = true,
			Emission = color * 0.5f,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
		};
		meshInst.MaterialOverride = mat;
		return meshInst;
	}

	public string? ActiveBuildingPlacementType
	{
		get => GameContext.InputService?.ActiveBuildingPlacementType;
		set
		{
			if (GameContext.InputService != null) GameContext.InputService.ActiveBuildingPlacementType = value;
		}
	}

	public int CycleSelectionIndex
	{
		get => GameContext.InputService?.GetCycleSelectionIndex(SelectedUnits.Count) ?? 0;
		set => GameContext.InputService?.SetCycleSelectionIndex(value, SelectedUnits.Count);
	}

	public bool HasWeaponsUpgrade(int playerIndex)
	{
		var pe = GetPlayerEntityForPlayerIndex(playerIndex);
		return GameContext.World?.GetFieldOrDefault<PlayerUpgrades, bool>(pe, u => u.WeaponsUpgrade) ?? false;
	}

	public void SetWeaponsUpgrade(int playerIndex, bool value)
	{
		var pe = GetPlayerEntityForPlayerIndex(playerIndex);
		GameContext.World?.Mutate(pe, (ref PlayerUpgrades u) => u.WeaponsUpgrade = value);
	}

	public bool HasShieldsUpgrade(int playerIndex)
	{
		var pe = GetPlayerEntityForPlayerIndex(playerIndex);
		return GameContext.World?.GetFieldOrDefault<PlayerUpgrades, bool>(pe, u => u.ShieldsUpgrade) ?? false;
	}

	public void SetShieldsUpgrade(int playerIndex, bool value)
	{
		var pe = GetPlayerEntityForPlayerIndex(playerIndex);
		GameContext.World?.Mutate(pe, (ref PlayerUpgrades u) => u.ShieldsUpgrade = value);
	}

	public bool HasHarvestingUpgrade(int playerIndex)
	{
		var pe = GetPlayerEntityForPlayerIndex(playerIndex);
		return GameContext.World?.GetFieldOrDefault<PlayerUpgrades, bool>(pe, u => u.HarvestingUpgrade) ?? false;
	}

	public void SetHarvestingUpgrade(int playerIndex, bool value)
	{
		var pe = GetPlayerEntityForPlayerIndex(playerIndex);
		GameContext.World?.Mutate(pe, (ref PlayerUpgrades u) => u.HarvestingUpgrade = value);
	}

	public int GetPlayerTechLevel(int playerIndex, string techId)
	{
		if (playerIndex <= 0) throw new ArgumentOutOfRangeException(nameof(playerIndex), playerIndex, "An explicit player index is required.");
		return GameContext.TechTreeService?.GetPlayerTechLevel(playerIndex, techId) ?? 0;
	}

	public bool HasUpgrade(int playerIndex, string techId)
	{
		return GetPlayerTechLevel(playerIndex, techId) > 0;
	}


	private Node3D? _buildingPreviewMesh { get; set; }


	private RuntimeTerrain _groundTerrain { get; set; }

	public RuntimeTerrain GroundTerrain
	{
		get
		{
			if (_groundTerrain == null && (GameContext.SessionState.IsMapEditorMode || GameContext.SaveLoadService.IsLoadingMap || (LobbyManager.Instance != null && LobbyManager.Instance.IsGameStarted)))
			{
				CreateGround();
			}

			return _groundTerrain;
		}
		private set
		{
			_groundTerrain = value;
			if (value != null && GameContext.EditorService != null)
			{
				GameContext.EditorService.SetTerrainSplatMap(value.SplatMap, value.CliffSplatMap);
			}
		}
	}

	public MeshInstance3D? _brushIndicatorMesh { get; set; }
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

	public EditorTool ActiveEditorTool
	{
		get => GameContext.EditorService.ActiveEditorTool;
		set
		{
			FlushTerrainMeshAndPhysics();
			GameContext.EditorService.ActiveEditorTool = value;
			GameContext.EditorService?.SetIsPastingObject(false);
			if (value != EditorTool.SelectArea && value != EditorTool.PasteArea)
			{
				HideSelectionHighlight();
			}

			RebuildAllCoordinatePersistentMeshes();
			UpdatePathingOverlay();
		}
	}

	public string ActivePlaceId { get; set; } = ""; // "soldier", "tree", etc.

	public string GetTerrainStatusString(Vector3 hitPos)
	{
		return GameContext.EditorService.GetTerrainStatusString(hitPos, ActiveEditorTool.ToString(), ActivePlaceId);
	}

	public void LoadMapProperties(string path)
	{
		ApplyTestRandomSeedIfNeeded(path);
		GameContext.MapPropertiesLoader?.LoadMapProperties(ECSContext.WorldEntity, path);
		_defaultCameraBoundsCaptured = false;
		CaptureDefaultCameraBounds();
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
		if (GameContext.MapEditorTerrainImportService == null)
		{
			return false;
		}

		return GameContext.MapEditorTerrainImportService.ImportTerrain(ECSContext.WorldEntity, selectedPath, out smoothedHeights, out splatMap, out treePositions);
	}


	public float EditorBrushRadius
	{
		get => GameContext.EditorService.EditorBrushRadius;
		set => GameContext.EditorService.EditorBrushRadius = Mathf.Clamp(value, EditorService.MIN_BRUSH_RADIUS, EditorService.MAX_BRUSH_RADIUS);
	}

	public float EditorBrushStrength
	{
		get => GameContext.EditorService.EditorBrushStrength;
		set => GameContext.EditorService.EditorBrushStrength = Mathf.Clamp(value, EditorService.MIN_BRUSH_STRENGTH, EditorService.MAX_BRUSH_STRENGTH);
	}

	public bool EditorSnapToGrid { get; set; } = false;


	public float EditorPlacementScale
	{
		get => GameContext.EditorService.EditorPlacementScale;
		set => GameContext.EditorService.EditorPlacementScale = Mathf.Clamp(value, EditorService.MIN_PLACEMENT_SCALE, EditorService.MAX_PLACEMENT_SCALE);
	}

	public bool EditorGridVisible => GameContext.EditorService.EditorGridMode == GridOverlayMode.Grid || GameContext.EditorService.EditorGridMode == GridOverlayMode.Both;

	public float EditorCameraBoundsLeft
	{
		get => GameContext.EditorService.GetCameraBoundsLeft(ECSContext.WorldEntity);
		set => GameContext.EditorService.SetCameraBoundsLeft(ECSContext.WorldEntity, value);
	}

	public float EditorCameraBoundsRight
	{
		get => GameContext.EditorService.GetCameraBoundsRight(ECSContext.WorldEntity);
		set => GameContext.EditorService.SetCameraBoundsRight(ECSContext.WorldEntity, value);
	}

	public float EditorCameraBoundsTop
	{
		get => GameContext.EditorService.GetCameraBoundsTop(ECSContext.WorldEntity);
		set => GameContext.EditorService.SetCameraBoundsTop(ECSContext.WorldEntity, value);
	}

	public float EditorCameraBoundsBottom
	{
		get => GameContext.EditorService.GetCameraBoundsBottom(ECSContext.WorldEntity);
		set => GameContext.EditorService.SetCameraBoundsBottom(ECSContext.WorldEntity, value);
	}

	public MirrorMode EditorMirrorMode
	{
		get => GameContext.World?.GetFieldOrDefault<EditorState, MirrorMode>(ECSContext.WorldEntity, s => s.MirrorMode, MirrorMode.None) ?? MirrorMode.None;
		set
		{
			GameContext.World?.Mutate<EditorState>(ECSContext.WorldEntity, (ref EditorState s) => s.MirrorMode = value);
			if (value == MirrorMode.Rotational)
			{
				GameContext.EditorService.EditorPolarRadialStep = 360.0f / Mathf.Max(1, EditorSymmetryFolds);
				GroundTerrain?.SetPolarRadialStep(GameContext.EditorService.EditorPolarRadialStep);
			}
			else
			{
				GameContext.EditorService.EditorPolarRadialStep = 360.0f / Mathf.Max(1, GameContext.EditorService.EditorPolarSpokeFolds);
				GroundTerrain?.SetPolarRadialStep(GameContext.EditorService.EditorPolarRadialStep);
			}

			UpdateGridOverlayVisibility();
		}
	}

	public Vector2 EditorSymmetryPivot
	{
		get => GameContext.EditorService?.SymmetryPivot ?? Vector2.Zero;
		set
		{
			if (GameContext.EditorService != null) GameContext.EditorService.SymmetryPivot = value;
			UpdateSymmetryPivotVisuals();
		}
	}

	public int EditorSymmetryFolds
	{
		get => GameContext.EditorService?.SymmetryFolds ?? 4;
		set
		{
			if (GameContext.EditorService != null) GameContext.EditorService.SymmetryFolds = value;
			if (EditorMirrorMode == MirrorMode.Rotational)
			{
				GameContext.EditorService.EditorPolarRadialStep = 360.0f / Mathf.Max(1, value);
				GroundTerrain?.SetPolarRadialStep(GameContext.EditorService.EditorPolarRadialStep);
			}
		}
	}


	public Vector3? EditorTapeMeasureEnd { get; set; }
	public bool EditorTapeMeasureActive { get; set; } = false;

	public bool EditorBrushIsSquare { get; set; } = true;


	public float EditorClumpCount
	{
		get => GameContext.EditorService.EditorClumpCount;
		set => GameContext.EditorService.EditorClumpCount = Mathf.Clamp(value, EditorService.MIN_CLUMP_COUNT, EditorService.MAX_CLUMP_COUNT);
	}


	public float EditorClumpScale
	{
		get => GameContext.EditorService.EditorClumpScale;
		set => GameContext.EditorService.EditorClumpScale = Mathf.Clamp(value, EditorService.MIN_CLUMP_SCALE, EditorService.MAX_CLUMP_SCALE);
	}


	public bool EditorClumpMode
	{
		get => GameContext.EditorService.EditorClumpMode;
		set
		{
			GameContext.EditorService.EditorClumpMode = value;
			UpdateBrushMesh();
		}
	}


	public string EditorSkyboxPath
	{
		get => GameContext.EditorService.GetSkyboxPath(ECSContext.WorldEntity);
		set => GameContext.EditorService.SetSkyboxPath(ECSContext.WorldEntity, value);
	}

	public bool EditorHasUnsavedChanges
	{
		get => GameContext.EditorService.GetHasUnsavedChanges(ECSContext.WorldEntity);
		set => GameContext.EditorService.SetHasUnsavedChanges(ECSContext.WorldEntity, value);
	}

	public bool EditorBlockMode
	{
		get => GameContext.EditorService.GetBlockMode(ECSContext.WorldEntity);
		set => GameContext.EditorService.SetBlockMode(ECSContext.WorldEntity, value);
	}

	public float EditorBlockLevelHeight
	{
		get => GameContext.EditorService.GetBlockLevelHeight(ECSContext.WorldEntity);
		set => GameContext.EditorService.SetBlockLevelHeight(ECSContext.WorldEntity, value);
	}


	public float EditorExactHeight
	{
		get => GameContext.EditorService.EditorExactHeight;
		set => GameContext.EditorService.EditorExactHeight = Math.Clamp(value, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
	}

	public WaterType EditorWaterMode
	{
		get => GameContext.EditorService.GetWaterMode(ECSContext.WorldEntity);
		set => GameContext.EditorService.SetWaterMode(ECSContext.WorldEntity, value);
	}

	public byte ActiveWaterProfileIndex
	{
		get => GameContext.EditorService.GetWaterProfileIndex(ECSContext.WorldEntity);
		set => GameContext.EditorService.SetWaterProfileIndex(ECSContext.WorldEntity, value);
	}


	private Node? _hoveredEditorObject { get; set; }
	private MeshInstance3D? _selectionHighlightMesh { get; set; }
	private MeshInstance3D? _coordinatePreviewMesh { get; set; }
	private MeshInstance3D? _coordinateSelectionOutlineMesh { get; set; }
	private List<MeshInstance3D> _coordinatePersistentMeshes { get; set; } = new();


	public void GenerateNewRandomPlacementRotationAndScale()
	{
		GameContext.EditorService.GenerateNewRandomPlacementRotationAndScale();
	}


	public bool PasteOptionPathing
	{
		get => GameContext.EditorService.PasteOptionPathing;
		set
		{
			GameContext.EditorService.PasteOptionPathing = value;
			UpdatePathingOverlay();
		}
	}


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
				else if (_selectedEditorObject is PropEntityProxyNode || _selectedEditorObject is Prop3D)
				{
					ClearSelectedPropEntity();
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
				else if (_selectedEditorObject is PropEntityProxyNode newPropProxy)
				{
					SetSelectedPropEntity(newPropProxy.Entity, newPropProxy.PropId);
				}
				else if (_selectedEditorObject is Prop3D newProp)
				{
					SetSelectedPropEntity(newProp.Entity, newProp.PropId);
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

	private Node? _selectedEditorObject { get; set; }
	private bool _isDraggingObject { get; set; }
	private Vector3 _dragObjectStartPos { get; set; }
	private Vector3 _dragObjectStartRot { get; set; }
	private Vector3 _dragObjectStartScale { get; set; }


	private Node3D _editorPreviewNode { get; set; }

	private string _editorPreviewId { get; set; } = "";
	private bool _editorPreviewIsEnemy { get; set; }

	public bool ActivePingMode
	{
		get => GameContext.InputService?.ActivePingMode ?? false;
		set
		{
			if (GameContext.InputService != null) GameContext.InputService.ActivePingMode = value;
		}
	}


	public static int GetUnitPathingFlags(UnitMetadata meta)
	{
		GameHost tempQualifier = Instance;
		return (tempQualifier != null ? GameContext.UnitSpawnService : null)?.GetUnitPathingFlags(meta) ?? 8;
	}


	public int GetMaxPopulation(int playerIndex)
	{
		return GameContext.ResourceEconomyService?.GetPlayerMaxPopulation(playerIndex) ?? 0;
	}

	public void SetMaxPopulation(int playerIndex, int max)
	{
		GameContext.ResourceEconomyService?.SetPlayerMaxPopulation(playerIndex, max);
	}

	public int GetCurrentPopulation(int playerIndex)
	{
		return GameContext.ResourceEconomyService?.GetPlayerCurrentPopulation(playerIndex) ?? 0;
	}

	public void SetCurrentPopulation(int playerIndex, int current)
	{
		GameContext.ResourceEconomyService?.SetPlayerCurrentPopulation(playerIndex, current);
	}

	public bool CheatsEnabled
		=> GameContext.SessionState.IsMapEditorMode || GameContext.SessionState.IsTestMode || (LobbyManager.Instance?.IsSinglePlayer ?? false);

	public string GetStringText(int stringId) => Realm.MapAPI.StringRegistry.Get(stringId);

	public float GameElapsedTime
	{
		get => GameContext.World?.GetFieldOrDefault<WorldState, float>(ECSContext.WorldEntity, s => s.GameElapsedTime) ?? 0f;
		private set => GameContext.World?.Mutate<WorldState>(ECSContext.WorldEntity, (ref WorldState s) =>
			GameContext.World.Set(ECSContext.WorldEntity, new WorldState(value, s.TimeOfDayIndex, s.TimeOfDayTimer, s.DayNightCycleEnabled)));
	}

	public int TimeOfDayIndex
		=> GameContext.World?.GetFieldOrDefault<WorldState, int>(ECSContext.WorldEntity, s => s.TimeOfDayIndex) ?? 0;

	private float TimeOfDayTimer
	{
		get => GameContext.World?.GetFieldOrDefault<WorldState, float>(ECSContext.WorldEntity, s => s.TimeOfDayTimer) ?? 0f;
		set => GameContext.World?.Mutate<WorldState>(ECSContext.WorldEntity, (ref WorldState s) =>
			GameContext.World.Set(ECSContext.WorldEntity, new WorldState(s.GameElapsedTime, s.TimeOfDayIndex, value, s.DayNightCycleEnabled)));
	}


	public float GetPlayerSpellCooldown(int playerIndex, string abilityId)
	{
		var pe = GetPlayerEntityForPlayerIndex(playerIndex);
		if (GameContext.World == null || pe == Entity.Null || !GameContext.World.IsAlive(pe)) return 0f;
		if (GameContext.World.Has<SpellCooldowns>(pe))
		{
			var spellCooldowns = GameContext.World.Get<SpellCooldowns>(pe);
			if (spellCooldowns.TryGetValue(abilityId, out float remaining)) return remaining;
		}

		return 0f;
	}

	public void SetPlayerSpellCooldown(int playerIndex, string abilityId, float cooldown)
	{
		var pe = GetPlayerEntityForPlayerIndex(playerIndex);
		if (GameContext.World == null || pe == Entity.Null || !GameContext.World.IsAlive(pe)) return;
		if (GameContext.World.Has<SpellCooldowns>(pe))
		{
			ref var spellCooldowns = ref GameContext.World.Get<SpellCooldowns>(pe);
			spellCooldowns.Set(abilityId, cooldown, cooldown);
		}
	}


	public static Dictionary<StringName, UnitMetadata> UnitRegistry => GameContext.RegistryService?.UnitRegistry ?? new();
	public static Dictionary<StringName, UnitMetadata> BuildingRegistry => GameContext.RegistryService?.BuildingRegistry ?? new();
	public static Dictionary<StringName, PropMetadata> PropRegistry => GameContext.RegistryService?.PropRegistry ?? new();
	public static Dictionary<StringName, ResourceMetadata> ResourceRegistry => GameContext.RegistryService?.ResourceRegistry ?? new();
	public static Dictionary<StringName, WeaponMetadata> WeaponRegistry => GameContext.RegistryService?.WeaponRegistry ?? new();
	public static Dictionary<StringName, AttachmentMetadata> AttachmentRegistry => GameContext.RegistryService?.AttachmentRegistry ?? new();
	public static Dictionary<StringName, ItemMetadata> ItemRegistry => GameContext.RegistryService?.ItemRegistry ?? new();
	public static Dictionary<StringName, UpgradeMetadata> UpgradeRegistry => GameContext.RegistryService?.UpgradeRegistry ?? new();

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
		return GameContext.UnitSpawnService.GetFallbackModelPath(unitId, isBuilding);
	}

	private float _goldBackup
	{
		get => GameContext.World?.GetFieldOrDefault<ReplayState, float>(ECSContext.WorldEntity, s => s.GoldBackup, 500f) ?? 500f;
		set => GameContext.World?.Mutate<ReplayState>(ECSContext.WorldEntity, (ref ReplayState s) => s.GoldBackup = value);
	}

	private float _woodBackup
	{
		get => GameContext.World?.GetFieldOrDefault<ReplayState, float>(ECSContext.WorldEntity, s => s.WoodBackup, 400f) ?? 400f;
		set => GameContext.World?.Mutate<ReplayState>(ECSContext.WorldEntity, (ref ReplayState s) => s.WoodBackup = value);
	}

	private float _stoneBackup
	{
		get => GameContext.World?.GetFieldOrDefault<ReplayState, float>(ECSContext.WorldEntity, s => s.StoneBackup, 200f) ?? 200f;
		set => GameContext.World?.Mutate<ReplayState>(ECSContext.WorldEntity, (ref ReplayState s) => s.StoneBackup = value);
	}

	private IMapScript? _activeMapScript
	{
		get => GameContext.MapScriptService?.ActiveMapScript;
		set
		{
			GameContext.MapScriptService?.ActiveMapScript = value;
		}
	}

	public static string? PendingMapScriptPath
	{
		get => GameContext.MapScriptService?.PendingMapScriptPath;
		set
		{
			GameContext.MapScriptService?.PendingMapScriptPath = value;
		}
	}

	private AssemblyLoadContext? _mapScriptLoadContext
	{
		get => GameContext.MapScriptService?.MapScriptLoadContext;
		set
		{
			GameContext.MapScriptService?.MapScriptLoadContext = value;
		}
	}


	private bool DayNightCycleEnabled
	{
		get => GameContext.World?.GetFieldOrDefault<WorldState, bool>(ECSContext.WorldEntity, s => s.DayNightCycleEnabled, true) ?? true;
		set => GameContext.World?.Mutate<WorldState>(ECSContext.WorldEntity, (ref WorldState s) =>
			GameContext.World.Set(ECSContext.WorldEntity, new WorldState(s.GameElapsedTime, s.TimeOfDayIndex, s.TimeOfDayTimer, value)));
	}

	public event Action<UnitId>? OnUnitCreated;
	public event Action<UnitId, UnitId>? OnUnitDied;
	public event Action<UnitId, UnitId, float>? OnUnitDamaged;
	public event Action<UnitId, string, System.Numerics.Vector3>? OnSpellCast;
	public event Action<string, UnitId>? OnPlayerChatMessage;
	public event Action<UnitId>? OnUnitSelected;
	public event Action<UnitId, UnitId>? OnUnitAttacked;
	public event Action<LootItemId, UnitId>? OnLootItemCollected;

	public void TriggerPlayerChatMessage(string message)
	{
		UnitId selected = UnitId.Invalid;
		if (SelectedUnits.Count > 0 && GameContext.World.IsAlive(SelectedUnits[0].Entity))
		{
			selected = (UnitId)SelectedUnits[0].Entity.Id;
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
			{ ECSContext.PlayerResourceService.GoldResourceId, 500 },
			{ ECSContext.PlayerResourceService.WoodResourceId, 400 },
			{ ECSContext.PlayerResourceService.StoneResourceId, 200 }
		};
		GameContext.World.Add(playerEntity, new PlayerResources(resourcesDict));
	}

	private void SetupPlayerEntityComponents(Entity playerEntity)
	{
		GameContext.World.Add(playerEntity, new PlayerPopulation(0, 0));
		GameContext.World.Add(playerEntity, new SpellCooldowns(null));
		GameContext.World.Add(playerEntity, new PlayerUpgrades(false, false, false));
	}

	private void SetupWorldEntityComponents()
	{
		int width = GroundTerrain != null ? GroundTerrain.Width : 128;
		int depth = GroundTerrain != null ? GroundTerrain.Depth : 128;
		float quadSize = GroundTerrain != null ? GroundTerrain.QuadSize : 2.0f;
		float cellSize = GroundTerrain != null ? GroundTerrain.CellSize : TerrainState.DefaultCellSize;
		var cells = GroundTerrain != null ? GroundTerrain.Cells : null;
		int[,] pathingCodes = GroundTerrain != null ? GroundTerrain.PathingCodes : null;
		DtNavMesh navMesh = GroundTerrain != null ? GroundTerrain.NavMesh : null;
		DtNavMeshQuery navMeshQuery = GroundTerrain != null ? GroundTerrain.NavMeshQuery : null;

		ECSContext.WorldEntity = GameContext.WorldInitService.SetupWorldEntityComponents(
			width, depth, quadSize, cellSize,
			cells, pathingCodes, navMesh, navMeshQuery
		);
	}


	private readonly ResourceNodeId[] _cachedResourceNodesBuffer = new ResourceNodeId[4096];
	private int _cachedResourceNodesCount = 0;

	public ReadOnlySpan<ResourceNodeId> GetCachedResourceNodes()
	{
		_cachedResourceNodesCount = 0;
		for (int i = 0; i < AllProps.Count && _cachedResourceNodesCount < _cachedResourceNodesBuffer.Length; i++)
		{
			var prop = AllProps[i];
			if (GodotObject.IsInstanceValid(prop))
			{
				if (prop.PropId == "goldmine" || prop.PropId == "tree" || prop.PropId == "rock")
				{
					_cachedResourceNodesBuffer[_cachedResourceNodesCount++] = (ResourceNodeId)prop.Entity.Id;
				}
			}
		}

		return new ReadOnlySpan<ResourceNodeId>(_cachedResourceNodesBuffer, 0, _cachedResourceNodesCount);
	}

	public bool TryGetResourceProp(ResourceNodeId node, out Prop3D? prop)
	{
		for (int i = 0; i < AllProps.Count; i++)
		{
			var p = AllProps[i];
			if (GodotObject.IsInstanceValid(p) && p.Entity.Id == node.Value)
			{
				prop = p;
				return true;
			}
		}
		prop = null;
		return false;
	}

	public static void EnsureMapProjectFiles(string mapDir)
	{
		string csprojPath = Path.Combine(mapDir, "MapScript.csproj");
		string libDir = Path.Combine(mapDir, "lib");
		Directory.CreateDirectory(libDir);

		string vscodeDir = Path.Combine(mapDir, ".vscode");
		Directory.CreateDirectory(vscodeDir);
		string vscodeSettingsPath = Path.Combine(vscodeDir, "settings.json");

		string projectRoot = PathUtils.GetProjectRoot();
		string templateDir = PathUtils.FindPath("MapTemplate");
		if (!Directory.Exists(templateDir))
			templateDir = Path.Combine(projectRoot, "..", "MapTemplate");

		CopyTemplateFile(PathUtils.FindPath("MapTemplate/.vscode/settings.json"), Path.Combine(templateDir, ".vscode", "settings.json"), vscodeSettingsPath);

		string repoRoot = Path.GetFullPath(Path.Combine(projectRoot, ".."));
		EnsureMapApiDllsCopied(repoRoot, templateDir, libDir);

		CopyTemplateFile(PathUtils.FindPath("MapTemplate/MapScript.csproj"), Path.Combine(templateDir, "MapScript.csproj"), csprojPath);
		CopyTemplateFile(PathUtils.FindPath("MapTemplate/Directory.Build.targets"), Path.Combine(templateDir, "Directory.Build.targets"), Path.Combine(mapDir, "Directory.Build.targets"));

		RestoreDotnetProject(csprojPath, mapDir);
	}

	private static void CopyTemplateFile(string primaryPath, string fallbackPath, string destPath)
	{
		string srcPath = File.Exists(primaryPath) ? primaryPath : fallbackPath;
		if (File.Exists(srcPath))
		{
			File.WriteAllText(destPath, File.ReadAllText(srcPath));
		}
	}

	private static void EnsureMapApiDllsCopied(string repoRoot, string templateDir, string libDir)
	{
		string sourceDll = Path.Combine(repoRoot, "Realm.MapAPI", "bin", "Release", "net10.0", "Realm.MapAPI.dll");
		string sourceXml = Path.Combine(repoRoot, "Realm.MapAPI", "bin", "Release", "net10.0", "Realm.MapAPI.xml");

		if (!File.Exists(sourceDll))
		{
			sourceDll = Path.Combine(repoRoot, "Realm.MapAPI", "bin", "Debug", "net10.0", "Realm.MapAPI.dll");
			sourceXml = Path.Combine(repoRoot, "Realm.MapAPI", "bin", "Debug", "net10.0", "Realm.MapAPI.xml");
		}

		if (TryCopyMapApiDll(sourceDll, sourceXml, libDir)) return;

		string templateLib = Path.Combine(templateDir, "lib");
		if (TryCopyMapApiDll(Path.Combine(templateLib, "Realm.MapAPI.dll"), Path.Combine(templateLib, "Realm.MapAPI.xml"), libDir)) return;

		string mapApiCsproj = Path.Combine(repoRoot, "Realm.MapAPI", "Realm.MapAPI.csproj");
		if (!File.Exists(mapApiCsproj)) return;

		GD.Print("Realm.MapAPI.dll not found. Auto-building Realm.MapAPI...");
		using var buildProcess = new Process();
		buildProcess.StartInfo.FileName = "dotnet";
		buildProcess.StartInfo.Arguments = $"build \"{mapApiCsproj}\" -c Debug";
		buildProcess.StartInfo.CreateNoWindow = true;
		buildProcess.StartInfo.UseShellExecute = false;
		buildProcess.Start();
		buildProcess.WaitForExit();

		TryCopyMapApiDll(Path.Combine(repoRoot, "Realm.MapAPI", "bin", "Debug", "net10.0", "Realm.MapAPI.dll"), Path.Combine(repoRoot, "Realm.MapAPI", "bin", "Debug", "net10.0", "Realm.MapAPI.xml"), libDir);
	}

	private static bool TryCopyMapApiDll(string dllPath, string xmlPath, string libDir)
	{
		if (!File.Exists(dllPath)) return false;

		CopyIfDifferentBytes(dllPath, Path.Combine(libDir, "Realm.MapAPI.dll"));
		if (File.Exists(xmlPath))
		{
			CopyIfDifferentBytes(xmlPath, Path.Combine(libDir, "Realm.MapAPI.xml"));
			CopyIfDifferentBytes(xmlPath, Path.Combine(libDir, "Realm.EditorAPI.xml"));
		}

		string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
		if (File.Exists(pdbPath))
			CopyIfDifferentBytes(pdbPath, Path.Combine(libDir, "Realm.MapAPI.pdb"));
		return true;
	}

	private static void CopyIfDifferentBytes(string src, string dst)
	{
		if (File.Exists(dst))
		{
			var sInfo = new FileInfo(src);
			var dInfo = new FileInfo(dst);
			if (sInfo.Length == dInfo.Length && File.ReadAllBytes(src).AsSpan().SequenceEqual(File.ReadAllBytes(dst)))
				return;
		}

		File.Copy(src, dst, true);
	}

	private static void RestoreDotnetProject(string csprojPath, string mapDir)
	{
		try
		{
			using var restoreProcess = new Process();
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
	private readonly Dictionary<int, StaticTextState> _staticTextStates = new();
	private readonly HashSet<int> _dirtyStaticTextHandles = new();
	private readonly HashSet<int> _destroyedStaticTextHandles = new();
	private int _nextStaticTextHandle { get; set; } = 1;
	private int _staticTextGeneration { get; set; } = 0;

	public void ClearStaticText()
	{
		_staticTextGeneration++;
		foreach (var label in _staticTextLabels.Values)
		{
			if (GodotObject.IsInstanceValid(label))
			{
				label.QueueFree();
			}
		}
		_staticTextLabels.Clear();
		_nextStaticTextHandle = 1;
		SharedShadowGradient = null;
	}

	public static void ClearCache()
	{
		SharedShadowGradient = null;
	}

	public void PanCameraInternal(Vector3 position, float duration)
	{
		Callable.From(() =>
		{
			var camera = MainCamera ?? GetTree().Root.GetNodeOrNull<CameraControl>("Main/Camera3D");
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

	public void ShakeCameraInternal(float intensity, float duration)
	{
		Callable.From(() =>
		{
			var camera = MainCamera ?? GetTree().Root.GetNodeOrNull<CameraControl>("Main/Camera3D");
			if (camera == null) return;

			var startPos = camera.Position;
			var tween = CreateTween();
			if (tween == null) return;
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

	private bool _defaultCameraBoundsCaptured = false;
	public float? DefaultCameraBoundsLeft { get; private set; }
	public float? DefaultCameraBoundsRight { get; private set; }
	public float? DefaultCameraBoundsTop { get; private set; }
	public float? DefaultCameraBoundsBottom { get; private set; }
	private readonly Dictionary<int, (float MinX, float MinZ, float MaxX, float MaxZ)> _playerCameraBoundsOverrides = new();

	public void CaptureDefaultCameraBounds()
	{
		if (_defaultCameraBoundsCaptured) return;
		if (ECSContext.WorldEntity != Entity.Null && GameContext.World != null && GameContext.World.IsAlive(ECSContext.WorldEntity) && GameContext.World.Has<CameraState>(ECSContext.WorldEntity))
		{
			ref var cam = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
			DefaultCameraBoundsLeft = cam.LimitLeft;
			DefaultCameraBoundsRight = cam.LimitRight;
			DefaultCameraBoundsTop = cam.LimitTop;
			DefaultCameraBoundsBottom = cam.LimitBottom;
			_defaultCameraBoundsCaptured = true;
		}
	}

	private void EnsureDefaultCameraBoundsCaptured(CameraControl? camera)
	{
		if (_defaultCameraBoundsCaptured) return;
		CaptureDefaultCameraBounds();
		if (!_defaultCameraBoundsCaptured && camera != null)
		{
			DefaultCameraBoundsLeft = camera.LimitLeft;
			DefaultCameraBoundsRight = camera.LimitRight;
			DefaultCameraBoundsTop = camera.LimitTop;
			DefaultCameraBoundsBottom = camera.LimitBottom;
			_defaultCameraBoundsCaptured = true;
		}
	}

	public void SetCameraBoundsInternal(float minX, float minZ, float maxX, float maxZ)
	{
		Callable.From(() =>
		{
			var camera = MainCamera ?? GetTree().Root.GetNodeOrNull<CameraControl>("Main/Camera3D");
			if (camera != null)
			{
				EnsureDefaultCameraBoundsCaptured(camera);
				camera.LimitLeft = minX;
				camera.LimitRight = maxX;
				camera.LimitTop = minZ;
				camera.LimitBottom = maxZ;
				camera.ClampToValidGameCamera();
			}
		}).CallDeferred();
	}

	public void ResetCameraBoundsInternal()
	{
		Callable.From(() =>
		{
			var camera = MainCamera ?? GetTree().Root.GetNodeOrNull<CameraControl>("Main/Camera3D");
			if (camera != null)
			{
				if (_defaultCameraBoundsCaptured)
				{
					camera.LimitLeft = DefaultCameraBoundsLeft;
					camera.LimitRight = DefaultCameraBoundsRight;
					camera.LimitTop = DefaultCameraBoundsTop;
					camera.LimitBottom = DefaultCameraBoundsBottom;
				}
				camera.ClampToValidGameCamera();
			}
		}).CallDeferred();
	}

	public void RecordPlayerCameraBoundsOverride(int playerIndex, float minX, float minZ, float maxX, float maxZ)
	{
		_playerCameraBoundsOverrides[playerIndex] = (minX, minZ, maxX, maxZ);
	}

	public void ClearPlayerCameraBoundsOverride(int playerIndex)
	{
		_playerCameraBoundsOverrides.Remove(playerIndex);
	}

	public Dictionary<HudElement, bool> LocalHudVisibilityOverrides { get; } = new();
	private readonly Dictionary<int, Dictionary<HudElement, bool>> _playerHudVisibilityOverrides = new();

	public void RecordPlayerHudVisibility(int playerIndex, HudElement element, bool visible)
	{
		if (!_playerHudVisibilityOverrides.TryGetValue(playerIndex, out var dict))
		{
			dict = new Dictionary<HudElement, bool>();
			_playerHudVisibilityOverrides[playerIndex] = dict;
		}
		dict[element] = visible;
	}

	public void SetHudElementVisibleInternal(HudElement element, bool visible)
	{
		LocalHudVisibilityOverrides[element] = visible;
		Callable.From(() =>
		{
			InGameHUD.Instance?.SetHudElementVisible(element, visible);
		}).CallDeferred();
	}

	public void SelectUnitInternal(UnitId unit)
	{
		Callable.From(() =>
		{
			for (int i = 0; i < AllUnits.Count; i++)
			{
				var u = AllUnits[i];
				if (GodotObject.IsInstanceValid(u) && u.Entity.Id == unit.Value && GameContext.World != null && GameContext.World.IsAlive(u.Entity))
				{
					ClearSelection();
					SelectUnit(u);
					InGameHUD.Instance?.RefreshUI(SelectedUnits);
					break;
				}
			}
		}).CallDeferred();
	}

	public void ClearSelectionInternal()
	{
		Callable.From(() =>
		{
			ClearSelection();
			InGameHUD.Instance?.RefreshUI(SelectedUnits);
		}).CallDeferred();
	}


	private void SyncPlayerResourceEcs(int playerIndex, float goldAmount)
	{
		var pe = GetPlayerEntityForPlayerIndex(playerIndex);
		if (pe != Entity.Null && GameContext.World != null && GameContext.World.IsAlive(pe) && GameContext.World.Has<PlayerResources>(pe))
		{
			ref var res = ref GameContext.World.Get<PlayerResources>(pe);
			res[ECSContext.PlayerResourceService.GoldResourceId] = (int)Math.Max(0f, goldAmount);
		}
	}

	public event Action<UnitId, int>? OnUnitEnterZone;
	public event Action<int>? OnPlayerLeft;

	private void TickZoneTriggers()
	{
		if (OnUnitEnterZone == null || GameContext.World == null || !GameContext.World.IsAlive(ECSContext.WorldEntity) || !GameContext.World.Has<ScriptZonesState>(ECSContext.WorldEntity)) return;

		int zoneHandle = GameContext.World.Get<ScriptZonesState>(ECSContext.WorldEntity).Handle;
		int zoneCount = ScriptZonesState.GetZones(zoneHandle).Length;
		if (zoneCount == 0) return;

		if (zoneCount > OccupiedZoneMask.Capacity && !_zoneCapacityWarningIssued)
		{
			_zoneCapacityWarningIssued = true;
			GD.PushWarning($"[GameHost] Map declares {zoneCount} script zones but only the first {OccupiedZoneMask.Capacity} can be tracked for zone enter events.");
		}

		var positionQuery = QueryCache.AllPositionAndDefinitionIdNoneDeadQuery;
		GameContext.World.Query(in positionQuery, (Entity entity, ref Position posComp) =>
		{
			if (!GameContext.World.Has<OccupiedZones>(entity)) GameContext.World.Add(entity, new OccupiedZones());
			ref var occupiedZones = ref GameContext.World.Get<OccupiedZones>(entity);
			CheckUnitZones(entity, new Vector3(posComp.Value.X, posComp.Value.Y, posComp.Value.Z), ref occupiedZones, ScriptZonesState.GetZones(zoneHandle));
		});
	}

	private bool _zoneCapacityWarningIssued;

	private void CheckUnitZones(Entity entity, Vector3 pos, ref OccupiedZones occupiedZones, ReadOnlySpan<ZoneBounds> zones)
	{
		for (int i = 0; i < zones.Length; i++)
		{
			ref readonly ZoneBounds z = ref zones[i];
			bool inside = pos.X >= z.MinX && pos.X <= z.MaxX && pos.Z >= z.MinZ && pos.Z <= z.MaxZ;

			if (inside && occupiedZones.Add(i) && TryGetUnit3D(entity, out _))
				OnUnitEnterZone?.Invoke((UnitId)entity.Id, i);
			else if (!inside)
				occupiedZones.Remove(i);
		}
	}


	private void TickGroundLoot(float delta)
	{
		if (AllLootItems.Count == 0) return;

		_expiredLootToDespawn.Clear();
		_collectedLootToProcess.Clear();

		for (int i = 0; i < AllLootItems.Count; i++)
		{
			var loot = AllLootItems[i];
			if (!GodotObject.IsInstanceValid(loot) || loot.IsDespawned) continue;

			Entity lootEntity = loot.Entity;
			if (!GameContext.World.IsAlive(lootEntity) || !GameContext.World.Has<GroundLoot>(lootEntity)) continue;

			ref var lootState = ref GameContext.World.Get<GroundLoot>(lootEntity);
			if (lootState.Lifetime > 0f)
			{
				lootState.RemainingLifetime -= delta;
				loot.RemainingLifetime = lootState.RemainingLifetime;
				if (lootState.RemainingLifetime <= 0f)
				{
					_expiredLootToDespawn.Add(loot.UniqueId);
					continue;
				}
			}

			if (!lootState.AutoPickup) continue;

			float radiusSq = lootState.PickupRadius * lootState.PickupRadius;
			string itemTypeId = lootState.ItemTypeId;
			int ownerPlayerIndex = lootState.OwnerPlayerIndex;
			var lootPosition = GameContext.World.Has<Position>(lootEntity) ? GameContext.World.Get<Position>(lootEntity).Value : System.Numerics.Vector3.Zero;
			var lootPos = new Godot.Vector3(lootPosition.X, lootPosition.Y, lootPosition.Z);

			Unit3D? bestUnit = null;
			float bestDistSq = float.MaxValue;

			for (int u = 0; u < AllUnits.Count; u++)
			{
				var unit = AllUnits[u];
				if (!GodotObject.IsInstanceValid(unit) || unit.IsBuilding) continue;
				if (ownerPlayerIndex >= 0 && unit.Player != ownerPlayerIndex) continue;
				if (!GameContext.World.IsAlive(unit.Entity) || GameContext.World.Has<Dead>(unit.Entity)) continue;

				if (GameContext.World.Has<Inventory>(unit.Entity))
				{
					ref var inv = ref GameContext.World.Get<Inventory>(unit.Entity);
					if (inv.Count >= 6 && !inv.HasItem(itemTypeId))
						continue;
				}

				float dx = unit.GlobalPosition.X - lootPos.X;
				float dy = unit.GlobalPosition.Y - lootPos.Y;
				float dz = unit.GlobalPosition.Z - lootPos.Z;
				float distSq = dx * dx + dy * dy + dz * dz;

				if (distSq <= radiusSq && distSq < bestDistSq)
				{
					bestDistSq = distSq;
					bestUnit = unit;
				}
			}

			if (bestUnit != null)
			{
				_collectedLootToProcess.Add((loot.UniqueId, bestUnit.Entity));
			}
		}

		for (int i = 0; i < _expiredLootToDespawn.Count; i++)
		{
			RemoveLootItem(_expiredLootToDespawn[i]);
		}

		for (int i = 0; i < _collectedLootToProcess.Count; i++)
		{
			var (lootId, unitEnt) = _collectedLootToProcess[i];
			if (GameContext.World.IsAlive(unitEnt))
			{
				CollectLootItem(lootId, GameContext.SimulationService.GetUnitWrapper(unitEnt));
			}
		}
	}

	public void NotifyPlayerLeft(int playerIndex)
	{
		OnPlayerLeft?.Invoke(playerIndex);
	}

	private void TickScheduledTimers(float delta)
	{
		if (GameContext.TimerService.ScheduledTimers.Count == 0) return;


		Span<int> keysBuffer = stackalloc int[Math.Min(GameContext.TimerService.ScheduledTimers.Count, 64)];
		int keyCount = 0;
		foreach (int k in GameContext.TimerService.ScheduledTimers.Keys)
		{
			if (keyCount < keysBuffer.Length)
				keysBuffer[keyCount++] = k;
		}

		List<int>? toRemove = null;
		for (int i = 0; i < keyCount; i++)
		{
			int handle = keysBuffer[i];
			if (!GameContext.TimerService.ScheduledTimers.TryGetValue(handle, out var entry)) continue;

			float remaining = entry.Remaining - delta;
			if (remaining <= 0f)
			{
				entry.Callback.Invoke();

				if (entry.Repeating)
				{
					GameContext.TimerService.ScheduledTimers[handle] = (entry.Interval, entry.Interval + remaining, true, entry.Callback);
				}
				else
				{
					toRemove ??= new List<int>();
					toRemove.Add(handle);
				}
			}
			else
			{
				GameContext.TimerService.ScheduledTimers[handle] = (entry.Interval, remaining, entry.Repeating, entry.Callback);
			}
		}

		if (toRemove != null)
		{
			foreach (int h in toRemove)
				GameContext.TimerService.ScheduledTimers.Remove(h);
		}
	}

	private static string SafeGlobalizePath(string path)
	{
		if (string.IsNullOrEmpty(path)) return path;
		try
		{
			return ProjectSettings.GlobalizePath(path);
		}
		catch
		{
			return path;
		}
	}

	public static string? ResolveMapDirectory(string mapNameOrPath, string? version = null)
	{
		if (string.IsNullOrWhiteSpace(mapNameOrPath)) return null;

		string norm = mapNameOrPath.Replace('\\', '/').Trim();
		string? explicitPath = TryResolveExplicitPath(norm);
		if (explicitPath != null) return explicitPath;

		string? targetVersion = !string.IsNullOrWhiteSpace(version) ? version : LobbyManager.Instance?.ActiveMapVersion;
		string? manifestPath = MapAssetManager.FindManifestPath(norm, targetVersion) ?? MapAssetManager.FindManifestPath(norm, null);

		if (!string.IsNullOrEmpty(manifestPath) && File.Exists(manifestPath))
			return Path.GetDirectoryName(manifestPath);

		return CheckMapDir(norm) ?? CheckMapDir(norm.ToLowerInvariant());
	}

	private static string? TryResolveExplicitPath(string norm)
	{
		if (!norm.StartsWith("user://") && !norm.StartsWith("res://") && !Path.IsPathRooted(norm)) return null;

		string global = norm.StartsWith("user://") || norm.StartsWith("res://") ? SafeGlobalizePath(norm) : norm;
		if (Directory.Exists(global)) return global;
		if (File.Exists(global)) return Path.GetDirectoryName(global);

		return null;
	}

	private static string? CheckMapDir(string name)
	{
		string resDir = SafeGlobalizePath($"res://Maps/{name}");
		if (Directory.Exists(resDir)) return resDir;

		string userDir = SafeGlobalizePath($"user://maps/{name}");
		if (Directory.Exists(userDir)) return userDir;

		return null;
	}

	public void LoadUnitMetadata(string mapName = null)
	{
		ResetAbilityCatalog();
		mapName = !string.IsNullOrEmpty(mapName) ? mapName : (!string.IsNullOrEmpty(ECSContext.ActiveMapName) ? ECSContext.ActiveMapName : MapWorkspaceService.DefaultWorkspaceFolder);
		ECSContext.ActiveMapName = mapName;
		LocalizationManager.CurrentMapName = mapName;
		LocalizationManager.SetupTranslations();

		string? resolvedDir = ResolveMapDirectory(mapName);
		string path = resolvedDir != null ? Path.Combine(resolvedDir, "metadata.json") : (mapName.StartsWith("user://") || mapName.StartsWith("res://") || Path.IsPathRooted(mapName) ? Path.Combine(mapName, "metadata.json") : $"res://Maps/{mapName}/metadata.json");
		if (resolvedDir != null) GameContext.MapStorageService.CurrentMapDirectory = resolvedDir;

		var metadata = (GameContext.MetadataService ?? GameContext.MetadataService).LoadMetadata(path);

		UnitRegistry.Clear();
		BuildingRegistry.Clear();
		PropRegistry.Clear();
		ResourceRegistry.Clear();
		WeaponRegistry.Clear();
		AttachmentRegistry.Clear();
		ItemRegistry.Clear();
		GameContext.RegistryService.VfxRegistry.Clear();

		if (metadata.Templates != null) LoadMetadataTemplates(metadata.Templates);

		Prop3D.ClearModelPathCache();
	}

	private void LoadMetadataTemplates(TemplateContainer templates)
	{
		LoadWeaponAndAttachmentTemplates(templates);
		LoadItemTemplates(templates);
		LoadUpgradeTemplates(templates);
		LoadUnitTemplates(templates);
		LoadBuildingTemplates(templates);
		LoadResourceAndPropTemplates(templates);

		if (templates.Abilities != null && templates.Abilities.Count > 0) RegisterCustomAbilities(templates.Abilities);
		foreach (var cfg in templates.Vfx)
		{
			if (!string.IsNullOrEmpty(cfg.VfxId))
			{
				GameContext.RegistryService.RegisterVfxConfig(cfg.VfxId, cfg);
			}
		}
	}

	private void LoadWeaponAndAttachmentTemplates(TemplateContainer templates)
	{
		foreach (var meta in templates.Weapons)
		{
			if (!string.IsNullOrEmpty(meta.TemplateID))
			{
				WeaponRegistry[(StringName)meta.TemplateID] = meta;
				if (meta.TemplateID.StartsWith("weapon/", StringComparison.OrdinalIgnoreCase))
				{
					WeaponRegistry[(StringName)meta.TemplateID.Substring(7)] = meta;
				}
			}
		}

		foreach (var meta in templates.Attachments)
		{
			if (!string.IsNullOrEmpty(meta.AttachmentId))
			{
				AttachmentRegistry[(StringName)meta.AttachmentId] = meta;
			}
		}
	}

	private void LoadItemTemplates(TemplateContainer templates)
	{
		foreach (var meta in templates.Items)
		{
			if (string.IsNullOrEmpty(meta.TemplateID)) continue;

			ItemRegistry[(StringName)meta.TemplateID] = meta;
			if (meta.TemplateID.StartsWith("item/", StringComparison.OrdinalIgnoreCase))
			{
				ItemRegistry[(StringName)meta.TemplateID.Substring(5)] = meta;
			}
		}
	}

	private void LoadUpgradeTemplates(TemplateContainer templates)
	{
		if (templates.Upgrades == null) return;
		foreach (var meta in templates.Upgrades)
		{
			if (string.IsNullOrEmpty(meta.TemplateID)) continue;

			UpgradeRegistry[(StringName)meta.TemplateID] = meta;
			if (meta.TemplateID.StartsWith("upgrade/", StringComparison.OrdinalIgnoreCase))
			{
				UpgradeRegistry[(StringName)meta.TemplateID.Substring(8)] = meta;
			}
		}
	}

	private void LoadUnitTemplates(TemplateContainer templates)
	{
		foreach (var meta in templates.Units)
		{
			if (string.IsNullOrEmpty(meta.TemplateID)) continue;
			if (meta.Scale <= 0f) meta.Scale = 1.0f;

			UnitRegistry[(StringName)meta.TemplateID] = meta;
			if (meta.TemplateID.StartsWith("unit/", StringComparison.OrdinalIgnoreCase))
			{
				UnitRegistry[(StringName)meta.TemplateID.Substring(5)] = meta;
			}

			if (!string.IsNullOrEmpty(meta.ModelPath))
			{
				ModelCache.PreloadModel(meta.ModelPath);
			}
		}
	}

	private void LoadBuildingTemplates(TemplateContainer templates)
	{
		foreach (var meta in templates.Buildings)
		{
			if (string.IsNullOrEmpty(meta.TemplateID)) continue;
			if (meta.Scale <= 0f) meta.Scale = 1.5f;

			BuildingRegistry[(StringName)meta.TemplateID] = meta;
			if (meta.TemplateID.StartsWith("building/", StringComparison.OrdinalIgnoreCase))
			{
				BuildingRegistry[(StringName)meta.TemplateID.Substring(9)] = meta;
			}

			if (!string.IsNullOrEmpty(meta.ModelPath))
			{
				ModelCache.PreloadModel(meta.ModelPath);
			}
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
			if (meta.TemplateID.StartsWith("resource/", StringComparison.OrdinalIgnoreCase))
			{
				ResourceRegistry[(StringName)meta.TemplateID.Substring(9)] = meta;
			}
		}

		foreach (var meta in templates.Props)
		{
			if (string.IsNullOrEmpty(meta.TemplateID)) continue;
			if (meta.Scale <= 0f) meta.Scale = 1.25f;
			if (meta.PathingType == 0) meta.PathingType = 255;

			PropRegistry[(StringName)meta.TemplateID] = meta;
			if (meta.TemplateID.StartsWith("prop/", StringComparison.OrdinalIgnoreCase))
			{
				PropRegistry[(StringName)meta.TemplateID.Substring(5)] = meta;
			}
		}
	}

	public void SaveAttachmentDefaultsToMetadata(string fileName, AttachmentMetadata meta)
	{
		try
		{
			string dir = !string.IsNullOrEmpty(GameContext.MapStorageService.CurrentMapDirectory) ? GameContext.MapStorageService.CurrentMapDirectory : ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath);
			var metadata = MapFileService.LoadMetadata(dir);
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

			MapFileService.SaveMetadata(dir, metadata);
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
			string dir = !string.IsNullOrEmpty(GameContext.MapStorageService.CurrentMapDirectory) ? GameContext.MapStorageService.CurrentMapDirectory : ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath);
			var metadata = MapFileService.LoadMetadata(dir);
			var unit = metadata.GetUnit(unitId);
			if (unit != null)
			{
				unit.Animations = animations;
				MapFileService.SaveMetadata(dir, metadata);
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
		ECSContext.IsGameOver = false;

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

		if (string.IsNullOrEmpty(checkDir) || !Directory.Exists(checkDir)) return null;

		var allWasm = Directory.GetFiles(checkDir, "*.wasm", SearchOption.AllDirectories)
			.Where(f => !f.Contains("native") && !f.Contains("obj")).ToList();

		if (allWasm.Count == 0) return null;

		return FindBestWasmMatch(allWasm);
	}

	private string? ResolveExplicitWasmDir(string norm)
	{
		if (norm.StartsWith("user://") || norm.StartsWith("res://") || Path.IsPathRooted(norm))
			return norm.StartsWith("user://") || norm.StartsWith("res://") ? ProjectSettings.GlobalizePath(norm) : norm;
		return null;
	}

	private string? FindBestWasmMatch(List<string> allWasm)
	{
		return allWasm.FirstOrDefault(f => f.Contains("publish") && Path.GetFileName(f).Equals("MapScript.wasm", StringComparison.OrdinalIgnoreCase))
			   ?? allWasm.FirstOrDefault(f => f.Contains("publish"))
			   ?? allWasm.FirstOrDefault(f => Path.GetFileName(f).Equals("MapScript.wasm", StringComparison.OrdinalIgnoreCase))
			   ?? allWasm.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
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
				_activeMapScript = new WasmRuntime(PendingMapScriptPath, ResolveWasmName(PendingMapScriptPath), isDebug: GameContext.SessionState.IsTestMode);
			}
			else
			{
				_mapScriptLoadContext = new MapScriptLoadContext();
				using var fs = new FileStream(PendingMapScriptPath, FileMode.Open, FileAccess.Read);
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
		string name = Path.GetFileNameWithoutExtension(path);
		if (!name.Equals("MapScript", StringComparison.OrdinalIgnoreCase)) return name;

		string parentDir = Path.GetDirectoryName(path);
		string[] ignore = { "bin", "Release", "net10.0", "wasi-wasm", "publish" };

		while (!string.IsNullOrEmpty(parentDir))
		{
			string folderName = Path.GetFileName(parentDir);
			if (!string.IsNullOrEmpty(folderName) && !ignore.Contains(folderName, StringComparer.OrdinalIgnoreCase))
				return folderName;
			parentDir = Path.GetDirectoryName(parentDir);
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

	private IMapScript? FindMapScriptInAssembly(Assembly asm, string? searchName = null)
	{
		Type?[]? types = GetAssemblyTypesSafe(asm);
		if (types == null) return null;

		foreach (var t in types)
		{
			if (!IsValidMapScriptType(t, searchName)) continue;

			try
			{
				return (IMapScript?)Activator.CreateInstance(t);
			}
			catch
			{
			}
		}

		return null;
	}

	private Type?[]? GetAssemblyTypesSafe(Assembly asm)
	{
		try
		{
			return asm.GetTypes();
		}
		catch (ReflectionTypeLoadException ex)
		{
			return ex.Types;
		}
		catch
		{
			return null;
		}
	}

	private bool IsValidMapScriptType(Type? t, string? searchName)
	{
		if (t == null || !typeof(IMapScript).IsAssignableFrom(t) || t.IsInterface || t.IsAbstract || typeof(IWasmRuntime).IsAssignableFrom(t) || t.GetConstructor(Type.EmptyTypes) == null) return false;
		if (searchName != null && !t.Name.ToLower().Contains(searchName) && !searchName.Contains(t.Name.ToLower())) return false;
		return true;
	}

	public List<Unit3D>[] _controlGroups { get; set; } = new List<Unit3D>[10];
	private double[] _lastGroupPressTime { get; set; } = new double[10];


	private bool _isDragging { get; set; }
	private Vector2 _dragStart { get; set; }
	private Vector2 _dragEnd { get; set; }
	private float DragThreshold { get; set; } = 8f;

	public override void _Ready()
	{
		ProcessPerformanceOptimizer.EnforceOptimalPerformance();
		MainNode = GetTree().Root.GetNodeOrNull("Main");
		MainCamera = GetTree().Root.GetNodeOrNull<CameraControl>("Main/Camera3D");

		GD.Print($"[GAMEHOST_READY] GameHost _Ready starting");
		Instance = this;
		GameSettings.ApplyGraphicsSettings(this);
		if (LobbyManager.Instance != null)
		{
			LobbyManager.Instance.ServerChatCommandReceived += HandleServerChatCommand;
		}

		ReinitializeEcsAndServices();
	}

	private void InitializeGameEcs()
	{
		ResolveServices();

		if (OperatingSystem.IsWindows()) VSCodeManager.Instance.StartInstallIfNeeded();

		InitializeTracking();

		CreateGround();
		EnsurePropMultiMeshManager();
		SetupWorldEntityComponents();

		if (GroundTerrain != null) GameContext.EditorService.SetTerrainSplatMap(GroundTerrain.SplatMap, GroundTerrain.CliffSplatMap);

		SetupSkybox();
		UpdateDayNightVisuals(0.0f);

		InitializeResourceIds();

		InitializeSimulationService();

		if (_multiplayerActive) InitializeMultiplayerPlayers();
		else InitializeSingleplayerPlayers();

		EnsureFallbackPlayers();
		ConfigurePerspective();

		string rawMapName = LobbyManager.Instance?.ActiveMapName ?? "";
		if (!string.IsNullOrEmpty(rawMapName)) LoadUnitMetadata(rawMapName);

		ProcessMapLoadingAndScript(rawMapName);

		StartReplayRecordingIfNeeded(rawMapName);

		if (!GameContext.ReplayService.IsResettingForReplay) SchedulePostInitializationTasks();

		GameContext.ShroudService.Initialize(MainNode);
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
		AddChild(propMultiMeshManager);
	}

	private void InitializeResourceIds()
	{
		ECSContext.PlayerResourceService.GoldResourceId = "gold".AsResourceId(ECSContext.DefinitionManager);
		ECSContext.PlayerResourceService.WoodResourceId = "wood".AsResourceId(ECSContext.DefinitionManager);
		ECSContext.PlayerResourceService.StoneResourceId = "stone".AsResourceId(ECSContext.DefinitionManager);
	}

	private void InitializeSimulationService()
	{
		GameContext.SimulationService.SetRuntimeReferences(AllUnits, AllProps, ECSContext.DefinitionManager, ECSContext.PlayerResourceService.GoldResourceId, ECSContext.PlayerResourceService.WoodResourceId, ECSContext.PlayerResourceService.StoneResourceId, GroundTerrain);
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
			if (GameContext.World.IsAlive(entity) && TryGetUnit3D(entity, out var unit3D)) Callable.From(() => KillUnit(unit3D)).CallDeferred();
		};

		GameContext.SimulationService.OnPropDepleted = entity =>
		{
			if (GameContext.World != null && GameContext.World.IsAlive(entity) && GameContext.World.Has<PropIdentity>(entity))
			{
				string propId = GameContext.World.Get<PropIdentity>(entity).PropId;
				PropMultiMeshManager.Instance?.MarkDirty(propId);
			}
			else
			{
				PropMultiMeshManager.Instance?.MarkAllDirty();
			}
		};
		GameContext.SimulationService.OnResourceHarvested = entity =>
		{
			PropMultiMeshManager.Instance?.SetInstanceCustomData(entity, new Color(1f, 0f, 0f, 0f));
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
		if (!GameContext.World.IsAlive(targetEntity)) return;

		UnitId attackerId = (attackerEntity != Entity.Null && GameContext.World.IsAlive(attackerEntity)) ? (UnitId)attackerEntity.Id : UnitId.Invalid;
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
		if (GameContext.World.IsAlive(targetEntity) && TryGetUnit3D(targetEntity, out var targetUnit3D))
			GameContext.FXService.SpawnHealNumber(this, targetUnit3D.GlobalPosition, healAmount);
	}

	private void HandleUnitAttacked(Entity attackerEntity, Entity targetEntity)
	{
		if (!GameContext.World.IsAlive(attackerEntity) || !GameContext.World.IsAlive(targetEntity)) return;

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
		GameContext.SimulationService.OnUiRefreshRequested = () => InGameHUD.Instance?.RefreshUI(SelectedUnits);

		GameContext.SimulationService.OnResourceDepositedForPlayer = (playerIndex, resType, carry) =>
		{
			if (!GameContext.PerspectiveFilter.CanObserve(playerIndex)) return;
			string resTypeUpper = resType.ToUpper();
			Callable.From(() => InGameHUD.Instance?.ShowFeedbackText($"+{carry:F0} {resTypeUpper} deposited", new Color(0.2f, 0.9f, 0.4f))).CallDeferred();
		};

		GameContext.SimulationService.OnProductionCompleted = (playerIndex, unitToSpawn) =>
		{
			if (!GameContext.PerspectiveFilter.CanObserve(playerIndex)) return;
			string displayName = UnitRegistry.TryGetValue(unitToSpawn, out var nm) ? nm.Name : unitToSpawn.ToUpper();
			Callable.From(() => InGameHUD.Instance?.ShowFeedbackText($"✓ {displayName} training complete!", new Color(0.3f, 0.9f, 0.4f))).CallDeferred();
		};
	}

	private void InitializeMultiplayerPlayers()
	{
		_localPeerId = Multiplayer.GetUniqueId();
		if (!Multiplayer.IsServer()) GameContext.NetworkService.MarkClientEnteredMultiplayer();
		if (Multiplayer is SceneMultiplayer sceneMultiplayer) sceneMultiplayer.ServerRelay = false;

		if (LobbyManager.Instance != null && LobbyManager.Instance.PlayerList.Count > 0)
			SetupLobbyPlayers();
		else
			SetupDefaultPlayers();
	}

	private void SetupLobbyPlayers()
	{
		var playerEntities = GameContext.World.Get<NetworkMappingState>(ECSContext.WorldEntity).PlayerEntities;
		_peerIdToPlayerEntityMap[0] = playerEntities[0];
		UpdateScriptPlayerState(0, "Neutral", active: true, isComputer: true, teamIndex: 0);

		foreach (var p in LobbyManager.Instance.PlayerList)
		{
			if (p.Slot >= 0 && p.Slot < playerEntities.Length)
			{
				var playerEntity = playerEntities[p.Slot];
				_peerIdToPlayerEntityMap[p.PeerId] = playerEntity;
				if (GameContext.World.Has<Name>(playerEntity))
				{
					GameContext.World.Set(playerEntity, new Name(p.Name));
				}
				UpdateScriptPlayerState(p.Slot, p.Name, active: true, isComputer: p.PeerId < 0, teamIndex: p.TeamIndex);
			}
		}

		GameContext.SimulationService?.RecalculatePlayerRelationships();
	}

	private void InitializeSingleplayerPlayers()
	{
		if (LobbyManager.Instance != null && LobbyManager.Instance.PlayerList.Count > 0)
			SetupLobbyPlayers();
		else
			SetupDefaultPlayers();
	}

	private void SetupDefaultPlayers()
	{
		var playerEntities = GameContext.World.Get<NetworkMappingState>(ECSContext.WorldEntity).PlayerEntities;
		_peerIdToPlayerEntityMap[0] = playerEntities[0];
		_peerIdToPlayerEntityMap[1] = playerEntities[1];
		_peerIdToPlayerEntityMap[-1] = playerEntities[2];

		UpdateScriptPlayerState(0, "Neutral", active: true, isComputer: true, teamIndex: 0);
		UpdateScriptPlayerState(1, "Horaid_Topa", active: true, isComputer: false, teamIndex: 1);
		UpdateScriptPlayerState(2, "Enemy_AI", active: true, isComputer: true, teamIndex: 2);

		GameContext.SimulationService?.RecalculatePlayerRelationships();
	}

	private void UpdateScriptPlayerState(int pIdx, string name, bool active = true, bool isComputer = false, int teamIndex = -1)
	{
		if (!GameContext.World.Has<ScriptPlayersState>(ECSContext.WorldEntity)) return;

		ref var playersState = ref GameContext.World.Get<ScriptPlayersState>(ECSContext.WorldEntity);
		if (playersState.IsValidSlot(pIdx))
		{
			playersState.SetName(pIdx, name);
			playersState.SetActive(pIdx, active);
			playersState.SetComputerControlled(pIdx, isComputer);
			if (teamIndex >= 0)
			{
				int teamNameStringId = Realm.MapAPI.StringRegistry.GetOrAdd(teamIndex == 0 ? "Neutral" : $"Team {teamIndex}");
				playersState.SetTeam(pIdx, teamIndex, teamNameStringId);
			}

			var player = playersState.Get(pIdx);
			GameContext.World.GetBuffer<ScriptPlayer>().Set(pIdx, in player);

			if (teamIndex >= 0 && GameContext.World.Has<NetworkMappingState>(ECSContext.WorldEntity))
			{
				var playerEntities = GameContext.World.Get<NetworkMappingState>(ECSContext.WorldEntity).PlayerEntities;
				if (pIdx < playerEntities.Length && playerEntities[pIdx] != Entity.Null && GameContext.World.IsAlive(playerEntities[pIdx]))
				{
					int stringId = playersState.Get(pIdx).TeamNameStringId;
					if (GameContext.World.Has<PlayerTeam>(playerEntities[pIdx]))
					{
						ref var pt = ref GameContext.World.Get<PlayerTeam>(playerEntities[pIdx]);
						pt.TeamIndex = teamIndex;
						pt.TeamNameStringId = stringId;
					}
					else
					{
						GameContext.World.Add(playerEntities[pIdx], new PlayerTeam(teamIndex, stringId));
					}
				}
			}
		}
	}

	private void EnsureFallbackPlayers()
	{
		if (GameContext.World != null && ECSContext.WorldEntity != Entity.Null && GameContext.World.Has<NetworkMappingState>(ECSContext.WorldEntity))
		{
			var playerEntities = GameContext.World.Get<NetworkMappingState>(ECSContext.WorldEntity).PlayerEntities;
			if (playerEntities != null && playerEntities.Length > 2)
			{
				_peerIdToPlayerEntityMap[0] = playerEntities[0];
				_peerIdToPlayerEntityMap[1] = playerEntities[1];
				_peerIdToPlayerEntityMap[-1] = playerEntities[2];
			}
		}
	}

	private void ProcessMapLoadingAndScript(string rawMapName)
	{
		if (!ShouldProcessMapLoading()) return;

		if (ShouldLoadCustomTerrain())
			LoadCustomTerrain(rawMapName);

		if (ShouldLoadAndRunMapScript())
		{
			ExecuteMapScriptLoading(rawMapName);
			return;
		}

		RebakeNavMesh();
	}

	private bool ShouldProcessMapLoading()
	{
		bool isGameStarted = LobbyManager.Instance?.IsGameStarted ?? false;
		return isGameStarted || GameContext.SessionState.IsMapEditorMode;
	}

	private bool ShouldLoadCustomTerrain()
	{
		return !GameContext.SessionState.IsMapEditorMode && !GameContext.SaveLoadService.IsLoadingMap;
	}

	private bool ShouldLoadAndRunMapScript()
	{
		if (GameContext.SaveLoadService.IsLoadingMap) return false;

		bool isGameStarted = LobbyManager.Instance?.IsGameStarted ?? false;
		return !isGameStarted || IsServerActive() || GameContext.SessionState.IsMapEditorMode;
	}

	private void ApplyTestRandomSeedIfNeeded(string rawMapNameOrPath)
	{
		bool isSinglePlayerOrTest = (LobbyManager.Instance?.IsSinglePlayer == true) || GameContext.SessionState.IsTestMode || !IsMultiplayerActive();
		if (!isSinglePlayerOrTest) return;

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string? resolvedDir = !string.IsNullOrEmpty(rawMapNameOrPath) ? ResolveMapDirectory(rawMapNameOrPath) : wsPath;
		string metadataPath = !string.IsNullOrEmpty(resolvedDir)
			? resolvedDir
			: (!string.IsNullOrEmpty(rawMapNameOrPath) ? rawMapNameOrPath : wsPath);

		var metadata = MapFileService.LoadMetadata(metadataPath);
		if (metadata.MapProperties?.TestRandomSeed != null)
		{
			EcsSharedMemory.RandomSeed = metadata.MapProperties.TestRandomSeed.Value;
			GameContext.RandomService.SetSeed(metadata.MapProperties.TestRandomSeed);
		}
	}

	private void ExecuteMapScriptLoading(string rawMapName)
	{
		ApplyTestRandomSeedIfNeeded(rawMapName);
		LoadMapScript(rawMapName);
		if (_activeMapScript != null && !GameContext.SessionState.IsMapEditorMode)
		{
			try
			{
				_activeMapScript.Initialize(this);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[GameHost] Exception during active map script Initialize: {ex.Message}");
			}
		}
		ReportStringTableCollisions();
		RebakeNavMesh();
	}

	private static void ReportStringTableCollisions()
	{
		int collisions = Realm.MapAPI.StringRegistry.CollisionCount;
		if (collisions == 0) return;

		GD.PushError($"[GameHost] {collisions} texts collided on their content hash and moved into the reserved identifier range, so peers can disagree about which text an identifier stands for: {Realm.MapAPI.StringRegistry.CollisionSummary}");
	}

	private void LoadCustomTerrain(string rawMapName)
	{
		string targetPath = ResolveCustomTerrainPath(rawMapName);

		if (!string.IsNullOrEmpty(targetPath) && (targetPath.StartsWith("user://") || targetPath.StartsWith("res://")))
			targetPath = ProjectSettings.GlobalizePath(targetPath);

		if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
		{
			bool terrainOnly = _multiplayerActive && !Multiplayer.IsServer();
			LoadMapFromFile(targetPath, terrainOnly, true);
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
		bool isGameStarted = LobbyManager.Instance != null && LobbyManager.Instance.IsGameStarted;
		if (!isGameStarted || GameContext.SessionState.IsMapEditorMode || ReplayPlaybackManager.Instance.IsPlayingReplay || !GameSettings.RecordReplays) return;

		string replayDir = ProjectSettings.GlobalizePath("user://replays");
		string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
		string replayPath = Path.Combine(replayDir, $"replay_{timestamp}.rep");

		GameContext.ReplayService.StartRecording(replayPath, rawMapName, LobbyManager.Instance?.PlayerList);
		GD.Print($"[ReplayRecorder] Started recording to {replayPath}");
	}

	private void SchedulePostInitializationTasks()
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
				ReplayPlaybackManager.Instance.ApplyInitialFrame();
		};
	}

	public void ResetWorldAndState()
	{
		ReplayPlaybackManager.Instance.StopReplay();
		StopRecording();

		ClearNodeCollection(AllUnits);
		SelectedUnits.Clear();
		_activeImpulseUnits.Clear();
		ClearNodeCollection(AllProps);
		ClearNodeCollection(AllDecals);
		ClearNodeCollection(AllLootItems);
		IdToLootItem.Clear();
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
		EntityIdToUnit3D.Clear();
		PlayerToUnits.Clear();
		EntityToProp3D.Clear();
		ClearNodeCollection(AllLootItems);
		IdToLootItem.Clear();
		_activeMapScript = null;
		ClearStaticText();

		bool wasMapEditorMode = GameContext.SessionState.IsMapEditorMode;
		bool wasLoadingMap = GameContext.SaveLoadService.IsLoadingMap;
		bool wasProfilingEnabled = GameContext.PerformanceTrackingService?.IsFunctionProfilingEnabled ?? false;

		GameContext.DisposeAndRecreateServices();
		ResolveServices();

		GameContext.SessionState.IsMapEditorMode = wasMapEditorMode;
		GameContext.SaveLoadService.IsLoadingMap = wasLoadingMap;
		if (wasProfilingEnabled)
		{
			GameContext.PerformanceTrackingService.IsFunctionProfilingEnabled = true;
		}

		// Do not trigger lazy GroundTerrain creation here: InitializeGameEcs()
		// builds the ground exactly once (CreateGround at Realm.Client.Core.cs:2751).
		// Triggering the getter here previously built the whole 128x128 mesh/water/navmesh
		// a second time on every _Ready, stalling the main thread at startup.
		if (_groundTerrain != null)
		{
			GameContext.EditorService.SetTerrainSplatMap(_groundTerrain.SplatMap, _groundTerrain.CliffSplatMap);
		}

		InitializeGameEcs();
	}

	public void StopRecording()
	{
		GameContext.ReplayService?.StopRecording();
	}

	public override void _ExitTree()
	{
		LogGameStabilitySummary();

		if (LobbyManager.Instance != null)
			LobbyManager.Instance.ServerChatCommandReceived -= HandleServerChatCommand;

		StopRecording();
		GameContext.Dispose();
		Instance = null;
	}

	private void LogGameStabilitySummary()
	{
		if ((Multiplayer.MultiplayerPeer == null || Multiplayer.IsServer()) && GameContext.PerformanceTrackingService.TrackerTickDurations != null && GameContext.PerformanceTrackingService.TrackerTickDurations.Count >= 30)
		{
			HostStabilityTracker.AddGameSummary(new GameStabilitySummary
			{
				AvgTickMs = HostStabilityTracker.CalculateAverage(GameContext.PerformanceTrackingService.TrackerTickDurations),
				MedianTickMs = HostStabilityTracker.CalculateMedian(GameContext.PerformanceTrackingService.TrackerTickDurations),
				MaxTickMs = HostStabilityTracker.CalculateMax(GameContext.PerformanceTrackingService.TrackerTickDurations),
				AvgApiMs = HostStabilityTracker.CalculateAverage(GameContext.PerformanceTrackingService.TrackerApiDurations),
				MedianApiMs = HostStabilityTracker.CalculateMedian(GameContext.PerformanceTrackingService.TrackerApiDurations),
				MaxApiMs = HostStabilityTracker.CalculateMax(GameContext.PerformanceTrackingService.TrackerApiDurations)
			});
		}
	}

	private void HandleServerChatCommand(int slot, string message)
	{
		if (!IsServerActive()) return;

		UnitId heroUnit = UnitId.Invalid;
		foreach (var u in AllUnits)
		{
			if (GodotObject.IsInstanceValid(u) && u.Player == slot && GameContext.World != null && GameContext.World.IsAlive(u.Entity) && !GameContext.World.Has<Dead>(u.Entity))
			{
				heroUnit = (UnitId)u.Entity.Id;
				break;
			}
		}

		OnPlayerChatMessage?.Invoke(message, heroUnit);
	}

	private void CreateGround()
	{
		if (!GameContext.SessionState.IsMapEditorMode)
		{
			string? activeMapName = LobbyManager.Instance?.ActiveMapName;
			if (!string.IsNullOrEmpty(activeMapName))
			{
				string? resolvedDirectory = ResolveMapDirectory(activeMapName);
				if (!string.IsNullOrEmpty(resolvedDirectory) && GameContext.MapStorageService != null)
					GameContext.MapStorageService.CurrentMapDirectory = resolvedDirectory;
			}
		}

		ClearExistingGround();

		if (GameContext.SessionState.IsMapEditorMode || GameContext.SaveLoadService.IsLoadingMap)
		{
			CreateEditableGround();
			return;
		}

		if (!ShouldCreateRuntimeGround()) return;

		string? rawMapName = LobbyManager.Instance?.ActiveMapName;
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
		var terrainNode = new EditableTerrain();
		terrainNode.Name = "Ground";
		AddChild(terrainNode);
		GroundTerrain = terrainNode;
	}

	private bool ShouldCreateRuntimeGround()
	{
		return LobbyManager.Instance != null && LobbyManager.Instance.IsGameStarted;
	}

	private string ResolveTerrainPath(string rawMapName)
	{
		string? resolvedDir = ResolveMapDirectory(rawMapName);
		if (!string.IsNullOrEmpty(resolvedDir))
			return Path.Combine(resolvedDir, "terrain.json");

		string normalizedRawMapName = rawMapName.Replace('\\', '/');
		string explicitPath = TryGetExplicitTerrainPath(normalizedRawMapName);
		if (!string.IsNullOrEmpty(explicitPath)) return explicitPath;

		return FallbackTerrainPath(rawMapName);
	}

	private string TryGetExplicitTerrainPath(string normalizedRawMapName)
	{
		if (!normalizedRawMapName.StartsWith("user://") && !normalizedRawMapName.StartsWith("res://") && !Path.IsPathRooted(normalizedRawMapName))
			return string.Empty;

		string checkDir = normalizedRawMapName;
		if (normalizedRawMapName.StartsWith("user://") || normalizedRawMapName.StartsWith("res://"))
			checkDir = ProjectSettings.GlobalizePath(normalizedRawMapName);

		return Directory.Exists(checkDir) ? Path.Combine(checkDir, "terrain.json") : string.Empty;
	}

	private string FallbackTerrainPath(string rawMapName)
	{
		string normalizedMapName = rawMapName.ToLower().Trim();
		string mapDir = $"res://Maps/{normalizedMapName}";
		if (Directory.Exists(ProjectSettings.GlobalizePath(mapDir)))
			return $"res://Maps/{normalizedMapName}/terrain.json";

		string userDir = ProjectSettings.GlobalizePath($"user://maps/{normalizedMapName}");
		if (Directory.Exists(userDir))
			return $"user://maps/{normalizedMapName}/terrain.json";

		return $"res://Maps/{normalizedMapName}/terrain.json";
	}

	private void CreateRuntimeGround(string terrainPath)
	{
		var activeTerrainNode = new RuntimeTerrain();
		activeTerrainNode.Name = "Ground";
		AddChild(activeTerrainNode);
		GroundTerrain = activeTerrainNode;

		string targetPath = terrainPath.StartsWith("user://") || terrainPath.StartsWith("res://")
			? ProjectSettings.GlobalizePath(terrainPath)
			: terrainPath;

		if (File.Exists(targetPath))
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
			worldEnv.Environment.BackgroundMode = Environment.BGMode.Sky;
			EditorSkyboxPath = path;
		}
	}

	private string GetSkyboxFullPath(string path)
	{
		if (path.StartsWith("res://") || File.Exists(path)) return path;

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string fullPath = Path.Combine(wsPath, path.Replace('/', Path.DirectorySeparatorChar));
		if (File.Exists(fullPath)) return fullPath;

		fullPath = Path.Combine(wsPath, "Assets", "skyboxes", Path.GetFileName(path));
		if (File.Exists(fullPath)) return fullPath;

		string rtexName = Path.GetFileNameWithoutExtension(path) + ".rtex";
		string rtexCandidate = Path.Combine(wsPath, "Assets", "skyboxes", rtexName);
		if (File.Exists(rtexCandidate)) return rtexCandidate;

		return path;
	}

	private Texture2D? LoadSkyboxTexture(string fullPath)
	{
		if (fullPath.StartsWith("res://"))
		{
			return ResourceLoader.Exists(fullPath) ? GD.Load<Texture2D>(fullPath) : null;
		}

		if (!File.Exists(fullPath)) return null;

		if (fullPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			byte[]? webpBytes = RtexFile.GetLayer(File.ReadAllBytes(fullPath), 0);
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

	private void RemoveIfHas<T>(Entity entity) where T : unmanaged
	{
		if (GameContext.World.Has<T>(entity)) GameContext.World.Remove<T>(entity);
	}

	public void ClearUnitOrders(Entity entity)
	{
		RemoveIfHas<MoveTo>(entity);
		RemoveIfHas<PathFollow>(entity);
		RemoveIfHas<AttackTarget>(entity);
		RemoveIfHas<AttackMove>(entity);
		RemoveIfHas<HoldPosition>(entity);
		RemoveIfHas<Follow>(entity);
		RemoveIfHas<Patrol>(entity);
		RemoveIfHas<HealingTarget>(entity);
		RemoveIfHas<WaypointQueue>(entity);
		RemoveIfHas<Gatherer>(entity);
		RemoveIfHas<BuildTask>(entity);
		RemoveIfHas<BuildQueue>(entity);
	}

	private void StopGatheringMovement(Entity entity)
	{
		if (GameContext.World.IsAlive(entity))
		{
			if (GameContext.World.Has<MoveTo>(entity)) GameContext.World.Remove<MoveTo>(entity);
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

	public void IssueGatherCommand(Entity propEntity, bool isQueued = false)
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		if (SelectedUnits.Count == 0 || propEntity == Entity.Null || !GameContext.World.IsAlive(propEntity)) return;

		string propId = GameContext.World.Has<PropIdentity>(propEntity) ? GameContext.World.Get<PropIdentity>(propEntity).PropId : "";
		string? resType = GetPropResourceType(propId);
		if (resType == null) return;

		Vector3 propPos = Vector3.Zero;
		if (GameContext.World.Has<Position>(propEntity))
		{
			var p = GameContext.World.Get<Position>(propEntity).Value;
			propPos = new Vector3(p.X, p.Y, p.Z);
		}

		SpawnTargetIndicator(propPos, new Color(0.9f, 0.8f, 0.1f));
		InGameHUD.Instance?.ShowFeedbackText($"Gathering {resType.ToUpper()} from {propId}", new Color(0.9f, 0.8f, 0.1f));

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			IssueMultiplayerGatherCommand(propEntity, propPos, isQueued);
			return;
		}

		IssueLocalGatherCommand(propEntity, propPos, resType, isQueued);
	}

	private string? GetPropResourceType(string propId)
	{
		return GameContext.RegistryService.GetResourceTypeForNode(propId);
	}

	private void IssueMultiplayerGatherCommand(Entity propEntity, Vector3 propPos, bool isQueued)
	{
		int localPlayerIndex = GameContext.PerspectiveFilter.ControlledPlayerIndex;
		if (localPlayerIndex <= 0) return;
		var targetIds = new List<int>();
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.Player != localPlayerIndex || unit.UnitId != "worker") continue;
			targetIds.Add(GetServerEntityId(unit.Entity));
		}

		QueueClientCommand(isQueued ? "gather_queued" : "gather", targetIds, propPos, GetServerEntityId(propEntity), "");
	}

	private void IssueLocalGatherCommand(Entity propEntity, Vector3 propPos, string resType, bool isQueued)
	{
		int localPlayerIndex = GameContext.PerspectiveFilter.ControlledPlayerIndex;
		if (localPlayerIndex <= 0) return;
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.Player != localPlayerIndex || unit.UnitId != "worker") continue;

				if (isQueued && GameContext.InputService != null && Realm.Client.Utils.UnitActivityHelper.IsUnitActive(unit.Entity))
			{
				GameContext.InputService.EnqueueCommand(unit.Entity, "gather", new System.Numerics.Vector3(propPos.X, propPos.Y, propPos.Z), propEntity);
			}
			else
			{
				if (!isQueued) ClearUnitOrders(unit.Entity);

				GameContext.World.SetOrAdd(unit.Entity, new Gatherer(resType, propEntity));
				GameContext.World.SetOrAdd(unit.Entity, new MoveTo(new System.Numerics.Vector3(propPos.X, propPos.Y, propPos.Z)));
			}
		}
	}

	public void IssueGatherCommand(Prop3D prop, bool isQueued = false)
	{
		if (prop != null && GodotObject.IsInstanceValid(prop))
		{
			IssueGatherCommand(prop.Entity, isQueued);
		}
	}

	private Entity CreateEcsUnit(string id, string name, float hp, float damage, float range, float armor, float speed, Vector3 pos, PlayerEntity owner)
	{
		UnitMetadata regMeta = null;
		if (!UnitRegistry.TryGetValue(id, out regMeta)) TryGetUnitOrBuildingMetadata(id, out regMeta);

		bool hasShields = GameContext.World?.GetFieldOrDefault<PlayerUpgrades, bool>(owner.Value, u => u.ShieldsUpgrade) ?? false;
		bool hasWeapons = GameContext.World?.GetFieldOrDefault<PlayerUpgrades, bool>(owner.Value, u => u.WeaponsUpgrade) ?? false;

		var entity = regMeta != null
			? CreateEcsUnitEntityWithMeta(id, name, hp, damage, range, armor, speed, pos, owner, regMeta, hasShields, hasWeapons)
			: GameContext.UnitSpawnService.CreateEcsUnitEntity(id, name, hp, damage, range, armor, speed, 15.0f, false, 1.5f, 8, pos, owner, owner.Value, hasShields, hasWeapons, null, 0f, 0f, 0f, 0f, 0f, "unarmored", 0f, 0f, 0f, "normal", 0f, 1f, "None", 0f, 0f, 0f, 1f, 0.5f, 0.25f, false, 0, "Ground");

		if (regMeta != null) ApplyUnitAttributes(entity, regMeta);

		OnUnitCreated?.Invoke(GameContext.SimulationService.GetUnitWrapper(entity));
		return entity;
	}

	private Entity CreateEcsUnitEntityWithMeta(string id, string name, float hp, float damage, float range, float armor, float speed, Vector3 pos, PlayerEntity owner, UnitMetadata meta, bool hasShields, bool hasWeapons)
	{
		return GameContext.UnitSpawnService.CreateEcsUnitEntity(
			id, name, hp, damage, range, armor, speed,
			meta.ScanRadius > 0 ? meta.ScanRadius : 15.0f, meta.IsHero,
			meta.AttackCooldown > 0 ? meta.AttackCooldown : 1.5f, GetUnitPathingFlags(meta), pos, owner,
			owner.Value, hasShields, hasWeapons, meta.Targets,
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
			GameContext.World.Set(entity, new UnitAttributes(meta.Strength, meta.Agility, meta.Vitality, meta.Intelligence, meta.Wisdom, meta.Fortune));
			AttributeStatCalculator.RecalculateEntityStats(GameContext.World, entity);
		}
	}

	private void ApplyGigachadBuffsIfNeeded(Entity entity, int playerIndex)
	{
		if (!GameContext.CheatService.GigachadEnabled) return;
		int localPlayerIndex = GameContext.PerspectiveFilter.ControlledPlayerIndex;
		if (localPlayerIndex <= 0) return;
		if (NetworkService.ArePlayerIndicesEnemies(localPlayerIndex, playerIndex)) return;
		if (GameContext.World.Has<Health>(entity)) GameContext.World.Set(entity, new Health(9000f, 9000f));
		if (GameContext.World.Has<Attack>(entity))
		{
			var atk = GameContext.World.Get<Attack>(entity);
			GameContext.World.Set(entity, new Attack(9001f, atk.Range, atk.Cooldown, atk.CurrentCooldown));
		}
	}

	private void HandleBuildingSpawn(Entity entity, Unit3D unit3D, string id, Vector3 pos)
	{
		if (TryGetUnitOrBuildingMetadata(id, out var meta) && (meta.PathingType == 16 || string.Equals(meta.VisualMode, "GroundPlane", StringComparison.OrdinalIgnoreCase)))
		{
			return;
		}

		GameContext.World.SetOrAdd(entity, new BuildingSpawnOffset(new System.Numerics.Vector3(0f, 0f, 8f)));
		float baseRadius = GetOrCalculateObstacleRadius(id, unit3D) * GetModelCollisionCircleRatio(GetModelAssetKey(unit3D));
		GameContext.World.SetOrAdd(entity, new CollisionRadius(baseRadius));
		if (!GameContext.SessionState.IsMapEditorMode) CarveObstacle(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z), baseRadius);
	}

	private Unit3D SpawnUnit3D(Entity entity, string id, string modelPath, Vector3 pos, bool isBuilding, int player = 0, bool isFromQueue = false, bool executeSpawnShader = false)
	{
		int playerIndex = player >= 0 ? player : 0;

		SetupUnitEntityComponents(entity, id, playerIndex);

		var unit3D = CreateAndSetupUnit3DNode(entity, id, modelPath, pos, isBuilding, playerIndex);

		if (isBuilding) HandleBuildingSpawn(entity, unit3D, id, pos);

		ApplyMapEditorTransforms(unit3D);

		unit3D.Visible = true;
		unit3D.UpdateLodVisibility();

		EntityToUnit3D[entity] = unit3D;
		EntityIdToUnit3D[entity.Id] = unit3D;
		if (!PlayerToUnits.TryGetValue(playerIndex, out var pList))
		{
			pList = new List<Unit3D>();
			PlayerToUnits[playerIndex] = pList;
		}
		pList.Add(unit3D);
		AllUnits.Add(unit3D);

		UpdatePopulationStats(id, playerIndex, isFromQueue);

		if (executeSpawnShader) ExecuteSpawnShader(unit3D, id);

		return unit3D;
	}

	private void SetupUnitEntityComponents(Entity entity, string id, int playerIndex)
	{
		GameContext.World.SetOrAdd(entity, new UnitOwnerPlayer(playerIndex));
		GameContext.World.SetOrAdd(entity, new DefinitionId(id));
		ApplyGigachadBuffsIfNeeded(entity, playerIndex);
		SyncPreviousTransform(GameContext.World, entity);
	}

	private static readonly QueryDescription _positionQuery = new QueryDescription().WithAll<Position>().WithNone<Building, PropIdentity, Dead>();
	private static readonly QueryDescription _rotationYQuery = new QueryDescription().WithAll<RotationY>().WithNone<Building, PropIdentity, Dead>();

	public void CapturePreviousStateSnapshots()
	{
		var world = GameContext.World;
		if (world == null) return;

		foreach (var entity in world.Query(in _positionQuery))
		{
			ref var pos = ref world.Get<Position>(entity);
			world.SetOrAdd(entity, new PreviousPosition(pos.Value));
		}

		foreach (var entity in world.Query(in _rotationYQuery))
		{
			ref var rot = ref world.Get<RotationY>(entity);
			world.SetOrAdd(entity, new PreviousRotationY(rot.Value));
		}
	}

	public static void SyncPreviousTransform(EcsWorld world, Entity entity)
	{
		if (world == null || !world.IsAlive(entity)) return;

		if (world.Has<Position>(entity))
		{
			world.SetOrAdd(entity, new PreviousPosition(world.Get<Position>(entity).Value));
		}

		if (world.Has<RotationY>(entity))
		{
			world.SetOrAdd(entity, new PreviousRotationY(world.Get<RotationY>(entity).Value));
		}
	}

	private Unit3D CreateAndSetupUnit3DNode(Entity entity, string id, string modelPath, Vector3 pos, bool isBuilding, int playerIndex)
	{
		var unit3D = new Unit3D { Entity = entity, UnitId = id, IsBuilding = isBuilding, Name = $"{id}_{entity.Id}", Player = playerIndex };

		if (!isBuilding && !GameContext.SessionState.IsMapEditorMode)
		{
		}

		AddChild(unit3D);
		unit3D.Position = pos;
		unit3D.LoadModel(modelPath);
		unit3D.UpdatePlayerColorVisual();

		return unit3D;
	}

	private void ApplyMapEditorTransforms(Unit3D unit3D)
	{
		if (!GameContext.SessionState.IsMapEditorMode) return;

		unit3D.RotationDegrees = new Vector3(0.0f, GameContext.EditorService.EditorPlacementRotation, 0.0f);
		unit3D.Scale *= EditorPlacementScale;
	}

	private void UpdatePopulationStats(string id, int playerIndex, bool isFromQueue)
	{
		if (_multiplayerActive && !IsServerActive()) return;
		if (!UnitRegistry.TryGetValue(id, out var popMeta)) return;

		if (!isFromQueue && popMeta.PopCost > 0)
		{
			int curPop = GameContext.ResourceEconomyService?.GetPlayerCurrentPopulation(playerIndex) ?? 0;
			GameContext.ResourceEconomyService?.SetPlayerCurrentPopulation(playerIndex, curPop + popMeta.PopCost);
		}
	}

	private void ExecuteSpawnShader(Unit3D unit3D, string id)
	{
		string spawnShader = GetModelSpawnShader(unit3D);
		if (string.IsNullOrEmpty(spawnShader)) spawnShader = GetModelSpawnShader(id);

		if (!string.IsNullOrEmpty(spawnShader)) SpawnDeathShaderManager.AnimateTransition(unit3D, spawnShader, true);
	}


	public bool TryGetInterpolatedTransform(Entity entity, float fraction, out System.Numerics.Vector3 position, out float rotationY)
	{
		if (GameContext.SimulationService != null)
		{
			return GameContext.SimulationService.TryGetInterpolatedTransform(entity, fraction, out position, out rotationY);
		}

		position = System.Numerics.Vector3.Zero;
		rotationY = 0f;
		return false;
	}

	public (System.Numerics.Vector3 Position, float RotationY) GetInterpolatedTransform(Entity entity, float fraction)
	{
		return GameContext.SimulationService?.GetInterpolatedTransform(entity, fraction) ?? (System.Numerics.Vector3.Zero, 0f);
	}

	public override void _Process(double delta)
	{
		using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.GodotProcess))
		{
			float fDelta = (float)delta;

			if (GameContext.World != null)
			{
				using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.UpdateVisualNodesFromEcs))
				{
					UpdateRenderFrameTransforms(fDelta);
					UpdateSelectedAndImpulseUnitVisuals(fDelta);
				}
			}

			if (GameContext.ShroudService != null)
			{
				using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.ShroudServiceTick))
				{
					bool isReplay = ReplayPlaybackManager.Instance.IsPlayingReplay;
					bool isSpectator = GameContext.PerspectiveFilter.IsSpectating;
					int spectatorPerspective = InGameHUD.Instance?.LiveSpectatorPerspective ?? -1;
					GameContext.ShroudService.Tick(fDelta, AllUnits, AllProps, AllDecals, AllVfx, VisualProjectilePool.Instance.ActiveProjectiles, spectatorPerspective, isReplay, isSpectator);
				}
			}

			if (_cachedWorldEnvironment == null || !GodotObject.IsInstanceValid(_cachedWorldEnvironment))
			{
				_cachedWorldEnvironment = MainNode?.GetNodeOrNull<WorldEnvironment>("WorldEnvironment") ?? GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
			}

			using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.EnvironmentUpdateFog))
			{
				GameContext.EnvironmentService?.UpdateEnvironmentalFog(MainCamera, _cachedWorldEnvironment);
			}
			using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.EnvironmentUpdateWeatherParticles))
			{
				GameContext.EnvironmentService?.UpdateWeatherParticlePosition(MainCamera);
			}

			VisualProjectilePool.Instance?.AdvanceActiveProjectiles(fDelta);

			GameContext.FXService?.UpdateFloatingLabels(fDelta);
			GameContext.FXService?.RenderBatchedRibbonTrails(MainCamera);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.GodotPhysicsProcess))
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
			if (GameContext.NetworkService.IsPaused) return;

			DispatchPhysicsTick(fDelta);
		}
	}

	private bool ShouldProcessPhysicsTick()
	{
		return LobbyManager.Instance?.IsGameStarted == true || GameContext.SessionState.IsMapEditorMode;
	}

	private void ProcessReplayTick(float fDelta)
	{
		CapturePreviousStateSnapshots();
		ReplayPlaybackManager.Instance.Update(fDelta);
	}

	private void DispatchPhysicsTick(float fDelta)
	{
		using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.DispatchPhysicsTick))
		{
			if (_multiplayerActive && !IsServerActive()) UpdateClientTick(fDelta);
			else if (GameContext.SessionState.IsMapEditorMode) ProcessMapEditorTick(fDelta);
			else ProcessGameplayTick(fDelta);
		}
	}

	private void UpdatePauseCountdown(float fDelta)
	{
		if (GameContext.NetworkService.ResumeCountdownSeconds < 0) return;

		GameContext.NetworkService.ResumeCountdownTimer += fDelta;
		if (GameContext.NetworkService.ResumeCountdownTimer < 1.0f) return;

		GameContext.NetworkService.ResumeCountdownTimer -= 1.0f;
		GameContext.NetworkService.ResumeCountdownSeconds--;

		bool isServer = Multiplayer.MultiplayerPeer != null && Multiplayer.IsServer();

		if (GameContext.NetworkService.ResumeCountdownSeconds <= 0)
		{
			GameContext.NetworkService.ResumeCountdownSeconds = -1;
			GameContext.NetworkService.IsPaused = false;
			if (isServer) Rpc(nameof(BroadcastPauseState), false, -1, true);
			else UpdatePauseUI();
		}
		else
		{
			if (isServer) Rpc(nameof(BroadcastCountdownState), GameContext.NetworkService.ResumeCountdownSeconds, GameContext.NetworkService.CountdownForcedByHost);
			else UpdatePauseUI();
		}
	}

	public bool IsMultiplayerActive() => _multiplayerActive;

	public bool IsMultiplayerServerActive() => _multiplayerActive && IsServerActive();

	public bool IsClientSimulationOnly => _multiplayerActive && !IsServerActive();

	private bool IsServerActive()
	{
		if (Multiplayer.MultiplayerPeer == null) return true;
		try
		{
			return Multiplayer.IsServer();
		}
		catch
		{
			return true;
		}
	}

	public void RebakeNavMesh()
	{
		if (GroundTerrain != null)
		{
			GroundTerrain.BakeNavMesh();
		}
	}

	public void CarveObstacle(System.Numerics.Vector3 pos, float radius)
	{
		if (GameContext.World != null && GameContext.World.IsAlive(ECSContext.WorldEntity) && GameContext.World.Has<TerrainState>(ECSContext.WorldEntity))
		{
			ref var state = ref GameContext.World.Get<TerrainState>(ECSContext.WorldEntity);
			ECSContext.TerrainNavMeshService?.CarveObstacle(ref state, pos, radius);
		}
	}

	public void UncarveObstacle(System.Numerics.Vector3 pos, float radius)
	{
		if (GameContext.World != null && GameContext.World.IsAlive(ECSContext.WorldEntity) && GameContext.World.Has<TerrainState>(ECSContext.WorldEntity))
		{
			ref var state = ref GameContext.World.Get<TerrainState>(ECSContext.WorldEntity);
			ECSContext.TerrainNavMeshService?.UncarveObstacle(ref state, pos, radius);
		}
	}


	private const float DefaultObstacleRadius = 0.5f;

	public float GetOrCalculateObstacleRadius(string id, Node3D node)
	{
		if (ECSContext.TerrainNavMeshService.ObstacleRadiusCache.TryGetValue(id, out float cachedRadius))
		{
			return cachedRadius;
		}

		string modelKey = node != null ? GetModelAssetKey(node) : GetModelAssetKey(id);
		if (!string.IsNullOrEmpty(modelKey) && GameContext.ModelOverrideService.ModelObstacleRadii.TryGetValue(modelKey, out float measuredRadius) && measuredRadius > 0f)
		{
			float modelScale = GetModelScale(id);
			if (modelScale <= 0f) modelScale = 1f;

			float scaledRadius = measuredRadius * modelScale;
			ECSContext.TerrainNavMeshService.ObstacleRadiusCache[id] = scaledRadius;
			return scaledRadius;
		}

		if (node == null)
		{
			if (TryMeasureModelAssetRadius(id, modelKey, out float assetRadius))
			{
				ECSContext.TerrainNavMeshService.ObstacleRadiusCache[id] = assetRadius;
				return assetRadius;
			}

			return DefaultObstacleRadius;
		}

		float calculatedRadius = CalculateNodeRadius(node);
		ECSContext.TerrainNavMeshService.ObstacleRadiusCache[id] = calculatedRadius;
		return calculatedRadius;
	}

	private bool TryMeasureModelAssetRadius(string id, string modelKey, out float radius)
	{
		radius = 0f;
		string modelPath = GetTemplateModelPath(id);
		if (string.IsNullOrEmpty(modelPath)) return false;

		Node modelNode = ModelCache.GetModel(modelPath);
		if (modelNode == null) return false;

		try
		{
			if (modelNode is not Node3D modelRoot) return false;

			float rawRadius = MeasureModelRadius(modelRoot);
			if (rawRadius <= 0f) return false;

			float modelScale = GetModelScale(id);
			if (modelScale <= 0f) modelScale = 1f;

			if (!string.IsNullOrEmpty(modelKey)) GameContext.ModelOverrideService.ModelObstacleRadii[modelKey] = rawRadius;
			radius = rawRadius * modelScale;
			return true;
		}
		finally
		{
			modelNode.Free();
		}
	}

	private string GetTemplateModelPath(string id)
	{
		if (UnitRegistry.TryGetValue(id, out var unitMeta) && !string.IsNullOrEmpty(unitMeta.ModelPath)) return unitMeta.ModelPath;
		if (BuildingRegistry.TryGetValue(id, out var buildingMeta) && !string.IsNullOrEmpty(buildingMeta.ModelPath)) return buildingMeta.ModelPath;
		if (PropRegistry.TryGetValue(id, out var propMeta) && !string.IsNullOrEmpty(propMeta.ModelPath)) return propMeta.ModelPath;
		if (ResourceRegistry.TryGetValue(id, out var resourceMeta) && !string.IsNullOrEmpty(resourceMeta.ModelPath)) return resourceMeta.ModelPath;
		return null;
	}

	public float ResolveTemplateBodyRadius(string templateId, Node3D node)
	{
		float radius = GetOrCalculateObstacleRadius(templateId, node) * GetModelCollisionCircleRatio(templateId);
		if (radius <= GameplayConstants.MinimumAuthoredCollisionRadius) return GameplayConstants.DefaultCollisionRadius;
		return radius;
	}

	private float CalculateNodeRadius(Node node)
	{
		if (node is not Node3D root) return DefaultObstacleRadius;

		float maxRadius = DefaultObstacleRadius;
		var toRoot = root.GlobalTransform.AffineInverse();

		foreach (var shapeNode in FindChildrenOfType<CollisionShape3D>(node))
		{
			if (shapeNode.Shape == null) continue;
			float r = GetShapeRadius(shapeNode.Shape) * GetScaleRelativeToRoot(root, shapeNode);
			if (r > maxRadius) maxRadius = r;
		}

		foreach (var meshNode in FindChildrenOfType<MeshInstance3D>(node))
		{
			if (meshNode.Mesh == null) continue;

			var aabb = meshNode.Mesh.GetAabb();
			if (aabb.Size == Vector3.Zero) continue;

			var meshTransform = toRoot * meshNode.GlobalTransform;
			for (int i = 0; i < 8; i++)
			{
				var corner = meshTransform * aabb.GetEndpoint(i);
				float r = MathF.Sqrt(corner.X * corner.X + corner.Z * corner.Z);
				if (r > maxRadius) maxRadius = r;
			}
		}

		return maxRadius;
	}

	private static float GetScaleRelativeToRoot(Node3D root, Node3D child)
	{
		var rootBasis = root.GlobalTransform.Basis;
		var childBasis = child.GlobalTransform.Basis;
		float rootScale = Math.Max(Math.Abs(rootBasis.Scale.X), Math.Abs(rootBasis.Scale.Z));
		float childScale = Math.Max(Math.Abs(childBasis.Scale.X), Math.Abs(childBasis.Scale.Z));
		return rootScale > 0.0001f ? childScale / rootScale : childScale;
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
	public bool IsPathingCapability(Entity entity, TerrainPathingFlags capability)
	{
		if (GameContext.World == null || !GameContext.World.IsAlive(entity) || !GameContext.World.Has<Realm.Ecs.Components.Movement.PathingFlags>(entity)) return false;
		int flags = GameContext.World.Get<Realm.Ecs.Components.Movement.PathingFlags>(entity).Value;
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
		GameContext.NetworkService.UpdateConnectionStatus(_multiplayerActive, IsServerActive());
		if (GameContext.NetworkService.IsConnectionLost && !IsServerActive() && LobbyManager.Instance != null && !LobbyManager.Instance.IsReconnecting)
		{
			LobbyManager.Instance.TriggerReconnect();
		}
	}

	private void ProcessGameplayTick(float fDelta)
	{
		if (ECSContext.IsGameOver) return;

		CapturePreviousStateSnapshots();

		using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.ProcessGameplayTick))
		{
			float actualIntervalMs = BeginTickTracking();

			_fDelta = fDelta;

			ProcessCoreSimulation(fDelta);
			ProcessVisualAndEnvironment(fDelta);
			ProcessMiscTicks(fDelta);

			if (_multiplayerActive && Multiplayer.IsServer())
				UpdateServerSnapshotTick(fDelta);

			if (GameContext.SessionState.IsTestMode)
				RunShadowClientVerification();

			EndTickTracking(actualIntervalMs);
		}
	}

	private float BeginTickTracking()
	{
		float actualIntervalMs = 0f;
		if (Multiplayer.MultiplayerPeer == null || Multiplayer.IsServer())
		{
			if (GameContext.PerformanceTrackingService.TrackerIntervalStopwatch.IsRunning)
			{
				actualIntervalMs = (float)GameContext.PerformanceTrackingService.TrackerIntervalStopwatch.Elapsed.TotalMilliseconds;
				GameContext.PerformanceTrackingService.TrackerIntervalStopwatch.Restart();
			}
			else
			{
				GameContext.PerformanceTrackingService.TrackerIntervalStopwatch.Start();
			}

			GameContext.PerformanceTrackingService.TrackerTickStopwatch.Restart();
		}

		return actualIntervalMs;
	}

	private int _populationRecomputeTick;

	private void ProcessCoreSimulation(float fDelta)
	{
		using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.ProcessCoreSimulation))
		{
			TickAuthoritativePopulation();
			bool isAuthoritative = !_multiplayerActive || IsServerActive();
			using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.SimulationTickEcs))
			{
				GameContext.SimulationService.TickEcs(fDelta, isAuthoritative);
			}
			using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.PollVfxQueue))
			{
				PollVFXQueue();
			}
			using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.TickConstructionSystem))
			{
				TickConstructionSystem(fDelta, isAuthoritative);
			}
		}
	}

	private void TickAuthoritativePopulation()
	{
		if (_multiplayerActive && !IsServerActive()) return;

		_populationRecomputeTick++;
		if (_populationRecomputeTick < 5) return;

		_populationRecomputeTick = 0;
		GameContext.ResourceEconomyService?.RecomputeCurrentPopulation();
	}

	private void ProcessVisualAndEnvironment(float fDelta)
	{
		using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.ProcessVisualAndEnvironment))
		{
			if (GameContext.World != null && GameContext.World.IsAlive(ECSContext.WorldEntity) && GameContext.World.Has<WorldState>(ECSContext.WorldEntity))
			{
				var state = GameContext.World.Get<WorldState>(ECSContext.WorldEntity);
				if (state.DayNightCycleEnabled && !GameContext.SessionState.IsMapEditorMode)
				{
					using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.UpdateDayNightVisuals))
					{
						float progress = state.TimeOfDayTimer / GameContext.EnvironmentService.TimeOfDayCycleDuration;
						UpdateDayNightVisuals(progress);
					}
				}
			}

			if (GameContext.EnvironmentService != null && GameContext.EnvironmentService.IsTransitioning)
			{
				using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.EnvironmentUpdateTransition))
				{
					GameContext.EnvironmentService.UpdateTransition(this, fDelta);
				}
			}

			using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.UpdateMinimapPings))
			{
				UpdateMinimapPings(fDelta);
			}
		}
	}

	private void ProcessMiscTicks(float fDelta)
	{
		using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.ProcessMiscTicks))
		{
			using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.TickScheduledTimers))
			{
				TickScheduledTimers(fDelta);
			}
			using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.TickZoneTriggers))
			{
				TickZoneTriggers();
			}
			using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.TickGroundLoot))
			{
				TickGroundLoot(fDelta);
			}

			if (_activeMapScript != null)
			{
				using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.WasmRuntimeUpdate))
				{
					try
					{
						_activeMapScript.Update(this, fDelta);
					}
					catch (Exception ex)
					{
						GD.PrintErr($"[GameHost] Exception during active map script Update: {ex.Message}");
					}
				}
			}

			using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.UpdateBuildingPreview))
			{
				UpdateBuildingPreview();
			}

			if (!ReplayPlaybackManager.Instance.IsPlayingReplay && GameSettings.RecordReplays && GameContext.ReplayService != null && GameContext.ReplayService.IsRecording)
			{
				using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.RecordGameplayTick))
				{
					RecordGameplayTick();
				}
			}
		}
	}

	private void EndTickTracking(float actualIntervalMs)
	{
		if (Multiplayer.MultiplayerPeer != null && !Multiplayer.IsServer()) return;

		GameContext.PerformanceTrackingService.TrackerTickStopwatch.Stop();
		float tickCpuMs = (float)GameContext.PerformanceTrackingService.TrackerTickStopwatch.Elapsed.TotalMilliseconds;
		float tickDelay = actualIntervalMs > 33.33f ? actualIntervalMs - 33.33f : 0f;

		float adjustedTickMs = tickCpuMs + tickDelay;
		if (GameContext.PerformanceTrackingService.TrackerTickDurations != null) GameContext.PerformanceTrackingService.TrackerTickDurations.Add(adjustedTickMs);

		GameContext.PerformanceTrackingService.TrackerLastTickDelay = tickDelay;
	}

	private void PollVFXQueue()
	{
		if (GameContext.World == null || !GameContext.World.IsAlive(ECSContext.WorldEntity) || !GameContext.World.Has<VFXQueue>(ECSContext.WorldEntity)) return;

		ref var queue = ref GameContext.World.Get<VFXQueue>(ECSContext.WorldEntity);
		if (queue.Count == 0) return;

		for (int i = 0; i < queue.Count; i++)
		{
			ProcessVFXRequest(queue.Get(i));
		}

		queue.Clear();
	}

	private void ProcessVFXRequest(VFXRequest req)
	{
		var godotPos = new Vector3(req.Position.X, req.Position.Y, req.Position.Z);
		var godotTarget = new Vector3(req.TargetPosition.X, req.TargetPosition.Y, req.TargetPosition.Z);

		if (TryProcessAbilityVfx(req, godotPos)) return;
		if (TryProcessProjectileVfx(req, godotPos, godotTarget)) return;

		ProcessMiscVfx(req, godotPos, godotTarget);
	}

	private bool TryProcessAbilityVfx(VFXRequest req, Vector3 godotPos)
	{
		var def = GetAbilityDefinition(req.EffectTypeId);
		if (def == null) return false;

		GameContext.FXService.SpawnAbilityEffect(this, def, godotPos);
		return true;
	}

	private bool TryProcessProjectileVfx(VFXRequest req, Vector3 godotPos, Vector3 godotTarget)
	{
		if (req.EffectTypeId != "arrow" && !WeaponRegistry.ContainsKey(req.EffectTypeId) && !req.EffectTypeId.StartsWith("proj:")) return false;

		string weaponId = req.EffectTypeId.StartsWith("proj:") ? req.EffectTypeId.Substring(5) : req.EffectTypeId;
		SpawnWeaponProjectile(godotPos, godotTarget, weaponId, GetEntityById(req.EntityId), req.OwnerPlayerIndex);
		return true;
	}

	private void ProcessMiscVfx(VFXRequest req, Vector3 godotPos, Vector3 godotTarget)
	{
		if (req.EffectTypeId == "heal") SpawnHealVisualEffect(godotPos, godotTarget);
		else if (req.EffectTypeId == "target_indicator") SpawnTargetIndicator(godotPos, new Color(req.Scale, 0.3f, 0.1f));
		else if (req.EffectTypeId == "damage_flash" || req.EffectTypeId == "heal_flash") TryProcessUnitFlash(req);
	}

	private void TryProcessUnitFlash(VFXRequest req)
	{
		if (req.EntityId == -1) return;
		if (!EntityIdToUnit3D.TryGetValue(req.EntityId, out var unit) || !GodotObject.IsInstanceValid(unit)) return;

		if (req.EffectTypeId == "damage_flash") GameContext.FXService.FlashDamageUnit(unit);
		else GameContext.FXService.FlashHealUnit(unit);
	}

	private Entity GetEntityById(int entityId)
	{
		if (entityId == -1) return Entity.Null;
		return EntityIdToUnit3D.TryGetValue(entityId, out var unit) && GodotObject.IsInstanceValid(unit) ? unit.Entity : Entity.Null;
	}


	private void UpdateMinimapPings(float fDelta)
	{
		for (int i = GameContext.FXService.ActivePings.Count - 1; i >= 0; i--)
		{
			var ping = GameContext.FXService.ActivePings[i];
			ping.LifeTime += fDelta;
			if (ping.LifeTime >= ping.MaxLifeTime)
			{
				GameContext.FXService.ActivePings.RemoveAt(i);
			}
			else
			{
				GameContext.FXService.ActivePings[i] = ping;
			}
		}
	}

	private void UpdateDayNightVisuals(float progress)
	{
		GameContext.EnvironmentService?.UpdateDayNightVisuals(this, progress);
	}

	public (int TimeOfDayIndex, float TimeOfDayTimer) SetTimeOfDay(int timeOfDayIndex)
	{
		return GameContext.EnvironmentService?.SetTimeOfDay(this, ECSContext.WorldEntity, timeOfDayIndex, GameContext.EnvironmentService.TimeOfDayCycleDuration) ?? (0, 0f);
	}

	public (int TimeOfDayIndex, float TimeOfDayTimer) CycleTimeOfDay()
	{
		return GameContext.EnvironmentService?.CycleTimeOfDay(this, ECSContext.WorldEntity, GameContext.EnvironmentService.TimeOfDayCycleDuration) ?? (0, 0f);
	}

	public string GetTimeOfDayName()
	{
		return GameContext.EnvironmentService?.GetTimeOfDayName(TimeOfDayIndex) ?? "Unknown";
	}

	private void SpawnSpritesheetEffect(string texturePath, Vector3 worldPosition, int columns, int rows, float secondsPerFrame, float sizeInWorldUnits)
	{
		GameContext.FXService.SpawnSpritesheetEffect(this, texturePath, worldPosition, columns, rows, secondsPerFrame, sizeInWorldUnits);
	}

	private void FlashDamageUnit(Unit3D unit)
	{
		GameContext.FXService.FlashDamageUnit(unit);
	}

	private void SpawnHealVisualEffect(Vector3 start, Vector3 target)
	{
		GameContext.FXService.SpawnHealVisualEffect(this, start, target);
	}

	private void FlashHealUnit(Unit3D unit)
	{
		GameContext.FXService.FlashHealUnit(unit);
	}

	private void SpawnPing3DEffect(Vector3 position)
	{
		GameContext.FXService.SpawnPing3DEffect(this, position);
	}

	public void SpawnArrowProjectileForReplay(System.Numerics.Vector3 start, System.Numerics.Vector3 target)
	{
		GameContext.FXService.SpawnArrowProjectile(this, new Vector3(start.X, start.Y, start.Z), new Vector3(target.X, target.Y, target.Z));
	}

	public void SpawnWeaponProjectileForReplay(System.Numerics.Vector3 start, System.Numerics.Vector3 target, string? weaponId = null)
	{
		GameContext.FXService.SpawnWeaponProjectile(this, new Vector3(start.X, start.Y, start.Z), new Vector3(target.X, target.Y, target.Z), weaponId);
	}

	private void SpawnArrowProjectile(Vector3 start, Vector3 target)
	{
		SpawnWeaponProjectile(start, target, "arrow");
	}

	public void SpawnWeaponProjectile(Vector3 start, Vector3 target, string? weaponId = null, Entity targetEntity = default, int ownerPlayerIndex = 0)
	{
		GameContext.FXService.SpawnWeaponProjectile(this, start, target, weaponId, targetEntity, ownerPlayerIndex);
		if (GameContext.ReplayService != null && GameContext.ReplayService.IsRecording)
		{
			GameContext.ReplayService.RecordProjectile(weaponId ?? "arrow", new System.Numerics.Vector3(start.X, start.Y, start.Z), new System.Numerics.Vector3(target.X, target.Y, target.Z));
		}

		if (_multiplayerActive && IsServerActive())
		{
			Rpc(nameof(ClientSpawnArrowProjectile), start, target, ownerPlayerIndex);
		}
	}

	private void SpawnTargetIndicator(Vector3 position, Color color)
	{
		GameContext.FXService.SpawnTargetIndicator(this, position, color);
	}

	public void AddMinimapPing(Vector3 position)
	{
		GameContext.FXService.AddMinimapPing(this, GameContext.FXService.ActivePings, position);
	}
}
