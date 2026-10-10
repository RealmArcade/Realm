using Arch.Core;
using Microsoft.Extensions.DependencyInjection;
using Realm.Ecs.Services;
using Realm.EditorAPI;
using System;

namespace Realm.Client.Services
{
	public static class ServiceLocator
	{
		private static IServiceProvider? _provider;

		static ServiceLocator()
		{
			EnsureServices();
		}

		public static void EnsureServices()
		{
			if (_provider != null)
			{
				return;
			}

			var pathfinder = new NavMeshPathfinder();

			var services = new ServiceCollection();

			var world = World.Create();
			var worldAccessor = new WorldAccessor(world);

			services.AddSingleton(worldAccessor);
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
			services.AddSingleton<ModelOverrideService>();
			services.AddSingleton<IEditorAPI>(sp => sp.GetRequiredService<EditorService>());
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
			services.AddSingleton<Realm.Client.Services.ModelOptimization.ModelOptimizerService>();
			services.AddSingleton<AssetIndexService>();
			services.AddSingleton<Realm.Client.Services.MetadataService>();
			services.AddSingleton<Realm.Client.Services.MapUpgradeService>();
			services.AddSingleton<Realm.Client.Services.MapStorageService>();
			services.AddSingleton<Realm.Client.Services.MapSaveDataService>();
			services.AddSingleton<SimulationService>(sp =>
			{
				return new SimulationService(sp.GetRequiredService<WorldAccessor>(), Entity.Null, pathfinder);
			});

			_provider = services.BuildServiceProvider();
		}

		public static void Dispose()
		{
			Get<NetworkService>()?.Clear();
			Get<ShroudService>()?.CleanUp();
			Get<EnvironmentService>()?.Cleanup();
			Get<EditorService>()?.ResetAllState();
			Get<ModelOverrideService>()?.ClearAll();
			Get<World>()?.Dispose();

			_provider = null;
		}

		public static void DisposeAndRecreateServices()
		{
			Dispose();
			EnsureServices();
		}

		public static T Get<T>() where T : class
		{
			if (_provider == null)
				throw new InvalidOperationException("ServiceLocator has not been initialized yet.");

			return _provider.GetRequiredService<T>();
		}

		public static T? TryGet<T>() where T : class
		{
			if (_provider == null) return null;

			return _provider.GetService<T>();
		}
	}
}
