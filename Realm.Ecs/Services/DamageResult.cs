namespace Realm.Ecs.Services;

/// <summary>
///     One damage application, with the number every stage produced. The stages are ordered and
///     documented in <see cref="CombatResolver" />: raw, effective resistance, mitigation, floor.
/// </summary>
public struct DamageResult
{
	public float RawDamage;

	/// <summary>Resistance points actually applied, after penetration. Zero for pure damage.</summary>
	public float EffectiveResistance;

	/// <summary>Fraction of damage removed by resistance, in [0, MaxResistMitigation].</summary>
	public float Mitigation;

	/// <summary>Damage after resistance, before the minimum-damage floor.</summary>
	public float MitigatedDamage;

	/// <summary>Damage actually applied.</summary>
	public float FinalDamage;

	public byte IsCriticalByte;

	public bool IsCritical
	{
		get => IsCriticalByte != 0;
		set => IsCriticalByte = value ? (byte)1 : (byte)0;
	}
}
