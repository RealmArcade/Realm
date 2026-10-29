using MemoryPack;

namespace Realm.Shared.Network;

[MemoryPackable]
public partial struct LootSnapshot
{
	public int EntityId;
	public string ItemTypeId;
	public int Charges;
	public float Lifetime;
	public float RemainingLifetime;
	public float PickupRadius;
	public bool AutoPickup;
	public int OwnerPlayerIndex;
	public NetworkVector3 Position;
}
