using Arch.Core;
using Microsoft.Extensions.DependencyInjection;
using Realm.Ecs.Services;

public partial class GameHost
{
	private WorldAccessor _worldAccessor;

	public void BuildDependencyInjection()
	{
		var services = new ServiceCollection();

		var world = World.Create();
		_worldAccessor = new WorldAccessor(world);

		services.AddSingleton(_worldAccessor);
		services.AddTransient<World>(sp => sp.GetRequiredService<WorldAccessor>().Current);

		// Ecs Services
		services.AddSingleton<DefinitionManager>();
		services.AddSingleton<ArchetypeManager>(sp =>
		{
			return new ArchetypeManager(sp.GetRequiredService<WorldAccessor>(), new System.Collections.Generic.List<Realm.Ecs.Archetypes.UnitArchetype>(), sp.GetRequiredService<DefinitionManager>());
		});
		services.AddSingleton<CombatService>();
		services.AddSingleton<EntityFactory>();
		services.AddSingleton<MapLoader>(sp => new MapLoader(sp.GetRequiredService<WorldAccessor>(), "Definitions"));
		services.AddSingleton<GameInitializer>(sp =>
		{
			return new GameInitializer(sp.GetRequiredService<WorldAccessor>(), sp.GetRequiredService<MapLoader>());
		});
		services.AddSingleton<MovementService>();
		services.AddSingleton<PlayerResourceService>();
		services.AddSingleton<StatService>();
		services.AddSingleton<TerrainNavMeshService>();

		// Godot / Presentation Services
		services.AddSingleton<AudioService>();
		services.AddSingleton<FXService>();
		services.AddSingleton<SaveLoadService>();
		services.AddSingleton<EditorService>();
		services.AddSingleton<Realm.MapAPI.IEditorAPI>(sp => sp.GetRequiredService<EditorService>());
		services.AddSingleton<ReplayService>();
		services.AddSingleton<NetworkService>();
		services.AddSingleton<TechTreeService>();
		services.AddSingleton<InputService>();
		services.AddSingleton<ShroudService>();
		services.AddSingleton<UnitSpawnService>();
		services.AddSingleton<WorldInitService>();
		services.AddSingleton<MapPropertiesLoader>();
		services.AddSingleton<MapEditorTerrainImportService>();
		services.AddSingleton<CheatService>();
		services.AddSingleton<EnvironmentService>();
		services.AddSingleton<SpectatorService>();
		services.AddSingleton<Realm.Godot.Services.ModelOptimization.ModelOptimizerService>();
		services.AddSingleton<AssetIndexService>();
		services.AddSingleton<Realm.Godot.Services.MetadataService>();
		services.AddSingleton<Realm.Godot.Services.MapUpgradeService>();
		services.AddSingleton<Realm.Godot.Services.MapStorageService>();
		services.AddSingleton<Realm.Godot.Services.MapSaveDataService>();
		services.AddSingleton<SimulationService>(sp =>
		{
			return new SimulationService(sp.GetRequiredService<WorldAccessor>(), Entity.Null, GameHost.Instance?._pathfinder ?? new NavMeshPathfinder());
		});

		var provider = services.BuildServiceProvider();
		ServiceLocator.Initialize(provider);
	}

	public override void _EnterTree()
	{
		BuildDependencyInjection();
		ResolveServices();
	}

	private void ResolveServices()
	{
		_audioService = ServiceLocator.Get<AudioService>();
		_fxService = ServiceLocator.Get<FXService>();
		_saveLoadService = ServiceLocator.Get<SaveLoadService>();
		_editorService = ServiceLocator.Get<EditorService>();
		_replayService = ServiceLocator.Get<ReplayService>();
		_networkService = ServiceLocator.Get<NetworkService>();
		_inputService = ServiceLocator.Get<InputService>();
		_shroudService = ServiceLocator.Get<ShroudService>();
		_unitSpawnService = ServiceLocator.Get<UnitSpawnService>();
		_worldInitService = ServiceLocator.Get<WorldInitService>();
		_mapPropertiesLoader = ServiceLocator.Get<MapPropertiesLoader>();
		_terrainImportService = ServiceLocator.Get<MapEditorTerrainImportService>();
		_cheatService = ServiceLocator.Get<CheatService>();
		_environmentService = ServiceLocator.Get<EnvironmentService>();
		_spectatorService = ServiceLocator.Get<SpectatorService>();
		_modelOptimizerService = ServiceLocator.Get<Realm.Godot.Services.ModelOptimization.ModelOptimizerService>();
		_terrainNavMeshService = ServiceLocator.Get<TerrainNavMeshService>();
		_metadataService = ServiceLocator.Get<Realm.Godot.Services.MetadataService>();
		_mapUpgradeService = ServiceLocator.Get<Realm.Godot.Services.MapUpgradeService>();
		_mapStorageService = ServiceLocator.Get<Realm.Godot.Services.MapStorageService>();
		_mapSaveDataService = ServiceLocator.Get<Realm.Godot.Services.MapSaveDataService>();
	}
}
