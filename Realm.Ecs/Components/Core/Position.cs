using System.Numerics;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Defines 3D spatial world position vector coordinates of an entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Position
{
	public Vector3 Value;

	public Position(Vector3 value)
	{
		Value = value;
	}

	public Position(float x, float y, float z)
	{
		Value = new Vector3(x, y, z);
	}

	public static implicit operator Vector3(Position position) => position.Value;
	public static implicit operator Position(Vector3 value) => new(value);
}
