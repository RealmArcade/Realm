using System;
using Godot;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Components.Meta;
using Arch.Core;
using System.Collections.Generic;
using System.Linq;

public partial class GameHost
{
	public string CurrentMapDirectory { get; set; } = MapWorkspaceService.GetDefaultWorkspaceGlobalPath();

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

		string path = string.IsNullOrEmpty(customPath) ? System.IO.Path.Combine(CurrentMapDirectory ?? MapWorkspaceService.GetDefaultWorkspaceGlobalPath(), "terrain.json") : customPath;
		string absolutePath = ProjectSettings.GlobalizePath(path);
		CurrentMapDirectory = System.IO.Path.GetDirectoryName(absolutePath);

		string[] cliffSplatData = GetCliffSplatData();

		bool success = _saveLoadService.SaveMapToFile(absolutePath, splatData, unitsData, propsData, decalsData, coordinatesData, cliffSplatData);
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

	private class EditorSaveState
	{
		public byte WaterProfileIndex;
		public WaterType WaterMode;
		public float WaterHeight;
		public bool BlockMode;
		public float BlockLevelHeight;
		public float ExactHeight;
		public int PaintTextureIndex;
		public int CliffPaintTextureIndex;
		public EditorTool ActiveTool;
		public string ActivePlaceId;
		public EditorService.CopiedAreaTemplate CopiedArea;
		public Vector2I? SelectionStart;
		public Vector2I? SelectionEnd;
	}

	private EditorSaveState CaptureEditorState()
	{
		return new EditorSaveState
		{
			WaterProfileIndex = ActiveWaterProfileIndex,
			WaterMode = EditorWaterMode,
			WaterHeight = EditorWaterHeight,
			BlockMode = EditorBlockMode,
			BlockLevelHeight = EditorBlockLevelHeight,
			ExactHeight = EditorExactHeight,
			PaintTextureIndex = EditorPaintTextureIndex,
			CliffPaintTextureIndex = EditorCliffPaintTextureIndex,
			ActiveTool = ActiveEditorTool,
			ActivePlaceId = ActivePlaceId,
			CopiedArea = _editorService?.CopiedArea,
			SelectionStart = _editorService?.SelectionStart,
			SelectionEnd = _editorService?.SelectionEnd
		};
	}

	private string[] GetSplatData()
	{
		int splatW = GroundTerrain.SplatMap.GetLength(0);
		int splatD = GroundTerrain.SplatMap.GetLength(1);
		string[] splatData = new string[splatW * splatD];
		for (int z = 0; z < splatD; z++)
		{
			for (int x = 0; x < splatW; x++)
			{
				splatData[z * splatW + x] = GroundTerrain.SplatMap[x, z].Serialize();
			}
		}
		return splatData;
	}

	private (Entity Entity, float RotationY, float Scale)[] GetUnitsData()
	{
		var unitsData = new List<(Entity Entity, float RotationY, float Scale)>();
		foreach (var unit in AllUnits)
		{
			if (!GodotObject.IsInstanceValid(unit) || !EcsWorld.IsAlive(unit.Entity)) continue;
			
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
			if (!GodotObject.IsInstanceValid(prop) || !EcsWorld.IsAlive(prop.Entity)) continue;
			
			float rotY = GetUnitPropRotationY(prop.Entity, prop.RotationDegrees.Y);
			float scale = GetUnitPropScale(prop.Entity, prop.Scale.X);
			propsData.Add((prop.Entity, rotY, scale));
		}
		return propsData.ToArray();
	}

	private float GetUnitPropRotationY(Entity entity, float defaultRotY)
	{
		float rotY = defaultRotY;
		if (EcsWorld.Has<RotationY>(entity))
		{
			float existingRotY = EcsWorld.Get<RotationY>(entity).Value;
			if (MathF.Abs(rotY - existingRotY) < 0.001f || MathF.Abs(MathF.Abs(rotY - existingRotY) - 360f) < 0.001f)
			{
				rotY = existingRotY;
			}
		}
		return rotY;
	}

	private float GetUnitPropScale(Entity entity, float defaultScale)
	{
		float scale = defaultScale;
		if (EcsWorld.Has<ModelScale>(entity))
		{
			float existingScale = EcsWorld.Get<ModelScale>(entity).Value;
			if (MathF.Abs(scale - existingScale) < 0.0001f)
			{
				scale = existingScale;
			}
		}
		return scale;
	}

	private (Entity Entity, System.Numerics.Vector3 Position, float RotationY, float Scale)[] GetDecalsData()
	{
		var decalsData = new List<(Entity Entity, System.Numerics.Vector3 Position, float RotationY, float Scale)>();
		foreach (var decal in AllDecals)
		{
			if (!GodotObject.IsInstanceValid(decal) || !(decal is Decal3D decal3D)) continue;
			
			EnsureDecalEntity(decal3D);
			float rotY = GetUnitPropRotationY(decal3D.Entity, decal.RotationDegrees.Y);
			float scale = GetDecalScale(decal3D);
			System.Numerics.Vector3 pos = GetDecalPosition(decal3D);
			
			decalsData.Add((decal3D.Entity, pos, rotY, scale));
		}
		return decalsData.ToArray();
	}

	private void EnsureDecalEntity(Decal3D decal3D)
	{
		if (!EcsWorld.IsAlive(decal3D.Entity))
		{
			var newEnt = EcsWorld.Create();
			decal3D.Entity = newEnt;
			EcsWorld.Add(newEnt, new DecalIdentity(decal3D.DecalId));
			EcsWorld.Add(newEnt, new Position(new System.Numerics.Vector3(decal3D.Position.X, decal3D.Position.Y, decal3D.Position.Z)));
			EcsWorld.Add(newEnt, new RotationY(decal3D.RotationDegrees.Y));
			EcsWorld.Add(newEnt, new ModelScale(decal3D.Scale.X));
		}
	}

	private float GetDecalScale(Decal3D decal3D)
	{
		float scale = decal3D.Scale.X != 1.0f 
			? decal3D.Scale.X 
			: (EcsWorld.Has<ModelScale>(decal3D.Entity) ? EcsWorld.Get<ModelScale>(decal3D.Entity).Value : (decal3D.Size.X / 6.0f));
		return GetUnitPropScale(decal3D.Entity, scale);
	}

	private System.Numerics.Vector3 GetDecalPosition(Decal3D decal3D)
	{
		var pos = new System.Numerics.Vector3(decal3D.Position.X, decal3D.Position.Y, decal3D.Position.Z);
		if (EcsWorld.Has<Position>(decal3D.Entity))
		{
			var existingPos = EcsWorld.Get<Position>(decal3D.Entity).Value;
			if ((pos - existingPos).Length() < 0.0001f)
			{
				pos = existingPos;
			}
		}
		return pos;
	}

	private List<CoordinateSaveData> GetCoordinatesData()
	{
		return EditorCoordinates.Select(r => new CoordinateSaveData
		{
			Name = r.Name,
			MinX = r.MinX,
			MinZ = r.MinZ,
			MaxX = r.MaxX,
			MaxZ = r.MaxZ
		}).ToList();
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
		int cliffW = GroundTerrain.Width + 1;
		int cliffD = GroundTerrain.Depth + 1;
		GroundTerrain.CliffSplatMap = new TerrainSplatWeights[cliffW, cliffD];
		for (int z = 0; z < cliffD; z++)
		{
			for (int x = 0; x < cliffW; x++)
			{
				GroundTerrain.CliffSplatMap[x, z] = TerrainSplatWeights.CreateSolid(1);
			}
		}
	}

	private string[] SerializeCliffSplatMap()
	{
		int cliffW = GroundTerrain.CliffSplatMap.GetLength(0);
		int cliffD = GroundTerrain.CliffSplatMap.GetLength(1);
		string[] cliffSplatData = new string[cliffW * cliffD];
		for (int z = 0; z < cliffD; z++)
		{
			for (int x = 0; x < cliffW; x++)
			{
				cliffSplatData[z * cliffW + x] = GroundTerrain.CliffSplatMap[x, z].Serialize();
			}
		}
		return cliffSplatData;
	}

	private void RestoreEditorStateAfterSave(EditorSaveState savedState)
	{
		ActiveWaterProfileIndex = savedState.WaterProfileIndex;
		EditorWaterMode = savedState.WaterMode;
		EditorWaterHeight = savedState.WaterHeight;
		EditorBlockMode = savedState.BlockMode;
		EditorBlockLevelHeight = savedState.BlockLevelHeight;
		EditorExactHeight = savedState.ExactHeight;
		EditorPaintTextureIndex = savedState.PaintTextureIndex;
		EditorCliffPaintTextureIndex = savedState.CliffPaintTextureIndex;
		
		if (_editorService != null)
		{
			_editorService.CopiedArea = savedState.CopiedArea;
			_editorService.SetSelectionStart(savedState.SelectionStart);
			_editorService.SetSelectionEnd(savedState.SelectionEnd);
		}
		
		MapEditorHUD.Instance?.SelectToolFromHotkey(savedState.ActiveTool);
		MapEditorHUD.Instance?.UpdateTextureLabels();
		MapEditorHUD.Instance?.RefreshWaterSwatches();
		MapEditorHUD.Instance?.UpdateMapNameHeader();
		MapEditorHUD.Instance?.ShowFeedback(TranslationServer.Translate("Map saved"));
	}

	public bool LoadMapFromFile(string customPath = "", bool terrainOnly = false, bool clearUnits = true, bool ensureGlbOptimized = true)
	{
		IsLoadingMap = true;
		try
		{
			string absolutePath = ResolveMapDirectory(customPath, ensureGlbOptimized);
			if (clearUnits)
			{
				ClearAllUnits();
			}

			bool success = _saveLoadService.LoadMapFromFile(absolutePath, terrainOnly);
			if (!success)
			{
				return HandleLoadFailure();
			}

			EnsureGroundTerrainExists();

			TerrainState terrain = default;
			EditorState editor = default;
			bool foundWorld = false;

			var worldQuery = Realm.Ecs.Common.QueryCache.AllTerrainStateAndEditorStateQuery;
			EcsWorld.Query(in worldQuery, (ref TerrainState t, ref EditorState e) =>
			{
				terrain = t;
				editor = e;
				foundWorld = true;
			});

			if (!foundWorld) return false;

			ApplyEditorState(editor);
			ApplyTerrainState(terrain);
			LoadSplatMaps(absolutePath);
			LoadCliffSplatMaps(absolutePath);
			_editorService?.SetTerrainSplatMap(GroundTerrain.SplatMap, GroundTerrain.CliffSplatMap);
			AlignTerrainSplatMapExternal();

			if (!terrainOnly)
			{
				ProcessSpawnRequests();
			}

			GroundTerrain.UpdateMeshAndPhysics(true, true);
			MapEditorHUD.Instance?.RegenerateMinimap();
			RestoreLoadedCoordinates();

			EditorHistoryManager.Clear();
			EditorHasUnsavedChanges = false;
			_editorService?.ResetAllState();

			return true;
		}
		finally
		{
			IsLoadingMap = false;
		}
	}

	private string ResolveMapDirectory(string customPath, bool ensureGlbOptimized)
	{
		string path = string.IsNullOrEmpty(customPath) ? System.IO.Path.Combine(CurrentMapDirectory ?? MapWorkspaceService.GetDefaultWorkspaceGlobalPath(), "terrain.json") : customPath;
		string absolutePath = ProjectSettings.GlobalizePath(path);
		CurrentMapDirectory = System.IO.Path.GetDirectoryName(absolutePath);
		if (ensureGlbOptimized)
		{
			MapWorkspaceService.EnsureGlbAssetsOptimized(CurrentMapDirectory);
			MapWorkspaceService.EnsurePngAssetsConverted(CurrentMapDirectory);
		}
		return absolutePath;
	}

	private bool HandleLoadFailure()
	{
		int defaultWidth = 128;
		int defaultDepth = 128;

		InitializeDefaultTerrainState(defaultWidth, defaultDepth);
		ResetGroundTerrain(defaultWidth, defaultDepth);
		ResetEditorUI();

		return false;
	}

	private void InitializeDefaultTerrainState(int defaultWidth, int defaultDepth)
	{
		if (!EcsWorld.Has<TerrainState>(_worldEntity)) return;

		float[,] defaultHeights = new float[defaultWidth, defaultDepth];
		int[,] defaultPathing = new int[defaultWidth, defaultDepth];

		for (int z = 0; z < defaultDepth; z++)
		{
			for (int x = 0; x < defaultWidth; x++)
			{
				defaultHeights[x, z] = 0.0f;
				defaultPathing[x, z] = EditableTerrain.GetDefaultPathingCode(WaterType.None);
			}
		}

		ref var ts = ref EcsWorld.Get<TerrainState>(_worldEntity);
		ts.SetHeights(defaultHeights);
		ts.PathingCodes = defaultPathing;
		EcsWorld.Set(_worldEntity, ts);
	}

	private void ResetGroundTerrain(int defaultWidth, int defaultDepth)
	{
		if (GroundTerrain == null) return;

		for (int z = 0; z < defaultDepth; z++)
		{
			for (int x = 0; x < defaultWidth; x++)
			{
				GroundTerrain.SplatMap[x, z] = TerrainSplatWeights.CreateSolid(0);
			}
		}

		GroundTerrain.UpdateMeshAndPhysics();
		GroundTerrain.UpdatePathingTexture();
	}

	private void ResetEditorUI()
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

		EditorCoordinates.Clear();
		RebuildAllCoordinatePersistentMeshes();

		if (MapEditorHUD.Instance != null)
		{
			MapEditorHUD.Instance.RefreshCoordinateListExternal();
		}
	}

	private void EnsureGroundTerrainExists()
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

		var terrainNode = IsMapEditorMode ? (RuntimeTerrain)new EditableTerrain() : new RuntimeTerrain();
		terrainNode.Name = "Ground";
		AddChild(terrainNode);
		GroundTerrain = terrainNode;
	}

	private void ApplyEditorState(EditorState editor)
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

	private void ApplyTerrainState(TerrainState terrain)
	{
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;

		if (terrain.Cells != null && GroundTerrain != null)
		{
			GroundTerrain.Cells = (Realm.Ecs.Components.Terrain.TerrainCell[,])terrain.Cells.Clone();
		}

		if (terrain.PathingCodes != null && terrain.PathingCodes.Length == width * depth && GroundTerrain != null)
		{
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					GroundTerrain.PathingCodes[x, z] = terrain.PathingCodes[x, z];
				}
			}
		}

		GroundTerrain.UpdateWaterSize();
	}

	private void LoadSplatMaps(string absolutePath)
	{
		int splatW = GroundTerrain.Width + 1;
		int splatD = GroundTerrain.Depth + 1;
		
		string groundIndicesPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(absolutePath), "terrain_splat_indices.exr");
		string groundWeightsPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(absolutePath), "terrain_splat_weights.exr");
		bool groundSplatLoaded = false;

		if (System.IO.File.Exists(groundIndicesPath) && System.IO.File.Exists(groundWeightsPath))
		{
			groundSplatLoaded = TryLoadSplatMapImages(groundIndicesPath, groundWeightsPath, splatW, splatD);
		}

		if (!groundSplatLoaded)
		{
			LoadSplatMapFallback(splatW, splatD);
		}
	}

	private bool TryLoadSplatMapImages(string indicesPath, string weightsPath, int splatW, int splatD)
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
		ReadOnlySpan<float> idxData = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(splatIdxImg.GetData());
		ReadOnlySpan<float> wgtData = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(splatWgtImg.GetData());

		GroundTerrain.SplatMap = new TerrainSplatWeights[splatW, splatD];
		for (int z = 0; z < splatD; z++)
		{
			for (int x = 0; x < splatW; x++)
			{
				int srcIdxX = sW == splatW ? x : System.Math.Clamp((int)System.Math.Floor(x * (float)(sW - 1) / System.Math.Max(1, splatW - 1)), 0, sW - 1);
				int srcIdxZ = sD == splatD ? z : System.Math.Clamp((int)System.Math.Floor(z * (float)(sD - 1) / System.Math.Max(1, splatD - 1)), 0, sD - 1);
				int idxOffset = (srcIdxZ * sW + srcIdxX) * 4;

				int srcWgtX = wgtW == splatW ? x : System.Math.Clamp((int)System.Math.Floor(x * (float)(wgtW - 1) / System.Math.Max(1, splatW - 1)), 0, wgtW - 1);
				int srcWgtZ = wgtD == splatD ? z : System.Math.Clamp((int)System.Math.Floor(z * (float)(wgtD - 1) / System.Math.Max(1, splatD - 1)), 0, wgtD - 1);
				int weightOffset = (srcWgtZ * wgtW + srcWgtX) * 4;

				GroundTerrain.SplatMap[x, z] = new TerrainSplatWeights
				{
					Index0 = (int)System.Math.Round(idxData[idxOffset + 0]),
					Index1 = (int)System.Math.Round(idxData[idxOffset + 1]),
					Index2 = (int)System.Math.Round(idxData[idxOffset + 2]),
					Index3 = (int)System.Math.Round(idxData[idxOffset + 3]),
					Weight0 = wgtData[weightOffset + 0],
					Weight1 = wgtData[weightOffset + 1],
					Weight2 = wgtData[weightOffset + 2],
					Weight3 = wgtData[weightOffset + 3]
				};
			}
		}
		return true;
	}

	private void LoadSplatMapFallback(int splatW, int splatD)
	{
		TerrainColorsState colorsState = default;
		bool foundColors = false;
		var worldQuery = Realm.Ecs.Common.QueryCache.AllTerrainStateAndEditorStateQuery;
		EcsWorld.Query(in worldQuery, (Entity entity) =>
		{
			if (EcsWorld.Has<TerrainColorsState>(entity))
			{
				colorsState = EcsWorld.Get<TerrainColorsState>(entity);
				foundColors = true;
			}
		});

		GroundTerrain.SplatMap = new TerrainSplatWeights[splatW, splatD];
		if (foundColors && colorsState.Colors != null)
		{
			int colorLen = colorsState.Colors.Length;
			int width = GroundTerrain.Width;
			int depth = GroundTerrain.Depth;

			for (int z = 0; z < splatD; z++)
			{
				for (int x = 0; x < splatW; x++)
				{
					int idx;
					if (colorLen == splatW * splatD)
					{
						idx = z * splatW + x;
					}
					else
					{
						int srcX = System.Math.Clamp(x, 0, width - 1);
						int srcZ = System.Math.Clamp(z, 0, depth - 1);
						idx = srcZ * width + srcX;
					}
					
					if (idx < colorLen)
					{
						string serialized = colorsState.Colors[idx];
						GroundTerrain.SplatMap[x, z] = TerrainSplatWeights.Deserialize(serialized);
					}
					else
					{
						GroundTerrain.SplatMap[x, z] = TerrainSplatWeights.CreateSolid(0);
					}
				}
			}
		}
		else
		{
			for (int z = 0; z < splatD; z++)
			{
				for (int x = 0; x < splatW; x++)
				{
					GroundTerrain.SplatMap[x, z] = TerrainSplatWeights.CreateSolid(0);
				}
			}
		}
	}

	private void LoadCliffSplatMaps(string absolutePath)
	{
		if (GroundTerrain == null) return;
		
		int targetCliffW = GroundTerrain.Width + 1;
		int targetCliffD = GroundTerrain.Depth + 1;
		string cliffIndicesPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(absolutePath), "terrain_cliff_splat_indices.exr");
		string cliffWeightsPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(absolutePath), "terrain_cliff_splat_weights.exr");
		
		bool loaded = false;
		if (System.IO.File.Exists(cliffIndicesPath) && System.IO.File.Exists(cliffWeightsPath))
		{
			loaded = TryLoadCliffSplatMapImages(cliffIndicesPath, cliffWeightsPath, targetCliffW, targetCliffD);
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

	private bool TryLoadCliffSplatMapImages(string indicesPath, string weightsPath, int targetCliffW, int targetCliffD)
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
		ReadOnlySpan<float> idxData = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(cliffIdxImg.GetData());
		ReadOnlySpan<float> wgtData = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(cliffWgtImg.GetData());
		
		GroundTerrain.CliffSplatMap = new TerrainSplatWeights[targetCliffW, targetCliffD];
		for (int z = 0; z < targetCliffD; z++)
		{
			for (int x = 0; x < targetCliffW; x++)
			{
				int srcIdxX = cW == targetCliffW ? x : System.Math.Clamp((int)System.Math.Floor(x * (float)(cW - 1) / System.Math.Max(1, targetCliffW - 1)), 0, cW - 1);
				int srcIdxZ = cD == targetCliffD ? z : System.Math.Clamp((int)System.Math.Floor(z * (float)(cD - 1) / System.Math.Max(1, targetCliffD - 1)), 0, cD - 1);
				int idxOffset = (srcIdxZ * cW + srcIdxX) * 4;

				int srcWgtX = wgtW == targetCliffW ? x : System.Math.Clamp((int)System.Math.Floor(x * (float)(wgtW - 1) / System.Math.Max(1, targetCliffW - 1)), 0, wgtW - 1);
				int srcWgtZ = wgtD == targetCliffD ? z : System.Math.Clamp((int)System.Math.Floor(z * (float)(wgtD - 1) / System.Math.Max(1, targetCliffD - 1)), 0, wgtD - 1);
				int weightOffset = (srcWgtZ * wgtW + srcWgtX) * 4;

				GroundTerrain.CliffSplatMap[x, z] = new TerrainSplatWeights
				{
					Index0 = (int)System.Math.Round(idxData[idxOffset + 0]),
					Index1 = (int)System.Math.Round(idxData[idxOffset + 1]),
					Index2 = (int)System.Math.Round(idxData[idxOffset + 2]),
					Index3 = (int)System.Math.Round(idxData[idxOffset + 3]),
					Weight0 = wgtData[weightOffset + 0],
					Weight1 = wgtData[weightOffset + 1],
					Weight2 = wgtData[weightOffset + 2],
					Weight3 = wgtData[weightOffset + 3]
				};
			}
		}
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
		var unitSpawnQuery = Realm.Ecs.Common.QueryCache.AllUnitSpawnRequestQuery;
		var unitRequests = new List<Entity>();
		EcsWorld.Query(in unitSpawnQuery, (Entity entity) => unitRequests.Add(entity));
		foreach (var reqEnt in unitRequests)
		{
			ref var req = ref EcsWorld.Get<UnitSpawnRequest>(reqEnt);
			SpawnUnitExternal(req.UnitId, new Vector3(req.Position.X, req.Position.Y, req.Position.Z), req.IsEnemy, req.RotationY, req.Scale, req.Player);
			EcsWorld.Destroy(reqEnt);
		}
	}

	private void ProcessPropSpawnRequests()
	{
		var propSpawnQuery = Realm.Ecs.Common.QueryCache.AllPropSpawnRequestQuery;
		var propRequests = new List<Entity>();
		EcsWorld.Query(in propSpawnQuery, (Entity entity) => propRequests.Add(entity));
		foreach (var reqEnt in propRequests)
		{
			ref var req = ref EcsWorld.Get<PropSpawnRequest>(reqEnt);
			SpawnPropExternalWithParams(req.PropId, new Vector3(req.Position.X, req.Position.Y, req.Position.Z), req.RotationY, req.Scale);
			EcsWorld.Destroy(reqEnt);
		}
	}

	private void ProcessDecalSpawnRequests()
	{
		var decalSpawnQuery = Realm.Ecs.Common.QueryCache.AllDecalSpawnRequestQuery;
		var decalRequests = new List<Entity>();
		EcsWorld.Query(in decalSpawnQuery, (Entity entity) => decalRequests.Add(entity));
		foreach (var reqEnt in decalRequests)
		{
			ref var req = ref EcsWorld.Get<DecalSpawnRequest>(reqEnt);
			SpawnDecalExternalWithParams(req.DecalId, new Vector3(req.Position.X, req.Position.Y, req.Position.Z), new Vector3(req.RotationX, req.RotationY, req.RotationZ), req.Scale);
			EcsWorld.Destroy(reqEnt);
		}
	}

	private void RestoreLoadedCoordinates()
	{
		var loadedCoordinates = _saveLoadService.GetLastLoadedCoordinates();
		EditorCoordinates.Clear();
		EditorCoordinates.AddRange(loadedCoordinates.Select(r => new EditorCoordinate { Name = r.Name, MinX = r.MinX, MinZ = r.MinZ, MaxX = r.MaxX, MaxZ = r.MaxZ }));
		RebuildAllCoordinatePersistentMeshes();
		MapEditorHUD.Instance?.RefreshCoordinateListExternal();
	}
}
