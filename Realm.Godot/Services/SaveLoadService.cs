using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Arch.Core;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Godot.Utils;
using Realm.Godot.VFX;
using Realm.Shared.Metadata;
using Realm.Godot.Services;

public class SaveLoadService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World EcsWorld => _ecsWorldAccessor.Current;

	private List<CoordinateSaveData> _lastLoadedCoordinates = new();

	public SaveLoadService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	public List<CoordinateSaveData> GetLastLoadedCoordinates() => _lastLoadedCoordinates;

	public bool SaveMapToFile(
		string absolutePath,
		string[] htmlColors,
		(Entity Entity, float RotationY, float Scale)[] unitsData,
		(Entity Entity, float RotationY, float Scale)[] propsData,
		(Entity Entity, System.Numerics.Vector3 Position, float RotationY, float Scale)[] decalsData,
		List<CoordinateSaveData> coordinatesData = null,
		string[] cliffHtmlColors = null)
	{
		try
		{
			Entity worldEntity = Entity.Null;
			var worldQuery = Realm.Ecs.Common.QueryCache.AllTerrainStateQuery;
			EcsWorld.Query(in worldQuery, (Entity entity) => worldEntity = entity);

			if (worldEntity != Entity.Null)
			{
				EcsWorld.SetOrAdd(worldEntity, new TerrainColorsState(htmlColors));
			}

			foreach (var u in unitsData)
			{
				if (EcsWorld.IsAlive(u.Entity))
				{
					if (EcsWorld.Has<RotationY>(u.Entity))
					{
						float existing = EcsWorld.Get<RotationY>(u.Entity).Value;
						if (MathF.Abs(existing - u.RotationY) > 0.001f && MathF.Abs(MathF.Abs(existing - u.RotationY) - 360f) > 0.001f)
						{
							EcsWorld.Set(u.Entity, new RotationY(u.RotationY));
						}
					}
					else
					{
						EcsWorld.Add(u.Entity, new RotationY(u.RotationY));
					}

					if (EcsWorld.Has<ModelScale>(u.Entity))
					{
						float existing = EcsWorld.Get<ModelScale>(u.Entity).Value;
						if (MathF.Abs(existing - u.Scale) > 0.0001f)
						{
							EcsWorld.Set(u.Entity, new ModelScale(u.Scale));
						}
					}
					else
					{
						EcsWorld.Add(u.Entity, new ModelScale(u.Scale));
					}
				}
			}

			foreach (var p in propsData)
			{
				if (EcsWorld.IsAlive(p.Entity))
				{
					if (EcsWorld.Has<RotationY>(p.Entity))
					{
						float existing = EcsWorld.Get<RotationY>(p.Entity).Value;
						if (MathF.Abs(existing - p.RotationY) > 0.001f && MathF.Abs(MathF.Abs(existing - p.RotationY) - 360f) > 0.001f)
						{
							EcsWorld.Set(p.Entity, new RotationY(p.RotationY));
						}
					}
					else
					{
						EcsWorld.Add(p.Entity, new RotationY(p.RotationY));
					}

					if (EcsWorld.Has<ModelScale>(p.Entity))
					{
						float existing = EcsWorld.Get<ModelScale>(p.Entity).Value;
						if (MathF.Abs(existing - p.Scale) > 0.0001f)
						{
							EcsWorld.Set(p.Entity, new ModelScale(p.Scale));
						}
					}
					else
					{
						EcsWorld.Add(p.Entity, new ModelScale(p.Scale));
					}
				}
			}

			var validDecalEntities = decalsData != null
				? new HashSet<Entity>(decalsData.Select(d => d.Entity))
				: null;

			if (decalsData != null)
			{
				foreach (var d in decalsData)
				{
					if (EcsWorld.IsAlive(d.Entity))
					{
						if (EcsWorld.Has<Position>(d.Entity))
						{
							var existingPos = EcsWorld.Get<Position>(d.Entity).Value;
							if ((d.Position - existingPos).Length() > 0.0001f)
							{
								EcsWorld.Set(d.Entity, new Position(d.Position));
							}
						}
						else
						{
							EcsWorld.Add(d.Entity, new Position(d.Position));
						}

						if (EcsWorld.Has<RotationY>(d.Entity))
						{
							float existing = EcsWorld.Get<RotationY>(d.Entity).Value;
							if (MathF.Abs(existing - d.RotationY) > 0.001f && MathF.Abs(MathF.Abs(existing - d.RotationY) - 360f) > 0.001f)
							{
								EcsWorld.Set(d.Entity, new RotationY(d.RotationY));
							}
						}
						else
						{
							EcsWorld.Add(d.Entity, new RotationY(d.RotationY));
						}

						if (EcsWorld.Has<ModelScale>(d.Entity))
						{
							float existing = EcsWorld.Get<ModelScale>(d.Entity).Value;
							if (MathF.Abs(existing - d.Scale) > 0.0001f)
							{
								EcsWorld.Set(d.Entity, new ModelScale(d.Scale));
							}
						}
						else
						{
							EcsWorld.Add(d.Entity, new ModelScale(d.Scale));
						}
					}
				}
			}

			TerrainState terrain = default;
			EditorState editor = default;
			bool foundWorld = false;

			var worldQuery2 = Realm.Ecs.Common.QueryCache.AllTerrainStateAndEditorStateQuery;
			EcsWorld.Query(in worldQuery2, (Entity entity, ref TerrainState t, ref EditorState e) =>
			{
				terrain = t;
				editor = e;
				foundWorld = true;
			});

			if (!foundWorld) return false;

			var saveData = new MapSaveData();
			saveData.Width = terrain.Width;
			saveData.Depth = terrain.Depth;
			saveData.CameraBoundsLeft = editor.CameraBoundsLeft;
			saveData.CameraBoundsRight = editor.CameraBoundsRight;
			saveData.CameraBoundsTop = editor.CameraBoundsTop;
			saveData.CameraBoundsBottom = editor.CameraBoundsBottom;
			saveData.SkyboxPath = editor.SkyboxPath;
			saveData.Coordinates = coordinatesData ?? new List<CoordinateSaveData>();

			int width = terrain.Width;
			int depth = terrain.Depth;

			string directory = Path.GetDirectoryName(absolutePath);
			if (!Directory.Exists(directory))
			{
				Directory.CreateDirectory(directory);
			}
			else
			{
				int maxBackups = EditorSettingsDialog.CurrentSettings?.MaxBackupSnapshots ?? 3;
				if (maxBackups > 0)
				{
					string backupSourceDir = directory;
					_ = Task.Run(() => CreateWorkspaceBackup(backupSourceDir, maxBackups));
				}
			}

			string heightsPath = Path.Combine(directory, "terrain_heights.exr");
			string waterPath = Path.Combine(directory, "terrain_water.exr");
			string splatIndicesPath = Path.Combine(directory, "terrain_splat_indices.exr");
			string splatWeightsPath = Path.Combine(directory, "terrain_splat_weights.exr");
			string pathingPath = Path.Combine(directory, "terrain_pathing.png");

			var cells = terrain.Cells;
			byte[] heightsBytes = new byte[width * depth * 4 * sizeof(float)];
			Span<float> heightsSpan = MemoryMarshal.Cast<byte, float>(heightsBytes.AsSpan());

			byte[] waterBytes = new byte[width * depth * 4 * sizeof(float)];
			Span<float> waterSpan = MemoryMarshal.Cast<byte, float>(waterBytes.AsSpan());

			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					var cell = cells != null ? cells[x, z] : default;
					int baseIdx = (z * width + x) * 4;

					heightsSpan[baseIdx + 0] = cell.Y_NW;
					heightsSpan[baseIdx + 1] = cell.Y_NE;
					heightsSpan[baseIdx + 2] = cell.Y_SE;
					heightsSpan[baseIdx + 3] = cell.Y_SW;

					waterSpan[baseIdx + 0] = (float)cell.WaterMode;
					waterSpan[baseIdx + 1] = (float)cell.WaterProfileIndex;
					waterSpan[baseIdx + 2] = cell.WaterHeight;
					waterSpan[baseIdx + 3] = 1f;
				}
			}

			Image heightsImage = Image.CreateFromData(width, depth, false, Image.Format.Rgbaf, heightsBytes);
			Image waterImage = Image.CreateFromData(width, depth, false, Image.Format.Rgbaf, waterBytes);
			heightsImage.SaveExr(heightsPath);
			waterImage.SaveExr(waterPath);

			byte[] pathingBytes = new byte[width * depth * 4];
			Span<byte> pathingSpan = pathingBytes.AsSpan();

			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					int code = terrain.PathingCodes != null ? terrain.PathingCodes[x, z] : EditableTerrain.GetDefaultPathingCode(Realm.Ecs.Components.Terrain.WaterType.None);
					int baseIdx = (z * width + x) * 4;

					pathingSpan[baseIdx + 0] = (byte)code;
					pathingSpan[baseIdx + 1] = 0;
					pathingSpan[baseIdx + 2] = 0;
					pathingSpan[baseIdx + 3] = 255;
				}
			}

			Image pathingImage = Image.CreateFromData(width, depth, false, Image.Format.Rgba8, pathingBytes);
			pathingImage.SavePng(pathingPath);

			int splatW = width;
			int splatD = depth;
			if (htmlColors != null && htmlColors.Length == (width + 1) * (depth + 1))
			{
				splatW = width + 1;
				splatD = depth + 1;
			}

			byte[] splatIndicesBytes = new byte[splatW * splatD * 4 * sizeof(float)];
			Span<float> splatIndicesSpan = MemoryMarshal.Cast<byte, float>(splatIndicesBytes.AsSpan());

			byte[] splatWeightsBytes = new byte[splatW * splatD * 4 * sizeof(float)];
			Span<float> splatWeightsSpan = MemoryMarshal.Cast<byte, float>(splatWeightsBytes.AsSpan());

			for (int z = 0; z < splatD; z++)
			{
				for (int x = 0; x < splatW; x++)
				{
					int idx = z * splatW + x;
					string serialized = (htmlColors != null && idx < htmlColors.Length) ? htmlColors[idx] : null;
					TerrainSplatWeights s = TerrainSplatWeights.Deserialize(serialized);
					int baseIdx = idx * 4;

					splatIndicesSpan[baseIdx + 0] = s.Index0;
					splatIndicesSpan[baseIdx + 1] = s.Index1;
					splatIndicesSpan[baseIdx + 2] = s.Index2;
					splatIndicesSpan[baseIdx + 3] = s.Index3;

					splatWeightsSpan[baseIdx + 0] = s.Weight0;
					splatWeightsSpan[baseIdx + 1] = s.Weight1;
					splatWeightsSpan[baseIdx + 2] = s.Weight2;
					splatWeightsSpan[baseIdx + 3] = s.Weight3;
				}
			}

			Image splatIndicesImage = Image.CreateFromData(splatW, splatD, false, Image.Format.Rgbaf, splatIndicesBytes);
			Image splatWeightsImage = Image.CreateFromData(splatW, splatD, false, Image.Format.Rgbaf, splatWeightsBytes);
			splatIndicesImage.SaveExr(splatIndicesPath);
			splatWeightsImage.SaveExr(splatWeightsPath);

			if (cliffHtmlColors != null && cliffHtmlColors.Length == splatW * splatD)
			{
				string cliffSplatIndicesPath = Path.Combine(directory, "terrain_cliff_splat_indices.exr");
				string cliffSplatWeightsPath = Path.Combine(directory, "terrain_cliff_splat_weights.exr");

				byte[] cliffIndicesBytes = new byte[splatW * splatD * 4 * sizeof(float)];
				Span<float> cliffIndicesSpan = MemoryMarshal.Cast<byte, float>(cliffIndicesBytes.AsSpan());

				byte[] cliffWeightsBytes = new byte[splatW * splatD * 4 * sizeof(float)];
				Span<float> cliffWeightsSpan = MemoryMarshal.Cast<byte, float>(cliffWeightsBytes.AsSpan());

				for (int z = 0; z < splatD; z++)
				{
					for (int x = 0; x < splatW; x++)
					{
						int idx = z * splatW + x;
						string serialized = cliffHtmlColors[idx];
						TerrainSplatWeights s = TerrainSplatWeights.Deserialize(serialized);
						int baseIdx = idx * 4;

						cliffIndicesSpan[baseIdx + 0] = s.Index0;
						cliffIndicesSpan[baseIdx + 1] = s.Index1;
						cliffIndicesSpan[baseIdx + 2] = s.Index2;
						cliffIndicesSpan[baseIdx + 3] = s.Index3;

						cliffWeightsSpan[baseIdx + 0] = s.Weight0;
						cliffWeightsSpan[baseIdx + 1] = s.Weight1;
						cliffWeightsSpan[baseIdx + 2] = s.Weight2;
						cliffWeightsSpan[baseIdx + 3] = s.Weight3;
					}
				}

				Image cliffIndicesImage = Image.CreateFromData(splatW, splatD, false, Image.Format.Rgbaf, cliffIndicesBytes);
				Image cliffWeightsImage = Image.CreateFromData(splatW, splatD, false, Image.Format.Rgbaf, cliffWeightsBytes);
				cliffIndicesImage.SaveExr(cliffSplatIndicesPath);
				cliffWeightsImage.SaveExr(cliffSplatWeightsPath);
			}

			saveData.Units = new List<UnitSaveData>();
			var unitQuery = Realm.Ecs.Common.QueryCache.AllDefinitionIdAndPositionAndOwnerQuery;
			EcsWorld.Query(in unitQuery, (Entity entity, ref DefinitionId defId, ref Position pos, ref Owner owner) =>
			{
				if (!IsValidUnitObjectId(defId.Value, directory))
				{
					return;
				}

				float rotY = 0f;
				if (EcsWorld.Has<RotationY>(entity)) rotY = EcsWorld.Get<RotationY>(entity).Value;

				float scale = 1f;
				if (EcsWorld.Has<ModelScale>(entity)) scale = EcsWorld.Get<ModelScale>(entity).Value;

				int playerIndex = 0;
				if (EcsWorld.Has<UnitOwnerPlayer>(entity))
				{
					playerIndex = EcsWorld.Get<UnitOwnerPlayer>(entity).PlayerIndex;
				}

				bool isEnemy = NetworkService.ArePlayerIndicesEnemies(GameHost.Instance?.LocalPlayerIndex ?? 0, playerIndex);
				if (EcsWorld.Has<UnitFaction>(entity))
				{
					isEnemy = EcsWorld.Get<UnitFaction>(entity).IsEnemy;
				}

				saveData.Units.Add(new UnitSaveData
				{
					UnitId = defId.Value,
					PosX = pos.Value.X,
					PosY = pos.Value.Y,
					PosZ = pos.Value.Z,
					RotationY = rotY,
					Scale = scale,
					IsEnemy = isEnemy,
					Player = playerIndex
				});
			});

			saveData.Props = new List<PropSaveData>();
			var propQuery = Realm.Ecs.Common.QueryCache.AllPropIdentityAndPositionQuery;
			EcsWorld.Query(in propQuery, (Entity entity, ref PropIdentity propId, ref Position pos) =>
			{
				if (!IsValidPropObjectId(propId.PropId, directory))
				{
					return;
				}

				float rotY = 0f;
				if (EcsWorld.Has<RotationY>(entity)) rotY = EcsWorld.Get<RotationY>(entity).Value;

				float scale = 1f;
				if (EcsWorld.Has<ModelScale>(entity)) scale = EcsWorld.Get<ModelScale>(entity).Value;

				saveData.Props.Add(new PropSaveData
				{
					PropId = propId.PropId,
					PosX = pos.Value.X,
					PosY = pos.Value.Y,
					PosZ = pos.Value.Z,
					RotationY = rotY,
					Scale = scale
				});
			});

			saveData.Decals = new List<DecalSaveData>();
			var decalQuery = Realm.Ecs.Common.QueryCache.AllDecalIdentityAndPositionQuery;
			var orphanedDecalEntities = new List<Entity>();
			var savedDecalFingerprints = new HashSet<string>();
			EcsWorld.Query(in decalQuery, (Entity entity, ref DecalIdentity decalId, ref Position pos) =>
			{
				if (validDecalEntities != null && !validDecalEntities.Contains(entity))
				{
					orphanedDecalEntities.Add(entity);
					return;
				}

				float rotX = 0f;
				float rotY = 0f;
				float rotZ = 0f;
				if (EcsWorld.Has<Realm.Ecs.Components.Meta.Rotation3D>(entity))
				{
					var r3d = EcsWorld.Get<Realm.Ecs.Components.Meta.Rotation3D>(entity).Value;
					rotX = r3d.X; rotY = r3d.Y; rotZ = r3d.Z;
				}
				else if (EcsWorld.Has<RotationY>(entity))
				{
					rotY = EcsWorld.Get<RotationY>(entity).Value;
				}

				float scale = 1f;
				if (EcsWorld.Has<ModelScale>(entity)) scale = EcsWorld.Get<ModelScale>(entity).Value;

				string fingerprint = $"{decalId.DecalId}_{pos.Value.X:F3}_{pos.Value.Y:F3}_{pos.Value.Z:F3}_{rotX:F2}_{rotY:F2}_{rotZ:F2}_{scale:F3}";
				if (!savedDecalFingerprints.Add(fingerprint))
				{
					orphanedDecalEntities.Add(entity);
					return;
				}

				saveData.Decals.Add(new DecalSaveData
				{
					DecalId = decalId.DecalId,
					PosX = pos.Value.X,
					PosY = pos.Value.Y,
					PosZ = pos.Value.Z,
					RotationX = rotX,
					RotationY = rotY,
					RotationZ = rotZ,
					Scale = scale
				});
			});

			foreach (var orphan in orphanedDecalEntities)
			{
				if (EcsWorld.IsAlive(orphan))
				{
					EcsWorld.Destroy(orphan);
				}
			}

			saveData.Vfx = new List<VfxSaveData>();
			if (GameHost.Instance?.AllVfx != null)
			{
				foreach (var vfx in GameHost.Instance.AllVfx)
				{
					if (vfx != null && GodotObject.IsInstanceValid(vfx))
					{
						saveData.Vfx.Add(new VfxSaveData
						{
							VfxId = vfx.Config?.VfxId ?? "vfx",
							PosX = vfx.Position.X,
							PosY = vfx.Position.Y,
							PosZ = vfx.Position.Z,
							RotationX = vfx.RotationDegrees.X,
							RotationY = vfx.RotationDegrees.Y,
							RotationZ = vfx.RotationDegrees.Z,
							ScaleX = vfx.Scale.X,
							ScaleY = vfx.Scale.Y,
							ScaleZ = vfx.Scale.Z,
							NormalOffset = vfx.Config?.SurfaceNormalOffset ?? 0f,
							Config = vfx.Config?.Clone()
						});
					}
				}
			}

			SortMapSaveData(saveData);

			var saveDoc = JsonSerializer.SerializeToNode(saveData) as JsonObject;
			if (saveDoc != null)
			{
				CleanTerrainJsonSchema(saveDoc);
				MapJsonFormatter.SaveFormattedJson(absolutePath, saveDoc);
			}
			else
			{
				string json = JsonSerializer.Serialize(saveData);
				MapJsonFormatter.SaveFormattedJson(absolutePath, json);
			}

			GameHost.Instance?.SaveModelYOffsetsToMetadataJson(directory);

			SyncMetadataAssetsAndPrune(directory);

			string metaPath = Path.Combine(directory, "metadata.json");
			if (File.Exists(metaPath))
			{
				try
				{
					MetadataService.Instance.UpdateMetadata(directory, meta =>
					{
						meta.MapProperties.MapWidth = width;
						meta.MapProperties.MapHeight = depth;
						MetadataService.Instance.CleanMetadata(meta);
					});
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[SaveLoadService] CleanMetadataJson error: {ex.Message}");
				}
			}

			MapWorkspaceService.EnsureLicenseFile(directory);

			if (foundWorld)
			{
				var updatedEditor = new EditorState(
					editor.BlockMode,
					editor.BlockLevelHeight,
					editor.CameraBoundsLeft,
					editor.CameraBoundsRight,
					editor.CameraBoundsTop,
					editor.CameraBoundsBottom,
					editor.SkyboxPath,
					false,
					editor.MirrorMode,
					editor.WaterMode,
					editor.WaterProfileIndex
				);

				EcsWorld.Query(in worldQuery2, (Entity entity, ref TerrainState t, ref EditorState e) =>
				{
					EcsWorld.Set(entity, updatedEditor);
				});
			}

			return true;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine(ex.Message);
			return false;
		}
	}

	public bool LoadMapFromFile(string absolutePath, bool terrainOnly = false)
	{
		if (!File.Exists(absolutePath)) return false;

		try
		{
			string json = File.ReadAllText(absolutePath);
			var saveData = JsonSerializer.Deserialize<MapSaveData>(json);
			if (saveData == null) return false;

			string mapDir = Path.GetDirectoryName(absolutePath);
			GameHost.Instance?.LoadModelYOffsetsFromMetadataJson(mapDir);

			_lastLoadedCoordinates = saveData.Coordinates ?? new List<CoordinateSaveData>();

			var unitQuery = Realm.Ecs.Common.QueryCache.AllDefinitionIdAndPositionAndOwnerQuery;
			var unitsToDestroy = new List<Entity>();
			EcsWorld.Query(in unitQuery, (Entity entity) => unitsToDestroy.Add(entity));
			foreach (var ent in unitsToDestroy) EcsWorld.Destroy(ent);

			var propQuery = Realm.Ecs.Common.QueryCache.AllPropIdentityAndPositionQuery;
			var propsToDestroy = new List<Entity>();
			EcsWorld.Query(in propQuery, (Entity entity) => propsToDestroy.Add(entity));
			foreach (var ent in propsToDestroy) EcsWorld.Destroy(ent);

			var decalQuery = Realm.Ecs.Common.QueryCache.AllDecalIdentityAndPositionQuery;
			var decalsToDestroy = new List<Entity>();
			EcsWorld.Query(in decalQuery, (Entity entity) => decalsToDestroy.Add(entity));
			foreach (var ent in decalsToDestroy) EcsWorld.Destroy(ent);

			var req1 = Realm.Ecs.Common.QueryCache.AllUnitSpawnRequestQuery;
			var req1List = new List<Entity>();
			EcsWorld.Query(in req1, (Entity entity) => req1List.Add(entity));
			foreach (var ent in req1List) EcsWorld.Destroy(ent);

			var req2 = Realm.Ecs.Common.QueryCache.AllPropSpawnRequestQuery;
			var req2List = new List<Entity>();
			EcsWorld.Query(in req2, (Entity entity) => req2List.Add(entity));
			foreach (var ent in req2List) EcsWorld.Destroy(ent);

			var req3 = Realm.Ecs.Common.QueryCache.AllDecalSpawnRequestQuery;
			var req3List = new List<Entity>();
			EcsWorld.Query(in req3, (Entity entity) => req3List.Add(entity));
			foreach (var ent in req3List) EcsWorld.Destroy(ent);

			int width = 0;
			int depth = 0;
			if (MetadataService.Instance.TryLoadMetadata(mapDir, out var loadedMeta) && loadedMeta.MapProperties != null)
			{
				if (loadedMeta.MapProperties.MapWidth.HasValue && loadedMeta.MapProperties.MapWidth.Value > 0)
				{
					width = loadedMeta.MapProperties.MapWidth.Value;
				}
				if (loadedMeta.MapProperties.MapHeight.HasValue && loadedMeta.MapProperties.MapHeight.Value > 0)
				{
					depth = loadedMeta.MapProperties.MapHeight.Value;
				}
			}

			if (width <= 0) width = saveData.Width > 0 ? saveData.Width : 128;
			if (depth <= 0) depth = saveData.Depth > 0 ? saveData.Depth : 128;

			width = Math.Clamp((int)Math.Round(width / 32.0) * 32, 32, 512);
			depth = Math.Clamp((int)Math.Round(depth / 32.0) * 32, 32, 512);

			saveData.Width = width;
			saveData.Depth = depth;

			Entity worldEntity = Entity.Null;
			var worldQuery = Realm.Ecs.Common.QueryCache.AllTerrainStateQuery;
			EcsWorld.Query(in worldQuery, (Entity entity) => worldEntity = entity);

			if (worldEntity == Entity.Null)
			{
				worldEntity = EcsWorld.Create();
			}

			if (!EcsWorld.Has<TerrainState>(worldEntity))
			{
				EcsWorld.Add(worldEntity, new TerrainState(width, depth, TerrainState.DefaultQuadSize, TerrainState.DefaultCellSize, new TerrainCell[width, depth], new int[width, depth], null, null));
			}
			else
			{
				ref var ts = ref EcsWorld.Get<TerrainState>(worldEntity);
				ts.Width = width;
				ts.Depth = depth;
				ts.Cells = new TerrainCell[width, depth];
				ts.PathingCodes = new int[width, depth];
				EcsWorld.Set(worldEntity, ts);
			}

			if (EcsWorld.Has<TerrainState>(worldEntity))
			{
				ref var ts = ref EcsWorld.Get<TerrainState>(worldEntity);

				if (ts.Cells == null || ts.Cells.GetLength(0) != width || ts.Cells.GetLength(1) != depth)
				{
					ts.Cells = new TerrainCell[width, depth];
				}
				if (ts.PathingCodes == null)
				{
					ts.PathingCodes = new int[width, depth];
				}

				if (ts.Cells == null || ts.Cells.GetLength(0) != width || ts.Cells.GetLength(1) != depth)
				{
					ts.Cells = new TerrainCell[width, depth];
				}

				string directory = Path.GetDirectoryName(absolutePath);
				string heightsPath = Path.Combine(directory, "terrain_heights.exr");
				bool heightsLoaded = false;
				TerrainCell[,] unscaledCells = null;
				int unscaledW = 0;
				int unscaledH = 0;

				if (File.Exists(heightsPath))
				{
					Image heightsImage = Image.LoadFromFile(heightsPath);
					if (heightsImage != null)
					{
						heightsImage.Convert(Image.Format.Rgbaf);
						int imgW = heightsImage.GetWidth();
						int imgH = heightsImage.GetHeight();
						ReadOnlySpan<float> floatData = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(heightsImage.GetData());

						if (imgW == width && imgH == depth)
						{
							for (int z = 0; z < depth; z++)
							{
								for (int x = 0; x < width; x++)
								{
									int baseIdx = (z * imgW + x) * 4;
									float yNW = floatData[baseIdx + 0];
									float yNE = floatData[baseIdx + 1];
									float ySE = floatData[baseIdx + 2];
									float ySW = floatData[baseIdx + 3];
									ts.Cells[x, z] = new TerrainCell(yNW, yNE, ySE, ySW);
								}
							}
						}
						else
						{
							unscaledW = imgW;
							unscaledH = imgH;
							unscaledCells = new TerrainCell[imgW, imgH];
							for (int z = 0; z < imgH; z++)
							{
								for (int x = 0; x < imgW; x++)
								{
									int baseIdx = (z * imgW + x) * 4;
									float yNW = floatData[baseIdx + 0];
									float yNE = floatData[baseIdx + 1];
									float ySE = floatData[baseIdx + 2];
									float ySW = floatData[baseIdx + 3];
									unscaledCells[x, z] = new TerrainCell(yNW, yNE, ySE, ySW);
								}
							}

							float[,] newGridHeights = new float[width + 1, depth + 1];
							for (int vz = 0; vz <= depth; vz++)
							{
								for (int vx = 0; vx <= width; vx++)
								{
									int srcVx = Math.Clamp((int)Math.Round(vx * (float)imgW / width), 0, imgW);
									int srcVz = Math.Clamp((int)Math.Round(vz * (float)imgH / depth), 0, imgH);
									float h = 0f;
									if (srcVx < imgW && srcVz < imgH) h = floatData[(srcVz * imgW + srcVx) * 4 + 0];
									else if (srcVx >= imgW && srcVz >= imgH) h = floatData[((imgH - 1) * imgW + (imgW - 1)) * 4 + 2];
									else if (srcVx >= imgW) h = floatData[(srcVz * imgW + (imgW - 1)) * 4 + 1];
									else if (srcVz >= imgH) h = floatData[((imgH - 1) * imgW + srcVx) * 4 + 3];
									newGridHeights[vx, vz] = h;
								}
							}

							for (int z = 0; z < depth; z++)
							{
								for (int x = 0; x < width; x++)
								{
									float yNW = newGridHeights[x, z];
									float yNE = newGridHeights[x + 1, z];
									float ySW = newGridHeights[x, z + 1];
									float ySE = newGridHeights[x + 1, z + 1];
									ts.Cells[x, z] = new TerrainCell(yNW, yNE, ySE, ySW);
								}
							}
						}
						heightsLoaded = true;
					}
				}

				if (!heightsLoaded)
				{
					ts.Cells = new TerrainCell[width, depth];
				}

				string waterPath = Path.Combine(directory, "terrain_water.exr");
				if (File.Exists(waterPath))
				{
					Image waterImage = Image.LoadFromFile(waterPath);
					if (waterImage != null)
					{
						waterImage.Convert(Image.Format.Rgbaf);
						int imgW = waterImage.GetWidth();
						int imgH = waterImage.GetHeight();
						ReadOnlySpan<float> waterFloatData = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(waterImage.GetData());

						if (imgW == width && imgH == depth && unscaledCells == null)
						{
							for (int z = 0; z < depth; z++)
							{
								for (int x = 0; x < width; x++)
								{
									int baseIdx = (z * imgW + x) * 4;
									var wMode = (WaterType)Math.Clamp((int)MathF.Round(waterFloatData[baseIdx + 0]), 0, 2);
									byte wProfile = (byte)Math.Clamp((int)MathF.Round(waterFloatData[baseIdx + 1]), 0, 255);
									float wHeight = waterFloatData[baseIdx + 2];
									ts.Cells[x, z].WaterMode = wMode;
									ts.Cells[x, z].WaterProfileIndex = wProfile;
									ts.Cells[x, z].WaterHeight = wHeight;
								}
							}
						}
						else
						{
							if (unscaledCells == null || unscaledW != imgW || unscaledH != imgH)
							{
								unscaledCells = new TerrainCell[imgW, imgH];
								unscaledW = imgW;
								unscaledH = imgH;
							}

							for (int z = 0; z < imgH; z++)
							{
								for (int x = 0; x < imgW; x++)
								{
									int baseIdx = (z * imgW + x) * 4;
									var wMode = (WaterType)Math.Clamp((int)MathF.Round(waterFloatData[baseIdx + 0]), 0, 2);
									byte wProfile = (byte)Math.Clamp((int)MathF.Round(waterFloatData[baseIdx + 1]), 0, 255);
									float wHeight = waterFloatData[baseIdx + 2];
									unscaledCells[x, z].WaterMode = wMode;
									unscaledCells[x, z].WaterProfileIndex = wProfile;
									unscaledCells[x, z].WaterHeight = wHeight;
								}
							}
						}
					}
				}

				string pathingPath = Path.Combine(directory, "terrain_pathing.png");
				bool pathingLoaded = false;
				if (File.Exists(pathingPath))
				{
					Image pathingImage = Image.LoadFromFile(pathingPath);
					if (pathingImage != null)
					{
						pathingImage.Convert(Image.Format.Rgba8);
						int imgW = pathingImage.GetWidth();
						int imgH = pathingImage.GetHeight();
						ReadOnlySpan<byte> byteData = pathingImage.GetData();

						for (int z = 0; z < depth; z++)
						{
							for (int x = 0; x < width; x++)
							{
								int srcX = imgW == width ? x : Math.Clamp((int)Math.Floor(x * (float)imgW / width), 0, imgW - 1);
								int srcZ = imgH == depth ? z : Math.Clamp((int)Math.Floor(z * (float)imgH / depth), 0, imgH - 1);
								int baseIdx = (srcZ * imgW + srcX) * 4;
								ts.PathingCodes[x, z] = byteData[baseIdx + 0];
							}
						}
						pathingLoaded = true;
					}
				}
				if (!pathingLoaded)
				{
					for (int z = 0; z < depth; z++)
					{
						for (int x = 0; x < width; x++)
						{
							ts.PathingCodes[x, z] = EditableTerrain.GetDefaultPathingCode(Realm.Ecs.Components.Terrain.WaterType.None);
						}
					}
				}

				if (unscaledCells != null)
				{
					RuntimeTerrain.ReconcileScaledWater(unscaledCells, unscaledW, unscaledH, ts.Cells, ts.PathingCodes, width, depth);
				}

				EcsWorld.Set(worldEntity, ts);
			}

			string splatIndicesPath = Path.Combine(Path.GetDirectoryName(absolutePath), "terrain_splat_indices.exr");
			string splatWeightsPath = Path.Combine(Path.GetDirectoryName(absolutePath), "terrain_splat_weights.exr");
			
			string[] loadedColors = null;

			if (File.Exists(splatIndicesPath) && File.Exists(splatWeightsPath))
			{
				Image splatIndicesImage = Image.LoadFromFile(splatIndicesPath);
				Image splatWeightsImage = Image.LoadFromFile(splatWeightsPath);
				if (splatIndicesImage != null && splatWeightsImage != null)
				{
					splatIndicesImage.Convert(Image.Format.Rgbaf);
					splatWeightsImage.Convert(Image.Format.Rgbaf);
					int idxW = splatIndicesImage.GetWidth();
					int idxH = splatIndicesImage.GetHeight();
					int wgtW = splatWeightsImage.GetWidth();
					int wgtH = splatWeightsImage.GetHeight();

					ReadOnlySpan<float> idxData = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(splatIndicesImage.GetData());
					ReadOnlySpan<float> wgtData = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(splatWeightsImage.GetData());

					int splatW = width + 1;
					int splatD = depth + 1;
					loadedColors = new string[splatW * splatD];
					for (int z = 0; z < splatD; z++)
					{
						for (int x = 0; x < splatW; x++)
						{
							int srcIdxX = idxW == splatW ? x : Math.Clamp((int)Math.Floor(x * (float)(idxW - 1) / Math.Max(1, splatW - 1)), 0, idxW - 1);
							int srcIdxZ = idxH == splatD ? z : Math.Clamp((int)Math.Floor(z * (float)(idxH - 1) / Math.Max(1, splatD - 1)), 0, idxH - 1);
							int idxOffset = (srcIdxZ * idxW + srcIdxX) * 4;

							int srcWgtX = wgtW == splatW ? x : Math.Clamp((int)Math.Floor(x * (float)(wgtW - 1) / Math.Max(1, splatW - 1)), 0, wgtW - 1);
							int srcWgtZ = wgtH == splatD ? z : Math.Clamp((int)Math.Floor(z * (float)(wgtH - 1) / Math.Max(1, splatD - 1)), 0, wgtH - 1);
							int weightOffset = (srcWgtZ * wgtW + srcWgtX) * 4;

							int i0 = (int)Math.Round(idxData[idxOffset + 0]);
							int i1 = (int)Math.Round(idxData[idxOffset + 1]);
							int i2 = (int)Math.Round(idxData[idxOffset + 2]);
							int i3 = (int)Math.Round(idxData[idxOffset + 3]);

							float w0 = wgtData[weightOffset + 0];
							float w1 = wgtData[weightOffset + 1];
							float w2 = wgtData[weightOffset + 2];
							float w3 = wgtData[weightOffset + 3];

							var s = new TerrainSplatWeights
							{
								Index0 = i0,
								Index1 = i1,
								Index2 = i2,
								Index3 = i3,
								Weight0 = w0,
								Weight1 = w1,
								Weight2 = w2,
								Weight3 = w3
							};

							loadedColors[z * splatW + x] = s.Serialize();
						}
					}
				}
			}

			if (loadedColors == null)
			{
				loadedColors = new string[width * depth];
				string defaultSolid = TerrainSplatWeights.CreateSolid(3).Serialize();
				for (int i = 0; i < loadedColors.Length; i++)
				{
					loadedColors[i] = defaultSolid;
				}
			}

			if (loadedColors != null)
			{
				EcsWorld.SetOrAdd(worldEntity, new TerrainColorsState(loadedColors));
			}

			bool isBlock = true;
			float step = EditableTerrain.TIER_HEIGHT;
			float left = saveData.CameraBoundsLeft ?? -95.0f;
			float right = saveData.CameraBoundsRight ?? 95.0f;
			float top = saveData.CameraBoundsTop ?? -95.0f;
			float bottom = saveData.CameraBoundsBottom ?? 125.0f;
			string skybox = saveData.SkyboxPath;

			WaterType currentWaterMode = EcsWorld.Has<EditorState>(worldEntity) ? EcsWorld.Get<EditorState>(worldEntity).WaterMode : WaterType.None;
			byte currentWaterProf = EcsWorld.Has<EditorState>(worldEntity) ? EcsWorld.Get<EditorState>(worldEntity).WaterProfileIndex : (byte)0;
			var newEditorState = new EditorState(isBlock, step, left, right, top, bottom, skybox, false, MirrorMode.None, currentWaterMode, currentWaterProf);
			EcsWorld.SetOrAdd(worldEntity, newEditorState);

			if (EcsWorld.Has<CameraState>(worldEntity))
			{
				ref var camState = ref EcsWorld.Get<CameraState>(worldEntity);
				camState.LimitLeft = left;
				camState.LimitRight = right;
				camState.LimitTop = top;
				camState.LimitBottom = bottom;
			}

			if (!terrainOnly)
			{
				if (saveData.Units != null)
				{
					foreach (var u in saveData.Units)
					{
						if (!IsValidUnitObjectId(u.UnitId, mapDir))
						{
							GD.PushWarning($"[SaveLoadService] Ignored invalid unit '{u.UnitId}' in terrain.json because it does not exist as an Object ID in metadata.json.");
							continue;
						}

						var reqEnt = EcsWorld.Create();
						EcsWorld.Add(reqEnt, new UnitSpawnRequest(
							u.UnitId,
							new System.Numerics.Vector3(u.PosX, u.PosY, u.PosZ),
							u.RotationY,
							u.Scale,
							u.IsEnemy,
							u.Player
						));
					}
				}

				if (saveData.Props != null)
				{
					foreach (var p in saveData.Props)
					{
						if (!IsValidPropObjectId(p.PropId, mapDir))
						{
							GD.PushWarning($"[SaveLoadService] Ignored invalid prop '{p.PropId}' in terrain.json because it does not exist as an Object ID in metadata.json.");
							continue;
						}

						var reqEnt = EcsWorld.Create();
						EcsWorld.Add(reqEnt, new PropSpawnRequest(
							p.PropId,
							new System.Numerics.Vector3(p.PosX, p.PosY, p.PosZ),
							p.RotationY,
							p.Scale
						));
					}
				}

				if (saveData.Decals != null)
				{
					var loadedDecalFingerprints = new HashSet<string>();
					foreach (var d in saveData.Decals)
					{
						string fingerprint = $"{d.DecalId}_{d.PosX:F3}_{d.PosY:F3}_{d.PosZ:F3}_{d.RotationX:F2}_{d.RotationY:F2}_{d.RotationZ:F2}_{d.Scale:F3}";
						if (!loadedDecalFingerprints.Add(fingerprint))
						{
							continue;
						}

						var reqEnt = EcsWorld.Create();
						EcsWorld.Add(reqEnt, new DecalSpawnRequest(
							d.DecalId,
							new System.Numerics.Vector3(d.PosX, d.PosY, d.PosZ),
							new System.Numerics.Vector3(d.RotationX, d.RotationY, d.RotationZ),
							d.Scale
						));
					}
				}

				if (saveData.Vfx != null && GameHost.Instance != null)
				{
					foreach (var v in saveData.Vfx)
					{
						GameHost.Instance.SpawnVfxExternalWithParams(
							v.VfxId,
							new Vector3(v.PosX, v.PosY, v.PosZ),
							new Vector3(v.RotationX, v.RotationY, v.RotationZ),
							new Vector3(v.ScaleX <= 0f ? 1f : v.ScaleX, v.ScaleY <= 0f ? 1f : v.ScaleY, v.ScaleZ <= 0f ? 1f : v.ScaleZ),
							v.NormalOffset,
							v.Config
						);
					}
				}
			}

			return true;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine(ex.Message);
			return false;
		}
	}

	public static void RemapSplatExrFiles(string mapDirectory, IReadOnlyDictionary<int, int> remap)
	{
		if (string.IsNullOrEmpty(mapDirectory) || remap == null || remap.Count == 0) return;

		string[] indicesFiles = new[]
		{
			Path.Combine(mapDirectory, "terrain_splat_indices.exr"),
			Path.Combine(mapDirectory, "terrain_cliff_splat_indices.exr")
		};

		foreach (string file in indicesFiles)
		{
			if (File.Exists(file))
			{
				try
				{
					var img = Image.LoadFromFile(file);
					if (img != null)
					{
						img.Convert(Image.Format.Rgbaf);
						int w = img.GetWidth();
						int h = img.GetHeight();
						byte[] data = img.GetData();
						Span<float> floats = MemoryMarshal.Cast<byte, float>(data.AsSpan());
						bool modified = false;
						for (int i = 0; i < floats.Length; i++)
						{
							int oldIdx = (int)MathF.Round(floats[i]);
							if (remap.TryGetValue(oldIdx, out int newIdx) && newIdx != oldIdx)
							{
								floats[i] = newIdx;
								modified = true;
							}
						}
						if (modified)
						{
							var updatedImg = Image.CreateFromData(w, h, false, Image.Format.Rgbaf, data);
							updatedImg.SaveExr(file);
						}
					}
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[SaveLoadService] Failed to remap splat EXR {file}: {ex.Message}");
				}
			}
		}
	}

	public static void SortMapSaveData(MapSaveData saveData)
	{
		if (saveData == null) return;

		int width = saveData.Width > 0 ? saveData.Width : 128;
		int depth = saveData.Depth > 0 ? saveData.Depth : 128;
		float topLeftX = -width / 2.0f;
		float topLeftZ = -depth / 2.0f;

		if (saveData.Units != null)
		{
			saveData.Units.Sort((a, b) =>
			{
				int comparison = string.Compare(a.UnitId, b.UnitId, StringComparison.OrdinalIgnoreCase);
				if (comparison != 0) return comparison;
				comparison = string.Compare(a.UnitId, b.UnitId, StringComparison.Ordinal);
				if (comparison != 0) return comparison;

				float distanceA = MathF.Sqrt(MathF.Pow(a.PosX - topLeftX, 2) + MathF.Pow(a.PosZ - topLeftZ, 2));
				float distanceB = MathF.Sqrt(MathF.Pow(b.PosX - topLeftX, 2) + MathF.Pow(b.PosZ - topLeftZ, 2));
				comparison = distanceA.CompareTo(distanceB);
				if (comparison != 0) return comparison;

				comparison = a.PosX.CompareTo(b.PosX);
				if (comparison != 0) return comparison;
				comparison = a.PosZ.CompareTo(b.PosZ);
				if (comparison != 0) return comparison;
				comparison = a.PosY.CompareTo(b.PosY);
				if (comparison != 0) return comparison;
				comparison = a.RotationY.CompareTo(b.RotationY);
				if (comparison != 0) return comparison;
				comparison = a.Scale.CompareTo(b.Scale);
				if (comparison != 0) return comparison;
				comparison = a.Player.CompareTo(b.Player);
				if (comparison != 0) return comparison;
				return a.IsEnemy.CompareTo(b.IsEnemy);
			});
		}

		if (saveData.Props != null)
		{
			saveData.Props.Sort((a, b) =>
			{
				int comparison = string.Compare(a.PropId, b.PropId, StringComparison.OrdinalIgnoreCase);
				if (comparison != 0) return comparison;
				comparison = string.Compare(a.PropId, b.PropId, StringComparison.Ordinal);
				if (comparison != 0) return comparison;

				float distanceA = MathF.Sqrt(MathF.Pow(a.PosX - topLeftX, 2) + MathF.Pow(a.PosZ - topLeftZ, 2));
				float distanceB = MathF.Sqrt(MathF.Pow(b.PosX - topLeftX, 2) + MathF.Pow(b.PosZ - topLeftZ, 2));
				comparison = distanceA.CompareTo(distanceB);
				if (comparison != 0) return comparison;

				comparison = a.PosX.CompareTo(b.PosX);
				if (comparison != 0) return comparison;
				comparison = a.PosZ.CompareTo(b.PosZ);
				if (comparison != 0) return comparison;
				comparison = a.PosY.CompareTo(b.PosY);
				if (comparison != 0) return comparison;
				comparison = a.RotationY.CompareTo(b.RotationY);
				if (comparison != 0) return comparison;
				return a.Scale.CompareTo(b.Scale);
			});
		}

		if (saveData.Decals != null)
		{
			saveData.Decals.Sort((a, b) =>
			{
				int comparison = string.Compare(a.DecalId, b.DecalId, StringComparison.OrdinalIgnoreCase);
				if (comparison != 0) return comparison;
				comparison = string.Compare(a.DecalId, b.DecalId, StringComparison.Ordinal);
				if (comparison != 0) return comparison;

				float distanceA = MathF.Sqrt(MathF.Pow(a.PosX - topLeftX, 2) + MathF.Pow(a.PosZ - topLeftZ, 2));
				float distanceB = MathF.Sqrt(MathF.Pow(b.PosX - topLeftX, 2) + MathF.Pow(b.PosZ - topLeftZ, 2));
				comparison = distanceA.CompareTo(distanceB);
				if (comparison != 0) return comparison;

				comparison = a.PosX.CompareTo(b.PosX);
				if (comparison != 0) return comparison;
				comparison = a.PosZ.CompareTo(b.PosZ);
				if (comparison != 0) return comparison;
				comparison = a.PosY.CompareTo(b.PosY);
				if (comparison != 0) return comparison;
				comparison = a.RotationY.CompareTo(b.RotationY);
				if (comparison != 0) return comparison;
				return a.Scale.CompareTo(b.Scale);
			});
		}

		if (saveData.Vfx != null)
		{
			saveData.Vfx.Sort((a, b) =>
			{
				int comparison = string.Compare(a.VfxId, b.VfxId, StringComparison.OrdinalIgnoreCase);
				if (comparison != 0) return comparison;
				comparison = string.Compare(a.VfxId, b.VfxId, StringComparison.Ordinal);
				if (comparison != 0) return comparison;

				float distanceA = MathF.Sqrt(MathF.Pow(a.PosX - topLeftX, 2) + MathF.Pow(a.PosZ - topLeftZ, 2));
				float distanceB = MathF.Sqrt(MathF.Pow(b.PosX - topLeftX, 2) + MathF.Pow(b.PosZ - topLeftZ, 2));
				comparison = distanceA.CompareTo(distanceB);
				if (comparison != 0) return comparison;

				comparison = a.PosX.CompareTo(b.PosX);
				if (comparison != 0) return comparison;
				comparison = a.PosZ.CompareTo(b.PosZ);
				if (comparison != 0) return comparison;
				comparison = a.PosY.CompareTo(b.PosY);
				if (comparison != 0) return comparison;
				comparison = a.RotationY.CompareTo(b.RotationY);
				if (comparison != 0) return comparison;
				return a.ScaleX.CompareTo(b.ScaleX);
			});
		}

		if (saveData.Coordinates != null)
		{
			saveData.Coordinates.Sort((a, b) =>
			{
				int comparison = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
				if (comparison != 0) return comparison;
				comparison = string.Compare(a.Name, b.Name, StringComparison.Ordinal);
				if (comparison != 0) return comparison;

				float centerAX = (a.MinX + a.MaxX) * 0.5f;
				float centerAZ = (a.MinZ + a.MaxZ) * 0.5f;
				float centerBX = (b.MinX + b.MaxX) * 0.5f;
				float centerBZ = (b.MinZ + b.MaxZ) * 0.5f;

				float distanceA = MathF.Sqrt(MathF.Pow(centerAX - topLeftX, 2) + MathF.Pow(centerAZ - topLeftZ, 2));
				float distanceB = MathF.Sqrt(MathF.Pow(centerBX - topLeftX, 2) + MathF.Pow(centerBZ - topLeftZ, 2));
				comparison = distanceA.CompareTo(distanceB);
				if (comparison != 0) return comparison;

				comparison = a.MinX.CompareTo(b.MinX);
				if (comparison != 0) return comparison;
				comparison = a.MinZ.CompareTo(b.MinZ);
				if (comparison != 0) return comparison;
				comparison = a.MaxX.CompareTo(b.MaxX);
				if (comparison != 0) return comparison;
				return a.MaxZ.CompareTo(b.MaxZ);
			});
		}
	}

	public static string CreateWorkspaceBackup(string workspacePath, int maxBackups = 3)
	{
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath))
			return string.Empty;

		try
		{
			string fullWsPath = Path.GetFullPath(workspacePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string fullUserDataDir = Path.GetFullPath(OS.GetUserDataDir()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string globalBackupsRoot = Path.GetFullPath(Path.Combine(fullUserDataDir, "map_backups")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string globalUpgradesRoot = Path.GetFullPath(Path.Combine(fullUserDataDir, "map_upgrades")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string resGlobalPath = Path.GetFullPath(ProjectSettings.GlobalizePath("res://")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

			if (string.Equals(fullWsPath, fullUserDataDir, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(fullWsPath, resGlobalPath, StringComparison.OrdinalIgnoreCase))
			{
				return string.Empty;
			}

			if (fullWsPath.Equals(globalBackupsRoot, StringComparison.OrdinalIgnoreCase) ||
				fullWsPath.StartsWith(globalBackupsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
				fullWsPath.Equals(globalUpgradesRoot, StringComparison.OrdinalIgnoreCase) ||
				fullWsPath.StartsWith(globalUpgradesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
				fullWsPath.IndexOf("map_backups", StringComparison.OrdinalIgnoreCase) >= 0 ||
				fullWsPath.IndexOf("map_upgrades", StringComparison.OrdinalIgnoreCase) >= 0 ||
				fullWsPath.IndexOf(".backups", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return string.Empty;
			}

			string wsName = Path.GetFileName(fullWsPath);
			if (string.IsNullOrEmpty(wsName) ||
				string.Equals(wsName, "map_backups", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(wsName, "map_upgrades", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(wsName, "backups", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(wsName, ".backups", StringComparison.OrdinalIgnoreCase) ||
				wsName.StartsWith("backup_", StringComparison.OrdinalIgnoreCase))
			{
				return string.Empty;
			}

			string backupsRoot = Path.GetFullPath(Path.Combine(globalBackupsRoot, wsName)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			if (!Directory.Exists(backupsRoot))
			{
				Directory.CreateDirectory(backupsRoot);
			}

			string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
			string targetBackupDir = Path.GetFullPath(Path.Combine(backupsRoot, $"backup_{timestamp}")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

			Directory.CreateDirectory(targetBackupDir);

			CopyDirectoryContentsSafe(fullWsPath, targetBackupDir, backupsRoot);

			PruneOldBackups(backupsRoot, maxBackups);

			return targetBackupDir;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[SaveLoadService] Failed to create workspace backup: {ex.Message}");
			return string.Empty;
		}
	}

	private static void CopyDirectoryContentsSafe(string sourceDir, string targetDir, string backupsRoot = null)
	{
		var source = new DirectoryInfo(sourceDir);
		if (!source.Exists) return;

		string normalizedTarget = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string normalizedBackupsRoot = !string.IsNullOrEmpty(backupsRoot) ? Path.GetFullPath(backupsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : null;

		var excludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			".git", "bin", "obj", ".godot", ".vs", ".vscode", ".idea", "map_backups", "map_upgrades", "backups", ".backups", ".dotnet", ".wasi", ".sidecarcache", ".cache"
		};

		var filesToCopy = new List<(string SourcePath, string DestPath)>();
		var dirsToProcess = new Queue<(DirectoryInfo Dir, string Target)>();
		dirsToProcess.Enqueue((source, targetDir));

		while (dirsToProcess.Count > 0)
		{
			var (curDir, curTarget) = dirsToProcess.Dequeue();
			Directory.CreateDirectory(curTarget);

			foreach (var file in curDir.GetFiles())
			{
				if (file.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
				filesToCopy.Add((file.FullName, Path.Combine(curTarget, file.Name)));
			}

			foreach (var subDir in curDir.GetDirectories())
			{
				if (excludedFolders.Contains(subDir.Name)) continue;
				if (subDir.Name.StartsWith("backup_", StringComparison.OrdinalIgnoreCase)) continue;

				string subDirFull = Path.GetFullPath(subDir.FullName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

				if (subDirFull.IndexOf("map_backups", StringComparison.OrdinalIgnoreCase) >= 0 ||
					subDirFull.IndexOf("map_upgrades", StringComparison.OrdinalIgnoreCase) >= 0 ||
					subDirFull.IndexOf(".backups", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					continue;
				}

				if (!string.IsNullOrEmpty(normalizedBackupsRoot) &&
					(string.Equals(subDirFull, normalizedBackupsRoot, StringComparison.OrdinalIgnoreCase) ||
					 subDirFull.StartsWith(normalizedBackupsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
				{
					continue;
				}

				if (string.Equals(subDirFull, normalizedTarget, StringComparison.OrdinalIgnoreCase) ||
					subDirFull.StartsWith(normalizedTarget + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
					normalizedTarget.StartsWith(subDirFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				dirsToProcess.Enqueue((subDir, Path.Combine(curTarget, subDir.Name)));
			}
		}

		Parallel.ForEach(filesToCopy, pair =>
		{
			try
			{
				File.Copy(pair.SourcePath, pair.DestPath, true);
			}
			catch { }
		});
	}

	private static void PruneOldBackups(string backupsRoot, int maxBackups)
	{
		if (maxBackups < 1) maxBackups = 1;
		try
		{
			var rootDir = new DirectoryInfo(backupsRoot);
			if (!rootDir.Exists) return;

			var backupDirs = rootDir.GetDirectories("backup_*")
				.OrderBy(d => d.CreationTimeUtc)
				.ThenBy(d => d.Name)
				.ToList();

			while (backupDirs.Count > maxBackups)
			{
				var oldest = backupDirs[0];
				backupDirs.RemoveAt(0);
				try
				{
					DeleteDirectoryRecursiveClearingReadOnly(oldest.FullName);
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[SaveLoadService] Failed to prune old backup {oldest.FullName}: {ex.Message}");
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[SaveLoadService] PruneOldBackups error: {ex.Message}");
		}
	}

	private static void DeleteDirectoryRecursiveClearingReadOnly(string targetDir)
	{
		if (!Directory.Exists(targetDir)) return;
		try
		{
			foreach (var file in Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories))
			{
				var attrs = File.GetAttributes(file);
				if ((attrs & FileAttributes.ReadOnly) != 0)
				{
					File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
				}
			}
			Directory.Delete(targetDir, true);
		}
		catch
		{
			try { Directory.Delete(targetDir, true); } catch { }
		}
	}

	private static HashSet<string>? _cachedAllowedTerrainTopLevel;
	private static HashSet<string>? _cachedAllowedTerrainUnitProperties;
	private static HashSet<string>? _cachedAllowedTerrainPropProperties;
	private static HashSet<string>? _cachedAllowedTerrainDecalProperties;
	private static HashSet<string>? _cachedAllowedTerrainCoordinateProperties;

	private static HashSet<string>? _cachedAllowedMetadataTopLevel;
	private static HashSet<string>? _cachedAllowedAssetCategories;
	private static HashSet<string>? _cachedAllowedGlbSubCategories;
	private static HashSet<string>? _cachedAllowedGlbItemProperties;
	private static HashSet<string>? _cachedAllowedTextureItemProperties;
	private static HashSet<string>? _cachedAllowedDecalItemProperties;
	private static HashSet<string>? _cachedAllowedVfxItemProperties;
	private static HashSet<string>? _cachedAllowedShaderItemProperties;
	private static HashSet<string>? _cachedAllowedMapProperties;
	private static HashSet<string>? _cachedAllowedEntityItemProperties;
	private static HashSet<string>? _cachedAllowedAbilityItemProperties;
	private static HashSet<string>? _cachedAllowedWeaponItemProperties;
	private static HashSet<string>? _cachedAllowedUpgradeItemProperties;
	private static HashSet<string>? _cachedAllowedCustomItemProperties;

	private static void AddTypeMembersToSet(Type type, HashSet<string> destination)
	{
		foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			destination.Add(property.Name);
			destination.Add(property.Name.ToLowerInvariant());
			destination.Add(ConvertToSnakeCase(property.Name));
		}
		foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
		{
			destination.Add(field.Name);
			destination.Add(field.Name.ToLowerInvariant());
			destination.Add(ConvertToSnakeCase(field.Name));
		}
	}

	private static string ConvertToSnakeCase(string input)
	{
		if (string.IsNullOrEmpty(input)) return input;
		var stringBuilder = new System.Text.StringBuilder();
		for (int index = 0; index < input.Length; index++)
		{
			char character = input[index];
			if (char.IsUpper(character))
			{
				if (index > 0 && input[index - 1] != '_')
				{
					stringBuilder.Append('_');
				}
				stringBuilder.Append(char.ToLowerInvariant(character));
			}
			else
			{
				stringBuilder.Append(character);
			}
		}
		return stringBuilder.ToString();
	}

	private static JsonObject? LoadMapSchemaJson()
	{
		string[] candidatePaths = new[]
		{
			PathUtils.FindPath("Realm.MapEditorExtension/metadata.schema.json"),
			PathUtils.FindPath("MapTemplate/.vscode/metadata.schema.json"),
			PathUtils.FindPath(".vscode/metadata.schema.json")
		};

		foreach (var candidatePath in candidatePaths)
		{
			if (!string.IsNullOrEmpty(candidatePath) && File.Exists(candidatePath))
			{
				try
				{
					return JsonNode.Parse(File.ReadAllText(candidatePath)) as JsonObject;
				}
				catch
				{
				}
			}
		}

		return null;
	}

	private static void ExtractPropertiesFromSchemaNode(JsonNode? node, HashSet<string> destination)
	{
		if (node is JsonObject jsonObject)
		{
			if (jsonObject.TryGetPropertyValue("properties", out var propertiesNode) && propertiesNode is JsonObject propertiesObject)
			{
				foreach (var property in propertiesObject)
				{
					destination.Add(property.Key);
					destination.Add(ConvertToSnakeCase(property.Key));
				}
			}
			if (jsonObject.TryGetPropertyValue("items", out var itemsNode))
			{
				ExtractPropertiesFromSchemaNode(itemsNode, destination);
			}
		}
	}

	private static HashSet<string> GetAllowedTerrainTopLevel()
	{
		if (_cachedAllowedTerrainTopLevel != null) return _cachedAllowedTerrainTopLevel;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddTypeMembersToSet(typeof(MapSaveData), set);
		_cachedAllowedTerrainTopLevel = set;
		return set;
	}

	private static HashSet<string> GetAllowedTerrainUnitProperties()
	{
		if (_cachedAllowedTerrainUnitProperties != null) return _cachedAllowedTerrainUnitProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddTypeMembersToSet(typeof(UnitSaveData), set);
		_cachedAllowedTerrainUnitProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedTerrainPropProperties()
	{
		if (_cachedAllowedTerrainPropProperties != null) return _cachedAllowedTerrainPropProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddTypeMembersToSet(typeof(PropSaveData), set);
		_cachedAllowedTerrainPropProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedTerrainDecalProperties()
	{
		if (_cachedAllowedTerrainDecalProperties != null) return _cachedAllowedTerrainDecalProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddTypeMembersToSet(typeof(DecalSaveData), set);
		_cachedAllowedTerrainDecalProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedTerrainCoordinateProperties()
	{
		if (_cachedAllowedTerrainCoordinateProperties != null) return _cachedAllowedTerrainCoordinateProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddTypeMembersToSet(typeof(CoordinateSaveData), set);
		_cachedAllowedTerrainCoordinateProperties = set;
		return set;
	}

	private static HashSet<string>? _cachedAllowedTerrainVfxProperties;

	private static HashSet<string> GetAllowedTerrainVfxProperties()
	{
		if (_cachedAllowedTerrainVfxProperties != null) return _cachedAllowedTerrainVfxProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddTypeMembersToSet(typeof(VfxSaveData), set);
		_cachedAllowedTerrainVfxProperties = set;
		return set;
	}

	public static void CleanTerrainJsonSchema(JsonObject root)
	{
		if (root == null) return;

		var allowedTopLevel = GetAllowedTerrainTopLevel();
		var topKeysToRemove = root.Select(keyValuePair => keyValuePair.Key).Where(key => !allowedTopLevel.Contains(key)).ToList();
		foreach (var key in topKeysToRemove)
		{
			root.Remove(key);
		}

		var allowedUnitProperties = GetAllowedTerrainUnitProperties();
		if (root.TryGetPropertyValue("Units", out var unitsNode) && unitsNode is JsonArray unitsArray)
		{
			foreach (var item in unitsArray.OfType<JsonObject>())
			{
				var propertiesToRemove = item.Select(property => property.Key).Where(property => !allowedUnitProperties.Contains(property)).ToList();
				foreach (var property in propertiesToRemove) item.Remove(property);
			}
		}

		var allowedPropProperties = GetAllowedTerrainPropProperties();
		if (root.TryGetPropertyValue("Props", out var propsNode) && propsNode is JsonArray propsArray)
		{
			foreach (var item in propsArray.OfType<JsonObject>())
			{
				var propertiesToRemove = item.Select(property => property.Key).Where(property => !allowedPropProperties.Contains(property)).ToList();
				foreach (var property in propertiesToRemove) item.Remove(property);
			}
		}

		var allowedDecalProperties = GetAllowedTerrainDecalProperties();
		if (root.TryGetPropertyValue("Decals", out var decalsNode) && decalsNode is JsonArray decalsArray)
		{
			foreach (var item in decalsArray.OfType<JsonObject>())
			{
				var propertiesToRemove = item.Select(property => property.Key).Where(property => !allowedDecalProperties.Contains(property)).ToList();
				foreach (var property in propertiesToRemove) item.Remove(property);
			}
		}

		var allowedCoordinateProperties = GetAllowedTerrainCoordinateProperties();
		if (root.TryGetPropertyValue("Coordinates", out var coordinatesNode) && coordinatesNode is JsonArray coordinatesArray)
		{
			foreach (var item in coordinatesArray.OfType<JsonObject>())
			{
				var propertiesToRemove = item.Select(property => property.Key).Where(property => !allowedCoordinateProperties.Contains(property)).ToList();
				foreach (var property in propertiesToRemove) item.Remove(property);
			}
		}

		var allowedVfxProperties = GetAllowedTerrainVfxProperties();
		if (root.TryGetPropertyValue("Vfx", out var vfxNode) && vfxNode is JsonArray vfxArray)
		{
			foreach (var item in vfxArray.OfType<JsonObject>())
			{
				var propertiesToRemove = item.Select(property => property.Key).Where(property => !allowedVfxProperties.Contains(property)).ToList();
				foreach (var property in propertiesToRemove) item.Remove(property);
			}
		}
	}

	public static bool IsValidPropObjectId(string propId, string mapDirectory = null)
	{
		if (string.IsNullOrWhiteSpace(propId)) return false;

		if (GameHost.PropRegistry != null && GameHost.PropRegistry.ContainsKey(propId)) return true;
		if (GameHost.ResourceRegistry != null && GameHost.ResourceRegistry.ContainsKey(propId)) return true;

		string targetDir = !string.IsNullOrEmpty(mapDirectory)
			? mapDirectory
			: MapWorkspaceService.GetActiveWorkspacePath();

		if (MetadataService.Instance.TryLoadMetadata(targetDir, out var metadata))
		{
			if (metadata.CustomProps != null && metadata.CustomProps.Any(p => propId.Equals(p.ObjectID, StringComparison.OrdinalIgnoreCase)))
				return true;
			if (metadata.CustomResources != null && metadata.CustomResources.Any(r => propId.Equals(r.ObjectID, StringComparison.OrdinalIgnoreCase)))
				return true;
		}

		return false;
	}

	public static bool IsValidUnitObjectId(string unitId, string mapDirectory = null)
	{
		if (string.IsNullOrWhiteSpace(unitId)) return false;

		if (GameHost.UnitRegistry != null && GameHost.UnitRegistry.ContainsKey(unitId)) return true;
		if (GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.ContainsKey(unitId)) return true;

		string targetDir = !string.IsNullOrEmpty(mapDirectory)
			? mapDirectory
			: MapWorkspaceService.GetActiveWorkspacePath();

		if (MetadataService.Instance.TryLoadMetadata(targetDir, out var metadata))
		{
			if (metadata.CustomUnits != null && metadata.CustomUnits.Any(u => unitId.Equals(u.ObjectID, StringComparison.OrdinalIgnoreCase)))
				return true;
			if (metadata.CustomBuildings != null && metadata.CustomBuildings.Any(b => unitId.Equals(b.ObjectID, StringComparison.OrdinalIgnoreCase)))
				return true;
		}

		return false;
	}

	private static void AddEnumNamesToSet<T>(HashSet<string> destination) where T : struct, Enum
	{
		foreach (var name in Enum.GetNames<T>())
		{
			destination.Add(name);
			destination.Add(name.ToLowerInvariant());
			destination.Add(ConvertToSnakeCase(name));
		}
	}

	private static HashSet<string> GetAllowedMetadataTopLevel(JsonObject? schemaRoot)
	{
		if (_cachedAllowedMetadataTopLevel != null) return _cachedAllowedMetadataTopLevel;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		set.Add("GameBuildNumber");
		set.Add("license");
		AddTypeMembersToSet(typeof(Realm.Ecs.Definitions.MapProperties), set);
		set.Add(nameof(Realm.Ecs.Definitions.MapProperties));

		foreach (var prop in typeof(MapMetadata).GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			set.Add(prop.Name);
			var jsonAttr = prop.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>();
			if (jsonAttr != null && !string.IsNullOrEmpty(jsonAttr.Name))
			{
				set.Add(jsonAttr.Name);
			}
		}
		set.Add("Models");
		set.Add("textures");
		set.Add("decals");
		set.Add("vfx_spritesheets");
		set.Add("noise_textures");
		set.Add("icons");
		set.Add("skyboxes");
		set.Add("ribbons");
		set.Add("shaders");
		set.Add("CustomUnits");
		set.Add("CustomBuildings");
		set.Add("CustomResources");
		set.Add("CustomProps");
		set.Add("CustomAbilities");
		set.Add("CustomWeapons");
		set.Add("CustomUpgrades");
		set.Add("CustomItems");
		set.Add("CustomAttachments");
		set.Add("CustomVfx");

		Type[] entityTypes = new[]
		{
			typeof(GameHost.UnitMetadata),
			typeof(GameHost.PropMetadata),
			typeof(GameHost.ResourceMetadata),
			typeof(GameHost.WeaponMetadata),
			typeof(GameHost.AttachmentMetadata),
			typeof(GameHost.AbilityMetadata),
			typeof(GameHost.UpgradeMetadata),
			typeof(GameHost.ItemMetadata)
		};

		foreach (var t in entityTypes)
		{
			string baseName = t.Name;
			if (baseName.EndsWith("Metadata", StringComparison.OrdinalIgnoreCase))
			{
				baseName = baseName[..^"Metadata".Length];
			}

			string plural = baseName.EndsWith("y", StringComparison.OrdinalIgnoreCase)
				? baseName[..^1] + "ies"
				: baseName + "s";

			set.Add("Custom" + plural);
		}

		if (schemaRoot != null && schemaRoot.TryGetPropertyValue("properties", out var propertiesNode) && propertiesNode is JsonObject propertiesObject)
		{
			foreach (var property in propertiesObject)
			{
				set.Add(property.Key);
			}
		}

		_cachedAllowedMetadataTopLevel = set;
		return set;
	}

	private static HashSet<string> GetAllowedAssetCategories(JsonObject? schemaRoot)
	{
		if (_cachedAllowedAssetCategories != null) return _cachedAllowedAssetCategories;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddEnumNamesToSet<GameHost.AssetCategory>(set);

		if (schemaRoot != null && schemaRoot.TryGetPropertyValue("properties", out var propertiesNode) && propertiesNode is JsonObject propertiesObject)
		{
			if (propertiesObject.TryGetPropertyValue("Assets", out var assetsDefinition))
			{
				ExtractPropertiesFromSchemaNode(assetsDefinition, set);
			}
		}

		if (schemaRoot != null && schemaRoot.TryGetPropertyValue("definitions", out var definitionsNode) && definitionsNode is JsonObject definitionsObject)
		{
			if (definitionsObject.TryGetPropertyValue("Assets", out var assetsDefinition))
			{
				ExtractPropertiesFromSchemaNode(assetsDefinition, set);
			}
		}

		_cachedAllowedAssetCategories = set;
		return set;
	}

	private static HashSet<string> GetAllowedGlbSubCategories(JsonObject? schemaRoot)
	{
		if (_cachedAllowedGlbSubCategories != null) return _cachedAllowedGlbSubCategories;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddEnumNamesToSet<GameHost.GlbSubCategory>(set);

		if (schemaRoot != null && schemaRoot.TryGetPropertyValue("definitions", out var definitionsNode) && definitionsNode is JsonObject definitionsObject)
		{
			if (definitionsObject.TryGetPropertyValue("Assets", out var assetsDefinition) && assetsDefinition is JsonObject assetsObj)
			{
				if (assetsObj.TryGetPropertyValue("properties", out var assetsProps) && assetsProps is JsonObject assetsPropsObj)
				{
					if (assetsPropsObj.TryGetPropertyValue("glb", out var glbDefinition))
					{
						ExtractPropertiesFromSchemaNode(glbDefinition, set);
					}
				}
			}
		}

		_cachedAllowedGlbSubCategories = set;
		return set;
	}

	private static HashSet<string> GetAllowedGlbItemProperties(JsonObject? schemaRoot)
	{
		if (_cachedAllowedGlbItemProperties != null) return _cachedAllowedGlbItemProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddTypeMembersToSet(typeof(GameHost.GlbItemMetadata), set);
		AddTypeMembersToSet(typeof(GameHost.UnitMetadata), set);
		AddTypeMembersToSet(typeof(GameHost.PropMetadata), set);
		AddTypeMembersToSet(typeof(GameHost.ResourceMetadata), set);
		AddTypeMembersToSet(typeof(GameHost.WeaponMetadata), set);
		AddTypeMembersToSet(typeof(GameHost.AttachmentMetadata), set);

		set.Add("spawn_shader");
		set.Add("death_shader");
		set.Add("despawn_shader");

		if (schemaRoot != null && schemaRoot.TryGetPropertyValue("definitions", out var definitionsNode) && definitionsNode is JsonObject definitionsObject)
		{
			if (definitionsObject.TryGetPropertyValue("EntityItem", out var entityDefinition))
			{
				ExtractPropertiesFromSchemaNode(entityDefinition, set);
			}
		}

		_cachedAllowedGlbItemProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedTextureItemProperties()
	{
		if (_cachedAllowedTextureItemProperties != null) return _cachedAllowedTextureItemProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddTypeMembersToSet(typeof(GameHost.TextureMetadata), set);
		AddTypeMembersToSet(typeof(TerrainTextureSnapshot), set);
		_cachedAllowedTextureItemProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedDecalItemProperties()
	{
		if (_cachedAllowedDecalItemProperties != null) return _cachedAllowedDecalItemProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddTypeMembersToSet(typeof(GameHost.DecalMetadata), set);
		AddTypeMembersToSet(typeof(DecalSnapshot), set);
		_cachedAllowedDecalItemProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedVfxItemProperties()
	{
		if (_cachedAllowedVfxItemProperties != null) return _cachedAllowedVfxItemProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddTypeMembersToSet(typeof(GameHost.VfxMetadata), set);
		_cachedAllowedVfxItemProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedShaderItemProperties()
	{
		if (_cachedAllowedShaderItemProperties != null) return _cachedAllowedShaderItemProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddTypeMembersToSet(typeof(CustomShaderConfig), set);
		_cachedAllowedShaderItemProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedMapProperties(JsonObject? schemaRoot)
	{
		if (_cachedAllowedMapProperties != null) return _cachedAllowedMapProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddTypeMembersToSet(typeof(Realm.Ecs.Definitions.MapProperties), set);

		if (schemaRoot != null && schemaRoot.TryGetPropertyValue("properties", out var propertiesNode) && propertiesNode is JsonObject propertiesObject)
		{
			if (propertiesObject.TryGetPropertyValue("MapProperties", out var mapPropertiesDefinition))
			{
				ExtractPropertiesFromSchemaNode(mapPropertiesDefinition, set);
			}
		}

		_cachedAllowedMapProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedEntityItemProperties(JsonObject? schemaRoot)
	{
		if (_cachedAllowedEntityItemProperties != null) return _cachedAllowedEntityItemProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddTypeMembersToSet(typeof(GameHost.UnitMetadata), set);
		AddTypeMembersToSet(typeof(GameHost.PropMetadata), set);
		AddTypeMembersToSet(typeof(GameHost.ResourceMetadata), set);

		set.Add("spawn_shader");
		set.Add("spawnshader");
		set.Add("SpawnShader");
		set.Add("death_shader");
		set.Add("deathshader");
		set.Add("DeathShader");
		set.Add("despawn_shader");
		set.Add("despawnshader");
		set.Add("DespawnShader");

		if (schemaRoot != null && schemaRoot.TryGetPropertyValue("definitions", out var definitionsNode) && definitionsNode is JsonObject definitionsObject)
		{
			if (definitionsObject.TryGetPropertyValue("EntityItem", out var entityDefinition))
			{
				ExtractPropertiesFromSchemaNode(entityDefinition, set);
			}
		}

		_cachedAllowedEntityItemProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedAbilityItemProperties(JsonObject? schemaRoot)
	{
		if (_cachedAllowedAbilityItemProperties != null) return _cachedAllowedAbilityItemProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddTypeMembersToSet(typeof(GameHost.AbilityMetadata), set);

		if (schemaRoot != null && schemaRoot.TryGetPropertyValue("definitions", out var definitionsNode) && definitionsNode is JsonObject definitionsObject)
		{
			if (definitionsObject.TryGetPropertyValue("CustomAbilities", out var abilityDefinition))
			{
				ExtractPropertiesFromSchemaNode(abilityDefinition, set);
			}
		}

		_cachedAllowedAbilityItemProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedWeaponItemProperties(JsonObject? schemaRoot)
	{
		if (_cachedAllowedWeaponItemProperties != null) return _cachedAllowedWeaponItemProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddTypeMembersToSet(typeof(GameHost.WeaponMetadata), set);

		if (schemaRoot != null && schemaRoot.TryGetPropertyValue("definitions", out var definitionsNode) && definitionsNode is JsonObject definitionsObject)
		{
			if (definitionsObject.TryGetPropertyValue("CustomWeapons", out var weaponDefinition))
			{
				ExtractPropertiesFromSchemaNode(weaponDefinition, set);
			}
		}

		_cachedAllowedWeaponItemProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedUpgradeItemProperties(JsonObject? schemaRoot)
	{
		if (_cachedAllowedUpgradeItemProperties != null) return _cachedAllowedUpgradeItemProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddTypeMembersToSet(typeof(GameHost.UpgradeMetadata), set);

		if (schemaRoot != null && schemaRoot.TryGetPropertyValue("definitions", out var definitionsNode) && definitionsNode is JsonObject definitionsObject)
		{
			if (definitionsObject.TryGetPropertyValue("CustomUpgrades", out var upgradeDefinition))
			{
				ExtractPropertiesFromSchemaNode(upgradeDefinition, set);
			}
		}

		_cachedAllowedUpgradeItemProperties = set;
		return set;
	}

	private static HashSet<string> GetAllowedCustomItemProperties(JsonObject? schemaRoot)
	{
		if (_cachedAllowedCustomItemProperties != null) return _cachedAllowedCustomItemProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddTypeMembersToSet(typeof(GameHost.ItemMetadata), set);

		if (schemaRoot != null && schemaRoot.TryGetPropertyValue("definitions", out var definitionsNode) && definitionsNode is JsonObject definitionsObject)
		{
			if (definitionsObject.TryGetPropertyValue("CustomItems", out var itemDefinition))
			{
				ExtractPropertiesFromSchemaNode(itemDefinition, set);
			}
		}

		_cachedAllowedCustomItemProperties = set;
		return set;
	}

	private static HashSet<string>? _cachedAllowedAttachmentItemProperties;

	private static HashSet<string> GetAllowedAttachmentItemProperties(JsonObject? schemaRoot)
	{
		if (_cachedAllowedAttachmentItemProperties != null) return _cachedAllowedAttachmentItemProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddTypeMembersToSet(typeof(GameHost.AttachmentMetadata), set);

		if (schemaRoot != null && schemaRoot.TryGetPropertyValue("definitions", out var definitionsNode) && definitionsNode is JsonObject definitionsObject)
		{
			if (definitionsObject.TryGetPropertyValue("CustomAttachments", out var attachDefinition))
			{
				ExtractPropertiesFromSchemaNode(attachDefinition, set);
			}
		}

		_cachedAllowedAttachmentItemProperties = set;
		return set;
	}

	private static HashSet<string>? _cachedAllowedVfxConfigProperties;

	private static HashSet<string> GetAllowedVfxConfigProperties(JsonObject? schemaRoot)
	{
		if (_cachedAllowedVfxConfigProperties != null) return _cachedAllowedVfxConfigProperties;
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddTypeMembersToSet(typeof(VfxAttachmentConfig), set);

		if (schemaRoot != null && schemaRoot.TryGetPropertyValue("definitions", out var definitionsNode) && definitionsNode is JsonObject definitionsObject)
		{
			if (definitionsObject.TryGetPropertyValue("CustomVfx", out var vfxDefinition))
			{
				ExtractPropertiesFromSchemaNode(vfxDefinition, set);
			}
		}

		_cachedAllowedVfxConfigProperties = set;
		return set;
	}

	private static void CleanJsonArrayObjects(JsonArray array, HashSet<string> allowedProperties)
	{
		foreach (var item in array.OfType<JsonObject>())
		{
			var propertiesToRemove = item.Select(property => property.Key).Where(property => !allowedProperties.Contains(property)).ToList();
			foreach (var property in propertiesToRemove)
			{
				item.Remove(property);
			}
		}
	}

	private static void CleanTexturesObject(JsonObject texturesObject)
	{
		var allowedTextureItemProperties = GetAllowedTextureItemProperties();
		foreach (var keyValuePair in texturesObject)
		{
			if (keyValuePair.Value is JsonObject itemObject)
			{
				var propertiesToRemove = itemObject.Select(property => property.Key).Where(property => !allowedTextureItemProperties.Contains(property)).ToList();
				foreach (var property in propertiesToRemove)
				{
					itemObject.Remove(property);
				}
			}
		}
	}

	internal static void CleanAssetsObject(JsonObject root, JsonObject? schemaRoot)
	{
		if (!root.TryGetPropertyValue("Assets", out var assetsNode) || assetsNode is not JsonObject assetsObject)
		{
			return;
		}

		var allowedCategories = GetAllowedAssetCategories(schemaRoot);
		var categoryKeysToRemove = assetsObject.Select(keyValuePair => keyValuePair.Key).Where(key => !allowedCategories.Contains(key)).ToList();
		foreach (var key in categoryKeysToRemove)
		{
			assetsObject.Remove(key);
		}

		if (assetsObject.TryGetPropertyValue("glb", out var glbNode) && glbNode is JsonObject glbObject)
		{
			var allowedGlbSubCategories = GetAllowedGlbSubCategories(schemaRoot);
			var subCategoryKeysToRemove = glbObject.Select(keyValuePair => keyValuePair.Key).Where(key => !allowedGlbSubCategories.Contains(key)).ToList();
			foreach (var key in subCategoryKeysToRemove)
			{
				glbObject.Remove(key);
			}

			var allowedGlbItemProperties = GetAllowedGlbItemProperties(schemaRoot);
			foreach (var subCategoryKeyValuePair in glbObject)
			{
				if (subCategoryKeyValuePair.Value is JsonObject subCategoryDictionary)
				{
					foreach (var itemKeyValuePair in subCategoryDictionary)
					{
						if (itemKeyValuePair.Value is JsonObject itemObject)
						{
							var itemPropertiesToRemove = itemObject.Select(property => property.Key).Where(property => !allowedGlbItemProperties.Contains(property)).ToList();
							foreach (var property in itemPropertiesToRemove) itemObject.Remove(property);
						}
					}
				}
			}
		}

		if (assetsObject.TryGetPropertyValue("textures", out var texturesNode) && texturesNode is JsonObject texturesObject)
		{
			CleanTexturesObject(texturesObject);
		}

		if (assetsObject.TryGetPropertyValue("decals", out var decalsNode) && decalsNode is JsonObject decalsObject)
		{
			var allowedDecalItemProperties = GetAllowedDecalItemProperties();
			foreach (var keyValuePair in decalsObject)
			{
				if (keyValuePair.Value is JsonObject itemObject)
				{
					var propertiesToRemove = itemObject.Select(property => property.Key).Where(property => !allowedDecalItemProperties.Contains(property)).ToList();
					foreach (var property in propertiesToRemove) itemObject.Remove(property);
				}
			}
		}

		if (assetsObject.TryGetPropertyValue("vfx_spritesheets", out var vfxNode) && vfxNode is JsonObject vfxObject)
		{
			var allowedVfxItemProperties = GetAllowedVfxItemProperties();
			foreach (var keyValuePair in vfxObject)
			{
				if (keyValuePair.Value is JsonObject itemObject)
				{
					var propertiesToRemove = itemObject.Select(property => property.Key).Where(property => !allowedVfxItemProperties.Contains(property)).ToList();
					foreach (var property in propertiesToRemove) itemObject.Remove(property);
				}
			}
		}

		if (assetsObject.TryGetPropertyValue("shaders", out var shadersNode) && shadersNode is JsonObject shadersObject)
		{
			var allowedShaderItemProperties = GetAllowedShaderItemProperties();
			foreach (var keyValuePair in shadersObject)
			{
				if (keyValuePair.Value is JsonObject itemObject)
				{
					var propertiesToRemove = itemObject.Select(property => property.Key).Where(property => !allowedShaderItemProperties.Contains(property)).ToList();
					foreach (var property in propertiesToRemove) itemObject.Remove(property);
				}
			}
		}
	}

	private static void CleanMapPropertiesObject(JsonObject mapPropertiesObject, JsonObject? schemaRoot)
	{
		var allowedMapProperties = GetAllowedMapProperties(schemaRoot);
		var propertiesToRemove = mapPropertiesObject.Select(property => property.Key).Where(property => !allowedMapProperties.Contains(property)).ToList();
		foreach (var property in propertiesToRemove)
		{
			mapPropertiesObject.Remove(property);
		}

		mapPropertiesObject.Remove("Assets");
	}

	public static void CleanMetadataJsonSchema(JsonObject root)
	{
		if (root == null) return;

		root["license"] = MetadataService.UgcLicenseUrl;

		root.Remove("Assets");
		root.Remove("Ratings");
		root.Remove("Greenlight");
		root.Remove("ModelOffsets");
		root.Remove("ModelScales");
		root.Remove("ModelCollisionCircleRatios");
		root.Remove("ModelObstacleRadii");
		root.Remove("ModelBrightness");
		root.Remove("ModelColorTint");
		root.Remove("ModelDespillPlayerColor");
		root.Remove("ModelNormalizeLuminance");
		root.Remove("ModelIgnorePlayerColor");
		root.Remove("ModelSpawnShaders");
		root.Remove("ModelDeathShaders");
		root.Remove("ModelNormalModes");

		if (root.TryGetPropertyValue("Models", out var modelsNode) && modelsNode is JsonObject modelsObject)
		{
			var allowedModelProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
			{
				"Offsets", "Scales", "CollisionCircleRatios", "ObstacleRadii", "Brightness",
				"ColorTint", "DespillPlayerColor", "NormalizeLuminance", "IgnorePlayerColor",
				"SpawnShaders", "DeathShaders"
			};
			foreach (var keyValuePair in modelsObject)
			{
				if (keyValuePair.Value is JsonObject itemObject)
				{
					itemObject.Remove("hash");
					var propertiesToRemove = itemObject.Select(property => property.Key).Where(property => !allowedModelProperties.Contains(property)).ToList();
					foreach (var property in propertiesToRemove) itemObject.Remove(property);
				}
			}
		}

		if (root.TryGetPropertyValue("textures", out var texturesNode) && texturesNode is JsonObject texturesObject)
		{
			CleanTexturesObject(texturesObject);
			foreach (var keyValuePair in texturesObject)
			{
				if (keyValuePair.Value is JsonObject itemObject)
				{
					itemObject.Remove("hash");
				}
			}
		}

		if (root.TryGetPropertyValue("decals", out var decalsNode) && decalsNode is JsonObject decalsObject)
		{
			var allowedDecalProperties = GetAllowedDecalItemProperties();
			foreach (var keyValuePair in decalsObject)
			{
				if (keyValuePair.Value is JsonObject itemObject)
				{
					itemObject.Remove("hash");
					var propertiesToRemove = itemObject.Select(property => property.Key).Where(property => !allowedDecalProperties.Contains(property)).ToList();
					foreach (var property in propertiesToRemove) itemObject.Remove(property);
				}
			}
		}

		if (root.TryGetPropertyValue("vfx_spritesheets", out var vfxNode) && vfxNode is JsonObject vfxObject)
		{
			var allowedVfxProperties = GetAllowedVfxItemProperties();
			foreach (var keyValuePair in vfxObject)
			{
				if (keyValuePair.Value is JsonObject itemObject)
				{
					itemObject.Remove("hash");
					var propertiesToRemove = itemObject.Select(property => property.Key).Where(property => !allowedVfxProperties.Contains(property)).ToList();
					foreach (var property in propertiesToRemove) itemObject.Remove(property);
				}
			}
		}

		if (root.TryGetPropertyValue("noise_textures", out var noiseNode) && noiseNode is JsonObject noiseObject)
		{
			foreach (var keyValuePair in noiseObject)
			{
				if (keyValuePair.Value is JsonObject itemObject)
				{
					itemObject.Remove("hash");
				}
			}
		}

		if (root.TryGetPropertyValue("shaders", out var shadersNode) && shadersNode is JsonObject shadersObject)
		{
			var allowedShaderProperties = GetAllowedShaderItemProperties();
			foreach (var keyValuePair in shadersObject)
			{
				if (keyValuePair.Value is JsonObject itemObject)
				{
					itemObject.Remove("hash");
					var propertiesToRemove = itemObject.Select(property => property.Key).Where(property => !allowedShaderProperties.Contains(property)).ToList();
					foreach (var property in propertiesToRemove) itemObject.Remove(property);
				}
			}
		}

		var schemaRoot = LoadMapSchemaJson();
		var allowedTopLevel = GetAllowedMetadataTopLevel(schemaRoot);

		var topKeysToRemove = root.Select(keyValuePair => keyValuePair.Key)
			.Where(key => !allowedTopLevel.Contains(key))
			.ToList();

		foreach (var key in topKeysToRemove)
		{
			root.Remove(key);
		}

		string mapPropsName = nameof(Realm.Ecs.Definitions.MapProperties);
		if (root.TryGetPropertyValue(mapPropsName, out var mapPropertiesNode) && mapPropertiesNode is JsonObject mapPropertiesObject)
		{
			CleanMapPropertiesObject(mapPropertiesObject, schemaRoot);
		}

		var allowedEntityProperties = GetAllowedEntityItemProperties(schemaRoot);
		foreach (var arrayName in GetMetadataEntityArrayNames())
		{
			if (root.TryGetPropertyValue(arrayName, out var node) && node is JsonArray array)
			{
				CleanJsonArrayObjects(array, allowedEntityProperties);
			}
		}

		var allowedAbilityProperties = GetAllowedAbilityItemProperties(schemaRoot);
		if (root.TryGetPropertyValue("CustomAbilities", out var abilitiesNode) && abilitiesNode is JsonArray abilitiesArray)
		{
			CleanJsonArrayObjects(abilitiesArray, allowedAbilityProperties);
		}

		var allowedWeaponProperties = GetAllowedWeaponItemProperties(schemaRoot);
		if (root.TryGetPropertyValue("CustomWeapons", out var weaponsNode) && weaponsNode is JsonArray weaponsArray)
		{
			CleanJsonArrayObjects(weaponsArray, allowedWeaponProperties);
		}

		var allowedUpgradeProperties = GetAllowedUpgradeItemProperties(schemaRoot);
		if (root.TryGetPropertyValue("CustomUpgrades", out var upgradesNode) && upgradesNode is JsonArray upgradesArray)
		{
			CleanJsonArrayObjects(upgradesArray, allowedUpgradeProperties);
		}

		var allowedCustomItemProperties = GetAllowedCustomItemProperties(schemaRoot);
		if (root.TryGetPropertyValue("CustomItems", out var customItemsNode) && customItemsNode is JsonArray customItemsArray)
		{
			CleanJsonArrayObjects(customItemsArray, allowedCustomItemProperties);
		}

		var allowedAttachmentProperties = GetAllowedAttachmentItemProperties(schemaRoot);
		if (root.TryGetPropertyValue("CustomAttachments", out var attachmentsNode) && attachmentsNode is JsonArray attachmentsArray)
		{
			CleanJsonArrayObjects(attachmentsArray, allowedAttachmentProperties);
		}

		var allowedVfxConfigProperties = GetAllowedVfxConfigProperties(schemaRoot);
		if (root.TryGetPropertyValue("CustomVfx", out var vfxConfigsNode) && vfxConfigsNode is JsonArray vfxConfigsArray)
		{
			CleanJsonArrayObjects(vfxConfigsArray, allowedVfxConfigProperties);
		}
	}

	private static string[] GetMetadataEntityArrayNames()
	{
		return new[]
		{
			"CustomUnits",
			"CustomBuildings",
			"CustomResources",
			"CustomProps"
		};
	}

	public static void SyncMetadataAssetsAndPrune(string mapDirectory)
	{
		if (string.IsNullOrEmpty(mapDirectory) || !Directory.Exists(mapDirectory)) return;

		try
		{
			var assetsObj = MapAssetHelper.LoadUnionedAssets(mapDirectory);
			string assetsDir = Path.Combine(mapDirectory, "Assets");

			var includedRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var assetsToSync = new List<(string RelativePath, JsonNode? EntryNode, JsonObject ParentObj, string PropertyKey, string Category, string? SubCategory)>();

			foreach (var categoryKvp in assetsObj)
			{
				string category = categoryKvp.Key.ToLowerInvariant();
				if (category == "glb" && categoryKvp.Value is JsonObject glbObj)
				{
					foreach (var subKvp in glbObj)
					{
						string subCategory = MapAssetHelper.NormalizeGlbSubCategory(subKvp.Key);
						if (subKvp.Value is JsonObject subCatObj)
						{
							foreach (var itemKvp in subCatObj)
							{
								string fileName = itemKvp.Key;
								string relPath = Path.Combine("Assets", "models", subCategory, fileName).Replace('\\', '/');
								includedRelativePaths.Add(relPath);
								assetsToSync.Add((relPath, itemKvp.Value, subCatObj, fileName, "glb", subCategory));
							}
						}
					}
				}
				else if (categoryKvp.Value is JsonObject catObj)
				{
					string subFolder = category switch
					{
						"vfx" or "vfx_spritesheets" => "vfx",
						"animations" => "animations",
						"sfx" => "sfx",
						"music" => "music",
						"icons" => "icons",
						"decals" => "decals",
						"ribbons" or "ribbon_textures" => "ribbons",
						"noise" or "noise_textures" => "noise",
						"skyboxes" => "skyboxes",
						"textures" => "textures",
						"shaders" => "shaders",
						_ => category
					};

					foreach (var itemKvp in catObj)
					{
						string fileName = itemKvp.Key;
						string relPath = Path.Combine("Assets", subFolder, fileName).Replace('\\', '/');
						includedRelativePaths.Add(relPath);

						if (subFolder is "sfx" or "music")
						{
							includedRelativePaths.Add(Path.Combine("Assets", "audio", subFolder, fileName).Replace('\\', '/'));
						}

						assetsToSync.Add((relPath, itemKvp.Value, catObj, fileName, category, null));
					}
				}
			}

			if (Directory.Exists(assetsDir))
			{
				string[] importFiles = Directory.GetFiles(assetsDir, "*.import", SearchOption.AllDirectories);
				foreach (var importFile in importFiles)
				{
					string sourceFile = importFile.Substring(0, importFile.Length - ".import".Length);
					if (!File.Exists(sourceFile))
					{
						try
						{
							File.Delete(importFile);
						}
						catch { }
					}
				}

				DeleteEmptyDirectoriesRecursive(assetsDir);
			}

			var nonExistentAssets = new List<(JsonObject ParentObj, string PropertyKey)>();
			Dictionary<string, string>? cachedAssetFiles = null;

			foreach (var (relPath, entryNode, parentObj, propertyKey, category, subCategory) in assetsToSync)
			{
				string fullDiskPath = Path.Combine(mapDirectory, relPath);
				if (!File.Exists(fullDiskPath))
				{
					string fileName = Path.GetFileName(relPath);
					if (category == "glb")
					{
						string? modelDisk = MapAssetHelper.FindModelOnDisk(mapDirectory, subCategory, fileName);
						if (!string.IsNullOrEmpty(modelDisk) && File.Exists(modelDisk))
						{
							fullDiskPath = modelDisk;
						}
					}
					else
					{
						string? altPath = FindAssetFileByName(assetsDir, fileName, ref cachedAssetFiles);
						if (altPath != null && File.Exists(altPath))
						{
							fullDiskPath = altPath;
						}
						else
						{
							string directMapPath = Path.Combine(mapDirectory, fileName);
							if (File.Exists(directMapPath))
							{
								fullDiskPath = directMapPath;
							}
						}
					}
				}

				if (File.Exists(fullDiskPath))
				{
					string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(fullDiskPath);
					if (!string.IsNullOrEmpty(canonicalBlake3))
					{
						if (entryNode is JsonObject itemObjRef)
						{
							itemObjRef["hash"] = canonicalBlake3;
						}
						else if (entryNode is JsonValue)
						{
							parentObj[propertyKey] = canonicalBlake3;
						}

						RealmMetadataHelper.SyncBlake3Metadata(fullDiskPath, canonicalBlake3);
					}
				}
				else
				{
					nonExistentAssets.Add((parentObj, propertyKey));
				}
			}

			foreach (var (parentObj, propertyKey) in nonExistentAssets)
			{
				parentObj.Remove(propertyKey);
			}

			MapAssetHelper.SaveAssetsToManifest(mapDirectory, assetsObj, removeFromMetadata: true);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[SaveLoadService] SyncMetadataAssetsAndPrune error: {ex.Message}");
		}
	}

	private static string? FindAssetFileByName(string searchDir, string fileName, ref Dictionary<string, string>? cachedFiles)
	{
		if (!Directory.Exists(searchDir)) return null;
		if (cachedFiles == null)
		{
			cachedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				foreach (var file in Directory.EnumerateFiles(searchDir, "*", SearchOption.AllDirectories))
				{
					string name = Path.GetFileName(file);
					cachedFiles.TryAdd(name, file);
				}
			}
			catch { }
		}
		return cachedFiles.TryGetValue(fileName, out var path) ? path : null;
	}

	private static void DeleteEmptyDirectoriesRecursive(string directory)
	{
		if (!Directory.Exists(directory)) return;
		foreach (var sub in Directory.GetDirectories(directory))
		{
			DeleteEmptyDirectoriesRecursive(sub);
		}
		if (Directory.GetFiles(directory).Length == 0 && Directory.GetDirectories(directory).Length == 0)
		{
			try { Directory.Delete(directory, false); } catch { }
		}
	}
}

