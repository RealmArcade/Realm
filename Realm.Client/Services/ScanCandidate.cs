using Realm.Ecs;

namespace Realm.Client.Services;

public readonly struct ScanCandidate
{
	public readonly Entity Enemy;
	public readonly float DistSq;

	public ScanCandidate(Entity enemy, float distSq)
	{
		Enemy = enemy;
		DistSq = distSq;
	}
}