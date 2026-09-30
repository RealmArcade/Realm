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

		byte savedWaterProfile = ActiveWaterProfileIndex;
		WaterType savedWaterMode = EditorWaterMode;
		float savedWaterHeight = EditorWaterHeight;
		bool savedBlockMode = EditorBlockMode;
		float savedBlockLevelHeight = EditorBlockLevelHeight;
		float savedExactHeight = EditorExactHeight;

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
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

		var unitsData = new List<(Entity Entity, float RotationY, float Scale)>();
		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit) && EcsWorld.IsAlive(unit.Entity))
			{
				float rotY = unit.RotationDegrees.Y;
				if (EcsWorld.Has<RotationY>(unit.Entity))
				{
					float existingRotY = EcsWorld.Get<RotationY>(unit.Entity).Value;
					if (MathF.Abs(rotY - existingRotY) < 0.001f || MathF.Abs(MathF.Abs(rotY - existingRotY) - 360f) < 0.001f)
					{
						rotY = existingRotY;
					}
				}

				float scale = unit.Scale.X;
				if (EcsWorld.Has<ModelScale>(unit.Entity))
				{
					float existingScale = EcsWorld.Get<ModelScale>(unit.Entity).Value;
					if (MathF.Abs(scale - existingScale) < 0.0001f)
					{
						scale = existingScale;
					}
				}

				unitsData.Add((unit.Entity, rotY, scale));
			}
		}

		var propsData = new List<(Entity Entity, float RotationY, float Scale)>();
		foreach (var prop in AllProps)
		{
			if (GodotObject.IsInstanceValid(prop) && EcsWorld.IsAlive(prop.Entity))
			{
				float rotY = prop.RotationDegrees.Y;
				if (EcsWorld.Has<RotationY>(prop.Entity))
				{
					float existingRotY = EcsWorld.Get<RotationY>(prop.Entity).Value;
					if (MathF.Abs(rotY - existingRotY) < 0.001f || MathF.Abs(MathF.Abs(rotY - existingRotY) - 360f) < 0.001f)
					{
						rotY = existingRotY;
					}
				}

				float scale = prop.Scale.X;
				if (EcsWorld.Has<ModelScale>(prop.Entity))
				{
					float existingScale = EcsWorld.Get<ModelScale>(prop.Entity).Value;
					if (MathF.Abs(scale - existingScale) < 0.0001f)
					{
						scale = existingScale;
					}
				}

				propsData.Add((prop.Entity, rotY, scale));
			}
		}

		var decalsData = new List<(Entity Entity, System.Numerics.Vector3 Position, float RotationY, float Scale)>();
		foreach (var decal in AllDecals)
		{
			if (GodotObject.IsInstanceValid(decal) && decal is Decal3D decal3D)
			{
				if (!EcsWorld.IsAlive(decal3D.Entity))
				{
					var newEnt = EcsWorld.Create();
					decal3D.Entity = newEnt;
					EcsWorld.Add(newEnt, new DecalIdentity(decal3D.DecalId));
					EcsWorld.Add(newEnt, new Position(new System.Numerics.Vector3(decal.Position.X, decal.Position.Y, decal.Position.Z)));
					EcsWorld.Add(newEnt, new RotationY(decal.RotationDegrees.Y));
					EcsWorld.Add(newEnt, new ModelScale(decal.Scale.X));
				}

				float rotY = decal.RotationDegrees.Y;
				if (EcsWorld.Has<RotationY>(decal3D.Entity))
				{
					float existingRotY = EcsWorld.Get<RotationY>(decal3D.Entity).Value;
					if (MathF.Abs(rotY - existingRotY) < 0.001f || MathF.Abs(MathF.Abs(rotY - existingRotY) - 360f) < 0.001f)
					{
						rotY = existingRotY;
					}
				}

				float scale = decal.Scale.X != 1.0f
					? decal.Scale.X
					: (EcsWorld.Has<ModelScale>(decal3D.Entity) ? EcsWorld.Get<ModelScale>(decal3D.Entity).Value : (decal.Size.X / 6.0f));
				if (EcsWorld.Has<ModelScale>(decal3D.Entity))
				{
					float existingScale = EcsWorld.Get<ModelScale>(decal3D.Entity).Value;
					if (MathF.Abs(scale - existingScale) < 0.0001f)
					{
						scale = existingScale;
					}
				}

				var pos = new System.Numerics.Vector3(decal.Position.X, decal.Position.Y, decal.Position.Z);
				if (EcsWorld.Has<Position>(decal3D.Entity))
				{
					var existingPos = EcsWorld.Get<Position>(decal3D.Entity).Value;
					if ((pos - existingPos).Length() < 0.0001f)
					{
						pos = existingPos;
					}
				}

				decalsData.Add((decal3D.Entity, pos, rotY, scale));
			}
		}

		var coordinatesData = EditorCoordinates.Select(r => new CoordinateSaveData
		{
			Name = r.Name,
			MinX = r.MinX,
			MinZ = r.MinZ,
			MaxX = r.MaxX,
			MaxZ = r.MaxZ
		}).ToList();

		string path = string.IsNullOrEmpty(customPath) ? System.IO.Path.Combine(CurrentMapDirectory ?? MapWorkspaceService.GetDefaultWorkspaceGlobalPath(), "terrain.json") : customPath;
		string absolutePath = ProjectSettings.GlobalizePath(path);
		CurrentMapDirectory = System.IO.Path.GetDirectoryName(absolutePath);

		string[] cliffSplatData = null;
		if (GroundTerrain.CliffSplatMap != null)
		{
			int cliffW = GroundTerrain.CliffSplatMap.GetLength(0);
			int cliffD = GroundTerrain.CliffSplatMap.GetLength(1);
			cliffSplatData = new string[cliffW * cliffD];
			for (int z = 0; z < cliffD; z++)
			{
				for (int x = 0; x < cliffW; x++)
				{
					cliffSplatData[z * cliffW + x] = GroundTerrain.CliffSplatMap[x, z].Serialize();
				}
			}
		}

		bool success = _saveLoadService.SaveMapToFile(absolutePath, splatData, unitsData.ToArray(), propsData.ToArray(), decalsData.ToArray(), coordinatesData, cliffSplatData);
		if (success)
		{
			if (performReload)
			{
				LoadMapFromFile(absolutePath, terrainOnly: false, clearUnits: true, ensureGlbOptimized: false);
				ActiveWaterProfileIndex = savedWaterProfile;
				EditorWaterMode = savedWaterMode;
				EditorWaterHeight = savedWaterHeight;
				EditorBlockMode = savedBlockMode;
				EditorBlockLevelHeight = savedBlockLevelHeight;
				EditorExactHeight = savedExactHeight;
				MapEditorHUD.Instance?.RefreshWaterSwatches();
				MapEditorHUD.Instance?.UpdateMapNameHeader();
				MapEditorHUD.Instance?.ShowFeedback(TranslationServer.Translate("Map saved"));
			}
			else
			{
				MapEditorHUD.Instance?.UpdateMapNameHeader();
			}
		}
	}

	public bool LoadMapFromFile(string customPath = "", bool terrainOnly = false, bool clearUnits = true, bool ensureGlbOptimized = true)
	{
		IsLoadingMap = true;
		try
		{
			string path = string.IsNullOrEmpty(customPath) ? System.IO.Path.Combine(CurrentMapDirectory ?? MapWorkspaceService.GetDefaultWorkspaceGlobalPath(), "terrain.json") : customPath;
			string absolutePath = ProjectSettings.GlobalizePath(path);
			CurrentMapDirectory = System.IO.Path.GetDirectoryName(absolutePath);
			if (ensureGlbOptimized)
			{
				MapWorkspaceService.EnsureGlbAssetsOptimized(CurrentMapDirectory);
				MapWorkspaceService.EnsurePngAssetsConverted(CurrentMapDirectory);
			}

			if (clearUnits)
			{
				ClearAllUnits();
			}

			bool success = _saveLoadService.LoadMapFromFile(absolutePath, terrainOnly);
			if (!success)
			{
				int defaultWidth = 128;
				int defaultDepth = 128;
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
				if (EcsWorld.Has<TerrainState>(_worldEntity))
				{
					ref var ts = ref EcsWorld.Get<TerrainState>(_worldEntity);
					ts.SetHeights(defaultHeights);
					ts.PathingCodes = defaultPathing;
					EcsWorld.Set(_worldEntity, ts);
				}
				if (GroundTerrain != null)
				{
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
				MapEditorHUD.Instance?.UpdateBlockModeExternal(true);
				MapEditorHUD.Instance?.UpdateBlockLevelHeightExternal(2.0f);
				MapEditorHUD.Instance?.UpdateCameraBoundsUI();
				UpdateCameraBoundsOverlayVisibility();
				UpdateGridOverlayVisibility();
				UpdatePathingOverlay();

				EditorCoordinates.Clear();
				RebuildAllCoordinatePersistentMeshes();
				MapEditorHUD.Instance?.RefreshCoordinateListExternal();

				return false;
			}

			if (GroundTerrain == null)
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

				var terrainNode = IsMapEditorMode ? (RuntimeTerrain)new EditableTerrain() : new RuntimeTerrain();
				terrainNode.Name = "Ground";
				AddChild(terrainNode);
				GroundTerrain = terrainNode;
			}

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

			int splatW = width + 1;
			int splatD = depth + 1;
			if (GroundTerrain.SplatMap == null || GroundTerrain.SplatMap.GetLength(0) != splatW || GroundTerrain.SplatMap.GetLength(1) != splatD)
			{
				GroundTerrain.SplatMap = new TerrainSplatWeights[splatW, splatD];
				for (int z = 0; z < splatD; z++)
				{
					for (int x = 0; x < splatW; x++)
					{
						GroundTerrain.SplatMap[x, z] = TerrainSplatWeights.CreateSolid(0);
					}
				}
			}
			GroundTerrain.UpdateWaterSize();

			TerrainColorsState colorsState = default;
			bool foundColors = false;
			EcsWorld.Query(in worldQuery, (Entity entity) =>
			{
				if (EcsWorld.Has<TerrainColorsState>(entity))
				{
					colorsState = EcsWorld.Get<TerrainColorsState>(entity);
					foundColors = true;
				}
			});

			if (foundColors && colorsState.Colors != null)
			{
				int colorLen = colorsState.Colors.Length;

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
					}
				}
			}

			if (GroundTerrain != null)
			{
				string cliffIndicesPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(absolutePath), "terrain_cliff_splat_indices.exr");
				string cliffWeightsPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(absolutePath), "terrain_cliff_splat_weights.exr");
				if (System.IO.File.Exists(cliffIndicesPath) && System.IO.File.Exists(cliffWeightsPath))
				{
					Image cliffIdxImg = Image.LoadFromFile(cliffIndicesPath);
					Image cliffWgtImg = Image.LoadFromFile(cliffWeightsPath);
					if (cliffIdxImg != null && cliffWgtImg != null)
					{
						cliffIdxImg.Convert(Image.Format.Rgbaf);
						cliffWgtImg.Convert(Image.Format.Rgbaf);
						int cW = cliffIdxImg.GetWidth();
						int cD = cliffIdxImg.GetHeight();
						ReadOnlySpan<float> idxData = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(cliffIdxImg.GetData());
						ReadOnlySpan<float> wgtData = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(cliffWgtImg.GetData());
						int targetCliffW = width + 1;
						int targetCliffD = depth + 1;
						GroundTerrain.CliffSplatMap = new TerrainSplatWeights[targetCliffW, targetCliffD];
						for (int z = 0; z < targetCliffD; z++)
						{
							for (int x = 0; x < targetCliffW; x++)
							{
								int srcX = cW == targetCliffW ? x : System.Math.Clamp((int)System.Math.Floor(x * (float)(cW - 1) / System.Math.Max(1, targetCliffW - 1)), 0, cW - 1);
								int srcZ = cD == targetCliffD ? z : System.Math.Clamp((int)System.Math.Floor(z * (float)(cD - 1) / System.Math.Max(1, targetCliffD - 1)), 0, cD - 1);
								int baseIdx = (srcZ * cW + srcX) * 4;
								GroundTerrain.CliffSplatMap[x, z] = new TerrainSplatWeights
								{
									Index0 = (int)System.Math.Round(idxData[baseIdx + 0]),
									Index1 = (int)System.Math.Round(idxData[baseIdx + 1]),
									Index2 = (int)System.Math.Round(idxData[baseIdx + 2]),
									Index3 = (int)System.Math.Round(idxData[baseIdx + 3]),
									Weight0 = wgtData[baseIdx + 0],
									Weight1 = wgtData[baseIdx + 1],
									Weight2 = wgtData[baseIdx + 2],
									Weight3 = wgtData[baseIdx + 3]
								};
							}
						}
					}
				}
				else
				{
					int cliffW = width + 1;
					int cliffD = depth + 1;
					GroundTerrain.CliffSplatMap = new TerrainSplatWeights[cliffW, cliffD];
					for (int z = 0; z < cliffD; z++)
					{
						for (int x = 0; x < cliffW; x++)
						{
							GroundTerrain.CliffSplatMap[x, z] = TerrainSplatWeights.CreateSolid(1);
						}
					}
				}

				_editorService?.SetTerrainSplatMap(GroundTerrain.SplatMap, GroundTerrain.CliffSplatMap);
			}

			AlignTerrainSplatMapExternal();

			if (!terrainOnly)
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

				var propSpawnQuery = Realm.Ecs.Common.QueryCache.AllPropSpawnRequestQuery;
				var propRequests = new List<Entity>();
				EcsWorld.Query(in propSpawnQuery, (Entity entity) => propRequests.Add(entity));
				foreach (var reqEnt in propRequests)
				{
					ref var req = ref EcsWorld.Get<PropSpawnRequest>(reqEnt);
					SpawnPropExternalWithParams(req.PropId, new Vector3(req.Position.X, req.Position.Y, req.Position.Z), req.RotationY, req.Scale);
					EcsWorld.Destroy(reqEnt);
				}
				PropMultiMeshManager.Instance?.RebuildAll();

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

			GroundTerrain.UpdateMeshAndPhysics(true, true);

			MapEditorHUD.Instance?.RegenerateMinimap();

			var loadedCoordinates = _saveLoadService.GetLastLoadedCoordinates();
			EditorCoordinates.Clear();
			EditorCoordinates.AddRange(loadedCoordinates.Select(r => new EditorCoordinate { Name = r.Name, MinX = r.MinX, MinZ = r.MinZ, MaxX = r.MaxX, MaxZ = r.MaxZ }));
			RebuildAllCoordinatePersistentMeshes();
			MapEditorHUD.Instance?.RefreshCoordinateListExternal();

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
}
