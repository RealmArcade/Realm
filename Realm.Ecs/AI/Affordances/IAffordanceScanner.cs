
namespace Realm.Ecs.AI.Affordances;

public interface IAffordanceScanner
{
	int FeatureCount { get; }
	IReadOnlyList<string> FeatureNames { get; }
	void ScanAffordances(EcsWorld world, int playerIndex, List<GenericAffordance> destinationList, object? customContext = null);
}