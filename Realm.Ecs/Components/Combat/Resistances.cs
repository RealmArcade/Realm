using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Combat;

/// <summary>
///     The two damage resistances, in points. Armor resists physical damage, magic resistance
///     resists magic damage, and neither resists pure damage.
///     They are stored as points rather than as a finished percentage so a percentage bonus is
///     applied exactly once, by the stat pipeline, and can never compound with itself: the
///     fraction a hit loses is only computed from the final points, in the resolver.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Resistances
{
	/// <summary>Points of physical damage resistance.</summary>
	public float Physical;

	/// <summary>Points of magic damage resistance.</summary>
	public float Magic;

	public Resistances(float physical, float magic = 0f)
	{
		Physical = physical;
		Magic = magic;
	}

	public readonly float Armor => Physical;
	public readonly float MagicResistance => Magic;
}
