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