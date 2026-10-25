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
	/// <summary>K in the resistance curve <c>R / (R + K)</c>. Higher is flatter. Default 16.67.</summary>
	[JsonPropertyName("ResistTuningConstantK")] public float? ResistTuningConstantK { get; set; }

	/// <summary>Hard ceiling on the fraction of damage resistance can remove, as 0..1. Default 0.9.</summary>
	[JsonPropertyName("MaxResistMitigation")] public float? MaxResistMitigation { get; set; }

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

	/// <summary>Magic resistance points granted per point of Intelligence.</summary>
	[JsonPropertyName("IntelligenceMagicResistancePerPoint")] public float? IntelligenceMagicResistancePerPoint { get; set; }

	[JsonPropertyName("WisdomMagicPenetrationK")] public float? WisdomMagicPenetrationK { get; set; }
	[JsonPropertyName("MaxPercentMagicPenetration")] public float? MaxPercentMagicPenetration { get; set; }
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
