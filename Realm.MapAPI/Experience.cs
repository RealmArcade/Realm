using System.Runtime.InteropServices;

namespace Realm.MapAPI;

/// <summary>
/// Represents the total accumulated experience points of a hero or entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Experience
{
	/// <summary>The total experience points accumulated.</summary>
	public float Value;

	/// <summary>
	/// Initializes a new instance of the <see cref="Experience"/> struct.
	/// </summary>
	/// <param name="value">The experience points.</param>
	public Experience(float value)
	{
		Value = value;
	}

	/// <summary>
	/// Implicitly converts a float into an <see cref="Experience"/> struct.
	/// </summary>
	public static implicit operator Experience(float value) => new(value);

	/// <summary>
	/// Implicitly converts an <see cref="Experience"/> struct into a float.
	/// </summary>
	public static implicit operator float(Experience experience) => experience.Value;
}
