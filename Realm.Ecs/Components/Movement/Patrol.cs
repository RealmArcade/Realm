using System.Runtime.InteropServices;
using System.Numerics;

namespace Realm.Ecs.Components.Movement;

/// <summary>Patrol component: unit walks back and forth between PointA and PointB, attacking enemies on sight.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Patrol
{
	public Vector3 PointA;
	public Vector3 PointB;
	public byte GoingToBByte;

	public bool GoingToB
	{
		get => GoingToBByte != 0;
		set => GoingToBByte = value ? (byte)1 : (byte)0;
	}

	public Patrol(Vector3 a, Vector3 b)
	{
		PointA = a;
		PointB = b;
		GoingToBByte = 1;
	}
}