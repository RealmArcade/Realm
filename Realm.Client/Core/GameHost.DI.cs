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

		_terrainNavMeshService = ServiceLocator.Get<TerrainNavMeshService>();
		_metadataService = ServiceLocator.Get<Realm.Client.Services.MetadataService>();
		_mapUpgradeService = ServiceLocator.Get<Realm.Client.Services.MapUpgradeService>();
		_mapStorageService = ServiceLocator.Get<Realm.Client.Services.MapStorageService>();
		_mapSaveDataService = ServiceLocator.Get<Realm.Client.Services.MapSaveDataService>();

	}
}
