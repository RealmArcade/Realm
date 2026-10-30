using System.Runtime.InteropServices;
using System.Numerics;

namespace Realm.Ecs.Components.Movement;

/// <summary>
///     Marks a unit the authority describes, and holds what drawing it needs: the newest position the authority put it at, and the facing to draw at the moment being rendered, which the presentation step keeps level with the render cursor for a unit standing still.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct LatestDescription
{
	public Vector3 Position;
	public float RotationY;
}