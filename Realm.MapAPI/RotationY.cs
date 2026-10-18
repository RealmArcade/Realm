using System.Runtime.InteropServices;

namespace Realm.MapAPI;

/// <summary>
/// Represents the planar facing orientation angle in radians around the vertical Y-axis.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct RotationY
{
	/// <summary>The rotation angle in radians.</summary>
	public float Value;

	/// <summary>
	/// Initializes a new instance of the <see cref="RotationY"/> struct.
	/// </summary>
	/// <param name="value">The angle in radians.</param>
	public RotationY(float value)
	{
		Value = value;
	}

	/// <summary>
	/// Implicitly converts a float into a <see cref="RotationY"/> struct.
	/// </summary>
	public static implicit operator RotationY(float value) => new(value);

	/// <summary>
	/// Implicitly converts a <see cref="RotationY"/> struct into a float.
	/// </summary>
	public static implicit operator float(RotationY rotation) => rotation.Value;
}
