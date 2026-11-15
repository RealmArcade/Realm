using Realm.Shared.Serialization;
using Realm.Shared.Terrain;

namespace Realm.Shared.Metadata;

[SchemaDescription("One weapon a unit attacks with, and the projectile it flies: its damage, its reach, its flight path, the effect that trails it and how it looks when it lands.")]
public class WeaponMetadata
{
	[SchemaDescription("The identifier unit templates name this weapon by.")]
	[SchemaSensitive]
	public string TemplateID { get; set; } = string.Empty;

	[SchemaDescription("The name the weapon shows when its damage is broken down for a player.")]
	public string Name { get; set; } = string.Empty;

	[SchemaDescription("Damage one attack with this weapon deals before mitigation.")]
	public float Damage { get; set; }

	[SchemaDescription("How far this weapon reaches, measured from the attacker's edge to the target's edge.")]
	[SchemaUnit("m")]
	public float Range { get; set; }

	[SchemaDescription("How long the attacker waits between attacks with this weapon.")]
	[SchemaUnit("s")]
	public float AttackCooldown { get; set; }

	[SchemaDescription("Whether the weapon strikes immediately or flies something at the target.")]
	public string? AttackType { get; set; }

	[SchemaDescription("Whether the weapon's damage is mitigated as physical or as magical.")]
	public string DamageType { get; set; } = "physical";

	[SchemaDescription("How fast the projectile travels. Zero hits its target immediately.")]
	[SchemaUnit("m/s")]
	public float ProjectileSpeed { get; set; }

	[SchemaDescription("The visual effect attached to the projectile or the swing.")]
	[SchemaReference("Vfx")]
	public string? VisualEffect { get; set; }

	[SchemaDescription("The sound played when the weapon attacks.")]
	public string? AttackSound { get; set; }

	[SchemaDescription("Path to the model the projectile flies, and the model a summoned projectile shows in the editor.")]
	[SchemaAsset(new[] { "rmesh" }, new[] { "Assets/models/items" })]
	public string? ProjectileModelPath { get; set; }

	[SchemaDescription("The visual effect played where the projectile lands.")]
	[SchemaReference("Vfx")]
	public string? ImpactVisualEffect { get; set; }

	[SchemaDescription("The sprite sheet effect sprayed where the projectile lands.")]
	[SchemaReference("Vfx")]
	public string? ImpactParticleEffect { get; set; }

	[SchemaDescription("The multiplier applied to the landing effect's size.")]
	public float ImpactParticleScale { get; set; } = 1.0f;

	[SchemaDescription("The sound played where the projectile lands.")]
	public string? ImpactSound { get; set; }

	[SchemaDescription("The path the projectile follows, such as a straight line, an arc, a boomerang or an orbit.")]
	public string? TrajectoryType { get; set; } = "Parabolic";

	[SchemaDescription("How long a boomerang projectile waits at its furthest point before it flies back.")]
	[SchemaUnit("s")]
	public float BoomerangReturnDelay { get; set; } = 0.2f;

	[SchemaDescription("How far from the attacker an orbiting projectile circles.")]
	[SchemaUnit("m")]
	public float OrbitRadius { get; set; } = 1.0f;

	[SchemaDescription("How fast an orbiting projectile circles its owner.")]
	public float OrbitSpeed { get; set; } = 10.0f;

	[SchemaDescription("How high an arcing projectile rises above the straight line between attacker and target.")]
	[SchemaUnit("m")]
	public float ArcHeight { get; set; }

	[SchemaDescription("How strongly a homing projectile steers after a target that moves. Zero flies the path it started on.")]
	public float HomingWeight { get; set; }

	[SchemaDescription("How sharply a homing projectile may turn.")]
	public float TurnRateLimit { get; set; }

	[SchemaDescription("The curve the projectile's progress along its path is eased with.")]
	public string? EaseCurve { get; set; }

	[SchemaDescription("The curve the projectile's speed is shaped with over its flight.")]
	public string? SpeedCurve { get; set; }

	[SchemaDescription("How quickly the projectile gains speed along its path.")]
	public float Acceleration { get; set; }

	[SchemaDescription("How long the projectile lives before it gives up and disappears.")]
	[SchemaUnit("s")]
	public float MaxLifetime { get; set; }

	[SchemaDescription("How far the projectile may travel before it gives up, which bounds a shot at a target that keeps running.")]
	[SchemaUnit("m")]
	public float FailsafeRange { get; set; }

	[SchemaDescription("The curve the projectile's size is shaped with over its flight.")]
	public string? ScaleCurve { get; set; }

	[SchemaDescription("How fast the projectile tumbles while it flies, as degrees per second around each axis.")]
	public Vector3Data? TumbleAngularVelocity { get; set; }

	[SchemaDescription("Whether the projectile model is turned to face the direction it is travelling.")]
	public bool OrientToTrajectory { get; set; } = true;

	[SchemaDescription("Which of the model's own axes points forward, so a authored mesh flies nose first.")]
	public string ForwardAxisPreset { get; set; } = "-Z";

	[SchemaDescription("A rotation added to the projectile model on top of the direction it is travelling.")]
	public Vector3Data? MeshRotationOffset { get; set; }

	[SchemaDescription("A translation added to the projectile model, which centres a mesh authored around another origin.")]
	public Vector3Data? MeshTranslationOffset { get; set; }

	[SchemaDescription("A size multiplier applied to the projectile model.")]
	public Vector3Data? MeshScaleOffset { get; set; }

	[SchemaDescription("How far a spiralling projectile swings away from its path.")]
	[SchemaUnit("m")]
	public float SpiralRadius { get; set; }

	[SchemaDescription("How many times a spiralling projectile winds around its path over the flight.")]
	public float SpiralFrequency { get; set; }

	[SchemaDescription("How far a zigzagging projectile swings away from its path.")]
	[SchemaUnit("m")]
	public float ZigzagAmplitude { get; set; }

	[SchemaDescription("How many times a zigzagging projectile crosses its path over the flight.")]
	public float ZigzagFrequency { get; set; }

	[SchemaDescription("How many times the projectile may bounce off a unit or the ground before it is spent.")]
	public int MaxBounces { get; set; }

	[SchemaDescription("How many further units the projectile passes through after its first hit.")]
	public int PierceCount { get; set; }

	[SchemaDescription("The shader the projectile is drawn with, such as a trail of noise or a pulsing glow.")]
	[SchemaDictionaryReference("gdshader")]
	public string? ShaderEffectType { get; set; }

	[SchemaDescription("Which channel of the projectile's material drives its emission, such as the noise texture or the base colour.")]
	public string EmissionMaskSource { get; set; } = "noise";

	[SchemaDescription("The base colour the projectile's material starts from.")]
	[SchemaControl(RealmSchemaControl.Color)]
	public string? BaseColor { get; set; }

	[SchemaDescription("The colour the projectile's emission glows with.")]
	[SchemaControl(RealmSchemaControl.Color)]
	public string? EmissionColor { get; set; }

	[SchemaDescription("How strongly the projectile's emission glows.")]
	public float EmissionEnergy { get; set; } = 4f;

	[SchemaDescription("How tightly the projectile's rim glow hugs its silhouette. Higher values keep the glow at the edges.")]
	public float FresnelPower { get; set; } = 3f;

	[SchemaDescription("The colour of the projectile's rim glow.")]
	[SchemaControl(RealmSchemaControl.Color)]
	public string? FresnelColor { get; set; }

	[SchemaDescription("How strong the projectile's rim glow is.")]
	public float FresnelFactor { get; set; } = 1.5f;

	[SchemaDescription("The size of the noise pattern the projectile's material samples.")]
	public float NoiseScale { get; set; } = 3f;

	[SchemaDescription("The noise texture the projectile's material samples.")]
	[SchemaDictionaryReference("noise_textures")]
	public string? NoiseTexture { get; set; }

	[SchemaDescription("How fast the projectile's first uv layer scrolls, which animates its surface without a sprite sheet.")]
	public Vector2Data? UvScrollSpeed1 { get; set; }

	[SchemaDescription("How fast the projectile's second uv layer scrolls, which the shader blends with the first.")]
	public Vector2Data? UvScrollSpeed2 { get; set; }

	[SchemaDescription("The noise value below which the projectile's material is cut away entirely.")]
	public float ThresholdCutoff { get; set; } = 0.5f;

	[SchemaDescription("How soft the edge of the cutoff is, so the projectile dissolves instead of ending in hard pixels.")]
	public float ThresholdSmoothness { get; set; } = 0.1f;

	[SchemaDescription("Whether the projectile carries a light that lights the ground it passes over.")]
	public bool PointLightEnabled { get; set; }

	[SchemaDescription("The colour of the light the projectile carries.")]
	[SchemaControl(RealmSchemaControl.Color)]
	public string? PointLightColor { get; set; }

	[SchemaDescription("How strongly the projectile's light lights its surroundings.")]
	public float PointLightIntensity { get; set; } = 2.0f;

	[SchemaDescription("How far the projectile's light reaches.")]
	[SchemaUnit("m")]
	public float PointLightRange { get; set; } = 6.0f;

	[SchemaDescription("The ribbon texture dragged behind the projectile.")]
	[SchemaDictionaryReference("ribbons")]
	public string? RibbonTexture { get; set; }

	[SchemaDescription("The colour multiplied over the projectile's ribbon.")]
	[SchemaControl(RealmSchemaControl.Color)]
	public string? RibbonColor { get; set; }

	[SchemaDescription("How wide the projectile's ribbon is where it leaves the projectile.")]
	[SchemaUnit("m")]
	public float RibbonWidth { get; set; } = 0.4f;

	[SchemaDescription("How long a piece of the projectile's ribbon lives before it fades.")]
	[SchemaUnit("s")]
	public float RibbonLifetime { get; set; } = 0.5f;

	[SchemaDescription("Whether the projectile's ribbon narrows to a point at its tail.")]
	public bool RibbonTaper { get; set; } = true;

	[SchemaDescription("Whether the projectile's ribbon adds its colour to what is behind it instead of blending over it.")]
	public bool RibbonAdditive { get; set; } = true;

	[SchemaDescription("How fast the projectile's ribbon texture scrolls along the trail.")]
	public float RibbonScrollSpeed { get; set; }

	[SchemaDescription("Where the ribbon leaves the projectile's model, relative to the projectile's own origin.")]
	public Vector3Data? TrailOffset { get; set; }

	[SchemaDescription("The map the weapon was imported from, kept so attribution survives a round trip through the editor.")]
	public string? Attribution_ImportedFromMap { get; set; }
}
