using System.Numerics;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Tracks the item a unit was told to put down: which item, how many pieces of it leave the inventory, and the spot
///     the unit walks to before it lets go.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct DropItemTask
{
	public int ItemStringId;

	public int Charges;

	public Vector3 Target;

	public DropItemTask(string itemId, int charges, Vector3 target)
	{
		ItemStringId = StringRegistry.GetOrAdd(itemId);
		Charges = charges;
		Target = target;
	}

	public string ItemId
	{
		get => StringRegistry.Get(ItemStringId);
		set => ItemStringId = StringRegistry.GetOrAdd(value);
	}
}
