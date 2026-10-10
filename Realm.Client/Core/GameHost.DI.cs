using Arch.Core;
using Microsoft.Extensions.DependencyInjection;
using Realm.Ecs.Services;
using Realm.EditorAPI;
using Realm.Client;
using Realm.Client.Core;
using Realm.Client.Services;

namespace Realm.Client.Core;

public partial class GameHost
{
	public override void _EnterTree()
	{
		ResolveServices();
	}

	private void ResolveServices()
	{
		ServiceLocator.EnsureServices();

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
		_modelOptimizerService = ServiceLocator.Get<Realm.Client.Services.ModelOptimization.ModelOptimizerService>();
		_terrainNavMeshService = ServiceLocator.Get<TerrainNavMeshService>();
		_metadataService = ServiceLocator.Get<Realm.Client.Services.MetadataService>();
		_mapUpgradeService = ServiceLocator.Get<Realm.Client.Services.MapUpgradeService>();
		_mapStorageService = ServiceLocator.Get<Realm.Client.Services.MapStorageService>();
		_mapSaveDataService = ServiceLocator.Get<Realm.Client.Services.MapSaveDataService>();
		_definitionManager = ServiceLocator.Get<DefinitionManager>();
		_simulationService = ServiceLocator.Get<SimulationService>();
		EcsWorld = ServiceLocator.Get<World>();
	}
}
