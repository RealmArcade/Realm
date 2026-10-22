using Realm.MapAPI;

namespace Realm.Client.Core;

public class EmptyMapScript : IMapScript
{
	// IMapScript contract: a map with no script still needs both no-op entry points.
	public void Initialize(IGameAPI api)
	{
	}

	public void Update(IGameAPI api, float delta)
	{
	}
}