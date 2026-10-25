using System;
using Godot;
using Realm.Ecs.Services;
using Realm.Shared.Metadata;

namespace Realm.Client.Services;

/// <summary>
///     Turns a map's authored <see cref="CombatTuningMetadata" /> into the runtime
///     <see cref="CombatTuning" /> and <see cref="CombatConfig" /> the stat and damage pipelines
///     read. Applied on every map load, so a map that authors nothing is reset to the engine
///     defaults rather than inheriting whatever the previous map asked for.
/// </summary>
public static class MapCombatTuningService
{
	private const float TinyPositive = 0.0001f;
	private const float LargeBound = 100000f;

	/// <summary>
	///     Replaces the active tuning with the map's, falling back to the defaults for every field
	///     the map leaves out. Invalid values are rejected with a warning and take the default, so a
	///     typo in map data cannot divide by zero mid-match.
	/// </summary>
	public static void Apply(MapMetadata? metadata)
	{
		CombatTuningMetadata? authored = metadata?.CombatTuning;
		if (authored == null)
		{
			ECSContext.ActiveCombatTuning = CombatTuning.Default;
			ECSContext.ActiveCombatConfig = new CombatConfig();
			return;
		}

		var fallback = CombatTuning.Default;
		var t = new CombatTuning
		{
			StrengthAttackPerPoint = Read(authored.StrengthAttackPerPoint, fallback.StrengthAttackPerPoint, "StrengthAttackPerPoint", 0f, LargeBound),
			StrengthArmorPerPoint = Read(authored.StrengthArmorPerPoint, fallback.StrengthArmorPerPoint, "StrengthArmorPerPoint", 0f, LargeBound),
			StrengthTenacityK = Read(authored.StrengthTenacityK, fallback.StrengthTenacityK, "StrengthTenacityK", TinyPositive, LargeBound),
			MaxTenacity = Read(authored.MaxTenacity, fallback.MaxTenacity, "MaxTenacity", 0f, 1f),

			AgilityHastePerPoint = Read(authored.AgilityHastePerPoint, fallback.AgilityHastePerPoint, "AgilityHastePerPoint", 0f, LargeBound),
			HastePercentDivisor = Read(authored.HastePercentDivisor, fallback.HastePercentDivisor, "HastePercentDivisor", TinyPositive, LargeBound),
			AgilityMovementPerPoint = Read(authored.AgilityMovementPerPoint, fallback.AgilityMovementPerPoint, "AgilityMovementPerPoint", 0f, LargeBound),
			MovementSoftCap1 = Read(authored.MovementSoftCap1, fallback.MovementSoftCap1, "MovementSoftCap1", 0f, LargeBound),
			MovementSoftCap2 = Read(authored.MovementSoftCap2, fallback.MovementSoftCap2, "MovementSoftCap2", 0f, LargeBound),
			MovementSoftCap1Taper = Read(authored.MovementSoftCap1Taper, fallback.MovementSoftCap1Taper, "MovementSoftCap1Taper", 0f, 1f),
			MovementSoftCap2Taper = Read(authored.MovementSoftCap2Taper, fallback.MovementSoftCap2Taper, "MovementSoftCap2Taper", 0f, 1f),
			AgilityCooldownHastePerPoint = Read(authored.AgilityCooldownHastePerPoint, fallback.AgilityCooldownHastePerPoint, "AgilityCooldownHastePerPoint", 0f, LargeBound),
			CooldownHasteDivisor = Read(authored.CooldownHasteDivisor, fallback.CooldownHasteDivisor, "CooldownHasteDivisor", TinyPositive, LargeBound),
			MaxCooldownReduction = Read(authored.MaxCooldownReduction, fallback.MaxCooldownReduction, "MaxCooldownReduction", 0f, 1f),

			VitalityHpPerPoint = Read(authored.VitalityHpPerPoint, fallback.VitalityHpPerPoint, "VitalityHpPerPoint", 0f, LargeBound),
			VitalityManaPerPoint = Read(authored.VitalityManaPerPoint, fallback.VitalityManaPerPoint, "VitalityManaPerPoint", 0f, LargeBound),
			VitalityLifeStealK = Read(authored.VitalityLifeStealK, fallback.VitalityLifeStealK, "VitalityLifeStealK", TinyPositive, LargeBound),
			MaxLifeSteal = Read(authored.MaxLifeSteal, fallback.MaxLifeSteal, "MaxLifeSteal", 0f, 1f),

			IntelligenceSpellAmpPerPoint = Read(authored.IntelligenceSpellAmpPerPoint, fallback.IntelligenceSpellAmpPerPoint, "IntelligenceSpellAmpPerPoint", 0f, LargeBound),
			IntelligenceSpellAmpExponent = Read(authored.IntelligenceSpellAmpExponent, fallback.IntelligenceSpellAmpExponent, "IntelligenceSpellAmpExponent", TinyPositive, 10f),
			IntelligenceManaRegenPerPoint = Read(authored.IntelligenceManaRegenPerPoint, fallback.IntelligenceManaRegenPerPoint, "IntelligenceManaRegenPerPoint", 0f, LargeBound),
			IntelligenceMagicResistK = Read(authored.IntelligenceMagicResistK, fallback.IntelligenceMagicResistK, "IntelligenceMagicResistK", TinyPositive, LargeBound),
			MaxMagicResist = Read(authored.MaxMagicResist, fallback.MaxMagicResist, "MaxMagicResist", 0f, 1f),

			WisdomMagicPenetrationK = Read(authored.WisdomMagicPenetrationK, fallback.WisdomMagicPenetrationK, "WisdomMagicPenetrationK", TinyPositive, LargeBound),
			MaxMagicPenetration = Read(authored.MaxMagicPenetration, fallback.MaxMagicPenetration, "MaxMagicPenetration", 0f, 1f),
			WisdomHealingAmpPerPoint = Read(authored.WisdomHealingAmpPerPoint, fallback.WisdomHealingAmpPerPoint, "WisdomHealingAmpPerPoint", 0f, LargeBound),
			WisdomHpRegenPerPoint = Read(authored.WisdomHpRegenPerPoint, fallback.WisdomHpRegenPerPoint, "WisdomHpRegenPerPoint", 0f, LargeBound),
			WisdomCritMultiplierPerPoint = Read(authored.WisdomCritMultiplierPerPoint, fallback.WisdomCritMultiplierPerPoint, "WisdomCritMultiplierPerPoint", 0f, LargeBound),

			FortuneCritChanceK = Read(authored.FortuneCritChanceK, fallback.FortuneCritChanceK, "FortuneCritChanceK", TinyPositive, LargeBound),
			MaxCritChance = Read(authored.MaxCritChance, fallback.MaxCritChance, "MaxCritChance", 0f, 1f),
			FortuneEvasionK = Read(authored.FortuneEvasionK, fallback.FortuneEvasionK, "FortuneEvasionK", TinyPositive, LargeBound),
			MaxEvasion = Read(authored.MaxEvasion, fallback.MaxEvasion, "MaxEvasion", 0f, 1f),
			FortuneProcRateK = Read(authored.FortuneProcRateK, fallback.FortuneProcRateK, "FortuneProcRateK", TinyPositive, LargeBound),
			MaxProcRateBonus = Read(authored.MaxProcRateBonus, fallback.MaxProcRateBonus, "MaxProcRateBonus", 0f, 1f),
			FortuneBountyPerPoint = Read(authored.FortuneBountyPerPoint, fallback.FortuneBountyPerPoint, "FortuneBountyPerPoint", 0f, LargeBound),

			MaxAttackSpeed = Read(authored.MaxAttackSpeed, fallback.MaxAttackSpeed, "MaxAttackSpeed", TinyPositive, LargeBound),
			MinAttackSpeed = Read(authored.MinAttackSpeed, fallback.MinAttackSpeed, "MinAttackSpeed", TinyPositive, LargeBound),
			MinAttackInterval = Read(authored.MinAttackInterval, fallback.MinAttackInterval, "MinAttackInterval", TinyPositive, LargeBound),
			BaseCastPoint = Read(authored.BaseCastPoint, fallback.BaseCastPoint, "BaseCastPoint", 0f, LargeBound),
			BaseCritMultiplier = Read(authored.BaseCritMultiplier, fallback.BaseCritMultiplier, "BaseCritMultiplier", 1f, LargeBound)
		};

		if (t.MinAttackSpeed > t.MaxAttackSpeed)
		{
			GD.PushWarning($"[CombatTuning] MinAttackSpeed ({t.MinAttackSpeed}) exceeds MaxAttackSpeed ({t.MaxAttackSpeed}); using the engine defaults for both.");
			t.MinAttackSpeed = fallback.MinAttackSpeed;
			t.MaxAttackSpeed = fallback.MaxAttackSpeed;
		}

		if (t.MovementSoftCap1 > t.MovementSoftCap2)
		{
			GD.PushWarning($"[CombatTuning] MovementSoftCap1 ({t.MovementSoftCap1}) exceeds MovementSoftCap2 ({t.MovementSoftCap2}); using the engine defaults for both.");
			t.MovementSoftCap1 = fallback.MovementSoftCap1;
			t.MovementSoftCap2 = fallback.MovementSoftCap2;
		}

		ECSContext.ActiveCombatTuning = t;
		ECSContext.ActiveCombatConfig = BuildConfig(authored);
	}

	private static CombatConfig BuildConfig(CombatTuningMetadata authored)
	{
		var defaults = new CombatConfig();
		var config = new CombatConfig
		{
			TuningConstantK = Read(authored.ArmorTuningConstantK, defaults.TuningConstantK, "ArmorTuningConstantK", TinyPositive, LargeBound),
			MinDamageFloor = Read(authored.MinDamageFloor, defaults.MinDamageFloor, "MinDamageFloor", 0f, LargeBound),
			UndefinedPairMultiplier = Read(authored.UndefinedPairMultiplier, defaults.UndefinedPairMultiplier, "UndefinedPairMultiplier", 0f, LargeBound)
		};

		if (authored.DamageMatrix == null)
		{
			return config;
		}

		foreach (DamageMatrixEntryMetadata entry in authored.DamageMatrix)
		{
			if (string.IsNullOrWhiteSpace(entry.DamageType) || string.IsNullOrWhiteSpace(entry.ArmorType))
			{
				GD.PushWarning("[CombatTuning] Skipping a DamageMatrix entry with a missing DamageType or ArmorType.");
				continue;
			}

			if (entry.Multiplier == null || !float.IsFinite(entry.Multiplier.Value) || entry.Multiplier.Value < 0f)
			{
				GD.PushWarning($"[CombatTuning] Skipping DamageMatrix entry {entry.DamageType} vs {entry.ArmorType}: multiplier must be a finite number >= 0.");
				continue;
			}

			config.SetMultiplier(entry.DamageType, entry.ArmorType, entry.Multiplier.Value);
		}

		return config;
	}

	private static float Read(float? authored, float fallback, string field, float min, float max)
	{
		if (authored == null)
		{
			return fallback;
		}

		float value = authored.Value;
		if (!float.IsFinite(value) || value < min || value > max)
		{
			GD.PushWarning($"[CombatTuning] Ignoring {field} = {value}; expected {min}..{max}. Using {fallback}.");
			return fallback;
		}

		return value;
	}
}
