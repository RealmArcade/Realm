using System.Runtime.InteropServices;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Realm.Ecs.Components.Movement;

[InlineArray(16)]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct WaypointBuffer
{
	public Vector3 Element0;
	public const int Length = 16;
}