using System.Numerics;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Movement;

/// <summary>
///     Stores the visual render position for smooth interpolation during prediction and state reconciliation.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct RenderPosition
{
	public Vector3 Value;

	public RenderPosition(Vector3 value)
	{
		Value = value;
	}
}
