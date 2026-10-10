using Arch.Core;
using Realm.Ecs.AI.Genres;

namespace Realm.Ecs.AI.Affordances;

/// <summary>
/// Scans the ECS world for candidate affordances (legal actions) available to a specific player.
/// Generates normalized feature vectors for each affordance.
/// </summary>
public class AffordanceScanner : StandardRtsGenreProvider, IAffordanceScanner
{
	public new const int FeatureCount = StandardRtsGenreProvider.StandardFeatureCount;

	public List<GenericAffordance> ScanAffordances(World world, int playerIndex)
	{
		var affordances = new List<GenericAffordance>(32);
		ScanAffordances(world, playerIndex, affordances, null);
		return affordances;
	}

	public List<GenericAffordance> ScanAffordances(World world, int playerIndex, object? definitionManager)
	{
		if (definitionManager is List<GenericAffordance> destList)
		{
			ScanAffordances(world, playerIndex, destList, null);
			return destList;
		}

		var affordances = new List<GenericAffordance>(32);
		ScanAffordances(world, playerIndex, affordances, definitionManager);
		return affordances;
	}

	public void ScanAffordances(World world, int playerIndex, List<GenericAffordance> destinationList)
	{
		ScanAffordances(world, playerIndex, destinationList, null);
	}
}
