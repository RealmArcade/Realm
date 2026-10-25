using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Combat;

/// <summary>
///     Marks a unit as summoned with a lifespan. A zero remaining time means it lasts until killed.
///     The source entity is kept so a caster's per-template summon cap can be counted without
///     trusting ownership alone, which changes when a summon is converted or gifted.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct SummonedUnit
{
	public float Remaining;
	public float Duration;
	public int SourceEntityId;
	public int OwnerPlayerIndex;
	public int TemplateStringId;

	public SummonedUnit(float remaining, int sourceEntityId, int ownerPlayerIndex, int templateStringId)
	{
		Remaining = remaining;
		Duration = remaining;
		SourceEntityId = sourceEntityId;
		OwnerPlayerIndex = ownerPlayerIndex;
		TemplateStringId = templateStringId;
	}
}
