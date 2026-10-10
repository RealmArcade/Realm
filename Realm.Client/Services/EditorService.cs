using Arch.Core;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.EditorAPI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Realm.Client.Services;

public class EditorService : IEditorAPI
{
    public Vector3 LastRaycastPos { get; set; } = new Vector3(float.MinValue, float.MinValue, float.MinValue);
	public static readonly float[] PolarRingSpacingOptions = new[] { 4.0f, 8.0f, 16.0f, 32.0f };
	public bool IsSyncing { get; set; } = false;
	public bool IsMapEditorMode { get; set; }

    public bool IsTestMode { get; set; } = false;
    public bool ReturningFromTest { get; set; } = false;
    public Vector3 SavedCameraPosition { get; set; }
    public float SavedTargetHeight { get; set; }
    public float SavedCurrentHeight { get; set; }
    public float SavedTargetYaw { get; set; }
    public float SavedCurrentYaw { get; set; }
    public float SavedTargetPitch { get; set; }
    public int ScaleDialogTargetWidth { get; set; }
    public float SavedCurrentPitch { get; set; }
    public bool SavedIsTopDown { get; set; }
    public float SavedYawSwing { get; set; }
    public float SavedPitchSwing { get; set; }
    public Realm.Client.Core.GameHost.GridOverlayMode SavedGridMode { get; set; } = Realm.Client.Core.GameHost.GridOverlayMode.Off;

	public double AutoBackupElapsedSeconds { get; set; } = 0;
	public string TempWorkspaceGodotPath { get; set; } = Realm.Client.Services.MapWorkspaceService.DefaultWorkspaceGodotPath;
	public string _tempWorkspacePath { get; set; } = Realm.Client.Services.MapWorkspaceService.GetDefaultWorkspaceGlobalPath();
	
	public int LastSelectionMinX { get; set; } = -1;
	public int LastSelectionMinZ { get; set; } = -1;
	public int LastSelectionMaxX { get; set; } = -1;
	public int LastSelectionMaxZ { get; set; } = -1;
	public bool LastSelectionBrushIsSquare { get; set; } = true;

	public int CurrentPasteAnchorIndex { get; set; } = 0;
	public static readonly string[] PasteAnchorNames = new string[]
	{
		"CENTER",
		"TOP-LEFT",
		"TOP-RIGHT",
		"BOTTOM-RIGHT",
		"BOTTOM-LEFT"
	};

	private readonly WorldAccessor EcsWorldAccessor;
	private World EcsWorld => EcsWorldAccessor.Current;
	public Rect2I? TerrainFlushRegion { get; set; }
	public float TerrainMeshRebuildPeriodMs { get; set; } = 33.3f;
	public System.Collections.Generic.Dictionary<string, Realm.Client.Core.GameHost.DecalAssetData> DecalAssetCache { get; set; } = new();
	public System.Collections.Generic.Dictionary<(int, int), Godot.ImageTexture> DecalOrmCache { get; set; } = new();
	public System.Collections.Generic.Dictionary<string, Godot.ImageTexture> DecalNormalCache { get; set; } = new();
	public List<Realm.Client.Core.GameHost.EditorCoordinate> EditorCoordinates { get; set; } = new();
	public int PendingCoordinateMinX { get; set; }
	public int PendingCoordinateMinZ { get; set; }
	public int PendingCoordinateMaxX { get; set; }
	public int PendingCoordinateMaxZ { get; set; }
	public bool ModelYOffsetSavePending { get; set; } = false;
	public bool ModelCollisionCircleSavePending { get; set; } = false;
	public readonly Dictionary<string, string> NormalizedAssetKeyCache = new(StringComparer.OrdinalIgnoreCase);


	public float EditorClumpCount { get; set; } = 5.0f;
	public float EditorClumpScale { get; set; } = 0.3f;
	public bool EditorClumpMode { get; set; } = false;
	public bool EditorRandomRotation { get; set; } = false;
	public bool EditorRandomScale { get; set; } = false;
	public float EditorExactHeight { get; set; } = 0.0f;
	public float EditorWaterHeight { get; set; } = 0.9f;
	public string EditorPreviewType { get; set; } = "";

	public float EditorPlacementRotation { get; set; } = 0.0f;
	public float EditorPlacementScale { get; set; } = 1.0f;
	public Realm.Client.Core.GameHost.GridOverlayMode EditorGridMode { get; set; } = Realm.Client.Core.GameHost.GridOverlayMode.Off;
	public bool EditorCameraBoundsVisible { get; set; } = false;
	public bool EditorDisableShadows { get; set; } = false;
	public int EditorPolarSpokeFolds { get; set; } = 4;
	public bool EditorPolarOverlayVisible { get; set; } = false;
	public float EditorPolarRingSpacing { get; set; } = 8.0f;
	public float EditorPolarRadialStep { get; set; } = 90.0f;
	public Godot.Vector3? EditorTapeMeasureStart { get; set; }

	public bool SavedDisableShadows { get; set; } = false;
	public string SavedEntityCategory { get; set; } = "";
	public bool IsWaterRemoveAction { get; set; } = false;
	public float SavedBrushRadius { get; set; } = 2f;
	public float SavedBrushStrength { get; set; } = 0.5f;
	public Realm.Client.Core.GameHost.EditorTool SavedActiveTool { get; set; } = Realm.Client.Core.GameHost.EditorTool.Raise;
	public Realm.Client.Core.GameHost.EditorTool ActiveEditorTool { get; set; } = Realm.Client.Core.GameHost.EditorTool.None;
	public string SavedActivePlaceId { get; set; } = "";
	public bool SavedCameraBoundsVisible { get; set; } = false;
	public float SavedTextureIntensity { get; set; } = 10f;
	public string? LastUsedFolder { get; set; } = null;
	public string? CurrentSourceFolder { get; set; } = null;
	public string? PendingCasSourceDirectory { get; set; } = null;
	public string? PendingDefaultSaveFolder { get; set; } = null;

	public const float MIN_BRUSH_RADIUS = 1.0f;
	public const float MAX_BRUSH_RADIUS = 20.0f;
	public const float MIN_BRUSH_STRENGTH = 0.0f;
	public const float MAX_BRUSH_STRENGTH = 10.0f;
	public const float MIN_PLACEMENT_SCALE = 0.25f;
	public const float MAX_PLACEMENT_SCALE = 5.0f;

	public bool _lastBrushShapeIsSquare { get; set; }
	public bool _hasLastBrushShape { get; set; }
	public float _lastRotationExternal { get; set; } = float.NaN;
	public float _lastPasteRotationExternal { get; set; } = float.NaN;
	public PasteReflection _lastPasteReflectionExternal { get; set; } = (PasteReflection)(-1);
	public float _lastScaleExternal { get; set; } = float.NaN;
	public float _lastBrushSizeExternal { get; set; } = float.NaN;

	public static readonly System.Collections.Generic.Dictionary<Realm.Client.Core.GameHost.EditorTool, string> ToolInfoTexts = new()
	{
		{ Realm.Client.Core.GameHost.EditorTool.Raise, "TOOL: Raise Heights\n\nDrag left click on the map ground to elevate terrain. Adjust size and strength in settings." },
		{ Realm.Client.Core.GameHost.EditorTool.Ramp, "TOOL: Ramping\n\nLeft-click once on the terrain to set the Ramp Start Point. Left-click again to set the Ramp End Point. The tool will smoothly interpolate heights between the two points. Press Right-click or Escape to cancel." },
		{ Realm.Client.Core.GameHost.EditorTool.Lower, "TOOL: Lower Heights\n\nDrag left click on the map ground to depress terrain. Adjust size and strength in settings." },
		{ Realm.Client.Core.GameHost.EditorTool.Height, "TOOL: Exact Height\n\nDrag left click on the map ground to set terrain to exact block height. Adjust height in settings." },
		{ Realm.Client.Core.GameHost.EditorTool.Plateau, "TOOL: Plateau\n\nDrag left click to flatten terrain to the elevation of your initial click point." },
		{ Realm.Client.Core.GameHost.EditorTool.Smooth, "TOOL: Smooth Terrain\n\nDrag left click to average neighbor vertex heights and smooth out rugged elevations." },
		{ Realm.Client.Core.GameHost.EditorTool.PaintTexture, "TOOL: Texture Painting\n\nDrag left click to paint texture layers onto the vertices of the terrain mesh." },
		{ Realm.Client.Core.GameHost.EditorTool.FloodFill, "TOOL: Flood Fill\n\nClick once on the terrain map to flood-fill an area sharing the same texture color until hitting a boundary (cliff or different texture). Uses selected texture swatch." },
		{ Realm.Client.Core.GameHost.EditorTool.SelectArea, "TOOL: Area Select\n\nDrag left click to select a rectangular area of the map. Press Ctrl+C to copy the area." },
		{ Realm.Client.Core.GameHost.EditorTool.PasteArea, "TOOL: Area Paste\n\nClick on the terrain to paste the copied area. Use the Affected Layers checkboxes to filter what is pasted (Textures, HeightMap, Units / Props, Pathing)." },
		{ Realm.Client.Core.GameHost.EditorTool.PlaceDecal, "TOOL: Place Decal\n\nLeft-click on the ground to project a decorative decal. Snapping, scaling, and rotation apply." },
		{ Realm.Client.Core.GameHost.EditorTool.DeleteObject, "TOOL: Object Eraser\n\nLeft-click directly on any unit or prop in 3D scene to erase and remove it from the map." },
		{ Realm.Client.Core.GameHost.EditorTool.SelectMove, "TOOL: Select / Move\n\nLeft-click directly on any unit, prop, or decal to select it. Hold and drag left click to move it. Use Alt + MouseWheel to scale, or Delete to delete." },
		{ Realm.Client.Core.GameHost.EditorTool.Eyedropper, "TOOL: Eyedropper / Picker\n\nLeft-click directly on any unit, prop, or decal to copy and select it as the active placement tool. Click on terrain to copy its texture color, or hold Shift to copy its height." },
		{ Realm.Client.Core.GameHost.EditorTool.Noise, "TOOL: Roughen Terrain\n\nDrag left-click to apply random height variations/noise to ruggedize the terrain surface. Adjust size and strength in settings." },
		{ Realm.Client.Core.GameHost.EditorTool.Water, "TOOL: Water Flood Fill\n\nClick on terrain to flood-fill water bounded by cliff walls. Uses selected Water Profile or toggles water." },
		{ Realm.Client.Core.GameHost.EditorTool.PaintPathing, "TOOL: Pathing Layer Painting\n\nDrag left click to paint pathing properties (ground, flying, water, etc.) onto the map. Use checkboxes to select layers, and Mode to Add/Remove." },
		{ Realm.Client.Core.GameHost.EditorTool.FloodFillPathing, "TOOL: Flood Fill Pathing\n\nClick once on the terrain map to flood-fill pathing properties (ground, flying, water, etc.) across an area sharing the same texture color until hitting a boundary. Use checkboxes to select layers, and Mode to Add/Remove." },
		{ Realm.Client.Core.GameHost.EditorTool.Measure, "TOOL: Tape Measure\n\nClick on the terrain to set measurement origin. Move the cursor or click again to lock the endpoint. Displays live Euclidean distance, Manhattan distance, grid delta, and slope." },
		{ Realm.Client.Core.GameHost.EditorTool.None, "Select a tool from the panels to begin terrain modification." }
	};

	public static readonly int[] SpokeCountOptions = new[] { 2, 3, 4, 5, 6, 8, 12, 16 };

	public const float MIN_CLUMP_COUNT = 1.0f;
	public const float MAX_CLUMP_COUNT = 20.0f;
	public const float MIN_CLUMP_SCALE = 0.0f;
	public const float MAX_CLUMP_SCALE = 1.0f;
	public const float MAX_CLUMP_DENSITY = MAX_CLUMP_COUNT;


	private TerrainSplatWeights[,] _terrainSplatMap;
	private TerrainSplatWeights[,] _terrainCliffSplatMap;

	private float _clumpSpawnCooldown;
	private bool _isDrawingClump;
	private readonly List<IEditorAction> _clumpSpawnActionsInSession = new();
	private readonly List<(Vector3 Position, float Radius)> _cachedSessionPoints = new();

	private readonly Dictionary<Vector2I, long> _paintCoordLastTimeMs = new();

	public void ResetPaintThrottle()
	{
		_paintCoordLastTimeMs.Clear();
	}

	private bool ShouldPaintCoord(int x, int z, long nowMs)
	{
		Vector2I coord = new Vector2I(x, z);
		if (_paintCoordLastTimeMs.TryGetValue(coord, out long lastTime))
		{
			if (nowMs - lastTime < 250)
			{
				return false;
			}
		}
		_paintCoordLastTimeMs[coord] = nowMs;
		return true;
	}

	private bool _hasBlockTargetHeight;
	private float _activeBlockTargetHeight;
	private WaterType _activeBlockTargetWaterMode = WaterType.None;
	private byte _activeBlockTargetWaterProfile = 0;
	private float _activeBlockTargetWaterHeight = 0f;
	private float? _activePlateauHeight;
	private WaterType _activePlateauWaterMode = WaterType.None;
	private byte _activePlateauWaterProfile = 0;
	private float _activePlateauWaterHeight = 0f;

	private TerrainCell[,] _terrainCellsBefore;
	private TerrainSplatWeights[,] _terrainSplatMapBefore;
	private TerrainSplatWeights[,] _terrainCliffSplatMapBefore;
	private int[,] _terrainPathingBefore;
	private bool _isDrawingTerrain;
	private int _drawMinX;
	private int _drawMinZ;
	private int _drawMaxX;
	private int _drawMaxZ;

	private CopiedAreaTemplate? _copiedArea;

	private Vector2I? _selectionStart;
	private Vector2I? _selectionEnd;
	private bool _isSelectingArea;

	private float _cachedRandomRotation;
	private float _cachedRandomScale = 1.0f;
	private bool _hasCachedRandom;
	private bool _isPastingObject;

	private CopiedObjectTemplate? _copiedObject;
	private Vector3? _rampStartPos;

	public struct CopiedObjectTemplate
	{
		public string Type;
		public string Id;
		public float Rotation;
		public float Scale;
		public bool IsEnemy;
	}

	public class CopiedAreaTemplate
	{
		public int Width;
		public int Depth;
		public int AnchorTileX;
		public int AnchorTileZ;
		public int SourceMinX;
		public int SourceMinZ;
		public int SourceMaxX;
		public int SourceMaxZ;
		public TerrainCell[,] Cells;
		public TerrainSplatWeights[,] SplatMap;
		public TerrainSplatWeights[,] CliffSplatMap;
		public int[,] Pathing;
		public List<CopiedEntityInfo> Entities;
		public bool[,] Mask;
	}

	public class CopiedEntityInfo
	{
		public string Type;
		public string Id;
		public Vector3 RelativePos;
		public float Rotation;
		public float Scale;
		public bool IsEnemy;
	}

	public struct TerrainEditResult
	{
		public bool HeightsModified;
		public bool SplatModified;
		public bool PathingModified;
		public int MinX;
		public int MinZ;
		public int MaxX;
		public int MaxZ;
	}

	public struct PasteAreaResult
	{
		public bool TerrainModified;
		public bool HeightsModified;
		public bool PathingModified;
		public List<EntitySpawnRequest> SpawnRequests;
	}

	public struct EntitySpawnRequest
	{
		public string Type;
		public string Id;
		public Vector3 Position;
		public float Rotation;
		public float Scale;
		public bool IsEnemy;
	}

	public struct EraseAreaResult
	{
		public bool TerrainModified;
		public bool HeightsModified;
		public bool PathingModified;
		public List<Node3D> NodesToDelete;
	}

	public bool PasteOptionTextures { get; set; } = true;
	public bool PasteOptionHeights { get; set; } = true;
	public Vector3 DragObjectStartHitPos { get; set; }
	public Vector2 DragStartMousePos { get; set; }
	public Vector3 DragStartGroundPos { get; set; }
	public bool DragObjectHasMoved { get; set; }

	public EditorService(WorldAccessor ecsWorldAccessor)
	{
		EcsWorldAccessor = ecsWorldAccessor;
	}

	public void SetTerrainSplatMap(TerrainSplatWeights[,] splatMap, TerrainSplatWeights[,] cliffSplatMap = null)
	{
		_terrainSplatMap = splatMap;
		if (cliffSplatMap != null)
		{
			_terrainCliffSplatMap = cliffSplatMap;
		}
		else if (Realm.Client.Core.GameHost.Instance?.GroundTerrain?.CliffSplatMap != null)
		{
			_terrainCliffSplatMap = Realm.Client.Core.GameHost.Instance.GroundTerrain.CliffSplatMap;
		}
		if (splatMap != null)
		{
			AlignSplatMapSlots(0, 0, splatMap.GetLength(0) - 1, splatMap.GetLength(1) - 1);
		}
	}

	public bool IsPastingObject => _isPastingObject;
	public bool HasCachedRandom => _hasCachedRandom;
	public float CachedRandomRotation => _cachedRandomRotation;
	public float CachedRandomScale => _cachedRandomScale;
	public Vector2I? SelectionStart => _selectionStart;
	public Vector2I? SelectionEnd => _selectionEnd;
	public bool IsSelectingArea => _isSelectingArea;
	public CopiedAreaTemplate CopiedArea
	{
		get => _copiedArea;
		set => _copiedArea = value;
	}
	public bool HasCopiedArea => _copiedArea != null;
	public int CopiedAreaWidth => _copiedArea?.Width ?? 0;
	public int CopiedAreaDepth => _copiedArea?.Depth ?? 0;
	public int CopiedAreaAnchorX => _copiedArea?.AnchorTileX ?? 0;
	public int CopiedAreaAnchorZ => _copiedArea?.AnchorTileZ ?? 0;
	public int CopiedAreaSourceMinX => _copiedArea?.SourceMinX ?? 0;
	public int CopiedAreaSourceMinZ => _copiedArea?.SourceMinZ ?? 0;
	public int CopiedAreaSourceMaxX => _copiedArea?.SourceMaxX ?? 0;
	public int CopiedAreaSourceMaxZ => _copiedArea?.SourceMaxZ ?? 0;
	public bool HasCopiedAreaMask => _copiedArea?.Mask != null;
	public bool IsCopiedCellMasked(int srcX, int srcZ) => _copiedArea == null || _copiedArea.Mask == null || (srcX >= 0 && srcX < _copiedArea.Width && srcZ >= 0 && srcZ < _copiedArea.Depth && _copiedArea.Mask[srcX, srcZ]);

	public void SetCopiedAreaAnchor(int anchorTileX, int anchorTileZ)
	{
		if (_copiedArea != null)
		{
			_copiedArea.AnchorTileX = Mathf.Clamp(anchorTileX, 0, Math.Max(0, _copiedArea.Width - 1));
			_copiedArea.AnchorTileZ = Mathf.Clamp(anchorTileZ, 0, Math.Max(0, _copiedArea.Depth - 1));
		}
	}

	private Vector2 _symmetryPivot = Vector2.Zero;
	private int _symmetryFolds = 4;

	public Vector2 SymmetryPivot
	{
		get => _symmetryPivot;
		set => _symmetryPivot = value;
	}

	public int SymmetryFolds
	{
		get => _symmetryFolds;
		set => _symmetryFolds = Math.Clamp(value, 2, 32);
	}

	public Vector3? RampStartPos => _rampStartPos;
	public bool IsDrawingTerrain => _isDrawingTerrain;
	public bool IsDrawingClump => _isDrawingClump;

	public CopiedObjectTemplate? GetCopiedObject() => _copiedObject;

	public void SetCopiedObject(CopiedObjectTemplate? template)
	{
		_copiedObject = template;
	}

	public void SetIsPastingObject(bool value)
	{
		_isPastingObject = value;
	}

	public void SetRampStartPos(Vector3? pos)
	{
		_rampStartPos = pos;
	}

	public void SetSelectionStart(Vector2I? value)
	{
		_selectionStart = value;
	}

	public void SetSelectionEnd(Vector2I? value)
	{
		_selectionEnd = value;
	}

	public void SetIsSelectingArea(bool value)
	{
		_isSelectingArea = value;
	}

	public void GenerateNewRandomPlacementRotationAndScale()
	{
		_cachedRandomRotation = (float)(GD.Randf() * 360.0);
		_cachedRandomScale = 0.2f + (float)(GD.Randf() * 2.8);
		_hasCachedRandom = true;
	}

	public void InvalidateCachedRandom()
	{
		_hasCachedRandom = false;
	}

	private void SetGridNodeHeight(ref TerrainState terrain, int gx, int gz, float height)
	{
		var cells = terrain.Cells;
		if (cells == null) return;
		int w = terrain.Width;
		int d = terrain.Depth;
		if (gx > 0 && gz > 0 && gx - 1 < w && gz - 1 < d)
		{
			ref var c = ref cells[gx - 1, gz - 1];
			c.Y_SE = height;
		}
		if (gx < w && gz > 0 && gz - 1 < d)
		{
			ref var c = ref cells[gx, gz - 1];
			c.Y_SW = height;
		}
		if (gx > 0 && gz < d && gx - 1 < w)
		{
			ref var c = ref cells[gx - 1, gz];
			c.Y_NE = height;
		}
		if (gx < w && gz < d)
		{
			ref var c = ref cells[gx, gz];
			c.Y_NW = height;
		}
	}

	private float GetGridNodeHeight(in TerrainState terrain, int gx, int gz)
	{
		var cells = terrain.Cells;
		if (cells == null) return 0f;
		int w = terrain.Width;
		int d = terrain.Depth;
		int cellX = Math.Clamp(gx, 0, w - 1);
		int cellZ = Math.Clamp(gz, 0, d - 1);
		if (gx < w && gz < d) return cells[cellX, cellZ].Y_NW;
		if (gx == w && gz < d) return cells[w - 1, cellZ].Y_NE;
		if (gx < w && gz == d) return cells[cellX, d - 1].Y_SW;
		return cells[w - 1, d - 1].Y_SE;
	}

	public float GetTerrainHeightAt(Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		var cells = terrain.Cells;
		if (cells == null) return 0.0f;

		int cellW = cells.GetLength(0);
		int cellD = cells.GetLength(1);
		if (cellW <= 0 || cellD <= 0) return 0.0f;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		float fx = worldPos.X / quadSize + width / 2.0f;
		float fz = worldPos.Z / quadSize + depth / 2.0f;

		int x = Math.Clamp((int)Math.Floor(fx), 0, cellW - 1);
		int z = Math.Clamp((int)Math.Floor(fz), 0, cellD - 1);
		float tx = Math.Clamp(fx - x, 0f, 1f);
		float tz = Math.Clamp(fz - z, 0f, 1f);

		var cell = cells[x, z];
		float hNW = cell.Y_NW;
		float hNE = cell.Y_NE;
		float hSW = cell.Y_SW;
		float hSE = cell.Y_SE;

		return (1 - tx) * (1 - tz) * hNW + tx * (1 - tz) * hNE + (1 - tx) * tz * hSW + tx * tz * hSE;
	}

	public WaterType GetWaterModeAt(Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		var cells = terrain.Cells;
		if (cells == null) return WaterType.None;

		int cellW = cells.GetLength(0);
		int cellD = cells.GetLength(1);
		if (cellW <= 0 || cellD <= 0) return WaterType.None;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		int x = Math.Clamp((int)Math.Floor(worldPos.X / quadSize + width / 2.0f), 0, cellW - 1);
		int z = Math.Clamp((int)Math.Floor(worldPos.Z / quadSize + depth / 2.0f), 0, cellD - 1);

		return cells[x, z].WaterMode;
	}

	public byte GetWaterProfileIndexAt(Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		var cells = terrain.Cells;
		if (cells == null) return 0;

		int cellW = cells.GetLength(0);
		int cellD = cells.GetLength(1);
		if (cellW <= 0 || cellD <= 0) return 0;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		int x = Math.Clamp((int)Math.Floor(worldPos.X / quadSize + width / 2.0f), 0, cellW - 1);
		int z = Math.Clamp((int)Math.Floor(worldPos.Z / quadSize + depth / 2.0f), 0, cellD - 1);

		return cells[x, z].WaterProfileIndex;
	}

	public float GetWaterHeightAt(Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		var cells = terrain.Cells;
		if (cells == null) return 0f;

		int cellW = cells.GetLength(0);
		int cellD = cells.GetLength(1);
		if (cellW <= 0 || cellD <= 0) return 0f;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		int x = Math.Clamp((int)Math.Floor(worldPos.X / quadSize + width / 2.0f), 0, cellW - 1);
		int z = Math.Clamp((int)Math.Floor(worldPos.Z / quadSize + depth / 2.0f), 0, cellD - 1);

		return cells[x, z].WaterHeight;
	}

	public TerrainEditResult ApplyContinuousTerrainEditing(
		Vector3 worldPos,
		float delta,
		Realm.Client.Core.GameHost.EditorTool activeTool,
		float brushRadius,
		float brushStrength,
		bool brushIsSquare,
		bool blockMode,
		float blockLevelHeight,
		int paintTextureIndex,
		int cliffPaintTextureIndex,
		int pathingMask,
		bool pathingAdd,
		bool isFirstClick = false,
		bool applyGroundTexture = true,
		bool applyCliffTexture = true)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return default;

		if (_terrainSplatMap == null && Realm.Client.Core.GameHost.Instance?.GroundTerrain != null)
		{
			_terrainSplatMap = Realm.Client.Core.GameHost.Instance.GroundTerrain.SplatMap;
		}
		if (_terrainCliffSplatMap == null && Realm.Client.Core.GameHost.Instance?.GroundTerrain != null)
		{
			_terrainCliffSplatMap = Realm.Client.Core.GameHost.Instance.GroundTerrain.CliffSplatMap;
		}
		if (_terrainSplatMap == null) return default;

		bool isHeights = activeTool == Realm.Client.Core.GameHost.EditorTool.Raise ||
		                 activeTool == Realm.Client.Core.GameHost.EditorTool.Lower ||
		                 activeTool == Realm.Client.Core.GameHost.EditorTool.Height ||
		                 activeTool == Realm.Client.Core.GameHost.EditorTool.Smooth ||
		                 activeTool == Realm.Client.Core.GameHost.EditorTool.Plateau ||
		                 activeTool == Realm.Client.Core.GameHost.EditorTool.Noise;

		bool isPaint = activeTool == Realm.Client.Core.GameHost.EditorTool.PaintTexture;

		bool isPathing = activeTool == Realm.Client.Core.GameHost.EditorTool.PaintPathing;

		if (!isHeights && !isPaint && !isPathing) return default;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		bool modified = false;
		var result = new TerrainEditResult();
		
		int modMinX = width;
		int modMinZ = depth;
		int modMaxX = -1;
		int modMaxZ = -1;

		if (isFirstClick)
		{
			ResetPaintThrottle();
		}

		bool isBlock = (blockMode || activeTool == Realm.Client.Core.GameHost.EditorTool.Height) && activeTool != Realm.Client.Core.GameHost.EditorTool.Noise && activeTool != Realm.Client.Core.GameHost.EditorTool.Smooth;
		if (isBlock)
		{
			int cx = Mathf.Clamp((int)Math.Floor(worldPos.X / quadSize + width / 2.0f), 0, width - 1);
			int cz = Mathf.Clamp((int)Math.Floor(worldPos.Z / quadSize + depth / 2.0f), 0, depth - 1);
			int N = Mathf.Max(1, (int)Mathf.Round(brushRadius));

			int quadMinX = cx - (N - 1) / 2;
			int quadMaxX = cx + N / 2;

			int quadMinZ = cz - (N - 1) / 2;
			int quadMaxZ = cz + N / 2;

			if (isHeights)
			{
				if (!_hasBlockTargetHeight)
				{
					float startHeight = GetTerrainHeightAt(worldPos);
					WaterType startWater = GetWaterModeAt(worldPos);
					if (activeTool == Realm.Client.Core.GameHost.EditorTool.Height)
					{
						_activeBlockTargetHeight = Math.Clamp(blockLevelHeight, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
					}
					else if (activeTool == Realm.Client.Core.GameHost.EditorTool.Raise)
					{
						_activeBlockTargetHeight = Math.Clamp(startHeight + blockLevelHeight, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
					}
					else if (activeTool == Realm.Client.Core.GameHost.EditorTool.Lower)
					{
						_activeBlockTargetHeight = Math.Clamp(startHeight - blockLevelHeight, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
					}
					else if (activeTool == Realm.Client.Core.GameHost.EditorTool.Plateau)
					{
						_activeBlockTargetHeight = Math.Clamp((float)MathF.Round(startHeight / TerrainCell.TIER_HEIGHT) * TerrainCell.TIER_HEIGHT, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
						_activeBlockTargetWaterMode = startWater;
						_activeBlockTargetWaterProfile = GetWaterProfileIndexAt(worldPos);
						_activeBlockTargetWaterHeight = GetWaterHeightAt(worldPos);
					}
					_activeBlockTargetHeight = Math.Clamp(_activeBlockTargetHeight, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
					_hasBlockTargetHeight = true;
				}
				float targetHeight = Math.Clamp(_activeBlockTargetHeight, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
				sbyte targetMacroTier = (sbyte)Math.Clamp((int)MathF.Round(targetHeight / TerrainCell.TIER_HEIGHT), TerrainCell.MIN_MACRO_TIER, TerrainCell.MAX_MACRO_TIER);

				for (int z = quadMinZ; z <= quadMaxZ; z++)
				{
					for (int x = quadMinX; x <= quadMaxX; x++)
					{
						if (x >= 0 && x < width && z >= 0 && z < depth)
						{
							bool inBounds = true;
							if (!brushIsSquare)
							{
								float radius = N / 2.0f;
								float centerX = quadMinX + N / 2.0f;
								float centerZ = quadMinZ + N / 2.0f;
								float dx = (x + 0.5f) - centerX;
								float dz = (z + 0.5f) - centerZ;
								inBounds = (dx * dx + dz * dz) <= (radius * radius);
							}

							if (inBounds)
							{
								if (activeTool == Realm.Client.Core.GameHost.EditorTool.Raise ||
								    activeTool == Realm.Client.Core.GameHost.EditorTool.Lower ||
								    activeTool == Realm.Client.Core.GameHost.EditorTool.Height ||
								    activeTool == Realm.Client.Core.GameHost.EditorTool.Plateau)
								{
									ref var cell = ref terrain.Cells[x, z];
									float oldH = cell.CenterHeight;
									float newH = targetHeight;
									bool waterChanged = false;

									if (activeTool == Realm.Client.Core.GameHost.EditorTool.Raise || activeTool == Realm.Client.Core.GameHost.EditorTool.Height)
									{
										if (cell.WaterMode != WaterType.None)
										{
											cell.WaterMode = WaterType.None;
											cell.WaterHeight = 0f;
											waterChanged = true;
										}
									}
									else if (activeTool == Realm.Client.Core.GameHost.EditorTool.Plateau)
									{
										if (cell.WaterMode != _activeBlockTargetWaterMode || cell.WaterProfileIndex != _activeBlockTargetWaterProfile || cell.WaterHeight != _activeBlockTargetWaterHeight)
										{
											cell.WaterMode = _activeBlockTargetWaterMode;
											cell.WaterProfileIndex = _activeBlockTargetWaterProfile;
											cell.WaterHeight = _activeBlockTargetWaterHeight;
											waterChanged = true;
										}
									}

									if (MathF.Abs(cell.CenterHeight - targetHeight) > 0.001f || waterChanged)
									{
										if (terrain.PathingCodes != null)
										{
											if (cell.WaterMode != WaterType.None)
											{
												var waterProf = Realm.Client.RuntimeTerrain.Instance?.GetWaterProfile(cell.WaterProfileIndex);
												terrain.PathingCodes[x, z] = waterProf != null ? waterProf.DefaultPathingCode : Realm.Client.EditableTerrain.GetDefaultPathingCode(cell.WaterMode);
												result.PathingModified = true;
												float wY = (targetMacroTier * TerrainCell.TIER_HEIGHT) + (cell.WaterHeight > 0.001f ? cell.WaterHeight : Realm.Client.RuntimeTerrain.WATER_DELTA);
												SpawnWaterProceduralBombing(x, z, cell.WaterProfileIndex, wY, quadSize, width, depth);
											}
											else
											{
												ClearWaterProceduralObjects(x, z);
												int defaultPathBefore = Realm.Client.EditableTerrain.GetDefaultPathingCode(cell);
												if (terrain.PathingCodes[x, z] == defaultPathBefore)
												{
													terrain.PathingCodes[x, z] = Realm.Client.EditableTerrain.GetDefaultPathingCode(cell);
													result.PathingModified = true;
												}
											}
										}
										SetGridNodeHeight(ref terrain, x, z, targetHeight);
										SetGridNodeHeight(ref terrain, x + 1, z, targetHeight);
										SetGridNodeHeight(ref terrain, x + 1, z + 1, targetHeight);
										SetGridNodeHeight(ref terrain, x, z + 1, targetHeight);

										if (_terrainSplatMap == null && Realm.Client.Core.GameHost.Instance?.GroundTerrain != null)
										{
											_terrainSplatMap = Realm.Client.Core.GameHost.Instance.GroundTerrain.SplatMap;
										}
										if (_terrainCliffSplatMap == null && Realm.Client.Core.GameHost.Instance?.GroundTerrain != null)
										{
											_terrainCliffSplatMap = Realm.Client.Core.GameHost.Instance.GroundTerrain.CliffSplatMap;
										}

										if (applyGroundTexture && _terrainSplatMap != null)
										{
											int splatW = _terrainSplatMap.GetLength(0);
											int splatD = _terrainSplatMap.GetLength(1);
											for (int gz = z; gz <= z + 1; gz++)
											{
												for (int gx = x; gx <= x + 1; gx++)
												{
													if (gx >= 0 && gx < splatW && gz >= 0 && gz < splatD)
													{
														_terrainSplatMap[gx, gz] = TerrainSplatWeights.CreateSolid(paintTextureIndex);
													}
												}
											}

											string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
											if (!string.IsNullOrEmpty(wsPath) && MetadataService.Instance.TryLoadMetadata(wsPath, out var metaRoot) && metaRoot != null)
											{
												string? swatchName = null;
												if (Realm.Client.Core.GameHost.Instance?.GroundTerrain != null && paintTextureIndex >= 0 && paintTextureIndex < Realm.Client.Core.GameHost.Instance.GroundTerrain.LoadedTextureList.Count)
												{
													swatchName = Realm.Client.Core.GameHost.Instance.GroundTerrain.LoadedTextureList[paintTextureIndex];
												}
												if (!string.IsNullOrEmpty(swatchName))
												{
													var prof = metaRoot.GetTerrainProfile(swatchName);
													if (prof != null)
													{
														if (terrain.PathingCodes != null && x < width && z < depth)
														{
															terrain.PathingCodes[x, z] = prof.DefaultPathingCode;
															result.PathingModified = true;
														}
													}
												}
											}

											SpawnTerrainProceduralBombing(x, z, paintTextureIndex, targetHeight, quadSize, width, depth);
										}

										if (applyCliffTexture && _terrainCliffSplatMap != null)
										{
											int cliffW = _terrainCliffSplatMap.GetLength(0);
											int cliffD = _terrainCliffSplatMap.GetLength(1);
											for (int nz = z - 1; nz <= z + 2; nz++)
											{
												for (int nx = x - 1; nx <= x + 2; nx++)
												{
													if (nx >= 0 && nx < cliffW && nz >= 0 && nz < cliffD)
													{
														_terrainCliffSplatMap[nx, nz] = TerrainSplatWeights.CreateSolid(cliffPaintTextureIndex);
													}
												}
											}
										}

										int minXBound = Math.Max(0, x - 1);
										int maxXBound = Math.Min(width - 1, x + 1);
										int minZBound = Math.Max(0, z - 1);
										int maxZBound = Math.Min(depth - 1, z + 1);

										if (minXBound < modMinX) modMinX = minXBound;
										if (maxXBound > modMaxX) modMaxX = maxXBound;
										if (minZBound < modMinZ) modMinZ = minZBound;
										if (maxZBound > modMaxZ) modMaxZ = maxZBound;
										modified = true;
									}
								}
								else if (activeTool == Realm.Client.Core.GameHost.EditorTool.Smooth)
								{
									int sum = 0;
									int count = 0;
									for (int nz = -1; nz <= 1; nz++)
									{
										for (int nx = -1; nx <= 1; nx++)
										{
											int nxVal = x + nx;
											int nzVal = z + nz;
											if (nxVal >= 0 && nxVal < width && nzVal >= 0 && nzVal < depth)
											{
												sum += terrain.Cells[nxVal, nzVal].MacroTier;
												count++;
											}
										}
									}
									sbyte avgMacro = (sbyte)Math.Clamp((int)MathF.Round((float)sum / count), -16, 16);
									float targetH = avgMacro * TerrainCell.TIER_HEIGHT;
									if (MathF.Abs(terrain.Cells[x, z].CenterHeight - targetH) > 0.001f)
									{
										SetGridNodeHeight(ref terrain, x, z, targetH);
										SetGridNodeHeight(ref terrain, x + 1, z, targetH);
										SetGridNodeHeight(ref terrain, x + 1, z + 1, targetH);
										SetGridNodeHeight(ref terrain, x, z + 1, targetH);

										int minXBound = Math.Max(0, x - 1);
										int maxXBound = Math.Min(width - 1, x + 1);
										int minZBound = Math.Max(0, z - 1);
										int maxZBound = Math.Min(depth - 1, z + 1);

										if (minXBound < modMinX) modMinX = minXBound;
										if (maxXBound > modMaxX) modMaxX = maxXBound;
										if (minZBound < modMinZ) modMinZ = minZBound;
										if (maxZBound > modMaxZ) modMaxZ = maxZBound;
										modified = true;
									}
								}
							}
						}
					}
				}
			}
			else if (isPaint)
			{
				if (_terrainCliffSplatMap == null && Realm.Client.Core.GameHost.Instance?.GroundTerrain != null)
				{
					_terrainCliffSplatMap = Realm.Client.Core.GameHost.Instance.GroundTerrain.CliffSplatMap;
				}

				int splatW = _terrainSplatMap.GetLength(0);
				int splatD = _terrainSplatMap.GetLength(1);
				int intensityLevel = Math.Clamp((int)MathF.Round(brushStrength), 0, 10);

				int minPaintX = Math.Clamp((int)Math.Floor((worldPos.X - brushRadius) / quadSize + width / 2.0f), 0, splatW - 1);
				int maxPaintX = Math.Clamp((int)Math.Ceiling((worldPos.X + brushRadius) / quadSize + width / 2.0f), 0, splatW - 1);
				int minPaintZ = Math.Clamp((int)Math.Floor((worldPos.Z - brushRadius) / quadSize + depth / 2.0f), 0, splatD - 1);
				int maxPaintZ = Math.Clamp((int)Math.Ceiling((worldPos.Z + brushRadius) / quadSize + depth / 2.0f), 0, splatD - 1);

				for (int z = minPaintZ; z <= maxPaintZ; z++)
				{
					for (int x = minPaintX; x <= maxPaintX; x++)
					{
						float vx = (x - width / 2.0f) * quadSize;
						float vz = (z - depth / 2.0f) * quadSize;

						bool inBounds = false;
						if (brushIsSquare)
						{
							float dx = Mathf.Abs(vx - worldPos.X);
							float dz = Mathf.Abs(vz - worldPos.Z);
							inBounds = dx <= brushRadius && dz <= brushRadius;
						}
						else
						{
							inBounds = new Vector2(vx - worldPos.X, vz - worldPos.Z).Length() <= brushRadius;
						}

						if (inBounds && ShouldPaintCoord(x, z, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
						{
							if (applyGroundTexture)
							{
								_terrainSplatMap[x, z] = TerrainSplatWeights.PaintVertexWeighted(_terrainSplatMap[x, z], paintTextureIndex, intensityLevel);
								string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
								if (!string.IsNullOrEmpty(wsPath) && MetadataService.Instance.TryLoadMetadata(wsPath, out var metaRoot) && metaRoot != null)
								{
									string? swatchName = null;
									if (Realm.Client.Core.GameHost.Instance?.GroundTerrain != null && paintTextureIndex >= 0 && paintTextureIndex < Realm.Client.Core.GameHost.Instance.GroundTerrain.LoadedTextureList.Count)
									{
										swatchName = Realm.Client.Core.GameHost.Instance.GroundTerrain.LoadedTextureList[paintTextureIndex];
									}
									if (!string.IsNullOrEmpty(swatchName))
									{
										var prof = metaRoot.GetTerrainProfile(swatchName);
										if (prof != null)
										{
											if (terrain.PathingCodes != null && x < width && z < depth)
											{
												terrain.PathingCodes[x, z] = prof.DefaultPathingCode;
												result.PathingModified = true;
											}
										}
									}
								}

								if (terrain.Cells != null && x < width && z < depth)
								{
									float tY = terrain.Cells[x, z].CenterHeight;
									SpawnTerrainProceduralBombing(x, z, paintTextureIndex, tY, quadSize, width, depth);
								}
							}
							if (applyCliffTexture && _terrainCliffSplatMap != null && x < _terrainCliffSplatMap.GetLength(0) && z < _terrainCliffSplatMap.GetLength(1))
							{
								_terrainCliffSplatMap[x, z] = TerrainSplatWeights.PaintVertexWeighted(_terrainCliffSplatMap[x, z], cliffPaintTextureIndex, intensityLevel);
							}

							if (x < modMinX) modMinX = x; if (x > modMaxX) modMaxX = x; if (z < modMinZ) modMinZ = z; if (z > modMaxZ) modMaxZ = z;
							modified = true;
						}
					}
				}
			}
			else if (isPathing)
			{
				for (int z = quadMinZ; z <= quadMaxZ; z++)
				{
					for (int x = quadMinX; x <= quadMaxX; x++)
					{
						if (x >= 0 && x < width && z >= 0 && z < depth)
						{
							bool inBounds = true;
							if (!brushIsSquare)
							{
								float radius = N / 2.0f;
								float centerX = quadMinX + N / 2.0f;
								float centerZ = quadMinZ + N / 2.0f;
								float dx = (x + 0.5f) - centerX;
								float dz = (z + 0.5f) - centerZ;
								inBounds = (dx * dx + dz * dz) <= (radius * radius);
							}

							if (inBounds)
							{
								if (pathingAdd)
									terrain.PathingCodes[x, z] |= pathingMask;
								else
									terrain.PathingCodes[x, z] &= ~pathingMask;
								if (x < modMinX) modMinX = x; if (x > modMaxX) modMaxX = x; if (z < modMinZ) modMinZ = z; if (z > modMaxZ) modMaxZ = z;
								modified = true;
							}
						}
					}
				}
			}
		}
		else
		{
			long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			int minGX = Math.Clamp((int)Math.Floor((worldPos.X - brushRadius) / quadSize + width / 2.0f), 0, width);
			int maxGX = Math.Clamp((int)Math.Ceiling((worldPos.X + brushRadius) / quadSize + width / 2.0f), 0, width);
			int minGZ = Math.Clamp((int)Math.Floor((worldPos.Z - brushRadius) / quadSize + depth / 2.0f), 0, depth);
			int maxGZ = Math.Clamp((int)Math.Ceiling((worldPos.Z + brushRadius) / quadSize + depth / 2.0f), 0, depth);

			for (int z = minGZ; z <= maxGZ; z++)
			{
				for (int x = minGX; x <= maxGX; x++)
				{
					float vx = (x - width / 2.0f) * quadSize;
					float vz = (z - depth / 2.0f) * quadSize;

					float dist = 0.0f;
					bool inBounds = false;
					if (brushIsSquare)
					{
						float dx = Mathf.Abs(vx - worldPos.X);
						float dz = Mathf.Abs(vz - worldPos.Z);
						inBounds = dx <= brushRadius && dz <= brushRadius;
						dist = Mathf.Max(dx, dz);
					}
					else
					{
						dist = new Vector2(vx - worldPos.X, vz - worldPos.Z).Length();
						inBounds = dist <= brushRadius;
					}

					if (inBounds)
					{
						float falloff = 1.0f - (dist / brushRadius);
						falloff = Mathf.Sin(falloff * Mathf.Pi / 2.0f);

						if (isHeights)
						{
							float oldH = GetGridNodeHeight(in terrain, x, z);
							float newH = oldH;
							if (activeTool == Realm.Client.Core.GameHost.EditorTool.Raise)
							{
								newH = Mathf.Clamp(oldH + brushStrength * falloff * delta, -10.0f, 50.0f);
							}
							else if (activeTool == Realm.Client.Core.GameHost.EditorTool.Lower)
							{
								newH = Mathf.Clamp(oldH - brushStrength * falloff * delta, -10.0f, 50.0f);
							}
							else if (activeTool == Realm.Client.Core.GameHost.EditorTool.Plateau)
							{
								if (!_activePlateauHeight.HasValue)
								{
									_activePlateauHeight = GetTerrainHeightAt(worldPos);
									_activePlateauWaterMode = GetWaterModeAt(worldPos);
									_activePlateauWaterProfile = GetWaterProfileIndexAt(worldPos);
									_activePlateauWaterHeight = GetWaterHeightAt(worldPos);
								}
								float targetHeight = _activePlateauHeight.Value;
								newH = Mathf.Clamp(Mathf.Lerp(oldH, targetHeight, falloff), -10.0f, 50.0f);
								int cellX = Math.Clamp(x, 0, width - 1);
								int cellZ = Math.Clamp(z, 0, depth - 1);
								if (terrain.Cells != null && cellX < terrain.Cells.GetLength(0) && cellZ < terrain.Cells.GetLength(1))
								{
									if (terrain.Cells[cellX, cellZ].WaterMode != _activePlateauWaterMode || terrain.Cells[cellX, cellZ].WaterProfileIndex != _activePlateauWaterProfile || terrain.Cells[cellX, cellZ].WaterHeight != _activePlateauWaterHeight)
									{
										terrain.Cells[cellX, cellZ].WaterMode = _activePlateauWaterMode;
										terrain.Cells[cellX, cellZ].WaterProfileIndex = _activePlateauWaterProfile;
										terrain.Cells[cellX, cellZ].WaterHeight = _activePlateauWaterHeight;
										if (terrain.PathingCodes != null)
										{
											var waterProf = Realm.Client.RuntimeTerrain.Instance?.GetWaterProfile(terrain.Cells[cellX, cellZ].WaterProfileIndex);
											terrain.PathingCodes[cellX, cellZ] = waterProf != null ? waterProf.DefaultPathingCode : Realm.Client.EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
											result.PathingModified = true;
										}
									}
								}
								if (Mathf.Abs(newH - oldH) > 0.001f && terrain.PathingCodes != null)
								{
									int defaultPathBefore = Realm.Client.EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
									if (terrain.PathingCodes[cellX, cellZ] == defaultPathBefore)
									{
										terrain.PathingCodes[cellX, cellZ] = Realm.Client.EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
										result.PathingModified = true;
									}
								}
							}
							else if (activeTool == Realm.Client.Core.GameHost.EditorTool.Smooth)
							{
								float avg = 0f;
								int count = 0;
								for (int nz = -1; nz <= 1; nz++)
								{
									for (int nx = -1; nx <= 1; nx++)
									{
										int nxVal = x + nx;
										int nzVal = z + nz;
										if (nxVal >= 0 && nxVal <= width && nzVal >= 0 && nzVal <= depth)
										{
											avg += GetGridNodeHeight(in terrain, nxVal, nzVal);
											count++;
										}
									}
								}
								avg /= count;
								float effectiveStrength = brushStrength * 15.0f + 5.0f;
								newH = Mathf.Clamp(Mathf.Lerp(oldH, avg, Mathf.Clamp(effectiveStrength * falloff * delta, 0.0f, 1.0f)), -10.0f, 50.0f);
							}
							else if (activeTool == Realm.Client.Core.GameHost.EditorTool.Noise)
							{
								float dRatio = Mathf.Clamp(dist / brushRadius, 0.0f, 1.0f);
								float falloffWeight = 1.0f - (dRatio * dRatio * (3.0f - 2.0f * dRatio));
								float octave1 = SimplexNoise.Simplex2D(x * 0.05f, z * 0.05f) * 1.0f;
								float octave2 = SimplexNoise.Simplex2D(x * 0.2f, z * 0.2f) * 0.35f;
								float noiseValue = (octave1 + octave2) / 1.35f;
								float effectiveStrength = brushStrength * 0.25f + 8.25f;
								newH = Mathf.Clamp(oldH + noiseValue * effectiveStrength * falloffWeight * delta * 0.5f, -10.0f, 50.0f);
							}

							if (Mathf.Abs(newH - oldH) > 0.001f)
							{
								SetGridNodeHeight(ref terrain, x, z, newH);
								if (activeTool == Realm.Client.Core.GameHost.EditorTool.Raise || activeTool == Realm.Client.Core.GameHost.EditorTool.Lower || activeTool == Realm.Client.Core.GameHost.EditorTool.Plateau)
								{
									if (_terrainCliffSplatMap == null && Realm.Client.Core.GameHost.Instance?.GroundTerrain != null)
									{
										_terrainCliffSplatMap = Realm.Client.Core.GameHost.Instance.GroundTerrain.CliffSplatMap;
									}
									if (_terrainCliffSplatMap != null)
									{
										for (int cz = z - 1; cz <= z; cz++)
										{
											for (int cx = x - 1; cx <= x; cx++)
											{
												if (cx >= 0 && cx < width && cz >= 0 && cz < depth)
												{
													for (int nz = cz - 1; nz <= cz + 1; nz++)
													{
														for (int nx = cx - 1; nx <= cx + 1; nx++)
														{
															if (nx >= 0 && nx < width && nz >= 0 && nz < depth)
															{
																_terrainCliffSplatMap[nx, nz] = TerrainSplatWeights.CreateSolid(cliffPaintTextureIndex);
															}
														}
													}
												}
											}
										}
									}
								}
								if (x < modMinX) modMinX = x; if (x > modMaxX) modMaxX = x; if (z < modMinZ) modMinZ = z; if (z > modMaxZ) modMaxZ = z;
								modified = true;
							}
						}
						else if (isPaint)
						{
							if (_terrainCliffSplatMap == null && Realm.Client.Core.GameHost.Instance?.GroundTerrain != null)
							{
								_terrainCliffSplatMap = Realm.Client.Core.GameHost.Instance.GroundTerrain.CliffSplatMap;
							}

							int splatW = _terrainSplatMap.GetLength(0);
							int splatD = _terrainSplatMap.GetLength(1);
							int intensityLevel = Math.Clamp((int)MathF.Round(brushStrength), 0, 10);

							if (x < splatW && z < splatD && ShouldPaintCoord(x, z, nowMs))
							{
								if (applyGroundTexture)
								{
									_terrainSplatMap[x, z] = TerrainSplatWeights.PaintVertexWeighted(_terrainSplatMap[x, z], paintTextureIndex, intensityLevel);
									string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
									if (!string.IsNullOrEmpty(wsPath) && MetadataService.Instance.TryLoadMetadata(wsPath, out var metaRoot) && metaRoot != null)
									{
										string? swatchName = null;
										if (Realm.Client.Core.GameHost.Instance?.GroundTerrain != null && paintTextureIndex >= 0 && paintTextureIndex < Realm.Client.Core.GameHost.Instance.GroundTerrain.LoadedTextureList.Count)
										{
											swatchName = Realm.Client.Core.GameHost.Instance.GroundTerrain.LoadedTextureList[paintTextureIndex];
										}
										if (!string.IsNullOrEmpty(swatchName))
										{
											var prof = metaRoot.GetTerrainProfile(swatchName);
											if (prof != null)
											{
												if (terrain.PathingCodes != null && x < width && z < depth)
												{
													terrain.PathingCodes[x, z] = prof.DefaultPathingCode;
													result.PathingModified = true;
												}
											}
										}
									}

									if (terrain.Cells != null && x < width && z < depth)
									{
										float tY = terrain.Cells[x, z].CenterHeight;
										SpawnTerrainProceduralBombing(x, z, paintTextureIndex, tY, quadSize, width, depth);
									}
								}
								if (applyCliffTexture && _terrainCliffSplatMap != null && x < _terrainCliffSplatMap.GetLength(0) && z < _terrainCliffSplatMap.GetLength(1))
								{
									_terrainCliffSplatMap[x, z] = TerrainSplatWeights.PaintVertexWeighted(_terrainCliffSplatMap[x, z], cliffPaintTextureIndex, intensityLevel);
								}

								if (x < modMinX) modMinX = x; if (x > modMaxX) modMaxX = x; if (z < modMinZ) modMinZ = z; if (z > modMaxZ) modMaxZ = z;
								modified = true;
							}
						}
						else if (isPathing && x < width && z < depth)
						{
							int icx = Mathf.Clamp((int)Math.Round(vx / quadSize + width / 2.0f), 0, width - 1);
							int icz = Mathf.Clamp((int)Math.Round(vz / quadSize + depth / 2.0f), 0, depth - 1);
							if (pathingAdd)
								terrain.PathingCodes[icx, icz] |= pathingMask;
							else
								terrain.PathingCodes[icx, icz] &= ~pathingMask;
							if (icx < modMinX) modMinX = icx; if (icx > modMaxX) modMaxX = icx; if (icz < modMinZ) modMinZ = icz; if (icz > modMaxZ) modMaxZ = icz;
							modified = true;
						}
					}
				}
			}
		}

		if (modified)
		{
			result.HeightsModified = isHeights;
			result.SplatModified = isPaint || (isHeights && activeTool != Realm.Client.Core.GameHost.EditorTool.Smooth && activeTool != Realm.Client.Core.GameHost.EditorTool.Noise);
			result.PathingModified = result.PathingModified || isPathing;
			if (modMinX < _drawMinX) _drawMinX = modMinX;
			if (modMaxX > _drawMaxX) _drawMaxX = modMaxX;
			if (modMinZ < _drawMinZ) _drawMinZ = modMinZ;
			if (modMaxZ > _drawMaxZ) _drawMaxZ = modMaxZ;
			result.MinX = modMinX;
			result.MinZ = modMinZ;
			result.MaxX = modMaxX;
			result.MaxZ = modMaxZ;

			if (result.SplatModified)
			{
				AlignSplatMapSlots(modMinX - 2, modMinZ - 2, modMaxX + 2, modMaxZ + 2);
			}
		}

		return result;
	}

	public void BeginTerrainDraw(
		Vector3 hitPos,
		Realm.Client.Core.GameHost.EditorTool activeTool,
		bool blockMode,
		float blockLevelHeight,
		float[,] currentHeights = null,
		TerrainSplatWeights[,] currentSplatMap = null,
		int[,] currentPathing = null,
		TerrainSplatWeights[,] currentCliffSplatMap = null)
	{
		ref var terrain = ref GetTerrainState();
		_isDrawingTerrain = true;
		_drawMinX = int.MaxValue;
		_drawMinZ = int.MaxValue;
		_drawMaxX = -1;
		_drawMaxZ = -1;
		_terrainCellsBefore = terrain.Cells != null ? (TerrainCell[,])terrain.Cells.Clone() : null;
		_terrainSplatMapBefore = currentSplatMap != null ? (TerrainSplatWeights[,])currentSplatMap.Clone() : null;
		_terrainCliffSplatMapBefore = currentCliffSplatMap != null ? (TerrainSplatWeights[,])currentCliffSplatMap.Clone() : null;
		_terrainPathingBefore = currentPathing != null ? (int[,])currentPathing.Clone() : null;

		float startHeight = GetTerrainHeightAt(hitPos);

		if (activeTool == Realm.Client.Core.GameHost.EditorTool.Plateau)
		{
			_activePlateauHeight = blockMode ? (float)MathF.Round(startHeight / TerrainCell.TIER_HEIGHT) * TerrainCell.TIER_HEIGHT : startHeight;
			_activePlateauWaterMode = GetWaterModeAt(hitPos);
			_activePlateauWaterProfile = GetWaterProfileIndexAt(hitPos);
			_activePlateauWaterHeight = GetWaterHeightAt(hitPos);
		}

		if (blockMode || activeTool == Realm.Client.Core.GameHost.EditorTool.Height)
		{
			SetBlockTargetHeight(activeTool, startHeight, blockLevelHeight, hitPos);
		}
	}

	private void SetBlockTargetHeight(Realm.Client.Core.GameHost.EditorTool activeTool, float startHeight, float blockLevelHeight, Vector3 hitPos)
	{
		if (activeTool == Realm.Client.Core.GameHost.EditorTool.Height)
		{
			_activeBlockTargetHeight = Math.Clamp(blockLevelHeight, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
			_hasBlockTargetHeight = true;
		}
		else if (activeTool == Realm.Client.Core.GameHost.EditorTool.Raise)
		{
			_activeBlockTargetHeight = Math.Clamp(startHeight + blockLevelHeight, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
			_hasBlockTargetHeight = true;
		}
		else if (activeTool == Realm.Client.Core.GameHost.EditorTool.Lower)
		{
			_activeBlockTargetHeight = Math.Clamp(startHeight - blockLevelHeight, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
			_hasBlockTargetHeight = true;
		}
		else if (activeTool == Realm.Client.Core.GameHost.EditorTool.Plateau)
		{
			_activeBlockTargetHeight = Math.Clamp((float)MathF.Round(startHeight / TerrainCell.TIER_HEIGHT) * TerrainCell.TIER_HEIGHT, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
			_activeBlockTargetWaterMode = GetWaterModeAt(hitPos);
			_activeBlockTargetWaterProfile = GetWaterProfileIndexAt(hitPos);
			_activeBlockTargetWaterHeight = GetWaterHeightAt(hitPos);
			_hasBlockTargetHeight = true;
		}
	}

	private bool CheckCellsChanged(TerrainCell[,] currentCells, int w, int d)
	{
		if (_terrainCellsBefore == null || currentCells == null) return false;
		for (int z = 0; z < d; z++)
		{
			for (int x = 0; x < w; x++)
			{
				int mapX = _drawMinX + x;
				int mapZ = _drawMinZ + z;
				if (mapX >= _terrainCellsBefore.GetLength(0) || mapZ >= _terrainCellsBefore.GetLength(1) ||
				    mapX >= currentCells.GetLength(0) || mapZ >= currentCells.GetLength(1)) continue;
				var b = _terrainCellsBefore[mapX, mapZ];
				var a = currentCells[mapX, mapZ];
				if (b.Y_NW != a.Y_NW || b.Y_NE != a.Y_NE || b.Y_SE != a.Y_SE || b.Y_SW != a.Y_SW || b.WaterMode != a.WaterMode || b.WaterProfileIndex != a.WaterProfileIndex)
					return true;
			}
		}
		return false;
	}

	private bool CheckPathingChanged(int[,] currentPathing, int w, int d)
	{
		if (_terrainPathingBefore == null || currentPathing == null) return false;
		for (int z = 0; z < d; z++)
		{
			for (int x = 0; x < w; x++)
			{
				int mapX = _drawMinX + x;
				int mapZ = _drawMinZ + z;
				if (mapX >= _terrainPathingBefore.GetLength(0) || mapZ >= _terrainPathingBefore.GetLength(1) ||
				    mapX >= currentPathing.GetLength(0) || mapZ >= currentPathing.GetLength(1)) continue;
				if (_terrainPathingBefore[mapX, mapZ] != currentPathing[mapX, mapZ]) return true;
			}
		}
		return false;
	}

	private bool CheckSplatChanged(TerrainSplatWeights[,] currentSplatMap, TerrainSplatWeights[,] currentCliffSplatMap, int splatNodeW, int splatNodeD)
	{
		if (_terrainSplatMapBefore != null && currentSplatMap != null)
		{
			for (int z = 0; z < splatNodeD; z++)
			{
				for (int x = 0; x < splatNodeW; x++)
				{
					int mapX = _drawMinX + x;
					int mapZ = _drawMinZ + z;
					if (mapX >= _terrainSplatMapBefore.GetLength(0) || mapZ >= _terrainSplatMapBefore.GetLength(1) ||
					    mapX >= currentSplatMap.GetLength(0) || mapZ >= currentSplatMap.GetLength(1)) continue;
					var b = _terrainSplatMapBefore[mapX, mapZ];
					var a = currentSplatMap[mapX, mapZ];
					if (b.Index0 != a.Index0 || b.Index1 != a.Index1 || b.Index2 != a.Index2 || b.Index3 != a.Index3 ||
					    b.Weight0 != a.Weight0 || b.Weight1 != a.Weight1 || b.Weight2 != a.Weight2 || b.Weight3 != a.Weight3) return true;
				}
			}
		}
		if (_terrainCliffSplatMapBefore != null && currentCliffSplatMap != null)
		{
			for (int z = 0; z < splatNodeD; z++)
			{
				for (int x = 0; x < splatNodeW; x++)
				{
					int mapX = _drawMinX + x;
					int mapZ = _drawMinZ + z;
					if (mapX >= _terrainCliffSplatMapBefore.GetLength(0) || mapZ >= _terrainCliffSplatMapBefore.GetLength(1) ||
					    mapX >= currentCliffSplatMap.GetLength(0) || mapZ >= currentCliffSplatMap.GetLength(1)) continue;
					var b = _terrainCliffSplatMapBefore[mapX, mapZ];
					var a = currentCliffSplatMap[mapX, mapZ];
					if (b.Index0 != a.Index0 || b.Index1 != a.Index1 || b.Index2 != a.Index2 || b.Index3 != a.Index3 ||
					    b.Weight0 != a.Weight0 || b.Weight1 != a.Weight1 || b.Weight2 != a.Weight2 || b.Weight3 != a.Weight3) return true;
				}
			}
		}
		return false;
	}

	private void BuildChangedCells(TerrainCell[,] currentCells, int w, int d, out TerrainCell[,] beforeC, out TerrainCell[,] afterC)
	{
		beforeC = new TerrainCell[w, d];
		afterC = new TerrainCell[w, d];
		for (int z = 0; z < d; z++)
		{
			for (int x = 0; x < w; x++)
			{
				int mapX = _drawMinX + x;
				int mapZ = _drawMinZ + z;
				if (_terrainCellsBefore != null && mapX < _terrainCellsBefore.GetLength(0) && mapZ < _terrainCellsBefore.GetLength(1))
					beforeC[x, z] = _terrainCellsBefore[mapX, mapZ];
				if (currentCells != null && mapX < currentCells.GetLength(0) && mapZ < currentCells.GetLength(1))
					afterC[x, z] = currentCells[mapX, mapZ];
			}
		}
	}

	private void BuildChangedPathing(int[,] currentPathing, int w, int d, out int[,] beforeP, out int[,] afterP)
	{
		beforeP = new int[w, d];
		afterP = new int[w, d];
		for (int z = 0; z < d; z++)
		{
			for (int x = 0; x < w; x++)
			{
				int mapX = _drawMinX + x;
				int mapZ = _drawMinZ + z;
				if (_terrainPathingBefore != null && mapX < _terrainPathingBefore.GetLength(0) && mapZ < _terrainPathingBefore.GetLength(1))
					beforeP[x, z] = _terrainPathingBefore[mapX, mapZ];
				if (currentPathing != null && mapX < currentPathing.GetLength(0) && mapZ < currentPathing.GetLength(1))
					afterP[x, z] = currentPathing[mapX, mapZ];
			}
		}
	}

	private void BuildChangedSplat(TerrainSplatWeights[,] currentSplatMap, TerrainSplatWeights[,] currentCliffSplatMap, int splatNodeW, int splatNodeD, out TerrainSplatWeights[,] beforeS, out TerrainSplatWeights[,] afterS, out TerrainSplatWeights[,] beforeCliffS, out TerrainSplatWeights[,] afterCliffS)
	{
		beforeS = null; afterS = null; beforeCliffS = null; afterCliffS = null;
		if (currentSplatMap != null)
		{
			beforeS = new TerrainSplatWeights[splatNodeW, splatNodeD];
			afterS = new TerrainSplatWeights[splatNodeW, splatNodeD];
			for (int z = 0; z < splatNodeD; z++)
			{
				for (int x = 0; x < splatNodeW; x++)
				{
					int mapX = _drawMinX + x;
					int mapZ = _drawMinZ + z;
					if (_terrainSplatMapBefore != null && mapX < _terrainSplatMapBefore.GetLength(0) && mapZ < _terrainSplatMapBefore.GetLength(1))
						beforeS[x, z] = _terrainSplatMapBefore[mapX, mapZ];
					if (mapX < currentSplatMap.GetLength(0) && mapZ < currentSplatMap.GetLength(1))
						afterS[x, z] = currentSplatMap[mapX, mapZ];
				}
			}
		}
		if (currentCliffSplatMap != null)
		{
			beforeCliffS = new TerrainSplatWeights[splatNodeW, splatNodeD];
			afterCliffS = new TerrainSplatWeights[splatNodeW, splatNodeD];
			for (int z = 0; z < splatNodeD; z++)
			{
				for (int x = 0; x < splatNodeW; x++)
				{
					int mapX = _drawMinX + x;
					int mapZ = _drawMinZ + z;
					if (_terrainCliffSplatMapBefore != null && mapX < _terrainCliffSplatMapBefore.GetLength(0) && mapZ < _terrainCliffSplatMapBefore.GetLength(1))
						beforeCliffS[x, z] = _terrainCliffSplatMapBefore[mapX, mapZ];
					if (mapX < currentCliffSplatMap.GetLength(0) && mapZ < currentCliffSplatMap.GetLength(1))
						afterCliffS[x, z] = currentCliffSplatMap[mapX, mapZ];
				}
			}
		}
	}

	public TerrainModifyAction EndTerrainDraw(
		float[,] currentHeights = null,
		TerrainSplatWeights[,] currentSplatMap = null,
		int[,] currentPathing = null,
		TerrainSplatWeights[,] currentCliffSplatMap = null)
	{
		_isDrawingTerrain = false;
		_hasBlockTargetHeight = false;
		_activePlateauHeight = null;
		_activePlateauWaterMode = WaterType.None;
		_activePlateauWaterProfile = 0;
		_activeBlockTargetWaterMode = WaterType.None;
		_activeBlockTargetWaterProfile = 0;

		ref var terrain = ref GetTerrainState();
		var currentCells = terrain.Cells;

		if (_drawMinX <= _drawMaxX && _drawMinZ <= _drawMaxZ)
		{
			bool cellsChanged = false;
			bool pathingChanged = false;
			bool splatChanged = false;

			int w = _drawMaxX - _drawMinX + 1;
			int d = _drawMaxZ - _drawMinZ + 1;

			if (_terrainCellsBefore != null && currentCells != null)
			{
				for (int z = 0; z < d && !cellsChanged; z++)
				{
					for (int x = 0; x < w && !cellsChanged; x++)
					{
						int mapX = _drawMinX + x;
						int mapZ = _drawMinZ + z;
						if (mapX < _terrainCellsBefore.GetLength(0) && mapZ < _terrainCellsBefore.GetLength(1) &&
						    mapX < currentCells.GetLength(0) && mapZ < currentCells.GetLength(1))
						{
							var b = _terrainCellsBefore[mapX, mapZ];
							var a = currentCells[mapX, mapZ];
							if (b.Y_NW != a.Y_NW || b.Y_NE != a.Y_NE || b.Y_SE != a.Y_SE || b.Y_SW != a.Y_SW || b.WaterMode != a.WaterMode || b.WaterProfileIndex != a.WaterProfileIndex)
							{
								cellsChanged = true;
							}
						}
					}
				}
			}

			if (_terrainPathingBefore != null && currentPathing != null)
			{
				for (int z = 0; z < d && !pathingChanged; z++)
				{
					for (int x = 0; x < w && !pathingChanged; x++)
					{
						int mapX = _drawMinX + x;
						int mapZ = _drawMinZ + z;
						if (mapX < _terrainPathingBefore.GetLength(0) && mapZ < _terrainPathingBefore.GetLength(1) &&
						    mapX < currentPathing.GetLength(0) && mapZ < currentPathing.GetLength(1))
						{
							if (_terrainPathingBefore[mapX, mapZ] != currentPathing[mapX, mapZ])
							{
								pathingChanged = true;
							}
						}
					}
				}
			}

			int splatW = currentSplatMap != null ? currentSplatMap.GetLength(0) : terrain.Width + 1;
			int splatD = currentSplatMap != null ? currentSplatMap.GetLength(1) : terrain.Depth + 1;
			int splatNodeW = Math.Min(w + 1, splatW - _drawMinX);
			int splatNodeD = Math.Min(d + 1, splatD - _drawMinZ);

			if (_terrainSplatMapBefore != null && currentSplatMap != null)
			{
				for (int z = 0; z < splatNodeD && !splatChanged; z++)
				{
					for (int x = 0; x < splatNodeW && !splatChanged; x++)
					{
						int mapX = _drawMinX + x;
						int mapZ = _drawMinZ + z;
						if (mapX < _terrainSplatMapBefore.GetLength(0) && mapZ < _terrainSplatMapBefore.GetLength(1) &&
						    mapX < currentSplatMap.GetLength(0) && mapZ < currentSplatMap.GetLength(1))
						{
							var b = _terrainSplatMapBefore[mapX, mapZ];
							var a = currentSplatMap[mapX, mapZ];
							if (b.Index0 != a.Index0 || b.Index1 != a.Index1 || b.Index2 != a.Index2 || b.Index3 != a.Index3 ||
							    b.Weight0 != a.Weight0 || b.Weight1 != a.Weight1 || b.Weight2 != a.Weight2 || b.Weight3 != a.Weight3)
							{
								splatChanged = true;
							}
						}
					}
				}
			}

			if (!splatChanged && _terrainCliffSplatMapBefore != null && currentCliffSplatMap != null)
			{
				for (int z = 0; z < splatNodeD && !splatChanged; z++)
				{
					for (int x = 0; x < splatNodeW && !splatChanged; x++)
					{
						int mapX = _drawMinX + x;
						int mapZ = _drawMinZ + z;
						if (mapX < _terrainCliffSplatMapBefore.GetLength(0) && mapZ < _terrainCliffSplatMapBefore.GetLength(1) &&
						    mapX < currentCliffSplatMap.GetLength(0) && mapZ < currentCliffSplatMap.GetLength(1))
						{
							var b = _terrainCliffSplatMapBefore[mapX, mapZ];
							var a = currentCliffSplatMap[mapX, mapZ];
							if (b.Index0 != a.Index0 || b.Index1 != a.Index1 || b.Index2 != a.Index2 || b.Index3 != a.Index3 ||
							    b.Weight0 != a.Weight0 || b.Weight1 != a.Weight1 || b.Weight2 != a.Weight2 || b.Weight3 != a.Weight3)
							{
								splatChanged = true;
							}
						}
					}
				}
			}

			TerrainCell[,] beforeC = null;
			TerrainCell[,] afterC = null;
			if (cellsChanged)
			{
				beforeC = new TerrainCell[w, d];
				afterC = new TerrainCell[w, d];
				for (int z = 0; z < d; z++)
				{
					for (int x = 0; x < w; x++)
					{
						int mapX = _drawMinX + x;
						int mapZ = _drawMinZ + z;
						if (_terrainCellsBefore != null && mapX < _terrainCellsBefore.GetLength(0) && mapZ < _terrainCellsBefore.GetLength(1))
						{
							beforeC[x, z] = _terrainCellsBefore[mapX, mapZ];
						}
						if (currentCells != null && mapX < currentCells.GetLength(0) && mapZ < currentCells.GetLength(1))
						{
							afterC[x, z] = currentCells[mapX, mapZ];
						}
					}
				}
			}

			int[,] beforeP = null;
			int[,] afterP = null;
			if (pathingChanged)
			{
				beforeP = new int[w, d];
				afterP = new int[w, d];
				for (int z = 0; z < d; z++)
				{
					for (int x = 0; x < w; x++)
					{
						int mapX = _drawMinX + x;
						int mapZ = _drawMinZ + z;
						if (_terrainPathingBefore != null && mapX < _terrainPathingBefore.GetLength(0) && mapZ < _terrainPathingBefore.GetLength(1))
						{
							beforeP[x, z] = _terrainPathingBefore[mapX, mapZ];
						}
						if (currentPathing != null && mapX < currentPathing.GetLength(0) && mapZ < currentPathing.GetLength(1))
						{
							afterP[x, z] = currentPathing[mapX, mapZ];
						}
					}
				}
			}

			TerrainSplatWeights[,] beforeS = null;
			TerrainSplatWeights[,] afterS = null;
			TerrainSplatWeights[,] beforeCliffS = null;
			TerrainSplatWeights[,] afterCliffS = null;
			if (splatChanged)
			{
				if (currentSplatMap != null)
				{
					beforeS = new TerrainSplatWeights[splatNodeW, splatNodeD];
					afterS = new TerrainSplatWeights[splatNodeW, splatNodeD];
					for (int z = 0; z < splatNodeD; z++)
					{
						for (int x = 0; x < splatNodeW; x++)
						{
							int mapX = _drawMinX + x;
							int mapZ = _drawMinZ + z;
							if (_terrainSplatMapBefore != null && mapX < _terrainSplatMapBefore.GetLength(0) && mapZ < _terrainSplatMapBefore.GetLength(1))
							{
								beforeS[x, z] = _terrainSplatMapBefore[mapX, mapZ];
							}
							if (mapX < currentSplatMap.GetLength(0) && mapZ < currentSplatMap.GetLength(1))
							{
								afterS[x, z] = currentSplatMap[mapX, mapZ];
							}
						}
					}
				}
				if (currentCliffSplatMap != null)
				{
					beforeCliffS = new TerrainSplatWeights[splatNodeW, splatNodeD];
					afterCliffS = new TerrainSplatWeights[splatNodeW, splatNodeD];
					for (int z = 0; z < splatNodeD; z++)
					{
						for (int x = 0; x < splatNodeW; x++)
						{
							int mapX = _drawMinX + x;
							int mapZ = _drawMinZ + z;
							if (_terrainCliffSplatMapBefore != null && mapX < _terrainCliffSplatMapBefore.GetLength(0) && mapZ < _terrainCliffSplatMapBefore.GetLength(1))
							{
								beforeCliffS[x, z] = _terrainCliffSplatMapBefore[mapX, mapZ];
							}
							if (mapX < currentCliffSplatMap.GetLength(0) && mapZ < currentCliffSplatMap.GetLength(1))
							{
								afterCliffS[x, z] = currentCliffSplatMap[mapX, mapZ];
							}
						}
					}
				}
			}

			return new TerrainModifyAction(_drawMinX, _drawMinZ, w, d, beforeC, afterC, beforeS, afterS, beforeP, afterP, beforeCliffS, afterCliffS);
		}

		return new TerrainModifyAction(
			_terrainCellsBefore, currentCells,
			_terrainSplatMapBefore, currentSplatMap,
			_terrainPathingBefore, currentPathing,
			_terrainCliffSplatMapBefore, currentCliffSplatMap);
	}

	public void ResetDrawState()
	{
		_hasBlockTargetHeight = false;
		_activePlateauHeight = null;
		_activePlateauWaterMode = WaterType.None;
		_activeBlockTargetWaterMode = WaterType.None;
	}

	public void ResetAllState()
	{
		_selectionStart = null;
		_selectionEnd = null;
		_copiedArea = null;
		_hasBlockTargetHeight = false;
		_activeBlockTargetHeight = 0.0f;
		_activePlateauHeight = null;
		_activePlateauWaterMode = WaterType.None;
		_activeBlockTargetWaterMode = WaterType.None;
		_terrainSplatMap = null;
		_terrainCliffSplatMap = null;
		_clumpSpawnCooldown = 0.0f;
	}

	public void TickClumpCooldown(float delta)
	{
		if (_clumpSpawnCooldown > 0.0f)
			_clumpSpawnCooldown -= delta;
	}

	public bool CanSpawnClump() => _clumpSpawnCooldown <= 0.0f;

	public void BeginClumpSession()
	{
		_isDrawingClump = true;
		_clumpSpawnActionsInSession.Clear();
		_cachedSessionPoints.Clear();
	}

	public void RecordClumpSpawnAction(IEditorAction action)
	{
		_clumpSpawnActionsInSession.Add(action);
		if (action is ObjectSpawnAction objAct && GodotObject.IsInstanceValid(objAct.SpawnedNode))
		{
			float baseRadius = 0.5f;
			_cachedSessionPoints.Add((objAct.Position, baseRadius * objAct.Scale));
		}
	}

	public void SetClumpCooldown(float value)
	{
		_clumpSpawnCooldown = value;
	}

	public CompositeAction EndClumpSession()
	{
		_isDrawingClump = false;
		if (_clumpSpawnActionsInSession.Count > 0)
		{
			return new CompositeAction(_clumpSpawnActionsInSession);
		}
		return null;
	}

	public List<EntitySpawnRequest> BuildClumpSpawnRequests(
		Vector3 centerPos,
		Realm.Client.Core.GameHost.EditorTool activeTool,
		string activePlaceId,
		bool placeUnitIsEnemy,
		float placementScale,
		float clumpCount,
		float clumpScale,
		float brushRadius,
		bool brushIsSquare,
		bool randomRotation,
		bool randomScale,
		float placementRotation,
		MirrorMode mirrorMode,
		float assetBaseCollisionRadius = 0.5f)
	{
		if (string.IsNullOrEmpty(activePlaceId)) return new List<EntitySpawnRequest>();

		var requests = new List<EntitySpawnRequest>();
		int spawnCount = Mathf.Max(1, (int)Math.Round(clumpCount));

		ref var terrain = ref GetTerrainState();
		float quadSize = terrain.Cells != null ? terrain.QuadSize : 1f;
		int terrainWidth = terrain.Cells != null ? terrain.Width : 0;
		int terrainDepth = terrain.Cells != null ? terrain.Depth : 0;
		float halfW = terrainWidth / 2.0f * quadSize;
		float halfD = terrainDepth / 2.0f * quadSize;

		float baseRadius = Mathf.Max(0.1f, assetBaseCollisionRadius);
		float autoClumpSpacing = Mathf.Clamp(brushRadius / (1.5f * Mathf.Sqrt(Mathf.Max(1, spawnCount))), 0.5f, 4.0f);

		int maxAttemptsPerObject = 30;

		for (int i = 0; i < spawnCount; i++)
		{
			bool placed = false;
			for (int attempt = 0; attempt < maxAttemptsPerObject; attempt++)
			{
				float dx = 0.0f;
				float dz = 0.0f;
				if (brushIsSquare)
				{
					dx = (float)(GD.Randf() * 2.0 - 1.0) * brushRadius;
					dz = (float)(GD.Randf() * 2.0 - 1.0) * brushRadius;
				}
				else
				{
					float r = Mathf.Sqrt((float)GD.Randf()) * brushRadius;
					float theta = (float)(GD.Randf() * Mathf.Pi * 2.0);
					dx = r * Mathf.Cos(theta);
					dz = r * Mathf.Sin(theta);
				}

				Vector3 spawnPos = new Vector3(centerPos.X + dx, centerPos.Y, centerPos.Z + dz);
				if (terrain.Cells != null)
				{
					if (Mathf.Abs(spawnPos.X) > halfW || Mathf.Abs(spawnPos.Z) > halfD) continue;
				}
				spawnPos.Y = GetTerrainHeightAt(spawnPos);

				float offsetRange = clumpScale * placementScale;
				float minScale = Mathf.Max(0.01f, placementScale - offsetRange);
				float maxScale = placementScale + offsetRange;
				float scaleVal = minScale + (float)GD.Randf() * (maxScale - minScale);

				float candidateRadius = baseRadius * scaleVal;

				bool collision = false;
				foreach (var req in requests)
				{
					float reqRadius = baseRadius * req.Scale;
					float minDist = (candidateRadius + reqRadius) * autoClumpSpacing;
					float distSq = (spawnPos.X - req.Position.X) * (spawnPos.X - req.Position.X) +
					               (spawnPos.Z - req.Position.Z) * (spawnPos.Z - req.Position.Z);
					if (distSq < minDist * minDist)
					{
						collision = true;
						break;
					}
				}

				if (collision) continue;

				foreach (var sessionPt in _cachedSessionPoints)
				{
					float minDist = (candidateRadius + sessionPt.Radius) * autoClumpSpacing;
					float distSq = (spawnPos.X - sessionPt.Position.X) * (spawnPos.X - sessionPt.Position.X) +
					               (spawnPos.Z - sessionPt.Position.Z) * (spawnPos.Z - sessionPt.Position.Z);
					if (distSq < minDist * minDist)
					{
						collision = true;
						break;
					}
				}

				if (collision) continue;

				float rotY = randomRotation ? (float)(GD.Randf() * 360.0) : placementRotation;
				string spawnType = activeTool == Realm.Client.Core.GameHost.EditorTool.PlaceUnit ? "unit"
					: activeTool == Realm.Client.Core.GameHost.EditorTool.PlaceProp ? "prop"
					: "decal";
				bool isEnemy = activeTool == Realm.Client.Core.GameHost.EditorTool.PlaceUnit && placeUnitIsEnemy;

				requests.Add(new EntitySpawnRequest
				{
					Type = spawnType,
					Id = activePlaceId,
					Position = spawnPos,
					Rotation = rotY,
					Scale = scaleVal,
					IsEnemy = isEnemy
				});

				if (mirrorMode != MirrorMode.None)
				{
					AddMirroredRequests(requests, spawnType, activePlaceId, spawnPos, rotY, scaleVal, isEnemy, mirrorMode);
				}

				placed = true;
				break;
			}

			if (!placed && requests.Count > 0)
			{
			}
		}

		return requests;
	}

	private void ProcessRampCell(Vector3 start, Vector3 end, float brushRadius, float segmentLengthSquared, int width, int depth, float quadSize, int gridX, int gridZ, ref TerrainState terrain, ref bool modified, HashSet<Vector2I> modifiedCells)
	{
		float worldX = (gridX - width / 2.0f) * quadSize;
		float worldZ = (gridZ - depth / 2.0f) * quadSize;
		float interpolationFactor = ((worldX - start.X) * (end.X - start.X) + (worldZ - start.Z) * (end.Z - start.Z)) / segmentLengthSquared;
		interpolationFactor = Mathf.Clamp(interpolationFactor, 0.0f, 1.0f);
		float projectedX = start.X + interpolationFactor * (end.X - start.X);
		float projectedZ = start.Z + interpolationFactor * (end.Z - start.Z);
		float distanceToProjected = Mathf.Sqrt((worldX - projectedX) * (worldX - projectedX) + (worldZ - projectedZ) * (worldZ - projectedZ));
		if (distanceToProjected > brushRadius) return;

		float targetHeight = Mathf.Lerp(start.Y, end.Y, interpolationFactor);
		float innerRadius = brushRadius * 0.7f;
		float oldHeight = GetGridNodeHeight(in terrain, gridX, gridZ);
		float newHeight = targetHeight;

		if (distanceToProjected > innerRadius)
		{
			float edgeFactor = 1.0f - ((distanceToProjected - innerRadius) / Math.Max(0.001f, brushRadius - innerRadius));
			edgeFactor = Mathf.Sin(edgeFactor * Mathf.Pi / 2.0f);
			newHeight = Mathf.Lerp(oldHeight, targetHeight, edgeFactor);
		}

		if (Mathf.Abs(newHeight - oldHeight) <= 0.001f) return;

		int cellX = Math.Clamp(gridX, 0, width - 1);
		int cellZ = Math.Clamp(gridZ, 0, depth - 1);
		if (terrain.PathingCodes != null)
		{
			int defaultPathBefore = Realm.Client.EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
			if (terrain.PathingCodes[cellX, cellZ] == defaultPathBefore)
			{
				terrain.PathingCodes[cellX, cellZ] = Realm.Client.EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
			}
		}
		SetGridNodeHeight(ref terrain, gridX, gridZ, newHeight);
		int rampPaintIdx = Realm.Client.Core.GameHost.Instance != null ? Realm.Client.Core.GameHost.Instance.EditorPaintTextureIndex : 0;
		int rampCliffIdx = Realm.Client.Core.GameHost.Instance != null ? Realm.Client.Core.GameHost.Instance.EditorCliffPaintTextureIndex : 1;
		SetGridNodeSplat(gridX, gridZ, new TerrainSplatWeights { Index0 = rampPaintIdx, Weight0 = 1.0f }, new TerrainSplatWeights { Index0 = rampPaintIdx, Weight0 = 1.0f }, new TerrainSplatWeights { Index0 = rampPaintIdx, Weight0 = 1.0f }, new TerrainSplatWeights { Index0 = rampPaintIdx, Weight0 = 1.0f });
		SetGridNodeCliffSplat(gridX, gridZ, new TerrainSplatWeights { Index0 = rampCliffIdx, Weight0 = 1.0f }, new TerrainSplatWeights { Index0 = rampCliffIdx, Weight0 = 1.0f }, new TerrainSplatWeights { Index0 = rampCliffIdx, Weight0 = 1.0f }, new TerrainSplatWeights { Index0 = rampCliffIdx, Weight0 = 1.0f });
		modified = true;
		modifiedCells.Add(new Vector2I(gridX, gridZ));
	}

	public bool ApplyRamp(Vector3 start, Vector3 end, float brushRadius, bool blockMode, float blockLevelHeight)
	{
		ref var terrain = ref GetTerrainState();
		var cells = terrain.Cells;
		if (cells == null) return false;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;
		bool modified = false;

		float segmentLengthSquared = (end.X - start.X) * (end.X - start.X) + (end.Z - start.Z) * (end.Z - start.Z);
		if (segmentLengthSquared <= 0.0001f) return false;

		float minWorldX = Mathf.Min(start.X, end.X) - brushRadius;
		float maxWorldX = Mathf.Max(start.X, end.X) + brushRadius;
		float minWorldZ = Mathf.Min(start.Z, end.Z) - brushRadius;
		float maxWorldZ = Mathf.Max(start.Z, end.Z) + brushRadius;

		int minGridX = Mathf.Clamp(Mathf.FloorToInt(minWorldX / quadSize + width / 2.0f), 0, width);
		int maxGridX = Mathf.Clamp(Mathf.CeilToInt(maxWorldX / quadSize + width / 2.0f), 0, width);
		int minGridZ = Mathf.Clamp(Mathf.FloorToInt(minWorldZ / quadSize + depth / 2.0f), 0, depth);
		int maxGridZ = Mathf.Clamp(Mathf.CeilToInt(maxWorldZ / quadSize + depth / 2.0f), 0, depth);

		var modifiedCells = new HashSet<Vector2I>();

		for (int gridZ = minGridZ; gridZ <= maxGridZ; gridZ++)
		{
			for (int gridX = minGridX; gridX <= maxGridX; gridX++)
			{
				float worldX = (gridX - width / 2.0f) * quadSize;
				float worldZ = (gridZ - depth / 2.0f) * quadSize;
				float interpolationFactor = ((worldX - start.X) * (end.X - start.X) + (worldZ - start.Z) * (end.Z - start.Z)) / segmentLengthSquared;
				interpolationFactor = Mathf.Clamp(interpolationFactor, 0.0f, 1.0f);
				float projectedX = start.X + interpolationFactor * (end.X - start.X);
				float projectedZ = start.Z + interpolationFactor * (end.Z - start.Z);
				float distanceToProjected = Mathf.Sqrt((worldX - projectedX) * (worldX - projectedX) + (worldZ - projectedZ) * (worldZ - projectedZ));
				if (distanceToProjected <= brushRadius)
				{
					float targetHeight = Mathf.Lerp(start.Y, end.Y, interpolationFactor);
					float innerRadius = brushRadius * 0.7f;
					float oldHeight = GetGridNodeHeight(in terrain, gridX, gridZ);
					float newHeight;

					if (distanceToProjected <= innerRadius)
					{
						newHeight = targetHeight;
					}
					else
					{
						float edgeFactor = 1.0f - ((distanceToProjected - innerRadius) / Math.Max(0.001f, brushRadius - innerRadius));
						edgeFactor = Mathf.Sin(edgeFactor * Mathf.Pi / 2.0f);
						newHeight = Mathf.Lerp(oldHeight, targetHeight, edgeFactor);
					}

					if (Mathf.Abs(newHeight - oldHeight) > 0.001f)
					{
						int cellX = Math.Clamp(gridX, 0, width - 1);
						int cellZ = Math.Clamp(gridZ, 0, depth - 1);
						if (terrain.PathingCodes != null)
						{
							int defaultPathBefore = Realm.Client.EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
							if (terrain.PathingCodes[cellX, cellZ] == defaultPathBefore)
							{
								terrain.PathingCodes[cellX, cellZ] = Realm.Client.EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
							}
						}
						SetGridNodeHeight(ref terrain, gridX, gridZ, newHeight);
						int rampPaintIdx = Realm.Client.Core.GameHost.Instance != null ? Realm.Client.Core.GameHost.Instance.EditorPaintTextureIndex : 0;
						int rampCliffIdx = Realm.Client.Core.GameHost.Instance != null ? Realm.Client.Core.GameHost.Instance.EditorCliffPaintTextureIndex : 1;

						if (_terrainSplatMap == null && Realm.Client.Core.GameHost.Instance?.GroundTerrain != null)
						{
							_terrainSplatMap = Realm.Client.Core.GameHost.Instance.GroundTerrain.SplatMap;
						}
						if (_terrainSplatMap != null)
						{
							for (int cz = gridZ - 1; cz <= gridZ; cz++)
							{
								for (int cx = gridX - 1; cx <= gridX; cx++)
								{
									if (cx >= 0 && cx < width && cz >= 0 && cz < depth)
									{
										_terrainSplatMap[cx, cz] = TerrainSplatWeights.CreateSolid(rampPaintIdx);
										modifiedCells.Add(new Vector2I(cx, cz));
									}
								}
							}
						}

						if (_terrainCliffSplatMap == null && Realm.Client.Core.GameHost.Instance?.GroundTerrain != null)
						{
							_terrainCliffSplatMap = Realm.Client.Core.GameHost.Instance.GroundTerrain.CliffSplatMap;
						}
						if (_terrainCliffSplatMap != null)
						{
							for (int cz = gridZ - 1; cz <= gridZ; cz++)
							{
								for (int cx = gridX - 1; cx <= gridX; cx++)
								{
									if (cx >= 0 && cx < width && cz >= 0 && cz < depth)
									{
										for (int nz = cz - 1; nz <= cz + 1; nz++)
										{
											for (int nx = cx - 1; nx <= cx + 1; nx++)
											{
												if (nx >= 0 && nx < width && nz >= 0 && nz < depth)
												{
													_terrainCliffSplatMap[nx, nz] = TerrainSplatWeights.CreateSolid(rampCliffIdx);
												}
											}
										}
									}
								}
							}
						}
						modified = true;
					}
				}
			}
		}

		if (modified)
		{
			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
			TerrainSwatchProfileData? rampProf = null;
			int rampTexIdx = Realm.Client.Core.GameHost.Instance != null ? Realm.Client.Core.GameHost.Instance.EditorPaintTextureIndex : 0;
			if (!string.IsNullOrEmpty(wsPath) && MetadataService.Instance.TryLoadMetadata(wsPath, out var metaRoot) && metaRoot != null)
			{
				string? swatchName = null;
				if (Realm.Client.Core.GameHost.Instance?.GroundTerrain != null && rampTexIdx >= 0 && rampTexIdx < Realm.Client.Core.GameHost.Instance.GroundTerrain.LoadedTextureList.Count)
				{
					swatchName = Realm.Client.Core.GameHost.Instance.GroundTerrain.LoadedTextureList[rampTexIdx];
				}
				if (!string.IsNullOrEmpty(swatchName))
				{
					rampProf = metaRoot.GetTerrainProfile(swatchName);
				}
			}

			foreach (var cellPos in modifiedCells)
			{
				if (rampProf != null && terrain.PathingCodes != null && cellPos.X < width && cellPos.Y < depth)
				{
					terrain.PathingCodes[cellPos.X, cellPos.Y] = rampProf.DefaultPathingCode;
				}
				if (terrain.Cells != null && cellPos.X < width && cellPos.Y < depth)
				{
					float tY = terrain.Cells[cellPos.X, cellPos.Y].CenterHeight;
					SpawnTerrainProceduralBombing(cellPos.X, cellPos.Y, rampTexIdx, tY, quadSize, width, depth);
				}
			}

			AlignSplatMapSlots(minGridX - 2, minGridZ - 2, maxGridX + 2, maxGridZ + 2);
		}

		return modified;
	}

	public float GetMinHeightInBrushBounds(Vector3 worldPos, float brushRadius, bool brushIsSquare)
	{
		float result = GetMinHeightInBrushBoundsInternal(worldPos, brushRadius, brushIsSquare);
		return result;
	}

	public void CopyArea(int minX, int minZ, int maxX, int maxZ, List<CopiedEntityInfo> entities, bool isSquare = true)
	{
		ref var terrain = ref GetTerrainState();
		var cells = terrain.Cells;
		if (cells == null) return;

		if (_terrainSplatMap == null && Realm.Client.Core.GameHost.Instance?.GroundTerrain != null)
		{
			_terrainSplatMap = Realm.Client.Core.GameHost.Instance.GroundTerrain.SplatMap;
		}
		if (_terrainCliffSplatMap == null && Realm.Client.Core.GameHost.Instance?.GroundTerrain != null)
		{
			_terrainCliffSplatMap = Realm.Client.Core.GameHost.Instance.GroundTerrain.CliffSplatMap;
		}

		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
		var copiedCells = new TerrainCell[selWidth, selDepth];
		var splatMap = _terrainSplatMap != null ? new TerrainSplatWeights[selWidth + 1, selDepth + 1] : null;
		var cliffSplatMap = _terrainCliffSplatMap != null ? new TerrainSplatWeights[selWidth + 1, selDepth + 1] : null;
		var pathing = new int[selWidth, selDepth];
		var mask = isSquare ? null : new bool[selWidth, selDepth];

		float selCenterX = (minX + maxX) * 0.5f;
		float selCenterZ = (minZ + maxZ) * 0.5f;
		float rx = Math.Max(0.5f, (maxX - minX) * 0.5f);
		float rz = Math.Max(0.5f, (maxZ - minZ) * 0.5f);

		for (int sz = 0; sz < selDepth; sz++)
		{
			for (int sx = 0; sx < selWidth; sx++)
			{
				int sourceX = Math.Clamp(minX + sx, 0, terrain.Width - 1);
				int sourceZ = Math.Clamp(minZ + sz, 0, terrain.Depth - 1);
				bool inBounds = true;
				if (!isSquare)
				{
					float cellCenterX = minX + sx + 0.5f;
					float cellCenterZ = minZ + sz + 0.5f;
					float ndx = (cellCenterX - selCenterX) / rx;
					float ndz = (cellCenterZ - selCenterZ) / rz;
					inBounds = (ndx * ndx + ndz * ndz <= 1.05f);
				}
				if (mask != null)
				{
					mask[sx, sz] = inBounds;
				}
				if (inBounds)
				{
					copiedCells[sx, sz] = cells[sourceX, sourceZ];
					if (terrain.PathingCodes != null && sourceX < terrain.PathingCodes.GetLength(0) && sourceZ < terrain.PathingCodes.GetLength(1))
					{
						pathing[sx, sz] = terrain.PathingCodes[sourceX, sourceZ];
					}
				}
			}
		}

		if (_terrainSplatMap != null && splatMap != null)
		{
			int mapW = _terrainSplatMap.GetLength(0);
			int mapD = _terrainSplatMap.GetLength(1);
			for (int vz = 0; vz <= selDepth; vz++)
			{
				for (int vx = 0; vx <= selWidth; vx++)
				{
					int sourceVx = Math.Clamp(minX + vx, 0, mapW - 1);
					int sourceVz = Math.Clamp(minZ + vz, 0, mapD - 1);
					splatMap[vx, vz] = _terrainSplatMap[sourceVx, sourceVz];
				}
			}
		}
		if (_terrainCliffSplatMap != null && cliffSplatMap != null)
		{
			int mapCW = _terrainCliffSplatMap.GetLength(0);
			int mapCD = _terrainCliffSplatMap.GetLength(1);
			for (int vz = 0; vz <= selDepth; vz++)
			{
				for (int vx = 0; vx <= selWidth; vx++)
				{
					int sourceVx = Math.Clamp(minX + vx, 0, mapCW - 1);
					int sourceVz = Math.Clamp(minZ + vz, 0, mapCD - 1);
					cliffSplatMap[vx, vz] = _terrainCliffSplatMap[sourceVx, sourceVz];
				}
			}
		}

		_copiedArea = new CopiedAreaTemplate
		{
			Width = selWidth,
			Depth = selDepth,
			AnchorTileX = selWidth / 2,
			AnchorTileZ = selDepth / 2,
			SourceMinX = minX,
			SourceMinZ = minZ,
			SourceMaxX = maxX,
			SourceMaxZ = maxZ,
			Cells = copiedCells,
			SplatMap = splatMap,
			CliffSplatMap = cliffSplatMap,
			Pathing = pathing,
			Entities = entities,
			Mask = mask
		};
	}

	public List<CopiedEntityInfo> BuildCopiedEntityList(
		int minX, int minZ, int maxX, int maxZ,
		IEnumerable<Node3D> sceneChildren,
		bool isSquare = true)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return new List<CopiedEntityInfo>();

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		float minWorldX = (minX - width / 2.0f) * quadSize - quadSize * 0.5f;
		float maxWorldX = (maxX - width / 2.0f) * quadSize + quadSize * 0.5f;
		float minWorldZ = (minZ - depth / 2.0f) * quadSize - quadSize * 0.5f;
		float maxWorldZ = (maxZ - depth / 2.0f) * quadSize + quadSize * 0.5f;
		float centerWorldX = (minWorldX + maxWorldX) * 0.5f;
		float centerWorldZ = (minWorldZ + maxWorldZ) * 0.5f;
		float rxWorld = Math.Max(quadSize * 0.5f, (maxWorldX - minWorldX) * 0.5f);
		float rzWorld = Math.Max(quadSize * 0.5f, (maxWorldZ - minWorldZ) * 0.5f);
		Vector3 origin = new Vector3((minX - width / 2.0f) * quadSize, 0.0f, (minZ - depth / 2.0f) * quadSize);

		var entities = new List<CopiedEntityInfo>();
		foreach (var n3d in sceneChildren)
		{
			if (!GodotObject.IsInstanceValid(n3d)) continue;
			Vector3 pos = n3d.Position;
			if (pos.X >= minWorldX && pos.X <= maxWorldX && pos.Z >= minWorldZ && pos.Z <= maxWorldZ)
			{
				if (!isSquare)
				{
					float ndx = (pos.X - centerWorldX) / rxWorld;
					float ndz = (pos.Z - centerWorldZ) / rzWorld;
					if (ndx * ndx + ndz * ndz > 1.0f) continue;
				}
				if (n3d is Realm.Client.Unit3D unit)
				{
					entities.Add(new CopiedEntityInfo
					{
						Type = "unit",
						Id = unit.UnitId,
						RelativePos = pos - origin,
						Rotation = unit.RotationDegrees.Y,
						Scale = unit.Scale.X,
						IsEnemy = unit.IsEnemy
					});
				}
				else if (n3d is Realm.Client.Prop3D prop)
				{
					entities.Add(new CopiedEntityInfo
					{
						Type = "prop",
						Id = prop.PropId,
						RelativePos = pos - origin,
						Rotation = prop.RotationDegrees.Y,
						Scale = prop.Scale.X,
						IsEnemy = false
					});
				}
				else if (n3d is Decal decal)
				{
					string decalId = decal is Realm.Client.Decal3D decal3D ? decal3D.DecalId : "logo";
					entities.Add(new CopiedEntityInfo
					{
						Type = "decal",
						Id = decalId,
						RelativePos = pos - origin,
						Rotation = decal.RotationDegrees.Y,
						Scale = decal.Scale.X,
						IsEnemy = false
					});
				}
			}
		}

		if (EcsWorldAccessor.Current != null)
		{
			var propQuery = Realm.Ecs.Common.QueryCache.AllPropIdentityAndPositionQuery;
			var world = EcsWorldAccessor.Current;
			world.Query(in propQuery, (Arch.Core.Entity entity, ref PropIdentity propIdComp, ref Position posComp) =>
			{
				if (Realm.Client.Core.GameHost.EntityToProp3D.ContainsKey(entity)) return;

				Vector3 worldPos = new Vector3(posComp.Value.X, posComp.Value.Y, posComp.Value.Z);
				if (worldPos.X >= minWorldX && worldPos.X <= maxWorldX && worldPos.Z >= minWorldZ && worldPos.Z <= maxWorldZ)
				{
					if (!isSquare)
					{
						float ndx = (worldPos.X - centerWorldX) / rxWorld;
						float ndz = (worldPos.Z - centerWorldZ) / rzWorld;
						if (ndx * ndx + ndz * ndz > 1.0f) return;
					}
					float rotY = world.Has<RotationY>(entity) ? world.Get<RotationY>(entity).Value : 0f;
					float scale = world.Has<ModelScale>(entity) ? world.Get<ModelScale>(entity).Value : 1f;

					entities.Add(new CopiedEntityInfo
					{
						Type = "prop",
						Id = propIdComp.PropId,
						RelativePos = worldPos - origin,
						Rotation = rotY,
						Scale = scale,
						IsEnemy = false
					});
				}
			});
		}

		return entities;
	}

	public PasteAreaResult BuildPasteAreaResult(
		int startX,
		int startZ,
		bool pasteHeights,
		bool pasteTextures,
		bool pasteEntities,
		bool pastePathing,
		MirrorMode mirrorMode,
		float rotationDegrees,
		PasteReflection pasteReflection = PasteReflection.None)
	{
		var result = new PasteAreaResult();
		result.SpawnRequests = new List<EntitySpawnRequest>();

		if (_copiedArea == null) return result;

		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return result;

		if (_terrainSplatMap == null && Realm.Client.Core.GameHost.Instance?.GroundTerrain != null)
		{
			_terrainSplatMap = Realm.Client.Core.GameHost.Instance.GroundTerrain.SplatMap;
		}
		if (_terrainCliffSplatMap == null && Realm.Client.Core.GameHost.Instance?.GroundTerrain != null)
		{
			_terrainCliffSplatMap = Realm.Client.Core.GameHost.Instance.GroundTerrain.CliffSplatMap;
		}

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		int pasteWidth = _copiedArea.Width;
		int pasteDepth = _copiedArea.Depth;
		bool modified = false;
		bool pathingModified = false;

		float r = rotationDegrees % 360.0f;
		if (r < 0) r += 360.0f;
		int rotSteps = (int)Math.Round(r / 90.0f) % 4;
		int targetWidth = (rotSteps == 1 || rotSteps == 3) ? pasteDepth : pasteWidth;
		int targetDepth = (rotSteps == 1 || rotSteps == 3) ? pasteWidth : pasteDepth;

		PasteBlock(startX, startZ, rotSteps, pasteReflection, MirrorMode.None, 0, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);

		if (mirrorMode != MirrorMode.None)
		{
			Vector3 centerPos = new Vector3((startX + targetWidth / 2.0f - width / 2.0f) * quadSize, 0, (startZ + targetDepth / 2.0f - depth / 2.0f) * quadSize);
			var transforms = GetMirroredTransforms(centerPos, 0.0f, mirrorMode);
			if (mirrorMode == MirrorMode.Horizontal)
			{
				if (transforms.Count > 0)
				{
					var (rcx, rcz) = WorldPosToCellCoords(transforms[0].Position);
					int rStartX = rcx - targetWidth / 2;
					int rStartZ = rcz - targetDepth / 2;
					PasteBlock(rStartX, rStartZ, rotSteps, pasteReflection, MirrorMode.Horizontal, 0, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);
				}
			}
			else if (mirrorMode == MirrorMode.Vertical)
			{
				if (transforms.Count > 0)
				{
					var (rcx, rcz) = WorldPosToCellCoords(transforms[0].Position);
					int rStartX = rcx - targetWidth / 2;
					int rStartZ = rcz - targetDepth / 2;
					PasteBlock(rStartX, rStartZ, rotSteps, pasteReflection, MirrorMode.Vertical, 0, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);
				}
			}
			else if (mirrorMode == MirrorMode.Both)
			{
				if (transforms.Count >= 3)
				{
					var (rcx0, rcz0) = WorldPosToCellCoords(transforms[0].Position);
					int rStartX0 = rcx0 - targetWidth / 2;
					int rStartZ0 = rcz0 - targetDepth / 2;
					PasteBlock(rStartX0, rStartZ0, rotSteps, pasteReflection, MirrorMode.Horizontal, 0, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);

					var (rcx1, rcz1) = WorldPosToCellCoords(transforms[1].Position);
					int rStartX1 = rcx1 - targetWidth / 2;
					int rStartZ1 = rcz1 - targetDepth / 2;
					PasteBlock(rStartX1, rStartZ1, rotSteps, pasteReflection, MirrorMode.Vertical, 0, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);

					var (rcx2, rcz2) = WorldPosToCellCoords(transforms[2].Position);
					int rStartX2 = rcx2 - targetWidth / 2;
					int rStartZ2 = rcz2 - targetDepth / 2;
					PasteBlock(rStartX2, rStartZ2, rotSteps, pasteReflection, MirrorMode.Both, 0, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);
				}
			}
			else if (mirrorMode == MirrorMode.Rotational)
			{
				foreach (var t in transforms)
				{
					int rotKSteps = ((int)Math.Round(t.Rotation / 90.0f) % 4 + 4) % 4;
					int curTargetWidth = (rotKSteps == 1 || rotKSteps == 3) ? targetDepth : targetWidth;
					int curTargetDepth = (rotKSteps == 1 || rotKSteps == 3) ? targetWidth : targetDepth;
					var (rcx, rcz) = WorldPosToCellCoords(t.Position);
					int rStartX = rcx - curTargetWidth / 2;
					int rStartZ = rcz - curTargetDepth / 2;
					PasteBlock(rStartX, rStartZ, rotSteps, pasteReflection, MirrorMode.None, rotKSteps, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);
				}
			}
		}

		if (modified && pasteHeights)
		{
			SanitizeCornerHeights(ref terrain);
		}
		if (modified && pasteTextures)
		{
			AlignSplatMapSlots(0, 0, width, depth);
		}
		result.TerrainModified = modified;
		result.HeightsModified = pasteHeights && modified;
		result.PathingModified = pathingModified;

		if (pasteEntities)
		{
			Vector3 pasteCenter = new Vector3((startX + (targetWidth - 1) / 2.0f - width / 2.0f) * quadSize, 0, (startZ + (targetDepth - 1) / 2.0f - depth / 2.0f) * quadSize);

			float rad = rotationDegrees * Mathf.Pi / 180.0f;
			float cosR = Mathf.Cos(rad);
			float sinR = Mathf.Sin(rad);

			Vector3 originalCenterOffset = new Vector3((pasteWidth - 1) / 2.0f * quadSize, 0, (pasteDepth - 1) / 2.0f * quadSize);

			foreach (var ent in _copiedArea.Entities)
			{
				Vector3 relPos = ent.RelativePos;
				float entRot = ent.Rotation;

				if (pasteReflection == PasteReflection.Horizontal)
				{
					relPos = new Vector3((pasteWidth - 1) * quadSize - relPos.X, relPos.Y, relPos.Z);
					entRot = -entRot;
				}
				else if (pasteReflection == PasteReflection.Vertical)
				{
					relPos = new Vector3(relPos.X, relPos.Y, (pasteDepth - 1) * quadSize - relPos.Z);
					entRot = 180.0f - entRot;
				}

				Vector3 relativeToCenter = relPos - originalCenterOffset;

				float rx = relativeToCenter.X * cosR - relativeToCenter.Z * sinR;
				float rz = relativeToCenter.X * sinR + relativeToCenter.Z * cosR;

				Vector3 rotatedRelative = new Vector3(rx, 0, rz);
				Vector3 destPos = pasteCenter + rotatedRelative;

				destPos.Y = GetTerrainHeightAt(destPos);

				float finalRot = entRot - rotationDegrees;

				result.SpawnRequests.Add(new EntitySpawnRequest
				{
					Type = ent.Type,
					Id = ent.Id,
					Position = destPos,
					Rotation = finalRot,
					Scale = ent.Scale,
					IsEnemy = ent.IsEnemy
				});

				AddMirroredRequests(result.SpawnRequests, ent.Type, ent.Id, destPos, finalRot, ent.Scale, ent.IsEnemy, mirrorMode);
			}
		}

		return result;
	}

	private void ProcessEraseHeightsAndTextures(int minX, int minZ, int selWidth, int selDepth, bool isSquare, float selCenterX, float selCenterZ, float rx, float rz, int width, int depth, bool pasteHeights, bool pasteTextures, bool pastePathing, ref TerrainState terrain, ref bool terrainModified, ref bool pathingModified)
	{
		for (int sz = 0; sz < selDepth; sz++)
		{
			for (int sx = 0; sx < selWidth; sx++)
			{
				if (!isSquare)
				{
					float cellCenterX = minX + sx + 0.5f;
					float cellCenterZ = minZ + sz + 0.5f;
					float ndx = (cellCenterX - selCenterX) / rx;
					float ndz = (cellCenterZ - selCenterZ) / rz;
					if (ndx * ndx + ndz * ndz > 1.05f) continue;
				}

				int targetX = minX + sx;
				int targetZ = minZ + sz;
				if (targetX >= 0 && targetX < width && targetZ >= 0 && targetZ < depth)
				{
					if (pasteHeights && terrain.Cells != null)
					{
						SetGridNodeHeight(ref terrain, targetX, targetZ, 0f);
						SetGridNodeHeight(ref terrain, targetX + 1, targetZ, 0f);
						SetGridNodeHeight(ref terrain, targetX + 1, targetZ + 1, 0f);
						SetGridNodeHeight(ref terrain, targetX, targetZ + 1, 0f);
						terrain.Cells[targetX, targetZ] = default;
					}
					if (pasteTextures)
					{
						var eraseSplat = new TerrainSplatWeights { Index0 = 0, Weight0 = 1.0f };
						SetGridNodeSplat(targetX, targetZ, in eraseSplat, in eraseSplat, in eraseSplat, in eraseSplat);
						var eraseCliff = new TerrainSplatWeights { Index0 = 1, Weight0 = 1.0f };
						SetGridNodeCliffSplat(targetX, targetZ, in eraseCliff, in eraseCliff, in eraseCliff, in eraseCliff);
					}
					if (pastePathing && terrain.PathingCodes != null && terrain.Cells != null)
					{
						terrain.PathingCodes[targetX, targetZ] = Realm.Client.EditableTerrain.GetDefaultPathingCode(terrain.Cells[targetX, targetZ]);
						pathingModified = true;
					}
					terrainModified = true;
				}
			}
		}
	}

	private void ProcessEraseEntities(IEnumerable<Node3D> sceneChildren, Node3D previewNode, bool isSquare, float selCenterX, float selCenterZ, float rx, float rz, float quadSize, List<Node3D> nodesToDelete)
	{
		foreach (var n in sceneChildren)
		{
			if (n == previewNode || !n.IsInsideTree()) continue;
			if (n is Realm.Client.Unit3D or Realm.Client.Prop3D or Realm.Client.Decal3D or Realm.Client.VFX.ProceduralVfxInstance3D)
			{
				var pos = n.GlobalPosition;
				bool inBounds = false;
				if (isSquare)
				{
					float dx = Mathf.Abs(pos.X - selCenterX);
					float dz = Mathf.Abs(pos.Z - selCenterZ);
					inBounds = (dx <= rx * quadSize && dz <= rz * quadSize);
				}
				else
				{
					float dx = (pos.X - selCenterX * quadSize) / (rx * quadSize);
					float dz = (pos.Z - selCenterZ * quadSize) / (rz * quadSize);
					inBounds = (dx * dx + dz * dz <= 1.0f);
				}
				if (inBounds) nodesToDelete.Add(n);
			}
		}
	}

	public EraseAreaResult BuildEraseAreaResult(
		int minX, int minZ, int maxX, int maxZ,
		bool pasteHeights,
		bool pasteTextures,
		bool pasteEntities,
		bool pastePathing,
		IEnumerable<Node3D> sceneChildren,
		Node3D previewNode,
		bool isSquare = true)
	{
		var result = new EraseAreaResult();
		result.NodesToDelete = new List<Node3D>();

		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return result;

		if (_terrainSplatMap == null && Realm.Client.Core.GameHost.Instance?.GroundTerrain != null)
		{
			_terrainSplatMap = Realm.Client.Core.GameHost.Instance.GroundTerrain.SplatMap;
		}
		if (_terrainCliffSplatMap == null && Realm.Client.Core.GameHost.Instance?.GroundTerrain != null)
		{
			_terrainCliffSplatMap = Realm.Client.Core.GameHost.Instance.GroundTerrain.CliffSplatMap;
		}

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
		bool terrainModified = false;
		bool pathingModified = false;

		float selCenterX = (minX + maxX) * 0.5f;
		float selCenterZ = (minZ + maxZ) * 0.5f;
		float rx = Math.Max(0.5f, (maxX - minX) * 0.5f);
		float rz = Math.Max(0.5f, (maxZ - minZ) * 0.5f);

		if (pasteHeights || pasteTextures || (pastePathing && terrain.PathingCodes != null))
		{
			for (int sz = 0; sz < selDepth; sz++)
			{
				for (int sx = 0; sx < selWidth; sx++)
				{
					if (!isSquare)
					{
						float cellCenterX = minX + sx + 0.5f;
						float cellCenterZ = minZ + sz + 0.5f;
						float ndx = (cellCenterX - selCenterX) / rx;
						float ndz = (cellCenterZ - selCenterZ) / rz;
						if (ndx * ndx + ndz * ndz > 1.05f) continue;
					}

					int targetX = minX + sx;
					int targetZ = minZ + sz;
					if (targetX >= 0 && targetX < width && targetZ >= 0 && targetZ < depth)
					{
						if (pasteHeights && terrain.Cells != null)
						{
							SetGridNodeHeight(ref terrain, targetX, targetZ, 0f);
							SetGridNodeHeight(ref terrain, targetX + 1, targetZ, 0f);
							SetGridNodeHeight(ref terrain, targetX + 1, targetZ + 1, 0f);
							SetGridNodeHeight(ref terrain, targetX, targetZ + 1, 0f);
							terrain.Cells[targetX, targetZ] = default;
						}
						if (pasteTextures)
						{
							var defaultGround = TerrainSplatWeights.CreateSolid(0);
							var defaultCliff = TerrainSplatWeights.CreateSolid(1);
							SetGridNodeSplat(targetX, targetZ, in defaultGround, in defaultGround, in defaultGround, in defaultGround);
							SetGridNodeCliffSplat(targetX, targetZ, in defaultCliff, in defaultCliff, in defaultCliff, in defaultCliff);
						}
						if (pastePathing && terrain.PathingCodes != null)
						{
							terrain.PathingCodes[targetX, targetZ] = Realm.Client.EditableTerrain.PATHING_GROUND | Realm.Client.EditableTerrain.PATHING_FLYING;
							pathingModified = true;
						}
						terrainModified = true;
					}
				}
			}
			if (terrainModified && pasteHeights)
			{
				SanitizeCornerHeights(ref terrain);
			}
			if (terrainModified && pasteTextures)
			{
				AlignSplatMapSlots(minX - 2, minZ - 2, maxX + 2, maxZ + 2);
			}

		}

		result.TerrainModified = terrainModified;
		result.HeightsModified = pasteHeights && terrainModified;
		result.PathingModified = pathingModified;

		if (pasteEntities)
		{
			float minWorldX = (minX - width / 2.0f) * quadSize - quadSize * 0.5f;
			float maxWorldX = (maxX - width / 2.0f) * quadSize + quadSize * 0.5f;
			float minWorldZ = (minZ - depth / 2.0f) * quadSize - quadSize * 0.5f;
			float maxWorldZ = (maxZ - depth / 2.0f) * quadSize + quadSize * 0.5f;
			float centerWorldX = (minWorldX + maxWorldX) * 0.5f;
			float centerWorldZ = (minWorldZ + maxWorldZ) * 0.5f;
			float rxWorld = Math.Max(quadSize * 0.5f, (maxWorldX - minWorldX) * 0.5f);
			float rzWorld = Math.Max(quadSize * 0.5f, (maxWorldZ - minWorldZ) * 0.5f);

			foreach (var n3d in sceneChildren)
			{
				if (!GodotObject.IsInstanceValid(n3d) || n3d == previewNode) continue;
				Vector3 pos = n3d.Position;
				if (pos.X >= minWorldX && pos.X <= maxWorldX && pos.Z >= minWorldZ && pos.Z <= maxWorldZ)
				{
					if (!isSquare)
					{
						float ndx = (pos.X - centerWorldX) / rxWorld;
						float ndz = (pos.Z - centerWorldZ) / rzWorld;
						if (ndx * ndx + ndz * ndz > 1.0f) continue;
					}
					if (n3d is Realm.Client.Unit3D || n3d is Realm.Client.Prop3D || n3d is Decal)
					{
						result.NodesToDelete.Add(n3d);
					}
				}
			}

			if (EcsWorldAccessor.Current != null)
			{
				var world = EcsWorldAccessor.Current;
				var staticPropsToDestroy = new List<(Arch.Core.Entity Entity, string PropId)>();
				var propQuery = Realm.Ecs.Common.QueryCache.AllPropIdentityAndPositionQuery;
				world.Query(in propQuery, (Arch.Core.Entity entity, ref PropIdentity propIdComp, ref Position posComp) =>
				{
					if (Realm.Client.Core.GameHost.EntityToProp3D.ContainsKey(entity)) return;
					Vector3 wPos = new Vector3(posComp.Value.X, posComp.Value.Y, posComp.Value.Z);
					if (wPos.X >= minWorldX && wPos.X <= maxWorldX && wPos.Z >= minWorldZ && wPos.Z <= maxWorldZ)
					{
						if (!isSquare)
						{
							float ndx = (wPos.X - centerWorldX) / rxWorld;
							float ndz = (wPos.Z - centerWorldZ) / rzWorld;
							if (ndx * ndx + ndz * ndz > 1.0f) return;
						}
						staticPropsToDestroy.Add((entity, propIdComp.PropId));
					}
				});

				foreach (var sp in staticPropsToDestroy)
				{
					if (world.IsAlive(sp.Entity))
					{
						world.Destroy(sp.Entity);
					}
					Realm.Client.PropMultiMeshManager.Instance?.MarkDirty(sp.PropId);
				}
			}
		}

		return result;
	}

	private int GetDominantTextureIndex(TerrainSplatWeights splat)
	{
		return splat.GetDominantIndex();
	}

	private void ProcessFloodFillNeighbor(int currX, int currZ, int nextX, int nextZ, int splatW, int splatD, bool[,] visited, TerrainSplatWeights[,] splatBefore, int startDominantIndex, bool isCliff, ref TerrainState terrain, Queue<(int x, int z)> queue)
	{
		if (nextX < 0 || nextX >= splatW || nextZ < 0 || nextZ >= splatD) return;
		if (visited[nextX, nextZ]) return;
		if (splatBefore[nextX, nextZ].GetDominantIndex() != startDominantIndex) return;

		if (!isCliff)
		{
			float hCurrent = GetGridNodeHeight(in terrain, currX, currZ);
			float hNext = GetGridNodeHeight(in terrain, nextX, nextZ);
			if (Mathf.Abs(hNext - hCurrent) >= 1.0f) return;
		}

		visited[nextX, nextZ] = true;
		queue.Enqueue((nextX, nextZ));
	}

	private List<Vector2I> GetFloodFillCells(Vector3 clickPos, TerrainSplatWeights[,] splatBefore, int width, int depth, float quadSize, bool[,] visited, bool isCliff)
	{
		ref var terrain = ref GetTerrainState();
		var cells = new List<Vector2I>();

		int splatW = splatBefore.GetLength(0);
		int splatD = splatBefore.GetLength(1);

		float startFx = clickPos.X / quadSize + width / 2.0f;
		float startFz = clickPos.Z / quadSize + depth / 2.0f;
		int clickX = Mathf.Clamp((int)Math.Round(startFx), 0, splatW - 1);
		int clickZ = Mathf.Clamp((int)Math.Round(startFz), 0, splatD - 1);
		int startDominantIndex = splatBefore[clickX, clickZ].GetDominantIndex();

		var queue = new Queue<(int x, int z)>();
		if (!visited[clickX, clickZ])
		{
			queue.Enqueue((clickX, clickZ));
			visited[clickX, clickZ] = true;
		}

		while (queue.Count > 0)
		{
			var (currX, currZ) = queue.Dequeue();
			cells.Add(new Vector2I(currX, currZ));

			int[] dx = { 0, 0, -1, 1 };
			int[] dz = { -1, 1, 0, 0 };
			for (int i = 0; i < 4; i++)
			{
				ProcessFloodFillNeighbor(currX, currZ, currX + dx[i], currZ + dz[i], splatW, splatD, visited, splatBefore, startDominantIndex, isCliff, ref terrain, queue);
			}
		}

		return cells;
	}

	private List<Vector2I> GetFloodFillArea(
		Vector3 clickPos,
		TerrainSplatWeights[,] splatBefore,
		int width,
		int depth,
		float quadSize,
		bool[,] visited,
		MirrorMode mirrorMode,
		bool isCliff,
		Func<int, int, bool> shouldFillCell)
	{
		var cells = new List<Vector2I>();
		int splatW = splatBefore.GetLength(0);
		int splatD = splatBefore.GetLength(1);

		float startFx = clickPos.X / quadSize + width / 2.0f;
		float startFz = clickPos.Z / quadSize + depth / 2.0f;
		int startX = Mathf.Clamp((int)Math.Round(startFx), 0, splatW - 1);
		int startZ = Mathf.Clamp((int)Math.Round(startFz), 0, splatD - 1);

		if (shouldFillCell(startX, startZ))
		{
			cells.AddRange(GetFloodFillCells(clickPos, splatBefore, width, depth, quadSize, visited, isCliff));
		}

		if (mirrorMode != MirrorMode.None)
		{
			var mirrors = GetMirroredPositions(clickPos, mirrorMode);
			foreach (var m in mirrors)
			{
				float mFx = m.X / quadSize + width / 2.0f;
				float mFz = m.Z / quadSize + depth / 2.0f;
				int mX = Mathf.Clamp((int)Math.Round(mFx), 0, splatW - 1);
				int mZ = Mathf.Clamp((int)Math.Round(mFz), 0, splatD - 1);
				if (shouldFillCell(mX, mZ))
				{
					cells.AddRange(GetFloodFillCells(m, splatBefore, width, depth, quadSize, visited, isCliff));
				}
			}
		}

		return cells;
	}

	public (float[,]? Heights, TerrainSplatWeights[,]? SplatMap, bool IsCliff) PerformFloodFill(
		Vector3 clickPos,
		int fillTextureIndex,
		int cliffTextureIndex,
		MirrorMode mirrorMode,
		bool isCliff = false)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return (null, null, isCliff);

		if (_terrainSplatMap == null && Realm.Client.Core.GameHost.Instance?.GroundTerrain != null)
		{
			_terrainSplatMap = Realm.Client.Core.GameHost.Instance.GroundTerrain.SplatMap;
		}
		if (_terrainCliffSplatMap == null && Realm.Client.Core.GameHost.Instance?.GroundTerrain != null)
		{
			_terrainCliffSplatMap = Realm.Client.Core.GameHost.Instance.GroundTerrain.CliffSplatMap;
		}

		if (_terrainSplatMap == null) return (null, null, isCliff);

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		TerrainSplatWeights[,] targetSplatMap = (isCliff && _terrainCliffSplatMap != null) ? _terrainCliffSplatMap : _terrainSplatMap;
		int targetTextureIndex = isCliff ? cliffTextureIndex : fillTextureIndex;

		int splatW = targetSplatMap.GetLength(0);
		int splatD = targetSplatMap.GetLength(1);

		var splatBefore = (TerrainSplatWeights[,])targetSplatMap.Clone();
		var visited = new bool[splatW, splatD];

		var cells = GetFloodFillArea(
			clickPos,
			splatBefore,
			width,
			depth,
			quadSize,
			visited,
			mirrorMode,
			isCliff,
			(x, z) => !splatBefore[x, z].IsSolid(targetTextureIndex)
		);

		if (cells.Count == 0) return (null, null, isCliff);

		foreach (var cell in cells)
		{
			targetSplatMap[cell.X, cell.Y] = TerrainSplatWeights.CreateSolid(targetTextureIndex);
		}

		int minGridX = splatW;
		int maxGridX = 0;
		int minGridZ = splatD;
		int maxGridZ = 0;
		foreach (var cell in cells)
		{
			if (cell.X < minGridX) minGridX = cell.X;
			if (cell.X > maxGridX) maxGridX = cell.X;
			if (cell.Y < minGridZ) minGridZ = cell.Y;
			if (cell.Y > maxGridZ) maxGridZ = cell.Y;
		}
		AlignSplatMapSlots(minGridX - 2, minGridZ - 2, maxGridX + 2, maxGridZ + 2);

		return (null, (TerrainSplatWeights[,])targetSplatMap.Clone(), isCliff);
	}

	private List<Vector2I> GetFloodFillPathingCells(
		Vector3 clickPos,
		TerrainSplatWeights[,] splatBefore,
		int width,
		int depth,
		float quadSize,
		bool[,] visited)
	{
		ref var terrain = ref GetTerrainState();
		var cells = new List<Vector2I>();

		int splatW = splatBefore != null ? splatBefore.GetLength(0) : 0;
		int splatD = splatBefore != null ? splatBefore.GetLength(1) : 0;

		float startFx = clickPos.X / quadSize + width / 2.0f;
		float startFz = clickPos.Z / quadSize + depth / 2.0f;
		int clickX = Mathf.Clamp((int)Math.Floor(startFx), 0, width - 1);
		int clickZ = Mathf.Clamp((int)Math.Floor(startFz), 0, depth - 1);

		int startDominantIndex = (splatBefore != null && clickX < splatW && clickZ < splatD)
			? splatBefore[clickX, clickZ].GetDominantIndex()
			: 0;

		WaterType startWaterType = terrain.Cells != null ? terrain.Cells[clickX, clickZ].WaterMode : WaterType.None;
		float startHeight = terrain.Cells != null ? terrain.Cells[clickX, clickZ].CenterHeight : clickPos.Y;

		var queue = new Queue<(int x, int z)>();
		if (!visited[clickX, clickZ])
		{
			queue.Enqueue((clickX, clickZ));
			visited[clickX, clickZ] = true;
		}

		while (queue.Count > 0)
		{
			var (currX, currZ) = queue.Dequeue();
			cells.Add(new Vector2I(currX, currZ));

			int[] dx = { 0, 0, -1, 1 };
			int[] dz = { -1, 1, 0, 0 };
			for (int i = 0; i < 4; i++)
			{
				int nextX = currX + dx[i];
				int nextZ = currZ + dz[i];
				if (nextX >= 0 && nextX < width && nextZ >= 0 && nextZ < depth)
				{
					if (!visited[nextX, nextZ])
					{
						if (splatBefore != null && nextX < splatW && nextZ < splatD)
						{
							if (splatBefore[nextX, nextZ].GetDominantIndex() != startDominantIndex) continue;
						}
						if (terrain.Cells != null)
						{
							if (startWaterType != terrain.Cells[nextX, nextZ].WaterMode) continue;

							float hNext = terrain.Cells[nextX, nextZ].CenterHeight;
							if (Mathf.Abs(hNext - startHeight) >= 3.0f) continue;
						}
						visited[nextX, nextZ] = true;
						queue.Enqueue((nextX, nextZ));
					}
				}
			}
		}

		return cells;
	}

	private List<Vector2I> GetFloodFillPathingArea(
		Vector3 clickPos,
		TerrainSplatWeights[,] splatBefore,
		int width,
		int depth,
		float quadSize,
		bool[,] visited,
		MirrorMode mirrorMode,
		Func<int, int, bool> shouldFillCell)
	{
		var cells = new List<Vector2I>();

		float startFx = clickPos.X / quadSize + width / 2.0f;
		float startFz = clickPos.Z / quadSize + depth / 2.0f;
		int startX = Mathf.Clamp((int)Math.Floor(startFx), 0, width - 1);
		int startZ = Mathf.Clamp((int)Math.Floor(startFz), 0, depth - 1);

		if (shouldFillCell(startX, startZ))
		{
			cells.AddRange(GetFloodFillPathingCells(clickPos, splatBefore, width, depth, quadSize, visited));
		}

		if (mirrorMode != MirrorMode.None)
		{
			var mirrors = GetMirroredPositions(clickPos, mirrorMode);
			foreach (var m in mirrors)
			{
				float mFx = m.X / quadSize + width / 2.0f;
				float mFz = m.Z / quadSize + depth / 2.0f;
				int mX = Mathf.Clamp((int)Math.Floor(mFx), 0, width - 1);
				int mZ = Mathf.Clamp((int)Math.Floor(mFz), 0, depth - 1);
				if (shouldFillCell(mX, mZ))
				{
					cells.AddRange(GetFloodFillPathingCells(m, splatBefore, width, depth, quadSize, visited));
				}
			}
		}

		return cells;
	}

	private bool CheckPathingCondition(int[,] pathingBefore, int x, int z, int pathingMask, bool pathingAdd)
	{
		int current = pathingBefore[x, z] & pathingMask;
		if (pathingAdd) return current != pathingMask;
		return current != 0;
	}

	public (int[,]? Before, int[,]? After) PerformFloodFillPathing(Vector3 clickPos, int pathingMask, bool pathingAdd, MirrorMode mirrorMode)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null || terrain.PathingCodes == null) return (null, null);

		if (_terrainSplatMap == null && Realm.Client.Core.GameHost.Instance?.GroundTerrain != null)
		{
			_terrainSplatMap = Realm.Client.Core.GameHost.Instance.GroundTerrain.SplatMap;
		}

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;
		var splatBefore = _terrainSplatMap != null ? (TerrainSplatWeights[,])_terrainSplatMap.Clone() : null;
		var pathingBefore = (int[,])terrain.PathingCodes.Clone();
		var visited = new bool[width, depth];

		var pathingCodes = terrain.PathingCodes;

		var cells = GetFloodFillPathingArea(
			clickPos,
			splatBefore,
			width,
			depth,
			quadSize,
			visited,
			mirrorMode,
			(x, z) => CheckPathingCondition(pathingBefore, x, z, pathingMask, pathingAdd)
		);

		if (cells.Count == 0) return (null, null);

		foreach (var cell in cells)
		{
			if (pathingAdd)
				pathingCodes[cell.X, cell.Y] |= pathingMask;
			else
				pathingCodes[cell.X, cell.Y] &= ~pathingMask;
		}

		return (pathingBefore, (int[,])pathingCodes.Clone());
	}

	private void ProcessWaterFloodFillNeighbor(TerrainCell currCell, TerrainCell nextCell, int nextX, int nextZ, bool isRemoveAction, float stepCliffThreshold, float baseWaterLevel, bool[,] visited, Queue<(int x, int z)> queue)
	{
		if (isRemoveAction)
		{
			if (nextCell.WaterMode != WaterType.None)
			{
				visited[nextX, nextZ] = true;
				queue.Enqueue((nextX, nextZ));
			}
			return;
		}

		float currHeight = currCell.CenterHeight;
		float nextHeight = nextCell.CenterHeight;
		float deltaH = nextHeight - currHeight;

		if (deltaH <= 0.05f)
		{
			visited[nextX, nextZ] = true;
			queue.Enqueue((nextX, nextZ));
			return;
		}

		float nextMaxH = Mathf.Max(Mathf.Max(nextCell.Y_NW, nextCell.Y_NE), Mathf.Max(nextCell.Y_SW, nextCell.Y_SE));
		float nextMinH = Mathf.Min(Mathf.Min(nextCell.Y_NW, nextCell.Y_NE), Mathf.Min(nextCell.Y_SW, nextCell.Y_SE));
		float nextCellInternalSpan = nextMaxH - nextMinH;

		bool isCliffStep = (nextCell.MacroTier - currCell.MacroTier >= 1) || deltaH >= stepCliffThreshold || nextCellInternalSpan >= stepCliffThreshold;
		if (!isCliffStep)
		{
			visited[nextX, nextZ] = true;
			queue.Enqueue((nextX, nextZ));
			return;
		}

		float obstacleHeight = Mathf.Max(nextMaxH, (float)nextCell.MacroTier * TerrainCell.TIER_HEIGHT);
		if (baseWaterLevel >= obstacleHeight)
		{
			visited[nextX, nextZ] = true;
			queue.Enqueue((nextX, nextZ));
		}
	}

	private List<Vector2I> GetWaterFloodFillCells(
		Vector3 clickPos,
		TerrainCell[,] cellsBefore,
		int width,
		int depth,
		float quadSize,
		bool[,] visited,
		float waterHeight,
		bool isRemoveAction = false)
	{
		var resultCells = new List<Vector2I>();

		float startFx = clickPos.X / quadSize + width / 2.0f;
		float startFz = clickPos.Z / quadSize + depth / 2.0f;
		int clickX = Mathf.Clamp((int)Math.Floor(startFx), 0, width - 1);
		int clickZ = Mathf.Clamp((int)Math.Floor(startFz), 0, depth - 1);

		if (isRemoveAction && cellsBefore[clickX, clickZ].WaterMode == WaterType.None)
		{
			return resultCells;
		}

		var queue = new Queue<(int x, int z)>();
		if (!visited[clickX, clickZ])
		{
			queue.Enqueue((clickX, clickZ));
			visited[clickX, clickZ] = true;
		}

		int[] dx = { 0, 0, -1, 1 };
		int[] dz = { -1, 1, 0, 0 };

		float effectiveWaterHeight = waterHeight > 0.001f ? waterHeight : 0.9f;
		float startTerrainHeight = clickPos.Y;
		if (float.IsNaN(startTerrainHeight) || float.IsInfinity(startTerrainHeight))
		{
			startTerrainHeight = cellsBefore[clickX, clickZ].CenterHeight;
		}
		float baseWaterLevel = startTerrainHeight + effectiveWaterHeight;
		float stepCliffThreshold = TerrainCell.TIER_HEIGHT * 0.70f;

		while (queue.Count > 0)
		{
			var (currX, currZ) = queue.Dequeue();
			resultCells.Add(new Vector2I(currX, currZ));

			var currCell = cellsBefore[currX, currZ];

			for (int i = 0; i < 4; i++)
			{
				int nextX = currX + dx[i];
				int nextZ = currZ + dz[i];

				if (nextX >= 0 && nextX < width && nextZ >= 0 && nextZ < depth)
				{
					if (!visited[nextX, nextZ])
					{
						var nextCell = cellsBefore[nextX, nextZ];

						ProcessWaterFloodFillNeighbor(currCell, nextCell, nextX, nextZ, isRemoveAction, stepCliffThreshold, baseWaterLevel, visited, queue);
					}
				}
			}
		}

		return resultCells;
	}

	private List<Vector2I> GetWaterFloodFillArea(
		Vector3 clickPos,
		TerrainCell[,] cellsBefore,
		int width,
		int depth,
		float quadSize,
		bool[,] visited,
		MirrorMode mirrorMode,
		float waterHeight,
		bool isRemoveAction = false)
	{
		var areaCells = new List<Vector2I>();

		areaCells.AddRange(GetWaterFloodFillCells(clickPos, cellsBefore, width, depth, quadSize, visited, waterHeight, isRemoveAction));

		if (mirrorMode != MirrorMode.None)
		{
			var mirrors = GetMirroredPositions(clickPos, mirrorMode);
			foreach (var m in mirrors)
			{
				areaCells.AddRange(GetWaterFloodFillCells(m, cellsBefore, width, depth, quadSize, visited, waterHeight, isRemoveAction));
			}
		}

		return areaCells;
	}

	public (TerrainCell[,]? BeforeCells, TerrainCell[,]? AfterCells, int[,]? BeforePathing, int[,]? AfterPathing, bool WasAdded) PerformWaterFloodFill(
		Vector3 clickPos,
		WaterType activeWaterMode,
		byte activeWaterProfile,
		float waterHeight,
		MirrorMode mirrorMode,
		bool isRemoveAction = false)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null || terrain.PathingCodes == null) return (null, null, null, null, false);

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		var cells = terrain.Cells;

		bool isRemoving = isRemoveAction;
		WaterType targetWaterMode = isRemoving ? WaterType.None : (activeWaterMode != WaterType.None ? activeWaterMode : WaterType.Shallow);
		byte targetWaterProfile = isRemoving ? (byte)0 : activeWaterProfile;
		float targetWaterHeight = isRemoving ? 0f : (waterHeight > 0.001f ? waterHeight : 0.9f);

		var beforeCells = (TerrainCell[,])cells.Clone();
		var beforePathing = (int[,])terrain.PathingCodes.Clone();

		var visited = new bool[width, depth];
		var filledCells = GetWaterFloodFillArea(clickPos, beforeCells, width, depth, quadSize, visited, mirrorMode, waterHeight, isRemoveAction);

		if (filledCells.Count == 0) return (null, null, null, null, false);

		foreach (var cellPos in filledCells)
		{
			int x = cellPos.X;
			int z = cellPos.Y;

			cells[x, z].WaterMode = targetWaterMode;
			cells[x, z].WaterProfileIndex = targetWaterProfile;
			cells[x, z].WaterHeight = targetWaterHeight;

			if (targetWaterMode != WaterType.None)
			{
				var waterProf = Realm.Client.RuntimeTerrain.Instance?.GetWaterProfile(targetWaterProfile);
				terrain.PathingCodes[x, z] = waterProf != null ? waterProf.DefaultPathingCode : Realm.Client.EditableTerrain.GetDefaultPathingCode(targetWaterMode);
				float wY = (cells[x, z].MacroTier * TerrainCell.TIER_HEIGHT) + (targetWaterHeight > 0.001f ? targetWaterHeight : Realm.Client.RuntimeTerrain.WATER_DELTA);
				SpawnWaterProceduralBombing(x, z, targetWaterProfile, wY, quadSize, width, depth);
			}
			else
			{
				ClearWaterProceduralObjects(x, z);
				terrain.PathingCodes[x, z] = Realm.Client.EditableTerrain.GetDefaultPathingCode(cells[x, z]);
			}
		}

		var afterCells = (TerrainCell[,])cells.Clone();
		var afterPathing = (int[,])terrain.PathingCodes.Clone();

		return (beforeCells, afterCells, beforePathing, afterPathing, !isRemoving);
	}

	public List<Realm.Client.Core.GameHost.MirroredTransform> GetMirroredTransforms(
		Vector3 pos,
		float rotation,
		MirrorMode mirrorMode,
		Vector2? pivot = null,
		int? folds = null)
	{
		var list = new List<Realm.Client.Core.GameHost.MirroredTransform>();
		if (mirrorMode == MirrorMode.None) return list;

		Vector2 p = pivot ?? _symmetryPivot;
		int n = folds ?? _symmetryFolds;
		if (n < 2) n = 2;

		float dx = pos.X - p.X;
		float dz = pos.Z - p.Y;

		bool isHoriz = mirrorMode == MirrorMode.Horizontal || mirrorMode == MirrorMode.Both;
		bool isVert = mirrorMode == MirrorMode.Vertical || mirrorMode == MirrorMode.Both;
		if (isHoriz) list.Add(new Realm.Client.Core.GameHost.MirroredTransform { Position = new Vector3(p.X - dx, pos.Y, p.Y + dz), Rotation = 180.0f - rotation });
		if (isVert) list.Add(new Realm.Client.Core.GameHost.MirroredTransform { Position = new Vector3(p.X + dx, pos.Y, p.Y - dz), Rotation = -rotation });
		if (mirrorMode == MirrorMode.Both) list.Add(new Realm.Client.Core.GameHost.MirroredTransform { Position = new Vector3(p.X - dx, pos.Y, p.Y - dz), Rotation = rotation + 180.0f });
		if (mirrorMode == MirrorMode.Rotational)
		{
			for (int k = 1; k < n; k++)
			{
				float angleRad = k * (Mathf.Tau / n);
				float cosA = Mathf.Cos(angleRad);
				float sinA = Mathf.Sin(angleRad);
				float rx = p.X + dx * cosA - dz * sinA;
				float rz = p.Y + dx * sinA + dz * cosA;
				float rotK = (rotation + k * (360.0f / n)) % 360.0f;
				if (rotK < 0) rotK += 360.0f;
				list.Add(new Realm.Client.Core.GameHost.MirroredTransform { Position = new Vector3(rx, pos.Y, rz), Rotation = rotK });
			}
		}

		return list;
	}

	public (int startX, int startZ, int targetWidth, int targetDepth) GetAnchoredPasteBounds(int targetX, int targetZ, float rotationDegrees, PasteReflection reflection = PasteReflection.None)
	{
		if (_copiedArea == null) return (targetX, targetZ, 0, 0);

		float r = rotationDegrees % 360.0f;
		if (r < 0) r += 360.0f;
		int rotSteps = (int)Math.Round(r / 90.0f) % 4;

		int pasteWidth = _copiedArea.Width;
		int pasteDepth = _copiedArea.Depth;
		int targetWidth = pasteWidth;
		int targetDepth = pasteDepth;
		if (rotSteps == 1 || rotSteps == 3)
		{
			targetWidth = pasteDepth;
			targetDepth = pasteWidth;
		}

		int anchorX = _copiedArea.AnchorTileX;
		int anchorZ = _copiedArea.AnchorTileZ;

		if (reflection == PasteReflection.Horizontal) anchorX = pasteWidth - 1 - anchorX;
		else if (reflection == PasteReflection.Vertical) anchorZ = pasteDepth - 1 - anchorZ;

		int rotAnchorX = anchorX;
		int rotAnchorZ = anchorZ;
		if (rotSteps == 1)
		{
			rotAnchorX = pasteDepth - 1 - anchorZ;
			rotAnchorZ = anchorX;
		}
		else if (rotSteps == 2)
		{
			rotAnchorX = pasteWidth - 1 - anchorX;
			rotAnchorZ = pasteDepth - 1 - anchorZ;
		}
		else if (rotSteps == 3)
		{
			rotAnchorX = anchorZ;
			rotAnchorZ = pasteWidth - 1 - anchorX;
		}

		return (targetX - rotAnchorX, targetZ - rotAnchorZ, targetWidth, targetDepth);
	}

	public (int minX, int minZ, int maxX, int maxZ) GetCurrentSelectionBounds()
	{
		if (_selectionStart == null || _selectionEnd == null) return (0, 0, 0, 0);
		int minX = Mathf.Min(_selectionStart.Value.X, _selectionEnd.Value.X);
		int minZ = Mathf.Min(_selectionStart.Value.Y, _selectionEnd.Value.Y);
		int maxX = Mathf.Max(_selectionStart.Value.X, _selectionEnd.Value.X);
		int maxZ = Mathf.Max(_selectionStart.Value.Y, _selectionEnd.Value.Y);
		return (minX, minZ, maxX, maxZ);
	}

	public (int cx, int cz) WorldPosToCellCoords(Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;
		float fx = worldPos.X / quadSize + width / 2.0f;
		float fz = worldPos.Z / quadSize + depth / 2.0f;
		int cx = Mathf.Clamp(Mathf.FloorToInt(fx), 0, width - 1);
		int cz = Mathf.Clamp(Mathf.FloorToInt(fz), 0, depth - 1);
		return (cx, cz);
	}

	public Vector3 SnapToGrid(Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return worldPos;
		float quadSize = terrain.QuadSize;
		int width = terrain.Width;
		int depth = terrain.Depth;
		float fx = Mathf.Round(worldPos.X / quadSize + width / 2.0f);
		worldPos.X = (Mathf.Clamp(fx, 0, width) - width / 2.0f) * quadSize;
		float fz = Mathf.Round(worldPos.Z / quadSize + depth / 2.0f);
		worldPos.Z = (Mathf.Clamp(fz, 0, depth) - depth / 2.0f) * quadSize;
		return worldPos;
	}

	public Vector3 SnapToVertex(Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return worldPos;
		float quadSize = terrain.QuadSize;
		int width = terrain.Width;
		int depth = terrain.Depth;
		float fx = Mathf.Round(worldPos.X / quadSize + width / 2.0f);
		int vx = Mathf.Clamp((int)fx, 0, width);
		float fz = Mathf.Round(worldPos.Z / quadSize + depth / 2.0f);
		int vz = Mathf.Clamp((int)fz, 0, depth);
		float worldX = (vx - width / 2.0f) * quadSize;
		float worldZ = (vz - depth / 2.0f) * quadSize;
		float height = Realm.Client.RuntimeTerrain.GetGridNodeHeight(vx, vz, terrain.Cells, width, depth);
		return new Vector3(worldX, height, worldZ);
	}

	public (int vx, int vz) WorldPosToVertexCoords(Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;
		float fx = worldPos.X / quadSize + width / 2.0f;
		float fz = worldPos.Z / quadSize + depth / 2.0f;
		int vx = Mathf.Clamp((int)Math.Round(fx), 0, width);
		int vz = Mathf.Clamp((int)Math.Round(fz), 0, depth);
		return (vx, vz);
	}

	private ref TerrainState GetTerrainState()
	{
		var worldQuery = Realm.Ecs.Common.QueryCache.AllTerrainStateQuery;
		Entity worldEntity = Entity.Null;
		EcsWorld.Query(in worldQuery, (Entity entity) => worldEntity = entity);
		if (worldEntity != Entity.Null && EcsWorld.IsAlive(worldEntity))
		{
			return ref EcsWorld.Get<TerrainState>(worldEntity);
		}
		throw new InvalidOperationException("TerrainState entity not found in ECS world.");
	}

	private float GetMinHeightInBrushBoundsInternal(Vector3 worldPos, float brushRadius = -1f, bool brushIsSquare = false)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return GetTerrainHeightAt(worldPos);

		float quadSize = terrain.QuadSize;
		int width = terrain.Width;
		int depth = terrain.Depth;
		float minHeight = float.MaxValue;
		bool foundAny = false;

		for (int z = 0; z <= depth; z++)
		{
			for (int x = 0; x <= width; x++)
			{
				float vx = (x - width / 2.0f) * quadSize;
				float vz = (z - depth / 2.0f) * quadSize;

				bool inBounds = false;
				if (brushRadius < 0)
				{
					inBounds = true;
				}
				else if (brushIsSquare)
				{
					float dx = Mathf.Abs(vx - worldPos.X);
					float dz = Mathf.Abs(vz - worldPos.Z);
					inBounds = dx <= brushRadius && dz <= brushRadius;
				}
				else
				{
					float dist = new Vector2(vx - worldPos.X, vz - worldPos.Z).Length();
					inBounds = dist <= brushRadius;
				}

				if (inBounds)
				{
					float h = GetGridNodeHeight(in terrain, x, z);
					if (h < minHeight)
					{
						minHeight = h;
						foundAny = true;
					}
				}
			}
		}

		return foundAny ? minHeight : GetTerrainHeightAt(worldPos);
	}

	private void AddMirroredRequests(
		List<EntitySpawnRequest> requests,
		string type, string id,
		Vector3 pos, float rotation, float scale,
		bool isEnemy,
		MirrorMode mirrorMode)
	{
		var mirrored = GetMirroredTransforms(pos, rotation, mirrorMode);
		foreach (var m in mirrored)
		{
			Vector3 mPos = m.Position;
			mPos.Y = GetTerrainHeightAt(mPos);
			requests.Add(new EntitySpawnRequest { Type = type, Id = id, Position = mPos, Rotation = m.Rotation, Scale = scale, IsEnemy = isEnemy });
		}
	}

	public List<Vector3> GetMirroredPositions(Vector3 pos, MirrorMode mirrorMode)
	{
		var list = new List<Vector3>();
		var transforms = GetMirroredTransforms(pos, 0.0f, mirrorMode);
		foreach (var t in transforms)
		{
			list.Add(t.Position);
		}
		return list;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static TerrainCell RotateCell(in TerrainCell cell, int rotSteps)
	{
		rotSteps = (rotSteps % 4 + 4) % 4;
		switch (rotSteps)
		{
			case 1:
				return new TerrainCell(cell.Y_SW, cell.Y_NW, cell.Y_NE, cell.Y_SE, cell.WaterMode, cell.WaterProfileIndex, cell.WaterHeight);
			case 2:
				return new TerrainCell(cell.Y_SE, cell.Y_SW, cell.Y_NW, cell.Y_NE, cell.WaterMode, cell.WaterProfileIndex, cell.WaterHeight);
			case 3:
				return new TerrainCell(cell.Y_NE, cell.Y_SE, cell.Y_SW, cell.Y_NW, cell.WaterMode, cell.WaterProfileIndex, cell.WaterHeight);
			case 0:
			default:
				return cell;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static TerrainCell MirrorCell(in TerrainCell cell, MirrorMode mode)
	{
		switch (mode)
		{
			case MirrorMode.Horizontal:
				return new TerrainCell(cell.Y_NE, cell.Y_NW, cell.Y_SW, cell.Y_SE, cell.WaterMode, cell.WaterProfileIndex, cell.WaterHeight);
			case MirrorMode.Vertical:
				return new TerrainCell(cell.Y_SW, cell.Y_SE, cell.Y_NE, cell.Y_NW, cell.WaterMode, cell.WaterProfileIndex, cell.WaterHeight);
			case MirrorMode.Both:
				return new TerrainCell(cell.Y_SE, cell.Y_SW, cell.Y_NW, cell.Y_NE, cell.WaterMode, cell.WaterProfileIndex, cell.WaterHeight);
			case MirrorMode.None:
			default:
				return cell;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static (TerrainSplatWeights NW, TerrainSplatWeights NE, TerrainSplatWeights SE, TerrainSplatWeights SW) MirrorSplatQuad(
		in TerrainSplatWeights nw, in TerrainSplatWeights ne, in TerrainSplatWeights se, in TerrainSplatWeights sw, MirrorMode mode)
	{
		switch (mode)
		{
			case MirrorMode.Horizontal:
				return (ne, nw, sw, se);
			case MirrorMode.Vertical:
				return (sw, se, ne, nw);
			case MirrorMode.Both:
				return (se, sw, nw, ne);
			case MirrorMode.None:
			default:
				return (nw, ne, se, sw);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static (TerrainSplatWeights NW, TerrainSplatWeights NE, TerrainSplatWeights SE, TerrainSplatWeights SW) RotateSplatQuad(
		in TerrainSplatWeights nw, in TerrainSplatWeights ne, in TerrainSplatWeights se, in TerrainSplatWeights sw, int rotSteps)
	{
		rotSteps = (rotSteps % 4 + 4) % 4;
		switch (rotSteps)
		{
			case 1:
				return (sw, nw, ne, se);
			case 2:
				return (se, sw, nw, ne);
			case 3:
				return (ne, se, sw, nw);
			case 0:
			default:
				return (nw, ne, se, sw);
		}
	}

	private void TrySetSplat(TerrainSplatWeights[,] map, int x, int z, int w, int d, in TerrainSplatWeights val)
	{
		if (x >= 0 && x < w && z >= 0 && z < d) map[x, z] = val;
	}

	private void SetGridNodeSplat(int gx, int gz, in TerrainSplatWeights nw, in TerrainSplatWeights ne, in TerrainSplatWeights se, in TerrainSplatWeights sw)
	{
		if (_terrainSplatMap == null) return;
		int w = _terrainSplatMap.GetLength(0);
		int d = _terrainSplatMap.GetLength(1);
		TrySetSplat(_terrainSplatMap, gx, gz, w, d, in nw);
		TrySetSplat(_terrainSplatMap, gx + 1, gz, w, d, in ne);
		TrySetSplat(_terrainSplatMap, gx + 1, gz + 1, w, d, in se);
		TrySetSplat(_terrainSplatMap, gx, gz + 1, w, d, in sw);
	}

	private void SetGridNodeCliffSplat(int gx, int gz, in TerrainSplatWeights nw, in TerrainSplatWeights ne, in TerrainSplatWeights se, in TerrainSplatWeights sw)
	{
		if (_terrainCliffSplatMap == null) return;
		int w = _terrainCliffSplatMap.GetLength(0);
		int d = _terrainCliffSplatMap.GetLength(1);
		TrySetSplat(_terrainCliffSplatMap, gx, gz, w, d, in nw);
		TrySetSplat(_terrainCliffSplatMap, gx + 1, gz, w, d, in ne);
		TrySetSplat(_terrainCliffSplatMap, gx + 1, gz + 1, w, d, in se);
		TrySetSplat(_terrainCliffSplatMap, gx, gz + 1, w, d, in sw);
	}

	private void SanitizeCellCorners(TerrainCell[,] cells, int gx, int gz, int w, int d, float h)
	{
		bool xm1 = gx > 0 && gx - 1 < w;
		bool zm1 = gz > 0 && gz - 1 < d;
		bool x0 = gx < w;
		bool z0 = gz < d;

		if (xm1 && zm1) cells[gx - 1, gz - 1].Y_SE = h;
		if (x0 && zm1) cells[gx, gz - 1].Y_SW = h;
		if (xm1 && z0) cells[gx - 1, gz].Y_NE = h;
		if (x0 && z0) cells[gx, gz].Y_NW = h;
	}

	private void SanitizeCornerHeights(ref TerrainState terrain)
	{
		var cells = terrain.Cells;
		if (cells == null) return;

		int w = terrain.Width;
		int d = terrain.Depth;

		for (int gz = 0; gz <= d; gz++)
		{
			for (int gx = 0; gx <= w; gx++)
			{
				SanitizeCellCorners(cells, gx, gz, w, d, GetGridNodeHeight(in terrain, gx, gz));
			}
		}
	}

	private void ProcessPasteBlockCell(
		int sx, int sz, int pasteWidth, int pasteDepth, int targetWidth, int targetDepth,
		int blockStartX, int blockStartZ,
		int rotSteps, PasteReflection pasteReflection, MirrorMode symmetryMirror, int foldRotSteps,
		int width, int depth, bool pasteHeights, bool pasteTextures, bool pastePathing,
		TerrainCell[,]? srcCells, TerrainCell[,]? cells,
		ref TerrainState terrain, ref bool modified, ref bool pathingModified)
	{
		int srcX = pasteReflection == PasteReflection.Horizontal ? pasteWidth - 1 - sx : sx;
		int srcZ = pasteReflection == PasteReflection.Vertical ? pasteDepth - 1 - sz : sz;

		if (_copiedArea.Mask != null && !_copiedArea.Mask[srcX, srcZ]) return;

		int rotX = sx; int rotZ = sz;
		if (rotSteps == 1) { rotX = pasteDepth - 1 - sz; rotZ = sx; }
		else if (rotSteps == 2) { rotX = pasteWidth - 1 - sx; rotZ = pasteDepth - 1 - sz; }
		else if (rotSteps == 3) { rotX = sz; rotZ = pasteWidth - 1 - sx; }

		TerrainCell curCell = default;
		if (pasteHeights && srcCells != null && srcX < pasteWidth && srcZ < pasteDepth)
		{
			curCell = srcCells[srcX, srcZ];
			if (pasteReflection == PasteReflection.Horizontal) curCell = MirrorCell(in curCell, MirrorMode.Horizontal);
			else if (pasteReflection == PasteReflection.Vertical) curCell = MirrorCell(in curCell, MirrorMode.Vertical);
			curCell = RotateCell(in curCell, rotSteps);
		}

		TerrainSplatWeights sNW = default, sNE = default, sSE = default, sSW = default;
		TerrainSplatWeights cS_NW = default, cS_NE = default, cS_SE = default, cS_SW = default;
		if (pasteTextures)
		{
			if (_copiedArea.SplatMap != null && srcX + 1 < _copiedArea.SplatMap.GetLength(0) && srcZ + 1 < _copiedArea.SplatMap.GetLength(1))
			{
				sNW = _copiedArea.SplatMap[srcX, srcZ]; sNE = _copiedArea.SplatMap[srcX + 1, srcZ]; sSE = _copiedArea.SplatMap[srcX + 1, srcZ + 1]; sSW = _copiedArea.SplatMap[srcX, srcZ + 1];
				if (pasteReflection == PasteReflection.Horizontal) (sNW, sNE, sSE, sSW) = MirrorSplatQuad(in sNW, in sNE, in sSE, in sSW, MirrorMode.Horizontal);
				else if (pasteReflection == PasteReflection.Vertical) (sNW, sNE, sSE, sSW) = MirrorSplatQuad(in sNW, in sNE, in sSE, in sSW, MirrorMode.Vertical);
				(sNW, sNE, sSE, sSW) = RotateSplatQuad(in sNW, in sNE, in sSE, in sSW, rotSteps);
			}
			if (_copiedArea.CliffSplatMap != null && srcX + 1 < _copiedArea.CliffSplatMap.GetLength(0) && srcZ + 1 < _copiedArea.CliffSplatMap.GetLength(1))
			{
				cS_NW = _copiedArea.CliffSplatMap[srcX, srcZ]; cS_NE = _copiedArea.CliffSplatMap[srcX + 1, srcZ]; cS_SE = _copiedArea.CliffSplatMap[srcX + 1, srcZ + 1]; cS_SW = _copiedArea.CliffSplatMap[srcX, srcZ + 1];
				if (pasteReflection == PasteReflection.Horizontal) (cS_NW, cS_NE, cS_SE, cS_SW) = MirrorSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, MirrorMode.Horizontal);
				else if (pasteReflection == PasteReflection.Vertical) (cS_NW, cS_NE, cS_SE, cS_SW) = MirrorSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, MirrorMode.Vertical);
				(cS_NW, cS_NE, cS_SE, cS_SW) = RotateSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, rotSteps);
			}
			else if (_copiedArea.SplatMap != null) { cS_NW = sNW; cS_NE = sNE; cS_SE = sSE; cS_SW = sSW; }
		}

		int finalX = rotX; int finalZ = rotZ;
		if (symmetryMirror == MirrorMode.Horizontal)
		{
			finalX = targetWidth - 1 - rotX; curCell = MirrorCell(in curCell, MirrorMode.Horizontal);
			(sNW, sNE, sSE, sSW) = MirrorSplatQuad(in sNW, in sNE, in sSE, in sSW, MirrorMode.Horizontal);
			(cS_NW, cS_NE, cS_SE, cS_SW) = MirrorSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, MirrorMode.Horizontal);
		}
		else if (symmetryMirror == MirrorMode.Vertical)
		{
			finalZ = targetDepth - 1 - rotZ; curCell = MirrorCell(in curCell, MirrorMode.Vertical);
			(sNW, sNE, sSE, sSW) = MirrorSplatQuad(in sNW, in sNE, in sSE, in sSW, MirrorMode.Vertical);
			(cS_NW, cS_NE, cS_SE, cS_SW) = MirrorSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, MirrorMode.Vertical);
		}
		else if (symmetryMirror == MirrorMode.Both)
		{
			finalX = targetWidth - 1 - rotX; finalZ = targetDepth - 1 - rotZ; curCell = MirrorCell(in curCell, MirrorMode.Both);
			(sNW, sNE, sSE, sSW) = MirrorSplatQuad(in sNW, in sNE, in sSE, in sSW, MirrorMode.Both);
			(cS_NW, cS_NE, cS_SE, cS_SW) = MirrorSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, MirrorMode.Both);
		}
		else if (foldRotSteps != 0)
		{
			if (foldRotSteps == 1) { finalX = targetDepth - 1 - rotZ; finalZ = rotX; }
			else if (foldRotSteps == 2) { finalX = targetWidth - 1 - rotX; finalZ = targetDepth - 1 - rotZ; }
			else if (foldRotSteps == 3) { finalX = rotZ; finalZ = targetWidth - 1 - rotX; }
			curCell = RotateCell(in curCell, foldRotSteps);
			(sNW, sNE, sSE, sSW) = RotateSplatQuad(in sNW, in sNE, in sSE, in sSW, foldRotSteps);
			(cS_NW, cS_NE, cS_SE, cS_SW) = RotateSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, foldRotSteps);
		}

		int targetX = blockStartX + finalX;
		int targetZ = blockStartZ + finalZ;

		if (targetX >= 0 && targetX < width && targetZ >= 0 && targetZ < depth)
		{
			if (pasteHeights && srcCells != null && cells != null)
			{
				SetGridNodeHeight(ref terrain, targetX, targetZ, curCell.Y_NW);
				SetGridNodeHeight(ref terrain, targetX + 1, targetZ, curCell.Y_NE);
				SetGridNodeHeight(ref terrain, targetX + 1, targetZ + 1, curCell.Y_SE);
				SetGridNodeHeight(ref terrain, targetX, targetZ + 1, curCell.Y_SW);
				cells[targetX, targetZ].WaterMode = curCell.WaterMode;
				cells[targetX, targetZ].WaterProfileIndex = curCell.WaterProfileIndex;
				cells[targetX, targetZ].WaterHeight = curCell.WaterHeight;
			}
			if (pasteTextures)
			{
				SetGridNodeSplat(targetX, targetZ, in sNW, in sNE, in sSE, in sSW);
				SetGridNodeCliffSplat(targetX, targetZ, in cS_NW, in cS_NE, in cS_SE, in cS_SW);
			}
			if (pastePathing && _copiedArea.Pathing != null && terrain.PathingCodes != null && srcX < pasteWidth && srcZ < pasteDepth)
			{
				terrain.PathingCodes[targetX, targetZ] = _copiedArea.Pathing[srcX, srcZ];
				pathingModified = true;
			}
			modified = true;
		}
	}

	private void PasteBlock(
		int blockStartX, int blockStartZ,
		int rotSteps,
		PasteReflection pasteReflection,
		MirrorMode symmetryMirror,
		int foldRotSteps,
		int width, int depth,
		bool pasteHeights, bool pasteTextures, bool pastePathing,
		ref TerrainState terrain,
		ref bool modified,
		ref bool pathingModified)
	{
		int pasteWidth = _copiedArea.Width;
		int pasteDepth = _copiedArea.Depth;
		int targetWidth = (rotSteps == 1 || rotSteps == 3) ? pasteDepth : pasteWidth;
		int targetDepth = (rotSteps == 1 || rotSteps == 3) ? pasteWidth : pasteDepth;

		var cells = terrain.Cells;
		var srcCells = _copiedArea.Cells;

		for (int sz = 0; sz < pasteDepth; sz++)
		{
			for (int sx = 0; sx < pasteWidth; sx++)
			{
				ProcessPasteBlockCell(sx, sz, pasteWidth, pasteDepth, targetWidth, targetDepth, blockStartX, blockStartZ, rotSteps, pasteReflection, symmetryMirror, foldRotSteps, width, depth, pasteHeights, pasteTextures, pastePathing, srcCells, cells, ref terrain, ref modified, ref pathingModified);
			}
		}
	}

	public bool GetBlockMode(Entity worldEntity)
	{
		return EcsWorld.GetFieldOrDefault<EditorState, bool>(worldEntity, s => s.BlockMode, true);
	}

	public void SetBlockMode(Entity worldEntity, bool value)
	{
		EcsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.BlockMode = value);
	}

	public float GetBlockLevelHeight(Entity worldEntity)
	{
		return EcsWorld.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.BlockLevelHeight, 3.0f);
	}

	public void SetBlockLevelHeight(Entity worldEntity, float value)
	{
		float clamped = Math.Clamp(value, 0.0f, 50.0f);
		EcsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.BlockLevelHeight = clamped);
	}

	public WaterType GetWaterMode(Entity worldEntity)
	{
		return EcsWorld.GetFieldOrDefault<EditorState, WaterType>(worldEntity, s => s.WaterMode, WaterType.None);
	}

	public void SetWaterMode(Entity worldEntity, WaterType value)
	{
		if (EcsWorld != null && EcsWorld.IsAlive(worldEntity))
		{
			if (EcsWorld.Has<EditorState>(worldEntity))
			{
				ref var state = ref EcsWorld.Get<EditorState>(worldEntity);
				state.WaterMode = value;
			}
			else
			{
				EcsWorld.Add(worldEntity, new EditorState(true, 3.0f, -95.0f, 95.0f, -95.0f, 125.0f, "", false, MirrorMode.None, value, 0));
			}
		}
	}

	public byte GetWaterProfileIndex(Entity worldEntity)
	{
		return EcsWorld.GetFieldOrDefault<EditorState, byte>(worldEntity, s => s.WaterProfileIndex, (byte)0);
	}

	public void SetWaterProfileIndex(Entity worldEntity, byte value)
	{
		if (EcsWorld != null && EcsWorld.IsAlive(worldEntity))
		{
			if (EcsWorld.Has<EditorState>(worldEntity))
			{
				ref var state = ref EcsWorld.Get<EditorState>(worldEntity);
				state.WaterProfileIndex = value;
			}
			else
			{
				EcsWorld.Add(worldEntity, new EditorState(true, 3.0f, -95.0f, 95.0f, -95.0f, 125.0f, "", false, MirrorMode.None, WaterType.None, value));
			}
		}
	}

	private readonly Dictionary<Vector2I, List<Node>> _waterProceduralObjects = new();
	private readonly Dictionary<Vector2I, List<Node>> _terrainProceduralObjects = new();

	private void ClearWaterProceduralObjects(int x, int z)
	{
		var key = new Vector2I(x, z);
		if (_waterProceduralObjects.TryGetValue(key, out var list))
		{
			foreach (var node in list)
			{
				if (GodotObject.IsInstanceValid(node))
				{
					if (node is Realm.Client.Decal3D d)
					{
						Realm.Client.Core.GameHost.Instance?.AllDecals.Remove(d);
						if (EcsWorld != null && EcsWorld.IsAlive(d.Entity)) EcsWorld.Destroy(d.Entity);
					}
					else if (node is Realm.Client.VFX.ProceduralVfxInstance3D vfx)
					{
						Realm.Client.Core.GameHost.Instance?.AllVfx.Remove(vfx);
						if (EcsWorld != null && EcsWorld.IsAlive(vfx.Entity)) EcsWorld.Destroy(vfx.Entity);
					}
					node.QueueFree();
				}
			}
			_waterProceduralObjects.Remove(key);
		}
	}

	private void ClearTerrainProceduralObjects(int x, int z)
	{
		var key = new Vector2I(x, z);
		if (!_terrainProceduralObjects.TryGetValue(key, out var list)) return;
		
		foreach (var node in list)
		{
			if (GodotObject.IsInstanceValid(node))
			{
				switch (node)
				{
					case Realm.Client.Decal3D d:
						Realm.Client.Core.GameHost.Instance?.AllDecals.Remove(d);
						if (EcsWorld != null && EcsWorld.IsAlive(d.Entity)) EcsWorld.Destroy(d.Entity);
						break;
					case Realm.Client.VFX.ProceduralVfxInstance3D vfx:
						Realm.Client.Core.GameHost.Instance?.AllVfx.Remove(vfx);
						if (EcsWorld != null && EcsWorld.IsAlive(vfx.Entity)) EcsWorld.Destroy(vfx.Entity);
						break;
				}
				node.QueueFree();
			}
		}
		_terrainProceduralObjects.Remove(key);
	}

	private void ProcessWaterDecalRules(Realm.Shared.Metadata.WaterProfileSaveData prof, float quadSize, float cellCenterX, float waterY, float cellCenterZ, List<Node> spawnedList)
	{
		if (prof.DecalBombingRules == null || prof.DecalBombingRules.Count == 0) return;
		foreach (var rule in prof.DecalBombingRules)
		{
			if (string.IsNullOrWhiteSpace(rule.DecalId)) continue;
			if (Random.Shared.NextSingle() > (rule.Density * 0.05f)) continue;
			float jx = (Random.Shared.NextSingle() - 0.5f) * quadSize * 0.8f;
			float jz = (Random.Shared.NextSingle() - 0.5f) * quadSize * 0.8f;
			float scale = Mathf.Lerp(rule.MinScale, rule.MaxScale, Random.Shared.NextSingle());
			float rotY = Random.Shared.NextSingle() * 360f;
			var pos = new Vector3(cellCenterX + jx, waterY, cellCenterZ + jz);
			var decal = Realm.Client.Core.GameHost.Instance?.SpawnDecalExternalWithParams(rule.DecalId, pos, new Vector3(0, rotY, 0), scale);
			if (decal != null) spawnedList.Add(decal);
		}
	}

	private void ProcessWaterVfxRules(Realm.Shared.Metadata.WaterProfileSaveData prof, float quadSize, float cellCenterX, float waterY, float cellCenterZ, List<Node> spawnedList)
	{
		if (prof.VfxBombingRules == null || prof.VfxBombingRules.Count == 0) return;
		foreach (var rule in prof.VfxBombingRules)
		{
			if (string.IsNullOrWhiteSpace(rule.VfxId)) continue;
			if (Random.Shared.NextSingle() > (rule.Density * 0.05f)) continue;
			float jx = (Random.Shared.NextSingle() - 0.5f) * quadSize * 0.8f;
			float jz = (Random.Shared.NextSingle() - 0.5f) * quadSize * 0.8f;
			float scale = Mathf.Lerp(rule.MinScale, rule.MaxScale, Random.Shared.NextSingle());
			float rotY = Random.Shared.NextSingle() * 360f;
			var pos = new Vector3(cellCenterX + jx, waterY, cellCenterZ + jz);
			var vfx = Realm.Client.Core.GameHost.Instance?.SpawnVfxExternalWithParams(rule.VfxId, pos, new Vector3(0, rotY, 0), new Vector3(scale, scale, scale));
			if (vfx != null) spawnedList.Add(vfx);
		}
	}

	private void SpawnWaterProceduralBombing(int x, int z, byte profileIndex, float waterY, float quadSize, int width, int depth)
	{
		ClearWaterProceduralObjects(x, z);
		if (Realm.Client.RuntimeTerrain.Instance == null) return;
		var prof = Realm.Client.RuntimeTerrain.Instance.GetWaterProfile(profileIndex);
		if (prof == null) return;

		float cellCenterX = (x + 0.5f - width / 2.0f) * quadSize;
		float cellCenterZ = (z + 0.5f - depth / 2.0f) * quadSize;
		var spawnedList = new List<Node>();

		ProcessWaterDecalRules(prof, quadSize, cellCenterX, waterY, cellCenterZ, spawnedList);
		ProcessWaterVfxRules(prof, quadSize, cellCenterX, waterY, cellCenterZ, spawnedList);

		if (spawnedList.Count > 0)
		{
			_waterProceduralObjects[new Vector2I(x, z)] = spawnedList;
		}
	}

	private void SpawnTerrainProceduralBombing(int x, int z, int textureIndex, float terrainY, float quadSize, int width, int depth)
	{
		ClearTerrainProceduralObjects(x, z);
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		if (string.IsNullOrEmpty(wsPath)) return;
		if (!MetadataService.Instance.TryLoadMetadata(wsPath, out var metaRoot) || metaRoot == null) return;

		string? swatchName = null;
		if (Realm.Client.Core.GameHost.Instance?.GroundTerrain != null && textureIndex >= 0 && textureIndex < Realm.Client.Core.GameHost.Instance.GroundTerrain.LoadedTextureList.Count)
		{
			swatchName = Realm.Client.Core.GameHost.Instance.GroundTerrain.LoadedTextureList[textureIndex];
		}
		if (string.IsNullOrEmpty(swatchName)) return;

		var prof = metaRoot.GetTerrainProfile(swatchName);
		if (prof == null) return;

		float cellCenterX = (x + 0.5f - width / 2.0f) * quadSize;
		float cellCenterZ = (z + 0.5f - depth / 2.0f) * quadSize;
		var spawnedList = new List<Node>();

		if (prof.DecalBombingRules != null && prof.DecalBombingRules.Count > 0)
		{
			foreach (var rule in prof.DecalBombingRules)
			{
				if (string.IsNullOrWhiteSpace(rule.DecalId)) continue;
				if (Random.Shared.NextSingle() <= (rule.Density * 0.05f))
				{
					float jx = (Random.Shared.NextSingle() - 0.5f) * quadSize * 0.8f;
					float jz = (Random.Shared.NextSingle() - 0.5f) * quadSize * 0.8f;
					float scale = Mathf.Lerp(rule.MinScale, rule.MaxScale, Random.Shared.NextSingle());
					float rotY = Random.Shared.NextSingle() * 360f;
					var pos = new Vector3(cellCenterX + jx, terrainY, cellCenterZ + jz);
					var decal = Realm.Client.Core.GameHost.Instance?.SpawnDecalExternalWithParams(rule.DecalId, pos, new Vector3(0, rotY, 0), scale);
					if (decal != null) spawnedList.Add(decal);
				}
			}
		}

		if (prof.VfxBombingRules != null && prof.VfxBombingRules.Count > 0)
		{
			foreach (var rule in prof.VfxBombingRules)
			{
				if (string.IsNullOrWhiteSpace(rule.VfxId)) continue;
				if (Random.Shared.NextSingle() <= (rule.Density * 0.05f))
				{
					float jx = (Random.Shared.NextSingle() - 0.5f) * quadSize * 0.8f;
					float jz = (Random.Shared.NextSingle() - 0.5f) * quadSize * 0.8f;
					float scale = Mathf.Lerp(rule.MinScale, rule.MaxScale, Random.Shared.NextSingle());
					float rotY = Random.Shared.NextSingle() * 360f;
					var pos = new Vector3(cellCenterX + jx, terrainY, cellCenterZ + jz);
					var vfx = Realm.Client.Core.GameHost.Instance?.SpawnVfxExternalWithParams(rule.VfxId, pos, new Vector3(0, rotY, 0), new Vector3(scale, scale, scale));
					if (vfx != null) spawnedList.Add(vfx);
				}
			}
		}

		if (spawnedList.Count > 0)
		{
			_terrainProceduralObjects[new Vector2I(x, z)] = spawnedList;
		}
	}

	public float GetCameraBoundsLeft(Entity worldEntity)
	{
		return EcsWorld.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.CameraBoundsLeft, -95.0f);
	}

	public void SetCameraBoundsLeft(Entity worldEntity, float value)
	{
		EcsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.CameraBoundsLeft = value);
	}

	public float GetCameraBoundsRight(Entity worldEntity)
	{
		return EcsWorld.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.CameraBoundsRight, 95.0f);
	}

	public void SetCameraBoundsRight(Entity worldEntity, float value)
	{
		EcsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.CameraBoundsRight = value);
	}

	public float GetCameraBoundsTop(Entity worldEntity)
	{
		return EcsWorld.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.CameraBoundsTop, -95.0f);
	}

	public void SetCameraBoundsTop(Entity worldEntity, float value)
	{
		EcsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.CameraBoundsTop = value);
	}

	public float GetCameraBoundsBottom(Entity worldEntity)
	{
		return EcsWorld.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.CameraBoundsBottom, 125.0f);
	}

	public void SetCameraBoundsBottom(Entity worldEntity, float value)
	{
		EcsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.CameraBoundsBottom = value);
	}

	public string GetSkyboxPath(Entity worldEntity)
	{
		return EcsWorld.GetFieldOrDefault<EditorState, string>(worldEntity, s => s.SkyboxPath, "");
	}

	public void SetSkyboxPath(Entity worldEntity, string value)
	{
		EcsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.SkyboxPath = value);
	}

	public bool GetHasUnsavedChanges(Entity worldEntity)
	{
		return EcsWorld.GetFieldOrDefault<EditorState, bool>(worldEntity, s => s.HasUnsavedChanges, false);
	}

	public void SetHasUnsavedChanges(Entity worldEntity, bool value)
	{
		EcsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.HasUnsavedChanges = value);
	}

	public string GetTerrainStatusString(Vector3 pos, string toolName, string activePlaceId)
	{
		string formattedToolName = toolName.ToUpper();
		if (!string.IsNullOrEmpty(activePlaceId))
		{
			string shortId = System.IO.Path.GetFileName(activePlaceId).ToUpper();
			formattedToolName += $" ({shortId})";
		}

		string status = $"ACTIVE TOOL: {formattedToolName} | Pos: {pos.X:F1}, {pos.Z:F1}, {pos.Y:F1}";

		ref var terrain = ref GetTerrainState();

		if (terrain.Cells != null && toolName.Equals("PaintPathing", StringComparison.OrdinalIgnoreCase))
		{
			float fx = pos.X / terrain.QuadSize + terrain.Width / 2.0f;
			float fz = pos.Z / terrain.QuadSize + terrain.Depth / 2.0f;
			int cx = Mathf.Clamp((int)Mathf.Round(fx), 0, terrain.Width - 1);
			int cz = Mathf.Clamp((int)Mathf.Round(fz), 0, terrain.Depth - 1);

			if (terrain.PathingCodes != null)
			{
				int code = terrain.PathingCodes[cx, cz];
				var layers = new List<string>();
				if ((code & Realm.Client.EditableTerrain.PATHING_GROUND) != 0)
				{
					layers.Add("Ground");
				}
				if ((code & Realm.Client.EditableTerrain.PATHING_FLYING) != 0)
				{
					layers.Add("Flying");
				}
				if ((code & Realm.Client.EditableTerrain.PATHING_SHALLOW_WATER) != 0)
				{
					layers.Add("Shallow Water");
				}
				if ((code & Realm.Client.EditableTerrain.PATHING_DEEP_WATER) != 0)
				{
					layers.Add("Deep Water");
				}
				if ((code & Realm.Client.EditableTerrain.PATHING_BUILDABLE) != 0)
				{
					layers.Add("Buildable");
				}

				string layersStr = layers.Count > 0 ? string.Join(", ", layers) : "None";
				status += $" | Path: {layersStr}";
			}
		}

		return status;
	}

	public void AlignSplatMapSlots(int minX, int minZ, int maxX, int maxZ)
	{
		if (_terrainSplatMap != null) AlignSplatMapMatrix(_terrainSplatMap, minX, minZ, maxX, maxZ);
		if (_terrainCliffSplatMap == null && Realm.Client.Core.GameHost.Instance?.GroundTerrain != null)
		{
			_terrainCliffSplatMap = Realm.Client.Core.GameHost.Instance.GroundTerrain.CliffSplatMap;
		}
		if (_terrainCliffSplatMap != null) AlignSplatMapMatrix(_terrainCliffSplatMap, minX, minZ, maxX, maxZ);
	}

	private void ProcessSplatNeighbors(TerrainSplatWeights[,] splatMatrix, int x, int z, int width, int depth)
	{
		for (int dz = -1; dz <= 1; dz++)
		{
			for (int dx = -1; dx <= 1; dx++)
			{
				if (dx == 0 && dz == 0) continue;
				int nx = x + dx;
				int nz = z + dz;
				if (nx >= 0 && nx < width && nz >= 0 && nz < depth)
				{
					var neighbor = splatMatrix[nx, nz];
					if (neighbor.Weight0 > 0.001f) TryAddIndexToUnusedSlot(splatMatrix, x, z, neighbor.Index0);
					if (neighbor.Weight1 > 0.001f) TryAddIndexToUnusedSlot(splatMatrix, x, z, neighbor.Index1);
					if (neighbor.Weight2 > 0.001f) TryAddIndexToUnusedSlot(splatMatrix, x, z, neighbor.Index2);
					if (neighbor.Weight3 > 0.001f) TryAddIndexToUnusedSlot(splatMatrix, x, z, neighbor.Index3);
				}
			}
		}
	}

	private void AlignSplatMapMatrix(TerrainSplatWeights[,] splatMatrix, int minX, int minZ, int maxX, int maxZ)
	{
		int width = splatMatrix.GetLength(0);
		int depth = splatMatrix.GetLength(1);

		minX = Math.Max(0, minX);
		minZ = Math.Max(0, minZ);
		maxX = Math.Min(width - 1, maxX);
		maxZ = Math.Min(depth - 1, maxZ);

		for (int z = minZ; z <= maxZ; z++)
		{
			for (int x = minX; x <= maxX; x++)
			{
				ProcessSplatNeighbors(splatMatrix, x, z, width, depth);
			}
		}
	}

	private void TryAddIndexToUnusedSlot(TerrainSplatWeights[,] splatMatrix, int x, int z, int index)
	{
		var current = splatMatrix[x, z];

		if (current.Index0 == index || current.Index1 == index || current.Index2 == index || current.Index3 == index)
		{
			return;
		}

		int preferredSlot = index % 4;
		bool slotAvailable = preferredSlot switch
		{
			0 => current.Weight0 <= 0.001f,
			1 => current.Weight1 <= 0.001f,
			2 => current.Weight2 <= 0.001f,
			3 => current.Weight3 <= 0.001f,
			_ => false
		};

		if (slotAvailable)
		{
			switch (preferredSlot)
			{
				case 0: current.Index0 = index; current.Weight0 = 0.0f; break;
				case 1: current.Index1 = index; current.Weight1 = 0.0f; break;
				case 2: current.Index2 = index; current.Weight2 = 0.0f; break;
				case 3: current.Index3 = index; current.Weight3 = 0.0f; break;
			}
			splatMatrix[x, z] = current;
			return;
		}

		if (current.Weight0 <= 0.001f) { current.Index0 = index; current.Weight0 = 0.0f; splatMatrix[x, z] = current; return; }
		if (current.Weight1 <= 0.001f) { current.Index1 = index; current.Weight1 = 0.0f; splatMatrix[x, z] = current; return; }
		if (current.Weight2 <= 0.001f) { current.Index2 = index; current.Weight2 = 0.0f; splatMatrix[x, z] = current; return; }
		if (current.Weight3 <= 0.001f) { current.Index3 = index; current.Weight3 = 0.0f; splatMatrix[x, z] = current; return; }
	}

	private FileSystemWatcher? _workspaceWatcher;
	private string? _watchedDirectory;
	private System.Threading.Timer? _debounceTimer;
	private readonly object _watcherLock = new();
	private long _lastProcessedMetadataWriteTime;
	private long _lastProcessedTerrainWriteTime;
	public static DateTime LastInternalSaveTimeUtc { get; set; } = DateTime.MinValue;
	public bool PasteOptionEntities { get; set; } = true;
	public bool PasteOptionPathing { get; set; } = true;
	public float EditorPasteRotation { get; set; } = 0.0f;
	public PasteReflection EditorPasteReflection { get; set; } = PasteReflection.None;

	private bool _isPaused;
	public bool IsPaused
	{
		get => _isPaused;
		set
		{
			_isPaused = value;
			if (value)
			{
				lock (_watcherLock)
				{
					_debounceTimer?.Dispose();
					_debounceTimer = null;
				}
			}
		}
	}

	public void UpdateWatchedFileTimestamps()
	{
		lock (_watcherLock)
		{
			if (!string.IsNullOrEmpty(_watchedDirectory) && Directory.Exists(_watchedDirectory))
			{
				string metaPath = Path.Combine(_watchedDirectory, "metadata.json");
				string terrainPath = Path.Combine(_watchedDirectory, "terrain.json");
				if (File.Exists(metaPath)) _lastProcessedMetadataWriteTime = File.GetLastWriteTimeUtc(metaPath).Ticks;
				if (File.Exists(terrainPath)) _lastProcessedTerrainWriteTime = File.GetLastWriteTimeUtc(terrainPath).Ticks;
			}
		}
	}

	public void StartWorkspaceWatcher(string directory, Action? onMetadataChanged = null, Action? onTerrainChanged = null)
	{
		lock (_watcherLock)
		{
			StopWorkspaceWatcher();

			if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
			{
				return;
			}

			_watchedDirectory = directory;
			try
			{
				string metaPath = Path.Combine(directory, "metadata.json");
				string terrainPath = Path.Combine(directory, "terrain.json");
				if (File.Exists(metaPath)) _lastProcessedMetadataWriteTime = File.GetLastWriteTimeUtc(metaPath).Ticks;
				if (File.Exists(terrainPath)) _lastProcessedTerrainWriteTime = File.GetLastWriteTimeUtc(terrainPath).Ticks;

				_workspaceWatcher = new FileSystemWatcher(directory)
				{
					NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
					IncludeSubdirectories = false,
					EnableRaisingEvents = true
				};

				_workspaceWatcher.Changed += (s, e) => OnWorkspaceFileChanged(e.FullPath, onMetadataChanged, onTerrainChanged);
				_workspaceWatcher.Created += (s, e) => OnWorkspaceFileChanged(e.FullPath, onMetadataChanged, onTerrainChanged);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[EditorService] Failed to start FileSystemWatcher on {directory}: {ex.Message}");
			}
		}
	}

	public void StopWorkspaceWatcher()
	{
		lock (_watcherLock)
		{
			if (_workspaceWatcher != null)
			{
				_workspaceWatcher.EnableRaisingEvents = false;
				_workspaceWatcher.Dispose();
				_workspaceWatcher = null;
			}
			_debounceTimer?.Dispose();
			_debounceTimer = null;
		}
	}

	private void OnWorkspaceFileChanged(string fullPath, Action? onMetadataChanged, Action? onTerrainChanged)
	{
		if (IsPaused || (UI.MapEditorHUD.Instance != null && UI.MapEditorHUD.Instance.IsSyncing))
		{
			return;
		}

		string fileName = Path.GetFileName(fullPath).ToLowerInvariant();
		if (fileName != "metadata.json" && fileName != "manifest.json" && fileName != "terrain.json")
		{
			return;
		}

		lock (_watcherLock)
		{
			_debounceTimer?.Dispose();
			_debounceTimer = new System.Threading.Timer(_ =>
			{
				ProcessDebouncedFileChange(fullPath, fileName, onMetadataChanged, onTerrainChanged);
			}, null, 150, Timeout.Infinite);
		}
	}

	private void ProcessDebouncedFileChange(string fullPath, string fileName, Action? onMetadataChanged, Action? onTerrainChanged)
	{
		try
		{
			if (IsPaused || (UI.MapEditorHUD.Instance?.IsSyncing ?? false) || !File.Exists(fullPath)) return;
			if ((DateTime.UtcNow - LastInternalSaveTimeUtc).TotalMilliseconds < 1500) return;

			long writeTime = File.GetLastWriteTimeUtc(fullPath).Ticks;
			bool isMetadata = fileName == "metadata.json" || fileName == "manifest.json";

			if (isMetadata && writeTime > _lastProcessedMetadataWriteTime)
			{
				_lastProcessedMetadataWriteTime = writeTime;
				Callable.From(() => HandleExternalMetadataChange(fullPath, onMetadataChanged)).CallDeferred();
			}
			else if (fileName == "terrain.json" && writeTime > _lastProcessedTerrainWriteTime)
			{
				_lastProcessedTerrainWriteTime = writeTime;
				Callable.From(() => HandleExternalTerrainChange(fullPath, onTerrainChanged)).CallDeferred();
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[EditorService] ProcessDebouncedFileChange error: {ex.Message}");
		}
	}

	private void HandleExternalMetadataChange(string fullPath, Action? customCallback)
	{
		if (Realm.Client.UI.MapEditor.FloatingDialogBase.HasAnyDialogOpen)
		{
			return;
		}

		ExecuteMetadataReload(fullPath, customCallback);
	}

	private void ExecuteMetadataReload(string fullPath, Action? customCallback)
	{
		try
		{
			string name = Path.GetFileName(fullPath);
			UI.MapEditorHUD.Instance?.ReadMetadataAndRefreshTextures();
			UI.MapEditorHUD.Instance?.ShowFeedback(string.Format(TranslationServer.Translate("{0} updated externally — reloaded."), name));
			customCallback?.Invoke();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[EditorService] ExecuteMetadataReload error: {ex.Message}");
		}
	}

	private void HandleExternalTerrainChange(string fullPath, Action? customCallback)
	{
		if (Realm.Client.UI.MapEditor.FloatingDialogBase.HasAnyDialogOpen)
		{
			return;
		}

		try
		{
			Realm.Client.Core.GameHost.Instance?.LoadMapFromFile(fullPath);
			UI.MapEditorHUD.Instance?.ShowFeedback(TranslationServer.Translate("terrain.json updated externally — reloaded."));
			customCallback?.Invoke();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[EditorService] HandleExternalTerrainChange error: {ex.Message}");
		}
	}

	public bool CommitCoordinate(string name, int minCellX, int minCellZ, int maxCellX, int maxCellZ)
	{
		return Realm.Client.Core.GameHost.Instance?.CommitCoordinateExternal(name, minCellX, minCellZ, maxCellX, maxCellZ) ?? false;
	}

	public bool DeleteCoordinate(string name)
	{
		if (Realm.Client.Core.GameHost.Instance == null) return false;
		Realm.Client.Core.GameHost.Instance.DeleteCoordinateExternal(name);
		return true;
	}

	public void SelectCoordinate(string name)
	{
		Realm.Client.Core.GameHost.Instance?.SelectCoordinateExternal(name);
	}
}