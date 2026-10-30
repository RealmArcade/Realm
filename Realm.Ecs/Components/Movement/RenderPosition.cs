using System.Numerics;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Movement;

/// <summary>
///     Stores the visual render position each frame draws from, so a unit interpolates between the two positions the presentation step wrote rather than being read mid-write.
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
