using System.Text.Json.Serialization;

namespace Realm.Shared.Metadata;

/// <summary>
///     Map-authored combat tuning. Every value is optional: an absent field keeps the engine
///     default, so a map only authors the numbers it actually wants to change and a map that
///     authors nothing behaves exactly like the built-in rules.
///     The defaults live on <c>Realm.Ecs.Services.CombatTuning</c> and <c>CombatConfig</c>; this
///     type mirrors their field names so an authored value maps to exactly one runtime field.
/// </summary>
public class CombatTuningMetadata
{
	/// <summary>
	///     Damage-to-armor multipliers. A pair that is not listed resolves through
	///     <see cref="UndefinedPairMultiplier" />.
	/// </summary>
	[JsonPropertyName("DamageMatrix")] public List<DamageMatrixEntryMetadata>? DamageMatrix { get; set; }

	/// <summary>Multiplier used for a damage/armor pair the matrix does not define. Default 1.0.</summary>
	[JsonPropertyName("UndefinedPairMultiplier")] public float? UndefinedPairMultiplier { get; set; }

	/// <summary>K in the asymptotic rated-armor mitigation curve. Higher is flatter. Default 16.67.</summary>
	[JsonPropertyName("ArmorTuningConstantK")] public float? ArmorTuningConstantK { get; set; }

	/// <summary>Damage floor applied after mitigation. Default 1.</summary>
	[JsonPropertyName("MinDamageFloor")] public float? MinDamageFloor { get; set; }

	[JsonPropertyName("StrengthAttackPerPoint")] public float? StrengthAttackPerPoint { get; set; }
	[JsonPropertyName("StrengthArmorPerPoint")] public float? StrengthArmorPerPoint { get; set; }
	[JsonPropertyName("StrengthTenacityK")] public float? StrengthTenacityK { get; set; }
	[JsonPropertyName("MaxTenacity")] public float? MaxTenacity { get; set; }

	[JsonPropertyName("AgilityHastePerPoint")] public float? AgilityHastePerPoint { get; set; }
	[JsonPropertyName("HastePercentDivisor")] public float? HastePercentDivisor { get; set; }
	[JsonPropertyName("AgilityMovementPerPoint")] public float? AgilityMovementPerPoint { get; set; }
	[JsonPropertyName("MovementSoftCap1")] public float? MovementSoftCap1 { get; set; }
	[JsonPropertyName("MovementSoftCap2")] public float? MovementSoftCap2 { get; set; }
	[JsonPropertyName("MovementSoftCap1Taper")] public float? MovementSoftCap1Taper { get; set; }
	[JsonPropertyName("MovementSoftCap2Taper")] public float? MovementSoftCap2Taper { get; set; }
	[JsonPropertyName("AgilityCooldownHastePerPoint")] public float? AgilityCooldownHastePerPoint { get; set; }
	[JsonPropertyName("CooldownHasteDivisor")] public float? CooldownHasteDivisor { get; set; }
	[JsonPropertyName("MaxCooldownReduction")] public float? MaxCooldownReduction { get; set; }

	[JsonPropertyName("VitalityHpPerPoint")] public float? VitalityHpPerPoint { get; set; }
	[JsonPropertyName("VitalityManaPerPoint")] public float? VitalityManaPerPoint { get; set; }
	[JsonPropertyName("VitalityLifeStealK")] public float? VitalityLifeStealK { get; set; }
	[JsonPropertyName("MaxLifeSteal")] public float? MaxLifeSteal { get; set; }

	[JsonPropertyName("IntelligenceSpellAmpPerPoint")] public float? IntelligenceSpellAmpPerPoint { get; set; }
	[JsonPropertyName("IntelligenceSpellAmpExponent")] public float? IntelligenceSpellAmpExponent { get; set; }
	[JsonPropertyName("IntelligenceManaRegenPerPoint")] public float? IntelligenceManaRegenPerPoint { get; set; }
	[JsonPropertyName("IntelligenceMagicResistK")] public float? IntelligenceMagicResistK { get; set; }
	[JsonPropertyName("MaxMagicResist")] public float? MaxMagicResist { get; set; }

	[JsonPropertyName("WisdomMagicPenetrationK")] public float? WisdomMagicPenetrationK { get; set; }
	[JsonPropertyName("MaxMagicPenetration")] public float? MaxMagicPenetration { get; set; }
	[JsonPropertyName("WisdomHealingAmpPerPoint")] public float? WisdomHealingAmpPerPoint { get; set; }
	[JsonPropertyName("WisdomHpRegenPerPoint")] public float? WisdomHpRegenPerPoint { get; set; }
	[JsonPropertyName("WisdomCritMultiplierPerPoint")] public float? WisdomCritMultiplierPerPoint { get; set; }

	[JsonPropertyName("FortuneCritChanceK")] public float? FortuneCritChanceK { get; set; }
	[JsonPropertyName("MaxCritChance")] public float? MaxCritChance { get; set; }
	[JsonPropertyName("FortuneEvasionK")] public float? FortuneEvasionK { get; set; }
	[JsonPropertyName("MaxEvasion")] public float? MaxEvasion { get; set; }
	[JsonPropertyName("FortuneProcRateK")] public float? FortuneProcRateK { get; set; }
	[JsonPropertyName("MaxProcRateBonus")] public float? MaxProcRateBonus { get; set; }
	[JsonPropertyName("FortuneBountyPerPoint")] public float? FortuneBountyPerPoint { get; set; }

	[JsonPropertyName("MaxAttackSpeed")] public float? MaxAttackSpeed { get; set; }
	[JsonPropertyName("MinAttackSpeed")] public float? MinAttackSpeed { get; set; }
	[JsonPropertyName("MinAttackInterval")] public float? MinAttackInterval { get; set; }
	[JsonPropertyName("BaseCastPoint")] public float? BaseCastPoint { get; set; }
	[JsonPropertyName("BaseCritMultiplier")] public float? BaseCritMultiplier { get; set; }
}

/// <summary>One damage-type against armor-type multiplier.</summary>
public class DamageMatrixEntryMetadata
{
	[JsonPropertyName("DamageType")] public string? DamageType { get; set; }
	[JsonPropertyName("ArmorType")] public string? ArmorType { get; set; }
	[JsonPropertyName("Multiplier")] public float? Multiplier { get; set; }
}
