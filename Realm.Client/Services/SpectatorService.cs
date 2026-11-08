using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;

namespace Realm.Client.Services;

public class SpectatorService
{
	private static Entity FindWorldEntity(EcsWorld world)
	{
		Entity worldEntity = Entity.Null;
		var query = QueryCache.AllNetworkMappingStateQuery;
		world.Query(in query, (Entity entity) => worldEntity = entity);
		return worldEntity;
	}

	public int GetSpectatorPerspective()
	{
		EcsWorld? world = GameContext.World;
		if (world == null) return -1;

		var worldEntity = FindWorldEntity(world);
		if (worldEntity != Entity.Null && world.IsAlive(worldEntity) && world.Has<SpectatorPerspective>(worldEntity))
		{
			return world.Get<SpectatorPerspective>(worldEntity).Value;
		}

		return -1;
	}

	public void SetSpectatorPerspective(int value)
	{
		EcsWorld? world = GameContext.World;
		if (world == null) return;

		var worldEntity = FindWorldEntity(world);
		if (worldEntity != Entity.Null && world.IsAlive(worldEntity))
		{
			world.SetOrAdd(worldEntity, new SpectatorPerspective(value));
		}
	}
}