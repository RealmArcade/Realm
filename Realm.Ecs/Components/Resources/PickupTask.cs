using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Tracks the ground item a unit was sent to collect. The item is taken only while the unit stands within the
///     item's own pickup radius, so the order is a walk followed by a collection rather than a remote pickup.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct PickupTask
{
	/// <summary>Gets or sets the ground item this unit is walking to.</summary>
	public Entity LootEntity;

	public PickupTask(Entity lootEntity)
	{
		LootEntity = lootEntity;
	}
}
