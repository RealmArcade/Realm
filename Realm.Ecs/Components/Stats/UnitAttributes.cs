namespace Realm.Ecs.Components.Stats;

using Realm.Ecs.Common;

/// <summary>
/// Represents the 6 core RPG attributes for an entity.
/// </summary>
public record struct UnitAttributes(
	[property: Tooltip("Attack damage, Tenacity (Crowd-control resistance), Armor (physical damage reduction).")]
	float Strength = 0f,
	[property: Tooltip("Attack Speed, Movement Speed, Cooldown reduction.")]
	float Agility = 0f,
	[property: Tooltip("Max Health, Max Mana, Life steal.")]
	float Vitality = 0f,
	[property: Tooltip("Ability damage, Mana regeneration, Magic resistance.")]
	float Intelligence = 0f,
	[property: Tooltip("Magic penetration, Healing power / HP regen rate, Critical multiplier.")]
	float Wisdom = 0f,
	[property: Tooltip("Critical chance, Evasion, Proc rates (on-hit effects), Bounty yield (XP gain ratio).")]
	float Fortune = 0f)
{
	public readonly float STR => Strength;
	public readonly float AGI => Agility;
	public readonly float VIT => Vitality;
	public readonly float INT => Intelligence;
	public readonly float WIS => Wisdom;
	public readonly float FORT => Fortune;
}
