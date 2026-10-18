using System.Runtime.InteropServices;

namespace Realm.MapAPI;

/// <summary>
/// Represents the hero or unit experience progression level.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Level
{
	/// <summary>The current level value.</summary>
	public int Value;

	/// <summary>
	/// Initializes a new instance of the <see cref="Level"/> struct.
	/// </summary>
	/// <param name="value">The level number.</param>
	public Level(int value)
	{
		Value = value;
	}

	/// <summary>
	/// Implicitly converts an integer into a <see cref="Level"/> struct.
	/// </summary>
	public static implicit operator Level(int value) => new(value);

	/// <summary>
	/// Implicitly converts a <see cref="Level"/> struct into an integer.
	/// </summary>
	public static implicit operator int(Level level) => level.Value;
}
