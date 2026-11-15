using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("One environment preset: the sun, ambient light, fog, ambient occlusion, glow and weather a match is lit with.")]
public class EnvironmentPresetConfig
{
	[SchemaDescription("The identifier the map's default preset field and a match setting name this preset by.")]
	[SchemaSensitive]
	public string Id { get; set; } = "day";

	[SchemaDescription("The name the preset shows in a match's environment list.")]
	public string Name { get; set; } = "Day";

	[SchemaDescription("Whether a match on this map starts with this preset when the map names none.")]
	public bool IsDefault { get; set; } = false;

	[SchemaDescription("How high the sun stands, from straight overhead to below the horizon.")]
	[SchemaUnit("°")]
	public float SunPitch { get; set; } = -58.0f;

	[SchemaDescription("Which way the sun faces, measured around the map.")]
	[SchemaUnit("°")]
	public float SunYaw { get; set; } = 29.0f;

	[SchemaDescription("How strongly the sun lights the battlefield.")]
	public float SunEnergy { get; set; } = 1.65f;

	[SchemaDescription("The colour of the sunlight.")]
	[SchemaControl(RealmSchemaControl.Color)]
	public string SunColorHex { get; set; } = "#FFFDF0";

	[SchemaDescription("How far shadow samples are pushed towards the light, which trades shadow acne for a little light leaking.")]
	public float ShadowBias { get; set; } = 0.03f;

	[SchemaDescription("How far shadow samples are pushed along the surface normal, which removes shadow acne on flat ground.")]
	public float ShadowNormalBias { get; set; } = 1.2f;

	[SchemaDescription("How strongly the sky fills the shadows.")]
	public float AmbientEnergy { get; set; } = 0.70f;

	[SchemaDescription("The colour of the light that fills the shadows.")]
	[SchemaControl(RealmSchemaControl.Color)]
	public string AmbientColorHex { get; set; } = "#7A93BC";

	[SchemaDescription("Whether the map is wrapped in distance fog.")]
	public bool FogEnabled { get; set; } = false;

	[SchemaDescription("How thick the distance fog is, where higher hides more of the horizon.")]
	public float FogDensity { get; set; } = 0.0f;

	[SchemaDescription("The colour the distance fog fades the horizon into.")]
	[SchemaControl(RealmSchemaControl.Color)]
	public string FogColorHex { get; set; } = "#8CA6BF";

	[SchemaDescription("Whether screen-space ambient occlusion darkens the creases between models and the ground.")]
	public bool SsaoEnabled { get; set; } = true;

	[SchemaDescription("How far the ambient occlusion reaches from each crease.")]
	[SchemaUnit("m")]
	public float SsaoRadius { get; set; } = 1.20f;

	[SchemaDescription("How dark the ambient occlusion makes the creases it finds.")]
	[SchemaRange(0, 1)]
	public float SsaoIntensity { get; set; } = 0.35f;

	[SchemaDescription("How much fine detail the ambient occlusion keeps, rather than smoothing it away.")]
	[SchemaRange(0, 1)]
	public float SsaoDetail { get; set; } = 0.50f;

	[SchemaDescription("How bright the finished picture is exposed at.")]
	public float TonemapExposure { get; set; } = 1.08f;

	[SchemaDescription("How much contrast is added to the finished picture.")]
	public float AdjustmentContrast { get; set; } = 1.04f;

	[SchemaDescription("How saturated the finished picture is.")]
	public float AdjustmentSaturation { get; set; } = 1.06f;

	[SchemaDescription("How strongly bright parts of the picture bloom.")]
	public float GlowIntensity { get; set; } = 0.15f;

	[SchemaDescription("How far the glow spreads from each bright part.")]
	public float GlowBloom { get; set; } = 0.14f;

	[SchemaDescription("How strong the glow is where it is applied.")]
	public float GlowStrength { get; set; } = 0.90f;

	[SchemaDescription("How strongly a character is filled from the front, so a unit stays readable when it stands against the sun.")]
	public float CharacterFillEnergy { get; set; } = 0.15f;

	[SchemaDescription("The weather the preset runs, such as clear, rain, snow or fog.")]
	public string WeatherType { get; set; } = "clear";

	[SchemaDescription("How many rain or snow particles are drawn at once. Zero turns the particles off without leaving the weather.")]
	public int RainParticleDensity { get; set; } = 0;

	[SchemaDescription("The fog the preset's weather settles at, as opposed to the fog it starts at.")]
	public float BaseFogDensity { get; set; } = 0.0f;

	[SchemaDescription("The line the match announces to the players when the preset's weather begins.")]
	[SchemaControl(RealmSchemaControl.Text)]
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