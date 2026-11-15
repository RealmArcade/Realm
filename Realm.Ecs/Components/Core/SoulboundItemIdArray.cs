using System.Runtime.CompilerServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Holds the item string identifiers a unit's soulbound set names, sized to the number of slots a unit can fill.
/// </summary>
[InlineArray(Capacity)]
public struct SoulboundItemIdArray
{
	public const int Capacity = InventorySlotArray.Capacity;

	private int _first;
}
