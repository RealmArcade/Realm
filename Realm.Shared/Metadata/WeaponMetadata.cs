using Realm.Shared.Terrain;

namespace Realm.Shared.Metadata;

public class WeaponMetadata
{
	public string TemplateID { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public float Damage { get; set; }
	public float Range { get; set; }
	public float AttackCooldown { get; set; }
	public string? AttackType { get; set; }
	public float ProjectileSpeed { get; set; }
	public string? VisualEffect { get; set; }
	public string? AttackSound { get; set; }
	public string? ProjectileModelPath { get; set; }
	public string? ImpactVisualEffect { get; set; }
	public string? ImpactSound { get; set; }

	public string? TrajectoryType { get; set; } = "Parabolic";
	public float BoomerangReturnDelay { get; set; } = 0.2f;
	public float OrbitRadius { get; set; } = 1.0f;
	public float OrbitSpeed { get; set; } = 10.0f;

	public float ArcHeight { get; set; }
	public float HomingWeight { get; set; }
	public float TurnRateLimit { get; set; }
	public string? EaseCurve { get; set; }
	public string? SpeedCurve { get; set; }
	public float Acceleration { get; set; }
	public float MaxLifetime { get; set; }
	public float FailsafeRange { get; set; }
	public string? ScaleCurve { get; set; }
	public Vector3Data? TumbleAngularVelocity { get; set; }
	public bool OrientToTrajectory { get; set; } = true;
	public string ForwardAxisPreset { get; set; } = "-Z";
	public Vector3Data? MeshRotationOffset { get; set; }
	public Vector3Data? MeshTranslationOffset { get; set; }
	public Vector3Data? MeshScaleOffset { get; set; }
	public float SpiralRadius { get; set; }
	public float SpiralFrequency { get; set; }
	public float ZigzagAmplitude { get; set; }
	public float ZigzagFrequency { get; set; }
	public int MaxBounces { get; set; }
	public int PierceCount { get; set; }

	public string? ShaderEffectType { get; set; }
	public string EmissionMaskSource { get; set; } = "noise";
	public string? BaseColor { get; set; }
	public string? EmissionColor { get; set; }
	public float EmissionEnergy { get; set; } = 4f;
	public float FresnelPower { get; set; } = 3f;
	public string? FresnelColor { get; set; }
	public float FresnelFactor { get; set; } = 1.5f;
	public float NoiseScale { get; set; } = 3f;
	public string? NoiseTexture { get; set; }
	public Vector2Data? UvScrollSpeed1 { get; set; }
	public Vector2Data? UvScrollSpeed2 { get; set; }
	public float ThresholdCutoff { get; set; } = 0.5f;
	public float ThresholdSmoothness { get; set; } = 0.1f;

	public bool PointLightEnabled { get; set; }
	public string? PointLightColor { get; set; }
	public float PointLightIntensity { get; set; } = 2.0f;
	public float PointLightRange { get; set; } = 6.0f;

	public string? RibbonTexture { get; set; }
	public string? RibbonColor { get; set; }
	public float RibbonWidth { get; set; } = 0.4f;
	public float RibbonLifetime { get; set; } = 0.5f;
	public bool RibbonTaper { get; set; } = true;
	public bool RibbonAdditive { get; set; } = true;
	public float RibbonScrollSpeed { get; set; }
	public Vector3Data? TrailOffset { get; set; }
}