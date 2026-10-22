using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Meta;

/// <summary>
/// Stores prior simulation tick rotational azimuth heading around the vertical Y-axis in radians for visual state-snapshot interpolation.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct PreviousRotationY
{
	public float Value;

	public PreviousRotationY(float value)
	{
		Value = value;
	}

	public static implicit operator float(PreviousRotationY rotation) => rotation.Value;
	public static implicit operator PreviousRotationY(float value) => new(value);
}
