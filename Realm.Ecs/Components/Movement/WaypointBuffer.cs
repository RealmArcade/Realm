using System.Numerics;

namespace Realm.Ecs.Components.Movement;

[System.Runtime.CompilerServices.InlineArray(16)]
public struct WaypointBuffer
{
	public Vector3 Element0;
	public const int Length = 16;
}