using System;
using System.Collections.Generic;
using Realm.Ecs;
using Realm.Client.Services.ModelOptimization;
using Realm.Ecs.Services;
using Realm.EditorAPI;

namespace Realm.Client.Services
{
	public static class GameContext
	{
		public static SessionState SessionState { get; } = new();
		
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

		public static AudioService AudioService { get; private set; }
		public static FXService FXService { get; private set; }
		public static SaveLoadService SaveLoadService { get; private set; }
		public static EditorService EditorService { get; private set; }
		public static ModelOverrideService ModelOverrideService { get; private set; }
		public static ReplayService ReplayService { get; private set; }
		public static NetworkService NetworkService { get; private set; }
		public static TechTreeService TechTreeService { get; private set; }
		public static InputService InputService { get; private set; }
		public static ShroudService ShroudService { get; private set; }
		public static UnitSpawnService UnitSpawnService { get; private set; }
		public static WorldInitService WorldInitService { get; private set; }
		public static MapPropertiesLoader MapPropertiesLoader { get; private set; }
		public static MapEditorTerrainImportService MapEditorTerrainImportService { get; private set; }
		public static CheatService CheatService { get; private set; }
		public static EnvironmentService EnvironmentService { get; private set; }
		public static SpectatorService SpectatorService { get; private set; }
		public static ModelOptimizerService ModelOptimizerService { get; private set; }
		public static AssetIndexService AssetIndexService { get; private set; }
		public static MetadataService MetadataService { get; private set; }
		public static MapUpgradeService MapUpgradeService { get; private set; }
		public static MapStorageService MapStorageService { get; private set; }
		public static MapSaveDataService MapSaveDataService { get; private set; }
		public static RegistryService RegistryService { get; private set; }
		public static MapScriptService MapScriptService { get; private set; }
		public static TimerService TimerService { get; private set; }
		public static RandomService RandomService { get; private set; }
		public static PerformanceTrackingService PerformanceTrackingService { get; private set; }
		public static NavMeshPathfinder NavMeshPathfinder { get; private set; }
		public static ResourceEconomyService ResourceEconomyService { get; private set; }
		public static SimulationService SimulationService { get; private set; }
		public static MovementPredictionService MovementPredictionService { get; private set; }
		public static PredictionPerformanceMonitor PredictionPerformanceMonitor { get; private set; }
		public static ClientRpcService ClientRPC { get; private set; }
		public static PerspectiveFilter PerspectiveFilter { get; private set; }
		public static RaycastService RaycastService { get; private set; }
		public static BaseMapFolderWatchService BaseMapFolderWatchService { get; private set; }

		public static IEditorAPI IEditorAPI { get; private set; }

		static GameContext()
		{
			EnsureServices();
		}

		public static void EnsureServices()
		{
			ECSContext.EnsureServices();

			if (AudioService != null)
			{
				return;
			}

			AudioService = new();
			FXService = new();
			SaveLoadService = new();
			EditorService = new();
			ModelOverrideService = new();
			ReplayService = new();
			NetworkService = new();
			TechTreeService = new();
			ShroudService = new();
			UnitSpawnService = new();
			WorldInitService = new();
			MapPropertiesLoader = new();
			MapEditorTerrainImportService = new();
			CheatService = new();
			EnvironmentService = new();
			SpectatorService = new();
			ModelOptimizerService = new();
			AssetIndexService = new();
			MetadataService = new();
			MapUpgradeService = new();
			MapStorageService = new();
			MapSaveDataService = new();
			RegistryService = new();
			MapScriptService = new();
			TimerService = new();
			RandomService = new();
			PerformanceTrackingService = new();
			ResourceEconomyService = new();
			NavMeshPathfinder = new();
			SimulationService = new();
			MovementPredictionService = new();
			PredictionPerformanceMonitor = new();
			InputService = new();
			ClientRPC = new();
			PerspectiveFilter = new();
			RaycastService = new();
			BaseMapFolderWatchService = new();

			IEditorAPI = EditorService;
		}

		public static void Dispose()
		{
			NetworkService?.Clear();
			ShroudService?.CleanUp();
			EnvironmentService?.Cleanup();
			EditorService?.ResetAllState();
			ModelOverrideService?.ClearAll();
			RegistryService?.ClearAll();
			MapScriptService?.ClearAll();
			Core.GameHost.Instance?.ClearStaticText();
			VisualProjectilePool.Instance.Clear();
			VisualProjectile3D.ClearSharedShaderCache();

			AudioService = null;
			FXService = null;
			SaveLoadService = null;
			EditorService = null;
			ModelOverrideService = null;
			ReplayService = null;
			NetworkService = null;
			TechTreeService = null;
			ShroudService = null;
			UnitSpawnService = null;
			WorldInitService = null;
			MapPropertiesLoader = null;
			MapEditorTerrainImportService = null;
			CheatService = null;
			EnvironmentService = null;
			SpectatorService = null;
			ModelOptimizerService = null;
			AssetIndexService = null;
			MetadataService = null;
			MapUpgradeService = null;
			MapStorageService = null;
			MapSaveDataService = null;
			RegistryService = null;
			MapScriptService = null;
			TimerService = null;
			RandomService = null;
			PerformanceTrackingService = null;
			ResourceEconomyService = null;
			NavMeshPathfinder = null;
			SimulationService = null;
			MovementPredictionService?.Clear();
			MovementPredictionService = null;
			InputService = null;
			ClientRPC = null;
			PerspectiveFilter = null;
			RaycastService = null;
			BaseMapFolderWatchService?.Dispose();
			BaseMapFolderWatchService = null;
			IEditorAPI = null;

			ECSContext.DisposeAndRecreateServices();
		}

		public static void DisposeAndRecreateServices()
		{
			Dispose();
			EnsureServices();
		}
	}
}
