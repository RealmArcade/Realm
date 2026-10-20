using System.Runtime.InteropServices;
using System.Numerics;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Represents the boundary coordinates of a zone defined by a script.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ZoneBounds
{
	public float MinX;
	public float MinZ;
	public float MaxX;
	public float MaxZ;
	public Vector3 Center;
}