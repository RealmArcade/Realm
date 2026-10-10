using Arch.Core;
using Realm.Ecs.Components.Resources;
namespace Realm.Ecs.Services;

/// <summary>
///     Demonstrates how player-specific resource data is used with archetype definition data.
/// </summary>
public class PlayerResourceService
{
	private readonly ArchetypeManager _archetypeManager;
	private readonly WorldAccessor _ecsWorldAccessor;

	public PlayerResourceService(WorldAccessor ecsWorldAccessor, ArchetypeManager archetypeManager)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
		_archetypeManager = archetypeManager;
	}

	/// <summary>
	///     Checks if a player can afford to build a unit defined by an archetype.
	/// </summary>
	public bool CanAfford(Entity playerEntity, string unitArchetypeId)
	{
		var archetype = _archetypeManager.GetUnitArchetype(unitArchetypeId);
		if (archetype?.ResourceCosts == null || archetype.ResourceCosts.Length == 0) return true; // No cost

		if (!_ecsWorldAccessor.Current.Has<PlayerResources>(playerEntity)) return false; // Player has no resources at all

		var playerResources = _ecsWorldAccessor.Current.Get<PlayerResources>(playerEntity).Value;

		foreach (var cost in archetype.ResourceCosts)
			if (!playerResources.TryGetValue(cost.ResourceTypeId, out var playerAmount) || playerAmount < cost.Amount)
				return false; // Player has insufficient funds for this resource type

		return true;
	}

	/// <summary>
	///     Deducts the cost of a unit from a player's resources.
	/// </summary>
	public void DeductCost(Entity playerEntity, string unitArchetypeId)
	{
		var archetype = _archetypeManager.GetUnitArchetype(unitArchetypeId);
		if (archetype?.ResourceCosts == null || !_ecsWorldAccessor.Current.Has<PlayerResources>(playerEntity)) return;

		ref var playerResources = ref _ecsWorldAccessor.Current.Get<PlayerResources>(playerEntity);

		foreach (var cost in archetype.ResourceCosts)
			if (playerResources.Value.ContainsKey(cost.ResourceTypeId))
				playerResources.Value[cost.ResourceTypeId] -= cost.Amount;
	}
}