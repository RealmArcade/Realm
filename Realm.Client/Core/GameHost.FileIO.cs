using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Realm.Ecs;
using Godot;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Textures;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Core;

public partial class GameHost
{
	public void SaveMapToFile(string customPath = "", bool performReload = true)
	{
		if (GroundTerrain == null) return;
		EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;

		var savedState = CaptureEditorState();
		string[] splatData = GetSplatData();
		var unitsData = GetUnitsData();
		var propsData = GetPropsData();
		var decalsData = GetDecalsData();
		var coordinatesData = GetCoordinatesData();

		string path = string.IsNullOrEmpty(customPath) ? Path.Combine(GameContext.MapStorageService.CurrentMapDirectory ?? MapWorkspaceService.GetDefaultWorkspaceGlobalPath(), "terrain.json") : customPath;
		string absolutePath = ProjectSettings.GlobalizePath(path);
		GameContext.MapStorageService.CurrentMapDirectory = Path.GetDirectoryName(absolutePath);

		string[] cliffSplatData = GetCliffSplatData();

		bool success = GameContext.SaveLoadService.SaveMapToFile(absolutePath, splatData, unitsData, propsData, decalsData, coordinatesData, cliffSplatData);
		if (success)
		{
			if (performReload)
			{
				LoadMapFromFile(absolutePath, terrainOnly: false, clearUnits: true, ensureGlbOptimized: false);
				RestoreEditorStateAfterSave(savedState);
			}
			else
			{
				MapEditorHUD.Instance?.UpdateMapNameHeader();
			}
		}
	}

	private EditorSaveState CaptureEditorState()
	{
		var camera = MainCamera;
		bool hasCameraState = GameContext.World != null && GameContext.World.IsAlive(ECSContext.WorldEntity) && GameContext.World.Has<CameraState>(ECSContext.WorldEntity);

		return new EditorSaveState
		{
			WaterProfileIndex = ActiveWaterProfileIndex,
			WaterMode = EditorWaterMode,
			WaterHeight = GameContext.EditorService.EditorWaterHeight,
			BlockMode = EditorBlockMode,
			BlockLevelHeight = EditorBlockLevelHeight,
			ExactHeight = EditorExactHeight,
			PaintTextureIndex = GameContext.EditorService.EditorPaintTextureIndex,
			CliffPaintTextureIndex = GameContext.EditorService.EditorCliffPaintTextureIndex,
			ActiveTool = ActiveEditorTool,
			ActivePlaceId = ActivePlaceId,
			CopiedArea = GameContext.EditorService?.CopiedArea,
			SelectionStart = GameContext.EditorService?.SelectionStart,
			SelectionEnd = GameContext.EditorService?.SelectionEnd,
			HasCameraPosition = camera != null,
			CameraPosition = camera != null ? camera.Position : default,
			HasCameraState = hasCameraState,
			CameraStateSnapshot = hasCameraState ? GameContext.World.Get<CameraState>(ECSContext.WorldEntity) : default
		};
	}

	private string[] GetSplatData()
	{
		return GameContext.SaveLoadService.GetSplatData(GroundTerrain.SplatMap);
	}

	private (Entity Entity, float RotationY, float Scale)[] GetUnitsData()
	{
		var unitsData = new List<(Entity Entity, float RotationY, float Scale)>();
		foreach (var unit in AllUnits)
		{
			if (!GodotObject.IsInstanceValid(unit) || !GameContext.World.IsAlive(unit.Entity)) continue;

			float rotY = GetUnitPropRotationY(unit.Entity, unit.RotationDegrees.Y);
			float scale = GetUnitPropScale(unit.Entity, unit.Scale.X);
			unitsData.Add((unit.Entity, rotY, scale));
		}

		return unitsData.ToArray();
	}

	private (Entity Entity, float RotationY, float Scale)[] GetPropsData()
	{
		var propsData = new List<(Entity Entity, float RotationY, float Scale)>();
		foreach (var prop in AllProps)
		{
			if (!GodotObject.IsInstanceValid(prop) || !GameContext.World.IsAlive(prop.Entity)) continue;

			float rotY = GetUnitPropRotationY(prop.Entity, prop.RotationDegrees.Y);
			float scale = GetUnitPropScale(prop.Entity, prop.Scale.X);
			propsData.Add((prop.Entity, rotY, scale));
		}

		return propsData.ToArray();
	}

	private float GetUnitPropRotationY(Entity entity, float defaultRotY)
	{
		return GameContext.SaveLoadService.GetUnitPropRotationY(entity, defaultRotY, GameContext.World);
	}

	private float GetUnitPropScale(Entity entity, float defaultScale)
	{
		return GameContext.SaveLoadService.GetUnitPropScale(entity, defaultScale, GameContext.World);
	}

	private (Entity Entity, Vector3 Position, float RotationY, float Scale)[] GetDecalsData()
	{
		var decalsData = new List<(Entity Entity, Vector3 Position, float RotationY, float Scale)>();
		foreach (var decal in AllDecals)
		{
			if (!GodotObject.IsInstanceValid(decal) || !(decal is Decal3D decal3D)) continue;

			EnsureDecalEntity(decal3D);
			float rotY = GetUnitPropRotationY(decal3D.Entity, decal.RotationDegrees.Y);
			float scale = GetDecalScale(decal3D);
			Vector3 pos = GetDecalPosition(decal3D);

			decalsData.Add((decal3D.Entity, pos, rotY, scale));
		}

		return decalsData.ToArray();
	}

	private void EnsureDecalEntity(Decal3D decal3D)
	{
		decal3D.Entity = GameContext.EditorService.EnsureDecalEntity(decal3D.Entity, decal3D.DecalId, new Vector3(decal3D.Position.X, decal3D.Position.Y, decal3D.Position.Z), decal3D.RotationDegrees.Y, decal3D.Scale.X);
	}

	private float GetDecalScale(Decal3D decal3D)
	{
		return GameContext.SaveLoadService.GetDecalScale(decal3D.Entity, decal3D.Scale.X, decal3D.Size.X);
	}

	private Vector3 GetDecalPosition(Decal3D decal3D)
	{
		return GameContext.SaveLoadService.GetDecalPosition(decal3D.Entity, new Vector3(decal3D.Position.X, decal3D.Position.Y, decal3D.Position.Z));
	}

	private List<CoordinateSaveData> GetCoordinatesData()
	{
		return GameContext.SaveLoadService.GetCoordinatesData();
	}

	private string[] GetCliffSplatData()
	{
		if (GroundTerrain.CliffSplatMap == null)
		{
			InitializeCliffSplatMap();
		}

		return SerializeCliffSplatMap();
	}

	private void InitializeCliffSplatMap()
	{
		if (GroundTerrain == null) return;
		GroundTerrain.CliffSplatMap = GameContext.EditorService.GenerateDefaultCliffSplatMap(GroundTerrain.Width, GroundTerrain.Depth);
	}

	private string[] SerializeCliffSplatMap()
	{
		return GameContext.SaveLoadService.SerializeCliffSplatMap(GroundTerrain.CliffSplatMap);
	}

	private void RestoreEditorStateAfterSave(EditorSaveState savedState)
	{
		RestoreCameraAfterSave(savedState);

		ActiveWaterProfileIndex = savedState.WaterProfileIndex;
		EditorWaterMode = savedState.WaterMode;
		EditorBlockMode = savedState.BlockMode;
		EditorBlockLevelHeight = savedState.BlockLevelHeight;
		EditorExactHeight = savedState.ExactHeight;

		GameContext.EditorService?.RestoreEditorStateDomain(
			savedState.WaterHeight,
			savedState.PaintTextureIndex,
			savedState.CliffPaintTextureIndex,
			savedState.CopiedArea,
			savedState.SelectionStart != null ? new System.Numerics.Vector3(savedState.SelectionStart.Value.X, 0, savedState.SelectionStart.Value.Y) : (System.Numerics.Vector3?)null,
			savedState.SelectionEnd != null ? new System.Numerics.Vector3(savedState.SelectionEnd.Value.X, 0, savedState.SelectionEnd.Value.Y) : (System.Numerics.Vector3?)null
		);

		MapEditorHUD.Instance?.SelectToolFromHotkey(savedState.ActiveTool);
		MapEditorHUD.Instance?.UpdateTextureLabels();
		MapEditorHUD.Instance?.RefreshWaterSwatches();
		MapEditorHUD.Instance?.UpdateMapNameHeader();
		MapEditorHUD.Instance?.ShowFeedback(TranslationServer.Translate("Map saved"));
	}

	private void RestoreCameraAfterSave(EditorSaveState savedState)
	{
		if (savedState.HasCameraPosition && MainCamera != null)
		{
			MainCamera.Position = savedState.CameraPosition;
		}

		bool hasCameraState = GameContext.World != null && GameContext.World.IsAlive(ECSContext.WorldEntity) && GameContext.World.Has<CameraState>(ECSContext.WorldEntity);
		if (savedState.HasCameraState && hasCameraState)
		{
			ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
			state = savedState.CameraStateSnapshot;
		}
	}

	public bool LoadMapFromFile(string customPath = "", bool terrainOnly = false, bool clearUnits = true, bool ensureGlbOptimized = true)
	{
		GameContext.SaveLoadService.IsLoadingMap = true;
		try
		{
			string absolutePath = ResolveMapDirectory(customPath, ensureGlbOptimized);
			if (clearUnits)
			{
				ClearAllUnits();
			}

			bool success = GameContext.SaveLoadService.LoadMapFromFile(absolutePath, terrainOnly);
			if (!success)
			{
				return HandleLoadFailure();
			}

			EnsureGroundTerrainExistsView();

			TerrainState terrain = default;
			EditorState editor = default;
			bool foundWorld = false;

			var worldQuery = QueryCache.AllTerrainStateAndEditorStateQuery;
			GameContext.World.Query(in worldQuery, (Entity _, ref TerrainState t, ref EditorState e) =>
			{
				terrain = t;
				editor = e;
				foundWorld = true;
			});

			if (!foundWorld) return false;

			ApplyEditorStateView(editor);
			ApplyTerrainStateView(terrain);
			LoadSplatMapsView(absolutePath);
			LoadCliffSplatMapsView(absolutePath);
			GameContext.EditorService?.SetTerrainSplatMap(GroundTerrain.SplatMap, GroundTerrain.CliffSplatMap);
			AlignTerrainSplatMapExternal();

			if (!terrainOnly)
			{
				ProcessSpawnRequests();
			}

			GroundTerrain.UpdateMeshAndPhysics(true, true);
			if (!GameContext.SessionState.IsMapEditorMode) GroundTerrain.ReloadTerrainTextures();
			MapEditorHUD.Instance?.RegenerateMinimap();
			RestoreLoadedCoordinates();

			EditorHistoryManager.Clear();
			EditorHasUnsavedChanges = false;
			GameContext.EditorService?.ResetAllState();

			return true;
		}
		finally
		{
			GameContext.SaveLoadService.IsLoadingMap = false;
		}
	}

	private string ResolveMapDirectory(string customPath, bool ensureGlbOptimized)
	{
		string currentDir = GameContext.MapStorageService.CurrentMapDirectory;
		string defaultPath = MapWorkspaceService.GetDefaultWorkspaceGlobalPath();
		string absolutePath = GameContext.MapStorageService.ResolveMapDirectoryDomain(customPath, currentDir, defaultPath, ensureGlbOptimized, ProjectSettings.GlobalizePath);
		return absolutePath;
	}

	private bool HandleLoadFailure()
	{
		int defaultWidth = 128;
		int defaultDepth = 128;

		InitializeDefaultTerrainState(defaultWidth, defaultDepth);
		ResetGroundTerrainView(defaultWidth, defaultDepth);
		ResetEditorUIView();

		return false;
	}

	private void InitializeDefaultTerrainState(int defaultWidth, int defaultDepth)
	{
		GameContext.EditorService.InitializeDefaultTerrainStateDomain(defaultWidth, defaultDepth);
	}

	private void ResetGroundTerrainView(int defaultWidth, int defaultDepth)
	{
		if (GroundTerrain == null) return;

		GameContext.EditorService.ResetTerrainSplatMap(GroundTerrain.SplatMap, defaultWidth, defaultDepth);

		GroundTerrain.UpdateMeshAndPhysics();
		GroundTerrain.UpdatePathingTexture();
	}

	private void ResetEditorUIView()
	{
		if (MapEditorHUD.Instance != null)
		{
			MapEditorHUD.Instance.UpdateBlockModeExternal(true);
			MapEditorHUD.Instance.UpdateBlockLevelHeightExternal(2.0f);
			MapEditorHUD.Instance.UpdateCameraBoundsUI();
		}

		UpdateCameraBoundsOverlayVisibility();
		UpdateGridOverlayVisibility();
		UpdatePathingOverlay();

		GameContext.EditorService.ClearEditorCoordinates();
		RebuildAllCoordinatePersistentMeshes();

		if (MapEditorHUD.Instance != null)
		{
			MapEditorHUD.Instance.RefreshCoordinateListExternal();
		}
	}

	private void EnsureGroundTerrainExistsView()
	{
		if (GroundTerrain != null) return;

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

		var terrainNode = GameContext.SessionState.IsMapEditorMode ? (RuntimeTerrain)new EditableTerrain() : new RuntimeTerrain();
		terrainNode.Name = "Ground";
		AddChild(terrainNode);
		GroundTerrain = terrainNode;
	}

	private void ApplyEditorStateView(EditorState editor)
	{
		MapEditorHUD.Instance?.UpdateBlockModeExternal(editor.BlockMode);
		MapEditorHUD.Instance?.UpdateBlockLevelHeightExternal(editor.BlockLevelHeight);
		MapEditorHUD.Instance?.UpdateCameraBoundsUI();
		UpdateCameraBoundsOverlayVisibility();
		UpdateGridOverlayVisibility();
		UpdatePathingOverlay();

		if (!string.IsNullOrEmpty(editor.SkyboxPath))
		{
			SetSkyboxTexture(editor.SkyboxPath);
			MapEditorHUD.Instance?.UpdateSelectedSkyboxExternal(editor.SkyboxPath);
		}
	}

	private void ApplyTerrainStateView(TerrainState terrain)
	{
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;

		if (terrain.Cells != null && GroundTerrain != null)
		{
			GroundTerrain.Cells = (TerrainCell[,])terrain.Cells.Clone();
		}

		if (terrain.PathingCodes != null && terrain.PathingCodes.Length == width * depth && GroundTerrain != null)
		{
			GameContext.SaveLoadService.CopyPathingCodes(terrain.PathingCodes, GroundTerrain.PathingCodes, width, depth);
		}

		GroundTerrain.UpdateWaterSize();
	}

	private void LoadSplatMapsView(string absolutePath)
	{
		int splatW = GroundTerrain.Width + 1;
		int splatD = GroundTerrain.Depth + 1;

		string groundIndicesPath = Path.Combine(Path.GetDirectoryName(absolutePath), "terrain_splat_indices.exr");
		string groundWeightsPath = Path.Combine(Path.GetDirectoryName(absolutePath), "terrain_splat_weights.exr");
		bool groundSplatLoaded = false;

		if (File.Exists(groundIndicesPath) && File.Exists(groundWeightsPath))
		{
			groundSplatLoaded = TryLoadSplatMapImagesView(groundIndicesPath, groundWeightsPath, splatW, splatD);
		}

		if (!groundSplatLoaded)
		{
			LoadSplatMapFallbackView(splatW, splatD);
		}
	}

	private bool TryLoadSplatMapImagesView(string indicesPath, string weightsPath, int splatW, int splatD)
	{
		Image splatIdxImg = Image.LoadFromFile(indicesPath);
		Image splatWgtImg = Image.LoadFromFile(weightsPath);
		if (splatIdxImg == null || splatWgtImg == null) return false;

		splatIdxImg.Convert(Image.Format.Rgbaf);
		splatWgtImg.Convert(Image.Format.Rgbaf);
		int sW = splatIdxImg.GetWidth();
		int sD = splatIdxImg.GetHeight();
		int wgtW = splatWgtImg.GetWidth();
		int wgtD = splatWgtImg.GetHeight();

		byte[] idxBytes = splatIdxImg.GetData();
		byte[] wgtBytes = splatWgtImg.GetData();

		GroundTerrain.SplatMap = GameContext.SaveLoadService.GenerateSplatMapFromImages(idxBytes, wgtBytes, splatW, splatD, sW, sD, wgtW, wgtD);
		return true;
	}

	private void LoadSplatMapFallbackView(int splatW, int splatD)
	{
		GroundTerrain.SplatMap = GameContext.SaveLoadService.GenerateFallbackSplatMap(splatW, splatD, GroundTerrain.Width, GroundTerrain.Depth);
	}

	private void LoadCliffSplatMapsView(string absolutePath)
	{
		if (GroundTerrain == null) return;

		int targetCliffW = GroundTerrain.Width + 1;
		int targetCliffD = GroundTerrain.Depth + 1;
		string cliffIndicesPath = Path.Combine(Path.GetDirectoryName(absolutePath), "terrain_cliff_splat_indices.exr");
		string cliffWeightsPath = Path.Combine(Path.GetDirectoryName(absolutePath), "terrain_cliff_splat_weights.exr");

		bool loaded = false;
		if (File.Exists(cliffIndicesPath) && File.Exists(cliffWeightsPath))
		{
			loaded = TryLoadCliffSplatMapImagesView(cliffIndicesPath, cliffWeightsPath, targetCliffW, targetCliffD);
		}

		if (!loaded)
		{
			GroundTerrain.CliffSplatMap = new TerrainSplatWeights[targetCliffW, targetCliffD];
			for (int z = 0; z < targetCliffD; z++)
			{
				for (int x = 0; x < targetCliffW; x++)
				{
					GroundTerrain.CliffSplatMap[x, z] = TerrainSplatWeights.CreateSolid(1);
				}
			}
		}
	}

	private bool TryLoadCliffSplatMapImagesView(string indicesPath, string weightsPath, int targetCliffW, int targetCliffD)
	{
		Image cliffIdxImg = Image.LoadFromFile(indicesPath);
		Image cliffWgtImg = Image.LoadFromFile(weightsPath);
		if (cliffIdxImg == null || cliffWgtImg == null) return false;

		cliffIdxImg.Convert(Image.Format.Rgbaf);
		cliffWgtImg.Convert(Image.Format.Rgbaf);
		int cW = cliffIdxImg.GetWidth();
		int cD = cliffIdxImg.GetHeight();
		int wgtW = cliffWgtImg.GetWidth();
		int wgtD = cliffWgtImg.GetHeight();

		byte[] idxBytes = cliffIdxImg.GetData();
		byte[] wgtBytes = cliffWgtImg.GetData();

		GroundTerrain.CliffSplatMap = GameContext.SaveLoadService.GenerateSplatMapFromImages(idxBytes, wgtBytes, targetCliffW, targetCliffD, cW, cD, wgtW, wgtD);
		return true;
	}

	private void ProcessSpawnRequests()
	{
		ProcessUnitSpawnRequests();
		ProcessPropSpawnRequests();
		PropMultiMeshManager.Instance?.RebuildAll();
		ProcessDecalSpawnRequests();
	}

	private void ProcessUnitSpawnRequests()
	{
		GameContext.UnitSpawnService.ProcessUnitSpawnRequestsDomain((unitId, pos, rotY, scale, player) => {
			SpawnUnitExternal(unitId, new Godot.Vector3(pos.X, pos.Y, pos.Z), rotY, scale, player);
		});
	}

	private void ProcessPropSpawnRequests()
	{
		GameContext.UnitSpawnService.ProcessPropSpawnRequests((propId, pos, rotY, scale) => {
			SpawnPropExternalWithParams(propId, new Godot.Vector3(pos.X, pos.Y, pos.Z), rotY, scale);
		});
	}

	private void ProcessDecalSpawnRequests()
	{
		GameContext.UnitSpawnService.ProcessDecalSpawnRequests((decalId, pos, rot, scale) => {
			SpawnDecalExternalWithParams(decalId, new Godot.Vector3(pos.X, pos.Y, pos.Z), new Godot.Vector3(rot.X, rot.Y, rot.Z), scale);
		});
	}

	private void RestoreLoadedCoordinates()
	{
		GameContext.EditorService.RestoreLoadedCoordinates();
		RebuildAllCoordinatePersistentMeshes();
		MapEditorHUD.Instance?.RefreshCoordinateListExternal();
	}




	public void FlushModelYOffsetSave()
	{
		if (GameContext.EditorService.ConsumeModelOffsetSavePending())
		{
			SaveModelYOffsetsToMetadataJson();
		}
	}

	public void FlushModelCollisionCircleSave()
	{
		FlushModelYOffsetSave();
	}

	public void LoadModelYOffsetsFromMetadataJson(string directory = null)
	{
		try
		{
			string mapDir = !string.IsNullOrEmpty(directory) ? directory : GameContext.MapStorageService.CurrentMapDirectory;
			if (string.IsNullOrEmpty(mapDir))
			{
				mapDir = ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath);
			}

			LoadUnitMetadata(mapDir);
			var metaService = GameContext.MetadataService ?? GameContext.MetadataService;
			var metadata = metaService.LoadMetadata(mapDir);

			GameContext.ModelOverrideService.LoadOverridesFromMetadata(metadata, GameContext.EditorService.NormalizeModelAssetKey);

			UpdateAllMaterialOverrides();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to load metadata overrides from JSON: {ex.Message}");
		}
	}










	public void SaveModelYOffsetsToMetadataJson(string directory = null)
	{
		try
		{
			string mapDir = !string.IsNullOrEmpty(directory) ? directory : GameContext.MapStorageService.CurrentMapDirectory;
			if (string.IsNullOrEmpty(mapDir)) mapDir = ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath);

			var metaService = GameContext.MetadataService ?? GameContext.MetadataService;
			metaService.UpdateMetadata(mapDir, UpdateMetadataModelOverrides);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"SaveModelYOffsetsToMetadataJson error: {ex.Message}");
		}
	}

	private DecalAssetData? TryLoadDecalFromResource(string decalId, string cacheKey)
	{
		if (!decalId.StartsWith("res://")) return null;

		try
		{
			if (ResourceLoader.Exists(decalId))
			{
				var resTex = GD.Load<Texture2D>(decalId);
				var resData = new DecalAssetData
				{
					DecalId = decalId,
					TexturePath = decalId,
					PrimaryTexture = resTex,
					Columns = 1,
					Rows = 1
				};
				GameContext.EditorService.CacheDecalAsset(cacheKey, resData);
				return resData;
			}
		}
		catch
		{
		}

		return null;
	}


	private bool LoadRtexDecalImages(string path, out Image? albedoImg, out Image? normalImg, ref int cols, ref int rows)
	{
		albedoImg = null;
		normalImg = null;
		byte[] rtexBytes = File.ReadAllBytes(path);
		var (customJson, layers, _) = RtexFile.Parse(rtexBytes);

		GameContext.EditorService.ParseRtexDecalMetadata(customJson, ref cols, ref rows);

		albedoImg = LoadRtexDecalLayer(layers, 0);
		normalImg = LoadRtexDecalLayer(layers, 1);

		return albedoImg != null;
	}


	private Image? LoadRtexDecalLayer(IReadOnlyList<byte[]> layers, int layerIndex)
	{
		if (layers.Count <= layerIndex || layers[layerIndex] == null || layers[layerIndex].Length == 0)
			return null;

		var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
		if (img.LoadWebpFromBuffer(layers[layerIndex]) != Error.Ok)
			img.LoadPngFromBuffer(layers[layerIndex]);

		return img;
	}

	private bool LoadExternalDecalImage(string path, out Image? albedoImg, ref int cols, ref int rows)
	{
		albedoImg = Image.LoadFromFile(path);
		string? meta = RealmMetadataHelper.ExtractMetadata(path);
		if (string.IsNullOrEmpty(meta)) return albedoImg != null;

		GameContext.EditorService.ParseExternalDecalMetadata(meta, ref cols, ref rows);

		return albedoImg != null;
	}


	private DecalAssetData? TryLoadDecalImageFromPath(string path, string decalId, string? explicitTexturePath, string cacheKey, ref int detectedCols, ref int detectedRows)
	{
		try
		{
			Image? albedoImg = null;
			Image? normalImg = null;

			if (path.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
			{
				if (!LoadRtexDecalImages(path, out albedoImg, out normalImg, ref detectedCols, ref detectedRows)) return null;
			}
			else
			{
				if (!LoadExternalDecalImage(path, out albedoImg, ref detectedCols, ref detectedRows)) return null;
			}

			if (albedoImg != null)
			{
				var assetData = new DecalAssetData
				{
					DecalId = decalId,
					TexturePath = explicitTexturePath ?? decalId,
					Columns = 1,
					Rows = 1,
					Fps = 12.0f,
					SubframeBlend = false
				};

				if (!albedoImg.HasMipmaps()) albedoImg.GenerateMipmaps();
				assetData.PrimaryTexture = ImageTexture.CreateFromImage(albedoImg);

				if (normalImg != null)
				{
					if (!normalImg.HasMipmaps()) normalImg.GenerateMipmaps();
					assetData.PrimaryNormal = ImageTexture.CreateFromImage(normalImg);
				}

				GameContext.EditorService.DecalAssetCache[cacheKey] = assetData;
				return assetData;
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to load decal image from '{path}': {ex.Message}");
		}

		return null;
	}

	public DecalAssetData LoadDecalAsset(
		string decalId,
		int overrideCols = 0,
		int overrideRows = 0,
		float overrideFps = 0f,
		bool? overrideSubframeBlend = null,
		bool forceReload = false)
	{
		if (string.IsNullOrEmpty(decalId)) decalId = "logo";
		string cacheKey = decalId;

		if (!forceReload && !GameContext.EditorService.HasDecalOverrides(overrideCols, overrideRows, overrideFps, overrideSubframeBlend))
		{
			if (TryGetValidCachedDecal(cacheKey, out var cachedData))
				return cachedData;
		}

		var resDecal = TryLoadDecalFromResource(decalId, cacheKey);
		if (resDecal != null) return resDecal;

		string filename = Path.GetFileName(decalId);
		string baseKey = Path.GetFileNameWithoutExtension(decalId);
		string wsPath = ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath);

		var meta = GameContext.EditorService.ParseDecalMetadata(decalId, filename, baseKey, wsPath);
		var loaded = TryLoadDecalFromCandidates(decalId, meta.cols, meta.rows, meta.explicitPath, wsPath, cacheKey);
		if (loaded != null) return loaded;

		return CreateFallbackDecal(decalId, meta.explicitPath, cacheKey);
	}


	private bool TryGetValidCachedDecal(string cacheKey, out DecalAssetData cachedData)
	{
		cachedData = null;
		if (GameContext.EditorService.DecalAssetCache.TryGetValue(cacheKey, out var data) && data != null && GodotObject.IsInstanceValid(data.PrimaryTexture))
		{
			cachedData = data;
			return true;
		}

		return false;
	}

	private DecalAssetData TryLoadDecalFromCandidates(string decalId, int cols, int rows, string? explicitPath, string wsPath, string cacheKey)
	{
		var candidatePaths = GameContext.EditorService.GetDecalCandidatePaths(decalId, explicitPath, wsPath);
		foreach (var path in candidatePaths)
		{
			if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;

			int tempCols = cols, tempRows = rows;
			var loaded = TryLoadDecalImageFromPath(path, decalId, explicitPath, cacheKey, ref tempCols, ref tempRows);
			if (loaded != null) return loaded;
		}

		return null;
	}

	private DecalAssetData CreateFallbackDecal(string decalId, string? explicitPath, string cacheKey)
	{
		var fallback = new DecalAssetData
		{
			DecalId = decalId,
			TexturePath = explicitPath ?? decalId,
			PrimaryTexture = GD.Load<Texture2D>("res://icon.svg"),
			Columns = 1,
			Rows = 1
		};
		GameContext.EditorService.DecalAssetCache[cacheKey] = fallback;
		return fallback;
	}

	public Texture2D LoadDecalTexture(string decalId)
	{
		return LoadDecalAsset(decalId).PrimaryTexture;
	}

	public string GetDecalTexturePath(string decalId)
	{
		if (string.IsNullOrEmpty(decalId)) decalId = "logo";
		if (decalId.StartsWith("res://")) return decalId;

		string wsPath = ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath);
		string targetKey = GameContext.EditorService.ResolveDecalKeyFromMetadata(decalId, wsPath);

		return GameContext.EditorService.FindDecalFile(targetKey, wsPath);
	}
}