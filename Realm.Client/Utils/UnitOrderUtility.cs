using Arch.Core;
using Realm.Client.Core;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Services;
using Realm.Client.Services;

namespace Realm.Client.Utils;

public static class UnitOrderUtility
{
	public static void ClearUnitOrders(Entity entity)
	{
		RemoveIfHas<MoveTo>(entity);
		RemoveIfHas<PathFollow>(entity);
		RemoveIfHas<AttackTarget>(entity);
		RemoveIfHas<AttackMove>(entity);
		RemoveIfHas<HoldPosition>(entity);
		RemoveIfHas<Follow>(entity);
		RemoveIfHas<Patrol>(entity);
		RemoveIfHas<HealingTarget>(entity);
		RemoveIfHas<WaypointQueue>(entity);
		RemoveIfHas<Gatherer>(entity);
		RemoveIfHas<BuildTask>(entity);
		RemoveIfHas<BuildQueue>(entity);
	}

	private static void RemoveIfHas<T>(Entity entity) where T : struct
	{
		if (GameContext.World.Has<T>(entity)) GameContext.World.Remove<T>(entity);
	}
}
