using Arch.Core;

namespace Realm.Ecs.Components.Movement;

/// <summary>Follow component: unit follows a target entity.</summary>
public struct Follow
{
	public Entity Target;

	public Follow(Entity target)
	{
		Target = target;
	}
}
