using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Meta;

/// <summary>
/// Tracks accumulated hero experience points.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Experience
{
	public float Current;

	public Experience(float current)
	{
		Current = current;
	}

	public float Value => Current;

	public static implicit operator float(Experience experience) => experience.Current;
	public static implicit operator Experience(float current) => new(current);
}
