using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Meta;

/// <summary>
/// Tracks current hero advancement level.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Level
{
	public int Value;

	public Level(int value)
	{
		Value = value;
	}

	public static implicit operator int(Level level) => level.Value;
	public static implicit operator Level(int value) => new(value);
}
