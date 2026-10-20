namespace Realm.Ecs.Services;

/// <summary>
///     Encapsulates the result of a 3-stage combat resolution calculation.
/// </summary>
public struct DamageResult
{
	public float RawDamage;
	public float EffectiveFlatArmor;
	public float EffectiveRatedArmor;
	public float MitigatedDamage;
	public float FinalDamage;
	public byte IsCriticalByte;

	public bool IsCritical
	{
		get => IsCriticalByte != 0;
		set => IsCriticalByte = value ? (byte)1 : (byte)0;
	}
}