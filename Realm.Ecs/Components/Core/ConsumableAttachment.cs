using System.Runtime.InteropServices;
using Realm.MapAPI;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// The consumable a unit is carrying, in the slot group it was bought for. Identifiers are interned
/// text ids so the component stays blittable while still naming a map-authored definition.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ConsumableAttachment
{
	public int DefinitionStringId;
	public int SlotGroupStringId;
	public int PurchasedByPlayer;

	public ConsumableAttachment(string definitionId, string slotGroup, int purchasedByPlayer)
	{
		DefinitionStringId = StringRegistry.GetOrAdd(definitionId);
		SlotGroupStringId = StringRegistry.GetOrAdd(slotGroup);
		PurchasedByPlayer = purchasedByPlayer;
	}

	public readonly bool HasDefinition => DefinitionStringId != 0;

	public readonly string GetDefinitionId() => StringRegistry.Get(DefinitionStringId);

	public readonly string GetSlotGroup() => StringRegistry.Get(SlotGroupStringId);

	public readonly bool IsInGroup(string group) => StringRegistry.GetOrAdd(group) == SlotGroupStringId;
}
