using Arch.Core;
using Godot;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Terrain;
using Realm.Client;
using Realm.Client.Core;
using Realm.Client.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Realm.Client.Core;

public partial class GameHost
{
	public string CurrentMapDirectory = Realm.Client.Services.MapWorkspaceService.GetDefaultWorkspaceGlobalPath();

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

		string path = string.IsNullOrEmpty(customPath) ? System.IO.Path.Combine(CurrentMapDirectory ?? Realm.Client.Services.MapWorkspaceService.GetDefaultWorkspaceGlobalPath(), "terrain.json") : customPath;
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
				Realm.Client.UI.MapEditorHUD.Instance?.UpdateMapNameHeader();
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
			if (!GodotObject.IsInstanceValid(decal) || !(decal is Realm.Client.Decal3D decal3D)) continue;
			
			EnsureDecalEntity(decal3D);
			float rotY = GetUnitPropRotationY(decal3D.Entity, decal.RotationDegrees.Y);
			float scale = GetDecalScale(decal3D);
			System.Numerics.Vector3 pos = GetDecalPosition(decal3D);
			
			decalsData.Add((decal3D.Entity, pos, rotY, scale));
		}
		return decalsData.ToArray();
	}

	private void EnsureDecalEntity(Realm.Client.Decal3D decal3D)
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

	private float GetDecalScale(Realm.Client.Decal3D decal3D)
	{
		float scale = decal3D.Scale.X != 1.0f 
			? decal3D.Scale.X 
			: (EcsWorld.Has<ModelScale>(decal3D.Entity) ? EcsWorld.Get<ModelScale>(decal3D.Entity).Value : (decal3D.Size.X / 6.0f));
		return GetUnitPropScale(decal3D.Entity, scale);
	}

	private System.Numerics.Vector3 GetDecalPosition(Realm.Client.Decal3D decal3D)
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
		
		Realm.Client.UI.MapEditorHUD.Instance?.SelectToolFromHotkey(savedState.ActiveTool);
		Realm.Client.UI.MapEditorHUD.Instance?.UpdateTextureLabels();
		Realm.Client.UI.MapEditorHUD.Instance?.RefreshWaterSwatches();
		Realm.Client.UI.MapEditorHUD.Instance?.UpdateMapNameHeader();
		Realm.Client.UI.MapEditorHUD.Instance?.ShowFeedback(TranslationServer.Translate("Map saved"));
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
			Realm.Client.UI.MapEditorHUD.Instance?.RegenerateMinimap();
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
		string path = string.IsNullOrEmpty(customPath) ? System.IO.Path.Combine(CurrentMapDirectory ?? Realm.Client.Services.MapWorkspaceService.GetDefaultWorkspaceGlobalPath(), "terrain.json") : customPath;
		string absolutePath = ProjectSettings.GlobalizePath(path);
		CurrentMapDirectory = System.IO.Path.GetDirectoryName(absolutePath);
		if (ensureGlbOptimized)
		{
			Realm.Client.Services.MapWorkspaceService.EnsureGlbAssetsOptimized(CurrentMapDirectory);
			Realm.Client.Services.MapWorkspaceService.EnsurePngAssetsConverted(CurrentMapDirectory);
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
				defaultPathing[x, z] = Realm.Client.EditableTerrain.GetDefaultPathingCode(WaterType.None);
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
		if (Realm.Client.UI.MapEditorHUD.Instance != null)
		{
			Realm.Client.UI.MapEditorHUD.Instance.UpdateBlockModeExternal(true);
			Realm.Client.UI.MapEditorHUD.Instance.UpdateBlockLevelHeightExternal(2.0f);
			Realm.Client.UI.MapEditorHUD.Instance.UpdateCameraBoundsUI();
		}

		UpdateCameraBoundsOverlayVisibility();
		UpdateGridOverlayVisibility();
		UpdatePathingOverlay();

		EditorCoordinates.Clear();
		RebuildAllCoordinatePersistentMeshes();

		if (Realm.Client.UI.MapEditorHUD.Instance != null)
		{
			Realm.Client.UI.MapEditorHUD.Instance.RefreshCoordinateListExternal();
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

		var terrainNode = IsMapEditorMode ? (Realm.Client.RuntimeTerrain)new Realm.Client.EditableTerrain() : new Realm.Client.RuntimeTerrain();
		terrainNode.Name = "Ground";
		AddChild(terrainNode);
		GroundTerrain = terrainNode;
	}

	private void ApplyEditorState(EditorState editor)
	{
		Realm.Client.UI.MapEditorHUD.Instance?.UpdateBlockModeExternal(editor.BlockMode);
		Realm.Client.UI.MapEditorHUD.Instance?.UpdateBlockLevelHeightExternal(editor.BlockLevelHeight);
		Realm.Client.UI.MapEditorHUD.Instance?.UpdateCameraBoundsUI();
		UpdateCameraBoundsOverlayVisibility();
		UpdateGridOverlayVisibility();
		UpdatePathingOverlay();

		if (!string.IsNullOrEmpty(editor.SkyboxPath))
		{
			SetSkyboxTexture(editor.SkyboxPath);
			Realm.Client.UI.MapEditorHUD.Instance?.UpdateSelectedSkyboxExternal(editor.SkyboxPath);
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
		Realm.Client.PropMultiMeshManager.Instance?.RebuildAll();
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
		Realm.Client.UI.MapEditorHUD.Instance?.RefreshCoordinateListExternal();
	}

	private static readonly System.Collections.Generic.HashSet<string> ValidAssetPrefixes = new(System.StringComparer.OrdinalIgnoreCase)
	{
		"unit", "building", "prop", "resource", "item", "ability", 
		"weapon", "upgrade", "terrain", "spritesheet", "decal", "SpawnShader"
	};

	private string ApplyModelAssetExtension(string filename)
	{
		if (filename.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || filename.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
		{
			return System.IO.Path.GetFileNameWithoutExtension(filename) + ".rmesh";
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
		if (_normalizedAssetKeyCache.TryGetValue(pathOrId, out string cached))
		{
			return cached;
		}

		string trimmed = pathOrId.Trim().Replace('\\', '/');
		if (trimmed.Contains('/'))
		{
			string prefix = trimmed.Substring(0, trimmed.IndexOf('/'));
			if (ValidAssetPrefixes.Contains(prefix))
			{
				string lower = trimmed.ToLowerInvariant();
				_normalizedAssetKeyCache[pathOrId] = lower;
				return lower;
			}
		}

		string filename = System.IO.Path.GetFileName(trimmed);
		filename = ApplyModelAssetExtension(filename);
		
		string result = filename.ToLowerInvariant();
		_normalizedAssetKeyCache[pathOrId] = result;
		return result;
	}

	public void FlushModelYOffsetSave()
	{
		if (_modelYOffsetSavePending || _modelCollisionCircleSavePending)
		{
			_modelYOffsetSavePending = false;
			_modelCollisionCircleSavePending = false;
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
			string mapDir = !string.IsNullOrEmpty(directory) ? directory : CurrentMapDirectory;
			if (string.IsNullOrEmpty(mapDir))
			{
				mapDir = Godot.ProjectSettings.GlobalizePath(Realm.Client.UI.MapEditorHUD.TempWorkspaceGodotPath);
			}
			LoadUnitMetadata(mapDir);
			var metaService = _metadataService ?? Realm.Client.Services.MetadataService.Instance;
			var metadata = metaService.LoadMetadata(mapDir);

			ModelYOffsets.Clear();
			ServiceLocator.Get<ModelOverrideService>().ClearAll();

			if (metadata.Templates != null)
			{
				ProcessMetadataEntities(metadata.Templates.Resources, 2.75f, r => r.TemplateID, r => r.ModelPath, r => r.YOffset, r => r.Scale, r => r.CollisionCircle, r => r.Brightness, r => r.Tint, r => r.DespillPlayerColor, r => r.NormalizeLuminance);
				ProcessMetadataEntities(metadata.Templates.Buildings, 1.2f, b => b.TemplateID, b => b.ModelPath, b => b.YOffset, b => b.Scale, b => b.CollisionCircle, b => b.Brightness, b => b.Tint, b => b.DespillPlayerColor, b => b.NormalizeLuminance);
				ProcessMetadataEntities(metadata.Templates.Props, 1.0f, p => p.TemplateID, p => p.ModelPath, p => p.YOffset, p => p.Scale, p => p.CollisionCircle, p => p.Brightness, p => p.Tint, p => p.DespillPlayerColor, p => p.NormalizeLuminance);
				ProcessMetadataEntities(metadata.Templates.Units, 1.5f, u => u.TemplateID, u => u.ModelPath, u => u.YOffset, u => u.Scale, u => u.CollisionCircle, u => u.Brightness, u => u.Tint, u => u.DespillPlayerColor, u => u.NormalizeLuminance);
			}

			if (metadata.Models != null)
			{
				foreach (var kvp in metadata.Models)
				{
					ProcessModelMetadata(kvp.Key, kvp.Value);
				}
			}

			UpdateAllMaterialOverrides();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to load metadata overrides from JSON: {ex.Message}");
		}
	}
	private void ProcessMetadataEntities<T>(IEnumerable<T> items, float defaultScale, Func<T, string> getId, Func<T, string> getModelPath, Func<T, float> getYOffset, Func<T, float> getScale, Func<T, float> getCollisionCircle, Func<T, float> getBrightness, Func<T, string> getTint, Func<T, bool> getDespillPlayerColor, Func<T, bool> getNormalizeLuminance)
	{
		if (items == null) return;
		foreach (var item in items)
		{
			string uId = getId(item);
			if (string.IsNullOrEmpty(uId)) continue;
			string normKey = NormalizeModelAssetKey(uId);
			
			ModelYOffsets[normKey] = getYOffset(item);
			
			float sVal = getScale(item);
			if (sVal > 0f)
			{
				ModelScales[normKey] = sVal;
			}
			else if (!ModelScales.ContainsKey(normKey))
			{
				ModelScales[normKey] = defaultScale;
			}

			string mPath = getModelPath(item);
			if (!string.IsNullOrEmpty(mPath))
			{
				string normModel = NormalizeModelAssetKey(mPath);
				if (ModelScales.TryGetValue(normKey, out float assignedScale) && !ModelScales.ContainsKey(normModel))
				{
					ModelScales[normModel] = assignedScale;
				}
			}

			ModelCollisionCircleRatios[normKey] = getCollisionCircle(item);
			ModelBrightness[normKey] = getBrightness(item);
			
			string tintStr = getTint(item);
			if (!string.IsNullOrEmpty(tintStr))
			{
				ModelColorTint[normKey] = Color.FromString(tintStr, new Color(1, 1, 1));
			}
			
			ModelDespillPlayerColor[normKey] = getDespillPlayerColor(item);
			ModelNormalizeLuminance[normKey] = getNormalizeLuminance(item);
		}
	}

	private void ProcessModelPhysicalMetadata(string key, string normKey, Realm.Shared.Metadata.ModelMetadata model)
	{
		TrySetModelYOffset(key, normKey, model);
		TrySetModelScale(key, normKey, model);
		TrySetModelCollisionCircleRatio(key, normKey, model);
		TrySetModelObstacleRadius(normKey, model);
		TrySetModelProceduralAnimation(normKey, model);
	}

	private void TrySetModelYOffset(string key, string normKey, Realm.Shared.Metadata.ModelMetadata model)
	{
		if (model.Offsets.HasValue && IsValidModelYOffset(key, model.Offsets.Value))
			ModelYOffsets[normKey] = model.Offsets.Value;
	}

	private void TrySetModelScale(string key, string normKey, Realm.Shared.Metadata.ModelMetadata model)
	{
		if (model.Scales.HasValue && IsValidModelScale(key, model.Scales.Value))
			ModelScales[normKey] = model.Scales.Value;
	}

	private void TrySetModelCollisionCircleRatio(string key, string normKey, Realm.Shared.Metadata.ModelMetadata model)
	{
		if (model.CollisionCircleRatios.HasValue && IsValidModelCollisionRatio(key, model.CollisionCircleRatios.Value))
			ModelCollisionCircleRatios[normKey] = model.CollisionCircleRatios.Value;
	}

	private void TrySetModelObstacleRadius(string normKey, Realm.Shared.Metadata.ModelMetadata model)
	{
		if (model.ObstacleRadii.HasValue && model.ObstacleRadii.Value > 0f)
			ModelObstacleRadii[normKey] = model.ObstacleRadii.Value;
	}

	private void TrySetModelProceduralAnimation(string normKey, Realm.Shared.Metadata.ModelMetadata model)
	{
		if (!string.IsNullOrWhiteSpace(model.ProceduralAnimation))
			ModelProceduralAnimations[normKey] = model.ProceduralAnimation.Trim();

		if (model.EnableProceduralAnimation.HasValue)
			ModelEnableProceduralAnimations[normKey] = model.EnableProceduralAnimation.Value;
	}

	private void ProcessModelVisualMetadata(string normKey, Realm.Shared.Metadata.ModelMetadata model)
	{
		if (!string.IsNullOrWhiteSpace(model.SpawnShaders)) ModelSpawnShaders[normKey] = model.SpawnShaders.Trim();
		if (!string.IsNullOrWhiteSpace(model.DeathShaders)) ModelDeathShaders[normKey] = model.DeathShaders.Trim();
		if (model.Brightness.HasValue) ModelBrightness[normKey] = model.Brightness.Value;
		if (!string.IsNullOrWhiteSpace(model.ColorTint) && Color.HtmlIsValid(model.ColorTint)) ModelColorTint[normKey] = Color.FromHtml(model.ColorTint);
		if (model.DespillPlayerColor.HasValue) ModelDespillPlayerColor[normKey] = model.DespillPlayerColor.Value;
		if (model.NormalizeLuminance.HasValue) ModelNormalizeLuminance[normKey] = model.NormalizeLuminance.Value;
		if (model.IgnorePlayerColor.HasValue) ModelIgnorePlayerColor[normKey] = model.IgnorePlayerColor.Value;
	}

	private void ProcessModelMetadata(string key, Realm.Shared.Metadata.ModelMetadata model)
	{
		if (model == null) return;
		string normKey = NormalizeModelAssetKey(key);

		ProcessModelPhysicalMetadata(key, normKey, model);
		ProcessModelVisualMetadata(normKey, model);
	}

	public void SaveModelYOffsetsToMetadataJson(string directory = null)
	{
		try
		{
			string mapDir = !string.IsNullOrEmpty(directory) ? directory : CurrentMapDirectory;
			if (string.IsNullOrEmpty(mapDir)) mapDir = Godot.ProjectSettings.GlobalizePath(Realm.Client.UI.MapEditorHUD.TempWorkspaceGodotPath);

			var metaService = _metadataService ?? Realm.Client.Services.MetadataService.Instance;
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
				_decalAssetCache[cacheKey] = resData;
				return resData;
			}
		}
		catch { }
		return null;
	}

	private Realm.Shared.Metadata.DecalMetadata? FindDecalMetadata(Realm.Shared.Metadata.MapMetadata metadata, string decalId, string filename, string baseKey)
	{
		if (metadata?.Decals == null) return null;
		if (metadata.Decals.TryGetValue(decalId, out var d0)) return d0;
		if (metadata.Decals.TryGetValue(filename, out var d1)) return d1;
		if (metadata.Decals.TryGetValue(baseKey, out var d2)) return d2;
		if (metadata.Decals.TryGetValue($"{baseKey}.rtex", out var d3)) return d3;
		if (metadata.Decals.TryGetValue($"{baseKey}.png", out var d4)) return d4;
		return null;
	}

	private Realm.Shared.Metadata.VfxMetadata? FindVfxMetadata(Realm.Shared.Metadata.MapMetadata metadata, string decalId, string filename, string baseKey)
	{
		if (metadata?.VfxSpritesheets == null) return null;
		if (metadata.VfxSpritesheets.TryGetValue(decalId, out var v0)) return v0;
		if (metadata.VfxSpritesheets.TryGetValue(filename, out var v1)) return v1;
		if (metadata.VfxSpritesheets.TryGetValue(baseKey, out var v2)) return v2;
		return null;
	}

	private (int cols, int rows, float fps, bool blend, string? explicitPath) ParseDecalMetadata(string decalId, string filename, string baseKey, string wsPath)
	{
		int cols = 1, rows = 1;
		float fps = 12.0f;
		bool blend = true;
		string? explicitPath = null;
		
		try
		{
			var metadata = Realm.Shared.Services.MapFileService.LoadMetadata(wsPath);
			if (metadata != null)
			{
				var meta = FindDecalMetadata(metadata, decalId, filename, baseKey);
				if (meta != null && !string.IsNullOrWhiteSpace(meta.TexturePath)) explicitPath = meta.TexturePath;

				var vmeta = FindVfxMetadata(metadata, decalId, filename, baseKey);
				if (vmeta != null)
				{
					if (vmeta.Columns > 0) cols = vmeta.Columns;
					if (vmeta.Rows > 0) rows = vmeta.Rows;
					if (vmeta.Fps > 0.001f) fps = vmeta.Fps;
					blend = vmeta.SubframeBlend;
				}
			}
		}
		catch { }
		
		return (cols, rows, fps, blend, explicitPath);
	}

	private List<string> GetDecalCandidatePaths(string decalId, string? explicitTexturePath, string wsPath)
	{
		var candidatePaths = new List<string>();

		void AddCandidates(string pathOrName)
		{
			if (string.IsNullOrWhiteSpace(pathOrName)) return;
			string fName = System.IO.Path.GetFileName(pathOrName);
			if (System.IO.Path.IsPathRooted(pathOrName))
			{
				candidatePaths.Add(pathOrName);
				return;
			}
			
			candidatePaths.Add(System.IO.Path.Combine(wsPath, "Assets", "decals", pathOrName));
			candidatePaths.Add(System.IO.Path.Combine(wsPath, "Assets", "decals", fName));
			candidatePaths.Add(System.IO.Path.Combine(wsPath, pathOrName));
			if (!fName.Contains('.'))
			{
				candidatePaths.Add(System.IO.Path.Combine(wsPath, "Assets", "decals", fName + ".rtex"));
				candidatePaths.Add(System.IO.Path.Combine(wsPath, "Assets", "decals", fName + ".webp"));
				candidatePaths.Add(System.IO.Path.Combine(wsPath, "Assets", "decals", fName + ".png"));
			}
		}

		if (!string.IsNullOrEmpty(explicitTexturePath)) AddCandidates(explicitTexturePath);
		AddCandidates(decalId);
		return candidatePaths;
	}
	
	private bool LoadRtexDecalImages(string path, out Image? albedoImg, out Image? normalImg, ref int cols, ref int rows)
	{
		albedoImg = null;
		normalImg = null;
		byte[] rtexBytes = System.IO.File.ReadAllBytes(path);
		var (customJson, layers, _) = Realm.Shared.Textures.RtexFile.Parse(rtexBytes);
		
		ParseRtexDecalMetadata(customJson, ref cols, ref rows);
		
		albedoImg = LoadRtexDecalLayer(layers, 0);
		normalImg = LoadRtexDecalLayer(layers, 1);
		
		return albedoImg != null;
	}

	private void ParseRtexDecalMetadata(string customJson, ref int cols, ref int rows)
	{
		if (string.IsNullOrEmpty(customJson)) return;
		try
		{
			if (System.Text.Json.Nodes.JsonNode.Parse(customJson) is not System.Text.Json.Nodes.JsonObject jNode) return;
			
			if (TryGetPositiveIntFromJson(jNode, "columns", out int c))
				cols = c;
				
			if (TryGetPositiveIntFromJson(jNode, "rows", out int r))
				rows = r;
		}
		catch { }
	}

	private Image? LoadRtexDecalLayer(System.Collections.Generic.IReadOnlyList<byte[]> layers, int layerIndex)
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
		string? meta = Realm.Shared.Metadata.RealmMetadataHelper.ExtractMetadata(path);
		if (string.IsNullOrEmpty(meta)) return albedoImg != null;
		
		ParseExternalDecalMetadata(meta, ref cols, ref rows);
		
		return albedoImg != null;
	}

	private void ParseExternalDecalMetadata(string meta, ref int cols, ref int rows)
	{
		if (string.IsNullOrEmpty(meta)) return;
		try
		{
			if (System.Text.Json.Nodes.JsonNode.Parse(meta) is not System.Text.Json.Nodes.JsonObject jNode) return;
			
			if (TryGetPositiveIntFromJson(jNode, "columns", out int c))
				cols = c;
				
			if (TryGetPositiveIntFromJson(jNode, "rows", out int r))
				rows = r;
		}
		catch { }
	}

	private bool TryGetPositiveIntFromJson(System.Text.Json.Nodes.JsonObject obj, string key, out int value)
	{
		value = 0;
		if (!obj.TryGetPropertyValue(key, out var node)) return false;
		if (node == null) return false;
		if (!int.TryParse(node.ToString(), out int parsed)) return false;
		if (parsed <= 0) return false;
		value = parsed;
		return true;
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

				_decalAssetCache[cacheKey] = assetData;
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
		
		if (!forceReload && !HasDecalOverrides(overrideCols, overrideRows, overrideFps, overrideSubframeBlend))
		{
			if (TryGetValidCachedDecal(cacheKey, out var cachedData))
				return cachedData;
		}

		var resDecal = TryLoadDecalFromResource(decalId, cacheKey);
		if (resDecal != null) return resDecal;

		string filename = System.IO.Path.GetFileName(decalId);
		string baseKey = System.IO.Path.GetFileNameWithoutExtension(decalId);
		string wsPath = ProjectSettings.GlobalizePath(Realm.Client.UI.MapEditorHUD.TempWorkspaceGodotPath);

		var meta = ParseDecalMetadata(decalId, filename, baseKey, wsPath);
		var loaded = TryLoadDecalFromCandidates(decalId, meta.cols, meta.rows, meta.explicitPath, wsPath, cacheKey);
		if (loaded != null) return loaded;

		return CreateFallbackDecal(decalId, meta.explicitPath, cacheKey);
	}

	private bool HasDecalOverrides(int overrideCols, int overrideRows, float overrideFps, bool? overrideSubframeBlend)
	{
		return overrideCols > 0 || overrideRows > 0 || overrideFps > 0.001f || overrideSubframeBlend.HasValue;
	}

	private bool TryGetValidCachedDecal(string cacheKey, out DecalAssetData cachedData)
	{
		cachedData = null;
		if (_decalAssetCache.TryGetValue(cacheKey, out var data) && data != null && GodotObject.IsInstanceValid(data.PrimaryTexture))
		{
			cachedData = data;
			return true;
		}
		return false;
	}

	private DecalAssetData TryLoadDecalFromCandidates(string decalId, int cols, int rows, string? explicitPath, string wsPath, string cacheKey)
	{
		var candidatePaths = GetDecalCandidatePaths(decalId, explicitPath, wsPath);
		foreach (var path in candidatePaths)
		{
			if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) continue;
			
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
		_decalAssetCache[cacheKey] = fallback;
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

		string wsPath = ProjectSettings.GlobalizePath(Realm.Client.UI.MapEditorHUD.TempWorkspaceGodotPath);
		string targetKey = ResolveDecalKeyFromMetadata(decalId, wsPath);

		return FindDecalFile(targetKey, wsPath);
	}

	private string ResolveDecalKeyFromMetadata(string decalId, string wsPath)
	{
		try
		{
			var metadata = Realm.Shared.Services.MapFileService.LoadMetadata(wsPath);
			if (metadata?.Decals != null)
			{
				string filename = System.IO.Path.GetFileName(decalId);
				string baseKey = System.IO.Path.GetFileNameWithoutExtension(decalId);
				Realm.Shared.Metadata.DecalMetadata meta = null;

				if (metadata.Decals.TryGetValue(decalId, out var d0)) meta = d0;
				else if (metadata.Decals.TryGetValue(filename, out var d1)) meta = d1;
				else if (metadata.Decals.TryGetValue(baseKey, out var d2)) meta = d2;

				if (meta != null && !string.IsNullOrWhiteSpace(meta.TexturePath))
				{
					return meta.TexturePath;
				}
			}
		}
		catch { }
		return decalId;
	}

	private string FindDecalFile(string targetKey, string wsPath)
	{
		string targetFilename = System.IO.Path.GetFileName(targetKey);
		string candidate1 = System.IO.Path.Combine(wsPath, "Assets", "decals", targetFilename);
		if (System.IO.File.Exists(candidate1)) return candidate1;

		if (!targetFilename.Contains('.'))
		{
			string candidate2 = System.IO.Path.Combine(wsPath, "Assets", "decals", targetFilename + ".png");
			if (System.IO.File.Exists(candidate2)) return candidate2;
			string candidateRtex = System.IO.Path.Combine(wsPath, "Assets", "decals", targetFilename + ".rtex");
			if (System.IO.File.Exists(candidateRtex)) return candidateRtex;
		}

		if (System.IO.Path.IsPathRooted(targetKey) && System.IO.File.Exists(targetKey)) return targetKey;

		string candidate3 = System.IO.Path.Combine(wsPath, targetKey);
		if (System.IO.File.Exists(candidate3)) return candidate3;

		return "res://icon.svg";
	}
}
