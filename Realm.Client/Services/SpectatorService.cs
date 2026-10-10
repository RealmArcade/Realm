using Arch.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Services;

namespace Realm.Client.Services;

public class SpectatorService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World EcsWorld => _ecsWorldAccessor.Current;

	public SpectatorService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	private Entity FindWorldEntity()
	{
		Entity worldEntity = Entity.Null;
		var query = Realm.Ecs.Common.QueryCache.AllNetworkMappingStateQuery;
		EcsWorld.Query(in query, (Entity entity) => worldEntity = entity);
		return worldEntity;
	}

	public int GetSpectatorPerspective()
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && EcsWorld.IsAlive(worldEntity) && EcsWorld.Has<SpectatorPerspective>(worldEntity))
		{
			return EcsWorld.Get<SpectatorPerspective>(worldEntity).Value;
		}
		return -1;
	}

	public void SetSpectatorPerspective(int value)
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && EcsWorld.IsAlive(worldEntity))
		{
			EcsWorld.SetOrAdd(worldEntity, new SpectatorPerspective(value));
		}
	}
}