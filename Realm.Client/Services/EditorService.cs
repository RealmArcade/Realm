using Realm.Shared.Maps;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Realm.Ecs;
using Godot;
using NSec.Cryptography;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services.MapHistory;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Client.VFX;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.EditorAPI;
using Realm.Shared.Distribution;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Environment = System.Environment;
using HttpClient = System.Net.Http.HttpClient;
using Key = NSec.Cryptography.Key;
using Timer = System.Threading.Timer;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Services;

public partial class EditorService : IEditorAPI
{
	/// <summary>
	/// The world the editor's terrain and entity edits are recorded against. Editor paths only
	/// run once a world exists, so reaching one without a world is a programming error;
	/// stating that once keeps each edit path from re-deriving it.
	/// </summary>
	private static EcsWorld World => GameContext.World
		?? throw new InvalidOperationException("EditorService used before a world existed.");

	public (bool Success, string ErrorMessage, IReadOnlyList<string> ExtractedAnimationFiles, string FileName) ImportGlbAssetFromExtensionDomain(string sourceFilePath, string category, string wsPath, Action<string, string, string, string?> updateMetadataAction)
	{
		return EditorAssetImport.ImportGlbAssetFromExtensionDomain(sourceFilePath, category, wsPath, updateMetadataAction);
	}

	public string ResolveAnimationFileName(string sourceFilePath, string? preferredFileName)
	{
		return EditorAssetImport.ResolveAnimationFileName(sourceFilePath, preferredFileName);
	}

	public (int ImportedCount, int SkippedCount, List<(string FileName, string Blake3)>? NewAnims) Import3DAnimationFileDomain(string sourceFilePath, string originalFileName, string animsDir)
	{
		return EditorAssetImport.Import3DAnimationFileDomain(sourceFilePath, originalFileName, animsDir);
	}

	public (bool Success, string FinalFileName, string NewHash) ImportRanimFileDomain(string sourceFilePath, string? preferredFileName, string animsDir)
	{
		return EditorAssetImport.ImportRanimFileDomain(sourceFilePath, preferredFileName, animsDir);
	}

	public (bool Success, string Message, string FileName, string Blake3) ImportDecalAssetDomain(string sourceFilePath, string wsPath)
	{
		return EditorAssetImport.ImportDecalAssetDomain(sourceFilePath, wsPath);
	}

	public string PrepareAnimationsDirectory(string wsPath)
	{
		return EditorAssetImport.PrepareAnimationsDirectory(wsPath);
	}

	public byte[]? GetRtexWebpBytes(string localRtex)
	{
		return EditorAssetImport.GetRtexWebpBytes(localRtex);
	}

	public async Task<string?> ProcessCreatorRegistrationAsync(string username, string pubKeyStr, Key key)
	{
		return await EditorAssetImport.ProcessCreatorRegistrationAsync(username, pubKeyStr, key);
	}

	public async Task<string?> RegisterCreatorAsync(string username, string pubKeyStr, Key key)
	{
		return await EditorAssetImport.RegisterCreatorAsync(username, pubKeyStr, key);
	}

	public bool IsTargetUnit(string unitId, string? targetId)
	{
		if (string.IsNullOrEmpty(targetId)) return true;
		if (string.Equals(unitId, targetId, StringComparison.OrdinalIgnoreCase)) return true;

		string unitFileName = Path.GetFileNameWithoutExtension(unitId);
		string targetFileName = Path.GetFileNameWithoutExtension(targetId);

		return string.Equals(unitFileName, targetId, StringComparison.OrdinalIgnoreCase) ||
			   string.Equals(unitId, targetFileName, StringComparison.OrdinalIgnoreCase);
	}

	public bool IsTargetProp(string propId, string? targetId)
	{
		return string.IsNullOrEmpty(targetId)
			   || string.Equals(propId, targetId, StringComparison.OrdinalIgnoreCase)
			   || string.Equals(Path.GetFileNameWithoutExtension(propId), targetId, StringComparison.OrdinalIgnoreCase)
			   || string.Equals(propId, Path.GetFileNameWithoutExtension(targetId), StringComparison.OrdinalIgnoreCase);
	}

	public void ScalePropEntitiesOnly(float scaleX, float scaleZ, Func<Entity, bool> hasProp3D)
	{
		if (GameContext.World != null)
		{
			var allPropEntitiesQuery = QueryCache.AllPropIdentityAndPositionQuery;
			World.Query(in allPropEntitiesQuery, (Entity entity, ref Position posComp) =>
			{
				if (!hasProp3D(entity))
				{
					World.Set(entity, new Position(new Vector3(posComp.Value.X * scaleX, posComp.Value.Y, posComp.Value.Z * scaleZ)));
				}
			});
		}
	}

	public void ResetTerrainSplatMap(TerrainSplatWeights[,] splatMap, int width, int depth)
	{
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				splatMap[x, z] = TerrainSplatWeights.CreateSolid(0);
			}
		}
	}

	public void ScaleCoordinateBounds(float scaleX, float scaleZ)
	{
		for (int i = 0; i < this.EditorCoordinates.Count; i++)
		{
			var coord = this.EditorCoordinates[i];
			coord.MinX *= scaleX;
			coord.MaxX *= scaleX;
			coord.MinZ *= scaleZ;
			coord.MaxZ *= scaleZ;
		}
	}

	public void UpdateScaleMetadata(int newWidth, int newDepth, MapCameraBounds cameraBounds)
	{
		EditorMetadataWrites.UpdateScaleMetadata(newWidth, newDepth, cameraBounds);
	}

	public void SetCameraBounds(Entity worldEntity, MapCameraBounds cameraBounds)
	{
		EditorEntityState.SetCameraBounds(worldEntity, cameraBounds);
	}

	public Entity EnsureDecalEntity(Entity currentEntity, string decalId, Vector3 position, float rotationY, float scaleX)
	{
		if (!World.IsAlive(currentEntity))
		{
			var newEnt = World.Create();
			World.Add(newEnt, new DecalIdentity(decalId));
			World.Add(newEnt, new Position(position));
			World.Add(newEnt, new RotationY(rotationY));
			World.Add(newEnt, new ModelScale(scaleX));
			return newEnt;
		}

		return currentEntity;
	}

	public void UpdateEntityEcsPosition(Entity entity, Vector3 newPos)
	{
		if (GameContext.World == null) return;
		if (World.IsAlive(entity) && World.Has<Position>(entity))
		{
			World.Set(entity, new Position(newPos));
		}
	}

	public void ClearEditorCoordinates()
	{
		EditorCoordinates.Clear();
	}

	public void ResetPivotToMapCenterDomain()
	{
		if (GameContext.Host is not { } host) return;

		var pivot = Vector2.Zero;
		host.EditorSymmetryPivot = pivot;
		host.GroundTerrain?.SetPolarCenter(pivot);
		host.UpdateSymmetryPivotVisuals();
		host.InvalidateSelectionHighlightMesh();
	}

	public (int cx, int cz) SetPivotDomain(Vector3 hitPos)
	{
		var pivot = new Vector2(hitPos.X, hitPos.Z);
		if (GameContext.Host is not { } host) return WorldPosToCellCoords(new Godot.Vector3(hitPos.X, hitPos.Y, hitPos.Z));

		host.EditorSymmetryPivot = pivot;
		host.GroundTerrain?.SetPolarCenter(pivot);
		host.UpdateSymmetryPivotVisuals();
		return WorldPosToCellCoords(new Godot.Vector3(hitPos.X, hitPos.Y, hitPos.Z));
	}

	public void SetPolarSpokesDomain(int spokes)
	{
		if (GameContext.Host == null) return;
		EditorPolarSpokeFolds = spokes;
		EditorPolarRadialStep = 360.0f / spokes;
		GameContext.Host.GroundTerrain?.SetPolarRadialStep(EditorPolarRadialStep);
	}

	public void ResetToBlankMapDomain()
	{
		if (IsSyncing) return;
		IsSyncing = true;
		IsPaused = true;
		try
		{
			GameContext.Host?.ClearMapEntirely();
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapEditorHUD] ResetToBlankMap error: {ex.Message}");
		}
		finally
		{
			UpdateWatchedFileTimestamps();
			IsPaused = false;
			IsSyncing = false;
		}
	}

	public void EnsureDefaultTemplateAnimationsSafeDomain()
	{
		try
		{
			RealmDefaultAnimations.EnsureDefaultTemplateAnimations(_editingDirectory);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[EditorService] Pre-warming default animations error: {ex.Message}");
		}
	}

	public bool HasCoordinate(string coordinateName)
	{
		return EditorCoordinateQueries.HasCoordinate(EditorCoordinates, coordinateName);
	}

	public Vector3 GetCoordinateMin(string coordinateName)
	{
		return EditorCoordinateQueries.GetCoordinateMin(EditorCoordinates, coordinateName);
	}

	public Vector3 GetCoordinateMax(string coordinateName)
	{
		return EditorCoordinateQueries.GetCoordinateMax(EditorCoordinates, coordinateName);
	}

	public bool IsPositionInCoordinate(Vector3 position, string coordinateName)
	{
		return EditorCoordinateQueries.IsPositionInCoordinate(EditorCoordinates, position, coordinateName);
	}

	public Godot.Vector3 LastRaycastPos { get; set; } = new Godot.Vector3(float.MinValue, float.MinValue, float.MinValue);
	public static readonly float[] PolarRingSpacingOptions = new[] { 4.0f, 8.0f, 16.0f, 32.0f };
	public bool IsSyncing
	{
		get => _isSyncing;
		set
		{
			_isSyncing = value;
			EditorWorkspaceWatcher.IsSyncing = value;
		}
	}

	public string GetWorkspacePath()
	{
		return MapWorkspaceService.GetActiveWorkspacePath();
	}

	public void SaveCustomUnitAnimations(string unitId, Dictionary<string, List<UnitAnimationEntry>> animations)
	{
		long? metadataTicks = EditorMetadataWrites.SaveCustomUnitAnimations(GetWorkspacePath(), unitId, animations);
		if (metadataTicks is long ticks) LastMetadataSyncTime = ticks;
	}

	public void SaveCustomAbilityVfxToMetadata(string abilityId, string visualEffect, string castSound, string iconPath, float aoeRadius)
	{
		long? metadataTicks = EditorMetadataWrites.SaveCustomAbilityVfxToMetadata(GetWorkspacePath(), abilityId, visualEffect, castSound, iconPath, aoeRadius);
		if (metadataTicks is long ticks) LastMetadataSyncTime = ticks;
	}

	public void SaveEntityModelPathToMetadata(string entityId, string fieldName, string domain, string newModelPath)
	{
		long? metadataTicks = EditorMetadataWrites.SaveEntityModelPathToMetadata(GetWorkspacePath(), entityId, fieldName, domain, newModelPath);
		if (metadataTicks is long ticks) LastMetadataSyncTime = ticks;
	}

	private readonly EditorMapIdentity _mapIdentity = new();
	public long LastMapNameCacheTicks { get => _mapIdentity.CacheTicks; set => _mapIdentity.CacheTicks = value; }
	public string? CachedMapName { get => _mapIdentity.CachedName; set => _mapIdentity.CachedName = value; }
	public string? CachedMapVersion { get => _mapIdentity.CachedVersion; set => _mapIdentity.CachedVersion = value; }
	public string CurrentDirectoryBlake3 { get => _workspace.CurrentDirectoryBlake3; set => _workspace.CurrentDirectoryBlake3 = value; }
	public long LastTerrainSyncTime { get; set; } = 0;
	public long LastMetadataSyncTime { get; set; } = 0;
	public int ScaleDialogTargetWidth { get; set; }

	public bool DecreaseScaleDialogWidth()
	{
		if (ScaleDialogTargetWidth > 32)
		{
			ScaleDialogTargetWidth = Math.Max(32, (ScaleDialogTargetWidth % 32 == 0 ? ScaleDialogTargetWidth - 32 : (ScaleDialogTargetWidth / 32) * 32));
			return true;
		}

		return false;
	}

	public bool IncreaseScaleDialogWidth()
	{
		if (ScaleDialogTargetWidth < 512)
		{
			ScaleDialogTargetWidth = Math.Min(512, (ScaleDialogTargetWidth / 32 + 1) * 32);
			return true;
		}

		return false;
	}

	public int DecreaseScaleDialogDepth(int currentDepth)
	{
		if (currentDepth > 32)
		{
			return Math.Max(32, (currentDepth % 32 == 0 ? currentDepth - 32 : (currentDepth / 32) * 32));
		}

		return currentDepth;
	}

	public int IncreaseScaleDialogDepth(int currentDepth)
	{
		if (currentDepth < 512)
		{
			return Math.Min(512, (currentDepth / 32 + 1) * 32);
		}

		return currentDepth;
	}

	public void SelectCliffTexture(int index)
	{
		EditorCliffPaintTextureIndex = index;
	}

	public bool IsPolarGridActive(GridOverlayMode mode, MirrorMode mirrorMode)
	{
		return mirrorMode == MirrorMode.Rotational || mode == GridOverlayMode.Polar || mode == GridOverlayMode.Both;
	}

	public double AutoBackupElapsedSeconds { get => _workspace.AutoBackupElapsedSeconds; set => _workspace.AutoBackupElapsedSeconds = value; }

	public bool CheckAndUpdateRaycast(Godot.Vector3 pos)
	{
		if ((pos - LastRaycastPos).LengthSquared() > 0.01f)
		{
			LastRaycastPos = pos;
			return true;
		}

		return false;
	}

	public bool CheckAutoBackup(int intervalMins, double delta)
	{
		return _workspace.TickBackupInterval(intervalMins, delta);
	}

	public bool TryGetValidCurrentSourceFolder(out string? folder)
	{
		folder = CurrentSourceFolder;
		return EditorMapFolderInspector.IsExistingFolder(folder);
	}

	public bool IsNestedInExistingMap(string fullSelectedPath)
	{
		return EditorMapFolderInspector.IsNestedInExistingMap(fullSelectedPath);
	}

	public bool ContainsNestedMap(string fullSelectedPath)
	{
		return EditorMapFolderInspector.ContainsNestedMap(fullSelectedPath);
	}

	public bool IsValidMapFolder(string folder)
	{
		return EditorMapFolderInspector.IsValidMapFolder(folder);
	}

	public bool IsMapFolderCorrupt(string folder, out string corruptFileName, out string parseErrorMessage)
	{
		return EditorMapFolderInspector.IsMapFolderCorrupt(folder, out corruptFileName, out parseErrorMessage);
	}

	public string _editingDirectory { get => _workspace.Directory; set => _workspace.Directory = value; }
	private readonly EditorWorkspace _workspace = new();

	public bool HasEditingDirectory => _workspace.HasDirectory;

	public void SetActiveMapDirectory(string absoluteDirectory)
	{
		_workspace.Directory = absoluteDirectory;
	}

	public void ClearEditingDirectoryDomain()
	{
		_workspace.Clear();
	}

	public void EnsureEditingDirectoryDomain()
	{
		if (_workspace.EnsureDirectory(CurrentSourceFolder)) SetCurrentSourceFolder(CurrentSourceFolder);
	}

	public int LastSelectionMinX { get; set; } = -1;
	public int LastSelectionMinZ { get; set; } = -1;
	public int LastSelectionMaxX { get; set; } = -1;
	public int LastSelectionMaxZ { get; set; } = -1;
	public bool LastSelectionBrushIsSquare { get; set; } = true;

	public int CurrentPasteAnchorIndex { get; set; } = 0;

	private readonly EditorSwatchCatalog _swatches = new();

	public List<string> SwatchPaths
	{
		get => _swatches.Paths;
		set => _swatches.Paths = value;
	}

	public List<string> SwatchDisplayNames
	{
		get => _swatches.DisplayNames;
		set => _swatches.DisplayNames = value;
	}

	public List<Color> SwatchColors
	{
		get => _swatches.Colors;
		set => _swatches.Colors = value;
	}

	public void ClearSwatchData()
	{
		_swatches.Clear();
	}

	public string GetActiveWorkspacePathFromSettings()
	{
		return MapWorkspaceService.GetActiveWorkspacePath();
	}

	public void PopulateSwatchData(SwatchSlotInfo[] slots, string wsPath)
	{
		_swatches.Populate(slots, wsPath);
	}

	public string GetSwatchName(Color color)
	{
		return _swatches.GetName(color);
	}

	public Color GetSwatchColor(int index)
	{
		return _swatches.GetColor(index);
	}

	public static readonly string[] PasteAnchorNames = new string[]
	{
		"CENTER",
		"TOP-LEFT",
		"TOP-RIGHT",
		"BOTTOM-RIGHT",
		"BOTTOM-LEFT"
	};

	public Rect2I? TerrainFlushRegion { get; set; }
	public float TerrainMeshRebuildPeriodMs { get; set; } = 33.3f;
	public Dictionary<string, DecalAssetData> DecalAssetCache { get; set; } = new();
	public Dictionary<(int, int), ImageTexture> DecalOrmCache { get; set; } = new();
	public Dictionary<string, ImageTexture> DecalNormalCache { get; set; } = new();

	public void ClearDecalCaches()
	{
		DecalOrmCache.Clear();
		DecalNormalCache.Clear();
		DecalAssetCache.Clear();
	}
	public List<EditorCoordinate> EditorCoordinates { get; set; } = new();
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
	public GridOverlayMode EditorGridMode { get; set; } = GridOverlayMode.Off;
	public bool EditorCameraBoundsVisible { get; set; } = false;
	public bool EditorDisableShadows { get; set; } = false;
	public int EditorPolarSpokeFolds { get; set; } = 4;
	public bool EditorPolarOverlayVisible { get; set; } = false;
	public float EditorPolarRingSpacing { get; set; } = 8.0f;
	public float EditorPolarRadialStep { get; set; } = 90.0f;
	public Godot.Vector3? EditorTapeMeasureStart { get; set; }
	public bool IsWaterRemoveAction { get; set; } = false;

	public int PlaceUnitPlayerIndex { get; set; } = 0;
	public bool PlaceUnitIsEnemy => PlaceUnitPlayerIndex != 0;
	public float EditorBrushRadius { get; set; } = 2.0f;
	public float EditorBrushStrength { get; set; } = 3.0f;
	public int EditorPaintTextureIndex { get; set; } = 3;
	public int EditorCliffPaintTextureIndex { get; set; } = 1;

	public (string terrainName, string cliffName) GetTextureNamesDomain()
	{
		int terrainIdx = EditorPaintTextureIndex;
		int cliffIdx = EditorCliffPaintTextureIndex;

		string terrainName = (terrainIdx >= 0 && terrainIdx < SwatchDisplayNames.Count) ? SwatchDisplayNames[terrainIdx] : "Unknown";
		string cliffName = (cliffIdx >= 0 && cliffIdx < SwatchDisplayNames.Count) ? SwatchDisplayNames[cliffIdx] : "Unknown";

		return (terrainName, cliffName);
	}

	public EditorTool ActiveEditorTool { get; set; } = EditorTool.None;
	public string? LastUsedFolder { get; set; } = null;
	public string? CurrentSourceFolder { get; set; } = null;
	public string? PendingCasSourceDirectory { get; set; } = null;
	public string? PendingDefaultSaveFolder { get; set; } = null;

	public void SetCurrentSourceFolder(string? folder)
	{
		CurrentSourceFolder = folder;
	}

	public const float MIN_BRUSH_RADIUS = 1.0f;
	public const float MAX_BRUSH_RADIUS = 20.0f;
	public const float MIN_BRUSH_STRENGTH = 0.0f;
	public const float MAX_BRUSH_STRENGTH = 10.0f;
	public const float MIN_PLACEMENT_SCALE = 0.25f;
	public const float MAX_PLACEMENT_SCALE = 5.0f;

	public bool _lastBrushShapeIsSquare { get; set; }
	public bool _hasLastBrushShape { get; set; }

	public bool TrySetBrushShapeExternal(bool isSquare)
	{
		if (_hasLastBrushShape && _lastBrushShapeIsSquare == isSquare) return false;
		_hasLastBrushShape = true;
		_lastBrushShapeIsSquare = isSquare;
		return true;
	}

	public float _lastRotationExternal { get; set; } = float.NaN;
	public float _lastPasteRotationExternal { get; set; } = float.NaN;
	public PasteReflection _lastPasteReflectionExternal { get; set; } = (PasteReflection)(-1);
	public float _lastScaleExternal { get; set; } = float.NaN;
	public float _lastBrushSizeExternal { get; set; } = float.NaN;

	public void UpdatePasteReflectionExternalDomain(PasteReflection reflection)
	{
		if (_lastPasteReflectionExternal == reflection) return;
		_lastPasteReflectionExternal = reflection;
	}

	public int GetPasteAnchorZ(int index, int depth)
	{
		return index switch
		{
			0 => depth / 2,
			1 => 0,
			2 => 0,
			3 => Math.Max(0, depth - 1),
			4 => Math.Max(0, depth - 1),
			_ => depth / 2
		};
	}

	public void CyclePasteAnchorDomain()
	{
		if (!HasCopiedArea) return;
		CurrentPasteAnchorIndex = (CurrentPasteAnchorIndex + 1) % PasteAnchorNames.Length;
		int w = CopiedAreaWidth;
		int d = CopiedAreaDepth;
		int ax = GetPasteAnchorX(CurrentPasteAnchorIndex, w);
		int az = GetPasteAnchorZ(CurrentPasteAnchorIndex, d);
		SetCopiedAreaAnchor(ax, az);
	}

	public bool SetLastRotationExternal(float angle)
	{
		if (Mathf.IsEqualApprox(_lastRotationExternal, angle)) return false;
		_lastRotationExternal = angle;
		return true;
	}

	public bool SetLastPasteRotationExternal(float angle)
	{
		if (Mathf.IsEqualApprox(_lastPasteRotationExternal, angle)) return false;
		_lastPasteRotationExternal = angle;
		return true;
	}

	public bool SetLastScaleExternal(float scale)
	{
		if (Mathf.IsEqualApprox(_lastScaleExternal, scale)) return false;
		_lastScaleExternal = scale;
		return true;
	}

	public bool SetLastBrushSizeExternal(float size)
	{
		if (Mathf.IsEqualApprox(_lastBrushSizeExternal, size)) return false;
		_lastBrushSizeExternal = size;
		return true;
	}

	public static readonly Dictionary<EditorTool, string> ToolInfoTexts = new()
	{
		{ EditorTool.Raise, "TOOL: Raise Heights\n\nDrag left click on the map ground to elevate terrain. Adjust size and strength in settings." },
		{ EditorTool.Ramp, "TOOL: Ramping\n\nLeft-click once on the terrain to set the Ramp Start Point. Left-click again to set the Ramp End Point. The tool will smoothly interpolate heights between the two points. Press Right-click or Escape to cancel." },
		{ EditorTool.Lower, "TOOL: Lower Heights\n\nDrag left click on the map ground to depress terrain. Adjust size and strength in settings." },
		{ EditorTool.Height, "TOOL: Exact Height\n\nDrag left click on the map ground to set terrain to exact block height. Adjust height in settings." },
		{ EditorTool.Plateau, "TOOL: Plateau\n\nDrag left click to flatten terrain to the elevation of your initial click point." },
		{ EditorTool.Smooth, "TOOL: Smooth Terrain\n\nDrag left click to average neighbor vertex heights and smooth out rugged elevations." },
		{ EditorTool.PaintTexture, "TOOL: Texture Painting\n\nDrag left click to paint texture layers onto the vertices of the terrain mesh." },
		{ EditorTool.FloodFill, "TOOL: Flood Fill\n\nClick once on the terrain map to flood-fill an area sharing the same texture color until hitting a boundary (cliff or different texture). Uses selected texture swatch." },
		{ EditorTool.SelectArea, "TOOL: Area Select\n\nDrag left click to select a rectangular area of the map. Press Ctrl+C to copy the area." },
		{ EditorTool.PasteArea, "TOOL: Area Paste\n\nClick on the terrain to paste the copied area. Use the Affected Layers checkboxes to filter what is pasted (Textures, HeightMap, Units / Props, Pathing)." },
		{ EditorTool.PlaceDecal, "TOOL: Place Decal\n\nLeft-click on the ground to project a decorative decal. Snapping, scaling, and rotation apply." },
		{ EditorTool.DeleteObject, "TOOL: Object Eraser\n\nLeft-click directly on any unit or prop in 3D scene to erase and remove it from the map." },
		{ EditorTool.SelectMove, "TOOL: Select / Move\n\nLeft-click directly on any unit, prop, or decal to select it. Hold and drag left click to move it. Drag on empty ground to marquee-select every object whose origin falls inside the rectangle. Shift adds to the selection and Ctrl toggles it. Use Alt + MouseWheel to scale, or Delete to delete." },
		{ EditorTool.Eyedropper, "TOOL: Eyedropper / Picker\n\nLeft-click directly on any unit, prop, or decal to copy and select it as the active placement tool. Click on terrain to copy its texture color, or hold Shift to copy its height." },
		{ EditorTool.Noise, "TOOL: Roughen Terrain\n\nDrag left-click to apply random height variations/noise to ruggedize the terrain surface. Adjust size and strength in settings." },
		{ EditorTool.Water, "TOOL: Water Flood Fill\n\nClick on terrain to flood-fill water bounded by cliff walls. Uses selected Water Profile or toggles water." },
		{ EditorTool.PaintPathing, "TOOL: Pathing Layer Painting\n\nDrag left click to paint pathing properties (ground, flying, water, etc.) onto the map. Use checkboxes to select layers, and Mode to Add/Remove." },
		{ EditorTool.FloodFillPathing, "TOOL: Flood Fill Pathing\n\nClick once on the terrain map to flood-fill pathing properties (ground, flying, water, etc.) across an area sharing the same texture color until hitting a boundary. Use checkboxes to select layers, and Mode to Add/Remove." },
		{ EditorTool.Measure, "TOOL: Tape Measure\n\nClick on the terrain to set measurement origin. Move the cursor or click again to lock the endpoint. Displays live Euclidean distance, Manhattan distance, grid delta, and slope." },
		{ EditorTool.None, "Select a tool from the panels to begin terrain modification." }
	};

	public static readonly int[] SpokeCountOptions = new[] { 2, 3, 4, 5, 6, 8, 12, 16 };

	public const float MIN_CLUMP_COUNT = 1.0f;
	public const float MAX_CLUMP_COUNT = 20.0f;
	public const float MIN_CLUMP_SCALE = 0.0f;
	public const float MAX_CLUMP_SCALE = 1.0f;
	public const float MAX_CLUMP_DENSITY = MAX_CLUMP_COUNT;

	private readonly EditorTerrainSurface _surface = new();

	private readonly EditorClumpSession _clump = new();

	public void ResetPaintThrottle() => _terrainEdit.ResetPaintThrottle();

	private readonly EditorTerrainEditSession _terrainEdit;
	private readonly EditorPasteWriter _paste;
	private readonly EditorAreaClipboard _clipboard;
	private readonly EditorRampTool _ramp;
	private readonly EditorInteractionState _interaction = new();

	public EditorService()
	{
		_terrainEdit = new EditorTerrainEditSession(_surface);
		_clipboard = new EditorAreaClipboard(_surface);
		_paste = new EditorPasteWriter(_surface, _clipboard);
		_ramp = new EditorRampTool(_surface);
	}

	public bool PasteOptionTextures { get; set; } = true;
	public bool PasteOptionHeights { get; set; } = true;
	public Godot.Vector3 DragObjectStartHitPos { get; set; }
	public Vector2 DragStartMousePos { get; set; }
	public Godot.Vector3 DragStartGroundPos { get; set; }
	public bool DragObjectHasMoved { get; set; }

	public bool IsSwatchCompatibleToolDomain(EditorTool tool) => tool switch
	{
		EditorTool.Raise => true,
		EditorTool.Lower => true,
		EditorTool.Height => true,
		EditorTool.Smooth => true,
		EditorTool.Plateau => true,
		EditorTool.Noise => true,
		EditorTool.Ramp => true,
		EditorTool.Water => true,
		EditorTool.PaintTexture => true,
		EditorTool.FloodFill => true,
		EditorTool.Eyedropper => true,
		EditorTool.SelectArea => true,
		_ => false
	};

	public void SetTerrainSplatMap(TerrainSplatWeights[,]? splatMap, TerrainSplatWeights[,]? cliffSplatMap = null)
	{
		_surface.SplatMap = splatMap;
		if (cliffSplatMap != null)
		{
			_surface.CliffSplatMap = cliffSplatMap;
		}
		else if (GameContext.Host?.GroundTerrain?.CliffSplatMap != null)
		{
			_surface.CliffSplatMap = GameContext.Host.GroundTerrain.CliffSplatMap;
		}

		if (splatMap != null)
		{
			AlignSplatMapSlots(0, 0, splatMap.GetLength(0) - 1, splatMap.GetLength(1) - 1);
		}
	}

	public bool IsPastingObject => _interaction.IsPastingObject;
	public bool HasCachedRandom => _interaction.HasCachedRandom;
	public float CachedRandomRotation => _interaction.CachedRandomRotation;
	public float CachedRandomScale => _interaction.CachedRandomScale;
	public Vector2I? SelectionStart => _interaction.SelectionStart;
	public Vector2I? SelectionEnd => _interaction.SelectionEnd;
	public bool IsSelectingArea => _interaction.IsSelectingArea;

	public Vector2I? ObjectMarqueeStart => _interaction.ObjectMarqueeStart;
	public Vector2I? ObjectMarqueeEnd => _interaction.ObjectMarqueeEnd;
	public bool IsObjectMarqueeSelecting => _interaction.IsObjectMarqueeSelecting;

	public CopiedAreaTemplate? CopiedArea
	{
		get => _clipboard.CopiedArea;
		set => _clipboard.CopiedArea = value;
	}

	public bool HasCopiedArea => _clipboard.HasCopiedArea;
	public int CopiedAreaWidth => _clipboard.CopiedAreaWidth;
	public int CopiedAreaDepth => _clipboard.CopiedAreaDepth;
	public int CopiedAreaAnchorX => _clipboard.CopiedAreaAnchorX;
	public int CopiedAreaAnchorZ => _clipboard.CopiedAreaAnchorZ;
	public int CopiedAreaSourceMinX => _clipboard.CopiedAreaSourceMinX;
	public int CopiedAreaSourceMinZ => _clipboard.CopiedAreaSourceMinZ;
	public int CopiedAreaSourceMaxX => _clipboard.CopiedAreaSourceMaxX;
	public int CopiedAreaSourceMaxZ => _clipboard.CopiedAreaSourceMaxZ;
	public bool HasCopiedAreaMask => _clipboard.HasCopiedAreaMask;
	public bool IsCopiedCellMasked(int srcX, int srcZ) => _clipboard.IsCopiedCellMasked(srcX, srcZ);

	public int GetPasteAnchorX(int index, int width) => _clipboard.GetPasteAnchorX(index, width);

	public void SetCopiedAreaAnchor(int anchorTileX, int anchorTileZ) => _clipboard.SetCopiedAreaAnchor(anchorTileX, anchorTileZ);

	public CopiedObjectTemplate? GetCopiedObject() => _clipboard.GetCopiedObject();

	public void SetCopiedObject(CopiedObjectTemplate? template) => _clipboard.SetCopiedObject(template);

	public void CopyArea(int minX, int minZ, int maxX, int maxZ, List<CopiedEntityInfo> entities, bool isSquare = true) =>
		_clipboard.CopyArea(minX, minZ, maxX, maxZ, entities, isSquare);

	public List<CopiedEntityInfo> BuildCopiedEntityList(
		int minX, int minZ, int maxX, int maxZ,
		IReadOnlyList<CopiedEntityCandidate> candidates,
		bool isSquare = true) =>
		_clipboard.BuildCopiedEntityList(minX, minZ, maxX, maxZ, candidates, isSquare);

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

	public Godot.Vector3? RampStartPos => _ramp.RampStartPos;
	public bool IsDrawingTerrain => _terrainEdit.IsDrawing;
	public bool IsDrawingClump => _clump.IsDrawing;

	public void CopyEditorPropDomain(string propId, float rotation, float scale)
	{
		SetCopiedObject(new CopiedObjectTemplate
		{
			Type = "prop",
			Id = propId,
			Rotation = rotation,
			Scale = scale,
			Player = 0
		});
	}

	public void CopyEditorDecalDomain(string decalId, float rotation, float scale)
	{
		SetCopiedObject(new CopiedObjectTemplate
		{
			Type = "decal",
			Id = decalId,
			Rotation = rotation,
			Scale = scale,
			Player = 0
		});
	}

	public void ApplyCopiedObjectStateDomain(CopiedObjectTemplate copiedObj, out EditorTool newTool)
	{
		SetIsPastingObject(true);
		EditorPlacementRotation = copiedObj.Rotation;

		newTool = EditorTool.None;
		switch (copiedObj.Type)
		{
			case "unit":
				newTool = EditorTool.PlaceUnit;
				SetPlaceUnitPlayerIndexDomain(copiedObj.Player);
				break;
			case "prop":
				newTool = EditorTool.PlaceProp;
				break;
			case "decal":
				newTool = EditorTool.PlaceDecal;
				break;
		}
	}

	public void SetIsPastingObject(bool value) => _interaction.SetIsPastingObject(value);

	public void SetSelectionStart(Vector2I? value) => _interaction.SetSelectionStart(value);

	public void SetSelectionEnd(Vector2I? value) => _interaction.SetSelectionEnd(value);

	public void SetIsSelectingArea(bool value) => _interaction.SetIsSelectingArea(value);

	public void SetObjectMarquee(Vector2I? start, Vector2I? end, bool selecting) => _interaction.SetObjectMarquee(start, end, selecting);

	public void ClearObjectMarquee() => _interaction.ClearObjectMarquee();

	public void GenerateNewRandomPlacementRotationAndScale() => _interaction.GenerateNewRandomPlacementRotationAndScale();

	public void InvalidateCachedRandom() => _interaction.InvalidateCachedRandom();

	public (int minX, int minZ, int maxX, int maxZ) GetCurrentSelectionBounds() => _interaction.GetSelectionBounds();

	private void SetGridNodeHeight(ref TerrainState terrain, int gx, int gz, float height) =>
		EditorTerrainGeometry.SetGridNodeHeight(ref terrain, gx, gz, height);

	private float GetGridNodeHeight(in TerrainState terrain, int gx, int gz) =>
		EditorTerrainGeometry.GetGridNodeHeight(in terrain, gx, gz);

	public float GetTerrainHeightAt(Godot.Vector3 worldPos)
	{
		if (!TryGetTerrainState(out Entity terrainEntity))
		{
			return 0.0f;
		}

		ref var terrain = ref World.Get<TerrainState>(terrainEntity);
		return EditorTerrainQueries.GetHeightAt(in terrain, worldPos);
	}

	public WaterType GetWaterModeAt(Godot.Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		return EditorTerrainQueries.GetWaterModeAt(in terrain, worldPos);
	}

	public byte GetWaterProfileIndexAt(Godot.Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		return EditorTerrainQueries.GetWaterProfileIndexAt(in terrain, worldPos);
	}

	public float GetWaterHeightAt(Godot.Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		return EditorTerrainQueries.GetWaterHeightAt(in terrain, worldPos);
	}

	public TerrainEditResult ApplyContinuousTerrainEditing(
		Godot.Vector3 worldPos, float delta, EditorTool activeTool,
		float brushRadius, float brushStrength, bool brushIsSquare, bool blockMode,
		float blockLevelHeight, int paintTextureIndex, int cliffPaintTextureIndex,
		int pathingMask, bool pathingAdd, bool isFirstClick = false,
		bool applyGroundTexture = true, bool applyCliffTexture = true)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null || !EnsureSplatMapsInitialized()) return default;

		bool isHeights = EditorTerrainEditSession.IsHeightTool(activeTool);
		bool isPaint = activeTool == EditorTool.PaintTexture;
		bool isPathing = activeTool == EditorTool.PaintPathing;

		if (!isHeights && !isPaint && !isPathing) return default;
		if (isFirstClick) _terrainEdit.ResetPaintThrottle();

		var ctx = new ContinuousEditContext
		{
			WorldPos = worldPos,
			Delta = delta,
			ActiveTool = activeTool,
			BrushRadius = brushRadius,
			BrushStrength = brushStrength,
			BrushIsSquare = brushIsSquare,
			PaintTextureIndex = paintTextureIndex,
			CliffPaintTextureIndex = cliffPaintTextureIndex,
			PathingMask = pathingMask,
			PathingAdd = pathingAdd,
			ApplyGroundTexture = applyGroundTexture,
			ApplyCliffTexture = applyCliffTexture,
			Width = terrain.Width,
			Depth = terrain.Depth,
			QuadSize = terrain.QuadSize,
			ModMinX = terrain.Width,
			ModMinZ = terrain.Depth,
			ModMaxX = -1,
			ModMaxZ = -1,
			NowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
			IsHeights = isHeights
		};

		bool isBlock = (blockMode || activeTool == EditorTool.Height) && activeTool != EditorTool.Noise && activeTool != EditorTool.Smooth;

		if (isBlock)
		{
			_terrainEdit.ApplyBlock(ref terrain, ref ctx, blockLevelHeight);
		}
		else
		{
			_terrainEdit.ApplyContinuous(ref terrain, ref ctx);
		}

		return _terrainEdit.Finalize(ctx, isHeights, isPaint, isPathing);
	}

	public void BeginTerrainDraw(
		Godot.Vector3 hitPos,
		EditorTool activeTool,
		bool blockMode,
		float blockLevelHeight,
		float[,]? currentHeights = null,
		TerrainSplatWeights[,]? currentSplatMap = null,
		int[,]? currentPathing = null,
		TerrainSplatWeights[,]? currentCliffSplatMap = null)
	{
		_terrainEdit.BeginDraw(hitPos, activeTool, blockMode, blockLevelHeight, currentHeights, currentSplatMap, currentPathing, currentCliffSplatMap);
	}

	public TerrainModifyAction EndTerrainDraw(
		float[,]? currentHeights = null,
		TerrainSplatWeights[,]? currentSplatMap = null,
		int[,]? currentPathing = null,
		TerrainSplatWeights[,]? currentCliffSplatMap = null)
	{
		return _terrainEdit.EndDraw(currentHeights, currentSplatMap, currentPathing, currentCliffSplatMap);
	}

	public void ResetDrawState() => _terrainEdit.ResetDrawState();

	public void ResetAllState()
	{
		_interaction.ClearSelection();
		_interaction.ClearObjectMarquee();
		_clipboard.ClearArea();
		_terrainEdit.ResetAll();
		_surface.SplatMap = null;
		_surface.CliffSplatMap = null;
		_clump.Reset();
	}

	public void TickClumpCooldown(float delta) => _clump.TickCooldown(delta);

	public bool CanSpawnClump() => _clump.CanSpawn;

	public void BeginClumpSession() => _clump.Begin();

	public Entity SpawnPropDomain(string propId, System.Numerics.Vector3 position, float rotationY, float scale, string cleanId, float defaultAmount, float terrainHeight, float collisionRadius)
	{
		var entity = PropEntityFactory.CreateCore(
			World,
			propId,
			new System.Numerics.Vector3(position.X, terrainHeight, position.Z),
			rotationY,
			scale,
			collisionRadius);

		GameContext.RegistryService.TryAddResourceNodeComponent(entity, propId, cleanId, defaultAmount);
		return entity;
	}

	public void RecordClumpSpawnAction(IEditorAction action) => _clump.Record(action);

	public void SetClumpCooldown(float value) => _clump.SetCooldown(value);

	public CompositeAction? EndClumpSession() => _clump.End();

	public List<EntitySpawnRequest> BuildClumpSpawnRequests(
		Godot.Vector3 centerPos,
		EditorTool activeTool,
		string activePlaceId,
		int placeUnitPlayerIndex,
		float placementScale,
		float clumpCount,
		float clumpScale,
		float brushRadius,
		bool brushIsSquare,
		bool randomRotation,
		bool randomScale,
		float placementRotation,
		MirrorMode mirrorMode,
		float assetBaseCollisionRadius = 0.5f) =>
		EditorClumpPlacement.BuildSpawnRequests(centerPos, activeTool, activePlaceId, placeUnitPlayerIndex, placementScale, clumpCount, clumpScale, brushRadius, brushIsSquare, randomRotation, randomScale, placementRotation, mirrorMode, assetBaseCollisionRadius, _clump.Points, _symmetryPivot, _symmetryFolds);

	public bool ApplyRamp(Godot.Vector3 start, Godot.Vector3 end, float brushRadius, bool blockMode, float blockLevelHeight) =>
		_ramp.Apply(start, end, brushRadius, blockMode, blockLevelHeight);

	public void SetRampStartPos(Godot.Vector3? pos) => _ramp.SetStart(pos);

	public float GetMinHeightInBrushBounds(Godot.Vector3 worldPos, float brushRadius, bool brushIsSquare) =>
		EditorTerrainQueries.GetMinHeightInBrushBounds(worldPos, brushRadius, brushIsSquare);

	public PasteAreaResult BuildPasteAreaResult(
		int startX,
		int startZ,
		bool pasteHeights,
		bool pasteTextures,
		bool pasteEntities,
		bool pastePathing,
		MirrorMode mirrorMode,
		float rotationDegrees,
		PasteReflection pasteReflection = PasteReflection.None) =>
		_paste.Build(startX, startZ, pasteHeights, pasteTextures, pasteEntities, pastePathing, mirrorMode, rotationDegrees, pasteReflection, _symmetryPivot, _symmetryFolds);

	public EraseAreaResult BuildEraseAreaResult(
		int minX, int minZ, int maxX, int maxZ,
		bool pasteHeights,
		bool pasteTextures,
		bool pasteEntities,
		bool pastePathing,
		ReadOnlySpan<Godot.Vector3> candidatePositions,
		bool isSquare = true)
	{
		var result = new EraseAreaResult { ErasedCandidateIndices = new List<int>() };
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return result;

		EnsureSplatMapsInitialized();

		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
		bool terrainModified = false;
		bool pathingModified = false;

		if (pasteHeights || pasteTextures || (pastePathing && terrain.PathingCodes != null))
		{
			EditorTerrainSurface.ProcessTerrainErase(ref terrain, minX, minZ, maxX, maxZ, selWidth, selDepth, isSquare, pasteHeights, pasteTextures, pastePathing, ref terrainModified, ref pathingModified, _surface.SplatMap, _surface.CliffSplatMap);
			if (terrainModified && pasteTextures) AlignSplatMapSlots(minX - 2, minZ - 2, maxX + 2, maxZ + 2);
		}

		result.TerrainModified = terrainModified;
		result.HeightsModified = pasteHeights && terrainModified;
		result.PathingModified = pathingModified;

		if (pasteEntities)
		{
			EditorTerrainSurface.ProcessEntityErase(minX, minZ, maxX, maxZ, terrain.Width, terrain.Depth, terrain.QuadSize, isSquare, candidatePositions, result.ErasedCandidateIndices);
		}

		return result;
	}

	public (float[,]? Heights, TerrainSplatWeights[,]? SplatMap, bool IsCliff) PerformFloodFill(
		Godot.Vector3 clickPos,
		int fillTextureIndex,
		int cliffTextureIndex,
		MirrorMode mirrorMode,
		bool isCliff = false) =>
		_surface.PerformTextureFloodFill(clickPos, fillTextureIndex, cliffTextureIndex, mirrorMode, isCliff, _symmetryPivot, _symmetryFolds);

	public (int[,]? Before, int[,]? After) PerformFloodFillPathing(Godot.Vector3 clickPos, int pathingMask, bool pathingAdd, MirrorMode mirrorMode) =>
		_surface.PerformPathingFloodFill(clickPos, pathingMask, pathingAdd, mirrorMode, _symmetryPivot, _symmetryFolds);

	public (TerrainCell[,]? BeforeCells, TerrainCell[,]? AfterCells, int[,]? BeforePathing, int[,]? AfterPathing, bool WasAdded) PerformWaterFloodFill(
		Godot.Vector3 clickPos,
		WaterType activeWaterMode,
		byte activeWaterProfile,
		float waterHeight,
		MirrorMode mirrorMode,
		bool isRemoveAction = false) =>
		_surface.PerformWaterFloodFill(clickPos, activeWaterMode, activeWaterProfile, waterHeight, mirrorMode, isRemoveAction, _symmetryPivot, _symmetryFolds);

	public bool FillWaterPoolAtCell(int cellX, int cellZ, float waterSurfaceY) =>
		_surface.FillWaterPoolAtCell(cellX, cellZ, waterSurfaceY);

	public List<MirroredTransform> GetMirroredTransforms(
		Godot.Vector3 pos,
		float rotation,
		MirrorMode mirrorMode,
		Vector2? pivot = null,
		int? folds = null)
	{
		return EditorGridSnapping.GetMirroredTransforms(pos, rotation, mirrorMode, pivot ?? _symmetryPivot, folds ?? _symmetryFolds);
	}

	public (int startX, int startZ, int targetWidth, int targetDepth) GetAnchoredPasteBounds(int targetX, int targetZ, float rotationDegrees, PasteReflection reflection = PasteReflection.None)
	{
		return EditorGridSnapping.GetAnchoredPasteBounds(_clipboard.CopiedArea, targetX, targetZ, rotationDegrees, reflection);
	}

	public (int cx, int cz) WorldPosToCellCoords(Godot.Vector3 worldPos)
	{
		return EditorGridSnapping.WorldPosToCellCoords(worldPos);
	}

	public Godot.Vector3 SnapToGrid(Godot.Vector3 worldPos)
	{
		return EditorGridSnapping.SnapToGrid(worldPos);
	}

	public Godot.Vector3 SnapToVertex(Godot.Vector3 worldPos)
	{
		return EditorGridSnapping.SnapToVertex(worldPos);
	}

	public (int vx, int vz) WorldPosToVertexCoords(Godot.Vector3 worldPos)
	{
		return EditorGridSnapping.WorldPosToVertexCoords(worldPos);
	}

	internal static bool TryGetTerrainState(out Entity terrainEntity)
	{
		terrainEntity = Entity.Null;
		if (GameContext.World == null) return false;

		var worldQuery = QueryCache.AllTerrainStateQuery;
		Entity foundEntity = Entity.Null;
		World.Query(in worldQuery, (Entity entity) => foundEntity = entity);
		terrainEntity = foundEntity;
		return terrainEntity != Entity.Null && World.IsAlive(terrainEntity);
	}

	internal static ref TerrainState GetTerrainState()
	{
		if (!TryGetTerrainState(out Entity terrainEntity))
		{
			throw new InvalidOperationException("TerrainState entity not found in ECS world.");
		}

		return ref World.Get<TerrainState>(terrainEntity);
	}

	public List<Godot.Vector3> GetMirroredPositions(Godot.Vector3 pos, MirrorMode mirrorMode)
	{
		return EditorGridSnapping.GetMirroredPositions(pos, mirrorMode, _symmetryPivot, _symmetryFolds);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static TerrainCell RotateCell(in TerrainCell cell, int rotSteps) =>
		EditorTerrainGeometry.RotateCell(in cell, rotSteps);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static TerrainCell MirrorCell(in TerrainCell cell, MirrorMode mode) =>
		EditorTerrainGeometry.MirrorCell(in cell, mode);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static (TerrainSplatWeights NW, TerrainSplatWeights NE, TerrainSplatWeights SE, TerrainSplatWeights SW) MirrorSplatQuad(
		in TerrainSplatWeights nw, in TerrainSplatWeights ne, in TerrainSplatWeights se, in TerrainSplatWeights sw, MirrorMode mode) =>
		EditorTerrainGeometry.MirrorSplatQuad(in nw, in ne, in se, in sw, mode);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static (TerrainSplatWeights NW, TerrainSplatWeights NE, TerrainSplatWeights SE, TerrainSplatWeights SW) RotateSplatQuad(
		in TerrainSplatWeights nw, in TerrainSplatWeights ne, in TerrainSplatWeights se, in TerrainSplatWeights sw, int rotSteps) =>
		EditorTerrainGeometry.RotateSplatQuad(in nw, in ne, in se, in sw, rotSteps);

	private void TrySetSplat(TerrainSplatWeights[,] map, int x, int z, int w, int d, in TerrainSplatWeights val) =>
		EditorTerrainGeometry.TrySetSplat(map, x, z, w, d, in val);

	private void SetGridNodeSplat(int gx, int gz, in TerrainSplatWeights nw, in TerrainSplatWeights ne, in TerrainSplatWeights se, in TerrainSplatWeights sw)
	{
		_surface.SetGridNodeSplat(gx, gz, in nw, in ne, in se, in sw);
	}

	private void SetGridNodeCliffSplat(int gx, int gz, in TerrainSplatWeights nw, in TerrainSplatWeights ne, in TerrainSplatWeights se, in TerrainSplatWeights sw)
	{
		_surface.SetGridNodeCliffSplat(gx, gz, in nw, in ne, in se, in sw);
	}

	private void SanitizeCellCorners(TerrainCell[,] cells, int gx, int gz, int w, int d, float h) =>
		EditorTerrainGeometry.SanitizeCellCorners(cells, gx, gz, w, d, h);

	private void SanitizeCornerHeights(ref TerrainState terrain)
	{
		EditorGridSnapping.SanitizeCornerHeights(ref terrain);
	}

	private void PasteBlock(
		CopiedAreaTemplate copiedArea,
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
		_paste.PasteBlock(copiedArea, blockStartX, blockStartZ, rotSteps, pasteReflection, symmetryMirror, foldRotSteps, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);
	}

	public bool GetBlockMode(Entity worldEntity) => EditorEntityState.GetBlockMode(worldEntity);

	public void SetBlockMode(Entity worldEntity, bool value) => EditorEntityState.SetBlockMode(worldEntity, value);

	public float GetBlockLevelHeight(Entity worldEntity) => EditorEntityState.GetBlockLevelHeight(worldEntity);

	public void SetBlockLevelHeight(Entity worldEntity, float value) => EditorEntityState.SetBlockLevelHeight(worldEntity, value);

	public WaterType GetWaterMode(Entity worldEntity) => EditorEntityState.GetWaterMode(worldEntity);

	public void SetWaterMode(Entity worldEntity, WaterType value) => EditorEntityState.SetWaterMode(worldEntity, value);

	public byte GetWaterProfileIndex(Entity worldEntity) => EditorEntityState.GetWaterProfileIndex(worldEntity);

	public void SetWaterProfileIndex(Entity worldEntity, byte value) => EditorEntityState.SetWaterProfileIndex(worldEntity, value);

	public List<ProceduralSpawnDescriptor> BuildWaterProceduralSpawns(int x, int z, byte profileIndex, float waterY, float quadSize, int width, int depth)
	{
		return EditorProceduralSpawns.BuildWater(x, z, profileIndex, waterY, quadSize, width, depth);
	}

	public List<ProceduralSpawnDescriptor> BuildTerrainProceduralSpawns(int x, int z, int textureIndex, float terrainY, float quadSize, int width, int depth)
	{
		return EditorProceduralSpawns.BuildTerrain(x, z, textureIndex, terrainY, quadSize, width, depth);
	}

	public float GetCameraBoundsLeft(Entity worldEntity) => EditorEntityState.GetCameraBoundsLeft(worldEntity);

	public void SetCameraBoundsLeft(Entity worldEntity, float value) => EditorEntityState.SetCameraBoundsLeft(worldEntity, value);

	public float GetCameraBoundsRight(Entity worldEntity) => EditorEntityState.GetCameraBoundsRight(worldEntity);

	public void SetCameraBoundsRight(Entity worldEntity, float value) => EditorEntityState.SetCameraBoundsRight(worldEntity, value);

	public float GetCameraBoundsTop(Entity worldEntity) => EditorEntityState.GetCameraBoundsTop(worldEntity);

	public void SetCameraBoundsTop(Entity worldEntity, float value) => EditorEntityState.SetCameraBoundsTop(worldEntity, value);

	public float GetCameraBoundsBottom(Entity worldEntity) => EditorEntityState.GetCameraBoundsBottom(worldEntity);

	public void SetCameraBoundsBottom(Entity worldEntity, float value) => EditorEntityState.SetCameraBoundsBottom(worldEntity, value);

	public string GetSkyboxPath(Entity worldEntity) => EditorEntityState.GetSkyboxPath(worldEntity);

	public void SetSkyboxPath(Entity worldEntity, string value) => EditorEntityState.SetSkyboxPath(worldEntity, value);

	public bool GetHasUnsavedChanges(Entity worldEntity) => EditorEntityState.GetHasUnsavedChanges(worldEntity);

	public void SetHasUnsavedChanges(Entity worldEntity, bool value) => EditorEntityState.SetHasUnsavedChanges(worldEntity, value);

	public static string GetLocalizedToolName(EditorTool tool) => EditorToolLabels.GetLocalizedToolName(tool);

	public string GetTerrainStatusString(Godot.Vector3 pos, EditorTool tool, string activePlaceId)
	{
		return EditorToolLabels.GetTerrainStatusString(pos, tool, activePlaceId);
	}

	public void AlignSplatMapSlots(int minX, int minZ, int maxX, int maxZ)
	{
		_surface.AlignSplatMapSlots(minX, minZ, maxX, maxZ);
	}

	public static DateTime LastEditorChangeUtc => EditorWorkspaceWatcher.LastEditorChangeUtc;

	internal static void NoteEditorChange() => EditorWorkspaceWatcher.NoteEditorChange();

	public static void NoteInternalFileWrite(string filePath) => EditorWorkspaceWatcher.NoteInternalFileWrite(filePath);

	public bool PasteOptionEntities { get; set; } = true;
	public bool PasteOptionPathing { get; set; } = true;
	public float EditorPasteRotation { get; set; } = 0.0f;
	public PasteReflection EditorPasteReflection { get; set; } = PasteReflection.None;

	private bool _isPaused;
	private bool _isSyncing;

	public void ClearHistoryAndState(ref bool hasUnsavedChanges)
	{
		EditorHistoryManager.Clear();
		hasUnsavedChanges = false;
	}

	public bool IsPaused
	{
		get => _isPaused;
		set
		{
			_isPaused = value;
			EditorWorkspaceWatcher.IsPaused = value;
			if (value) EditorWorkspaceWatcher.CancelPendingEvaluation();
		}
	}

	public void UpdateWatchedFileTimestamps() => EditorWorkspaceWatcher.UpdateWatchedFileTimestamps();

	public static void ReconcileKnownFileStatesFromDisk(bool clearPendingChanges = true) => EditorWorkspaceWatcher.ReconcileKnownFileStatesFromDisk(clearPendingChanges);

	public static bool HasPendingExternalChanges => EditorWorkspaceWatcher.HasPendingExternalChanges;

	public bool TryTakePendingExternalChanges(out bool metadataChanged, out bool terrainChanged) => EditorWorkspaceWatcher.TryTakePendingExternalChanges(out metadataChanged, out terrainChanged);

	public void StartWorkspaceWatcher(string directory) => EditorWorkspaceWatcher.Start(directory);

	public void StopWorkspaceWatcher() => EditorWorkspaceWatcher.Stop();

	public bool CommitCoordinate(string name, int minCellX, int minCellZ, int maxCellX, int maxCellZ)
	{
		return GameContext.Host?.CommitCoordinateExternal(name, minCellX, minCellZ, maxCellX, maxCellZ) ?? false;
	}

	public bool DeleteCoordinate(string name)
	{
		if (GameContext.Host == null) return false;
		GameContext.Host.DeleteCoordinateExternal(name);
		return true;
	}

	public void SelectCoordinate(string name)
	{
		GameContext.Host?.SelectCoordinateExternal(name);
	}

	private bool EnsureSplatMapsInitialized()
	{
		if (_surface.SplatMap == null && GameContext.Host?.GroundTerrain != null)
			_surface.SplatMap = GameContext.Host.GroundTerrain.SplatMap;
		if (_surface.CliffSplatMap == null && GameContext.Host?.GroundTerrain != null)
			_surface.CliffSplatMap = GameContext.Host.GroundTerrain.CliffSplatMap;
		return _surface.SplatMap != null;
	}

	public PasteReflection CyclePasteReflectionDomain()
	{
		var current = this.EditorPasteReflection;
		var next = current switch
		{
			PasteReflection.None => PasteReflection.Horizontal,
			PasteReflection.Horizontal => PasteReflection.Vertical,
			PasteReflection.Vertical => PasteReflection.None,
			_ => PasteReflection.None
		};
		this.EditorPasteReflection = next;
		return next;
	}

	public void RequestOpenFromCasDomain(string casSourceDirectory, string? defaultSaveFolder)
	{
		this.PendingCasSourceDirectory = casSourceDirectory;
		this.PendingDefaultSaveFolder = defaultSaveFolder;
	}

	public void UpdateLastMetadataSyncTimeDomain(long writeTimeTicks)
	{
		this.LastMetadataSyncTime = Math.Max(writeTimeTicks, DateTime.UtcNow.Ticks);
	}

	public void InitializeEditingDirectoryDomain(long terrainTime, long metadataTime)
	{
		this.LastTerrainSyncTime = terrainTime;
		this.LastMetadataSyncTime = metadataTime;
		if (!HasEditingDirectory) return;
		this.StartWorkspaceWatcher(this._editingDirectory);
	}

	public (string? casDir, string? defaultSave) ConsumePendingCasSourceDomain()
	{
		string? casDir = this.PendingCasSourceDirectory;
		string? defaultSave = this.PendingDefaultSaveFolder;
		this.PendingCasSourceDirectory = null;
		this.PendingDefaultSaveFolder = null;
		return (casDir, defaultSave);
	}

	public void RedoDomain()
	{
		EditorHistoryManager.Redo();
	}

	public void SetPendingCoordinatesDomain(int minX, int minZ, int maxX, int maxZ)
	{
		PendingCoordinateMinX = minX;
		PendingCoordinateMinZ = minZ;
		PendingCoordinateMaxX = maxX;
		PendingCoordinateMaxZ = maxZ;
	}

	public void SetPlaceUnitPlayerIndexDomain(int playerIndex)
	{
		PlaceUnitPlayerIndex = Mathf.Clamp(playerIndex, 0, PlayerColorConfig.Palette.Length - 1);
	}

	public void UndoDomain()
	{
		EditorHistoryManager.Undo();
	}

	public bool TryGetSymmetryVisibilityState(out bool isRotational, out GridOverlayMode gridMode)
	{
		isRotational = false;
		gridMode = EditorGridMode;

		if (GameContext.Host == null)
			return false;

		isRotational = GameContext.Host.EditorMirrorMode == MirrorMode.Rotational;
		return true;
	}

	public void LoadMapProperties()
	{
		GameContext.Terrain?.ReloadWaterProfiles();
	}

	public TerrainSplatWeights[,] GenerateDefaultCliffSplatMap(int width, int depth)
	{
		int cliffW = width + 1;
		int cliffD = depth + 1;
		var splatMap = new TerrainSplatWeights[cliffW, cliffD];
		for (int z = 0; z < cliffD; z++)
		{
			for (int x = 0; x < cliffW; x++)
			{
				splatMap[x, z] = TerrainSplatWeights.CreateSolid(1);
			}
		}

		return splatMap;
	}

	public void CacheDecalAsset(string cacheKey, DecalAssetData assetData)
	{
		DecalAssetCache[cacheKey] = assetData;
	}

	public DecalMetadata? FindDecalMetadata(MapMetadata metadata, string decalId, string filename, string baseKey)
	{
		return EditorDecalMetadata.FindDecalMetadata(metadata, decalId, filename, baseKey);
	}

	public VfxMetadata? FindVfxMetadata(MapMetadata metadata, string decalId, string filename, string baseKey)
	{
		return EditorDecalMetadata.FindVfxMetadata(metadata, decalId, filename, baseKey);
	}

	public (int cols, int rows, float fps, bool blend, string? explicitPath) ParseDecalMetadata(string decalId, string filename, string baseKey, string wsPath)
	{
		return EditorDecalMetadata.ParseDecalMetadata(decalId, filename, baseKey, wsPath);
	}

	public List<string> GetDecalCandidatePaths(string decalId, string? explicitTexturePath, string wsPath)
	{
		return EditorDecalMetadata.GetDecalCandidatePaths(decalId, explicitTexturePath, wsPath);
	}

	public void ParseRtexDecalMetadata(string? customJson, ref int cols, ref int rows)
	{
		EditorDecalMetadata.ParseRtexDecalMetadata(customJson, ref cols, ref rows);
	}

	public void ParseExternalDecalMetadata(string meta, ref int cols, ref int rows)
	{
		EditorDecalMetadata.ParseExternalDecalMetadata(meta, ref cols, ref rows);
	}

	public void SetMirrorModeDomain(MirrorMode mode)
	{
		if (GameContext.Host == null) return;
		GameContext.Host.EditorMirrorMode = mode;
		GameContext.Host.UpdateSymmetryPivotVisuals();
		GameContext.Host.InvalidateSelectionHighlightMesh();
	}

	public void SetSymmetrySpokesDomain(int spokes)
	{
		if (GameContext.Host == null) return;
		GameContext.Host.EditorSymmetryFolds = spokes;
		GameContext.Host.InvalidateSelectionHighlightMesh();
	}

	public void SetPolarRingSpacingDomain(float spacing)
	{
		if (GameContext.Host == null) return;
		EditorPolarRingSpacing = spacing;
		GameContext.Host.GroundTerrain?.SetPolarRingSpacing(spacing);
	}

	public bool ImportTextureAssetFromExtensionDomain(string sourceFilePath, int slotIndex, string wsPath, GameHost? gameHost, out string cleanBaseName, out string outputRtex, Action<string, string, string, int> updateMetadata)
	{
		return EditorAssetImport.ImportTextureAssetFromExtensionDomain(sourceFilePath, slotIndex, wsPath, gameHost, out cleanBaseName, out outputRtex, updateMetadata);
	}

	public void UpdateTextureMetadataAndReload(int index, string cleanBaseName, string outputRtex, GameHost? gameHost, Action<string, string, string, int> updateMetadata)
	{
		EditorAssetImport.UpdateTextureMetadataAndReload(index, cleanBaseName, outputRtex, gameHost, updateMetadata);
	}

	public void CopyOrProcessTexture(string imagePath, string outputRtex, GameHost? gameHost)
	{
		EditorAssetImport.CopyOrProcessTexture(imagePath, outputRtex, gameHost);
	}

	public string PrepareTextureOutputDir(string cleanBaseName)
	{
		return EditorAssetImport.PrepareTextureOutputDir(cleanBaseName, _editingDirectory);
	}

	public string ResolveTextureFileName(string imagePath, string? preferredFileName)
	{
		return EditorAssetImport.ResolveTextureFileName(imagePath, preferredFileName);
	}

	public string GetManifestBlake3(string workspacePath, string mapTitle, string mapVersion, string author)
	{
		return _mapIdentity.GetManifestBlake3(workspacePath, mapTitle, mapVersion, author);
	}

	public string GetMapVersionFromMetadata(bool forceReload, string workspacePath)
	{
		return _mapIdentity.GetVersionFromMetadata(forceReload, workspacePath);
	}

	public string SanitizeMapName(string? candidateName)
	{
		return _mapIdentity.SanitizeName(candidateName);
	}

	public bool TryGetMapNameFromManifest(string workspacePath, out string mapName)
	{
		return _mapIdentity.TryGetNameFromManifest(workspacePath, out mapName);
	}

	public bool TryGetMapNameFromMetadata(string workspacePath, out string mapName)
	{
		return _mapIdentity.TryGetNameFromMetadata(workspacePath, out mapName);
	}

	public bool TryGetMapNameFromActiveHost(out string mapName)
	{
		return _mapIdentity.TryGetNameFromActiveHost(out mapName);
	}

	public string UpdateCachedMapName(string mapName, long nowTicks)
	{
		return _mapIdentity.UpdateCachedName(mapName, nowTicks);
	}

	public static bool TrySanitizeCandidate(string? candidateName, out string sanitizedMapName)
	{
		return EditorMapIdentity.TrySanitizeCandidate(candidateName, out sanitizedMapName);
	}

	public string RemoveUnitObjectAttachmentDomain(string unitId, string socket, string attachmentId, string? parentAttachmentId = null)
	{
		string regKey = EditorAttachmentWrites.RemoveUnitObjectAttachment(unitId, socket, attachmentId, parentAttachmentId, _editingDirectory, out long? metadataSyncTime);
		if (metadataSyncTime is long ticks) LastMetadataSyncTime = ticks;
		return regKey;
	}

	public string RestoreUnitObjectAttachmentsDomain(string targetId, UnitObjectAttachments? snapshot)
	{
		string regKey = EditorAttachmentWrites.RestoreUnitObjectAttachments(targetId, snapshot, _editingDirectory, out long? metadataSyncTime);
		if (metadataSyncTime is long ticks) LastMetadataSyncTime = ticks;
		return regKey;
	}

	public string RestoreUnitPseudoSocketsDomain(string targetId, List<PseudoSocketAnchor>? snapshot)
	{
		string regKey = EditorAttachmentWrites.RestoreUnitPseudoSockets(targetId, snapshot, _editingDirectory, out long? metadataSyncTime);
		if (metadataSyncTime is long ticks) LastMetadataSyncTime = ticks;
		return regKey;
	}

	public (bool Success, string Message, string FileName, string Blake3) ImportSkyboxAsset(string sourceFilePath, string wsPath)
	{
		return EditorAssetImport.ImportSkyboxAsset(sourceFilePath, wsPath);
	}

	public (bool Success, string Message, string FileName, string Blake3) ImportSpritesheetAsset(string sourceFilePath, string wsPath)
	{
		return EditorAssetImport.ImportSpritesheetAsset(sourceFilePath, wsPath);
	}

	public (bool Success, string Message, string FileName, string Blake3) ImportIconAsset(string sourceFilePath, string wsPath)
	{
		return EditorAssetImport.ImportIconAsset(sourceFilePath, wsPath);
	}

	public long GetLastWriteTimeSafe(string path)
	{
		return EditorWorkspace.GetLastWriteTimeTicks(path);
	}

	public long GetMaxTerrainWriteTime(string baseTerrainJsonPath)
	{
		long maxTime = GetLastWriteTimeSafe(baseTerrainJsonPath);
		string? dir = Path.GetDirectoryName(baseTerrainJsonPath);
		if (!string.IsNullOrEmpty(dir))
		{
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(MapTerrainFiles.HeightsPath(dir)));
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(MapTerrainFiles.WaterPath(dir)));
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(MapTerrainFiles.PathingPath(dir)));
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(MapTerrainFiles.SplatIndicesPath(dir, MapTerrainFiles.SplatPrefix)));
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(MapTerrainFiles.SplatWeightsPath(dir, MapTerrainFiles.SplatPrefix)));
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(MapTerrainFiles.SplatIndicesPath(dir, MapTerrainFiles.CliffSplatPrefix)));
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(MapTerrainFiles.SplatWeightsPath(dir, MapTerrainFiles.CliffSplatPrefix)));
		}

		return maxTime;
	}

	public void ResetEditingDirectorySyncStateDomain()
	{
		this.LastTerrainSyncTime = 0;
		this.LastMetadataSyncTime = 0;
	}

	public void GenerateMapDirectoryDomain(string mapName, string mapDir, string projectRoot, string templateDirSearch)
	{
		EditorWorkspace.CreateMapDirectory(mapName, mapDir, projectRoot, templateDirSearch);
	}

	public void GenerateVSCodeFilesExternalDomain()
	{
		if (string.IsNullOrEmpty(this._editingDirectory)) return;
		string unitsPath = Path.Combine(this._editingDirectory, "metadata.json");
		Directory.CreateDirectory(this._editingDirectory);
		MapWorkspaceService.SetupWorkspace(this._editingDirectory, "MapScript");
		string terrainPath = Path.Combine(this._editingDirectory, "terrain.json");
		this.LastTerrainSyncTime = GetMaxTerrainWriteTime(terrainPath);
		this.LastMetadataSyncTime = GetLastWriteTimeSafe(unitsPath);
	}

	public void UpdateWorkspaceModTime()
	{
		_workspace.TouchModTime();
	}

	public bool HasDecalOverrides(int overrideCols, int overrideRows, float overrideFps, bool? overrideSubframeBlend)
	{
		return overrideCols > 0 || overrideRows > 0 || overrideFps > 0.001f || overrideSubframeBlend.HasValue;
	}

	public string ResolveDecalKeyFromMetadata(string decalId, string wsPath)
	{
		return EditorAssetImport.ResolveDecalKeyFromMetadata(decalId, wsPath);
	}

	public string FindDecalFile(string targetKey, string wsPath)
	{
		return EditorAssetImport.FindDecalFile(targetKey, wsPath);
	}

	public void RestoreLoadedCoordinates()
	{
		var loadedCoordinates = GameContext.SaveLoadService.GetLastLoadedCoordinates();
		EditorCoordinates.Clear();
		EditorCoordinates.AddRange(loadedCoordinates.Select(r => new EditorCoordinate { Name = r.Name, MinX = r.MinX, MinZ = r.MinZ, MaxX = r.MaxX, MaxZ = r.MaxZ }));
	}

	private string ApplyModelAssetExtension(string filename)
	{
		if (filename.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || filename.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
		{
			return Path.GetFileNameWithoutExtension(filename) + ".rmesh";
		}

		if (filename.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) || filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || filename.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
		{
			return filename;
		}

		if (!filename.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
		{
			return filename + ".rmesh";
		}

		return filename;
	}

	public string NormalizeModelAssetKey(string pathOrId)
	{
		if (string.IsNullOrEmpty(pathOrId)) return "";
		if (NormalizedAssetKeyCache.TryGetValue(pathOrId, out string? cached))
		{
			return cached;
		}

		string trimmed = pathOrId.Trim().Replace('\\', '/');
		if (trimmed.Contains('/'))
		{
			string prefix = trimmed.Substring(0, trimmed.IndexOf('/'));
			if (AssetIndexService.ValidAssetPrefixes.Contains(prefix))
			{
				string lower = trimmed.ToLowerInvariant();
				NormalizedAssetKeyCache[pathOrId] = lower;
				return lower;
			}
		}

		string filename = Path.GetFileName(trimmed);
		filename = ApplyModelAssetExtension(filename);

		string result = filename.ToLowerInvariant();
		NormalizedAssetKeyCache[pathOrId] = result;
		return result;
	}

	public bool ConsumeModelOffsetSavePending()
	{
		if (ModelYOffsetSavePending || ModelCollisionCircleSavePending)
		{
			ModelYOffsetSavePending = false;
			ModelCollisionCircleSavePending = false;
			return true;
		}
		return false;
	}

	public string GetInitialDirectoryDomain(string lastOpenedFolder, string documentsDir, Func<string, bool> isRestrictedSaveDirectory)
	{
		if (!string.IsNullOrEmpty(LastUsedFolder) && !isRestrictedSaveDirectory(LastUsedFolder) && Directory.Exists(LastUsedFolder))
		{
			return LastUsedFolder;
		}

		if (!string.IsNullOrEmpty(CurrentSourceFolder) && !isRestrictedSaveDirectory(CurrentSourceFolder) && Directory.Exists(CurrentSourceFolder))
		{
			return CurrentSourceFolder;
		}

		if (!string.IsNullOrEmpty(lastOpenedFolder) && !isRestrictedSaveDirectory(lastOpenedFolder) && Directory.Exists(lastOpenedFolder))
		{
			return lastOpenedFolder;
		}

		return documentsDir;
	}

	public bool IsRestrictedSaveDirectoryDomain(string fullPath, string mapBackups, string userDir, string resDir)
	{
		return EditorWorkspace.IsRestrictedSaveDirectory(fullPath, mapBackups, userDir, resDir);
	}

	public string GetDefaultDevelopmentMapDirectoryDomain(string mapName, string docsDir)
	{
		return EditorWorkspace.GetDefaultDevelopmentMapDirectory(mapName, docsDir);
	}

	public async Task<bool> PerformCompilationStepAsyncDomain(string workspace, Func<string, string?> findProjectFile, Func<string, bool, bool> tryBypassCompilation, Func<string, bool> checkMapApiDependency, Func<string, string, bool, Task<bool>> executeDotnetPublishAsync, Action<string> onStatusError, Action<string> onLog, bool isDebug = false)
	{
		return await EditorMapBuild.PerformCompilationStepAsync(workspace, findProjectFile, tryBypassCompilation, checkMapApiDependency, executeDotnetPublishAsync, onStatusError, onLog, isDebug);
	}

	public string? FindProjectFileDomain(string workspace, Action<string> onErrorMessage)
	{
		return EditorMapBuild.FindProjectFile(workspace, onErrorMessage);
	}

	public bool TryBypassCompilationDomain(string workspace, Func<string, string?> findExistingWasmFile, Func<string, DateTime, bool> hasNewerSourceFiles, Action onBypassSuccess, bool isDebug = false)
	{
		return EditorMapBuild.TryBypassCompilation(workspace, findExistingWasmFile, hasNewerSourceFiles, onBypassSuccess, isDebug);
	}
	public void RestoreEditorStateDomain(float waterHeight, int paintTextureIndex, int cliffPaintTextureIndex, CopiedAreaTemplate? copiedArea, System.Numerics.Vector3? selectionStart, System.Numerics.Vector3? selectionEnd)
	{
		this.EditorWaterHeight = waterHeight;
		this.EditorPaintTextureIndex = paintTextureIndex;
		this.EditorCliffPaintTextureIndex = cliffPaintTextureIndex;
		this.CopiedArea = copiedArea;
		SetSelectionStart(selectionStart != null ? new Godot.Vector2I((int)selectionStart.Value.X, (int)selectionStart.Value.Z) : (Godot.Vector2I?)null);
		SetSelectionEnd(selectionEnd != null ? new Godot.Vector2I((int)selectionEnd.Value.X, (int)selectionEnd.Value.Z) : (Godot.Vector2I?)null);
	}

	public void InitializeDefaultTerrainStateDomain(int defaultWidth, int defaultDepth)
	{
		if (!World.Has<Realm.Ecs.Components.Terrain.TerrainState>(GameContext.Simulation.WorldEntity)) return;

		float[,] defaultHeights = new float[defaultWidth, defaultDepth];
		int[,] defaultPathing = new int[defaultWidth, defaultDepth];

		for (int z = 0; z < defaultDepth; z++)
		{
			for (int x = 0; x < defaultWidth; x++)
			{
				defaultHeights[x, z] = 0.0f;
				defaultPathing[x, z] = Realm.Client.EditableTerrain.GetDefaultPathingCode(Realm.Ecs.Components.Terrain.WaterType.None);
			}
		}

		ref var ts = ref World.Get<Realm.Ecs.Components.Terrain.TerrainState>(GameContext.Simulation.WorldEntity);
		ts.SetHeights(defaultHeights);
		ts.PathingCodes = defaultPathing;
		World.Set(GameContext.Simulation.WorldEntity, ts);
	}

	public void UpdateMetadataJsonAssetDomain(string category, string fileName, string blake3Hash, string? subCategory = null, int columns = 0, int rows = 0, int targetSlot = -1) =>
		EditorMetadataOperations.UpdateMetadataJsonAsset(category, fileName, blake3Hash, _editingDirectory, subCategory, columns, rows, targetSlot);
}
