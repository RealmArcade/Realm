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

	private bool _buildingPlacementNeedsBuilder = true;

	private RuntimeTerrain? _groundTerrain { get; set; }

	public RuntimeTerrain? GroundTerrain
	{
		get
		{
			if (_groundTerrain == null && (GameContext.SessionState.IsMapEditorMode || GameContext.SaveLoadService.IsLoadingMap || (GameContext.Lobby != null && GameContext.Lobby.IsGameStarted)))
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
				GameContext.EditorService.Surface.AttachSplatMaps(value.SplatMap, value.CliffSplatMap);
			}
		}
	}

	/// <summary>
	/// The ground terrain for paths that cannot run without it. Call sites that can cope with a
	/// session that has no ground read the nullable <see cref="GroundTerrain"/> instead.
	/// </summary>
	public RuntimeTerrain RequireGroundTerrain()
	{
		return GroundTerrain ?? throw new InvalidOperationException("The current session has no ground terrain.");
	}

	private MapEditorPaintCursor? _paintCursor { get; set; }
	private bool _paintCursorSuppressed { get; set; }
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
		get => GameContext.EditorService.Tools.ActiveTool;
		set
		{
			GameContext.EditorService.FlushTerrainMeshAndPhysics();
			GameContext.EditorService.Tools.ActiveTool = value;
			GameContext.EditorService?.Interaction.SetIsPastingObject(false);
			if (value != EditorTool.SelectArea && value != EditorTool.PasteArea)
			{
				HideSelectionHighlight();
			}

			RebuildAllCoordinatePersistentMeshes();
			UpdatePathingOverlay();
		}
	}

	public string ActivePlaceId { get; set; } = "";

	public string GetTerrainStatusString(Vector3 hitPos)
	{
		return GameContext.EditorService.GetTerrainStatusString(hitPos, ActiveEditorTool, ActivePlaceId);
	}

	public void LoadMapProperties(string path)
	{
		ApplyTestRandomSeedIfNeeded(path);
		GameContext.MapPropertiesLoader?.LoadMapProperties(GameContext.Simulation.WorldEntity, path);
		_defaultCameraBoundsCaptured = false;
		CaptureDefaultCameraBounds();
	}

	public float EditorBrushRadius
	{
		get => GameContext.EditorService.Tools.BrushRadius;
		set => GameContext.EditorService.Tools.BrushRadius = Mathf.Clamp(value, EditorToolSettings.MinBrushRadius, EditorToolSettings.MaxBrushRadius);
	}

	public float EditorBrushStrength
	{
		get => GameContext.EditorService.Tools.BrushStrength;
		set => GameContext.EditorService.Tools.BrushStrength = Mathf.Clamp(value, EditorToolSettings.MinBrushStrength, EditorToolSettings.MaxBrushStrength);
	}

	public bool EditorSnapToGrid { get; set; } = false;

	public float EditorPlacementScale
	{
		get => GameContext.EditorService.Tools.PlacementScale;
		set => GameContext.EditorService.Tools.PlacementScale = Mathf.Clamp(value, EditorToolSettings.MinPlacementScale, EditorToolSettings.MaxPlacementScale);
	}

	public bool EditorGridVisible => GameContext.EditorService.Tools.GridMode == GridOverlayMode.Grid || GameContext.EditorService.Tools.GridMode == GridOverlayMode.Both;

	public float EditorCameraBoundsLeft
	{
		get => EditorEntityState.GetCameraBoundsLeft(GameContext.Simulation.WorldEntity);
		set => EditorEntityState.SetCameraBoundsLeft(GameContext.Simulation.WorldEntity, value);
	}

	public float EditorCameraBoundsRight
	{
		get => EditorEntityState.GetCameraBoundsRight(GameContext.Simulation.WorldEntity);
		set => EditorEntityState.SetCameraBoundsRight(GameContext.Simulation.WorldEntity, value);
	}

	public float EditorCameraBoundsTop
	{
		get => EditorEntityState.GetCameraBoundsTop(GameContext.Simulation.WorldEntity);
		set => EditorEntityState.SetCameraBoundsTop(GameContext.Simulation.WorldEntity, value);
	}

	public float EditorCameraBoundsBottom
	{
		get => EditorEntityState.GetCameraBoundsBottom(GameContext.Simulation.WorldEntity);
		set => EditorEntityState.SetCameraBoundsBottom(GameContext.Simulation.WorldEntity, value);
	}

	public MirrorMode EditorMirrorMode
	{
		get => GameContext.World?.GetFieldOrDefault<EditorState, MirrorMode>(GameContext.Simulation.WorldEntity, s => s.MirrorMode, MirrorMode.None) ?? MirrorMode.None;
		set
		{
			GameContext.World?.Mutate<EditorState>(GameContext.Simulation.WorldEntity, (ref EditorState s) => s.MirrorMode = value);
			if (value == MirrorMode.Rotational)
			{
				GameContext.EditorService.Tools.PolarRadialStep = 360.0f / Mathf.Max(1, EditorSymmetryFolds);
				GroundTerrain?.SetPolarRadialStep(GameContext.EditorService.Tools.PolarRadialStep);
			}
			else
			{
				GameContext.EditorService.Tools.PolarRadialStep = 360.0f / Mathf.Max(1, GameContext.EditorService.Tools.PolarSpokeFolds);
				GroundTerrain?.SetPolarRadialStep(GameContext.EditorService.Tools.PolarRadialStep);
			}

			UpdateGridOverlayVisibility();
		}
	}

	public Vector2 EditorSymmetryPivot
	{
		get => GameContext.EditorService?.Symmetry.Pivot ?? Vector2.Zero;
		set
		{
			if (GameContext.EditorService != null) GameContext.EditorService.Symmetry.Pivot = value;
			UpdateSymmetryPivotVisuals();
		}
	}

	public int EditorSymmetryFolds
	{
		get => GameContext.EditorService?.Symmetry.Folds ?? 4;
		set
		{
			if (GameContext.EditorService != null) GameContext.EditorService.Symmetry.Folds = value;
			if (EditorMirrorMode == MirrorMode.Rotational)
			{
				var editor = GameContext.EditorService;
				if (editor == null) return;
				editor.Tools.PolarRadialStep = 360.0f / Mathf.Max(1, value);
				GroundTerrain?.SetPolarRadialStep(editor.Tools.PolarRadialStep);
			}
		}
	}

	public Vector3? EditorTapeMeasureEnd { get; set; }
	public bool EditorTapeMeasureActive { get; set; } = false;

	public bool EditorBrushIsSquare { get; set; } = true;

	public float EditorClumpCount
	{
		get => GameContext.EditorService.Tools.ClumpCount;
		set => GameContext.EditorService.Tools.ClumpCount = Mathf.Clamp(value, EditorToolSettings.MinClumpCount, EditorToolSettings.MaxClumpCount);
	}

	public float EditorClumpScale
	{
		get => GameContext.EditorService.Tools.ClumpScale;
		set => GameContext.EditorService.Tools.ClumpScale = Mathf.Clamp(value, EditorToolSettings.MinClumpScale, EditorToolSettings.MaxClumpScale);
	}

	public bool EditorClumpMode
	{
		get => GameContext.EditorService.Tools.ClumpMode;
		set
		{
			GameContext.EditorService.Tools.ClumpMode = value;
			UpdateBrushMesh();
		}
	}

	public string EditorSkyboxPath
	{
		get => EditorEntityState.GetSkyboxPath(GameContext.Simulation.WorldEntity);
		set => EditorEntityState.SetSkyboxPath(GameContext.Simulation.WorldEntity, value);
	}

	public bool EditorHasUnsavedChanges
	{
		get => EditorEntityState.GetHasUnsavedChanges(GameContext.Simulation.WorldEntity);
		set => EditorEntityState.SetHasUnsavedChanges(GameContext.Simulation.WorldEntity, value);
	}

	public bool EditorBlockMode
	{
		get => EditorEntityState.GetBlockMode(GameContext.Simulation.WorldEntity);
		set => EditorEntityState.SetBlockMode(GameContext.Simulation.WorldEntity, value);
	}

	public float EditorBlockLevelHeight
	{
		get => EditorEntityState.GetBlockLevelHeight(GameContext.Simulation.WorldEntity);
		set => EditorEntityState.SetBlockLevelHeight(GameContext.Simulation.WorldEntity, value);
	}

	public float EditorExactHeight
	{
		get => GameContext.EditorService.Tools.ExactHeight;
		set => GameContext.EditorService.Tools.ExactHeight = Math.Clamp(value, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
	}

	public WaterType EditorWaterMode
	{
		get => EditorEntityState.GetWaterMode(GameContext.Simulation.WorldEntity);
		set => EditorEntityState.SetWaterMode(GameContext.Simulation.WorldEntity, value);
	}

	public byte ActiveWaterProfileIndex
	{
		get => EditorEntityState.GetWaterProfileIndex(GameContext.Simulation.WorldEntity);
		set => EditorEntityState.SetWaterProfileIndex(GameContext.Simulation.WorldEntity, value);
	}

	private Node? _hoveredEditorObject { get; set; }
	private MeshInstance3D? _selectionHighlightMesh { get; set; }
	private MeshInstance3D? _coordinatePreviewMesh { get; set; }
	private MeshInstance3D? _coordinateSelectionOutlineMesh { get; set; }
	private MeshInstance3D? _objectMarqueeFillMesh { get; set; }
	private MeshInstance3D? _objectMarqueeOutlineMesh { get; set; }
	private List<MeshInstance3D> _coordinatePersistentMeshes { get; set; } = new();
	private List<MeshInstance3D> _spawnAnchorMeshes { get; set; } = new();

	public void GenerateNewRandomPlacementRotationAndScale()
	{
		GameContext.EditorService.Interaction.GenerateNewRandomPlacementRotationAndScale();
	}

	public bool PasteOptionPathing
	{
		get => GameContext.EditorService.Tools.PastePathing;
		set
		{
			GameContext.EditorService.Tools.PastePathing = value;
			UpdatePathingOverlay();
		}
	}

	public Node? SelectedEditorObject
	{
		get => _selectedEditorObject;
		set
		{
			if (value == null)
			{
				if (_selectedEditorObject == null && _editorObjectSelection.Count == 0) return;
				SetEditorObjectSelection(null);
				return;
			}

			if (GodotObject.IsInstanceValid(value)
				&& _editorObjectSelection.Count == 1
				&& _editorObjectSelection[0] == value)
			{
				return;
			}

			SetEditorObjectSelection(new[] { value });
		}
	}

	public int SelectedEditorObjectCount => _editorObjectSelection.Count;

	public void SetEditorObjectSelection(IEnumerable<Node>? nodes)
	{
		ClearEditorSelectionVisuals();
		_editorObjectSelection.Clear();

		if (nodes != null)
		{
			foreach (Node node in nodes)
			{
				if (node == null || !GodotObject.IsInstanceValid(node)) continue;
				if (_editorObjectSelection.Contains(node)) continue;
				_editorObjectSelection.Add(node);
			}
		}

		_selectedEditorObject = _editorObjectSelection.Count > 0 ? _editorObjectSelection[^1] : null;
		ApplyEditorSelectionVisuals();

		GameContext.MapEditorHud?.UpdateSelectedObjectInfo();
		UpdateEditorCoverageOverlay();
	}

	public void AddEditorObjectToSelection(Node node)
	{
		if (node == null || !GodotObject.IsInstanceValid(node) || IsEditorObjectSelected(node)) return;

		_editorObjectSelection.Add(node);

		ClearSelectedPropEntity();
		ApplyEditorSelectionVisual(node, true);
		_selectedEditorObject = node;

		GameContext.MapEditorHud?.UpdateSelectedObjectInfo();
		UpdateEditorCoverageOverlay();
	}

	public void RemoveEditorObjectFromSelection(Node node)
	{
		if (node == null || !IsEditorObjectSelected(node)) return;

		ApplyEditorSelectionVisual(node, false);
		_editorObjectSelection.Remove(node);
		ClearSelectedPropEntity();

		_selectedEditorObject = _editorObjectSelection.Count > 0 ? _editorObjectSelection[^1] : null;
		ApplyEditorSelectionVisuals();

		GameContext.MapEditorHud?.UpdateSelectedObjectInfo();
		UpdateEditorCoverageOverlay();
	}

	public bool IsEditorObjectSelected(Node node) => node != null && _editorObjectSelection.Contains(node);

	public void ToggleEditorObjectSelection(Node node)
	{
		if (node == null) return;

		if (IsEditorObjectSelected(node))
		{
			RemoveEditorObjectFromSelection(node);
		}
		else
		{
			AddEditorObjectToSelection(node);
		}
	}

	public void ClearEditorObjectSelection()
	{
		SetEditorObjectSelection(null);
	}

	private void ClearEditorSelectionVisuals()
	{
		for (int i = 0; i < _editorObjectSelection.Count; i++)
		{
			ApplyEditorSelectionVisual(_editorObjectSelection[i], false);
		}

		ClearSelectedPropEntity();
	}

	private void ApplyEditorSelectionVisuals()
	{
		var selection = _editorObjectSelection;
		if (selection.Count == 0) return;

		if (selection.Count == 1)
		{
			Node primary = selection[0];
			if (!GodotObject.IsInstanceValid(primary)) return;

			if (primary is PropEntityProxyNode proxy)
			{
				SetSelectedPropEntity(proxy.Entity, proxy.PropId);
				return;
			}

			if (primary is Prop3D prop && primary is not Unit3D)
			{
				SetSelectedPropEntity(prop.Entity, prop.PropId);
				return;
			}

			ApplyEditorSelectionVisual(primary, true);
			return;
		}

		for (int i = 0; i < selection.Count; i++)
		{
			ApplyEditorSelectionVisual(selection[i], true);
		}
	}

	private void ApplyEditorSelectionVisual(Node node, bool selected)
	{
		if (node == null || !GodotObject.IsInstanceValid(node)) return;

		if (node is Unit3D unit)
		{
			unit.IsSelected = selected;
			return;
		}

		if (node is Prop3D prop)
		{
			prop.IsSelected = selected;
			return;
		}

		if ((node as Decal ?? FindDecalInParentChain(node)) is Decal decal)
		{
			UpdateDecalSelectionRing(decal, selected);
			return;
		}

		if (node is ProceduralVfxInstance3D vfx)
		{
			vfx.IsSelected = selected;
		}
	}

	private Node? _selectedEditorObject { get; set; }
	private readonly List<Node> _editorObjectSelection = new();
	private bool _isDraggingObject { get; set; }
	private bool _isDraggingSelectionGroup { get; set; }
	private Vector3 _dragObjectStartPos { get; set; }
	private Vector3 _dragObjectStartRot { get; set; }
	private Vector3 _dragObjectStartScale { get; set; }
	private readonly List<(Node3D Node, Vector3 Position, Vector3 Rotation, Vector3 Scale)> _dragSelectionStartTransforms = new();

	private Node3D? _editorPreviewNode { get; set; }

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

}
