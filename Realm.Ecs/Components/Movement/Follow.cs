using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Movement;

/// <summary>Follow component: unit follows a target entity.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Follow
{
	public Entity Target;

	public Follow(Entity target)
	{
		Target = target;
	}
}