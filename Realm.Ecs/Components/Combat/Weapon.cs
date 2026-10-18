using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Combat;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Weapon
{
	public float BaseDamage;
	public float DamageVariance;
	public int DamageTypeId;
	public float MinRange;
	public float MaxRange;
	public float AttackInterval;
	public float AttackWindup;
	public float AttackBackswing;
	public int BurstCount;
	public float BurstInterval;
	public DeliveryStyle DeliveryStyle;
	public float ProjectileSpeed;
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
	public float CurrentCooldown;
	public int CurrentBurstRemaining;
	public float CurrentBurstTimer;
	public float WindupTimer;
	public bool IsWindingUp;

	public Weapon(
		float baseDamage,
		float damageVariance = 0f,
		string damageType = "normal",
		float minRange = 0f,
		float maxRange = 10f,
		float attackInterval = 1.5f,
		float attackWindup = 0f,
		float attackBackswing = 0f,
		int burstCount = 1,
		float burstInterval = 0f,
		DeliveryStyle deliveryStyle = DeliveryStyle.Instant,
		float projectileSpeed = 20f,
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
		bool friendlyFire = false,
		float currentCooldown = 0f,
		int currentBurstRemaining = 0,
		float currentBurstTimer = 0f,
		float windupTimer = 0f,
		bool isWindingUp = false)
	{
		BaseDamage = baseDamage;
		DamageVariance = damageVariance;
		DamageTypeId = StringRegistry.GetOrAdd(damageType);
		MinRange = minRange;
		MaxRange = maxRange;
		AttackInterval = attackInterval;
		AttackWindup = attackWindup;
		AttackBackswing = attackBackswing;
		BurstCount = burstCount;
		BurstInterval = burstInterval;
		DeliveryStyle = deliveryStyle;
		ProjectileSpeed = projectileSpeed;
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
		CurrentCooldown = currentCooldown;
		CurrentBurstRemaining = currentBurstRemaining;
		CurrentBurstTimer = currentBurstTimer;
		WindupTimer = windupTimer;
		IsWindingUp = isWindingUp;
	}

	public string DamageType
	{
		get => StringRegistry.Get(DamageTypeId);
		set => DamageTypeId = StringRegistry.GetOrAdd(value);
	}
}
