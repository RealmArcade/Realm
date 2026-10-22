using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Client.Services;

namespace Realm.Client.Utils;

public static class UnitActivityHelper
{
	public static bool IsUnitActive(Entity entity)
	{
		return GameContext.World.Has<MoveTo>(entity) ||
			   GameContext.World.Has<BuildTask>(entity) ||
			   GameContext.World.Has<AttackTarget>(entity) ||
			   GameContext.World.Has<AttackMove>(entity) ||
			   GameContext.World.Has<Follow>(entity) ||
			   GameContext.World.Has<Patrol>(entity) ||
			   GameContext.World.Has<Gatherer>(entity) ||
			   GameContext.World.Has<WaypointQueue>(entity) ||
			   (GameContext.World.Has<BuildQueue>(entity) && GameContext.World.Get<BuildQueue>(entity).Count > 0);
	}
}
