using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Realm.Shared.Terrain;

namespace Realm.Shared.Metadata;

public class EnvironmentPresetConfig
{
	public string Id { get; set; } = "day";
	public string Name { get; set; } = "Day";
	public bool IsDefault { get; set; } = false;

	public float SunPitch { get; set; } = -58.0f;
	public float SunYaw { get; set; } = 29.0f;
	public float SunEnergy { get; set; } = 1.65f;
	public string SunColorHex { get; set; } = "#FFFDF0";
	public float ShadowBias { get; set; } = 0.03f;
	public float ShadowNormalBias { get; set; } = 1.2f;

	public float AmbientEnergy { get; set; } = 0.70f;
	public string AmbientColorHex { get; set; } = "#7A93BC";

	public bool FogEnabled { get; set; } = false;
	public float FogDensity { get; set; } = 0.0f;
	public string FogColorHex { get; set; } = "#8CA6BF";

	public bool SsaoEnabled { get; set; } = true;
	public float SsaoRadius { get; set; } = 1.20f;
	public float SsaoIntensity { get; set; } = 0.35f;
	public float SsaoDetail { get; set; } = 0.50f;

	public float TonemapExposure { get; set; } = 1.08f;
	public float AdjustmentContrast { get; set; } = 1.04f;
	public float AdjustmentSaturation { get; set; } = 1.06f;
	public float GlowIntensity { get; set; } = 0.15f;
	public float GlowBloom { get; set; } = 0.14f;
	public float GlowStrength { get; set; } = 0.90f;

	public float CharacterFillEnergy { get; set; } = 0.15f;

	public string WeatherType { get; set; } = "clear";
	public int RainParticleDensity { get; set; } = 0;
	public float BaseFogDensity { get; set; } = 0.0f;
	public string WeatherAnnouncement { get; set; } = "Weather Forecast: Clear Skies";

	public EnvironmentPresetConfig Clone()
	{
		return new EnvironmentPresetConfig
		{
			Id = this.Id,
			Name = this.Name,
			IsDefault = this.IsDefault,
			SunPitch = this.SunPitch,
			SunYaw = this.SunYaw,
			SunEnergy = this.SunEnergy,
			SunColorHex = this.SunColorHex,
			ShadowBias = this.ShadowBias,
			ShadowNormalBias = this.ShadowNormalBias,
			AmbientEnergy = this.AmbientEnergy,
			AmbientColorHex = this.AmbientColorHex,
			FogEnabled = this.FogEnabled,
			FogDensity = this.FogDensity,
			FogColorHex = this.FogColorHex,
			SsaoEnabled = this.SsaoEnabled,
			SsaoRadius = this.SsaoRadius,
			SsaoIntensity = this.SsaoIntensity,
			SsaoDetail = this.SsaoDetail,
			TonemapExposure = this.TonemapExposure,
			AdjustmentContrast = this.AdjustmentContrast,
			AdjustmentSaturation = this.AdjustmentSaturation,
			GlowIntensity = this.GlowIntensity,
			GlowBloom = this.GlowBloom,
			GlowStrength = this.GlowStrength,
			CharacterFillEnergy = this.CharacterFillEnergy,
			WeatherType = this.WeatherType,
			RainParticleDensity = this.RainParticleDensity,
			BaseFogDensity = this.BaseFogDensity,
			WeatherAnnouncement = this.WeatherAnnouncement
		};
	}

	public static List<EnvironmentPresetConfig> CreateDefaultPresets()
	{
		return new List<EnvironmentPresetConfig>
		{
			new EnvironmentPresetConfig
			{
				Id = "day",
				Name = "Day (Midday Sun)",
				IsDefault = true,
				SunPitch = -58.0f,
				SunYaw = 29.0f,
				SunEnergy = 1.65f,
				SunColorHex = "#FFFAEE",
				AmbientEnergy = 0.70f,
				AmbientColorHex = "#7A93BC",
				FogEnabled = false,
				FogDensity = 0.0f,
				FogColorHex = "#8CA6BF",
				SsaoEnabled = true,
				SsaoRadius = 1.20f,
				SsaoIntensity = 0.35f,
				SsaoDetail = 0.50f,
				TonemapExposure = 1.08f,
				AdjustmentContrast = 1.04f,
				AdjustmentSaturation = 1.06f,
				GlowIntensity = 0.15f,
				GlowBloom = 0.14f,
				GlowStrength = 0.90f,
				CharacterFillEnergy = 0.15f,
				WeatherType = "clear",
				RainParticleDensity = 0,
				BaseFogDensity = 0.0f,
				WeatherAnnouncement = "Weather Forecast: Clear Skies"
			},
			new EnvironmentPresetConfig
			{
				Id = "dusk",
				Name = "Dusk (Golden Hour)",
				IsDefault = false,
				SunPitch = -45.0f,
				SunYaw = -115.0f,
				SunEnergy = 1.35f,
				SunColorHex = "#FFB261",
				AmbientEnergy = 0.75f,
				AmbientColorHex = "#6B75B7",
				FogEnabled = false,
				FogDensity = 0.0f,
				FogColorHex = "#734D59",
				SsaoEnabled = true,
				SsaoRadius = 1.20f,
				SsaoIntensity = 0.30f,
				SsaoDetail = 0.50f,
				TonemapExposure = 1.10f,
				AdjustmentContrast = 1.04f,
				AdjustmentSaturation = 1.00f,
				GlowIntensity = 0.30f,
				GlowBloom = 0.14f,
				GlowStrength = 0.90f,
				CharacterFillEnergy = 0.20f,
				WeatherType = "clear",
				RainParticleDensity = 0,
				BaseFogDensity = 0.0f,
				WeatherAnnouncement = "Weather Forecast: Golden Dusk"
			},
			new EnvironmentPresetConfig
			{
				Id = "night",
				Name = "Night (Moonlight)",
				IsDefault = false,
				SunPitch = -50.0f,
				SunYaw = 155.0f,
				SunEnergy = 0.45f,
				SunColorHex = "#B2E0FF",
				AmbientEnergy = 0.95f,
				AmbientColorHex = "#476BD8",
				FogEnabled = false,
				FogDensity = 0.0f,
				FogColorHex = "#141F33",
				SsaoEnabled = true,
				SsaoRadius = 1.00f,
				SsaoIntensity = 0.20f,
				SsaoDetail = 0.50f,
				TonemapExposure = 1.12f,
				AdjustmentContrast = 1.02f,
				AdjustmentSaturation = 1.08f,
				GlowIntensity = 0.25f,
				GlowBloom = 0.08f,
				GlowStrength = 0.90f,
				CharacterFillEnergy = 0.35f,
				WeatherType = "clear",
				RainParticleDensity = 0,
				BaseFogDensity = 0.0f,
				WeatherAnnouncement = "Weather Forecast: Starry Night"
			},
			new EnvironmentPresetConfig
			{
				Id = "dawn",
				Name = "Dawn (Sunrise)",
				IsDefault = false,
				SunPitch = -45.0f,
				SunYaw = 95.0f,
				SunEnergy = 1.70f,
				SunColorHex = "#FFE0B7",
				AmbientEnergy = 0.75f,
				AmbientColorHex = "#9389D6",
				FogEnabled = false,
				FogDensity = 0.0f,
				FogColorHex = "#66738C",
				SsaoEnabled = true,
				SsaoRadius = 1.10f,
				SsaoIntensity = 0.30f,
				SsaoDetail = 0.50f,
				TonemapExposure = 1.08f,
				AdjustmentContrast = 1.04f,
				AdjustmentSaturation = 1.06f,
				GlowIntensity = 0.20f,
				GlowBloom = 0.10f,
				GlowStrength = 0.90f,
				CharacterFillEnergy = 0.22f,
				WeatherType = "clear",
				RainParticleDensity = 0,
				BaseFogDensity = 0.0f,
				WeatherAnnouncement = "Weather Forecast: Morning Dawn"
			},
			new EnvironmentPresetConfig
			{
				Id = "rain",
				Name = "Rainy Storm",
				IsDefault = false,
				SunPitch = -60.0f,
				SunYaw = 45.0f,
				SunEnergy = 1.20f,
				SunColorHex = "#C2D4E5",
				AmbientEnergy = 0.70f,
				AmbientColorHex = "#556B82",
				FogEnabled = true,
				FogDensity = 0.0075f,
				FogColorHex = "#4F6378",
				SsaoEnabled = true,
				SsaoRadius = 1.20f,
				SsaoIntensity = 0.40f,
				SsaoDetail = 0.50f,
				TonemapExposure = 1.05f,
				AdjustmentContrast = 1.00f,
				AdjustmentSaturation = 0.85f,
				GlowIntensity = 0.10f,
				GlowBloom = 0.05f,
				GlowStrength = 0.90f,
				CharacterFillEnergy = 0.25f,
				WeatherType = "rain",
				RainParticleDensity = 800,
				BaseFogDensity = 0.0075f,
				WeatherAnnouncement = "Weather Forecast: Light Rain Shower"
			},
			new EnvironmentPresetConfig
			{
				Id = "snow",
				Name = "Winter Snow",
				IsDefault = false,
				SunPitch = -50.0f,
				SunYaw = 35.0f,
				SunEnergy = 2.00f,
				SunColorHex = "#EBF4FF",
				AmbientEnergy = 0.85f,
				AmbientColorHex = "#8CA6BF",
				FogEnabled = true,
				FogDensity = 0.0050f,
				FogColorHex = "#A6B8CC",
				SsaoEnabled = true,
				SsaoRadius = 1.20f,
				SsaoIntensity = 0.35f,
				SsaoDetail = 0.50f,
				TonemapExposure = 1.15f,
				AdjustmentContrast = 1.02f,
				AdjustmentSaturation = 0.95f,
				GlowIntensity = 0.20f,
				GlowBloom = 0.12f,
				GlowStrength = 0.90f,
				CharacterFillEnergy = 0.20f,
				WeatherType = "snow",
				RainParticleDensity = 600,
				BaseFogDensity = 0.0050f,
				WeatherAnnouncement = "Weather Forecast: Winter Snowfall"
			},
			new EnvironmentPresetConfig
			{
				Id = "fog",
				Name = "Dense Fog",
				IsDefault = false,
				SunPitch = -55.0f,
				SunYaw = 30.0f,
				SunEnergy = 0.90f,
				SunColorHex = "#DDE4EB",
				AmbientEnergy = 0.75f,
				AmbientColorHex = "#8A9BA8",
				FogEnabled = true,
				FogDensity = 0.0450f,
				FogColorHex = "#B3C2CC",
				SsaoEnabled = true,
				SsaoRadius = 1.00f,
				SsaoIntensity = 0.25f,
				SsaoDetail = 0.50f,
				TonemapExposure = 1.10f,
				AdjustmentContrast = 0.95f,
				AdjustmentSaturation = 0.80f,
				GlowIntensity = 0.20f,
				GlowBloom = 0.15f,
				GlowStrength = 0.90f,
				CharacterFillEnergy = 0.30f,
				WeatherType = "fog",
				RainParticleDensity = 0,
				BaseFogDensity = 0.0175f,
				WeatherAnnouncement = "Weather Forecast: Dense Fog Warning"
			}
		};
	}
}

public class ProceduralBombingDecalRule
{
	public string DecalId { get; set; } = "";
	public float Density { get; set; } = 0.5f;
	public float MinScale { get; set; } = 0.2f;
	public float MaxScale { get; set; } = 0.5f;
	public float MinRotationDeg { get; set; } = 0.0f;
	public float MaxRotationDeg { get; set; } = 360.0f;
	public string TintHex { get; set; } = "#FFFFFF";

	public ProceduralBombingDecalRule Clone()
	{
		return new ProceduralBombingDecalRule
		{
			DecalId = this.DecalId,
			Density = this.Density,
			MinScale = this.MinScale,
			MaxScale = this.MaxScale,
			MinRotationDeg = this.MinRotationDeg,
			MaxRotationDeg = this.MaxRotationDeg,
			TintHex = this.TintHex
		};
	}
}

public class ProceduralBombingVfxRule
{
	public string VfxId { get; set; } = "";
	public float Density { get; set; } = 0.5f;
	public float MinScale { get; set; } = 0.2f;
	public float MaxScale { get; set; } = 0.5f;
	public float NormalOffset { get; set; } = 0.0f;
	public VfxAttachmentConfig? Config { get; set; }

	public ProceduralBombingVfxRule Clone()
	{
		return new ProceduralBombingVfxRule
		{
			VfxId = this.VfxId,
			Density = this.Density,
			MinScale = this.MinScale,
			MaxScale = this.MaxScale,
			NormalOffset = this.NormalOffset,
			Config = this.Config?.Clone()
		};
	}
}

public class WaterProfileSaveData
{
	public string Id { get; set; } = "water_default";
	public string Name { get; set; } = "Water";
	public byte ProfileIndex { get; set; } = 0;
	public string ShallowColorHex { get; set; } = "#0D4D618C";
	public string DeepColorHex { get; set; } = "#030F24FA";
	public string FoamColorHex { get; set; } = "#D9F2FFD9";
	public float MaxDepth { get; set; } = 2.0f;
	public float FoamDepth { get; set; } = 0.6f;
	public float WaveSpeed { get; set; } = 1.2f;
	public float WaveStrength { get; set; } = 0.06f;
	public bool UseNormalTexture { get; set; } = false;
	public string NormalTexturePath { get; set; } = "";
	public float NormalScale { get; set; } = 1.0f;
	public float FlowDirectionX { get; set; } = 1.0f;
	public float FlowDirectionY { get; set; } = 0.0f;
	public float FlowSpeed { get; set; } = 0.5f;
	public bool UseFlowMap { get; set; } = false;
	public string FlowMapPath { get; set; } = "";
	public float RefractionStrength { get; set; } = 0.0f;
	public float CausticStrength { get; set; } = 0.0f;
	public float CausticScale { get; set; } = 1.0f;
	public float CausticSpeed { get; set; } = 1.0f;
	public string EmissionColorHex { get; set; } = "#000000FF";
	public float EmissionBoost { get; set; } = 0.0f;
	public string CoreColorHex { get; set; } = "#FFE680FF";
	public float CoreThreshold { get; set; } = 0.8f;
	public string SubsurfaceColorHex { get; set; } = "#000000FF";
	public float SubsurfaceStrength { get; set; } = 0.0f;
	public bool UseDetailTexture { get; set; } = false;
	public string DetailTexturePath { get; set; } = "";
	public string DetailTileMode { get; set; } = "Stochastic";
	public float DetailUvScaleX { get; set; } = 1.0f;
	public float DetailUvScaleY { get; set; } = 1.0f;
	public float DetailUvScrollX { get; set; } = 0.0f;
	public float DetailUvScrollY { get; set; } = 0.0f;
	public float DetailStochasticTileSize { get; set; } = 1.0f;
	public float DetailCrossFade { get; set; } = 0.0f;
	public float DetailAlpha { get; set; } = 0.5f;
	public int DetailBlendMode { get; set; } = 0;
	public int DefaultPathingCode { get; set; } = 1 | 4; // ShallowWater | Flying
	public List<ProceduralBombingDecalRule> DecalBombingRules { get; set; } = new();
	public List<ProceduralBombingVfxRule> VfxBombingRules { get; set; } = new();

	public WaterProfileSaveData Clone()
	{
		var clone = new WaterProfileSaveData
		{
			Id = this.Id,
			Name = this.Name,
			ProfileIndex = this.ProfileIndex,
			ShallowColorHex = this.ShallowColorHex,
			DeepColorHex = this.DeepColorHex,
			FoamColorHex = this.FoamColorHex,
			MaxDepth = this.MaxDepth,
			FoamDepth = this.FoamDepth,
			WaveSpeed = this.WaveSpeed,
			WaveStrength = this.WaveStrength,
			UseNormalTexture = this.UseNormalTexture,
			NormalTexturePath = this.NormalTexturePath,
			NormalScale = this.NormalScale,
			FlowDirectionX = this.FlowDirectionX,
			FlowDirectionY = this.FlowDirectionY,
			FlowSpeed = this.FlowSpeed,
			UseFlowMap = this.UseFlowMap,
			FlowMapPath = this.FlowMapPath,
			RefractionStrength = this.RefractionStrength,
			CausticStrength = this.CausticStrength,
			CausticScale = this.CausticScale,
			CausticSpeed = this.CausticSpeed,
			EmissionColorHex = this.EmissionColorHex,
			EmissionBoost = this.EmissionBoost,
			CoreColorHex = this.CoreColorHex,
			CoreThreshold = this.CoreThreshold,
			SubsurfaceColorHex = this.SubsurfaceColorHex,
			SubsurfaceStrength = this.SubsurfaceStrength,
			UseDetailTexture = this.UseDetailTexture,
			DetailTexturePath = this.DetailTexturePath,
			DetailTileMode = this.DetailTileMode,
			DetailUvScaleX = this.DetailUvScaleX,
			DetailUvScaleY = this.DetailUvScaleY,
			DetailUvScrollX = this.DetailUvScrollX,
			DetailUvScrollY = this.DetailUvScrollY,
			DetailStochasticTileSize = this.DetailStochasticTileSize,
			DetailCrossFade = this.DetailCrossFade,
			DetailAlpha = this.DetailAlpha,
			DetailBlendMode = this.DetailBlendMode,
			DefaultPathingCode = this.DefaultPathingCode,
			DecalBombingRules = new List<ProceduralBombingDecalRule>(),
			VfxBombingRules = new List<ProceduralBombingVfxRule>()
		};
		if (this.DecalBombingRules != null)
		{
			foreach (var r in this.DecalBombingRules) clone.DecalBombingRules.Add(r.Clone());
		}
		if (this.VfxBombingRules != null)
		{
			foreach (var r in this.VfxBombingRules) clone.VfxBombingRules.Add(r.Clone());
		}
		return clone;
	}

	public static List<WaterProfileSaveData> CreateDefaultProfiles()
	{
		return new List<WaterProfileSaveData>
		{
			new WaterProfileSaveData
			{
				Id = "water_shallow",
				Name = "Shallow Water",
				ProfileIndex = 0,
				ShallowColorHex = "#4AD4E655",
				DeepColorHex = "#0A5C8AE0",
				FoamColorHex = "#F0FBFFC0",
				MaxDepth = 1.8f,
				FoamDepth = 0.20f,
				WaveSpeed = 1.2f,
				WaveStrength = 0.04f,
				NormalScale = 0.4f,
				RefractionStrength = 0.20f,
				CausticStrength = 0.50f,
				CausticScale = 1.5f,
				CausticSpeed = 1.2f,
				DefaultPathingCode = 1 | 4
			},
			new WaterProfileSaveData
			{
				Id = "water_deep",
				Name = "Deep Ocean",
				ProfileIndex = 1,
				ShallowColorHex = "#082B5ECC",
				DeepColorHex = "#010614FA",
				FoamColorHex = "#D0EBFFB0",
				MaxDepth = 3.0f,
				FoamDepth = 0.22f,
				WaveSpeed = 1.0f,
				WaveStrength = 0.06f,
				NormalScale = 0.1f,
				RefractionStrength = 0.08f,
				CausticStrength = 0.20f,
				CausticScale = 0.8f,
				CausticSpeed = 0.8f,
				DefaultPathingCode = 2 | 4
			}
		};
	}
}

public class TerrainSwatchProfileData
{
	public int DefaultPathingCode { get; set; } = 8 | 32 | 4; // Ground | Buildable | Flying
	public List<ProceduralBombingDecalRule> DecalBombingRules { get; set; } = new();
	public List<ProceduralBombingVfxRule> VfxBombingRules { get; set; } = new();

	public TerrainSwatchProfileData Clone()
	{
		var clone = new TerrainSwatchProfileData
		{
			DefaultPathingCode = this.DefaultPathingCode,
			DecalBombingRules = new List<ProceduralBombingDecalRule>(),
			VfxBombingRules = new List<ProceduralBombingVfxRule>()
		};
		if (this.DecalBombingRules != null)
		{
			foreach (var r in this.DecalBombingRules) clone.DecalBombingRules.Add(r.Clone());
		}
		if (this.VfxBombingRules != null)
		{
			foreach (var r in this.VfxBombingRules) clone.VfxBombingRules.Add(r.Clone());
		}
		return clone;
	}
}

public enum ProceduralMotionType
{
	ClothSway = 1,
	FoliageWind = 2,
	WingFlap = 3,
	TransientShake = 4
}

public enum SpatialMaskMode
{
	HeightGradient = 0,
	RadialAxis = 1,
	NormalFacing = 2
}

public class ProceduralAnimationConfig
{
	[JsonPropertyName("Id")]
	public string Id { get; set; } = string.Empty;

	[JsonPropertyName("Name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("MotionType")]
	public ProceduralMotionType MotionType { get; set; } = ProceduralMotionType.ClothSway;

	[JsonPropertyName("MaskMode")]
	public SpatialMaskMode MaskMode { get; set; } = SpatialMaskMode.HeightGradient;

	[JsonPropertyName("MaskMin")]
	public float MaskMin { get; set; } = 0.0f;

	[JsonPropertyName("MaskMax")]
	public float MaskMax { get; set; } = 2.0f;

	[JsonPropertyName("MaskPower")]
	public float MaskPower { get; set; } = 1.0f;

	[JsonPropertyName("MaskInvert")]
	public bool MaskInvert { get; set; } = false;

	[JsonPropertyName("SwayFrequency")]
	public float SwayFrequency { get; set; } = 1.5f;

	[JsonPropertyName("SwayAmplitude")]
	public float SwayAmplitude { get; set; } = 0.15f;

	[JsonPropertyName("FlutterFrequency")]
	public float FlutterFrequency { get; set; } = 5.0f;

	[JsonPropertyName("FlutterAmplitude")]
	public float FlutterAmplitude { get; set; } = 0.03f;

	[JsonPropertyName("WindInfluence")]
	public float WindInfluence { get; set; } = 1.0f;

	[JsonPropertyName("VelocityDragInfluence")]
	public float VelocityDragInfluence { get; set; } = 1.0f;

	[JsonPropertyName("WaveTurbulence")]
	public float WaveTurbulence { get; set; } = 0.5f;

	[JsonPropertyName("ImpulseDecay")]
	public float ImpulseDecay { get; set; } = 4.0f;

	[JsonPropertyName("ImpulseFrequency")]
	public float ImpulseFrequency { get; set; } = 12.0f;

	[JsonPropertyName("ImpulseAmplitude")]
	public float ImpulseAmplitude { get; set; } = 0.25f;

	[JsonPropertyName("ImpulseDuration")]
	public float ImpulseDuration { get; set; } = 0.6f;

	public ProceduralAnimationConfig Clone()
	{
		return new ProceduralAnimationConfig
		{
			Id = Id,
			Name = Name,
			MotionType = MotionType,
			MaskMode = MaskMode,
			MaskMin = MaskMin,
			MaskMax = MaskMax,
			MaskPower = MaskPower,
			MaskInvert = MaskInvert,
			SwayFrequency = SwayFrequency,
			SwayAmplitude = SwayAmplitude,
			FlutterFrequency = FlutterFrequency,
			FlutterAmplitude = FlutterAmplitude,
			WindInfluence = WindInfluence,
			VelocityDragInfluence = VelocityDragInfluence,
			WaveTurbulence = WaveTurbulence,
			ImpulseDecay = ImpulseDecay,
			ImpulseFrequency = ImpulseFrequency,
			ImpulseAmplitude = ImpulseAmplitude,
			ImpulseDuration = ImpulseDuration
		};
	}
}

public enum VfxPrimitiveType
{
	VortexDisc,
	FunnelCone,
	RibbonRing,
	HemisphereDome,
	GroundPlane,
	WeaponFin,
	CrossQuad,
	SlashArc,
	LightShaft,
	AuraCapsule,
	AuraSphere,
	ProjectedVolumeCube,
	ParticleSystem
}

public enum VfxBlendMode
{
	Additive,
	AlphaBlend
}

public enum VfxPlacementMode
{
	SurfaceSnap,
	Free
}

public enum SpellParticleShape
{
	Point,
	Sphere,
	Box,
	Ring
}

public enum SpellParticleRenderMode
{
	BillboardQuad,
	Mesh
}

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

public class VfxAttachmentConfig
{
	public string VfxId { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;

	public VfxPrimitiveType PrimitiveType { get; set; } = VfxPrimitiveType.VortexDisc;
	public SpellParticleConfig? ParticleConfig { get; set; }
	public VfxBlendMode BlendMode { get; set; } = VfxBlendMode.Additive;
	public VfxPlacementMode PlacementMode { get; set; } = VfxPlacementMode.SurfaceSnap;
	public string TargetSocket { get; set; } = "Root";

	public string BaseTexture { get; set; } = "";
	public string NoiseTexture { get; set; } = "";

	public Vector2Data BaseUvScroll { get; set; } = new Vector2Data(0.2f, 0.0f);
	public Vector2Data BaseUvScale { get; set; } = Vector2Data.One;
	public bool UseFlipbook { get; set; } = false;
	public int FlipbookColumns { get; set; } = 1;
	public int FlipbookRows { get; set; } = 1;
	public float FlipbookFps { get; set; } = 12.0f;
	public bool FlipbookSubframeBlend { get; set; } = true;
	public Vector2Data NoiseUvScroll { get; set; } = new Vector2Data(-0.15f, 0.25f);
	public Vector2Data NoiseUvScale { get; set; } = Vector2Data.One;

	public float DistortionStrength { get; set; } = 0.2f;

	public string BaseColor { get; set; } = "#ff7711";
	public string SecondaryColor { get; set; } = "#aa1100";
	public string CoreColor { get; set; } = "#ffffff";
	public float EmissionBoost { get; set; } = 3.5f;
	public float CoreThreshold { get; set; } = 0.65f;

	public bool LuminanceToAlpha { get; set; } = true;
	public float LuminanceThreshold { get; set; } = 0.05f;
	public float LuminanceSmoothness { get; set; } = 0.08f;
	public bool UseGrayscale { get; set; } = true;
	public bool InvertMask { get; set; } = false;
	public float HighPassCutoff { get; set; } = 0.0f;

	public bool EnableRadialFalloff { get; set; } = true;
	public float RadialFalloffStart { get; set; } = 0.65f;
	public float RadialFalloffEnd { get; set; } = 1.0f;

	public bool EnableLengthFade { get; set; } = false;
	public float LengthFadeStart { get; set; } = 0.0f;
	public float LengthFadeEnd { get; set; } = 1.0f;
	public float ErosionProgress { get; set; } = 0.0f;

	public bool EnableFresnel { get; set; } = false;
	public float FresnelPower { get; set; } = 2.5f;
	public float FresnelIntensity { get; set; } = 1.5f;

	public bool EnableDepthFade { get; set; } = true;
	public float DepthFadeDistance { get; set; } = 0.35f;

	public float SurfaceNormalOffset { get; set; } = 0.02f;

	public Vector3Data PositionOffset { get; set; } = Vector3Data.Zero;
	public Vector3Data RotationOffset { get; set; } = Vector3Data.Zero;
	public Vector3Data ScaleOffset { get; set; } = Vector3Data.One;

	public VfxAttachmentConfig Clone()
	{
		return new VfxAttachmentConfig
		{
			VfxId = VfxId,
			Name = Name,
			PrimitiveType = PrimitiveType,
			BlendMode = BlendMode,
			PlacementMode = PlacementMode,
			TargetSocket = TargetSocket,
			BaseTexture = BaseTexture,
			NoiseTexture = NoiseTexture,
			BaseUvScroll = BaseUvScroll,
			BaseUvScale = BaseUvScale,
			UseFlipbook = UseFlipbook,
			FlipbookColumns = FlipbookColumns,
			FlipbookRows = FlipbookRows,
			FlipbookFps = FlipbookFps,
			FlipbookSubframeBlend = FlipbookSubframeBlend,
			NoiseUvScroll = NoiseUvScroll,
			NoiseUvScale = NoiseUvScale,
			DistortionStrength = DistortionStrength,
			BaseColor = BaseColor,
			SecondaryColor = SecondaryColor,
			CoreColor = CoreColor,
			EmissionBoost = EmissionBoost,
			CoreThreshold = CoreThreshold,
			LuminanceToAlpha = LuminanceToAlpha,
			LuminanceThreshold = LuminanceThreshold,
			LuminanceSmoothness = LuminanceSmoothness,
			UseGrayscale = UseGrayscale,
			InvertMask = InvertMask,
			HighPassCutoff = HighPassCutoff,
			EnableRadialFalloff = EnableRadialFalloff,
			RadialFalloffStart = RadialFalloffStart,
			RadialFalloffEnd = RadialFalloffEnd,
			EnableLengthFade = EnableLengthFade,
			LengthFadeStart = LengthFadeStart,
			LengthFadeEnd = LengthFadeEnd,
			ErosionProgress = ErosionProgress,
			EnableFresnel = EnableFresnel,
			FresnelPower = FresnelPower,
			FresnelIntensity = FresnelIntensity,
			EnableDepthFade = EnableDepthFade,
			DepthFadeDistance = DepthFadeDistance,
			SurfaceNormalOffset = SurfaceNormalOffset,
			PositionOffset = PositionOffset,
			RotationOffset = RotationOffset,
			ScaleOffset = ScaleOffset,
			ParticleConfig = ParticleConfig?.Clone()
		};
	}
}
