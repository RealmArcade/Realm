using Realm.Ecs.Components.Core;

namespace Realm.Ecs.Services;

/// <summary>
///     The food cap as one rule both processes read: a unit or building fits only while what it costs, added to the
///     population the player already holds, stays at or under the cap. It rules only on the arithmetic, so whoever asks
///     decides what counts - a site being raised is not a building yet and takes nothing from the cap while it stands
///     unfinished. The authority refuses orders with it and the screen colours a silhouette with the same answer.
/// </summary>
public static class PlayerPopulationRules
{
	/// <summary>
	///     Reads the population a player holds and the cap the match allows.
	/// </summary>
	/// <param name="world">The world the player belongs to.</param>
	/// <param name="playerEntity">The player entity, which carries the reading.</param>
	/// <param name="current">The population the player holds.</param>
	/// <param name="maximum">The cap the map allows.</param>
	/// <returns>Whether the player carries a population reading at all.</returns>
	public static bool TryRead(EcsWorld world, Entity playerEntity, out int current, out int maximum)
	{
		current = 0;
		maximum = 0;
		if (world == null || playerEntity == Entity.Null || !world.IsAlive(playerEntity)) return false;
		if (!world.TryGet<PlayerPopulation>(playerEntity, out PlayerPopulation population)) return false;

		current = population.Current;
		maximum = population.Max;
		return true;
	}

	/// <summary>
	///     Reports whether one more entity fits under the player's food cap. A cost of zero fits everywhere, and a player
	///     with no population reading is under no cap, so both answer true rather than refusing a rule the world never
	///     stated.
	/// </summary>
	/// <param name="world">The world the player belongs to.</param>
	/// <param name="playerEntity">The player asking to hold one more entity.</param>
	/// <param name="populationCost">What that entity costs to hold.</param>
	/// <returns>Whether the cost fits under the cap.</returns>
	public static bool FitsWithinCap(EcsWorld world, Entity playerEntity, int populationCost)
	{
		if (populationCost <= 0) return true;
		if (!TryRead(world, playerEntity, out int current, out int maximum)) return true;

		return current + populationCost <= maximum;
	}
}
