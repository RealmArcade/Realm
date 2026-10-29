using MemoryPack;

namespace Realm.Shared.Network;

/// <summary>
///     One item lying on the map, described to the peers that can see it. Loot is not a unit: it carries no owner, no
///     health and no definition a screen could spawn a unit from, so it cannot ride the unit section. It travels as its
///     own short record, and a peer turns it into an entity with a position and a <c>GroundLoot</c> component, which is
///     what the screen already draws from.
/// </summary>
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
