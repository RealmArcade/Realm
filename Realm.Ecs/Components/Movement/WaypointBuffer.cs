using System.Numerics;
using System.Runtime.CompilerServices;

namespace Realm.Ecs.Components.Movement;

[InlineArray(16)]
public struct WaypointBuffer
{
	public Vector3 Element0;
	public const int Length = 16;
}