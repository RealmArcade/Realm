using System.Collections.Generic;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Metadata;
using Realm.Shared.Services;
using Realm.Shared.Terrain;

namespace Realm.Shared.Maps;

public sealed class LoadedMap : IDisposable
{
	internal LoadedMap(SimulationContext simulation, DefinitionCatalog catalog, MapSaveData saveData)
	{
		Simulation = simulation;
		Catalog = catalog;
		SaveData = saveData;
	}

	public SimulationContext Simulation { get; }

	public EcsWorld World => Simulation.World;

	public DefinitionCatalog Catalog { get; }

	public MapSaveData SaveData { get; }

	public Entity WorldEntity { get; internal set; } = Entity.Null;

	public Entity[] PlayerEntities { get; } = new Entity[EcsWorld.MAX_PLAYERS];

	public MapMetadata Metadata => Catalog.Metadata;

	public int Width { get; internal set; }

	public int Depth { get; internal set; }

	public int PlayerCount { get; internal set; }

	public int UnitCount { get; internal set; }

	public int PropCount { get; internal set; }

	public bool HasNavMesh { get; internal set; }

	public List<string> Warnings { get; } = new();

	public PlayerEntity GetPlayerEntity(int playerSlot) => MatchRoster.ResolvePlayer(PlayerEntities, playerSlot);

	public void Dispose() => Simulation.Dispose();
}

public static class MapWorldLoader
{
	private const int MinimumGridSize = 32;
	private const int MaximumGridSize = 512;
	private const int GridSizeMultiple = 32;
	private const float DefaultGridSize = 128f;

	public static LoadedMap Load(string mapFolderPath, IReadOnlyList<MatchRosterSeat>? roster = null)
	{
		LoadedMap loaded = LoadShell(mapFolderPath);
		try
		{
			Seat(loaded, roster);
			return loaded;
		}
		catch
		{
			loaded.Dispose();
			throw;
		}
	}

	public static LoadedMap LoadShell(string mapFolderPath)
	{
		string folder = Path.GetFullPath(mapFolderPath);
		if (!Directory.Exists(folder))
		{
			throw new DirectoryNotFoundException($"The map folder '{folder}' does not exist.");
		}

		MapMetadata metadata = MapFileService.LoadMetadata(folder);
		MapSaveData saveData = MapFileService.LoadTerrain(folder);
		DefinitionCatalog catalog = DefinitionCatalog.Build(metadata);

		var simulation = new SimulationContext();
		var loaded = new LoadedMap(simulation, catalog, saveData);

		try
		{
			MapCombatTuning.Apply(simulation, metadata, loaded.Warnings);
			catalog.BindTo(simulation.MapRules);

			DetermineGridSize(metadata, saveData, out int width, out int depth);
			loaded.Width = width;
			loaded.Depth = depth;

			Entity worldEntity = simulation.World.Create();
			loaded.WorldEntity = worldEntity;
			simulation.WorldEntity = worldEntity;

			TerrainState terrain = BuildTerrain(folder, width, depth);
			simulation.TerrainNavMeshService.BakeNavMesh(ref terrain);
			loaded.HasNavMesh = terrain.NavMesh != null;
			simulation.World.Add(worldEntity, terrain);

			SpawnPlacedProps(loaded);
			return loaded;
		}
		catch
		{
			loaded.Dispose();
			throw;
		}
	}

	public static void Seat(LoadedMap loaded, IReadOnlyList<MatchRosterSeat>? roster = null)
	{
		if (loaded.PlayerCount > 0) return;

		SpawnPlayers(loaded, roster);
		SpawnPlacedUnits(loaded);
	}

	public static void DetermineGridSize(MapMetadata? metadata, MapSaveData? saveData, out int width, out int depth)
	{
		float declaredWidth = metadata?.MapProperties?.MapWidth ?? 0f;
		float declaredDepth = metadata?.MapProperties?.MapHeight ?? 0f;

		if (declaredWidth <= 0f) declaredWidth = saveData?.Width > 0 ? saveData.Width : DefaultGridSize;
		if (declaredDepth <= 0f) declaredDepth = saveData?.Depth > 0 ? saveData.Depth : DefaultGridSize;

		width = Math.Clamp((int)Math.Round(declaredWidth / GridSizeMultiple) * GridSizeMultiple, MinimumGridSize, MaximumGridSize);
		depth = Math.Clamp((int)Math.Round(declaredDepth / GridSizeMultiple) * GridSizeMultiple, MinimumGridSize, MaximumGridSize);
	}

	private static TerrainState BuildTerrain(string folder, int width, int depth)
	{
		var state = new TerrainState(
			width,
			depth,
			TerrainState.DefaultQuadSize,
			TerrainState.DefaultCellSize,
			new TerrainCell[width, depth],
			new int[width, depth],
			null,
			null);

		ReadHeights(folder, ref state, width, depth);
		ReadWater(folder, ref state);
		ReadPathing(folder, ref state, width, depth);
		return state;
	}

	private static void ReadHeights(string folder, ref TerrainState state, int width, int depth)
	{
		float[]? heights = MapTerrainFiles.TryReadHeights(folder, out int imageWidth, out int imageHeight);
		if (heights == null) return;

		if (imageWidth != width || imageHeight != depth)
		{
			throw new InvalidDataException(
				$"'{MapTerrainFiles.HeightsFileName}' describes a {imageWidth}x{imageHeight} grid but the map declares {width}x{depth}. The height image and the map size have to agree.");
		}

		state.Cells = MapTerrainFiles.CellsFromHeights(heights, width, depth);
	}

	private static void ReadWater(string folder, ref TerrainState state)
	{
		float[]? water = MapTerrainFiles.TryReadWater(folder, out int imageWidth, out int imageHeight);
		if (water == null || state.Cells == null) return;

		if (imageWidth != state.Cells.GetLength(0) || imageHeight != state.Cells.GetLength(1))
		{
			throw new InvalidDataException(
				$"'{MapTerrainFiles.WaterFileName}' describes a {imageWidth}x{imageHeight} grid but the height image describes {state.Cells.GetLength(0)}x{state.Cells.GetLength(1)}. The two images have to agree.");
		}

		MapTerrainFiles.ApplyWater(water, imageWidth, imageHeight, state.Cells);
	}

	private static void ReadPathing(string folder, ref TerrainState state, int width, int depth)
	{
		int[,]? pathingCodes = state.PathingCodes;
		if (pathingCodes == null) return;

		byte[]? pathing = MapTerrainFiles.TryReadPathing(folder, out int imageWidth, out int imageHeight);
		if (pathing == null)
		{
			int defaultCode = TerrainPathing.DefaultCode(WaterType.None);
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					pathingCodes[x, z] = defaultCode;
				}
			}

			return;
		}

		MapTerrainFiles.ApplyPathing(pathing, imageWidth, imageHeight, pathingCodes, width, depth);
	}

	private static void SpawnPlayers(LoadedMap loaded, IReadOnlyList<MatchRosterSeat>? roster)
	{
		EcsWorld world = loaded.World;
		SimulationContext simulation = loaded.Simulation;

		if (simulation.PlayerResourceService.GoldResourceId == default)
		{
			simulation.PlayerResourceService.GoldResourceId = "gold".AsResourceId(simulation.DefinitionManager);
			simulation.PlayerResourceService.WoodResourceId = "wood".AsResourceId(simulation.DefinitionManager);
			simulation.PlayerResourceService.StoneResourceId = "stone".AsResourceId(simulation.DefinitionManager);
		}

		ScriptPlayer[] players = MatchRoster.CreateDefaultPlayers();
		MatchRoster.SeatPlayers(players, roster);
		var mappingState = new NetworkMappingState(new Dictionary<int, Entity>(), new Dictionary<int, int>(), new Dictionary<int, Entity>());

		for (int index = 0; index < EcsWorld.MAX_PLAYERS; index++)
		{
			Entity playerEntity = world.Create();
			world.Add(playerEntity, new Player());
			world.Add(playerEntity, new UnitOwnerPlayer(index));
			world.Add(playerEntity, new Name(players[index].Name));
			world.Add(playerEntity, new PlayerResources(new Dictionary<ResourceId, int>
			{
				{ simulation.PlayerResourceService.GoldResourceId, (int)players[index].Gold },
				{ simulation.PlayerResourceService.WoodResourceId, (int)players[index].Wood },
				{ simulation.PlayerResourceService.StoneResourceId, (int)players[index].Stone }
			}));
			world.Add(playerEntity, new PlayerPopulation(0, 0));
			world.Add(playerEntity, new SpellCooldowns(null));
			world.Add(playerEntity, new PlayerUpgrades(false, false, false));
			world.Add(playerEntity, new PlayerTeam(players[index].Team, players[index].TeamNameStringId));
			world.GetBuffer<ScriptPlayer>().Set(index, in players[index]);

			loaded.PlayerEntities[index] = playerEntity;
			mappingState.PlayerEntities![index] = playerEntity;
		}

		loaded.PlayerCount = loaded.PlayerEntities.Length;
		world.SetOrAdd(loaded.WorldEntity, new ScriptPlayersState(players));
		world.SetOrAdd(loaded.WorldEntity, new PlayerRelationshipMatrix());
		world.SetOrAdd(loaded.WorldEntity, mappingState);
	}

	private static void SpawnPlacedUnits(LoadedMap loaded)
	{
		EcsWorld world = loaded.World;
		MapSaveData saveData = loaded.SaveData;

		if (saveData.Units == null) return;

		for (int index = 0; index < saveData.Units.Count; index++)
		{
			UnitSaveData unit = saveData.Units[index];
			Entity entity = UnitEntityFactory.CreateFromMetadata(
				world,
				loaded.Catalog,
				unit.TemplateId,
				new Vector3(unit.PosX, unit.PosY, unit.PosZ),
				unit.RotationY,
				unit.Scale,
				loaded.GetPlayerEntity(unit.Player),
				bypassPopulation: false);

			if (entity == Entity.Null)
			{
				loaded.Warnings.Add($"The map places '{unit.TemplateId}' but declares no such unit or building template.");
				continue;
			}

			loaded.UnitCount++;
		}
	}

	private static void SpawnPlacedProps(LoadedMap loaded)
	{
		EcsWorld world = loaded.World;
		MapSaveData saveData = loaded.SaveData;

		if (saveData.Props == null) return;

		for (int index = 0; index < saveData.Props.Count; index++)
		{
			PropSaveData prop = saveData.Props[index];
			PropEntityFactory.Create(
				world,
				loaded.Catalog,
				prop.TemplateId,
				cleanId: null,
				new Vector3(prop.PosX, prop.PosY, prop.PosZ),
				prop.RotationY,
				prop.Scale,
				collisionRadius: 0f);
			loaded.PropCount++;
		}
	}
}
