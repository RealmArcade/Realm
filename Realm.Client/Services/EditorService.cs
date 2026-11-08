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
		string scaleWsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string scaleMetaPath = MetadataService.ResolveMetadataPath(scaleWsPath);

		if (File.Exists(scaleMetaPath))
		{
			try
			{
				GameContext.MetadataService.UpdateMetadata(scaleWsPath, meta =>
				{
					meta.MapProperties.MapWidth = newWidth;
					meta.MapProperties.MapHeight = newDepth;
					meta.MapProperties.CameraBoundsLeft = cameraBounds.Left;
					meta.MapProperties.CameraBoundsRight = cameraBounds.Right;
					meta.MapProperties.CameraBoundsTop = cameraBounds.Top;
					meta.MapProperties.CameraBoundsBottom = cameraBounds.Bottom;
				});
			}
			catch (Exception ex)
			{
				GameLog.LogError($"Failed to update metadata.json map dimensions during scale: {ex.Message}");
			}
		}
	}

	public void SetCameraBounds(Entity worldEntity, MapCameraBounds cameraBounds)
	{
		if (GameContext.World == null || !World.IsAlive(worldEntity)) return;

		World.Mutate<EditorState>(worldEntity, (ref EditorState state) =>
		{
			state.CameraBoundsLeft = cameraBounds.Left;
			state.CameraBoundsRight = cameraBounds.Right;
			state.CameraBoundsTop = cameraBounds.Top;
			state.CameraBoundsBottom = cameraBounds.Bottom;
		});

		if (World.Has<CameraState>(worldEntity))
		{
			World.Mutate<CameraState>(worldEntity, (ref CameraState cameraState) =>
			{
				cameraState.LimitLeft = cameraBounds.Left;
				cameraState.LimitRight = cameraBounds.Right;
				cameraState.LimitTop = cameraBounds.Top;
				cameraState.LimitBottom = cameraBounds.Bottom;
			});
		}
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

	public long LastMapNameCacheTicks { get; set; } = 0;
	public string? CachedMapName { get; set; }
	public string? CachedMapVersion { get; set; }
	public string CurrentDirectoryBlake3 { get; set; } = string.Empty;
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

	public double AutoBackupElapsedSeconds { get; set; } = 0;

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
		if (intervalMins <= 0) return false;
		AutoBackupElapsedSeconds += delta;
		double targetSeconds = intervalMins * 60.0;
		if (AutoBackupElapsedSeconds >= targetSeconds)
		{
			AutoBackupElapsedSeconds = 0;
			return true;
		}

		return false;
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

	public string _editingDirectory { get; set; } = string.Empty;

	public bool HasEditingDirectory => !string.IsNullOrEmpty(_editingDirectory);

	public void SetActiveMapDirectory(string absoluteDirectory)
	{
		_editingDirectory = absoluteDirectory;
	}

	public void ClearEditingDirectoryDomain()
	{
		_editingDirectory = string.Empty;
	}

	public void EnsureEditingDirectoryDomain()
	{
		if (HasEditingDirectory) return;

		string? sourceFolder = CurrentSourceFolder;
		if (string.IsNullOrEmpty(sourceFolder) || !Directory.Exists(sourceFolder)) return;

		SetActiveMapDirectory(Path.GetFullPath(sourceFolder));
		SetCurrentSourceFolder(sourceFolder);
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

	private float _clumpSpawnCooldown;
	private bool _isDrawingClump;
	private readonly List<IEditorAction> _clumpSpawnActionsInSession = new();
	private readonly List<(Godot.Vector3 Position, float Radius)> _cachedSessionPoints = new();

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

	private TerrainCell[,]? _terrainCellsBefore;
	private TerrainSplatWeights[,]? _terrainSplatMapBefore;
	private TerrainSplatWeights[,]? _terrainCliffSplatMapBefore;
	private int[,]? _terrainPathingBefore;
	private bool _isDrawingTerrain;
	private int _drawMinX;
	private int _drawMinZ;
	private int _drawMaxX;
	private int _drawMaxZ;

	private CopiedAreaTemplate? _copiedArea;

	private Vector2I? _selectionStart;
	private Vector2I? _selectionEnd;
	private bool _isSelectingArea;

	private Vector2I? _objectMarqueeStart;
	private Vector2I? _objectMarqueeEnd;
	private bool _isObjectMarqueeSelecting;

	private float _cachedRandomRotation;
	private float _cachedRandomScale = 1.0f;
	private bool _hasCachedRandom;
	private bool _isPastingObject;

	private CopiedObjectTemplate? _copiedObject;
	private Godot.Vector3? _rampStartPos;


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

	public bool IsPastingObject => _isPastingObject;
	public bool HasCachedRandom => _hasCachedRandom;
	public float CachedRandomRotation => _cachedRandomRotation;
	public float CachedRandomScale => _cachedRandomScale;
	public Vector2I? SelectionStart => _selectionStart;
	public Vector2I? SelectionEnd => _selectionEnd;
	public bool IsSelectingArea => _isSelectingArea;

	public Vector2I? ObjectMarqueeStart => _objectMarqueeStart;
	public Vector2I? ObjectMarqueeEnd => _objectMarqueeEnd;
	public bool IsObjectMarqueeSelecting => _isObjectMarqueeSelecting;

	public CopiedAreaTemplate? CopiedArea
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


	public int GetPasteAnchorX(int index, int width)
	{
		return index switch
		{
			0 => width / 2,
			1 => 0,
			2 => Math.Max(0, width - 1),
			3 => Math.Max(0, width - 1),
			4 => 0,
			_ => width / 2
		};
	}

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

	public Godot.Vector3? RampStartPos => _rampStartPos;
	public bool IsDrawingTerrain => _isDrawingTerrain;
	public bool IsDrawingClump => _isDrawingClump;

	public CopiedObjectTemplate? GetCopiedObject() => _copiedObject;

	public void SetCopiedObject(CopiedObjectTemplate? template)
	{
		_copiedObject = template;
	}

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

	public void SetIsPastingObject(bool value)
	{
		_isPastingObject = value;
	}

	public void SetRampStartPos(Godot.Vector3? pos)
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

	public void SetObjectMarquee(Vector2I? start, Vector2I? end, bool selecting)
	{
		_objectMarqueeStart = start;
		_objectMarqueeEnd = end;
		_isObjectMarqueeSelecting = selecting;
	}

	public void ClearObjectMarquee()
	{
		_objectMarqueeStart = null;
		_objectMarqueeEnd = null;
		_isObjectMarqueeSelecting = false;
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

		bool isHeights = IsHeightTool(activeTool);
		bool isPaint = activeTool == EditorTool.PaintTexture;
		bool isPathing = activeTool == EditorTool.PaintPathing;

		if (!isHeights && !isPaint && !isPathing) return default;
		if (isFirstClick) ResetPaintThrottle();

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
			ApplyBlockModeEditing(ref terrain, ref ctx, blockLevelHeight);
		}
		else
		{
			ApplyContinuousModeEditing(ref terrain, ref ctx);
		}

		return FinalizeTerrainEditResult(ctx, isHeights, isPaint, isPathing);
	}


	private bool IsHeightTool(EditorTool tool)
	{
		return tool == EditorTool.Raise || tool == EditorTool.Lower ||
			   tool == EditorTool.Height || tool == EditorTool.Smooth ||
			   tool == EditorTool.Plateau || tool == EditorTool.Noise;
	}


	private void ApplyBlockModeEditing(ref TerrainState terrain, ref ContinuousEditContext ctx, float blockLevelHeight)
	{
		if (ctx.ActiveTool == EditorTool.PaintTexture)
		{
			ProcessBlockPaint(ref terrain, ref ctx);
			return;
		}

		int cx = Mathf.Clamp((int)Math.Floor(ctx.WorldPos.X / ctx.QuadSize + ctx.Width / 2.0f), 0, ctx.Width - 1);
		int cz = Mathf.Clamp((int)Math.Floor(ctx.WorldPos.Z / ctx.QuadSize + ctx.Depth / 2.0f), 0, ctx.Depth - 1);
		int N = Mathf.Max(1, (int)Mathf.Round(ctx.BrushRadius));
		int quadMinX = cx - (N - 1) / 2;
		int quadMaxX = cx + N / 2;
		int quadMinZ = cz - (N - 1) / 2;
		int quadMaxZ = cz + N / 2;

		if (ctx.IsHeights) InitializeBlockHeights(ctx.ActiveTool, ctx.WorldPos, blockLevelHeight);

		float targetHeight = Math.Clamp(_activeBlockTargetHeight, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
		sbyte targetMacroTier = (sbyte)Math.Clamp((int)MathF.Round(targetHeight / TerrainCell.TIER_HEIGHT), TerrainCell.MIN_MACRO_TIER, TerrainCell.MAX_MACRO_TIER);

		for (int z = quadMinZ; z <= quadMaxZ; z++)
		{
			for (int x = quadMinX; x <= quadMaxX; x++)
			{
				if (x >= 0 && x < ctx.Width && z >= 0 && z < ctx.Depth)
				{
					if (IsPointInBrush(x, z, quadMinX, quadMinZ, N, ctx.BrushIsSquare))
					{
						ProcessBlockCell(ref terrain, ref ctx, x, z, targetHeight, targetMacroTier);
					}
				}
			}
		}
	}

	private void InitializeBlockHeights(EditorTool tool, Godot.Vector3 worldPos, float blockLevelHeight)
	{
		if (_hasBlockTargetHeight) return;
		float startHeight = GetTerrainHeightAt(worldPos);
		if (tool == EditorTool.Height) _activeBlockTargetHeight = blockLevelHeight;
		else if (tool == EditorTool.Raise) _activeBlockTargetHeight = startHeight + blockLevelHeight;
		else if (tool == EditorTool.Lower) _activeBlockTargetHeight = startHeight - blockLevelHeight;
		else if (tool == EditorTool.Plateau)
		{
			_activeBlockTargetHeight = (float)MathF.Round(startHeight / TerrainCell.TIER_HEIGHT) * TerrainCell.TIER_HEIGHT;
			_activeBlockTargetWaterMode = GetWaterModeAt(worldPos);
			_activeBlockTargetWaterProfile = GetWaterProfileIndexAt(worldPos);
			_activeBlockTargetWaterHeight = GetWaterHeightAt(worldPos);
		}

		_activeBlockTargetHeight = Math.Clamp(_activeBlockTargetHeight, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
		_hasBlockTargetHeight = true;
	}

	private bool IsPointInBrush(int x, int z, int quadMinX, int quadMinZ, int N, bool brushIsSquare)
	{
		if (brushIsSquare) return true;
		float dx = (x + 0.5f) - (quadMinX + N / 2.0f);
		float dz = (z + 0.5f) - (quadMinZ + N / 2.0f);
		return (dx * dx + dz * dz) <= (N / 2.0f * N / 2.0f);
	}

	private void ProcessBlockCell(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float targetHeight, sbyte targetMacroTier)
	{
		if (ctx.IsHeights) ProcessBlockHeights(ref terrain, ref ctx, x, z, targetHeight, targetMacroTier);
		else if (ctx.ActiveTool == EditorTool.PaintPathing) ProcessBlockPathing(ref terrain, ref ctx, x, z);
	}

	private void ProcessBlockHeights(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float targetHeight, sbyte targetMacroTier)
	{
		if (ctx.ActiveTool == EditorTool.Smooth)
		{
			ApplyBlockSmooth(ref terrain, ref ctx, x, z);
			return;
		}

		ref var cell = ref terrain.Cells![x, z];
		bool waterChanged = UpdateCellWater(ref cell, ctx.ActiveTool);

		if (MathF.Abs(cell.CenterHeight - targetHeight) > 0.001f || waterChanged)
		{
			UpdateBlockCellHeightAndPathing(ref terrain, ref ctx, x, z, targetHeight, targetMacroTier, ref cell);
			ApplyBlockTextures(ref terrain, ref ctx, x, z, targetHeight);
			UpdateContextBounds(ref ctx, x, z);
		}
	}

	private bool UpdateCellWater(ref TerrainCell cell, EditorTool tool)
	{
		if (tool == EditorTool.Raise || tool == EditorTool.Height)
		{
			if (cell.WaterMode == WaterType.None) return false;
			cell.WaterMode = WaterType.None;
			cell.WaterHeight = 0f;
			return true;
		}

		if (tool == EditorTool.Plateau)
		{
			if (cell.WaterMode == _activeBlockTargetWaterMode && cell.WaterProfileIndex == _activeBlockTargetWaterProfile && cell.WaterHeight == _activeBlockTargetWaterHeight) return false;
			cell.WaterMode = _activeBlockTargetWaterMode;
			cell.WaterProfileIndex = _activeBlockTargetWaterProfile;
			cell.WaterHeight = _activeBlockTargetWaterHeight;
			return true;
		}

		return false;
	}

	private void UpdateBlockCellHeightAndPathing(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float targetHeight, sbyte targetMacroTier, ref TerrainCell cell)
	{
		if (terrain.PathingCodes != null)
		{
			if (cell.WaterMode != WaterType.None)
			{
				var waterProf = GameContext.Terrain?.GetWaterProfile(cell.WaterProfileIndex);
				terrain.PathingCodes[x, z] = waterProf != null ? waterProf.DefaultPathingCode : EditableTerrain.GetDefaultPathingCode(cell.WaterMode);
				ctx.PathingModified = true;
				float wY = (targetMacroTier * TerrainCell.TIER_HEIGHT) + (cell.WaterHeight > 0.001f ? cell.WaterHeight : RuntimeTerrain.WATER_DELTA);
				var waterSpawns = BuildWaterProceduralSpawns(x, z, cell.WaterProfileIndex, wY, ctx.QuadSize, ctx.Width, ctx.Depth);
				GameContext.Host?.ApplyProceduralSpawns(x, z, CollectionsMarshal.AsSpan(waterSpawns), ProceduralSpawnLayer.Water);
			}
			else
			{
				GameContext.Host?.ClearProceduralSpawns(x, z, ProceduralSpawnLayer.Water);
				if (terrain.PathingCodes[x, z] == EditableTerrain.GetDefaultPathingCode(cell))
				{
					terrain.PathingCodes[x, z] = EditableTerrain.GetDefaultPathingCode(cell);
					ctx.PathingModified = true;
				}
			}
		}

		SetGridNodeHeight(ref terrain, x, z, targetHeight);
		SetGridNodeHeight(ref terrain, x + 1, z, targetHeight);
		SetGridNodeHeight(ref terrain, x + 1, z + 1, targetHeight);
		SetGridNodeHeight(ref terrain, x, z + 1, targetHeight);
	}

	private void ApplyBlockTextures(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float targetHeight)
	{
		if (ctx.ApplyGroundTexture && _surface.SplatMap != null)
		{
			for (int gz = z; gz <= z + 1; gz++)
			for (int gx = x; gx <= x + 1; gx++)
				if (gx >= 0 && gx < _surface.SplatMap.GetLength(0) && gz >= 0 && gz < _surface.SplatMap.GetLength(1))
					_surface.SplatMap[gx, gz] = TerrainSplatWeights.CreateSolid(ctx.PaintTextureIndex);
			UpdatePathingFromMetadata(ref terrain, ref ctx, x, z);
			var terrainSpawns = BuildTerrainProceduralSpawns(x, z, ctx.PaintTextureIndex, targetHeight, ctx.QuadSize, ctx.Width, ctx.Depth);
			GameContext.Host?.ApplyProceduralSpawns(x, z, CollectionsMarshal.AsSpan(terrainSpawns), ProceduralSpawnLayer.Terrain);
		}

		if (ctx.ApplyCliffTexture && _surface.CliffSplatMap != null)
		{
			for (int nz = z - 1; nz <= z + 2; nz++)
			for (int nx = x - 1; nx <= x + 2; nx++)
				if (nx >= 0 && nx < _surface.CliffSplatMap.GetLength(0) && nz >= 0 && nz < _surface.CliffSplatMap.GetLength(1))
					_surface.CliffSplatMap[nx, nz] = TerrainSplatWeights.CreateSolid(ctx.CliffPaintTextureIndex);
		}
	}

	private void ApplyBlockSmooth(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z)
	{
		int sum = 0, count = 0;
		for (int nz = -1; nz <= 1; nz++)
		{
			for (int nx = -1; nx <= 1; nx++)
			{
				if (x + nx >= 0 && x + nx < ctx.Width && z + nz >= 0 && z + nz < ctx.Depth)
				{
					sum += terrain.Cells![x + nx, z + nz].MacroTier;
					count++;
				}
			}
		}

		sbyte avgMacro = (sbyte)Math.Clamp((int)MathF.Round((float)sum / count), -16, 16);
		float targetH = avgMacro * TerrainCell.TIER_HEIGHT;
		if (MathF.Abs(terrain.Cells![x, z].CenterHeight - targetH) > 0.001f)
		{
			SetGridNodeHeight(ref terrain, x, z, targetH);
			SetGridNodeHeight(ref terrain, x + 1, z, targetH);
			SetGridNodeHeight(ref terrain, x + 1, z + 1, targetH);
			SetGridNodeHeight(ref terrain, x, z + 1, targetH);
			UpdateContextBounds(ref ctx, x, z);
		}
	}

	private void ProcessBlockPaint(ref TerrainState terrain, ref ContinuousEditContext ctx)
	{
		int splatW = _surface.SplatMap!.GetLength(0);
		int splatD = _surface.SplatMap.GetLength(1);
		int intensityLevel = Math.Clamp((int)MathF.Round(ctx.BrushStrength), 0, 10);

		int minPaintX = Math.Clamp((int)Math.Floor((ctx.WorldPos.X - ctx.BrushRadius) / ctx.QuadSize + ctx.Width / 2.0f), 0, splatW - 1);
		int maxPaintX = Math.Clamp((int)Math.Ceiling((ctx.WorldPos.X + ctx.BrushRadius) / ctx.QuadSize + ctx.Width / 2.0f), 0, splatW - 1);
		int minPaintZ = Math.Clamp((int)Math.Floor((ctx.WorldPos.Z - ctx.BrushRadius) / ctx.QuadSize + ctx.Depth / 2.0f), 0, splatD - 1);
		int maxPaintZ = Math.Clamp((int)Math.Ceiling((ctx.WorldPos.Z + ctx.BrushRadius) / ctx.QuadSize + ctx.Depth / 2.0f), 0, splatD - 1);

		for (int pz = minPaintZ; pz <= maxPaintZ; pz++)
		{
			for (int px = minPaintX; px <= maxPaintX; px++)
			{
				if (IsPaintPointInBrush(px, pz, ref ctx) && ShouldPaintCoord(px, pz, ctx.NowMs))
				{
					ApplyPaintToCell(ref terrain, ref ctx, px, pz, intensityLevel);
				}
			}
		}
	}

	private bool IsPaintPointInBrush(int px, int pz, ref ContinuousEditContext ctx)
	{
		float vx = (px - ctx.Width / 2.0f) * ctx.QuadSize;
		float vz = (pz - ctx.Depth / 2.0f) * ctx.QuadSize;
		if (ctx.BrushIsSquare) return Mathf.Abs(vx - ctx.WorldPos.X) <= ctx.BrushRadius && Mathf.Abs(vz - ctx.WorldPos.Z) <= ctx.BrushRadius;
		return new Vector2(vx - ctx.WorldPos.X, vz - ctx.WorldPos.Z).Length() <= ctx.BrushRadius;
	}

	private void ApplyPaintToCell(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, int intensityLevel)
	{
		if (ctx.ApplyGroundTexture)
		{
			_surface.SplatMap![x, z] = TerrainSplatWeights.PaintVertexWeighted(_surface.SplatMap[x, z], ctx.PaintTextureIndex, intensityLevel);
			UpdatePathingFromMetadata(ref terrain, ref ctx, x, z);
			if (terrain.Cells != null && x < ctx.Width && z < ctx.Depth)
			{
				var terrainSpawns = BuildTerrainProceduralSpawns(x, z, ctx.PaintTextureIndex, terrain.Cells[x, z].CenterHeight, ctx.QuadSize, ctx.Width, ctx.Depth);
				GameContext.Host?.ApplyProceduralSpawns(x, z, CollectionsMarshal.AsSpan(terrainSpawns), ProceduralSpawnLayer.Terrain);
			}
		}

		if (ctx.ApplyCliffTexture && _surface.CliffSplatMap != null && x < _surface.CliffSplatMap.GetLength(0) && z < _surface.CliffSplatMap.GetLength(1))
		{
			_surface.CliffSplatMap[x, z] = TerrainSplatWeights.PaintVertexWeighted(_surface.CliffSplatMap[x, z], ctx.CliffPaintTextureIndex, intensityLevel);
		}

		UpdateContextBoundsTight(ref ctx, x, z);
	}

	private void UpdatePathingFromMetadata(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z)
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		if (!string.IsNullOrEmpty(wsPath) && GameContext.MetadataService.TryLoadMetadata(wsPath, out var metaRoot) && metaRoot != null)
		{
			string? swatchName = null;
			var list = GameContext.Host?.GroundTerrain?.LoadedTextureList;
			if (list != null && ctx.PaintTextureIndex >= 0 && ctx.PaintTextureIndex < list.Count)
			{
				swatchName = list[ctx.PaintTextureIndex];
			}

			if (!string.IsNullOrEmpty(swatchName) && terrain.PathingCodes != null && x < ctx.Width && z < ctx.Depth)
			{
				var prof = metaRoot.GetTerrainProfile(swatchName);
				if (prof != null)
				{
					terrain.PathingCodes[x, z] = prof.DefaultPathingCode;
					ctx.PathingModified = true;
				}
			}
		}
	}

	private void ProcessBlockPathing(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z)
	{
		if (ctx.PathingAdd) terrain.PathingCodes![x, z] |= ctx.PathingMask;
		else terrain.PathingCodes![x, z] &= ~ctx.PathingMask;
		UpdateContextBoundsTight(ref ctx, x, z);
	}

	private void ApplyContinuousModeEditing(ref TerrainState terrain, ref ContinuousEditContext ctx)
	{
		int minGX = Math.Clamp((int)Math.Floor((ctx.WorldPos.X - ctx.BrushRadius) / ctx.QuadSize + ctx.Width / 2.0f), 0, ctx.Width);
		int maxGX = Math.Clamp((int)Math.Ceiling((ctx.WorldPos.X + ctx.BrushRadius) / ctx.QuadSize + ctx.Width / 2.0f), 0, ctx.Width);
		int minGZ = Math.Clamp((int)Math.Floor((ctx.WorldPos.Z - ctx.BrushRadius) / ctx.QuadSize + ctx.Depth / 2.0f), 0, ctx.Depth);
		int maxGZ = Math.Clamp((int)Math.Ceiling((ctx.WorldPos.Z + ctx.BrushRadius) / ctx.QuadSize + ctx.Depth / 2.0f), 0, ctx.Depth);

		for (int z = minGZ; z <= maxGZ; z++)
		{
			for (int x = minGX; x <= maxGX; x++)
			{
				float dist = CalculateBrushDistance(x, z, ref ctx);
				if (dist <= ctx.BrushRadius)
				{
					ProcessContinuousCell(ref terrain, ref ctx, x, z, dist);
				}
			}
		}
	}

	private float CalculateBrushDistance(int x, int z, ref ContinuousEditContext ctx)
	{
		float vx = (x - ctx.Width / 2.0f) * ctx.QuadSize;
		float vz = (z - ctx.Depth / 2.0f) * ctx.QuadSize;
		if (ctx.BrushIsSquare) return Mathf.Max(Mathf.Abs(vx - ctx.WorldPos.X), Mathf.Abs(vz - ctx.WorldPos.Z));
		return new Vector2(vx - ctx.WorldPos.X, vz - ctx.WorldPos.Z).Length();
	}

	private void ProcessContinuousCell(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float dist)
	{
		if (ctx.IsHeights)
		{
			float falloff = Mathf.Sin((1.0f - (dist / ctx.BrushRadius)) * Mathf.Pi / 2.0f);
			ProcessContinuousHeight(ref terrain, ref ctx, x, z, falloff, dist);
		}
		else if (ctx.ActiveTool == EditorTool.PaintTexture)
		{
			int splatW = _surface.SplatMap!.GetLength(0);
			int splatD = _surface.SplatMap.GetLength(1);
			if (x >= 0 && x < splatW && z >= 0 && z < splatD && ShouldPaintCoord(x, z, ctx.NowMs))
			{
				int intensityLevel = Math.Clamp((int)MathF.Round(ctx.BrushStrength), 0, 10);
				ApplyPaintToCell(ref terrain, ref ctx, x, z, intensityLevel);
			}
		}
		else if (ctx.ActiveTool == EditorTool.PaintPathing && x < ctx.Width && z < ctx.Depth)
		{
			float vx = (x - ctx.Width / 2.0f) * ctx.QuadSize;
			float vz = (z - ctx.Depth / 2.0f) * ctx.QuadSize;
			int icx = Mathf.Clamp((int)Math.Round(vx / ctx.QuadSize + ctx.Width / 2.0f), 0, ctx.Width - 1);
			int icz = Mathf.Clamp((int)Math.Round(vz / ctx.QuadSize + ctx.Depth / 2.0f), 0, ctx.Depth - 1);
			ProcessBlockPathing(ref terrain, ref ctx, icx, icz);
		}
	}

	private void ProcessContinuousHeight(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float falloff, float dist)
	{
		float oldH = GetGridNodeHeight(in terrain, x, z);
		float newH = CalculateNewContinuousHeight(in terrain, ref ctx, x, z, oldH, falloff, dist);

		if (Mathf.Abs(newH - oldH) > 0.001f)
		{
			if (ctx.ActiveTool == EditorTool.Plateau && terrain.PathingCodes != null)
			{
				int cellX = Math.Clamp(x, 0, ctx.Width - 1);
				int cellZ = Math.Clamp(z, 0, ctx.Depth - 1);
				int defaultPathBefore = EditableTerrain.GetDefaultPathingCode(terrain.Cells![cellX, cellZ]);
				if (terrain.PathingCodes[cellX, cellZ] == defaultPathBefore)
				{
					terrain.PathingCodes[cellX, cellZ] = EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
					ctx.PathingModified = true;
				}
			}

			SetGridNodeHeight(ref terrain, x, z, newH);
			if (ctx.ActiveTool == EditorTool.Raise || ctx.ActiveTool == EditorTool.Lower || ctx.ActiveTool == EditorTool.Plateau)
			{
				ApplyContinuousCliffSplat(ref ctx, x, z);
			}

			UpdateContextBoundsTight(ref ctx, x, z);
		}
	}

	private float CalculateNewContinuousHeight(in TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float oldH, float falloff, float dist)
	{
		if (ctx.ActiveTool == EditorTool.Raise) return Mathf.Clamp(oldH + ctx.BrushStrength * falloff * ctx.Delta, -10.0f, 50.0f);
		if (ctx.ActiveTool == EditorTool.Lower) return Mathf.Clamp(oldH - ctx.BrushStrength * falloff * ctx.Delta, -10.0f, 50.0f);
		if (ctx.ActiveTool == EditorTool.Plateau) return CalculatePlateauHeight(in terrain, ref ctx, x, z, oldH, falloff);
		if (ctx.ActiveTool == EditorTool.Smooth) return CalculateSmoothHeight(in terrain, ref ctx, x, z, oldH, falloff);
		if (ctx.ActiveTool == EditorTool.Noise) return CalculateNoiseHeight(oldH, x, z, dist, ref ctx);
		return oldH;
	}

	private float CalculatePlateauHeight(in TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float oldH, float falloff)
	{
		if (!_activePlateauHeight.HasValue)
		{
			_activePlateauHeight = GetTerrainHeightAt(ctx.WorldPos);
			_activePlateauWaterMode = GetWaterModeAt(ctx.WorldPos);
			_activePlateauWaterProfile = GetWaterProfileIndexAt(ctx.WorldPos);
			_activePlateauWaterHeight = GetWaterHeightAt(ctx.WorldPos);
		}

		float newH = Mathf.Clamp(Mathf.Lerp(oldH, _activePlateauHeight.Value, falloff), -10.0f, 50.0f);

		int cellX = Math.Clamp(x, 0, ctx.Width - 1);
		int cellZ = Math.Clamp(z, 0, ctx.Depth - 1);
		if (terrain.Cells != null && (terrain.Cells[cellX, cellZ].WaterMode != _activePlateauWaterMode || terrain.Cells[cellX, cellZ].WaterProfileIndex != _activePlateauWaterProfile || terrain.Cells[cellX, cellZ].WaterHeight != _activePlateauWaterHeight))
		{
			terrain.Cells[cellX, cellZ].WaterMode = _activePlateauWaterMode;
			terrain.Cells[cellX, cellZ].WaterProfileIndex = _activePlateauWaterProfile;
			terrain.Cells[cellX, cellZ].WaterHeight = _activePlateauWaterHeight;
			if (terrain.PathingCodes != null)
			{
				var waterProf = GameContext.Terrain?.GetWaterProfile(terrain.Cells[cellX, cellZ].WaterProfileIndex);
				terrain.PathingCodes[cellX, cellZ] = waterProf != null ? waterProf.DefaultPathingCode : EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
				ctx.PathingModified = true;
			}
		}

		return newH;
	}

	private float CalculateSmoothHeight(in TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float oldH, float falloff)
	{
		float avg = 0f;
		int count = 0;
		for (int nz = -1; nz <= 1; nz++)
		for (int nx = -1; nx <= 1; nx++)
			if (x + nx >= 0 && x + nx <= ctx.Width && z + nz >= 0 && z + nz <= ctx.Depth)
			{
				avg += GetGridNodeHeight(in terrain, x + nx, z + nz);
				count++;
			}

		avg /= count;
		float effectiveStrength = ctx.BrushStrength * 15.0f + 5.0f;
		return Mathf.Clamp(Mathf.Lerp(oldH, avg, Mathf.Clamp(effectiveStrength * falloff * ctx.Delta, 0.0f, 1.0f)), -10.0f, 50.0f);
	}

	private float CalculateNoiseHeight(float oldH, int x, int z, float dist, ref ContinuousEditContext ctx)
	{
		float dRatio = Mathf.Clamp(dist / ctx.BrushRadius, 0.0f, 1.0f);
		float falloffWeight = 1.0f - (dRatio * dRatio * (3.0f - 2.0f * dRatio));
		float noiseValue = (SimplexNoise.Simplex2D(x * 0.05f, z * 0.05f) * 1.0f + SimplexNoise.Simplex2D(x * 0.2f, z * 0.2f) * 0.35f) / 1.35f;
		return Mathf.Clamp(oldH + noiseValue * (ctx.BrushStrength * 0.25f + 8.25f) * falloffWeight * ctx.Delta * 0.5f, -10.0f, 50.0f);
	}

	private void ApplyContinuousCliffSplat(ref ContinuousEditContext ctx, int x, int z)
	{
		if (_surface.CliffSplatMap == null) return;
		for (int cz = z - 1; cz <= z; cz++)
		{
			for (int cx = x - 1; cx <= x; cx++)
			{
				if (cx >= 0 && cx < ctx.Width && cz >= 0 && cz < ctx.Depth) ApplyCliffSplatToNeighbors(ref ctx, cx, cz);
			}
		}
	}

	private void ApplyCliffSplatToNeighbors(ref ContinuousEditContext ctx, int cx, int cz)
	{
		for (int nz = cz - 1; nz <= cz + 1; nz++)
		{
			for (int nx = cx - 1; nx <= cx + 1; nx++)
			{
				if (nx >= 0 && nx < ctx.Width && nz >= 0 && nz < ctx.Depth) _surface.CliffSplatMap![nx, nz] = TerrainSplatWeights.CreateSolid(ctx.CliffPaintTextureIndex);
			}
		}
	}

	private void UpdateContextBounds(ref ContinuousEditContext ctx, int x, int z)
	{
		int minX = Math.Max(0, x - 1);
		int maxX = Math.Min(ctx.Width - 1, x + 1);
		int minZ = Math.Max(0, z - 1);
		int maxZ = Math.Min(ctx.Depth - 1, z + 1);
		if (minX < ctx.ModMinX) ctx.ModMinX = minX;
		if (maxX > ctx.ModMaxX) ctx.ModMaxX = maxX;
		if (minZ < ctx.ModMinZ) ctx.ModMinZ = minZ;
		if (maxZ > ctx.ModMaxZ) ctx.ModMaxZ = maxZ;
		ctx.Modified = true;
	}

	private void UpdateContextBoundsTight(ref ContinuousEditContext ctx, int x, int z)
	{
		if (x < ctx.ModMinX) ctx.ModMinX = x;
		if (x > ctx.ModMaxX) ctx.ModMaxX = x;
		if (z < ctx.ModMinZ) ctx.ModMinZ = z;
		if (z > ctx.ModMaxZ) ctx.ModMaxZ = z;
		ctx.Modified = true;
	}

	private TerrainEditResult FinalizeTerrainEditResult(ContinuousEditContext ctx, bool isHeights, bool isPaint, bool isPathing)
	{
		var result = new TerrainEditResult { PathingModified = ctx.PathingModified };
		if (!ctx.Modified) return result;

		result.HeightsModified = isHeights;
		result.SplatModified = isPaint || (isHeights && ctx.ActiveTool != EditorTool.Smooth && ctx.ActiveTool != EditorTool.Noise);
		result.PathingModified = result.PathingModified || isPathing;

		if (ctx.ModMinX < _drawMinX) _drawMinX = ctx.ModMinX;
		if (ctx.ModMaxX > _drawMaxX) _drawMaxX = ctx.ModMaxX;
		if (ctx.ModMinZ < _drawMinZ) _drawMinZ = ctx.ModMinZ;
		if (ctx.ModMaxZ > _drawMaxZ) _drawMaxZ = ctx.ModMaxZ;

		result.MinX = ctx.ModMinX;
		result.MinZ = ctx.ModMinZ;
		result.MaxX = ctx.ModMaxX;
		result.MaxZ = ctx.ModMaxZ;
		if (result.SplatModified) AlignSplatMapSlots(ctx.ModMinX - 2, ctx.ModMinZ - 2, ctx.ModMaxX + 2, ctx.ModMaxZ + 2);

		return result;
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

		if (activeTool == EditorTool.Plateau)
		{
			_activePlateauHeight = blockMode ? (float)MathF.Round(startHeight / TerrainCell.TIER_HEIGHT) * TerrainCell.TIER_HEIGHT : startHeight;
			_activePlateauWaterMode = GetWaterModeAt(hitPos);
			_activePlateauWaterProfile = GetWaterProfileIndexAt(hitPos);
			_activePlateauWaterHeight = GetWaterHeightAt(hitPos);
		}

		if (blockMode || activeTool == EditorTool.Height)
		{
			SetBlockTargetHeight(activeTool, startHeight, blockLevelHeight, hitPos);
		}
	}

	private void SetBlockTargetHeight(EditorTool activeTool, float startHeight, float blockLevelHeight, Godot.Vector3 hitPos)
	{
		if (activeTool == EditorTool.Height)
		{
			_activeBlockTargetHeight = Math.Clamp(blockLevelHeight, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
			_hasBlockTargetHeight = true;
		}
		else if (activeTool == EditorTool.Raise)
		{
			_activeBlockTargetHeight = Math.Clamp(startHeight + blockLevelHeight, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
			_hasBlockTargetHeight = true;
		}
		else if (activeTool == EditorTool.Lower)
		{
			_activeBlockTargetHeight = Math.Clamp(startHeight - blockLevelHeight, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
			_hasBlockTargetHeight = true;
		}
		else if (activeTool == EditorTool.Plateau)
		{
			_activeBlockTargetHeight = Math.Clamp((float)MathF.Round(startHeight / TerrainCell.TIER_HEIGHT) * TerrainCell.TIER_HEIGHT, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
			_activeBlockTargetWaterMode = GetWaterModeAt(hitPos);
			_activeBlockTargetWaterProfile = GetWaterProfileIndexAt(hitPos);
			_activeBlockTargetWaterHeight = GetWaterHeightAt(hitPos);
			_hasBlockTargetHeight = true;
		}
	}

	public TerrainModifyAction EndTerrainDraw(
		float[,]? currentHeights = null,
		TerrainSplatWeights[,]? currentSplatMap = null,
		int[,]? currentPathing = null,
		TerrainSplatWeights[,]? currentCliffSplatMap = null)
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

			TerrainCell[,]? beforeC = null;
			TerrainCell[,]? afterC = null;
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

			int[,]? beforeP = null;
			int[,]? afterP = null;
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

			TerrainSplatWeights[,]? beforeS = null;
			TerrainSplatWeights[,]? afterS = null;
			TerrainSplatWeights[,]? beforeCliffS = null;
			TerrainSplatWeights[,]? afterCliffS = null;
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
		ClearObjectMarquee();
		_copiedArea = null;
		_hasBlockTargetHeight = false;
		_activeBlockTargetHeight = 0.0f;
		_activePlateauHeight = null;
		_activePlateauWaterMode = WaterType.None;
		_activeBlockTargetWaterMode = WaterType.None;
		_surface.SplatMap = null;
		_surface.CliffSplatMap = null;
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

	public CompositeAction? EndClumpSession()
	{
		_isDrawingClump = false;
		if (_clumpSpawnActionsInSession.Count > 0)
		{
			return new CompositeAction(_clumpSpawnActionsInSession);
		}

		return null;
	}

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

				Godot.Vector3 spawnPos = new Godot.Vector3(centerPos.X + dx, centerPos.Y, centerPos.Z + dz);
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
				string spawnType = activeTool == EditorTool.PlaceUnit ? "unit"
					: activeTool == EditorTool.PlaceProp ? "prop"
					: "decal";
				int playerIndex = activeTool == EditorTool.PlaceUnit ? placeUnitPlayerIndex : 0;

				requests.Add(new EntitySpawnRequest
				{
					Type = spawnType,
					Id = activePlaceId,
					Position = spawnPos,
					Rotation = rotY,
					Scale = scaleVal,
					Player = playerIndex
				});

				if (mirrorMode != MirrorMode.None)
				{
					AddMirroredRequests(requests, spawnType, activePlaceId, spawnPos, rotY, scaleVal, playerIndex, mirrorMode);
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

	private void ProcessRampCell(Godot.Vector3 start, Godot.Vector3 end, float brushRadius, float segmentLengthSquared, int width, int depth, float quadSize, int gridX, int gridZ, ref TerrainState terrain, ref bool modified, HashSet<Vector2I> modifiedCells)
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
		if (terrain.PathingCodes != null && terrain.Cells != null)
		{
			int defaultPathBefore = EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
			if (terrain.PathingCodes[cellX, cellZ] == defaultPathBefore)
			{
				terrain.PathingCodes[cellX, cellZ] = EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
			}
		}

		SetGridNodeHeight(ref terrain, gridX, gridZ, newHeight);
		int rampPaintIdx = GameContext.Host != null ? GameContext.EditorService.EditorPaintTextureIndex : 0;
		int rampCliffIdx = GameContext.Host != null ? GameContext.EditorService.EditorCliffPaintTextureIndex : 1;
		SetGridNodeSplat(gridX, gridZ, new TerrainSplatWeights { Index0 = rampPaintIdx, Weight0 = 1.0f }, new TerrainSplatWeights { Index0 = rampPaintIdx, Weight0 = 1.0f }, new TerrainSplatWeights { Index0 = rampPaintIdx, Weight0 = 1.0f }, new TerrainSplatWeights { Index0 = rampPaintIdx, Weight0 = 1.0f });
		SetGridNodeCliffSplat(gridX, gridZ, new TerrainSplatWeights { Index0 = rampCliffIdx, Weight0 = 1.0f }, new TerrainSplatWeights { Index0 = rampCliffIdx, Weight0 = 1.0f }, new TerrainSplatWeights { Index0 = rampCliffIdx, Weight0 = 1.0f }, new TerrainSplatWeights { Index0 = rampCliffIdx, Weight0 = 1.0f });
		modified = true;
		modifiedCells.Add(new Vector2I(gridX, gridZ));
	}

	public bool ApplyRamp(Godot.Vector3 start, Godot.Vector3 end, float brushRadius, bool blockMode, float blockLevelHeight)
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
				ProcessRampCell(start, end, brushRadius, segmentLengthSquared, width, depth, quadSize, gridX, gridZ, ref terrain, ref modified, modifiedCells);
			}
		}

		if (modified)
		{
			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
			TerrainSwatchProfileData? rampProf = null;
			int rampTexIdx = GameContext.Host != null ? GameContext.EditorService.EditorPaintTextureIndex : 0;
			if (!string.IsNullOrEmpty(wsPath) && GameContext.MetadataService.TryLoadMetadata(wsPath, out var metaRoot) && metaRoot != null)
			{
				string? swatchName = null;
				if (GameContext.Host?.GroundTerrain != null && rampTexIdx >= 0 && rampTexIdx < GameContext.Host.GroundTerrain.LoadedTextureList.Count)
				{
					swatchName = GameContext.Host.GroundTerrain.LoadedTextureList[rampTexIdx];
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
					var terrainSpawns = BuildTerrainProceduralSpawns(cellPos.X, cellPos.Y, rampTexIdx, tY, quadSize, width, depth);
					GameContext.Host?.ApplyProceduralSpawns(cellPos.X, cellPos.Y, CollectionsMarshal.AsSpan(terrainSpawns), ProceduralSpawnLayer.Terrain);
				}
			}

			AlignSplatMapSlots(minGridX - 2, minGridZ - 2, maxGridX + 2, maxGridZ + 2);
		}

		return modified;
	}

	public float GetMinHeightInBrushBounds(Godot.Vector3 worldPos, float brushRadius, bool brushIsSquare)
	{
		float result = GetMinHeightInBrushBoundsInternal(worldPos, brushRadius, brushIsSquare);
		return result;
	}

	public void CopyArea(int minX, int minZ, int maxX, int maxZ, List<CopiedEntityInfo> entities, bool isSquare = true)
	{
		ref var terrain = ref GetTerrainState();
		var cells = terrain.Cells;
		if (cells == null) return;

		if (_surface.SplatMap == null && GameContext.Host?.GroundTerrain != null)
		{
			_surface.SplatMap = GameContext.Host.GroundTerrain.SplatMap;
		}

		if (_surface.CliffSplatMap == null && GameContext.Host?.GroundTerrain != null)
		{
			_surface.CliffSplatMap = GameContext.Host.GroundTerrain.CliffSplatMap;
		}

		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
		var copiedCells = new TerrainCell[selWidth, selDepth];
		var splatMap = _surface.SplatMap != null ? new TerrainSplatWeights[selWidth + 1, selDepth + 1] : null;
		var cliffSplatMap = _surface.CliffSplatMap != null ? new TerrainSplatWeights[selWidth + 1, selDepth + 1] : null;
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

		if (_surface.SplatMap != null && splatMap != null)
		{
			int mapW = _surface.SplatMap.GetLength(0);
			int mapD = _surface.SplatMap.GetLength(1);
			for (int vz = 0; vz <= selDepth; vz++)
			{
				for (int vx = 0; vx <= selWidth; vx++)
				{
					int sourceVx = Math.Clamp(minX + vx, 0, mapW - 1);
					int sourceVz = Math.Clamp(minZ + vz, 0, mapD - 1);
					splatMap[vx, vz] = _surface.SplatMap[sourceVx, sourceVz];
				}
			}
		}

		if (_surface.CliffSplatMap != null && cliffSplatMap != null)
		{
			int mapCW = _surface.CliffSplatMap.GetLength(0);
			int mapCD = _surface.CliffSplatMap.GetLength(1);
			for (int vz = 0; vz <= selDepth; vz++)
			{
				for (int vx = 0; vx <= selWidth; vx++)
				{
					int sourceVx = Math.Clamp(minX + vx, 0, mapCW - 1);
					int sourceVz = Math.Clamp(minZ + vz, 0, mapCD - 1);
					cliffSplatMap[vx, vz] = _surface.CliffSplatMap[sourceVx, sourceVz];
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
		IReadOnlyList<CopiedEntityCandidate> candidates,
		bool isSquare = true)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return new List<CopiedEntityInfo>();

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		EditorTerrainSurface.CalculateWorldBounds(minX, minZ, maxX, maxZ, width, depth, quadSize, out float minWorldX, out float maxWorldX, out float minWorldZ, out float maxWorldZ, out float centerWorldX, out float centerWorldZ, out float rxWorld, out float rzWorld);
		Godot.Vector3 origin = new Godot.Vector3((minX - width / 2.0f) * quadSize, 0.0f, (minZ - depth / 2.0f) * quadSize);

		var entities = new List<CopiedEntityInfo>();
		for (int index = 0; index < candidates.Count; index++)
		{
			CopiedEntityCandidate candidate = candidates[index];
			Godot.Vector3 pos = candidate.Position;
			if (pos.X < minWorldX || pos.X > maxWorldX || pos.Z < minWorldZ || pos.Z > maxWorldZ) continue;

			if (!isSquare)
			{
				float ndx = (pos.X - centerWorldX) / rxWorld;
				float ndz = (pos.Z - centerWorldZ) / rzWorld;
				if (ndx * ndx + ndz * ndz > 1.0f) continue;
			}

			entities.Add(new CopiedEntityInfo
			{
				Type = candidate.Type,
				Id = candidate.Id,
				RelativePos = pos - origin,
				Rotation = candidate.RotationY,
				Scale = candidate.Scale,
				Player = candidate.Player
			});
		}

		if (GameContext.World != null)
		{
			var propQuery = QueryCache.AllPropIdentityAndPositionQuery;
			var world = GameContext.World;
			world.Query(in propQuery, (Entity entity, ref PropIdentity propIdComp, ref Position posComp) =>
			{
				if (GameHost.EntityToProp3D.ContainsKey(entity)) return;

				Godot.Vector3 worldPos = new Godot.Vector3(posComp.Value.X, posComp.Value.Y, posComp.Value.Z);
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
						Player = 0
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
		var copiedArea = _copiedArea;

		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return result;

		if (_surface.SplatMap == null && GameContext.Host?.GroundTerrain != null)
		{
			_surface.SplatMap = GameContext.Host.GroundTerrain.SplatMap;
		}

		if (_surface.CliffSplatMap == null && GameContext.Host?.GroundTerrain != null)
		{
			_surface.CliffSplatMap = GameContext.Host.GroundTerrain.CliffSplatMap;
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

		PasteBlock(copiedArea, startX, startZ, rotSteps, pasteReflection, MirrorMode.None, 0, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);

		if (mirrorMode != MirrorMode.None)
		{
			Godot.Vector3 centerPos = new Godot.Vector3((startX + targetWidth / 2.0f - width / 2.0f) * quadSize, 0, (startZ + targetDepth / 2.0f - depth / 2.0f) * quadSize);
			var transforms = GetMirroredTransforms(centerPos, 0.0f, mirrorMode);
			if (mirrorMode == MirrorMode.Horizontal)
			{
				if (transforms.Count > 0)
				{
					var (rcx, rcz) = WorldPosToCellCoords(transforms[0].Position);
					int rStartX = rcx - targetWidth / 2;
					int rStartZ = rcz - targetDepth / 2;
					PasteBlock(copiedArea, rStartX, rStartZ, rotSteps, pasteReflection, MirrorMode.Horizontal, 0, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);
				}
			}
			else if (mirrorMode == MirrorMode.Vertical)
			{
				if (transforms.Count > 0)
				{
					var (rcx, rcz) = WorldPosToCellCoords(transforms[0].Position);
					int rStartX = rcx - targetWidth / 2;
					int rStartZ = rcz - targetDepth / 2;
					PasteBlock(copiedArea, rStartX, rStartZ, rotSteps, pasteReflection, MirrorMode.Vertical, 0, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);
				}
			}
			else if (mirrorMode == MirrorMode.Both)
			{
				if (transforms.Count >= 3)
				{
					var (rcx0, rcz0) = WorldPosToCellCoords(transforms[0].Position);
					int rStartX0 = rcx0 - targetWidth / 2;
					int rStartZ0 = rcz0 - targetDepth / 2;
					PasteBlock(copiedArea, rStartX0, rStartZ0, rotSteps, pasteReflection, MirrorMode.Horizontal, 0, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);

					var (rcx1, rcz1) = WorldPosToCellCoords(transforms[1].Position);
					int rStartX1 = rcx1 - targetWidth / 2;
					int rStartZ1 = rcz1 - targetDepth / 2;
					PasteBlock(copiedArea, rStartX1, rStartZ1, rotSteps, pasteReflection, MirrorMode.Vertical, 0, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);

					var (rcx2, rcz2) = WorldPosToCellCoords(transforms[2].Position);
					int rStartX2 = rcx2 - targetWidth / 2;
					int rStartZ2 = rcz2 - targetDepth / 2;
					PasteBlock(copiedArea, rStartX2, rStartZ2, rotSteps, pasteReflection, MirrorMode.Both, 0, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);
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
					PasteBlock(copiedArea, rStartX, rStartZ, rotSteps, pasteReflection, MirrorMode.None, rotKSteps, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);
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
			Godot.Vector3 pasteCenter = new Godot.Vector3((startX + (targetWidth - 1) / 2.0f - width / 2.0f) * quadSize, 0, (startZ + (targetDepth - 1) / 2.0f - depth / 2.0f) * quadSize);

			float rad = rotationDegrees * Mathf.Pi / 180.0f;
			float cosR = Mathf.Cos(rad);
			float sinR = Mathf.Sin(rad);

			Godot.Vector3 originalCenterOffset = new Godot.Vector3((pasteWidth - 1) / 2.0f * quadSize, 0, (pasteDepth - 1) / 2.0f * quadSize);

			foreach (var ent in _copiedArea.Entities)
			{
				Godot.Vector3 relPos = ent.RelativePos;
				float entRot = ent.Rotation;

				if (pasteReflection == PasteReflection.Horizontal)
				{
					relPos = new Godot.Vector3((pasteWidth - 1) * quadSize - relPos.X, relPos.Y, relPos.Z);
					entRot = -entRot;
				}
				else if (pasteReflection == PasteReflection.Vertical)
				{
					relPos = new Godot.Vector3(relPos.X, relPos.Y, (pasteDepth - 1) * quadSize - relPos.Z);
					entRot = 180.0f - entRot;
				}

				Godot.Vector3 relativeToCenter = relPos - originalCenterOffset;

				float rx = relativeToCenter.X * cosR - relativeToCenter.Z * sinR;
				float rz = relativeToCenter.X * sinR + relativeToCenter.Z * cosR;

				Godot.Vector3 rotatedRelative = new Godot.Vector3(rx, 0, rz);
				Godot.Vector3 destPos = pasteCenter + rotatedRelative;

				destPos.Y = GetTerrainHeightAt(destPos);

				float finalRot = entRot - rotationDegrees;

				result.SpawnRequests.Add(new EntitySpawnRequest
				{
					Type = ent.Type,
					Id = ent.Id,
					Position = destPos,
					Rotation = finalRot,
					Scale = ent.Scale,
					Player = ent.Player
				});

				AddMirroredRequests(result.SpawnRequests, ent.Type, ent.Id, destPos, finalRot, ent.Scale, ent.Player, mirrorMode);
			}
		}

		return result;
	}

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
		bool isCliff = false)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return (null, null, isCliff);

		if (_surface.SplatMap == null && GameContext.Host?.GroundTerrain != null)
		{
			_surface.SplatMap = GameContext.Host.GroundTerrain.SplatMap;
		}

		if (_surface.CliffSplatMap == null && GameContext.Host?.GroundTerrain != null)
		{
			_surface.CliffSplatMap = GameContext.Host.GroundTerrain.CliffSplatMap;
		}

		if (_surface.SplatMap == null) return (null, null, isCliff);

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		TerrainSplatWeights[,] targetSplatMap = (isCliff && _surface.CliffSplatMap != null) ? _surface.CliffSplatMap : _surface.SplatMap;
		int targetTextureIndex = isCliff ? cliffTextureIndex : fillTextureIndex;

		int splatW = targetSplatMap.GetLength(0);
		int splatD = targetSplatMap.GetLength(1);

		var splatBefore = (TerrainSplatWeights[,])targetSplatMap.Clone();
		var visited = new bool[splatW, splatD];

		var cells = EditorTerrainSurface.GetFloodFillArea(
			clickPos,
			splatBefore,
			width,
			depth,
			quadSize,
			visited,
			mirrorMode,
			isCliff,
			(x, z) => !splatBefore[x, z].IsSolid(targetTextureIndex),
			_symmetryPivot,
			_symmetryFolds
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

	public (int[,]? Before, int[,]? After) PerformFloodFillPathing(Godot.Vector3 clickPos, int pathingMask, bool pathingAdd, MirrorMode mirrorMode)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null || terrain.PathingCodes == null) return (null, null);

		if (_surface.SplatMap == null && GameContext.Host?.GroundTerrain != null)
		{
			_surface.SplatMap = GameContext.Host.GroundTerrain.SplatMap;
		}

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;
		var splatBefore = _surface.SplatMap != null ? (TerrainSplatWeights[,])_surface.SplatMap.Clone() : null;
		var pathingBefore = (int[,])terrain.PathingCodes.Clone();
		var visited = new bool[width, depth];

		var pathingCodes = terrain.PathingCodes;

		var cells = EditorTerrainSurface.GetFloodFillPathingArea(
			clickPos,
			splatBefore,
			width,
			depth,
			quadSize,
			visited,
			mirrorMode,
			(x, z) => EditorTerrainSurface.CheckPathingCondition(pathingBefore, x, z, pathingMask, pathingAdd),
			_symmetryPivot,
			_symmetryFolds
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

	public (TerrainCell[,]? BeforeCells, TerrainCell[,]? AfterCells, int[,]? BeforePathing, int[,]? AfterPathing, bool WasAdded) PerformWaterFloodFill(
		Godot.Vector3 clickPos,
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

		var beforeCells = (TerrainCell[,])cells.Clone();
		var beforePathing = (int[,])terrain.PathingCodes.Clone();

		var visited = new bool[width, depth];
		int clickX = Mathf.Clamp((int)Math.Floor(clickPos.X / quadSize + width / 2.0f), 0, width - 1);
		int clickZ = Mathf.Clamp((int)Math.Floor(clickPos.Z / quadSize + depth / 2.0f), 0, depth - 1);
		float waterSurfaceY = EditorTerrainSurface.CalculateBaseWaterLevel(clickPos, beforeCells[clickX, clickZ], waterHeight);
		var filledCells = EditorTerrainSurface.GetWaterFloodFillArea(clickPos, beforeCells, width, depth, quadSize, visited, mirrorMode, waterSurfaceY, isRemoveAction, _symmetryPivot, _symmetryFolds);

		if (filledCells.Count == 0) return (null, null, null, null, false);

		EditorTerrainSurface.ApplyWaterPool(cells, terrain.PathingCodes, filledCells, targetWaterMode, targetWaterProfile, waterSurfaceY, quadSize, width, depth, isRemoving);

		var afterCells = (TerrainCell[,])cells.Clone();
		var afterPathing = (int[,])terrain.PathingCodes.Clone();

		return (beforeCells, afterCells, beforePathing, afterPathing, !isRemoving);
	}

	public bool FillWaterPoolAtCell(int cellX, int cellZ, float waterSurfaceY)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null || terrain.PathingCodes == null) return false;

		int width = terrain.Width;
		int depth = terrain.Depth;
		if (cellX < 0 || cellX >= width || cellZ < 0 || cellZ >= depth) return false;
		if (terrain.Cells[cellX, cellZ].WaterMode != WaterType.None) return false;

		byte waterProfileIndex = GameContext.Host?.ActiveWaterProfileIndex ?? (byte)0;
		bool[,] visited = _surface.AcquireWaterPoolVisited(width, depth);
		var filledCells = EditorTerrainSurface.CollectWaterPoolCells(cellX, cellZ, terrain.Cells, width, depth, visited, waterSurfaceY, false, new List<Vector2I>());
		if (filledCells.Count == 0) return false;

		EditorTerrainSurface.ApplyWaterPool(terrain.Cells, terrain.PathingCodes, filledCells, null, waterProfileIndex, waterSurfaceY, terrain.QuadSize, width, depth, false);
		return true;
	}

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
		return EditorGridSnapping.GetAnchoredPasteBounds(_copiedArea, targetX, targetZ, rotationDegrees, reflection);
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

	private float GetMinHeightInBrushBoundsInternal(Godot.Vector3 worldPos, float brushRadius = -1f, bool brushIsSquare = false)
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
		Godot.Vector3 pos, float rotation, float scale,
		int player,
		MirrorMode mirrorMode)
	{
		var mirrored = GetMirroredTransforms(pos, rotation, mirrorMode);
		foreach (var m in mirrored)
		{
			Godot.Vector3 mPos = m.Position;
			mPos.Y = GetTerrainHeightAt(mPos);
			requests.Add(new EntitySpawnRequest { Type = type, Id = id, Position = mPos, Rotation = m.Rotation, Scale = scale, Player = player });
		}
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

	private void ProcessPasteBlockCell(
		CopiedAreaTemplate copiedArea,
		int sx, int sz, int pasteWidth, int pasteDepth, int targetWidth, int targetDepth,
		int blockStartX, int blockStartZ,
		int rotSteps, PasteReflection pasteReflection, MirrorMode symmetryMirror, int foldRotSteps,
		int width, int depth, bool pasteHeights, bool pasteTextures, bool pastePathing,
		TerrainCell[,]? srcCells, TerrainCell[,]? cells,
		ref TerrainState terrain, ref bool modified, ref bool pathingModified)
	{
		int srcX = pasteReflection == PasteReflection.Horizontal ? pasteWidth - 1 - sx : sx;
		int srcZ = pasteReflection == PasteReflection.Vertical ? pasteDepth - 1 - sz : sz;

		if (copiedArea.Mask != null && !copiedArea.Mask[srcX, srcZ]) return;

		int rotX = sx;
		int rotZ = sz;
		if (rotSteps == 1)
		{
			rotX = pasteDepth - 1 - sz;
			rotZ = sx;
		}
		else if (rotSteps == 2)
		{
			rotX = pasteWidth - 1 - sx;
			rotZ = pasteDepth - 1 - sz;
		}
		else if (rotSteps == 3)
		{
			rotX = sz;
			rotZ = pasteWidth - 1 - sx;
		}

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
			if (copiedArea.SplatMap != null && srcX + 1 < copiedArea.SplatMap.GetLength(0) && srcZ + 1 < copiedArea.SplatMap.GetLength(1))
			{
				sNW = copiedArea.SplatMap[srcX, srcZ];
				sNE = copiedArea.SplatMap[srcX + 1, srcZ];
				sSE = copiedArea.SplatMap[srcX + 1, srcZ + 1];
				sSW = copiedArea.SplatMap[srcX, srcZ + 1];
				if (pasteReflection == PasteReflection.Horizontal) (sNW, sNE, sSE, sSW) = MirrorSplatQuad(in sNW, in sNE, in sSE, in sSW, MirrorMode.Horizontal);
				else if (pasteReflection == PasteReflection.Vertical) (sNW, sNE, sSE, sSW) = MirrorSplatQuad(in sNW, in sNE, in sSE, in sSW, MirrorMode.Vertical);
				(sNW, sNE, sSE, sSW) = RotateSplatQuad(in sNW, in sNE, in sSE, in sSW, rotSteps);
			}

			if (copiedArea.CliffSplatMap != null && srcX + 1 < copiedArea.CliffSplatMap.GetLength(0) && srcZ + 1 < copiedArea.CliffSplatMap.GetLength(1))
			{
				cS_NW = copiedArea.CliffSplatMap[srcX, srcZ];
				cS_NE = copiedArea.CliffSplatMap[srcX + 1, srcZ];
				cS_SE = copiedArea.CliffSplatMap[srcX + 1, srcZ + 1];
				cS_SW = copiedArea.CliffSplatMap[srcX, srcZ + 1];
				if (pasteReflection == PasteReflection.Horizontal) (cS_NW, cS_NE, cS_SE, cS_SW) = MirrorSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, MirrorMode.Horizontal);
				else if (pasteReflection == PasteReflection.Vertical) (cS_NW, cS_NE, cS_SE, cS_SW) = MirrorSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, MirrorMode.Vertical);
				(cS_NW, cS_NE, cS_SE, cS_SW) = RotateSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, rotSteps);
			}
			else if (copiedArea.SplatMap != null)
			{
				cS_NW = sNW;
				cS_NE = sNE;
				cS_SE = sSE;
				cS_SW = sSW;
			}
		}

		int finalX = rotX;
		int finalZ = rotZ;
		if (symmetryMirror == MirrorMode.Horizontal)
		{
			finalX = targetWidth - 1 - rotX;
			curCell = MirrorCell(in curCell, MirrorMode.Horizontal);
			(sNW, sNE, sSE, sSW) = MirrorSplatQuad(in sNW, in sNE, in sSE, in sSW, MirrorMode.Horizontal);
			(cS_NW, cS_NE, cS_SE, cS_SW) = MirrorSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, MirrorMode.Horizontal);
		}
		else if (symmetryMirror == MirrorMode.Vertical)
		{
			finalZ = targetDepth - 1 - rotZ;
			curCell = MirrorCell(in curCell, MirrorMode.Vertical);
			(sNW, sNE, sSE, sSW) = MirrorSplatQuad(in sNW, in sNE, in sSE, in sSW, MirrorMode.Vertical);
			(cS_NW, cS_NE, cS_SE, cS_SW) = MirrorSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, MirrorMode.Vertical);
		}
		else if (symmetryMirror == MirrorMode.Both)
		{
			finalX = targetWidth - 1 - rotX;
			finalZ = targetDepth - 1 - rotZ;
			curCell = MirrorCell(in curCell, MirrorMode.Both);
			(sNW, sNE, sSE, sSW) = MirrorSplatQuad(in sNW, in sNE, in sSE, in sSW, MirrorMode.Both);
			(cS_NW, cS_NE, cS_SE, cS_SW) = MirrorSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, MirrorMode.Both);
		}
		else if (foldRotSteps != 0)
		{
			if (foldRotSteps == 1)
			{
				finalX = targetDepth - 1 - rotZ;
				finalZ = rotX;
			}
			else if (foldRotSteps == 2)
			{
				finalX = targetWidth - 1 - rotX;
				finalZ = targetDepth - 1 - rotZ;
			}
			else if (foldRotSteps == 3)
			{
				finalX = rotZ;
				finalZ = targetWidth - 1 - rotX;
			}

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

			if (pastePathing && copiedArea.Pathing != null && terrain.PathingCodes != null && srcX < pasteWidth && srcZ < pasteDepth)
			{
				terrain.PathingCodes[targetX, targetZ] = copiedArea.Pathing[srcX, srcZ];
				pathingModified = true;
			}

			modified = true;
		}
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
		int pasteWidth = copiedArea.Width;
		int pasteDepth = copiedArea.Depth;
		int targetWidth = (rotSteps == 1 || rotSteps == 3) ? pasteDepth : pasteWidth;
		int targetDepth = (rotSteps == 1 || rotSteps == 3) ? pasteWidth : pasteDepth;

		var cells = terrain.Cells;
		var srcCells = copiedArea.Cells;

		for (int sz = 0; sz < pasteDepth; sz++)
		{
			for (int sx = 0; sx < pasteWidth; sx++)
			{
				ProcessPasteBlockCell(copiedArea, sx, sz, pasteWidth, pasteDepth, targetWidth, targetDepth, blockStartX, blockStartZ, rotSteps, pasteReflection, symmetryMirror, foldRotSteps, width, depth, pasteHeights, pasteTextures, pastePathing, srcCells, cells, ref terrain, ref modified, ref pathingModified);
			}
		}
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
		cleanBaseName = Path.GetFileNameWithoutExtension(sourceFilePath).ToLowerInvariant().Replace(" ", "_") + ".rtex";
		string texDir = Path.Combine(wsPath, "Assets", "textures");
		Directory.CreateDirectory(texDir);
		outputRtex = Path.Combine(texDir, cleanBaseName);

		string ext = Path.GetExtension(sourceFilePath);
		if (ext.Equals(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			File.Copy(sourceFilePath, outputRtex, true);
		}
		else if (gameHost != null && gameHost.GroundTerrain != null)
		{
			gameHost.GroundTerrain.ProcessAndSaveRawTexture(sourceFilePath, outputRtex);
		}

		if (File.Exists(outputRtex))
		{
			byte[] rtexBytes = File.ReadAllBytes(outputRtex);
			string blake3 = RealmMetadataHelper.ComputeBlake3(rtexBytes, ".rtex");
			updateMetadata?.Invoke("textures", cleanBaseName, blake3, slotIndex);
			return true;
		}

		return false;
	}

	public void UpdateTextureMetadataAndReload(int index, string cleanBaseName, string outputRtex, GameHost? gameHost, Action<string, string, string, int> updateMetadata)
	{
		if (File.Exists(outputRtex))
		{
			byte[] rtexBytes = File.ReadAllBytes(outputRtex);
			string blake3 = RealmMetadataHelper.ComputeBlake3(rtexBytes, ".rtex");
			updateMetadata?.Invoke("textures", cleanBaseName, blake3, index);
		}

		if (gameHost != null && gameHost.GroundTerrain != null)
		{
			gameHost.GroundTerrain.ReloadTerrainTextures(true);
		}
	}

	public void CopyOrProcessTexture(string imagePath, string outputRtex, GameHost? gameHost)
	{
		string ext = Path.GetExtension(imagePath);
		if (ext.Equals(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			File.Copy(imagePath, outputRtex, true);
		}
		else if (gameHost != null && gameHost.GroundTerrain != null)
		{
			gameHost.GroundTerrain.ProcessAndSaveRawTexture(imagePath, outputRtex);
		}
	}

	public string PrepareTextureOutputDir(string cleanBaseName)
	{
		string wsPath = string.IsNullOrEmpty(this._editingDirectory)
			? MapWorkspaceService.GetDefaultMapFolderGlobalPath()
			: this._editingDirectory;
		string texDir = Path.Combine(wsPath, "Assets", "textures");
		Directory.CreateDirectory(texDir);
		return Path.Combine(texDir, cleanBaseName);
	}

	public string ResolveTextureFileName(string imagePath, string? preferredFileName)
	{
		string resolvedName = !string.IsNullOrWhiteSpace(preferredFileName) && !AssetIndexService.IsHexHash(Path.GetFileNameWithoutExtension(preferredFileName))
			? preferredFileName
			: (GameContext.AssetIndexService?.ResolvePrettyFileName(imagePath, preferredFileName) ?? Path.GetFileName(imagePath));
		return Path.GetFileNameWithoutExtension(resolvedName).ToLowerInvariant().Replace(" ", "_") + ".rtex";
	}

	public string GetManifestBlake3(string workspacePath, string mapTitle, string mapVersion, string author)
	{
		try
		{
			string manifestJsonPath = Path.Combine(workspacePath, "manifest.json");
			if (File.Exists(manifestJsonPath))
			{
				string manifestBlake3 = RealmMetadataHelper.ComputeBlake3(manifestJsonPath);
				if (!string.IsNullOrEmpty(manifestBlake3))
				{
					return manifestBlake3;
				}
			}

			if (Directory.Exists(workspacePath))
			{
				var manifest = MapManifest.CreateFromDirectory(workspacePath, mapTitle, author, mapVersion);
				return manifest.ComputeManifestBlake3();
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"could not compute the manifest hash of workspace {workspacePath}, so changes to it may go unnoticed: {ex.GetType().Name}: {ex.Message}");
		}

		return string.Empty;
	}

	public string GetMapVersionFromMetadata(bool forceReload, string workspacePath)
	{
		long now = Environment.TickCount64;
		if (!forceReload && !string.IsNullOrEmpty(this.CachedMapVersion) && (now - this.LastMapNameCacheTicks < 2000))
		{
			return this.CachedMapVersion;
		}

		try
		{
			var manifest = MapFileService.LoadManifest(workspacePath);
			if (!string.IsNullOrWhiteSpace(manifest.Version))
			{
				this.CachedMapVersion = manifest.Version.Trim();
				return this.CachedMapVersion;
			}

			var metadata = MapFileService.LoadMetadata(workspacePath);
			if (!string.IsNullOrWhiteSpace(metadata.MapProperties?.Version))
			{
				this.CachedMapVersion = metadata.MapProperties.Version.Trim();
				return this.CachedMapVersion;
			}

			this.CachedMapVersion = "1.0.0";
			return "1.0.0";
		}
		catch
		{
			this.CachedMapVersion = "1.0.0";
			return "1.0.0";
		}
	}


	public string SanitizeMapName(string? candidateName)
	{
		if (string.IsNullOrWhiteSpace(candidateName))
		{
			return MapNaming.CreateUntitledMapName();
		}

		string sanitized = candidateName.Trim();
		return string.IsNullOrEmpty(sanitized) ? MapNaming.CreateUntitledMapName() : sanitized;
	}

	public bool TryGetMapNameFromManifest(string workspacePath, out string mapName)
	{
		var manifest = MapFileService.LoadManifest(workspacePath);
		return TrySanitizeCandidate(manifest?.MapName, out mapName);
	}

	public bool TryGetMapNameFromMetadata(string workspacePath, out string mapName)
	{
		var metadata = MapFileService.LoadMetadata(workspacePath);
		return TrySanitizeCandidate(metadata?.MapProperties?.MapName, out mapName);
	}

	public bool TryGetMapNameFromActiveHost(out string mapName)
	{
		mapName = string.Empty;
		if (!string.IsNullOrEmpty(GameContext.Simulation.ActiveMapName))
		{
			string candidate = Path.GetFileNameWithoutExtension(GameContext.Simulation.ActiveMapName);
			return TrySanitizeCandidate(candidate, out mapName);
		}

		return false;
	}

	public string UpdateCachedMapName(string mapName, long nowTicks)
	{
		this.CachedMapName = mapName;
		this.LastMapNameCacheTicks = nowTicks;
		return mapName;
	}


	public static bool TrySanitizeCandidate(string? candidateName, out string sanitizedMapName)
	{
		sanitizedMapName = string.Empty;
		if (string.IsNullOrWhiteSpace(candidateName))
		{
			return false;
		}

		string cleaned = candidateName.Trim();
		if (string.IsNullOrEmpty(cleaned))
		{
			return false;
		}

		sanitizedMapName = cleaned;
		return true;
	}


	public string RemoveUnitObjectAttachmentDomain(string unitId, string socket, string attachmentId, string? parentAttachmentId = null)
	{
		if (string.IsNullOrEmpty(unitId) || string.IsNullOrEmpty(attachmentId)) return unitId;

		var registryService = GameContext.RegistryService;
		var unitRegistry = registryService.UnitRegistry;
		var buildingRegistry = registryService.BuildingRegistry;

		string regKey = unitId;
		bool isBuildingMeta = false;

		if (unitRegistry.ContainsKey((StringName)unitId))
		{
			regKey = unitId;
		}
		else if (buildingRegistry != null && buildingRegistry.ContainsKey((StringName)unitId))
		{
			regKey = unitId;
			isBuildingMeta = true;
		}
		else
		{
			string cleanId = Path.GetFileNameWithoutExtension(unitId);
			if (unitRegistry.ContainsKey((StringName)cleanId))
			{
				regKey = cleanId;
			}
			else if (buildingRegistry != null && buildingRegistry.ContainsKey((StringName)cleanId))
			{
				regKey = cleanId;
				isBuildingMeta = true;
			}
			else
			{
				foreach (var k in unitRegistry.Keys)
				{
					string kStr = k.ToString();
					if (kStr.Equals(unitId, StringComparison.OrdinalIgnoreCase) ||
						kStr.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
						Path.GetFileNameWithoutExtension(kStr).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
					{
						regKey = kStr;
						break;
					}
				}

				if (!isBuildingMeta && buildingRegistry != null)
				{
					foreach (var k in buildingRegistry.Keys)
					{
						string kStr = k.ToString();
						if (kStr.Equals(unitId, StringComparison.OrdinalIgnoreCase) ||
							kStr.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
							Path.GetFileNameWithoutExtension(kStr).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
						{
							regKey = kStr;
							isBuildingMeta = true;
							break;
						}
					}
				}
			}
		}

		if (isBuildingMeta && buildingRegistry != null && buildingRegistry.TryGetValue((StringName)regKey, out var bMeta))
		{
			bMeta.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId);
			buildingRegistry[(StringName)regKey] = bMeta;
		}
		else if (unitRegistry.TryGetValue((StringName)regKey, out var uMeta))
		{
			uMeta.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId);
			unitRegistry[(StringName)regKey] = uMeta;
		}

		string wsPath = this._editingDirectory;
		string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
		if (!File.Exists(metadataPath)) return regKey;

		GameContext.MetadataService.UpdateMetadata(wsPath, meta =>
		{
			bool updated = meta.UpdateUnit(unitId, u =>
			{
				u.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId);
				return u;
			});
			if (!updated)
				updated = meta.UpdateBuilding(unitId, b =>
				{
					b.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId);
					return b;
				});
			if (!updated)
				updated = meta.UpdateUnit(regKey, u =>
				{
					u.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId);
					return u;
				});
			if (!updated)
				meta.UpdateBuilding(regKey, b =>
				{
					b.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId);
					return b;
				});
		});

		long getWriteTimeSafe = File.Exists(metadataPath) ? File.GetLastWriteTimeUtc(metadataPath).Ticks : 0;
		this.LastMetadataSyncTime = getWriteTimeSafe;

		return regKey;
	}

	public string RestoreUnitObjectAttachmentsDomain(string targetId, UnitObjectAttachments? snapshot)
	{
		if (string.IsNullOrEmpty(targetId)) return targetId;

		var registryService = GameContext.RegistryService;
		var unitRegistry = registryService.UnitRegistry;
		var buildingRegistry = registryService.BuildingRegistry;

		string regKey = targetId;
		bool isBuildingMeta = false;

		if (unitRegistry.ContainsKey((StringName)targetId))
		{
			regKey = targetId;
		}
		else if (buildingRegistry != null && buildingRegistry.ContainsKey((StringName)targetId))
		{
			regKey = targetId;
			isBuildingMeta = true;
		}
		else
		{
			string cleanId = Path.GetFileNameWithoutExtension(targetId);
			if (unitRegistry.ContainsKey((StringName)cleanId))
			{
				regKey = cleanId;
			}
			else if (buildingRegistry != null && buildingRegistry.ContainsKey((StringName)cleanId))
			{
				regKey = cleanId;
				isBuildingMeta = true;
			}
			else
			{
				foreach (var k in unitRegistry.Keys)
				{
					string kStr = k.ToString();
					if (kStr.Equals(targetId, StringComparison.OrdinalIgnoreCase) ||
						kStr.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
						Path.GetFileNameWithoutExtension(kStr).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
					{
						regKey = kStr;
						break;
					}
				}

				if (!isBuildingMeta && buildingRegistry != null)
				{
					foreach (var k in buildingRegistry.Keys)
					{
						string kStr = k.ToString();
						if (kStr.Equals(targetId, StringComparison.OrdinalIgnoreCase) ||
							kStr.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
							Path.GetFileNameWithoutExtension(kStr).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
						{
							regKey = kStr;
							isBuildingMeta = true;
							break;
						}
					}
				}
			}
		}

		if (isBuildingMeta && buildingRegistry != null && buildingRegistry.TryGetValue((StringName)regKey, out var bMeta))
		{
			bMeta.ObjectAttachments = (snapshot != null && snapshot.HasAny()) ? snapshot?.Clone() : null;
			buildingRegistry[(StringName)regKey] = bMeta;
		}
		else if (unitRegistry.TryGetValue((StringName)regKey, out var uMeta))
		{
			uMeta.ObjectAttachments = (snapshot != null && snapshot.HasAny()) ? snapshot?.Clone() : null;
			unitRegistry[(StringName)regKey] = uMeta;
		}

		string wsPath = this._editingDirectory;
		string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
		if (File.Exists(metadataPath))
		{
			GameContext.MetadataService.UpdateMetadata(wsPath, meta =>
			{
				var atts = (snapshot != null && snapshot.HasAny()) ? snapshot?.Clone() : null;
				bool updated = meta.UpdateUnit(targetId, u =>
				{
					u.ObjectAttachments = atts;
					return u;
				});
				if (!updated)
					updated = meta.UpdateBuilding(targetId, b =>
					{
						b.ObjectAttachments = atts;
						return b;
					});
				if (!updated)
					updated = meta.UpdateUnit(regKey, u =>
					{
						u.ObjectAttachments = atts;
						return u;
					});
				if (!updated)
					meta.UpdateBuilding(regKey, b =>
					{
						b.ObjectAttachments = atts;
						return b;
					});
			});

			long getWriteTimeSafe = File.Exists(metadataPath) ? File.GetLastWriteTimeUtc(metadataPath).Ticks : 0;
			this.LastMetadataSyncTime = getWriteTimeSafe;
		}

		return regKey;
	}

	public string RestoreUnitPseudoSocketsDomain(string targetId, List<PseudoSocketAnchor>? snapshot)
	{
		if (string.IsNullOrEmpty(targetId)) return targetId;

		var registryService = GameContext.RegistryService;
		string cleanId = Path.GetFileNameWithoutExtension(targetId);
		string regKey = ResolvePseudoSocketRegistryKey(targetId, cleanId, registryService);

		ApplyPseudoSocketsToRegistry(regKey, snapshot, registryService);
		PseudoSocketCache.Clear();

		string wsPath = this._editingDirectory;
		string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
		if (File.Exists(metadataPath))
		{
			GameContext.MetadataService.UpdateMetadata(wsPath, meta =>
			{
				meta.UpdateUnit(targetId, u => { u.PseudoSockets = ClonePseudoSockets(snapshot); return u; });
				meta.UpdateBuilding(targetId, b => { b.PseudoSockets = ClonePseudoSockets(snapshot); return b; });
				meta.UpdateProp(targetId, p => { p.PseudoSockets = ClonePseudoSockets(snapshot); return p; });
				meta.UpdateResource(targetId, r => { r.PseudoSockets = ClonePseudoSockets(snapshot); return r; });

				if (!string.Equals(regKey, targetId, StringComparison.OrdinalIgnoreCase))
				{
					meta.UpdateUnit(regKey, u => { u.PseudoSockets = ClonePseudoSockets(snapshot); return u; });
					meta.UpdateBuilding(regKey, b => { b.PseudoSockets = ClonePseudoSockets(snapshot); return b; });
					meta.UpdateProp(regKey, p => { p.PseudoSockets = ClonePseudoSockets(snapshot); return p; });
					meta.UpdateResource(regKey, r => { r.PseudoSockets = ClonePseudoSockets(snapshot); return r; });
				}
			});

			long getWriteTimeSafe = File.Exists(metadataPath) ? File.GetLastWriteTimeUtc(metadataPath).Ticks : 0;
			this.LastMetadataSyncTime = getWriteTimeSafe;
		}

		return regKey;
	}

	private static string ResolvePseudoSocketRegistryKey(string targetId, string cleanId, RegistryService registryService)
	{
		if (registryService.UnitRegistry.ContainsKey((StringName)targetId)) return targetId;
		if (registryService.BuildingRegistry.ContainsKey((StringName)targetId)) return targetId;
		if (registryService.PropRegistry.ContainsKey((StringName)targetId)) return targetId;
		if (registryService.ResourceRegistry.ContainsKey((StringName)targetId)) return targetId;

		if (registryService.UnitRegistry.ContainsKey((StringName)cleanId)) return cleanId;
		if (registryService.BuildingRegistry.ContainsKey((StringName)cleanId)) return cleanId;
		if (registryService.PropRegistry.ContainsKey((StringName)cleanId)) return cleanId;
		if (registryService.ResourceRegistry.ContainsKey((StringName)cleanId)) return cleanId;

		return targetId;
	}

	private static void ApplyPseudoSocketsToRegistry(string regKey, List<PseudoSocketAnchor>? snapshot, RegistryService registryService)
	{
		var storedAnchors = ClonePseudoSockets(snapshot);

		if (registryService.UnitRegistry.TryGetValue((StringName)regKey, out var unitMeta))
		{
			unitMeta.PseudoSockets = storedAnchors;
			registryService.UnitRegistry[(StringName)regKey] = unitMeta;
		}

		if (registryService.BuildingRegistry.TryGetValue((StringName)regKey, out var buildingMeta))
		{
			buildingMeta.PseudoSockets = storedAnchors;
			registryService.BuildingRegistry[(StringName)regKey] = buildingMeta;
		}

		if (registryService.PropRegistry.TryGetValue((StringName)regKey, out var propMeta))
		{
			propMeta.PseudoSockets = storedAnchors;
			registryService.PropRegistry[(StringName)regKey] = propMeta;
		}

		if (registryService.ResourceRegistry.TryGetValue((StringName)regKey, out var resourceMeta))
		{
			resourceMeta.PseudoSockets = storedAnchors;
			registryService.ResourceRegistry[(StringName)regKey] = resourceMeta;
		}
	}

	private static List<PseudoSocketAnchor>? ClonePseudoSockets(List<PseudoSocketAnchor>? source)
	{
		if (source == null || source.Count == 0) return null;

		var clone = new List<PseudoSocketAnchor>(source.Count);
		foreach (var anchor in source)
		{
			if (anchor != null) clone.Add(anchor.Clone());
		}

		return clone;
	}

	public (bool Success, string Message, string FileName, string Blake3) ImportSkyboxAsset(string sourceFilePath, string wsPath)
	{
		try
		{
			string fileName = Path.GetFileName(sourceFilePath);
			string skyboxesDir = Path.Combine(wsPath, "Assets", "skyboxes");
			Directory.CreateDirectory(skyboxesDir);
			string targetPath = Path.Combine(skyboxesDir, fileName);

			File.Copy(sourceFilePath, targetPath, true);

			byte[] bytes = File.ReadAllBytes(targetPath);
			string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, fileName);

			return (true, "", fileName, blake3);
		}
		catch (Exception ex)
		{
			return (false, ex.Message, null!, null!);
		}
	}

	private (bool Success, string Message, string FileName, string Blake3) ImportImageAsAsset(string sourceFilePath, string wsPath, string subFolder)
	{
		try
		{
			string baseName = Path.GetFileNameWithoutExtension(sourceFilePath);
			string fileName = baseName + ".png";
			string outputDir = Path.Combine(wsPath, "Assets", subFolder);
			Directory.CreateDirectory(outputDir);
			string targetPath = Path.Combine(outputDir, fileName);

			var img = Image.LoadFromFile(sourceFilePath);
			if (img != null)
			{
				img.SavePng(targetPath);
			}
			else
			{
				File.Copy(sourceFilePath, targetPath, true);
			}

			byte[] bytes = File.ReadAllBytes(targetPath);
			string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, ".png");

			return (true, "", fileName, blake3);
		}
		catch (Exception ex)
		{
			return (false, ex.Message, null!, null!);
		}
	}

	public (bool Success, string Message, string FileName, string Blake3) ImportSpritesheetAsset(string sourceFilePath, string wsPath)
	{
		return ImportImageAsAsset(sourceFilePath, wsPath, "vfx");
	}

	public (bool Success, string Message, string FileName, string Blake3) ImportIconAsset(string sourceFilePath, string wsPath)
	{
		return ImportImageAsAsset(sourceFilePath, wsPath, "icons");
	}

	public long GetLastWriteTimeSafe(string path)
	{
		if (!File.Exists(path)) return 0;
		return File.GetLastWriteTimeUtc(path).Ticks;
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
		Directory.CreateDirectory(mapDir);

		string templateDir = templateDirSearch;
		if (!Directory.Exists(templateDir))
		{
			templateDir = Path.Combine(projectRoot, "..", "MapTemplate");
		}

		string templateScriptPath = Path.Combine(templateDir, "MapScript.cs");

		string scriptContent;
		if (File.Exists(templateScriptPath))
		{
			scriptContent = File.ReadAllText(templateScriptPath).Replace("__MAP_NAME__", mapName);
		}
		else
		{
			scriptContent = $@"namespace Realm.Maps;


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

		File.WriteAllText(Path.Combine(mapDir, "MapScript.cs"), scriptContent);
		Realm.Client.Utils.MapJsonFormatter.SaveFormattedJson(Path.Combine(mapDir, "metadata.json"), "{}");
		Realm.Client.Utils.MapJsonFormatter.SaveFormattedJson(Path.Combine(mapDir, "terrain.json"), "{}");
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
		try
		{
			var wsDir = new DirectoryInfo(this._editingDirectory);
			wsDir.LastWriteTimeUtc = DateTime.UtcNow;
		}
		catch
		{
		}
	}

	public bool HasDecalOverrides(int overrideCols, int overrideRows, float overrideFps, bool? overrideSubframeBlend)
	{
		return overrideCols > 0 || overrideRows > 0 || overrideFps > 0.001f || overrideSubframeBlend.HasValue;
	}

	public string ResolveDecalKeyFromMetadata(string decalId, string wsPath)
	{
		try
		{
			var metadata = MapFileService.LoadMetadata(wsPath);
			if (metadata?.Decals != null)
			{
				string filename = Path.GetFileName(decalId);
				string baseKey = Path.GetFileNameWithoutExtension(decalId);
				DecalMetadata? meta = null;

				if (metadata.Decals.TryGetValue(decalId, out var d0)) meta = d0;
				else if (metadata.Decals.TryGetValue(filename, out var d1)) meta = d1;
				else if (metadata.Decals.TryGetValue(baseKey, out var d2)) meta = d2;

				if (meta != null && !string.IsNullOrWhiteSpace(meta.TexturePath))
				{
					return meta.TexturePath;
				}
			}
		}
		catch
		{
		}

		return decalId;
	}

	public string FindDecalFile(string targetKey, string wsPath)
	{
		string targetFilename = Path.GetFileName(targetKey);
		string candidate1 = Path.Combine(wsPath, "Assets", "decals", targetFilename);
		if (File.Exists(candidate1)) return candidate1;

		if (!targetFilename.Contains('.'))
		{
			string candidate2 = Path.Combine(wsPath, "Assets", "decals", targetFilename + ".png");
			if (File.Exists(candidate2)) return candidate2;
			string candidateRtex = Path.Combine(wsPath, "Assets", "decals", targetFilename + ".rtex");
			if (File.Exists(candidateRtex)) return candidateRtex;
		}

		if (Path.IsPathRooted(targetKey) && File.Exists(targetKey)) return targetKey;

		string candidate3 = Path.Combine(wsPath, targetKey);
		if (File.Exists(candidate3)) return candidate3;

		return "res://icon.svg";
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
		if (fullPath.Equals(mapBackups, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(mapBackups + "/", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		if (fullPath.Equals(userDir, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(userDir + "/", StringComparison.OrdinalIgnoreCase) ||
			fullPath.Equals(resDir, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(resDir + "/", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return false;
	}

	public string GetDefaultDevelopmentMapDirectoryDomain(string mapName, string docsDir)
	{
		string cleanName = string.Join("_", mapName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
		if (string.IsNullOrEmpty(cleanName) || MapNaming.IsUntitledMapName(cleanName)) cleanName = "new_map";

		return Path.Combine(docsDir, cleanName);
	}

	public async Task<bool> PerformCompilationStepAsyncDomain(string workspace, Func<string, string?> findProjectFile, Func<string, bool, bool> tryBypassCompilation, Func<string, bool> checkMapApiDependency, Func<string, string, bool, Task<bool>> executeDotnetPublishAsync, Action<string> onStatusError, Action<string> onLog, bool isDebug = false)
	{
		try
		{
			if (!Directory.Exists(workspace))
			{
				onStatusError($"[MapEditorHUD] ERROR: The map folder '{workspace}' is not open, so there is nothing to compile");
				onLog($"[MapEditorHUD] The compilation step was asked for a workspace that does not exist: '{workspace}'");
				return false;
			}

			Realm.Client.Services.MapWorkspaceService.EnsureWitFile(workspace);
			Realm.Client.Services.MapWorkspaceService.EnsureWasmEntryPoint(workspace);
			Realm.Client.Services.MapWorkspaceService.EnsureCsproj(workspace, Path.GetFileName(workspace));
			Realm.Client.Services.MapWorkspaceService.EnsureSingleScriptOutput(workspace);

			string? csproj = findProjectFile(workspace);
			if (string.IsNullOrEmpty(csproj)) return false;

			if (!isDebug && tryBypassCompilation(workspace, isDebug)) return true;

			if (!checkMapApiDependency(workspace)) return false;

			return await executeDotnetPublishAsync(workspace, csproj, isDebug);
		}
		catch (Exception ex)
		{
			onStatusError(ex.Message);
			onLog(ex.ToString());
			return false;
		}
	}

	public string? FindProjectFileDomain(string workspace, Action<string> onErrorMessage)
	{
		var csprojFiles = Directory.GetFiles(workspace, "*.csproj", SearchOption.TopDirectoryOnly);
		if (csprojFiles.Length == 0)
		{
			onErrorMessage("[MapEditorHUD] ERROR: No .csproj found in workspace, cannot compile map script");
			return null;
		}

		return System.Linq.Enumerable.FirstOrDefault(csprojFiles, f => Path.GetFileName(f).Equals("MapScript.csproj", StringComparison.OrdinalIgnoreCase)) ?? csprojFiles[0];
	}

	public bool TryBypassCompilationDomain(string workspace, Func<string, string?> findExistingWasmFile, Func<string, DateTime, bool> hasNewerSourceFiles, Action onBypassSuccess, bool isDebug = false)
	{
		string? existingWasm = findExistingWasmFile(workspace);
		if (string.IsNullOrEmpty(existingWasm) || !File.Exists(existingWasm)) return false;

		if (!string.Equals(MapWorkspaceService.ReadBuildFlavorDomain(workspace), isDebug ? MapWorkspaceService.DebugBuildFlavor : MapWorkspaceService.ReleaseBuildFlavor, StringComparison.OrdinalIgnoreCase)) return false;

		DateTime wasmTime = File.GetLastWriteTimeUtc(existingWasm);
		if (hasNewerSourceFiles(workspace, wasmTime)) return false;

		onBypassSuccess();
		return true;
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
