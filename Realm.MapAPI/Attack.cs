using System.Runtime.InteropServices;

namespace Realm.MapAPI;

/// <summary>
/// Defines combat attack parameters including damage, range, cooldowns, and area-of-effect splash.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Attack
{
	/// <summary>Base attack damage dealt.</summary>
	public float Damage;
	/// <summary>Maximum attack range distance in world units.</summary>
	public float Range;
	/// <summary>Attack interval or cooldown duration in seconds.</summary>
	public float Cooldown;
	/// <summary>Current remaining attack cooldown in seconds.</summary>
	public float CurrentCooldown;
	/// <summary>Damage variance percentage or range.</summary>
	public float DamageVariance;
	/// <summary>String registry identifier for the damage category.</summary>
	public int DamageTypeId;
	/// <summary>Flat armor penetration amount.</summary>
	public float FlatArmorPenetration;
	/// <summary>Percent armor penetration ratio (0.0 to 1.0).</summary>
	public float PercentArmorPenetration;
	/// <summary>Critical strike probability ratio (0.0 to 1.0).</summary>
	public float CritChance;
	/// <summary>Critical strike damage multiplier.</summary>
	public float CritMultiplier;
	/// <summary>Area-of-effect splash falloff style.</summary>
	public SplashType SplashType;
	/// <summary>Inner splash radius distance in world units.</summary>
	public float SplashInnerRadius;
	/// <summary>Medium splash radius distance in world units.</summary>
	public float SplashMediumRadius;
	/// <summary>Outer splash radius distance in world units.</summary>
	public float SplashOuterRadius;
	/// <summary>Damage multiplier applied within inner splash radius.</summary>
	public float SplashInnerRatio;
	/// <summary>Damage multiplier applied within medium splash radius.</summary>
	public float SplashMediumRatio;
	/// <summary>Damage multiplier applied within outer splash radius.</summary>
	public float SplashOuterRatio;
	/// <summary>Backing byte indicating whether splash damage hurts allied units (1 if true, 0 if false).</summary>
	public byte FriendlyFireByte;

	/// <summary>
	/// Gets or sets a value indicating whether splash damage affects friendly units.
	/// </summary>
	public bool FriendlyFire
	{
		get => FriendlyFireByte != 0;
		set => FriendlyFireByte = value ? (byte)1 : (byte)0;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="Attack"/> struct.
	/// </summary>
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

	/// <summary>
	/// Gets or sets the damage category name.
	/// </summary>
	public string DamageType
	{
		get => StringRegistry.Get(DamageTypeId);
		set => DamageTypeId = StringRegistry.GetOrAdd(value);
	}
}
