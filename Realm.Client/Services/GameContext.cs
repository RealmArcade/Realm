using System.Runtime.Versioning;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using Realm.Client.UI;
using Realm.Client.Services.ModelOptimization;
using Realm.Ecs;
using Realm.Ecs.Services;
using Realm.EditorAPI;

namespace Realm.Client.Services
{
	public static class GameContext
	{
		public static SessionState SessionState { get; } = new();

		private static ServiceScope _globalScope = new("Global");
		private static ServiceScope? _screenScope;

		private static ReplayPlaybackManager? _replayPlayback;
		private static PeerSeederManager? _peerSeeder;
		private static VSCodeManager? _vscode;
		private static WasmConsoleWindow? _wasmConsole;

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

		public static GameHost? Host { get; private set; }
		public static RuntimeTerrain? Terrain { get; private set; }
		public static LobbyManager? Lobby { get; private set; }
		public static UIManager? UserInterface { get; private set; }
		public static InGameHUD? InGameHud { get; private set; }
		public static MapEditorHUD? MapEditorHud { get; private set; }
		public static SettingsMenu? Settings { get; private set; }
		public static PropMultiMeshManager? PropMultiMesh { get; private set; }

		public static ReplayPlaybackManager ReplayPlayback => _replayPlayback ??= new ReplayPlaybackManager();
		public static PeerSeederManager PeerSeeder => _peerSeeder ??= new PeerSeederManager();
		public static UpnpService Upnp { get; } = new();

		[SupportedOSPlatform("windows")]
		public static VSCodeManager VSCode => _vscode ??= new VSCodeManager();

		public static bool HasWasmConsole => _wasmConsole != null && Godot.GodotObject.IsInstanceValid(_wasmConsole);

		public static WasmConsoleWindow WasmConsole => HasWasmConsole ? _wasmConsole! : CreateWasmConsole();

		public static bool IsSettingsMenuOpen => Settings is { } settings
			&& Godot.GodotObject.IsInstanceValid(settings)
			&& settings.IsInsideTree()
			&& settings.Visible;

		public static void RegisterHost(GameHost host) => Host = host;
		public static void UnregisterHost(GameHost host)
		{
			if (ReferenceEquals(Host, host)) Host = null;
		}

		public static void RegisterTerrain(RuntimeTerrain terrain) => Terrain = terrain;
		public static void UnregisterTerrain(RuntimeTerrain terrain)
		{
			if (ReferenceEquals(Terrain, terrain)) Terrain = null;
		}

		public static void RegisterLobby(LobbyManager lobby) => Lobby = lobby;
		public static void UnregisterLobby(LobbyManager lobby)
		{
			if (ReferenceEquals(Lobby, lobby)) Lobby = null;
		}

		public static void RegisterUserInterface(UIManager userInterface) => UserInterface = userInterface;
		public static void UnregisterUserInterface(UIManager userInterface)
		{
			if (ReferenceEquals(UserInterface, userInterface)) UserInterface = null;
		}

		public static void RegisterInGameHud(InGameHUD inGameHud) => InGameHud = inGameHud;
		public static void UnregisterInGameHud(InGameHUD inGameHud)
		{
			if (ReferenceEquals(InGameHud, inGameHud)) InGameHud = null;
		}

		public static void RegisterMapEditorHud(MapEditorHUD mapEditorHud) => MapEditorHud = mapEditorHud;
		public static void UnregisterMapEditorHud(MapEditorHUD mapEditorHud)
		{
			if (ReferenceEquals(MapEditorHud, mapEditorHud)) MapEditorHud = null;
		}

		public static void RegisterSettings(SettingsMenu settings) => Settings = settings;
		public static void UnregisterSettings(SettingsMenu settings)
		{
			if (ReferenceEquals(Settings, settings)) Settings = null;
		}

		public static void RegisterPropMultiMesh(PropMultiMeshManager propMultiMesh) => PropMultiMesh = propMultiMesh;
		public static void UnregisterPropMultiMesh(PropMultiMeshManager propMultiMesh)
		{
			if (ReferenceEquals(PropMultiMesh, propMultiMesh)) PropMultiMesh = null;
		}

		public static void UnregisterWasmConsole(WasmConsoleWindow wasmConsole)
		{
			if (ReferenceEquals(_wasmConsole, wasmConsole)) _wasmConsole = null;
		}

		public static AudioService AudioService => GlobalScope.Get<AudioService>();
		public static InputService InputService => GlobalScope.Get<InputService>();
		public static AssetIndexService AssetIndexService => GlobalScope.Get<AssetIndexService>();
		public static MetadataService MetadataService => GlobalScope.Get<MetadataService>();
		public static MapUpgradeService MapUpgradeService => GlobalScope.Get<MapUpgradeService>();
		public static MapCatalogService MapCatalogService => GlobalScope.Get<MapCatalogService>();
		public static MapStorageService MapStorageService => GlobalScope.Get<MapStorageService>();
		public static MapAssetImportService MapAssetImportService => GlobalScope.Get<MapAssetImportService>();
		public static MapTemplateImportService MapTemplateImportService => GlobalScope.Get<MapTemplateImportService>();
		public static MapCodeImportService MapCodeImportService => GlobalScope.Get<MapCodeImportService>();
		public static MapAttributionService MapAttributionService => GlobalScope.Get<MapAttributionService>();
		public static ModelOptimizerService ModelOptimizerService => GlobalScope.Get<ModelOptimizerService>();
		public static CheatService CheatService => GlobalScope.Get<CheatService>();
		public static BaseMapFolderWatchService BaseMapFolderWatchService => GlobalScope.Get<BaseMapFolderWatchService>();

		public static FXService FXService => ScreenScope.Get<FXService>();
		public static SaveLoadService SaveLoadService => ScreenScope.Get<SaveLoadService>();
		public static EditorService EditorService => ScreenScope.Get<EditorService>();
		public static MapGenerationService MapGenerationService => ScreenScope.Get<MapGenerationService>();
		public static ModelOverrideService ModelOverrideService => ScreenScope.Get<ModelOverrideService>();
		public static ReplayService ReplayService => ScreenScope.Get<ReplayService>();
		public static NetworkService NetworkService => ScreenScope.Get<NetworkService>();
		public static TechTreeService TechTreeService => ScreenScope.Get<TechTreeService>();
		public static ShroudService ShroudService => ScreenScope.Get<ShroudService>();
		public static UnitSpawnService UnitSpawnService => ScreenScope.Get<UnitSpawnService>();
		public static WorldInitService WorldInitService => ScreenScope.Get<WorldInitService>();
		public static MapPropertiesLoader MapPropertiesLoader => ScreenScope.Get<MapPropertiesLoader>();
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
		public static ConsumableService ConsumableService => ScreenScope.Get<ConsumableService>();
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

		private static WasmConsoleWindow CreateWasmConsole()
		{
			_wasmConsole = new WasmConsoleWindow();
			_wasmConsole.Name = "WasmConsoleWindow";
			return _wasmConsole;
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
			scope.RegisterCleanup<EditorService>(static service =>
			{
				service.StopWorkspaceWatcher();
				service.ResetAllState();
			});
			scope.RegisterCleanup<MapGenerationService>(static service => service.DiscardRun());
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

			Host?.ClearStaticText();
			VisualProjectilePool.Clear();
			VisualProjectile3D.ClearSharedShaderCache();

			ECSContext.Dispose();
		}
	}
}
