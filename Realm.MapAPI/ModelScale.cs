using System.Runtime.InteropServices;

namespace Realm.MapAPI;

/// <summary>
/// Represents the visual model uniform scaling factor of an entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ModelScale
{
	/// <summary>The visual scale multiplier.</summary>
	public float Value;

	/// <summary>
	/// Initializes a new instance of the <see cref="ModelScale"/> struct.
	/// </summary>
	/// <param name="value">The uniform scale factor.</param>
	public ModelScale(float value)
	{
		Value = value;
	}

	/// <summary>
	/// Implicitly converts a float into a <see cref="ModelScale"/> struct.
	/// </summary>
	public static implicit operator ModelScale(float value) => new(value);

	/// <summary>
	/// Implicitly converts a <see cref="ModelScale"/> struct into a float.
	/// </summary>
	public static implicit operator float(ModelScale scale) => scale.Value;
}
