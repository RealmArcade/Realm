using System.Numerics;
using System.Runtime.InteropServices;

namespace Realm.MapAPI;

/// <summary>
/// Represents the 3D world space position vector of an entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Position
{
	/// <summary>
	/// The 3D coordinates vector.
	/// </summary>
	public Vector3 Value;

	/// <summary>
	/// Initializes a new instance of the <see cref="Position"/> struct.
	/// </summary>
	/// <param name="value">The 3D coordinates vector.</param>
	public Position(Vector3 value)
	{
		Value = value;
	}

	/// <summary>
	/// Implicitly converts a <see cref="Vector3"/> into a <see cref="Position"/> struct.
	/// </summary>
	/// <param name="value">The 3D coordinates vector.</param>
	public static implicit operator Position(Vector3 value) => new(value);

	/// <summary>
	/// Implicitly converts a <see cref="Position"/> struct into a <see cref="Vector3"/>.
	/// </summary>
	/// <param name="position">The position struct.</param>
	public static implicit operator Vector3(Position position) => position.Value;
}
