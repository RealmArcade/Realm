using System.Numerics;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Stores prior simulation tick 3D spatial world position vector coordinates for visual state-snapshot interpolation.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct PreviousPosition
{
	public Vector3 Value;

	public PreviousPosition(Vector3 value)
	{
		Value = value;
	}

	public PreviousPosition(float x, float y, float z)
	{
		Value = new Vector3(x, y, z);
	}

	public static implicit operator Vector3(PreviousPosition position) => position.Value;
	public static implicit operator PreviousPosition(Vector3 value) => new(value);
}
