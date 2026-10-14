namespace Realm.Ecs.Components.Resources;

/// <summary>
///     A component for harvestable resource node entities storing resource type, amount, capacity, harvest rate, growth rate, and worker slot limits.
/// </summary>
public record struct ResourceNode(
	Guid ResourceTypeId,
	float Amount,
	float MaxCapacity = 2000f,
	float HarvestRate = 10f,
	float GrowthRate = 0f,
	int MaxWorkers = 5
);