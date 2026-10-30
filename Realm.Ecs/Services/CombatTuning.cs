namespace Realm.Ecs.Services;

/// <summary>
///     Every coefficient, cap, floor and conversion constant the stat pipeline uses. One instance
///     is active per match; a map overrides individual values through its metadata's
///     <c>CombatTuning</c> block, and anything the map leaves out keeps the default below, which
///     is the behaviour the engine had when these were compile-time constants.
/// </summary>
public sealed class CombatTuning
{
	/// <summary>The defaults every field falls back to. Never mutated.</summary>
	public static CombatTuning Default { get; } = new();

	public CombatTuning Clone() => (CombatTuning)MemberwiseClone();

	// --- Strength: attack damage, tenacity, armor points ---
	public float StrengthAttackPerPoint { get; set; } = 1.5f;
	public float StrengthArmorPerPoint { get; set; } = 0.15f;
	public float StrengthTenacityK { get; set; } = 100.0f;
	public float MaxTenacity { get; set; } = 0.75f;

	// --- Agility: attack speed, movement speed, cooldown reduction ---
	public float AgilityHastePerPoint { get; set; } = 1.0f;
	public float HastePercentDivisor { get; set; } = 100.0f;
	public float AgilityMovementPerPoint { get; set; } = 0.4f;
	public float MovementSoftCap1 { get; set; } = 450.0f;
	public float MovementSoftCap2 { get; set; } = 550.0f;
	public float MovementSoftCap1Taper { get; set; } = 0.8f;
	public float MovementSoftCap2Taper { get; set; } = 0.5f;
	public float AgilityCooldownHastePerPoint { get; set; } = 1.0f;
	public float CooldownHasteDivisor { get; set; } = 100.0f;
	public float MaxCooldownReduction { get; set; } = 0.60f;

	// --- Vitality: health, mana, life steal ---
	public float VitalityHpPerPoint { get; set; } = 18.0f;
	public float VitalityManaPerPoint { get; set; } = 8.0f;
	public float VitalityLifeStealK { get; set; } = 150.0f;
	public float MaxLifeSteal { get; set; } = 0.40f;

	// --- Intelligence: ability damage, mana regeneration, magic resistance points ---
	public float IntelligenceSpellAmpPerPoint { get; set; } = 0.0075f;
	public float IntelligenceSpellAmpExponent { get; set; } = 1.05f;
	public float IntelligenceManaRegenPerPoint { get; set; } = 0.05f;

	/// <summary>
	///     Magic resistance points granted per point of Intelligence. The points are turned into a
	///     damage reduction fraction by the resolver's curve, exactly like armor.
	/// </summary>
	public float IntelligenceMagicResistancePerPoint { get; set; } = 0.15f;

	// --- Wisdom: magic penetration, healing and regeneration, critical damage ---
	public float WisdomMagicPenetrationK { get; set; } = 120.0f;
	public float MaxPercentMagicPenetration { get; set; } = 0.50f;
	public float WisdomHealingAmpPerPoint { get; set; } = 0.008f;
	public float WisdomHpRegenPerPoint { get; set; } = 0.04f;
	public float WisdomCritMultiplierPerPoint { get; set; } = 0.006f;

	// --- Fortune: critical chance, evasion, proc rates, bounty ---
	public float FortuneCritChanceK { get; set; } = 100.0f;
	public float MaxCritChance { get; set; } = 0.80f;
	public float FortuneEvasionK { get; set; } = 150.0f;
	public float MaxEvasion { get; set; } = 0.50f;
	public float FortuneProcRateK { get; set; } = 200.0f;
	public float MaxProcRateBonus { get; set; } = 0.50f;
	public float FortuneBountyPerPoint { get; set; } = 0.005f;

	// --- Attack timing and unit defaults ---
	public float MaxAttackSpeed { get; set; } = 5.0f;
	public float MinAttackSpeed { get; set; } = 0.1f;
	public float MinAttackInterval { get; set; } = 0.01f;
	public float BaseCastPoint { get; set; } = 0.3f;
	public float BaseCritMultiplier { get; set; } = 1.5f;
}
