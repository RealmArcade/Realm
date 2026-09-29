using System;
using System.Text.Json.Serialization;

namespace Realm.Godot.VFX;

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
