using Godot;

namespace Realm.Client.Services;

public struct LiveEnvironmentState
{
	public float SunPitch;
	public float SunYaw;
	public float SunEnergy;
	public Color SunColor;
	public float ShadowBias;
	public float ShadowNormalBias;
	public float AmbientEnergy;
	public Color AmbientColor;
	public bool FogEnabled;
	public float FogDensity;
	public Color FogColor;
	public bool SsaoEnabled;
	public float SsaoRadius;
	public float SsaoIntensity;
	public float SsaoDetail;
	public float TonemapExposure;
	public float AdjustmentContrast;
	public float AdjustmentSaturation;
	public float GlowIntensity;
	public float GlowBloom;
	public float GlowStrength;
	public float CharacterFillEnergy;
	public string WeatherType;
}