using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Meta;

/// <summary>
/// Tracks rotational azimuth heading around the vertical Y-axis in radians.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct RotationY
{
	public float Value;

	public RotationY(float value)
	{
		Value = value;
	}

	public static implicit operator float(RotationY rotation) => rotation.Value;
	public static implicit operator RotationY(float value) => new(value);
}
