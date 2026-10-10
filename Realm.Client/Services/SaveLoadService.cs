using Arch.Core;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Realm.Client.Services;

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

	private void UpdateEntityTransforms(Entity entity, System.Numerics.Vector3? position, float rotationY, float scale)
	{
		if (!EcsWorld.IsAlive(entity)) return;

		if (position.HasValue)
		{
			if (EcsWorld.Has<Position>(entity))
			{
				var existingPos = EcsWorld.Get<Position>(entity).Value;
				if ((position.Value - existingPos).Length() > 0.0001f)
				{
					EcsWorld.Set(entity, new Position(position.Value));
				}
			}
			else
			{
				EcsWorld.Add(entity, new Position(position.Value));
			}
		}

		if (EcsWorld.Has<RotationY>(entity))
		{
			float existing = EcsWorld.Get<RotationY>(entity).Value;
			if (MathF.Abs(existing - rotationY) > 0.001f && MathF.Abs(MathF.Abs(existing - rotationY) - 360f) > 0.001f)
			{
				EcsWorld.Set(entity, new RotationY(rotationY));
			}
		}
		else
		{
			EcsWorld.Add(entity, new RotationY(rotationY));
		}

		if (EcsWorld.Has<ModelScale>(entity))
		{
			float existing = EcsWorld.Get<ModelScale>(entity).Value;
			if (MathF.Abs(existing - scale) > 0.0001f)
			{
				EcsWorld.Set(entity, new ModelScale(scale));
			}
		}
		else
		{
			EcsWorld.Add(entity, new ModelScale(scale));
		}
	}

	private void SaveTerrainData(string directory, int width, int depth, TerrainState terrain, string[] htmlColors, string[] cliffHtmlColors)
	{
		SaveHeightsAndWater(directory, width, depth, terrain);
		SavePathing(directory, width, depth, terrain);
		SaveSplatData(directory, width, depth, htmlColors, "terrain_splat");
		SaveSplatData(directory, width, depth, cliffHtmlColors, "terrain_cliff_splat");
	}

	private void SaveHeightsAndWater(string directory, int width, int depth, TerrainState terrain)
	{
		byte[] heightsBytes = new byte[width * depth * 4 * sizeof(float)];
		Span<float> heightsSpan = MemoryMarshal.Cast<byte, float>(heightsBytes.AsSpan());

		byte[] waterBytes = new byte[width * depth * 4 * sizeof(float)];
		Span<float> waterSpan = MemoryMarshal.Cast<byte, float>(waterBytes.AsSpan());

		var cells = terrain.Cells;
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
		SaveExrSafe(heightsImage, Path.Combine(directory, "terrain_heights.exr"));
		SaveExrSafe(waterImage, Path.Combine(directory, "terrain_water.exr"));
	}

	private void SavePathing(string directory, int width, int depth, TerrainState terrain)
	{
		byte[] pathingBytes = new byte[width * depth * 4];
		Span<byte> pathingSpan = pathingBytes.AsSpan();

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				int code = terrain.PathingCodes != null ? terrain.PathingCodes[x, z] : Realm.Client.EditableTerrain.GetDefaultPathingCode(Realm.Ecs.Components.Terrain.WaterType.None);
				int baseIdx = (z * width + x) * 4;

				pathingSpan[baseIdx + 0] = (byte)code;
				pathingSpan[baseIdx + 1] = 0;
				pathingSpan[baseIdx + 2] = 0;
				pathingSpan[baseIdx + 3] = 255;
			}
		}

		Image pathingImage = Image.CreateFromData(width, depth, false, Image.Format.Rgba8, pathingBytes);
		SavePngSafe(pathingImage, Path.Combine(directory, "terrain_pathing.png"));
	}

	private void SaveSplatData(string directory, int width, int depth, string[] htmlColors, string prefix)
	{
		if (htmlColors == null) return;

		int splatW = width;
		int splatD = depth;
		if (htmlColors.Length == (width + 1) * (depth + 1))
		{
			splatW = width + 1;
			splatD = depth + 1;
		}
		else if (htmlColors.Length != width * depth)
		{
			return;
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
				string serialized = idx < htmlColors.Length ? htmlColors[idx] : null;
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
		SaveExrSafe(splatIndicesImage, Path.Combine(directory, $"{prefix}_indices.exr"));
		SaveExrSafe(splatWeightsImage, Path.Combine(directory, $"{prefix}_weights.exr"));
	}

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

			UpdateEntityTransforms(unitsData, propsData, decalsData);

			var validDecalEntities = decalsData != null
				? new HashSet<Entity>(decalsData.Select(d => d.Entity))
				: null;

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

			string directory = Path.GetDirectoryName(absolutePath);
			if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

			SaveTerrainData(directory, terrain.Width, terrain.Depth, terrain, htmlColors, cliffHtmlColors);

			var saveData = CreateInitialMapSaveData(terrain, editor, coordinatesData);
			PopulateMapSaveData(saveData, directory, validDecalEntities);

			SortMapSaveData(saveData);
			EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;
			Realm.Shared.Services.MapFileService.SaveTerrain(absolutePath, saveData);

			Realm.Client.Core.GameHost.Instance?.SaveModelYOffsetsToMetadataJson(directory);
			SyncMetadataAssetsAndPrune(directory);
			CleanMetadataFile(directory, terrain.Width, terrain.Depth);
			MapWorkspaceService.EnsureLicenseFile(directory);

			if (foundWorld)
			{
				UpdateEditorState(editor, worldQuery2);
			}
			BackupWorkspaceIfNeeded(directory);

			return true;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine(ex.Message);
			return false;
		}
	}

	private void UpdateEntityTransforms(
		(Entity Entity, float RotationY, float Scale)[] unitsData,
		(Entity Entity, float RotationY, float Scale)[] propsData,
		(Entity Entity, System.Numerics.Vector3 Position, float RotationY, float Scale)[] decalsData)
	{
		foreach (var u in unitsData) UpdateEntityTransforms(u.Entity, null, u.RotationY, u.Scale);
		foreach (var p in propsData) UpdateEntityTransforms(p.Entity, null, p.RotationY, p.Scale);

		if (decalsData != null)
		{
			foreach (var d in decalsData) UpdateEntityTransforms(d.Entity, d.Position, d.RotationY, d.Scale);
		}
	}

	private MapSaveData CreateInitialMapSaveData(TerrainState terrain, EditorState editor, List<CoordinateSaveData> coordinatesData)
	{
		return new MapSaveData
		{
			Width = terrain.Width,
			Depth = terrain.Depth,
			CameraBoundsLeft = editor.CameraBoundsLeft,
			CameraBoundsRight = editor.CameraBoundsRight,
			CameraBoundsTop = editor.CameraBoundsTop,
			CameraBoundsBottom = editor.CameraBoundsBottom,
			SkyboxPath = editor.SkyboxPath,
			Coordinates = coordinatesData ?? new List<CoordinateSaveData>()
		};
	}

	private void PopulateMapSaveData(MapSaveData saveData, string directory, HashSet<Entity> validDecalEntities)
	{
		saveData.Units = GetUnitSaveData(directory);
		saveData.Props = GetPropSaveData(directory);
		saveData.Decals = GetDecalSaveData(validDecalEntities);
		saveData.Vfx = GetVfxSaveData();
	}

	private List<UnitSaveData> GetUnitSaveData(string directory)
	{
		var units = new List<UnitSaveData>();
		var unitQuery = Realm.Ecs.Common.QueryCache.AllDefinitionIdAndPositionAndOwnerQuery;
		int localPlayerIndex = Realm.Client.Core.GameHost.Instance?.LocalPlayerIndex ?? 0;

		EcsWorld.Query(in unitQuery, (Entity entity, ref DefinitionId defId, ref Position pos, ref Owner owner) =>
		{
			if (!IsValidUnitObjectId(defId.Value, directory)) return;

			float rotY = EcsWorld.Has<RotationY>(entity) ? EcsWorld.Get<RotationY>(entity).Value : 0f;
			float scale = EcsWorld.Has<ModelScale>(entity) ? EcsWorld.Get<ModelScale>(entity).Value : 1f;
			int playerIndex = EcsWorld.Has<UnitOwnerPlayer>(entity) ? EcsWorld.Get<UnitOwnerPlayer>(entity).PlayerIndex : 0;
			
			bool isEnemy = EcsWorld.Has<UnitFaction>(entity) ? EcsWorld.Get<UnitFaction>(entity).IsEnemy : NetworkService.ArePlayerIndicesEnemies(localPlayerIndex, playerIndex);

			units.Add(new UnitSaveData
			{
				TemplateId = defId.Value,
				PosX = pos.Value.X,
				PosY = pos.Value.Y,
				PosZ = pos.Value.Z,
				RotationY = rotY,
				Scale = scale,
				IsEnemy = isEnemy,
				Player = playerIndex
			});
		});

		return units;
	}

	private List<PropSaveData> GetPropSaveData(string directory)
	{
		var props = new List<PropSaveData>();
		var propQuery = Realm.Ecs.Common.QueryCache.AllPropIdentityAndPositionQuery;

		EcsWorld.Query(in propQuery, (Entity entity, ref PropIdentity propId, ref Position pos) =>
		{
			if (!IsValidPropObjectId(propId.PropId, directory)) return;

			float rotY = EcsWorld.Has<RotationY>(entity) ? EcsWorld.Get<RotationY>(entity).Value : 0f;
			float scale = EcsWorld.Has<ModelScale>(entity) ? EcsWorld.Get<ModelScale>(entity).Value : 1f;

			props.Add(new PropSaveData
			{
				TemplateId = propId.PropId,
				PosX = pos.Value.X,
				PosY = pos.Value.Y,
				PosZ = pos.Value.Z,
				RotationY = rotY,
				Scale = scale
			});
		});

		return props;
	}

	private List<DecalSaveData> GetDecalSaveData(HashSet<Entity> validDecalEntities)
	{
		var decals = new List<DecalSaveData>();
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

			float rotX = 0f, rotY = 0f, rotZ = 0f;
			if (EcsWorld.Has<Realm.Ecs.Components.Meta.Rotation3D>(entity))
			{
				var r3d = EcsWorld.Get<Realm.Ecs.Components.Meta.Rotation3D>(entity).Value;
				rotX = r3d.X; rotY = r3d.Y; rotZ = r3d.Z;
			}
			else if (EcsWorld.Has<RotationY>(entity))
			{
				rotY = EcsWorld.Get<RotationY>(entity).Value;
			}

			float scale = EcsWorld.Has<ModelScale>(entity) ? EcsWorld.Get<ModelScale>(entity).Value : 1f;

			string fingerprint = $"{decalId.DecalId}_{pos.Value.X:F3}_{pos.Value.Y:F3}_{pos.Value.Z:F3}_{rotX:F2}_{rotY:F2}_{rotZ:F2}_{scale:F3}";
			if (!savedDecalFingerprints.Add(fingerprint))
			{
				orphanedDecalEntities.Add(entity);
				return;
			}

			decals.Add(new DecalSaveData
			{
				TemplateId = decalId.DecalId,
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
			if (EcsWorld.IsAlive(orphan)) EcsWorld.Destroy(orphan);
		}

		return decals;
	}

	private List<VfxSaveData> GetVfxSaveData()
	{
		var vfxList = new List<VfxSaveData>();
		var allVfx = Realm.Client.Core.GameHost.Instance?.AllVfx;
		if (allVfx == null) return vfxList;

		foreach (var vfx in allVfx)
		{
			if (vfx == null || !GodotObject.IsInstanceValid(vfx)) continue;
			vfxList.Add(CreateVfxSaveData(vfx));
		}

		return vfxList;
	}

	private VfxSaveData CreateVfxSaveData(Realm.Client.VFX.ProceduralVfxInstance3D vfx)
	{
		var config = vfx.Config;
		return new VfxSaveData
		{
			VfxId = config?.VfxId ?? "vfx",
			PosX = vfx.Position.X,
			PosY = vfx.Position.Y,
			PosZ = vfx.Position.Z,
			RotationX = vfx.RotationDegrees.X,
			RotationY = vfx.RotationDegrees.Y,
			RotationZ = vfx.RotationDegrees.Z,
			ScaleX = vfx.Scale.X,
			ScaleY = vfx.Scale.Y,
			ScaleZ = vfx.Scale.Z,
			NormalOffset = config?.SurfaceNormalOffset ?? 0f,
			Config = config?.Clone()
		};
	}

	private void CleanMetadataFile(string directory, int width, int depth)
	{
		string metaPath = Path.Combine(directory, "metadata.json");
		if (!File.Exists(metaPath)) return;

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

	private void UpdateEditorState(EditorState editor, Arch.Core.QueryDescription worldQuery2)
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

	private void BackupWorkspaceIfNeeded(string directory)
	{
		int maxBackups = Realm.Client.UI.MapEditor.EditorSettingsDialog.CurrentSettings?.MaxBackupSnapshots ?? 3;
		if (maxBackups > 0)
		{
			string backupSourceDir = directory;
			_ = Task.Run(() => CreateWorkspaceBackup(backupSourceDir, maxBackups));
		}
	}

	private void CleanupOldMapEntities()
	{
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
	}

	private void DetermineMapDimensions(string mapDir, MapSaveData saveData, out int width, out int depth)
	{
		width = 0;
		depth = 0;
		
		if (MetadataService.Instance.TryLoadMetadata(mapDir, out var loadedMeta) && loadedMeta.MapProperties != null)
		{
			width = loadedMeta.MapProperties.MapWidth ?? 0;
			depth = loadedMeta.MapProperties.MapHeight ?? 0;
		}

		if (width <= 0) width = saveData.Width > 0 ? saveData.Width : 128;
		if (depth <= 0) depth = saveData.Depth > 0 ? saveData.Depth : 128;

		width = Math.Clamp((int)Math.Round(width / 32.0) * 32, 32, 512);
		depth = Math.Clamp((int)Math.Round(depth / 32.0) * 32, 32, 512);
	}

	private Entity InitializeTerrainState(int width, int depth)
	{
		Entity foundEntity = Entity.Null;
		var worldQuery = Realm.Ecs.Common.QueryCache.AllTerrainStateQuery;
		EcsWorld.Query(in worldQuery, (Entity entity) => foundEntity = entity);

		if (foundEntity == Entity.Null)
		{
			foundEntity = EcsWorld.Create();
		}

		if (!EcsWorld.Has<TerrainState>(foundEntity))
		{
			EcsWorld.Add(foundEntity, new TerrainState(width, depth, TerrainState.DefaultQuadSize, TerrainState.DefaultCellSize, new TerrainCell[width, depth], new int[width, depth], null, null));
		}
		else
		{
			ref var ts = ref EcsWorld.Get<TerrainState>(foundEntity);
			ts.Width = width;
			ts.Depth = depth;
			ts.Cells = new TerrainCell[width, depth];
			ts.PathingCodes = new int[width, depth];
			EcsWorld.Set(foundEntity, ts);
		}
		return foundEntity;
	}

	public bool LoadMapFromFile(string absolutePath, bool terrainOnly = false)
	{
		if (!File.Exists(absolutePath)) return false;

		try
		{
			var saveData = Realm.Shared.Services.MapFileService.LoadTerrain(absolutePath);
			if (saveData == null) return false;

			string mapDir = Path.GetDirectoryName(absolutePath);
			Realm.Client.Core.GameHost.Instance?.LoadModelYOffsetsFromMetadataJson(mapDir);

			_lastLoadedCoordinates = saveData.Coordinates ?? new List<CoordinateSaveData>();

			CleanupOldMapEntities();

			DetermineMapDimensions(mapDir, saveData, out int width, out int depth);
			saveData.Width = width;
			saveData.Depth = depth;

			Entity worldEntity = InitializeTerrainState(width, depth);

			if (EcsWorld.Has<TerrainState>(worldEntity))
			{
				LoadTerrainStateData(worldEntity, width, depth, mapDir);
			}

			LoadSplatColors(absolutePath, worldEntity, width, depth);
			LoadEditorAndCameraState(worldEntity, saveData);

			if (!terrainOnly)
			{
				SpawnEntitiesFromSaveData(saveData, mapDir);
			}

			return true;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine(ex.Message);
			return false;
		}
	}

	private void LoadTerrainStateData(Entity worldEntity, int width, int depth, string directory)
	{
		ref var ts = ref EcsWorld.Get<TerrainState>(worldEntity);

		if (ts.Cells == null || ts.Cells.GetLength(0) != width || ts.Cells.GetLength(1) != depth)
			ts.Cells = new TerrainCell[width, depth];
		if (ts.PathingCodes == null || ts.PathingCodes.GetLength(0) != width || ts.PathingCodes.GetLength(1) != depth)
			ts.PathingCodes = new int[width, depth];

		TerrainCell[,] unscaledCells = null;
		int unscaledW = 0, unscaledH = 0;

		LoadTerrainHeights(directory, width, depth, ref ts, ref unscaledCells, ref unscaledW, ref unscaledH);
		LoadTerrainWater(directory, width, depth, ref ts, ref unscaledCells, ref unscaledW, ref unscaledH);
		LoadTerrainPathing(directory, width, depth, ref ts);

		if (unscaledCells != null)
		{
			Realm.Client.RuntimeTerrain.ReconcileScaledWater(unscaledCells, unscaledW, unscaledH, ts.Cells, ts.PathingCodes, width, depth);
		}

		EcsWorld.Set(worldEntity, ts);
	}

	private void LoadTerrainHeights(string directory, int width, int depth, ref TerrainState ts, ref TerrainCell[,] unscaledCells, ref int unscaledW, ref int unscaledH)
	{
		string heightsPath = Path.Combine(directory, "terrain_heights.exr");
		if (!File.Exists(heightsPath)) return;

		Image heightsImage = Image.LoadFromFile(heightsPath);
		if (heightsImage == null) return;

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
					ts.Cells[x, z] = new TerrainCell(floatData[baseIdx], floatData[baseIdx + 1], floatData[baseIdx + 2], floatData[baseIdx + 3]);
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
					unscaledCells[x, z] = new TerrainCell(floatData[baseIdx], floatData[baseIdx + 1], floatData[baseIdx + 2], floatData[baseIdx + 3]);
				}
			}

			ScaleTerrainHeights(width, depth, imgW, imgH, floatData, ref ts);
		}
	}

	private void ScaleTerrainHeights(int width, int depth, int imgW, int imgH, ReadOnlySpan<float> floatData, ref TerrainState ts)
	{
		float[,] newGridHeights = new float[width + 1, depth + 1];
		for (int vz = 0; vz <= depth; vz++)
		{
			for (int vx = 0; vx <= width; vx++)
			{
				int srcVx = Math.Clamp((int)Math.Round(vx * (float)imgW / width), 0, imgW);
				int srcVz = Math.Clamp((int)Math.Round(vz * (float)imgH / depth), 0, imgH);
				newGridHeights[vx, vz] = GetInterpolatedHeight(srcVx, srcVz, imgW, imgH, floatData);
			}
		}

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				ts.Cells[x, z] = new TerrainCell(newGridHeights[x, z], newGridHeights[x + 1, z], newGridHeights[x + 1, z + 1], newGridHeights[x, z + 1]);
			}
		}
	}

	private float GetInterpolatedHeight(int srcVx, int srcVz, int imgW, int imgH, ReadOnlySpan<float> floatData)
	{
		if (srcVx < imgW && srcVz < imgH) return floatData[(srcVz * imgW + srcVx) * 4 + 0];
		if (srcVx >= imgW && srcVz >= imgH) return floatData[((imgH - 1) * imgW + (imgW - 1)) * 4 + 2];
		if (srcVx >= imgW) return floatData[(srcVz * imgW + (imgW - 1)) * 4 + 1];
		if (srcVz >= imgH) return floatData[((imgH - 1) * imgW + srcVx) * 4 + 3];
		return 0f;
	}

	private void LoadTerrainWater(string directory, int width, int depth, ref TerrainState ts, ref TerrainCell[,] unscaledCells, ref int unscaledW, ref int unscaledH)
	{
		string waterPath = Path.Combine(directory, "terrain_water.exr");
		if (!File.Exists(waterPath)) return;

		Image waterImage = Image.LoadFromFile(waterPath);
		if (waterImage == null) return;

		waterImage.Convert(Image.Format.Rgbaf);
		int imgW = waterImage.GetWidth();
		int imgH = waterImage.GetHeight();
		ReadOnlySpan<float> waterFloatData = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(waterImage.GetData());

		if (imgW == width && imgH == depth && unscaledCells == null)
		{
			PopulateWaterScaled(width, depth, ref ts, waterFloatData, imgW);
		}
		else
		{
			PopulateWaterUnscaled(imgW, imgH, ref unscaledCells, ref unscaledW, ref unscaledH, waterFloatData);
		}
	}

	private void PopulateWaterScaled(int width, int depth, ref TerrainState ts, ReadOnlySpan<float> waterFloatData, int imgW)
	{
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				int baseIdx = (z * imgW + x) * 4;
				ts.Cells[x, z].WaterMode = (WaterType)Math.Clamp((int)MathF.Round(waterFloatData[baseIdx + 0]), 0, 2);
				ts.Cells[x, z].WaterProfileIndex = (byte)Math.Clamp((int)MathF.Round(waterFloatData[baseIdx + 1]), 0, 255);
				ts.Cells[x, z].WaterHeight = waterFloatData[baseIdx + 2];
			}
		}
	}

	private void PopulateWaterUnscaled(int imgW, int imgH, ref TerrainCell[,] unscaledCells, ref int unscaledW, ref int unscaledH, ReadOnlySpan<float> waterFloatData)
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
				unscaledCells[x, z].WaterMode = (WaterType)Math.Clamp((int)MathF.Round(waterFloatData[baseIdx + 0]), 0, 2);
				unscaledCells[x, z].WaterProfileIndex = (byte)Math.Clamp((int)MathF.Round(waterFloatData[baseIdx + 1]), 0, 255);
				unscaledCells[x, z].WaterHeight = waterFloatData[baseIdx + 2];
			}
		}
	}

	private void LoadTerrainPathing(string directory, int width, int depth, ref TerrainState ts)
	{
		string pathingPath = Path.Combine(directory, "terrain_pathing.png");
		if (!File.Exists(pathingPath))
		{
			SetDefaultPathing(width, depth, ref ts);
			return;
		}

		Image pathingImage = Image.LoadFromFile(pathingPath);
		if (pathingImage == null)
		{
			SetDefaultPathing(width, depth, ref ts);
			return;
		}

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
	}

	private void SetDefaultPathing(int width, int depth, ref TerrainState ts)
	{
		int defaultPathing = Realm.Client.EditableTerrain.GetDefaultPathingCode(Realm.Ecs.Components.Terrain.WaterType.None);
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				ts.PathingCodes[x, z] = defaultPathing;
			}
		}
	}

	private void LoadSplatColors(string absolutePath, Entity worldEntity, int width, int depth)
	{
		string splatIndicesPath = Path.Combine(Path.GetDirectoryName(absolutePath), "terrain_splat_indices.exr");
		string splatWeightsPath = Path.Combine(Path.GetDirectoryName(absolutePath), "terrain_splat_weights.exr");
		
		string[] loadedColors = null;

		if (File.Exists(splatIndicesPath) && File.Exists(splatWeightsPath))
		{
			loadedColors = ReadSplatImages(splatIndicesPath, splatWeightsPath, width, depth);
		}

		if (loadedColors == null)
		{
			loadedColors = new string[width * depth];
			string defaultSolid = TerrainSplatWeights.CreateSolid(0).Serialize();
			for (int i = 0; i < loadedColors.Length; i++)
			{
				loadedColors[i] = defaultSolid;
			}
		}

		EcsWorld.SetOrAdd(worldEntity, new TerrainColorsState(loadedColors));
	}

	private string[] ReadSplatImages(string splatIndicesPath, string splatWeightsPath, int width, int depth)
	{
		Image splatIndicesImage = Image.LoadFromFile(splatIndicesPath);
		Image splatWeightsImage = Image.LoadFromFile(splatWeightsPath);
		
		if (splatIndicesImage == null || splatWeightsImage == null) return null;

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
		string[] loadedColors = new string[splatW * splatD];

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

				var s = new TerrainSplatWeights
				{
					Index0 = (int)Math.Round(idxData[idxOffset + 0]),
					Index1 = (int)Math.Round(idxData[idxOffset + 1]),
					Index2 = (int)Math.Round(idxData[idxOffset + 2]),
					Index3 = (int)Math.Round(idxData[idxOffset + 3]),
					Weight0 = wgtData[weightOffset + 0],
					Weight1 = wgtData[weightOffset + 1],
					Weight2 = wgtData[weightOffset + 2],
					Weight3 = wgtData[weightOffset + 3]
				};

				loadedColors[z * splatW + x] = s.Serialize();
			}
		}

		return loadedColors;
	}

	private void LoadEditorAndCameraState(Entity worldEntity, MapSaveData saveData)
	{
		float left = saveData.CameraBoundsLeft ?? -95.0f;
		float right = saveData.CameraBoundsRight ?? 95.0f;
		float top = saveData.CameraBoundsTop ?? -95.0f;
		float bottom = saveData.CameraBoundsBottom ?? 125.0f;

		WaterType currentWaterMode = EcsWorld.Has<EditorState>(worldEntity) ? EcsWorld.Get<EditorState>(worldEntity).WaterMode : WaterType.None;
		byte currentWaterProf = EcsWorld.Has<EditorState>(worldEntity) ? EcsWorld.Get<EditorState>(worldEntity).WaterProfileIndex : (byte)0;
		var newEditorState = new EditorState(true, Realm.Client.EditableTerrain.TIER_HEIGHT, left, right, top, bottom, saveData.SkyboxPath, false, MirrorMode.None, currentWaterMode, currentWaterProf);
		EcsWorld.SetOrAdd(worldEntity, newEditorState);

		if (EcsWorld.Has<CameraState>(worldEntity))
		{
			ref var camState = ref EcsWorld.Get<CameraState>(worldEntity);
			camState.LimitLeft = left;
			camState.LimitRight = right;
			camState.LimitTop = top;
			camState.LimitBottom = bottom;
		}
	}

	private void SpawnEntitiesFromSaveData(MapSaveData saveData, string mapDir)
	{
		SpawnUnits(saveData.Units, mapDir);
		SpawnProps(saveData.Props, mapDir);
		SpawnDecals(saveData.Decals);
		SpawnVfx(saveData.Vfx);
	}

	private void SpawnUnits(List<UnitSaveData>? units, string mapDir)
	{
		if (units == null) return;
		foreach (var u in units)
		{
			if (!IsValidUnitObjectId(u.TemplateId, mapDir))
			{
				GD.PushWarning($"[SaveLoadService] Ignored invalid unit '{u.TemplateId}' in terrain.json because it does not exist as an Object ID in metadata.json.");
				continue;
			}
			EcsWorld.Add(EcsWorld.Create(), new UnitSpawnRequest(u.TemplateId, new System.Numerics.Vector3(u.PosX, u.PosY, u.PosZ), u.RotationY, u.Scale, u.IsEnemy, u.Player));
		}
	}

	private void SpawnProps(List<PropSaveData>? props, string mapDir)
	{
		if (props == null) return;
		foreach (var p in props)
		{
			if (!IsValidPropObjectId(p.TemplateId, mapDir))
			{
				GD.PushWarning($"[SaveLoadService] Ignored invalid prop '{p.TemplateId}' in terrain.json because it does not exist as an Object ID in metadata.json.");
				continue;
			}
			EcsWorld.Add(EcsWorld.Create(), new PropSpawnRequest(p.TemplateId, new System.Numerics.Vector3(p.PosX, p.PosY, p.PosZ), p.RotationY, p.Scale));
		}
	}

	private void SpawnDecals(List<DecalSaveData>? decals)
	{
		if (decals == null) return;
		var loadedDecalFingerprints = new HashSet<string>();
		foreach (var d in decals)
		{
			if (!loadedDecalFingerprints.Add($"{d.TemplateId}_{d.PosX:F3}_{d.PosY:F3}_{d.PosZ:F3}_{d.RotationX:F2}_{d.RotationY:F2}_{d.RotationZ:F2}_{d.Scale:F3}")) continue;
			EcsWorld.Add(EcsWorld.Create(), new DecalSpawnRequest(d.TemplateId, new System.Numerics.Vector3(d.PosX, d.PosY, d.PosZ), new System.Numerics.Vector3(d.RotationX, d.RotationY, d.RotationZ), d.Scale));
		}
	}

	private void SpawnVfx(List<VfxSaveData>? vfxList)
	{
		if (vfxList == null || Realm.Client.Core.GameHost.Instance == null) return;
		foreach (var v in vfxList)
		{
			Realm.Client.Core.GameHost.Instance.SpawnVfxExternalWithParams(
				v.VfxId, new Vector3(v.PosX, v.PosY, v.PosZ), new Vector3(v.RotationX, v.RotationY, v.RotationZ),
				new Vector3(v.ScaleX <= 0f ? 1f : v.ScaleX, v.ScaleY <= 0f ? 1f : v.ScaleY, v.ScaleZ <= 0f ? 1f : v.ScaleZ),
				v.NormalOffset, v.Config
			);
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
			RemapSingleSplatExrFile(file, remap);
		}
	}

	private static void RemapSingleSplatExrFile(string file, IReadOnlyDictionary<int, int> remap)
	{
		if (!File.Exists(file)) return;
		try
		{
			var img = Image.LoadFromFile(file);
			if (img == null) return;
			
			img.Convert(Image.Format.Rgbaf);
			int w = img.GetWidth();
			int h = img.GetHeight();
			byte[] data = img.GetData();
			Span<float> floats = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(data.AsSpan());
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
		catch (Exception ex)
		{
			GD.PrintErr($"[SaveLoadService] Failed to remap splat EXR {file}: {ex.Message}");
		}
	}

	private static int CompareUnits(UnitSaveData a, UnitSaveData b, float topLeftX, float topLeftZ)
	{
		int comparison = string.Compare(a.TemplateId, b.TemplateId, StringComparison.OrdinalIgnoreCase);
		if (comparison != 0) return comparison;
		comparison = string.Compare(a.TemplateId, b.TemplateId, StringComparison.Ordinal);
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
	}

	private static int CompareProps(PropSaveData a, PropSaveData b, float topLeftX, float topLeftZ)
	{
		int comparison = string.Compare(a.TemplateId, b.TemplateId, StringComparison.OrdinalIgnoreCase);
		if (comparison != 0) return comparison;
		comparison = string.Compare(a.TemplateId, b.TemplateId, StringComparison.Ordinal);
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
	}

	private static int CompareDecals(DecalSaveData a, DecalSaveData b, float topLeftX, float topLeftZ)
	{
		int comparison = string.Compare(a.TemplateId, b.TemplateId, StringComparison.OrdinalIgnoreCase);
		if (comparison != 0) return comparison;
		comparison = string.Compare(a.TemplateId, b.TemplateId, StringComparison.Ordinal);
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
	}

	private static int CompareVfx(VfxSaveData a, VfxSaveData b, float topLeftX, float topLeftZ)
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
	}

	private static int CompareCoordinates(CoordinateSaveData a, CoordinateSaveData b, float topLeftX, float topLeftZ)
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
			saveData.Units.Sort((a, b) => CompareUnits(a, b, topLeftX, topLeftZ));
		}

		if (saveData.Props != null)
		{
			saveData.Props.Sort((a, b) => CompareProps(a, b, topLeftX, topLeftZ));
		}

		if (saveData.Decals != null)
		{
			saveData.Decals.Sort((a, b) => CompareDecals(a, b, topLeftX, topLeftZ));
		}

		if (saveData.Vfx != null)
		{
			saveData.Vfx.Sort((a, b) => CompareVfx(a, b, topLeftX, topLeftZ));
		}

		if (saveData.Coordinates != null)
		{
			saveData.Coordinates.Sort((a, b) => CompareCoordinates(a, b, topLeftX, topLeftZ));
		}


	}

	private static bool IsValidWorkspaceForBackup(string fullWsPath, string globalBackupsRoot, string globalUpgradesRoot)
	{
		string fullUserDataDir = Path.GetFullPath(OS.GetUserDataDir()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string resGlobalPath = Path.GetFullPath(ProjectSettings.GlobalizePath("res://")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		if (string.Equals(fullWsPath, fullUserDataDir, StringComparison.OrdinalIgnoreCase)) return false;
		if (string.Equals(fullWsPath, resGlobalPath, StringComparison.OrdinalIgnoreCase)) return false;

		if (IsPathInsideRestrictedDirectory(fullWsPath, globalBackupsRoot, globalUpgradesRoot)) return false;
		if (IsInvalidWorkspaceName(Path.GetFileName(fullWsPath))) return false;

		return true;
	}

	private static bool IsPathInsideRestrictedDirectory(string fullWsPath, string globalBackupsRoot, string globalUpgradesRoot)
	{
		if (fullWsPath.Equals(globalBackupsRoot, StringComparison.OrdinalIgnoreCase)) return true;
		if (fullWsPath.StartsWith(globalBackupsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
		if (fullWsPath.Equals(globalUpgradesRoot, StringComparison.OrdinalIgnoreCase)) return true;
		if (fullWsPath.StartsWith(globalUpgradesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
		
		if (fullWsPath.Contains("map_backups", StringComparison.OrdinalIgnoreCase)) return true;
		if (fullWsPath.Contains("map_upgrades", StringComparison.OrdinalIgnoreCase)) return true;
		if (fullWsPath.Contains(".backups", StringComparison.OrdinalIgnoreCase)) return true;

		return false;
	}

	private static bool IsInvalidWorkspaceName(string wsName)
	{
		if (string.IsNullOrEmpty(wsName)) return true;
		if (string.Equals(wsName, "map_backups", StringComparison.OrdinalIgnoreCase)) return true;
		if (string.Equals(wsName, "map_upgrades", StringComparison.OrdinalIgnoreCase)) return true;
		if (string.Equals(wsName, "backups", StringComparison.OrdinalIgnoreCase)) return true;
		if (string.Equals(wsName, ".backups", StringComparison.OrdinalIgnoreCase)) return true;
		if (wsName.StartsWith("backup_", StringComparison.OrdinalIgnoreCase)) return true;

		return false;
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

			if (!IsValidWorkspaceForBackup(fullWsPath, globalBackupsRoot, globalUpgradesRoot))
			{
				return string.Empty;
			}

			string wsName = Path.GetFileName(fullWsPath);
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

	private static bool ShouldExcludeDirectory(DirectoryInfo subDir, string subDirFull, string normalizedTarget, string normalizedBackupsRoot, HashSet<string> excludedFolders)
	{
		if (excludedFolders.Contains(subDir.Name) || subDir.Name.StartsWith("backup_", StringComparison.OrdinalIgnoreCase))
			return true;

		if (ContainsExcludedPath(subDirFull))
			return true;

		if (!string.IsNullOrEmpty(normalizedBackupsRoot) && IsSubPathOf(subDirFull, normalizedBackupsRoot))
			return true;

		if (IsSubPathOf(subDirFull, normalizedTarget) || IsSubPathOf(normalizedTarget, subDirFull))
			return true;

		return false;
	}

	private static bool ContainsExcludedPath(string path)
	{
		return path.IndexOf("map_backups", StringComparison.OrdinalIgnoreCase) >= 0 ||
		       path.IndexOf("map_upgrades", StringComparison.OrdinalIgnoreCase) >= 0 ||
		       path.IndexOf(".backups", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static bool IsSubPathOf(string path, string basePath)
	{
		return string.Equals(path, basePath, StringComparison.OrdinalIgnoreCase) ||
		       path.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	}

	private static void CopyFilesParallel(List<(string SourcePath, string DestPath)> filesToCopy)
	{
		Parallel.ForEach(filesToCopy, pair =>
		{
			try
			{
				using var srcStream = new FileStream(pair.SourcePath, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
				using var dstStream = new FileStream(pair.DestPath, FileMode.Create, System.IO.FileAccess.Write, FileShare.ReadWrite);
				srcStream.CopyTo(dstStream);
			}
			catch { }
		});
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
				string subDirFull = Path.GetFullPath(subDir.FullName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				if (ShouldExcludeDirectory(subDir, subDirFull, normalizedTarget, normalizedBackupsRoot, excludedFolders))
				{
					continue;
				}

				dirsToProcess.Enqueue((subDir, Path.Combine(curTarget, subDir.Name)));
			}
		}

		CopyFilesParallel(filesToCopy);
	}

	private static bool SaveExrSafe(Image image, string path, int maxRetries = 5)
	{
		for (int i = 0; i < maxRetries; i++)
		{
			var err = image.SaveExr(path);
			if (err == Error.Ok) return true;
			System.Threading.Thread.Sleep(25);
		}
		GD.PrintErr($"[SaveLoadService] Failed to save EXR file: {path}");
		return false;
	}

	private static bool SavePngSafe(Image image, string path, int maxRetries = 5)
	{
		for (int i = 0; i < maxRetries; i++)
		{
			var err = image.SavePng(path);
			if (err == Error.Ok) return true;
			System.Threading.Thread.Sleep(25);
		}
		GD.PrintErr($"[SaveLoadService] Failed to save PNG file: {path}");
		return false;
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

	public static bool IsValidPropObjectId(string propId, string mapDirectory = null)
	{
		if (string.IsNullOrWhiteSpace(propId)) return false;

		if (Realm.Client.Core.GameHost.PropRegistry?.ContainsKey(propId) == true || Realm.Client.Core.GameHost.ResourceRegistry?.ContainsKey(propId) == true) return true;

		return HasPropMetadata(propId, mapDirectory);
	}

	private static bool HasPropMetadata(string propId, string mapDirectory)
	{
		string targetDir = !string.IsNullOrEmpty(mapDirectory) ? mapDirectory : MapWorkspaceService.GetActiveWorkspacePath();
		if (!MetadataService.Instance.TryLoadMetadata(targetDir, out var metadata) || metadata.Templates == null) return false;

		bool hasProp = metadata.Templates.Props?.Any(p => string.Equals(propId, p.TemplateID, StringComparison.OrdinalIgnoreCase)) ?? false;
		if (hasProp) return true;

		return metadata.Templates.Resources?.Any(r => string.Equals(propId, r.TemplateID, StringComparison.OrdinalIgnoreCase)) ?? false;
	}

	public static bool IsValidUnitObjectId(string unitId, string mapDirectory = null)
	{
		if (string.IsNullOrWhiteSpace(unitId)) return false;

		if (Realm.Client.Core.GameHost.UnitRegistry?.ContainsKey(unitId) == true || Realm.Client.Core.GameHost.BuildingRegistry?.ContainsKey(unitId) == true) return true;

		return HasUnitMetadata(unitId, mapDirectory);
	}

	private static bool HasUnitMetadata(string unitId, string mapDirectory)
	{
		string targetDir = !string.IsNullOrEmpty(mapDirectory) ? mapDirectory : MapWorkspaceService.GetActiveWorkspacePath();
		if (!MetadataService.Instance.TryLoadMetadata(targetDir, out var metadata) || metadata.Templates == null) return false;

		bool hasUnit = metadata.Templates.Units?.Any(u => string.Equals(unitId, u.TemplateID, StringComparison.OrdinalIgnoreCase)) ?? false;
		if (hasUnit) return true;

		return metadata.Templates.Buildings?.Any(b => string.Equals(unitId, b.TemplateID, StringComparison.OrdinalIgnoreCase)) ?? false;
	}



	private static readonly Dictionary<string, string> CategorySubFolderMap = new(StringComparer.OrdinalIgnoreCase)
	{
		{ "character", "models/units" },
		{ "building", "models/buildings" },
		{ "prop", "models/props" },
		{ "item", "models/items" },
		{ "spritesheet", "vfx_spritesheets" }, { "vfx", "vfx_spritesheets" }, { "vfx_spritesheets", "vfx_spritesheets" },
		{ "animation", "animations" }, { "animations", "animations" },
		{ "soundeffect", "audio/sfx" }, { "sfx", "audio/sfx" },
		{ "music", "audio/music" },
		{ "icon", "icons" }, { "icons", "icons" },
		{ "decal", "decals" }, { "decals", "decals" },
		{ "ribbon", "ribbons" }, { "ribbons", "ribbons" }, { "ribbon_textures", "ribbons" },
		{ "noise", "noise" }, { "noise_textures", "noise" },
		{ "skybox", "skyboxes" }, { "skyboxes", "skyboxes" },
		{ "terrain", "textures" }, { "textures", "textures" },
		{ "shader", "shaders" }, { "shaders", "shaders" }
	};

	private static string GetSubFolderForCategory(string category)
	{
		return CategorySubFolderMap.TryGetValue(category, out string folder) ? folder : category.ToLowerInvariant();
	}

	private static string ResolveFullDiskPath(string mapDirectory, string assetsDir, string relPath, string category, string baseFileName, ref Dictionary<string, string>? cachedAssetFiles)
	{
		string fullDiskPath = Path.Combine(mapDirectory, relPath);
		if (!File.Exists(fullDiskPath))
		{
			if (category is "Character" or "Building" or "Prop" or "Item")
			{
				string? modelDisk = MapAssetHelper.FindModelOnDisk(mapDirectory, category, baseFileName);
				if (!string.IsNullOrEmpty(modelDisk) && File.Exists(modelDisk))
				{
					fullDiskPath = modelDisk;
				}
			}
			else
			{
				string? altPath = FindAssetFileByName(assetsDir, baseFileName, ref cachedAssetFiles);
				if (altPath != null && File.Exists(altPath))
				{
					fullDiskPath = altPath;
				}
				else
				{
					string directMapPath = Path.Combine(mapDirectory, baseFileName);
					if (File.Exists(directMapPath))
					{
						fullDiskPath = directMapPath;
					}
				}
			}
		}
		return fullDiskPath;
	}

	public static void SyncMetadataAssetsAndPrune(string mapDirectory)
	{
		if (string.IsNullOrEmpty(mapDirectory) || !Directory.Exists(mapDirectory)) return;

		try
		{
			var assetsObj = MapAssetHelper.LoadAssets(mapDirectory);
			string assetsDir = Path.Combine(mapDirectory, "Assets");

			var assetsToSync = CollectAssetsToSync(assetsObj);

			PruneImportFilesAndEmptyDirs(assetsDir);

			SyncAssetHashes(mapDirectory, assetsDir, assetsObj, assetsToSync);

			MapAssetHelper.SaveAssetsToManifest(mapDirectory, assetsObj, removeFromMetadata: true);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[SaveLoadService] SyncMetadataAssetsAndPrune error: {ex.Message}");
		}
	}

	private static List<(string RelativePath, string Category, string FileName)> CollectAssetsToSync(Realm.Shared.Distribution.MapManifestAssets assetsObj)
	{
		var assetsToSync = new List<(string RelativePath, string Category, string FileName)>();
		foreach (var categoryKvp in assetsObj.GetAllCategories())
		{
			string category = categoryKvp.Key;
			string subFolder = GetSubFolderForCategory(category);
			foreach (var itemKvp in categoryKvp.Value)
			{
				string fileName = itemKvp.Key;
				string relPath = Path.Combine("Assets", subFolder, fileName).Replace('\\', '/');
				assetsToSync.Add((relPath, category, fileName));
			}
		}
		return assetsToSync;
	}

	private static void PruneImportFilesAndEmptyDirs(string assetsDir)
	{
		if (!Directory.Exists(assetsDir)) return;

		string[] importFiles = Directory.GetFiles(assetsDir, "*.import", SearchOption.AllDirectories);
		foreach (var importFile in importFiles)
		{
			string sourceFile = importFile.Substring(0, importFile.Length - ".import".Length);
			if (!File.Exists(sourceFile))
			{
				try { File.Delete(importFile); } catch { }
			}
		}

		DeleteEmptyDirectoriesRecursive(assetsDir);
	}

	private static void SyncAssetHashes(string mapDirectory, string assetsDir, Realm.Shared.Distribution.MapManifestAssets assetsObj, List<(string RelativePath, string Category, string FileName)> assetsToSync)
	{
		Dictionary<string, string>? cachedAssetFiles = null;

		foreach (var (relPath, category, fileName) in assetsToSync)
		{
			string baseFileName = Path.GetFileName(relPath);
			string fullDiskPath = ResolveFullDiskPath(mapDirectory, assetsDir, relPath, category, baseFileName, ref cachedAssetFiles);

			if (!File.Exists(fullDiskPath)) continue;

			string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(fullDiskPath);
			if (string.IsNullOrEmpty(canonicalBlake3)) continue;

			var catDict = assetsObj.GetCategory(category);
			if (catDict != null)
			{
				catDict[fileName] = canonicalBlake3;
			}

			RealmMetadataHelper.SyncBlake3Metadata(fullDiskPath, canonicalBlake3);
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