using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Meta;

/// <summary>
///     Carries the number and colour a unit's overhead badge is drawn with, which a map script sets for the units that
///     state a rank above themselves.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct OverheadBadge
{
	public int Value;

	public uint ColorRgb;
}
