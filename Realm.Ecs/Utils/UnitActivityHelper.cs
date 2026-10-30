using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;


namespace Realm.Ecs.Utils;

public static class UnitActivityHelper
{
	public static bool IsUnitActive(EcsWorld world, Entity entity)
	{
		return world.Has<MoveTo>(entity) ||
			   world.Has<BuildTask>(entity) ||
			   world.Has<AttackTarget>(entity) ||
			   world.Has<AttackMove>(entity) ||
			   world.Has<Follow>(entity) ||
			   world.Has<Patrol>(entity) ||
			   world.Has<Gatherer>(entity) ||
			   world.Has<WaypointQueue>(entity) ||
			   (world.Has<BuildQueue>(entity) && world.Get<BuildQueue>(entity).Count > 0);
	}
}
