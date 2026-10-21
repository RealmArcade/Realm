using System;
using MemoryPack;

namespace Realm.Client;

[MemoryPackable]
public partial class DecalAnimationConfig
{
	public bool AnimateOpacity { get; set; } = false;
	public float OpacityPulseSpeed { get; set; } = 1.0f;
	public float MinOpacity { get; set; } = 0.2f;
	public float MaxOpacity { get; set; } = 1.0f;

	public bool AnimateEmission { get; set; } = false;
	public float EmissionPulseSpeed { get; set; } = 1.0f;
	public float MinEmission { get; set; } = 0.0f;
	public float MaxEmission { get; set; } = 2.0f;

	public bool AnimateScale { get; set; } = false;
	public float ScalePulseSpeed { get; set; } = 1.0f;
	public float MinScaleRatio { get; set; } = 0.8f;
	public float MaxScaleRatio { get; set; } = 1.2f;
}
