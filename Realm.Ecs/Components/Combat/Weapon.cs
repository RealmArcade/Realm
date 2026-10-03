namespace Realm.Ecs.Components.Combat;

/// <summary>
///     Defines delivery style options for weapons.
/// </summary>
public enum DeliveryStyle
{
	Instant,
	HomingProjectile
}

/// <summary>
///     Defines splash area falloff options for weapons.
/// </summary>
public enum SplashType
{
	None,
	RadialStep,
	RadialLinear
}

/// <summary>
///     Defines a weapon's attributes including damage, cadence, penetration, delivery, and splash options.
/// </summary>
internal record struct Weapon(
	float BaseDamage,
	float DamageVariance = 0f,
	string DamageType = "normal",
	float MinRange = 0f,
	float MaxRange = 10f,
	float AttackInterval = 1.5f,
	float AttackWindup = 0f,
	float AttackBackswing = 0f,
	int BurstCount = 1,
	float BurstInterval = 0f,
	DeliveryStyle DeliveryStyle = DeliveryStyle.Instant,
	float ProjectileSpeed = 20f,
	float FlatArmorPenetration = 0f,
	float PercentArmorPenetration = 0f,
	float CritChance = 0f,
	float CritMultiplier = 1f,
	SplashType SplashType = SplashType.None,
	float SplashInnerRadius = 0f,
	float SplashMediumRadius = 0f,
	float SplashOuterRadius = 0f,
	float SplashInnerRatio = 1f,
	float SplashMediumRatio = 0.5f,
	float SplashOuterRatio = 0.25f,
	bool FriendlyFire = false,
	float CurrentCooldown = 0f,
	int CurrentBurstRemaining = 0,
	float CurrentBurstTimer = 0f,
	float WindupTimer = 0f,
	bool IsWindingUp = false);
