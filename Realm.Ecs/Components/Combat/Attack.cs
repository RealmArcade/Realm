using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Combat;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Attack
{
	public float Damage;
	public float Range;
	public float Cooldown;
	public float CurrentCooldown;
	public float DamageVariance;
	public int DamageTypeId;
	public float FlatArmorPenetration;
	public float PercentArmorPenetration;
	public float CritChance;
	public float CritMultiplier;
	public SplashType SplashType;
	public float SplashInnerRadius;
	public float SplashMediumRadius;
	public float SplashOuterRadius;
	public float SplashInnerRatio;
	public float SplashMediumRatio;
	public float SplashOuterRatio;
	public bool FriendlyFire;

	public Attack(
		float damage,
		float range,
		float cooldown,
		float currentCooldown = 0,
		float damageVariance = 0f,
		string damageType = "normal",
		float flatArmorPenetration = 0f,
		float percentArmorPenetration = 0f,
		float critChance = 0f,
		float critMultiplier = 1f,
		SplashType splashType = SplashType.None,
		float splashInnerRadius = 0f,
		float splashMediumRadius = 0f,
		float splashOuterRadius = 0f,
		float splashInnerRatio = 1f,
		float splashMediumRatio = 0.5f,
		float splashOuterRatio = 0.25f,
		bool friendlyFire = false)
	{
		Damage = damage;
		Range = range;
		Cooldown = cooldown;
		CurrentCooldown = currentCooldown;
		DamageVariance = damageVariance;
		DamageTypeId = StringRegistry.GetOrAdd(damageType);
		FlatArmorPenetration = flatArmorPenetration;
		PercentArmorPenetration = percentArmorPenetration;
		CritChance = critChance;
		CritMultiplier = critMultiplier;
		SplashType = splashType;
		SplashInnerRadius = splashInnerRadius;
		SplashMediumRadius = splashMediumRadius;
		SplashOuterRadius = splashOuterRadius;
		SplashInnerRatio = splashInnerRatio;
		SplashMediumRatio = splashMediumRatio;
		SplashOuterRatio = splashOuterRatio;
		FriendlyFire = friendlyFire;
	}

	public string DamageType
	{
		get => StringRegistry.Get(DamageTypeId);
		set => DamageTypeId = StringRegistry.GetOrAdd(value);
	}

	public readonly Weapon ToWeapon()
	{
		return new Weapon(
			baseDamage: Damage,
			damageVariance: DamageVariance,
			damageType: DamageType,
			minRange: 0f,
			maxRange: Range,
			attackInterval: Cooldown,
			currentCooldown: CurrentCooldown,
			flatArmorPenetration: FlatArmorPenetration,
			percentArmorPenetration: PercentArmorPenetration,
			critChance: CritChance,
			critMultiplier: CritMultiplier,
			splashType: SplashType,
			splashInnerRadius: SplashInnerRadius,
			splashMediumRadius: SplashMediumRadius,
			splashOuterRadius: SplashOuterRadius,
			splashInnerRatio: SplashInnerRatio,
			splashMediumRatio: SplashMediumRatio,
			splashOuterRatio: SplashOuterRatio,
			friendlyFire: FriendlyFire
		);
	}
}
