using System.Numerics;

namespace Realm.Ecs.Components.Movement;

/// <summary>Patrol component: unit walks back and forth between PointA and PointB, attacking enemies on sight.</summary>
public struct Patrol
{
	public Vector3 PointA;
	public Vector3 PointB;
	public bool GoingToB; // true = currently moving towards B

	public Patrol(Vector3 a, Vector3 b)
	{
		PointA  = a;
		PointB  = b;
		GoingToB = true;
	}
}
