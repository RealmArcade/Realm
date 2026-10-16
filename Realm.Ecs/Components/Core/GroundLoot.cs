namespace Realm.Ecs.Components.Core;

/// <summary>
/// Holds state for ground loot pickup items spawned in the game world.
/// </summary>
public record struct GroundLoot
{
	public int UniqueId { get; set; }
	public string ItemTypeId { get; set; }
	public int Charges { get; set; }
	public float Lifetime { get; set; }
	public float RemainingLifetime { get; set; }
	public float PickupRadius { get; set; }
	public bool AutoPickup { get; set; }
	public int OwnerPlayerIndex { get; set; }
	public bool IsDespawned { get; set; }

	public GroundLoot(int uniqueId, string itemTypeId, int charges, float lifetime, float pickupRadius = 2.0f, bool autoPickup = true, int ownerPlayerIndex = -1)
	{
		UniqueId = uniqueId;
		ItemTypeId = itemTypeId;
		Charges = charges;
		Lifetime = lifetime;
		RemainingLifetime = lifetime;
		PickupRadius = pickupRadius;
		AutoPickup = autoPickup;
		OwnerPlayerIndex = ownerPlayerIndex;
		IsDespawned = false;
	}
}
