namespace Realm.Ecs.Components.Combat;

/// <summary>
///     Defines an entity's primary attack capabilities.
/// </summary>
internal record struct Attack(
	float Damage,
	float Range,
	float Cooldown,
	float CurrentCooldown = 0,
	float DamageVariance = 0f,
	string DamageType = "normal",
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
	bool FriendlyFire = false)
{
	public readonly Weapon ToWeapon()
	{
		return new Weapon(
			BaseDamage: Damage,
			DamageVariance: DamageVariance,
			DamageType: DamageType,
			MinRange: 0f,
			MaxRange: Range,
			AttackInterval: Cooldown,
			CurrentCooldown: CurrentCooldown,
			FlatArmorPenetration: FlatArmorPenetration,
			PercentArmorPenetration: PercentArmorPenetration,
			CritChance: CritChance,
			CritMultiplier: CritMultiplier,
			SplashType: SplashType,
			SplashInnerRadius: SplashInnerRadius,
			SplashMediumRadius: SplashMediumRadius,
			SplashOuterRadius: SplashOuterRadius,
			SplashInnerRatio: SplashInnerRatio,
			SplashMediumRatio: SplashMediumRatio,
			SplashOuterRatio: SplashOuterRatio,
			FriendlyFire: FriendlyFire
		);
	}
}
