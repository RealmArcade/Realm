using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Describes one carried item: which item it is and how many charges of it remain.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct InventorySlot
{
	public int ItemStringId;
	public int Count;
}
