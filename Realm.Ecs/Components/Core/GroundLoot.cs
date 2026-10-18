using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct GroundLoot
{
	public int UniqueId { get; set; }
	public int StringId;
	public int Charges { get; set; }
	public float Lifetime { get; set; }
	public float PickupRadius { get; set; }
	public bool AutoPickup { get; set; }
	public int OwnerPlayerIndex { get; set; }

	public GroundLoot(int uniqueId, string itemTypeId, int charges, float lifetime, float pickupRadius = 2.0f, bool autoPickup = true, int ownerPlayerIndex = -1)
	{
		UniqueId = uniqueId;
		StringId = StringRegistry.GetOrAdd(itemTypeId);
		Charges = charges;
		Lifetime = lifetime;
		PickupRadius = pickupRadius;
		AutoPickup = autoPickup;
		OwnerPlayerIndex = ownerPlayerIndex;
	}

	public string ItemTypeId
	{
		get => StringRegistry.Get(StringId);
		set => StringId = StringRegistry.GetOrAdd(value);
	}
}
