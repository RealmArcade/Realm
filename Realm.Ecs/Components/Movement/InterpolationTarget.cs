using System.Runtime.InteropServices;
using System.Numerics;

namespace Realm.Ecs.Components.Movement;

/// <summary>
///     Stores the network interpolation target state for non-local units.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct InterpolationTarget
{
	public Vector3 Position;
	public Vector3 Velocity;
	public float RotationY;
}