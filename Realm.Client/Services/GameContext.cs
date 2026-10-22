using Realm.Ecs;
using Realm.Client.Services.ModelOptimization;
using Realm.Ecs.Services;
using Realm.EditorAPI;

namespace Realm.Client.Services
{
	public static class GameContext
	{
		public static SessionState SessionState { get; } = new();

		private static ServiceScope _globalScope = new("Global");
		private static ServiceScope? _screenScope;

		/// <summary>
		/// Services that live for the whole application session and are disposed only at shutdown.
		/// </summary>
		public static ServiceScope GlobalScope => _globalScope;

		/// <summary>
		/// Services owned by the screen that is currently open. Ending the screen disposes this scope; because a
		/// disposed scope never hands a service out again, the next request creates a fresh scope instead of
		/// returning disposed state. A screen must therefore read this property when it needs a service rather
		/// than keeping a scope reference across screens.
		/// </summary>
		public static ServiceScope ScreenScope => _screenScope is { IsDisposed: false } scope ? scope : CreateScreenScope();

		public static EcsWorld? World => ECSContext.World;
		public static Entity WorldEntity
		{
			get => ECSContext.WorldEntity;
			set => ECSContext.WorldEntity = value;
		}
		public static bool IsGameOver
		{
			get => ECSContext.IsGameOver;
			set => ECSContext.IsGameOver = value;
		}
		public static string ActiveMapName
		{
			get => ECSContext.ActiveMapName;
			set => ECSContext.ActiveMapName = value;
		}

		public static AudioService AudioService => GlobalScope.Get<AudioService>();
		public static InputService InputService => GlobalScope.Get<InputService>();
		public static AssetIndexService AssetIndexService => GlobalScope.Get<AssetIndexService>();
		public static MetadataService MetadataService => GlobalScope.Get<MetadataService>();
		public static MapUpgradeService MapUpgradeService => GlobalScope.Get<MapUpgradeService>();
		public static MapCatalogService MapCatalogService => GlobalScope.Get<MapCatalogService>();
		public static MapStorageService MapStorageService => GlobalScope.Get<MapStorageService>();
		public static MapAssetImportService MapAssetImportService => GlobalScope.Get<MapAssetImportService>();
		public static ModelOptimizerService ModelOptimizerService => GlobalScope.Get<ModelOptimizerService>();
		public static CheatService CheatService => GlobalScope.Get<CheatService>();
		public static BaseMapFolderWatchService BaseMapFolderWatchService => GlobalScope.Get<BaseMapFolderWatchService>();

		public static FXService FXService => ScreenScope.Get<FXService>();
		public static SaveLoadService SaveLoadService => ScreenScope.Get<SaveLoadService>();
		public static EditorService EditorService => ScreenScope.Get<EditorService>();
		public static ModelOverrideService ModelOverrideService => ScreenScope.Get<ModelOverrideService>();
		public static ReplayService ReplayService => ScreenScope.Get<ReplayService>();
		public static NetworkService NetworkService => ScreenScope.Get<NetworkService>();
		public static TechTreeService TechTreeService => ScreenScope.Get<TechTreeService>();
		public static ShroudService ShroudService => ScreenScope.Get<ShroudService>();
		public static UnitSpawnService UnitSpawnService => ScreenScope.Get<UnitSpawnService>();
		public static WorldInitService WorldInitService => ScreenScope.Get<WorldInitService>();
		public static MapPropertiesLoader MapPropertiesLoader => ScreenScope.Get<MapPropertiesLoader>();
		public static MapEditorTerrainImportService MapEditorTerrainImportService => ScreenScope.Get<MapEditorTerrainImportService>();
		public static EnvironmentService EnvironmentService => ScreenScope.Get<EnvironmentService>();
		public static WindService WindService => ScreenScope.Get<WindService>();
		public static SpectatorService SpectatorService => ScreenScope.Get<SpectatorService>();
		public static MapSaveDataService MapSaveDataService => ScreenScope.Get<MapSaveDataService>();
		public static RegistryService RegistryService => ScreenScope.Get<RegistryService>();
		public static MapScriptService MapScriptService => ScreenScope.Get<MapScriptService>();
		public static TimerService TimerService => ScreenScope.Get<TimerService>();
		public static RandomService RandomService => ScreenScope.Get<RandomService>();
		public static PerformanceTrackingService PerformanceTrackingService => ScreenScope.Get<PerformanceTrackingService>();
		public static NavMeshPathfinder NavMeshPathfinder => ScreenScope.Get<NavMeshPathfinder>();
		public static ResourceEconomyService ResourceEconomyService => ScreenScope.Get<ResourceEconomyService>();
		public static SimulationService SimulationService => ScreenScope.Get<SimulationService>();
		public static MovementPredictionService MovementPredictionService => ScreenScope.Get<MovementPredictionService>();
		public static PredictionPerformanceMonitor PredictionPerformanceMonitor => ScreenScope.Get<PredictionPerformanceMonitor>();
		public static ClientRpcService ClientRPC => ScreenScope.Get<ClientRpcService>();
		public static PerspectiveFilter PerspectiveFilter => ScreenScope.Get<PerspectiveFilter>();
		public static RaycastService RaycastService => ScreenScope.Get<RaycastService>();

		public static IEditorAPI IEditorAPI => EditorService;

		static GameContext()
		{
			EnsureServices();
		}

		public static void EnsureServices()
		{
			ECSContext.EnsureServices();

			if (_globalScope.IsDisposed)
			{
				_globalScope = new ServiceScope("Global");
			}
		}

		private static ServiceScope CreateScreenScope()
		{
			var scope = new ServiceScope("Screen");
			scope.RegisterCleanup<ShroudService>(static service => service.CleanUp());
			scope.RegisterCleanup<EnvironmentService>(static service => service.Cleanup());
			scope.RegisterCleanup<EditorService>(static service => service.ResetAllState());
			scope.RegisterCleanup<ModelOverrideService>(static service => service.ClearAll());
			scope.RegisterCleanup<RegistryService>(static service => service.ClearAll());
			scope.RegisterCleanup<MapScriptService>(static service => service.ClearAll());
			scope.RegisterCleanup<MovementPredictionService>(static service => service.Clear());
			scope.RegisterCleanup<NetworkService>(static service => service.Clear());
			scope.RegisterDisposeAction(ECSContext.DisposeAndRecreateServices);

			_screenScope = scope;
			return scope;
		}

		public static void EndScreenScope()
		{
			ServiceScope? scope = _screenScope;
			_screenScope = null;
			scope?.Dispose();
		}

		public static void Dispose()
		{
			EndScreenScope();

			_globalScope.Dispose();
			_globalScope = new ServiceScope("Global");

			Core.GameHost.Instance?.ClearStaticText();
			VisualProjectilePool.Instance.Clear();
			VisualProjectile3D.ClearSharedShaderCache();

			ECSContext.Dispose();
		}
	}
}
