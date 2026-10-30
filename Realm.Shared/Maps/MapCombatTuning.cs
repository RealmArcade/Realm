using System;
using Realm.Ecs.Services;
using Realm.Shared.Metadata;

namespace Realm.Shared.Maps;

public static class MapCombatTuning
{
	private const float TinyPositive = 0.0001f;
	private const float LargeBound = 100000f;

	public static void Apply(SimulationContext simulation, MapMetadata? metadata, List<string>? warnings = null)
	{
		CombatTuningMetadata? authored = metadata?.CombatTuning;
		if (authored == null)
		{
			simulation.ActiveCombatTuning = CombatTuning.Default;
			simulation.ActiveCombatConfig = new CombatConfig();
			return;
		}

		var fallback = CombatTuning.Default;
		var t = new CombatTuning
		{
			StrengthAttackPerPoint = Read(authored.StrengthAttackPerPoint, fallback.StrengthAttackPerPoint, "StrengthAttackPerPoint", 0f, LargeBound, warnings),
			StrengthArmorPerPoint = Read(authored.StrengthArmorPerPoint, fallback.StrengthArmorPerPoint, "StrengthArmorPerPoint", 0f, LargeBound, warnings),
			StrengthTenacityK = Read(authored.StrengthTenacityK, fallback.StrengthTenacityK, "StrengthTenacityK", TinyPositive, LargeBound, warnings),
			MaxTenacity = Read(authored.MaxTenacity, fallback.MaxTenacity, "MaxTenacity", 0f, 1f, warnings),

			AgilityHastePerPoint = Read(authored.AgilityHastePerPoint, fallback.AgilityHastePerPoint, "AgilityHastePerPoint", 0f, LargeBound, warnings),
			HastePercentDivisor = Read(authored.HastePercentDivisor, fallback.HastePercentDivisor, "HastePercentDivisor", TinyPositive, LargeBound, warnings),
			AgilityMovementPerPoint = Read(authored.AgilityMovementPerPoint, fallback.AgilityMovementPerPoint, "AgilityMovementPerPoint", 0f, LargeBound, warnings),
			MovementSoftCap1 = Read(authored.MovementSoftCap1, fallback.MovementSoftCap1, "MovementSoftCap1", 0f, LargeBound, warnings),
			MovementSoftCap2 = Read(authored.MovementSoftCap2, fallback.MovementSoftCap2, "MovementSoftCap2", 0f, LargeBound, warnings),
			MovementSoftCap1Taper = Read(authored.MovementSoftCap1Taper, fallback.MovementSoftCap1Taper, "MovementSoftCap1Taper", 0f, 1f, warnings),
			MovementSoftCap2Taper = Read(authored.MovementSoftCap2Taper, fallback.MovementSoftCap2Taper, "MovementSoftCap2Taper", 0f, 1f, warnings),
			AgilityCooldownHastePerPoint = Read(authored.AgilityCooldownHastePerPoint, fallback.AgilityCooldownHastePerPoint, "AgilityCooldownHastePerPoint", 0f, LargeBound, warnings),
			CooldownHasteDivisor = Read(authored.CooldownHasteDivisor, fallback.CooldownHasteDivisor, "CooldownHasteDivisor", TinyPositive, LargeBound, warnings),
			MaxCooldownReduction = Read(authored.MaxCooldownReduction, fallback.MaxCooldownReduction, "MaxCooldownReduction", 0f, 1f, warnings),

			VitalityHpPerPoint = Read(authored.VitalityHpPerPoint, fallback.VitalityHpPerPoint, "VitalityHpPerPoint", 0f, LargeBound, warnings),
			VitalityManaPerPoint = Read(authored.VitalityManaPerPoint, fallback.VitalityManaPerPoint, "VitalityManaPerPoint", 0f, LargeBound, warnings),
			VitalityLifeStealK = Read(authored.VitalityLifeStealK, fallback.VitalityLifeStealK, "VitalityLifeStealK", TinyPositive, LargeBound, warnings),
			MaxLifeSteal = Read(authored.MaxLifeSteal, fallback.MaxLifeSteal, "MaxLifeSteal", 0f, 1f, warnings),

			IntelligenceSpellAmpPerPoint = Read(authored.IntelligenceSpellAmpPerPoint, fallback.IntelligenceSpellAmpPerPoint, "IntelligenceSpellAmpPerPoint", 0f, LargeBound, warnings),
			IntelligenceSpellAmpExponent = Read(authored.IntelligenceSpellAmpExponent, fallback.IntelligenceSpellAmpExponent, "IntelligenceSpellAmpExponent", TinyPositive, 10f, warnings),
			IntelligenceManaRegenPerPoint = Read(authored.IntelligenceManaRegenPerPoint, fallback.IntelligenceManaRegenPerPoint, "IntelligenceManaRegenPerPoint", 0f, LargeBound, warnings),
			IntelligenceMagicResistancePerPoint = Read(authored.IntelligenceMagicResistancePerPoint, fallback.IntelligenceMagicResistancePerPoint, "IntelligenceMagicResistancePerPoint", 0f, LargeBound, warnings),

			WisdomMagicPenetrationK = Read(authored.WisdomMagicPenetrationK, fallback.WisdomMagicPenetrationK, "WisdomMagicPenetrationK", TinyPositive, LargeBound, warnings),
			MaxPercentMagicPenetration = Read(authored.MaxPercentMagicPenetration, fallback.MaxPercentMagicPenetration, "MaxPercentMagicPenetration", 0f, 1f, warnings),
			WisdomHealingAmpPerPoint = Read(authored.WisdomHealingAmpPerPoint, fallback.WisdomHealingAmpPerPoint, "WisdomHealingAmpPerPoint", 0f, LargeBound, warnings),
			WisdomHpRegenPerPoint = Read(authored.WisdomHpRegenPerPoint, fallback.WisdomHpRegenPerPoint, "WisdomHpRegenPerPoint", 0f, LargeBound, warnings),
			WisdomCritMultiplierPerPoint = Read(authored.WisdomCritMultiplierPerPoint, fallback.WisdomCritMultiplierPerPoint, "WisdomCritMultiplierPerPoint", 0f, LargeBound, warnings),

			FortuneCritChanceK = Read(authored.FortuneCritChanceK, fallback.FortuneCritChanceK, "FortuneCritChanceK", TinyPositive, LargeBound, warnings),
			MaxCritChance = Read(authored.MaxCritChance, fallback.MaxCritChance, "MaxCritChance", 0f, 1f, warnings),
			FortuneEvasionK = Read(authored.FortuneEvasionK, fallback.FortuneEvasionK, "FortuneEvasionK", TinyPositive, LargeBound, warnings),
			MaxEvasion = Read(authored.MaxEvasion, fallback.MaxEvasion, "MaxEvasion", 0f, 1f, warnings),
			FortuneProcRateK = Read(authored.FortuneProcRateK, fallback.FortuneProcRateK, "FortuneProcRateK", TinyPositive, LargeBound, warnings),
			MaxProcRateBonus = Read(authored.MaxProcRateBonus, fallback.MaxProcRateBonus, "MaxProcRateBonus", 0f, 1f, warnings),
			FortuneBountyPerPoint = Read(authored.FortuneBountyPerPoint, fallback.FortuneBountyPerPoint, "FortuneBountyPerPoint", 0f, LargeBound, warnings),

			MaxAttackSpeed = Read(authored.MaxAttackSpeed, fallback.MaxAttackSpeed, "MaxAttackSpeed", TinyPositive, LargeBound, warnings),
			MinAttackSpeed = Read(authored.MinAttackSpeed, fallback.MinAttackSpeed, "MinAttackSpeed", TinyPositive, LargeBound, warnings),
			MinAttackInterval = Read(authored.MinAttackInterval, fallback.MinAttackInterval, "MinAttackInterval", TinyPositive, LargeBound, warnings),
			BaseCastPoint = Read(authored.BaseCastPoint, fallback.BaseCastPoint, "BaseCastPoint", 0f, LargeBound, warnings),
			BaseCritMultiplier = Read(authored.BaseCritMultiplier, fallback.BaseCritMultiplier, "BaseCritMultiplier", 1f, LargeBound, warnings)
		};

		if (t.MinAttackSpeed > t.MaxAttackSpeed)
		{
			warnings?.Add($"[CombatTuning] MinAttackSpeed ({t.MinAttackSpeed}) exceeds MaxAttackSpeed ({t.MaxAttackSpeed}); using the engine defaults for both.");
			t.MinAttackSpeed = fallback.MinAttackSpeed;
			t.MaxAttackSpeed = fallback.MaxAttackSpeed;
		}

		if (t.MovementSoftCap1 > t.MovementSoftCap2)
		{
			warnings?.Add($"[CombatTuning] MovementSoftCap1 ({t.MovementSoftCap1}) exceeds MovementSoftCap2 ({t.MovementSoftCap2}); using the engine defaults for both.");
			t.MovementSoftCap1 = fallback.MovementSoftCap1;
			t.MovementSoftCap2 = fallback.MovementSoftCap2;
		}

		simulation.ActiveCombatTuning = t;
		simulation.ActiveCombatConfig = BuildConfig(authored, warnings);
	}

	private static CombatConfig BuildConfig(CombatTuningMetadata authored, List<string>? warnings)
	{
		var defaults = new CombatConfig();
		return new CombatConfig
		{
			ResistTuningConstantK = Read(authored.ResistTuningConstantK, defaults.ResistTuningConstantK, "ResistTuningConstantK", TinyPositive, LargeBound, warnings),
			MaxResistMitigation = Read(authored.MaxResistMitigation, defaults.MaxResistMitigation, "MaxResistMitigation", 0f, 1f, warnings),
			MinDamageFloor = Read(authored.MinDamageFloor, defaults.MinDamageFloor, "MinDamageFloor", 0f, LargeBound, warnings)
		};
	}

	private static float Read(float? authored, float fallback, string field, float min, float max, List<string>? warnings)
	{
		if (authored == null)
		{
			return fallback;
		}

		float value = authored.Value;
		if (!float.IsFinite(value) || value < min || value > max)
		{
			warnings?.Add($"[CombatTuning] Ignoring {field} = {value}; expected {min}..{max}. Using {fallback}.");
			return fallback;
		}

		return value;
	}
}
