using System.Runtime.CompilerServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Holds the fixed number of item slots one entity can carry, sized for the busiest hand authored map.
/// </summary>
[InlineArray(Capacity)]
public struct InventorySlotArray
{
	public const int Capacity = 16;

	private InventorySlot _first;
}
