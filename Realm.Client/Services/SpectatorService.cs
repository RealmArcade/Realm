using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;

namespace Realm.Client.Services;

public class SpectatorService
{
	private Entity FindWorldEntity()
	{
		Entity worldEntity = Entity.Null;
		var query = QueryCache.AllNetworkMappingStateQuery;
		GameContext.World.Query(in query, (Entity entity) => worldEntity = entity);
		return worldEntity;
	}

	public int GetSpectatorPerspective()
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && GameContext.World.IsAlive(worldEntity) && GameContext.World.Has<SpectatorPerspective>(worldEntity))
		{
			return GameContext.World.Get<SpectatorPerspective>(worldEntity).Value;
		}

		return -1;
	}

	public void SetSpectatorPerspective(int value)
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && GameContext.World.IsAlive(worldEntity))
		{
			GameContext.World.SetOrAdd(worldEntity, new SpectatorPerspective(value));
		}
	}
}