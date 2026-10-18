using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Combat;

/// <summary>
/// Defines combat attack parameters including damage, range, cooldowns, and area-of-effect splash.
/// </summary>
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
	public byte FriendlyFireByte;

	public bool FriendlyFire
	{
		get => FriendlyFireByte != 0;
		set => FriendlyFireByte = value ? (byte)1 : (byte)0;
	}

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
		FriendlyFireByte = friendlyFire ? (byte)1 : (byte)0;
	}

	public string DamageType
	{
		get => StringRegistry.Get(DamageTypeId);
		set => DamageTypeId = StringRegistry.GetOrAdd(value);
	}
}
