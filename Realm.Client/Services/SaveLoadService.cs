using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Client.VFX;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Terrain;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using FileAccess = System.IO.FileAccess;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Services;

public class SaveLoadService
{
	private List<CoordinateSaveData> _lastLoadedCoordinates = new();
	private List<SpawnAnchorSaveData> _lastLoadedSpawnAnchors = new();

	public bool IsLoadingMap { get; set; }

	// A save or load is meaningless before a world exists, and this makes that failure name itself
	// instead of surfacing as a NullReferenceException from somewhere inside a query.
	private static EcsWorld World => GameContext.World
		?? throw new InvalidOperationException("SaveLoadService used before a world existed.");

	public JsonSerializerOptions Options { get; } = new()
	{
		PropertyNameCaseInsensitive = true,
		IncludeFields = true,
		Converters = { new JsonStringEnumConverter() },
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	public TerrainSplatWeights[,] GenerateFallbackSplatMap(int splatW, int splatD, int width, int depth)
	{
		TerrainColorsState colorsState = default;
		bool foundColors = false;
		var worldQuery = QueryCache.AllTerrainStateAndEditorStateQuery;
		World.Query(in worldQuery, (Entity entity) =>
		{
			if (World.Has<TerrainColorsState>(entity))
			{
				colorsState = World.Get<TerrainColorsState>(entity);
				foundColors = true;
			}
		});

		var splatMap = new TerrainSplatWeights[splatW, splatD];
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
						int srcX = Math.Clamp(x, 0, width - 1);
						int srcZ = Math.Clamp(z, 0, depth - 1);
						idx = srcZ * width + srcX;
					}

					if (idx < colorLen)
					{
						string serialized = colorsState.Colors[idx];
						splatMap[x, z] = TerrainSplatWeights.Deserialize(serialized);
					}
					else
					{
						splatMap[x, z] = TerrainSplatWeights.CreateSolid(0);
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
					splatMap[x, z] = TerrainSplatWeights.CreateSolid(0);
				}
			}
		}

		return splatMap;
	}

	public void CopyPathingCodes(int[,] source, int[,] dest, int width, int depth)
	{
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				dest[x, z] = source[x, z];
			}
		}
	}

	public List<CoordinateSaveData> GetLastLoadedCoordinates() => _lastLoadedCoordinates;

	public List<SpawnAnchorSaveData> GetLastLoadedSpawnAnchors() => _lastLoadedSpawnAnchors;

	private void UpdateEntityTransforms(Entity entity, Vector3? position, float rotationY, float scale)
	{
		if (!World.IsAlive(entity)) return;

		if (position.HasValue)
		{
			if (World.Has<Position>(entity))
			{
				var existingPos = World.Get<Position>(entity).Value;
				if ((position.Value - existingPos).Length() > 0.0001f)
				{
					World.Set(entity, new Position(position.Value));
				}
			}
			else
			{
				World.Add(entity, new Position(position.Value));
			}
		}

		if (World.Has<RotationY>(entity))
		{
			float existing = World.Get<RotationY>(entity).Value;
			if (MathF.Abs(existing - rotationY) > 0.001f && MathF.Abs(MathF.Abs(existing - rotationY) - 360f) > 0.001f)
			{
				World.Set(entity, new RotationY(rotationY));
			}
		}
		else
		{
			World.Add(entity, new RotationY(rotationY));
		}

		if (World.Has<ModelScale>(entity))
		{
			float existing = World.Get<ModelScale>(entity).Value;
			if (MathF.Abs(existing - scale) > 0.0001f)
			{
				World.Set(entity, new ModelScale(scale));
			}
		}
		else
		{
			World.Add(entity, new ModelScale(scale));
		}
	}

	public bool SaveMapToFile(
		string absolutePath,
		string[] htmlColors,
		(Entity Entity, float RotationY, float Scale)[] unitsData,
		(Entity Entity, float RotationY, float Scale)[] propsData,
		(Entity Entity, Vector3 Position, float RotationY, float Scale)[] decalsData,
		List<CoordinateSaveData>? coordinatesData = null,
		string[]? cliffHtmlColors = null,
		Action<MapTerrainSaveRequest>? terrainWriter = null)
	{
		try
		{
			Entity worldEntity = Entity.Null;
			var worldQuery = QueryCache.AllTerrainStateQuery;
			World.Query(in worldQuery, (Entity entity) => worldEntity = entity);

			if (worldEntity != Entity.Null)
			{
				World.SetOrAdd(worldEntity, new TerrainColorsState(htmlColors));
			}

			UpdateEntityTransforms(unitsData, propsData, decalsData);

			var validDecalEntities = decalsData != null
				? new HashSet<Entity>(decalsData.Select(d => d.Entity))
				: null;

			TerrainState terrain = default;
			EditorState editor = default;
			bool foundWorld = false;

			var worldQuery2 = QueryCache.AllTerrainStateAndEditorStateQuery;
			World.Query(in worldQuery2, (Entity entity, ref TerrainState t, ref EditorState e) =>
			{
				terrain = t;
				editor = e;
				foundWorld = true;
			});

			if (!foundWorld) return false;

			string? directory = Path.GetDirectoryName(absolutePath);
			if (string.IsNullOrEmpty(directory)) return false;
			if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

			var terrainImages = new MapTerrainImageData(
				directory,
				terrain.Width,
				terrain.Depth,
				CloneTerrainCells(terrain.Cells),
				ClonePathingCodes(terrain.PathingCodes),
				htmlColors,
				cliffHtmlColors);

			var saveData = CreateInitialMapSaveData(terrain, editor, coordinatesData);
			PopulateMapSaveData(saveData, directory, validDecalEntities);

			MapSaveOrdering.SortMapSaveData(saveData);
			var terrainSaveRequest = new MapTerrainSaveRequest
			{
				AbsolutePath = absolutePath,
				SaveData = saveData,
				Images = terrainImages
			};

			if (terrainWriter != null)
			{
				terrainWriter(terrainSaveRequest);
			}
			else
			{
				terrainSaveRequest.WriteToDisk();
			}

			GameContext.Host?.SaveModelYOffsetsToMetadataJson(directory);
			MapAssetSync.SyncMetadataAssetsAndPrune(directory);
			CleanMetadataFile(directory, terrain.Width, terrain.Depth);
			WorkspaceMetadataBootstrapper.EnsureLicenseFile(directory);

			if (foundWorld)
			{
				UpdateEditorState(editor, worldQuery2);
			}

			return true;
		}
		catch (Exception ex)
		{
			GameLog.LogError(ex.Message);
			return false;
		}
	}

	/// <summary>
	///     Carries the placements the editor does not author into the terrain it is about to write: the neutral objective
	///     site declarations the board holds. The file is rewritten whole, so anything the save does not read back from
	///     disk is lost, and a site belongs to the board rather than to a script.
	/// </summary>
	/// <param name="saveData">The terrain being written.</param>
	/// <param name="absolutePath">The terrain file the map currently holds.</param>
	private static TerrainCell[,]? CloneTerrainCells(TerrainCell[,]? cells)
	{
		return cells == null ? null : (TerrainCell[,])cells.Clone();
	}

	private static int[,]? ClonePathingCodes(int[,]? pathingCodes)
	{
		return pathingCodes == null ? null : (int[,])pathingCodes.Clone();
	}

	private void UpdateEntityTransforms(
		(Entity Entity, float RotationY, float Scale)[] unitsData,
		(Entity Entity, float RotationY, float Scale)[] propsData,
		(Entity Entity, Vector3 Position, float RotationY, float Scale)[] decalsData)
	{
		foreach (var u in unitsData) UpdateEntityTransforms(u.Entity, null, u.RotationY, u.Scale);
		foreach (var p in propsData) UpdateEntityTransforms(p.Entity, null, p.RotationY, p.Scale);

		if (decalsData != null)
		{
			foreach (var d in decalsData) UpdateEntityTransforms(d.Entity, d.Position, d.RotationY, d.Scale);
		}
	}

	private MapSaveData CreateInitialMapSaveData(TerrainState terrain, EditorState editor, List<CoordinateSaveData>? coordinatesData)
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
			Coordinates = coordinatesData ?? new List<CoordinateSaveData>(),
			SpawnAnchors = CopySpawnAnchors(GameContext.EditorService.EditorSpawnAnchors)
		};
	}

	private static List<SpawnAnchorSaveData> CopySpawnAnchors(List<EditorSpawnAnchor> anchors)
	{
		var saved = new List<SpawnAnchorSaveData>(anchors.Count);

		for (int index = 0; index < anchors.Count; index++)
		{
			EditorSpawnAnchor anchor = anchors[index];
			saved.Add(new SpawnAnchorSaveData { Player = anchor.Player, PosX = anchor.PositionX, PosZ = anchor.PositionZ });
		}

		return saved;
	}

	private void PopulateMapSaveData(MapSaveData saveData, string directory, HashSet<Entity>? validDecalEntities)
	{
		saveData.Units = GetUnitSaveData(directory);
		saveData.Props = GetPropSaveData(directory);
		saveData.Decals = GetDecalSaveData(validDecalEntities);
		saveData.Vfx = GetVfxSaveData();
	}

	private List<UnitSaveData> GetUnitSaveData(string directory)
	{
		var units = new List<UnitSaveData>();
		var unitQuery = QueryCache.AllDefinitionIdAndPositionAndOwnerQuery;

		World.Query(in unitQuery, (Entity entity, ref DefinitionId defId, ref Position pos, ref Owner owner) =>
		{
			if (!MapAssetSync.IsValidUnitObjectId(defId.Value, directory)) return;

			float rotY = World.Has<RotationY>(entity) ? World.Get<RotationY>(entity).Value : 0f;
			float scale = World.Has<ModelScale>(entity) ? World.Get<ModelScale>(entity).Value : 1f;
			int playerIndex = World.Has<UnitOwnerPlayer>(entity) ? World.Get<UnitOwnerPlayer>(entity).PlayerIndex : 0;

			units.Add(new UnitSaveData
			{
				TemplateId = defId.Value,
				PosX = pos.Value.X,
				PosY = pos.Value.Y,
				PosZ = pos.Value.Z,
				RotationY = rotY,
				Scale = scale,
				Player = playerIndex
			});
		});

		return units;
	}

	private List<PropSaveData> GetPropSaveData(string directory)
	{
		var props = new List<PropSaveData>();
		var propQuery = QueryCache.AllPropIdentityAndPositionQuery;

		World.Query(in propQuery, (Entity entity, ref PropIdentity propId, ref Position pos) =>
		{
			if (!MapAssetSync.IsValidPropObjectId(propId.PropId, directory)) return;

			float rotY = World.Has<RotationY>(entity) ? World.Get<RotationY>(entity).Value : 0f;
			float scale = World.Has<ModelScale>(entity) ? World.Get<ModelScale>(entity).Value : 1f;

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

	private List<DecalSaveData> GetDecalSaveData(HashSet<Entity>? validDecalEntities)
	{
		var decals = new List<DecalSaveData>();
		var decalQuery = QueryCache.AllDecalIdentityAndPositionQuery;
		var orphanedDecalEntities = new List<Entity>();
		var savedDecalFingerprints = new HashSet<string>();

		World.Query(in decalQuery, (Entity entity, ref DecalIdentity decalId, ref Position pos) =>
		{
			if (validDecalEntities != null && !validDecalEntities.Contains(entity))
			{
				orphanedDecalEntities.Add(entity);
				return;
			}

			float rotX = 0f, rotY = 0f, rotZ = 0f;
			if (World.Has<Rotation3D>(entity))
			{
				var r3d = World.Get<Rotation3D>(entity).Value;
				rotX = r3d.X;
				rotY = r3d.Y;
				rotZ = r3d.Z;
			}
			else if (World.Has<RotationY>(entity))
			{
				rotY = World.Get<RotationY>(entity).Value;
			}

			float scale = World.Has<ModelScale>(entity) ? World.Get<ModelScale>(entity).Value : 1f;

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
			if (World.IsAlive(orphan)) World.Destroy(orphan);
		}

		return decals;
	}

	private List<VfxSaveData> GetVfxSaveData()
	{
		var vfxList = new List<VfxSaveData>();
		var allVfx = GameContext.Host?.AllVfx;
		if (allVfx == null) return vfxList;

		foreach (var vfx in allVfx)
		{
			if (vfx == null || !GodotObject.IsInstanceValid(vfx)) continue;
			vfxList.Add(CreateVfxSaveData(vfx));
		}

		return vfxList;
	}

	private VfxSaveData CreateVfxSaveData(ProceduralVfxInstance3D vfx)
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
			GameContext.MetadataService.UpdateMetadata(directory, meta =>
			{
				meta.MapProperties.MapWidth = width;
				meta.MapProperties.MapHeight = depth;
				GameContext.MetadataService.CleanMetadata(meta);
			});
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[SaveLoadService] CleanMetadataJson error: {ex.Message}");
		}
	}

	private void UpdateEditorState(EditorState editor, QueryDescription worldQuery2)
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

		World.Query(in worldQuery2, (Entity entity, ref TerrainState t, ref EditorState e) => { World.Set(entity, updatedEditor); });
	}

	private void CleanupOldMapEntities()
	{
		var unitQuery = QueryCache.AllDefinitionIdAndPositionAndOwnerQuery;
		var unitsToDestroy = new List<Entity>();
		World.Query(in unitQuery, (Entity entity) => unitsToDestroy.Add(entity));
		foreach (var ent in unitsToDestroy) World.Destroy(ent);

		var propQuery = QueryCache.AllPropIdentityAndPositionQuery;
		var propsToDestroy = new List<Entity>();
		World.Query(in propQuery, (Entity entity) => propsToDestroy.Add(entity));
		foreach (var ent in propsToDestroy) World.Destroy(ent);

		var decalQuery = QueryCache.AllDecalIdentityAndPositionQuery;
		var decalsToDestroy = new List<Entity>();
		World.Query(in decalQuery, (Entity entity) => decalsToDestroy.Add(entity));
		foreach (var ent in decalsToDestroy) World.Destroy(ent);

		var req1 = QueryCache.AllUnitSpawnRequestQuery;
		var req1List = new List<Entity>();
		World.Query(in req1, (Entity entity) => req1List.Add(entity));
		foreach (var ent in req1List) World.Destroy(ent);

		var req2 = QueryCache.AllPropSpawnRequestQuery;
		var req2List = new List<Entity>();
		World.Query(in req2, (Entity entity) => req2List.Add(entity));
		foreach (var ent in req2List) World.Destroy(ent);

		var req3 = QueryCache.AllDecalSpawnRequestQuery;
		var req3List = new List<Entity>();
		World.Query(in req3, (Entity entity) => req3List.Add(entity));
		foreach (var ent in req3List) World.Destroy(ent);
	}

	private void DetermineMapDimensions(string mapDir, MapSaveData saveData, out int width, out int depth)
	{
		width = 0;
		depth = 0;

		if (GameContext.MetadataService.TryLoadMetadata(mapDir, out var loadedMeta) && loadedMeta.MapProperties != null)
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
		var worldQuery = QueryCache.AllTerrainStateQuery;
		World.Query(in worldQuery, (Entity entity) => foundEntity = entity);

		if (foundEntity == Entity.Null)
		{
			foundEntity = World.Create();
		}

		if (!World.Has<TerrainState>(foundEntity))
		{
			World.Add(foundEntity, new TerrainState(width, depth, TerrainState.DefaultQuadSize, TerrainState.DefaultCellSize, new TerrainCell[width, depth], new int[width, depth], null, null));
		}
		else
		{
			ref var ts = ref World.Get<TerrainState>(foundEntity);
			ts.Width = width;
			ts.Depth = depth;
			ts.Cells = new TerrainCell[width, depth];
			ts.PathingCodes = new int[width, depth];
			World.Set(foundEntity, ts);
		}

		return foundEntity;
	}

	/// <summary>
	///     Loads the world a map folder describes.
	/// </summary>
	/// <param name="absolutePath">The terrain file to read.</param>
	/// <param name="terrainOnly">Whether to load only the terrain, leaving the map's own entities alone.</param>
	/// <param name="unitsFromAuthority">
	///     Whether the authority is already telling this screen about the units the map places. The props and decals a
	///     map places are never replicated, so they are loaded either way.
	/// </param>
	public bool LoadMapFromFile(string absolutePath, bool terrainOnly = false, bool unitsFromAuthority = false)
	{
		if (!File.Exists(absolutePath)) return false;

		try
		{
			var saveData = MapFileService.LoadTerrain(absolutePath);
			if (saveData == null) return false;

			string? mapDir = Path.GetDirectoryName(absolutePath);
			if (string.IsNullOrEmpty(mapDir)) return false;
			GameContext.Host?.LoadModelYOffsetsFromMetadataJson(mapDir);

			_lastLoadedCoordinates = saveData.Coordinates ?? new List<CoordinateSaveData>();
			_lastLoadedSpawnAnchors = saveData.SpawnAnchors ?? new List<SpawnAnchorSaveData>();

			CleanupOldMapEntities();

			DetermineMapDimensions(mapDir, saveData, out int width, out int depth);
			saveData.Width = width;
			saveData.Depth = depth;

			Entity worldEntity = InitializeTerrainState(width, depth);

			if (World.Has<TerrainState>(worldEntity))
			{
				LoadTerrainStateData(worldEntity, width, depth, mapDir);
			}

			LoadSplatColors(absolutePath, worldEntity, width, depth);
			LoadEditorAndCameraState(worldEntity, saveData);

			if (!terrainOnly)
			{
				SpawnEntitiesFromSaveData(saveData, mapDir, unitsFromAuthority);
			}

			return true;
		}
		catch (Exception ex)
		{
			GameLog.LogError(ex.Message);
			return false;
		}
	}

	private void LoadTerrainStateData(Entity worldEntity, int width, int depth, string directory)
	{
		ref var ts = ref World.Get<TerrainState>(worldEntity);

		if (ts.Cells == null || ts.Cells.GetLength(0) != width || ts.Cells.GetLength(1) != depth)
			ts.Cells = new TerrainCell[width, depth];
		if (ts.PathingCodes == null || ts.PathingCodes.GetLength(0) != width || ts.PathingCodes.GetLength(1) != depth)
			ts.PathingCodes = new int[width, depth];

		TerrainCell[,]? unscaledCells = null;
		int unscaledW = 0, unscaledH = 0;

		MapTerrainLoad.LoadTerrainHeights(directory, width, depth, ref ts, ref unscaledCells, ref unscaledW, ref unscaledH);
		MapTerrainLoad.LoadTerrainWater(directory, width, depth, ref ts, ref unscaledCells, ref unscaledW, ref unscaledH);
		MapTerrainLoad.LoadTerrainPathing(directory, width, depth, ref ts);

		// A ref-returned struct's fields are not flow-tracked across statements, so the grids the
		// call needs are named here, where the null checks below are visible to the compiler.
		var scaledCells = ts.Cells;
		var scaledPathing = ts.PathingCodes;
		if (unscaledCells != null && scaledCells != null && scaledPathing != null)
		{
			RuntimeTerrain.ReconcileScaledWater(unscaledCells, unscaledW, unscaledH, scaledCells, scaledPathing, width, depth);
		}

		World.Set(worldEntity, ts);
	}





	private void LoadSplatColors(string absolutePath, Entity worldEntity, int width, int depth)
	{
		string? directory = Path.GetDirectoryName(absolutePath);
		if (string.IsNullOrEmpty(directory)) return;
		string[]? loadedColors = MapTerrainFiles.ReadSplat(directory, MapTerrainFiles.SplatPrefix, width + 1, depth + 1);

		if (loadedColors == null)
		{
			loadedColors = new string[width * depth];
			string defaultSolid = TerrainSplatWeights.CreateSolid(0).Serialize();
			for (int i = 0; i < loadedColors.Length; i++)
			{
				loadedColors[i] = defaultSolid;
			}
		}

		World.SetOrAdd(worldEntity, new TerrainColorsState(loadedColors));
	}

	private void LoadEditorAndCameraState(Entity worldEntity, MapSaveData saveData)
	{
		MapCameraBounds defaultBounds = MapCameraBounds.FromTerrain(saveData.Width, saveData.Depth, RuntimeTerrain.DefaultQuadSize);
		float left = saveData.CameraBoundsLeft ?? defaultBounds.Left;
		float right = saveData.CameraBoundsRight ?? defaultBounds.Right;
		float top = saveData.CameraBoundsTop ?? defaultBounds.Top;
		float bottom = saveData.CameraBoundsBottom ?? defaultBounds.Bottom;

		WaterType currentWaterMode = World.Has<EditorState>(worldEntity) ? World.Get<EditorState>(worldEntity).WaterMode : WaterType.None;
		byte currentWaterProf = World.Has<EditorState>(worldEntity) ? World.Get<EditorState>(worldEntity).WaterProfileIndex : (byte)0;
		var newEditorState = new EditorState(true, EditableTerrain.TIER_HEIGHT, left, right, top, bottom, saveData.SkyboxPath ?? string.Empty, false, MirrorMode.None, currentWaterMode, currentWaterProf);
		World.SetOrAdd(worldEntity, newEditorState);

		if (World.Has<CameraState>(worldEntity))
		{
			ref var camState = ref World.Get<CameraState>(worldEntity);
			camState.LimitLeft = left;
			camState.LimitRight = right;
			camState.LimitTop = top;
			camState.LimitBottom = bottom;
		}
	}

	private void SpawnEntitiesFromSaveData(MapSaveData saveData, string mapDir, bool unitsFromAuthority)
	{
		if (!unitsFromAuthority) SpawnUnits(saveData.Units, mapDir);
		SpawnProps(saveData.Props, mapDir);
		SpawnDecals(saveData.Decals);
		SpawnVfx(saveData.Vfx);
	}

	private void SpawnUnits(List<UnitSaveData>? units, string mapDir)
	{
		if (units == null) return;
		foreach (var u in units)
		{
			if (!MapAssetSync.IsValidUnitObjectId(u.TemplateId, mapDir))
			{
				GD.PushWarning($"[SaveLoadService] Ignored invalid unit '{u.TemplateId}' in terrain.json because it does not exist as an Object ID in metadata.json.");
				continue;
			}

			World.Add(World.Create(), new UnitSpawnRequest(u.TemplateId, new Vector3(u.PosX, u.PosY, u.PosZ), u.RotationY, u.Scale, u.Player));
		}
	}

	private void SpawnProps(List<PropSaveData>? props, string mapDir)
	{
		if (props == null) return;
		foreach (var p in props)
		{
			if (!MapAssetSync.IsValidPropObjectId(p.TemplateId, mapDir))
			{
				GD.PushWarning($"[SaveLoadService] Ignored invalid prop '{p.TemplateId}' in terrain.json because it does not exist as an Object ID in metadata.json.");
				continue;
			}

			World.Add(World.Create(), new PropSpawnRequest(p.TemplateId, new Vector3(p.PosX, p.PosY, p.PosZ), p.RotationY, p.Scale));
		}
	}

	private void SpawnDecals(List<DecalSaveData>? decals)
	{
		if (decals == null) return;
		var loadedDecalFingerprints = new HashSet<string>();
		foreach (var d in decals)
		{
			if (!loadedDecalFingerprints.Add($"{d.TemplateId}_{d.PosX:F3}_{d.PosY:F3}_{d.PosZ:F3}_{d.RotationX:F2}_{d.RotationY:F2}_{d.RotationZ:F2}_{d.Scale:F3}")) continue;
			World.Add(World.Create(), new DecalSpawnRequest(d.TemplateId, new Vector3(d.PosX, d.PosY, d.PosZ), new Vector3(d.RotationX, d.RotationY, d.RotationZ), d.Scale));
		}
	}

	private void SpawnVfx(List<VfxSaveData>? vfxList)
	{
		if (vfxList == null || GameContext.Host == null) return;
		foreach (var v in vfxList)
		{
			GameContext.Host.SpawnVfxExternalWithParams(
				v.VfxId, new Godot.Vector3(v.PosX, v.PosY, v.PosZ), new Godot.Vector3(v.RotationX, v.RotationY, v.RotationZ),
				new Godot.Vector3(v.ScaleX <= 0f ? 1f : v.ScaleX, v.ScaleY <= 0f ? 1f : v.ScaleY, v.ScaleZ <= 0f ? 1f : v.ScaleZ),
				v.NormalOffset, v.Config
			);
		}
	}

	public static void RemapSplatExrFiles(string mapDirectory, IReadOnlyDictionary<int, int> remap)
	{
		if (string.IsNullOrEmpty(mapDirectory) || remap == null || remap.Count == 0) return;

		RemapSplatIndicesFile(MapTerrainFiles.SplatIndicesPath(mapDirectory, MapTerrainFiles.SplatPrefix), remap);
		RemapSplatIndicesFile(MapTerrainFiles.SplatIndicesPath(mapDirectory, MapTerrainFiles.CliffSplatPrefix), remap);
	}

	private static void RemapSplatIndicesFile(string file, IReadOnlyDictionary<int, int> remap)
	{
		if (!File.Exists(file)) return;
		try
		{
			float[] floats = ExrImageCodec.DecodeRgbaFloat(file, out int width, out int height);
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
				ExrImageCodec.EncodeRgbaFloat(file, floats, width, height);
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[SaveLoadService] Failed to remap splat EXR {file}: {ex.Message}");
		}
	}











	public string[] GetSplatData(TerrainSplatWeights[,] splatMap)
	{
		if (splatMap == null) return new string[0];
		int splatW = splatMap.GetLength(0);
		int splatD = splatMap.GetLength(1);
		string[] splatData = new string[splatW * splatD];
		for (int z = 0; z < splatD; z++)
		{
			for (int x = 0; x < splatW; x++)
			{
				splatData[z * splatW + x] = splatMap[x, z].Serialize();
			}
		}

		return splatData;
	}

	public float GetUnitPropRotationY(Entity entity, float defaultRotY, EcsWorld ecsWorld)
	{
		return ecsWorld.Has<RotationY>(entity) ? ecsWorld.Get<RotationY>(entity).Value : defaultRotY;
	}

	public float GetUnitPropScale(Entity entity, float defaultScale, EcsWorld ecsWorld)
	{
		return ecsWorld.Has<ModelScale>(entity) ? ecsWorld.Get<ModelScale>(entity).Value : defaultScale;
	}

	public float GetDecalScale(Entity entity, float visualScaleX, float decalSizeX)
	{
		if (visualScaleX != 1.0f)
		{
			return visualScaleX;
		}

		if (World.Has<ModelScale>(entity))
		{
			return World.Get<ModelScale>(entity).Value;
		}

		return decalSizeX / 6.0f;
	}

	public Vector3 GetDecalPosition(Entity entity, Vector3 rawPosition)
	{
		return World.Has<Position>(entity) ? World.Get<Position>(entity).Value : rawPosition;
	}

	public List<CoordinateSaveData> GetCoordinatesData()
	{
		return GameContext.EditorService.EditorCoordinates.Select(r => new CoordinateSaveData
		{
			Name = r.Name,
			MinX = r.MinX,
			MinZ = r.MinZ,
			MaxX = r.MaxX,
			MaxZ = r.MaxZ
		}).ToList();
	}

	public string[] SerializeCliffSplatMap(TerrainSplatWeights[,]? cliffSplatMap)
	{
		if (cliffSplatMap == null) return Array.Empty<string>();
		int cliffW = cliffSplatMap.GetLength(0);
		int cliffD = cliffSplatMap.GetLength(1);
		string[] cliffSplatData = new string[cliffW * cliffD];
		for (int z = 0; z < cliffD; z++)
		{
			for (int x = 0; x < cliffW; x++)
			{
				cliffSplatData[z * cliffW + x] = cliffSplatMap[x, z].Serialize();
			}
		}

		return cliffSplatData;
	}
}