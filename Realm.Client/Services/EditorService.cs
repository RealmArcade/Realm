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
			World.SetOrAdd(entity, new PreviousPosition(newPos));
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
		if (GameContext.Host is not { } host) return EditorGridSnapping.WorldPosToCellCoords(new Godot.Vector3(hitPos.X, hitPos.Y, hitPos.Z));

		host.EditorSymmetryPivot = pivot;
		host.GroundTerrain?.SetPolarCenter(pivot);
		host.UpdateSymmetryPivotVisuals();
		return EditorGridSnapping.WorldPosToCellCoords(new Godot.Vector3(hitPos.X, hitPos.Y, hitPos.Z));
	}

	public void SetPolarSpokesDomain(int spokes)
	{
		if (GameContext.Host == null) return;
		Tools.PolarSpokeFolds = spokes;
		Tools.PolarRadialStep = 360.0f / spokes;
		GameContext.Host.GroundTerrain?.SetPolarRadialStep(Tools.PolarRadialStep);
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
			EditorWorkspaceWatcher.UpdateWatchedFileTimestamps();
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
	public bool DecreaseScaleDialogWidth()
	{
		if (Tools.ScaleDialogTargetWidth > 32)
		{
			Tools.ScaleDialogTargetWidth = Math.Max(32, (Tools.ScaleDialogTargetWidth % 32 == 0 ? Tools.ScaleDialogTargetWidth - 32 : (Tools.ScaleDialogTargetWidth / 32) * 32));
			return true;
		}

		return false;
	}

	public bool IncreaseScaleDialogWidth()
	{
		if (Tools.ScaleDialogTargetWidth < 512)
		{
			Tools.ScaleDialogTargetWidth = Math.Min(512, (Tools.ScaleDialogTargetWidth / 32 + 1) * 32);
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
		Tools.CliffPaintTextureIndex = index;
	}

	public bool IsPolarGridActive(GridOverlayMode mode, MirrorMode mirrorMode)
	{
		return mirrorMode == MirrorMode.Rotational || mode == GridOverlayMode.Polar || mode == GridOverlayMode.Both;
	}

	public double AutoBackupElapsedSeconds { get => _workspace.AutoBackupElapsedSeconds; set => _workspace.AutoBackupElapsedSeconds = value; }

	public bool CheckAndUpdateRaycast(Godot.Vector3 pos)
	{
		if ((pos - Tools.LastRaycastPos).LengthSquared() > 0.01f)
		{
			Tools.LastRaycastPos = pos;
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

	private readonly EditorSwatchCatalog _swatches = new();

	public EditorSwatchCatalog Swatches => _swatches;

	public EditorDecalAssets Decals { get; } = new();
	public List<EditorCoordinate> EditorCoordinates { get; set; } = new();
	public bool ModelYOffsetSavePending { get; set; } = false;
	public bool ModelCollisionCircleSavePending { get; set; } = false;
	public readonly Dictionary<string, string> NormalizedAssetKeyCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly EditorAssetKeys _assetKeys;

	public (string terrainName, string cliffName) GetTextureNamesDomain()
	{
		return _swatches.GetTextureNames(Tools.PaintTextureIndex, Tools.CliffPaintTextureIndex);
	}

	public string? LastUsedFolder { get; set; } = null;
	public string? CurrentSourceFolder { get; set; } = null;
	public string? PendingCasSourceDirectory { get; set; } = null;
	public string? PendingDefaultSaveFolder { get; set; } = null;

	public void SetCurrentSourceFolder(string? folder)
	{
		CurrentSourceFolder = folder;
	}

	public EditorPlacementSettings Placement { get; } = new();

	private readonly EditorTerrainSurface _surface = new();

	public EditorTerrainSurface Surface => _surface;

	private readonly EditorClumpSession _clump = new();

	public EditorClumpSession Clump => _clump;


	private readonly EditorTerrainEditSession _terrainEdit;

	public EditorTerrainEditSession TerrainEdit => _terrainEdit;
	private readonly EditorPasteWriter _paste;
	private readonly EditorAreaClipboard _clipboard;

	public EditorAreaClipboard Clipboard => _clipboard;

	public EditorToolSettings Tools { get; } = new();

	public EditorSymmetry Symmetry { get; } = new();

	public EditorMapIdentity MapIdentity => _mapIdentity;

	private readonly EditorRampTool _ramp;
	private readonly EditorInteractionState _interaction = new();

	public EditorInteractionState Interaction => _interaction;

	public EditorService()
	{
		_terrainEdit = new EditorTerrainEditSession(_surface);
		_clipboard = new EditorAreaClipboard(_surface);
		_paste = new EditorPasteWriter(_surface, _clipboard);
		_ramp = new EditorRampTool(_surface);
		_assetKeys = new EditorAssetKeys(NormalizedAssetKeyCache);
	}

	public Godot.Vector3? RampStartPos => _ramp.RampStartPos;
	public bool IsDrawingTerrain => _terrainEdit.IsDrawing;

	public void ApplyCopiedObjectStateDomain(CopiedObjectTemplate copiedObj, out EditorTool newTool)
	{
		_interaction.SetIsPastingObject(true);
		Tools.PlacementRotation = copiedObj.Rotation;

		newTool = EditorTool.None;
		switch (copiedObj.Type)
		{
			case CopiedObjectKind.Unit:
				newTool = EditorTool.PlaceUnit;
				SetPlaceUnitPlayerIndexDomain(copiedObj.Player);
				break;
			case CopiedObjectKind.Prop:
				newTool = EditorTool.PlaceProp;
				break;
			case CopiedObjectKind.Decal:
				newTool = EditorTool.PlaceDecal;
				break;
		}
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
		EditorClumpPlacement.BuildSpawnRequests(centerPos, activeTool, activePlaceId, placeUnitPlayerIndex, placementScale, clumpCount, clumpScale, brushRadius, brushIsSquare, randomRotation, randomScale, placementRotation, mirrorMode, assetBaseCollisionRadius, _clump.Points, Symmetry.Pivot, Symmetry.Folds);

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
		_paste.Build(startX, startZ, pasteHeights, pasteTextures, pasteEntities, pastePathing, mirrorMode, rotationDegrees, pasteReflection, Symmetry.Pivot, Symmetry.Folds);


	public (float[,]? Heights, TerrainSplatWeights[,]? SplatMap, bool IsCliff) PerformFloodFill(
		Godot.Vector3 clickPos,
		int fillTextureIndex,
		int cliffTextureIndex,
		MirrorMode mirrorMode,
		bool isCliff = false) =>
		_surface.PerformTextureFloodFill(clickPos, fillTextureIndex, cliffTextureIndex, mirrorMode, isCliff, Symmetry.Pivot, Symmetry.Folds);

	public (int[,]? Before, int[,]? After) PerformFloodFillPathing(Godot.Vector3 clickPos, int pathingMask, bool pathingAdd, MirrorMode mirrorMode) =>
		_surface.PerformPathingFloodFill(clickPos, pathingMask, pathingAdd, mirrorMode, Symmetry.Pivot, Symmetry.Folds);

	public (TerrainCell[,]? BeforeCells, TerrainCell[,]? AfterCells, int[,]? BeforePathing, int[,]? AfterPathing, bool WasAdded) PerformWaterFloodFill(
		Godot.Vector3 clickPos,
		WaterType activeWaterMode,
		byte activeWaterProfile,
		float waterHeight,
		MirrorMode mirrorMode,
		bool isRemoveAction = false) =>
		_surface.PerformWaterFloodFill(clickPos, activeWaterMode, activeWaterProfile, waterHeight, mirrorMode, isRemoveAction, Symmetry.Pivot, Symmetry.Folds);

	public bool FillWaterPoolAtCell(int cellX, int cellZ, float waterSurfaceY) =>
		_surface.FillWaterPoolAtCell(cellX, cellZ, waterSurfaceY);

	public static bool TryGetTerrainState(out Entity terrainEntity)
	{
		terrainEntity = Entity.Null;
		if (GameContext.World == null) return false;

		var worldQuery = QueryCache.AllTerrainStateQuery;
		Entity foundEntity = Entity.Null;
		World.Query(in worldQuery, (Entity entity) => foundEntity = entity);
		terrainEntity = foundEntity;
		return terrainEntity != Entity.Null && World.IsAlive(terrainEntity);
	}

	public static ref TerrainState GetTerrainState()
	{
		if (!TryGetTerrainState(out Entity terrainEntity))
		{
			throw new InvalidOperationException("TerrainState entity not found in ECS world.");
		}

		return ref World.Get<TerrainState>(terrainEntity);
	}

	private void SetGridNodeSplat(int gx, int gz, in TerrainSplatWeights nw, in TerrainSplatWeights ne, in TerrainSplatWeights se, in TerrainSplatWeights sw)
	{
		_surface.SetGridNodeSplat(gx, gz, in nw, in ne, in se, in sw);
	}

	private void SetGridNodeCliffSplat(int gx, int gz, in TerrainSplatWeights nw, in TerrainSplatWeights ne, in TerrainSplatWeights se, in TerrainSplatWeights sw)
	{
		_surface.SetGridNodeCliffSplat(gx, gz, in nw, in ne, in se, in sw);
	}

	public string GetTerrainStatusString(Godot.Vector3 pos, EditorTool tool, string activePlaceId)
	{
		return EditorToolLabels.GetTerrainStatusString(pos, tool, activePlaceId);
	}



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
		EditorWorkspaceWatcher.Start(this._editingDirectory);
	}

	public (string? casDir, string? defaultSave) ConsumePendingCasSourceDomain()
	{
		string? casDir = this.PendingCasSourceDirectory;
		string? defaultSave = this.PendingDefaultSaveFolder;
		this.PendingCasSourceDirectory = null;
		this.PendingDefaultSaveFolder = null;
		return (casDir, defaultSave);
	}


	public void SetPendingCoordinatesDomain(int minX, int minZ, int maxX, int maxZ)
	{
		Tools.PendingCoordinateMinX = minX;
		Tools.PendingCoordinateMinZ = minZ;
		Tools.PendingCoordinateMaxX = maxX;
		Tools.PendingCoordinateMaxZ = maxZ;
	}

	public void SetPlaceUnitPlayerIndexDomain(int playerIndex)
	{
		Tools.PlaceUnitPlayerIndex = Mathf.Clamp(playerIndex, 0, PlayerColorConfig.Palette.Length - 1);
	}


	public bool TryGetSymmetryVisibilityState(out bool isRotational, out GridOverlayMode gridMode)
	{
		isRotational = false;
		gridMode = Tools.GridMode;

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
		Tools.PolarRingSpacing = spacing;
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

	public void RestoreLoadedCoordinates()
	{
		var loadedCoordinates = GameContext.SaveLoadService.GetLastLoadedCoordinates();
		EditorCoordinates.Clear();
		EditorCoordinates.AddRange(loadedCoordinates.Select(r => new EditorCoordinate { Name = r.Name, MinX = r.MinX, MinZ = r.MinZ, MaxX = r.MaxX, MaxZ = r.MaxZ }));
	}

	public string NormalizeModelAssetKey(string pathOrId)
	{
		return _assetKeys.NormalizeModelAssetKey(pathOrId);
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
		return EditorWorkspace.ChooseInitialDirectory(LastUsedFolder, CurrentSourceFolder, lastOpenedFolder, documentsDir, isRestrictedSaveDirectory);
	}



	public async Task<bool> PerformCompilationStepAsyncDomain(string workspace, Func<string, string?> findProjectFile, Func<string, bool, bool> tryBypassCompilation, Func<string, bool> checkMapApiDependency, Func<string, string, bool, Task<bool>> executeDotnetPublishAsync, Action<string> onStatusError, Action<string> onLog, bool isDebug = false)
	{
		return await EditorMapBuild.PerformCompilationStepAsync(workspace, findProjectFile, tryBypassCompilation, checkMapApiDependency, executeDotnetPublishAsync, onStatusError, onLog, isDebug);
	}


	public void RestoreEditorStateDomain(float waterHeight, int paintTextureIndex, int cliffPaintTextureIndex, CopiedAreaTemplate? copiedArea, System.Numerics.Vector3? selectionStart, System.Numerics.Vector3? selectionEnd)
	{
		Tools.WaterHeight = waterHeight;
		Tools.PaintTextureIndex = paintTextureIndex;
		Tools.CliffPaintTextureIndex = cliffPaintTextureIndex;
		_clipboard.CopiedArea = copiedArea;
		_interaction.SetSelectionStart(selectionStart != null ? new Godot.Vector2I((int)selectionStart.Value.X, (int)selectionStart.Value.Z) : (Godot.Vector2I?)null);
		_interaction.SetSelectionEnd(selectionEnd != null ? new Godot.Vector2I((int)selectionEnd.Value.X, (int)selectionEnd.Value.Z) : (Godot.Vector2I?)null);
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
