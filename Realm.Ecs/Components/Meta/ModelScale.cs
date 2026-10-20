using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Meta;

/// <summary>
/// Defines visual mesh scaling factor for an entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ModelScale
{
	public float Value;

	public ModelScale(float value)
	{
		Value = value;
	}

	public static implicit operator float(ModelScale scale) => scale.Value;
	public static implicit operator ModelScale(float value) => new(value);
}
