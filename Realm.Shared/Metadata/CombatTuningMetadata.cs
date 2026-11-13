using System.Text.Json.Serialization;
using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("The combat curve constants a map may override. They turn the attributes a unit carries into damage, mitigation, haste, crit and bounty, so a map changes its whole feel without retuning a single unit.")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class CombatTuningMetadata
{
	[JsonPropertyName("ResistTuningConstantK")]
	[SchemaDescription("How strongly armour and magic resistance convert into damage reduction. Higher values make every point of resistance worth less.")]
	public float? ResistTuningConstantK { get; set; }

	[JsonPropertyName("MaxResistMitigation")]
	[SchemaDescription("The most damage armour or magic resistance may ever remove, as a fraction of one. Absent means one, which allows total immunity.")]
	[SchemaUnit("%")]
	[SchemaRange(0, 1)]
	public float? MaxResistMitigation { get; set; }

	[JsonPropertyName("MinDamageFloor")]
	[SchemaDescription("The smallest share of its damage an attack always deals, however well armoured the target is.")]
	[SchemaUnit("%")]
	[SchemaRange(0, 1)]
	public float? MinDamageFloor { get; set; }

	[JsonPropertyName("StrengthAttackPerPoint")]
	[SchemaDescription("How much attack damage one point of strength adds.")]
	public float? StrengthAttackPerPoint { get; set; }

	[JsonPropertyName("StrengthArmorPerPoint")]
	[SchemaDescription("How much armour one point of strength adds.")]
	public float? StrengthArmorPerPoint { get; set; }

	[JsonPropertyName("StrengthTenacityK")]
	[SchemaDescription("How strongly strength converts into tenacity, the resistance to being pushed and controlled.")]
	public float? StrengthTenacityK { get; set; }

	[JsonPropertyName("MaxTenacity")]
	[SchemaDescription("The most tenacity a unit may reach, as a fraction of one.")]
	[SchemaUnit("%")]
	[SchemaRange(0, 1)]
	public float? MaxTenacity { get; set; }

	[JsonPropertyName("AgilityHastePerPoint")]
	[SchemaDescription("How much attack haste one point of agility adds.")]
	public float? AgilityHastePerPoint { get; set; }

	[JsonPropertyName("HastePercentDivisor")]
	[SchemaDescription("What accumulated haste is divided by to turn it into extra attacks, which keeps a large agility pool from breaking attack speed.")]
	public float? HastePercentDivisor { get; set; }

	[JsonPropertyName("AgilityMovementPerPoint")]
	[SchemaDescription("How much movement speed one point of agility adds.")]
	public float? AgilityMovementPerPoint { get; set; }

	[JsonPropertyName("MovementSoftCap1")]
	[SchemaDescription("The first speed at which movement bonuses start to diminish.")]
	[SchemaUnit("m/s")]
	public float? MovementSoftCap1 { get; set; }

	[JsonPropertyName("MovementSoftCap2")]
	[SchemaDescription("The second speed at which movement bonuses diminish harder still.")]
	[SchemaUnit("m/s")]
	public float? MovementSoftCap2 { get; set; }

	[JsonPropertyName("MovementSoftCap1Taper")]
	[SchemaDescription("How sharply movement bonuses diminish past the first soft cap.")]
	public float? MovementSoftCap1Taper { get; set; }

	[JsonPropertyName("MovementSoftCap2Taper")]
	[SchemaDescription("How sharply movement bonuses diminish past the second soft cap.")]
	public float? MovementSoftCap2Taper { get; set; }

	[JsonPropertyName("AgilityCooldownHastePerPoint")]
	[SchemaDescription("How much cooldown reduction one point of agility adds.")]
	public float? AgilityCooldownHastePerPoint { get; set; }

	[JsonPropertyName("CooldownHasteDivisor")]
	[SchemaDescription("What accumulated cooldown haste is divided by to turn it into shorter cooldowns.")]
	public float? CooldownHasteDivisor { get; set; }

	[JsonPropertyName("MaxCooldownReduction")]
	[SchemaDescription("The most cooldown reduction a unit may reach, as a fraction of one.")]
	[SchemaUnit("%")]
	[SchemaRange(0, 1)]
	public float? MaxCooldownReduction { get; set; }

	[JsonPropertyName("VitalityHpPerPoint")]
	[SchemaDescription("How much health one point of vitality adds.")]
	public float? VitalityHpPerPoint { get; set; }

	[JsonPropertyName("VitalityManaPerPoint")]
	[SchemaDescription("How much mana one point of vitality adds.")]
	public float? VitalityManaPerPoint { get; set; }

	[JsonPropertyName("VitalityLifeStealK")]
	[SchemaDescription("How strongly vitality converts into life steal.")]
	public float? VitalityLifeStealK { get; set; }

	[JsonPropertyName("MaxLifeSteal")]
	[SchemaDescription("The most life steal a unit may reach, as a fraction of the damage it deals.")]
	[SchemaUnit("%")]
	[SchemaRange(0, 1)]
	public float? MaxLifeSteal { get; set; }

	[JsonPropertyName("IntelligenceSpellAmpPerPoint")]
	[SchemaDescription("How much spell power one point of intelligence adds.")]
	public float? IntelligenceSpellAmpPerPoint { get; set; }

	[JsonPropertyName("IntelligenceSpellAmpExponent")]
	[SchemaDescription("How sharply spell power from intelligence falls off as the pool grows, so a large pool does not scale linearly.")]
	public float? IntelligenceSpellAmpExponent { get; set; }

	[JsonPropertyName("IntelligenceManaRegenPerPoint")]
	[SchemaDescription("How much mana regeneration one point of intelligence adds.")]
	public float? IntelligenceManaRegenPerPoint { get; set; }

	[JsonPropertyName("IntelligenceMagicResistancePerPoint")]
	[SchemaDescription("How much magic resistance one point of intelligence adds.")]
	public float? IntelligenceMagicResistancePerPoint { get; set; }

	[JsonPropertyName("WisdomMagicPenetrationK")]
	[SchemaDescription("How strongly wisdom converts into magic penetration.")]
	public float? WisdomMagicPenetrationK { get; set; }

	[JsonPropertyName("MaxPercentMagicPenetration")]
	[SchemaDescription("The most magic penetration a unit may reach, as a fraction of the target's resistance.")]
	[SchemaUnit("%")]
	[SchemaRange(0, 1)]
	public float? MaxPercentMagicPenetration { get; set; }

	[JsonPropertyName("WisdomHealingAmpPerPoint")]
	[SchemaDescription("How much healing power one point of wisdom adds.")]
	public float? WisdomHealingAmpPerPoint { get; set; }

	[JsonPropertyName("WisdomHpRegenPerPoint")]
	[SchemaDescription("How much health regeneration one point of wisdom adds.")]
	public float? WisdomHpRegenPerPoint { get; set; }

	[JsonPropertyName("WisdomCritMultiplierPerPoint")]
	[SchemaDescription("How much a crit multiplies damage by, per point of wisdom.")]
	public float? WisdomCritMultiplierPerPoint { get; set; }

	[JsonPropertyName("FortuneCritChanceK")]
	[SchemaDescription("How strongly fortune converts into crit chance.")]
	public float? FortuneCritChanceK { get; set; }

	[JsonPropertyName("MaxCritChance")]
	[SchemaDescription("The highest crit chance a unit may reach, as a fraction of one.")]
	[SchemaUnit("%")]
	[SchemaRange(0, 1)]
	public float? MaxCritChance { get; set; }

	[JsonPropertyName("FortuneEvasionK")]
	[SchemaDescription("How strongly fortune converts into evasion, the chance an incoming attack misses.")]
	public float? FortuneEvasionK { get; set; }

	[JsonPropertyName("MaxEvasion")]
	[SchemaDescription("The highest evasion a unit may reach, as a fraction of one.")]
	[SchemaUnit("%")]
	[SchemaRange(0, 1)]
	public float? MaxEvasion { get; set; }

	[JsonPropertyName("FortuneProcRateK")]
	[SchemaDescription("How strongly fortune converts into the chance an on-hit effect fires.")]
	public float? FortuneProcRateK { get; set; }

	[JsonPropertyName("MaxProcRateBonus")]
	[SchemaDescription("The highest bonus an on-hit effect's chance may reach, as a fraction of one.")]
	[SchemaUnit("%")]
	[SchemaRange(0, 1)]
	public float? MaxProcRateBonus { get; set; }

	[JsonPropertyName("FortuneBountyPerPoint")]
	[SchemaDescription("How much extra gold a killer is paid per point of fortune.")]
	public float? FortuneBountyPerPoint { get; set; }

	[JsonPropertyName("MaxAttackSpeed")]
	[SchemaDescription("The fastest a unit may ever attack, in attacks per second.")]
	public float? MaxAttackSpeed { get; set; }

	[JsonPropertyName("MinAttackSpeed")]
	[SchemaDescription("The slowest a unit may ever be slowed to, in attacks per second.")]
	public float? MinAttackSpeed { get; set; }

	[JsonPropertyName("MinAttackInterval")]
	[SchemaDescription("The shortest gap the fastest attack speed may reach, in seconds between attacks.")]
	[SchemaUnit("s")]
	public float? MinAttackInterval { get; set; }

	[JsonPropertyName("BaseCastPoint")]
	[SchemaDescription("How long every unit winds up before a spell leaves its hands, before any per-ability change.")]
	[SchemaUnit("s")]
	public float? BaseCastPoint { get; set; }

	[JsonPropertyName("BaseCritMultiplier")]
	[SchemaDescription("What a crit multiplies damage by before any crit bonus is added.")]
	public float? BaseCritMultiplier { get; set; }
}
