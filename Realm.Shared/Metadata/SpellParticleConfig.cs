using Realm.Shared.Terrain;

namespace Realm.Shared.Metadata;

public class SpellParticleConfig
{
	public string ParticleId { get; set; } = "particle_burst";
	public string Name { get; set; } = "Spell Particles";

	public int Amount { get; set; } = 32;
	public float Lifetime { get; set; } = 1.0f;
	public float Explosiveness { get; set; } = 0.0f;
	public float Randomness { get; set; } = 0.0f;
	public bool LocalCoords { get; set; } = false;

	public SpellParticleShape EmitterShape { get; set; } = SpellParticleShape.Sphere;
	public float SphereRadius { get; set; } = 0.5f;
	public Vector3Data BoxExtents { get; set; } = new Vector3Data(0.5f, 0.5f, 0.5f);
	public float RingRadius { get; set; } = 1.0f;
	public float RingInnerRadius { get; set; } = 0.8f;
	public float RingHeight { get; set; } = 0.1f;

	public Vector3Data Direction { get; set; } = new Vector3Data(0f, 1f, 0f);
	public float SpreadDegrees { get; set; } = 45.0f;
	public float InitialVelocityMin { get; set; } = 1.0f;
	public float InitialVelocityMax { get; set; } = 3.0f;
	public Vector3Data Gravity { get; set; } = new Vector3Data(0.0f, -4.0f, 0.0f);
	public Vector3Data LinearAccel { get; set; } = Vector3Data.Zero;
	public float RadialAccel { get; set; } = 0.0f;
	public float TangentialAccel { get; set; } = 0.0f;
	public float Damping { get; set; } = 0.5f;

	public float InitialScaleMin { get; set; } = 0.2f;
	public float InitialScaleMax { get; set; } = 0.4f;
	public float EndScaleRatio { get; set; } = 0.0f;

	public string ColorStart { get; set; } = "#ffaa00";
	public string ColorMid { get; set; } = "#ff4400";
	public string ColorEnd { get; set; } = "#220000";
	public float AlphaStart { get; set; } = 1.0f;
	public float AlphaMid { get; set; } = 0.8f;
	public float AlphaEnd { get; set; } = 0.0f;

	public float EmissionEnergy { get; set; } = 3.0f;

	public SpellParticleRenderMode RenderMode { get; set; } = SpellParticleRenderMode.BillboardQuad;
	public VfxBlendMode BlendMode { get; set; } = VfxBlendMode.Additive;
	public string ParticleTexture { get; set; } = "";
	public string MeshAssetPath { get; set; } = "";

	public SpellParticleConfig Clone()
	{
		return new SpellParticleConfig
		{
			ParticleId = ParticleId,
			Name = Name,
			Amount = Amount,
			Lifetime = Lifetime,
			Explosiveness = Explosiveness,
			Randomness = Randomness,
			LocalCoords = LocalCoords,
			EmitterShape = EmitterShape,
			SphereRadius = SphereRadius,
			BoxExtents = BoxExtents,
			RingRadius = RingRadius,
			RingInnerRadius = RingInnerRadius,
			RingHeight = RingHeight,
			Direction = Direction,
			SpreadDegrees = SpreadDegrees,
			InitialVelocityMin = InitialVelocityMin,
			InitialVelocityMax = InitialVelocityMax,
			Gravity = Gravity,
			LinearAccel = LinearAccel,
			RadialAccel = RadialAccel,
			TangentialAccel = TangentialAccel,
			Damping = Damping,
			InitialScaleMin = InitialScaleMin,
			InitialScaleMax = InitialScaleMax,
			EndScaleRatio = EndScaleRatio,
			ColorStart = ColorStart,
			ColorMid = ColorMid,
			ColorEnd = ColorEnd,
			AlphaStart = AlphaStart,
			AlphaMid = AlphaMid,
			AlphaEnd = AlphaEnd,
			EmissionEnergy = EmissionEnergy,
			RenderMode = RenderMode,
			BlendMode = BlendMode,
			ParticleTexture = ParticleTexture,
			MeshAssetPath = MeshAssetPath
		};
	}

	public static SpellParticleConfig CreatePreset(string presetName)
	{
		return presetName.ToLowerInvariant() switch
		{
			"fire_sparks" or "fire" => new SpellParticleConfig
			{
				ParticleId = "particle_fire_sparks",
				Name = "Fire Sparks",
				Amount = 40,
				Lifetime = 1.2f,
				Explosiveness = 0.05f,
				Randomness = 0.3f,
				EmitterShape = SpellParticleShape.Sphere,
				SphereRadius = 0.4f,
				Direction = new Vector3Data(0f, 1f, 0f),
				SpreadDegrees = 30.0f,
				InitialVelocityMin = 1.5f,
				InitialVelocityMax = 3.5f,
				Gravity = new Vector3Data(0.0f, 1.0f, 0.0f),
				Damping = 0.8f,
				InitialScaleMin = 0.15f,
				InitialScaleMax = 0.35f,
				EndScaleRatio = 0.0f,
				ColorStart = "#ffee44",
				ColorMid = "#ff5500",
				ColorEnd = "#440000",
				AlphaStart = 1.0f,
				AlphaMid = 0.85f,
				AlphaEnd = 0.0f,
				EmissionEnergy = 4.0f,
				RenderMode = SpellParticleRenderMode.BillboardQuad,
				BlendMode = VfxBlendMode.Additive
			},
			"arcane_burst" or "arcane" => new SpellParticleConfig
			{
				ParticleId = "particle_arcane_burst",
				Name = "Arcane Burst",
				Amount = 60,
				Lifetime = 0.8f,
				Explosiveness = 0.85f,
				Randomness = 0.2f,
				EmitterShape = SpellParticleShape.Point,
				Direction = new Vector3Data(0f, 1f, 0f),
				SpreadDegrees = 180.0f,
				InitialVelocityMin = 3.0f,
				InitialVelocityMax = 6.0f,
				Gravity = Vector3Data.Zero,
				Damping = 2.5f,
				InitialScaleMin = 0.2f,
				InitialScaleMax = 0.4f,
				EndScaleRatio = 0.1f,
				ColorStart = "#cceeff",
				ColorMid = "#6600ff",
				ColorEnd = "#000033",
				AlphaStart = 1.0f,
				AlphaMid = 0.7f,
				AlphaEnd = 0.0f,
				EmissionEnergy = 5.0f,
				RenderMode = SpellParticleRenderMode.BillboardQuad,
				BlendMode = VfxBlendMode.Additive
			},
			"frost_nova" or "frost" => new SpellParticleConfig
			{
				ParticleId = "particle_frost_nova",
				Name = "Frost Nova Ring",
				Amount = 50,
				Lifetime = 1.0f,
				Explosiveness = 0.9f,
				EmitterShape = SpellParticleShape.Ring,
				RingRadius = 1.5f,
				RingInnerRadius = 1.2f,
				RingHeight = 0.1f,
				Direction = new Vector3Data(0f, 1f, 0f),
				SpreadDegrees = 15.0f,
				InitialVelocityMin = 2.0f,
				InitialVelocityMax = 4.0f,
				RadialAccel = 4.0f,
				Gravity = new Vector3Data(0.0f, -1.0f, 0.0f),
				Damping = 1.5f,
				InitialScaleMin = 0.25f,
				InitialScaleMax = 0.45f,
				EndScaleRatio = 0.0f,
				ColorStart = "#ffffff",
				ColorMid = "#66ccff",
				ColorEnd = "#002266",
				AlphaStart = 1.0f,
				AlphaMid = 0.8f,
				AlphaEnd = 0.0f,
				EmissionEnergy = 3.5f,
				RenderMode = SpellParticleRenderMode.BillboardQuad,
				BlendMode = VfxBlendMode.Additive
			},
			"poison_spores" or "poison" => new SpellParticleConfig
			{
				ParticleId = "particle_poison_spores",
				Name = "Poison Spores",
				Amount = 30,
				Lifetime = 2.0f,
				Explosiveness = 0.0f,
				Randomness = 0.5f,
				EmitterShape = SpellParticleShape.Sphere,
				SphereRadius = 0.8f,
				Direction = new Vector3Data(0f, 1f, 0f),
				SpreadDegrees = 60.0f,
				InitialVelocityMin = 0.5f,
				InitialVelocityMax = 1.2f,
				Gravity = new Vector3Data(0.0f, 0.2f, 0.0f),
				TangentialAccel = 1.0f,
				Damping = 0.3f,
				InitialScaleMin = 0.3f,
				InitialScaleMax = 0.6f,
				EndScaleRatio = 0.2f,
				ColorStart = "#88ff33",
				ColorMid = "#22aa11",
				ColorEnd = "#003300",
				AlphaStart = 0.8f,
				AlphaMid = 0.6f,
				AlphaEnd = 0.0f,
				EmissionEnergy = 2.0f,
				RenderMode = SpellParticleRenderMode.BillboardQuad,
				BlendMode = VfxBlendMode.AlphaBlend
			},
			"holy_motes" or "holy" => new SpellParticleConfig
			{
				ParticleId = "particle_holy_motes",
				Name = "Holy Light Motes",
				Amount = 25,
				Lifetime = 1.5f,
				Explosiveness = 0.0f,
				Randomness = 0.4f,
				EmitterShape = SpellParticleShape.Box,
				BoxExtents = new Vector3Data(0.8f, 0.2f, 0.8f),
				Direction = new Vector3Data(0f, 1f, 0f),
				SpreadDegrees = 10.0f,
				InitialVelocityMin = 1.0f,
				InitialVelocityMax = 2.2f,
				Gravity = new Vector3Data(0.0f, 0.5f, 0.0f),
				Damping = 0.2f,
				InitialScaleMin = 0.15f,
				InitialScaleMax = 0.3f,
				EndScaleRatio = 0.0f,
				ColorStart = "#ffffff",
				ColorMid = "#ffea77",
				ColorEnd = "#aa7700",
				AlphaStart = 1.0f,
				AlphaMid = 0.9f,
				AlphaEnd = 0.0f,
				EmissionEnergy = 4.5f,
				RenderMode = SpellParticleRenderMode.BillboardQuad,
				BlendMode = VfxBlendMode.Additive
			},
			"projectile_debris" or "debris" => new SpellParticleConfig
			{
				ParticleId = "particle_projectile_debris",
				Name = "Mesh Projectile Debris",
				Amount = 16,
				Lifetime = 0.9f,
				Explosiveness = 0.95f,
				Randomness = 0.3f,
				EmitterShape = SpellParticleShape.Sphere,
				SphereRadius = 0.2f,
				Direction = new Vector3Data(0f, 1f, 0f),
				SpreadDegrees = 180.0f,
				InitialVelocityMin = 2.5f,
				InitialVelocityMax = 5.0f,
				Gravity = new Vector3Data(0.0f, -9.8f, 0.0f),
				Damping = 0.5f,
				InitialScaleMin = 0.2f,
				InitialScaleMax = 0.4f,
				EndScaleRatio = 0.1f,
				ColorStart = "#ffffff",
				ColorMid = "#ddaa66",
				ColorEnd = "#553311",
				AlphaStart = 1.0f,
				AlphaMid = 1.0f,
				AlphaEnd = 0.0f,
				EmissionEnergy = 1.5f,
				RenderMode = SpellParticleRenderMode.Mesh,
				BlendMode = VfxBlendMode.AlphaBlend
			},
			_ => new SpellParticleConfig()
		};
	}

	public static Dictionary<string, SpellParticleConfig> GetAllPresets()
	{
		return new Dictionary<string, SpellParticleConfig>(StringComparer.OrdinalIgnoreCase)
		{
			{ "particle_fire_sparks", CreatePreset("fire_sparks") },
			{ "particle_arcane_burst", CreatePreset("arcane_burst") },
			{ "particle_frost_nova", CreatePreset("frost_nova") },
			{ "particle_poison_spores", CreatePreset("poison_spores") },
			{ "particle_holy_motes", CreatePreset("holy_motes") },
			{ "particle_projectile_debris", CreatePreset("projectile_debris") }
		};
	}
}