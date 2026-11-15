using System.Collections.Generic;
using System.Text.Json.Serialization;
using Realm.Shared.Serialization;
using Realm.Shared.Terrain;

namespace Realm.Shared.Metadata;

[SchemaDescription("One procedural motion profile: which parts of a model move, how strongly they sway with the wind, and how they shake when they are struck.")]
public class ProceduralAnimationConfig
{
	[JsonPropertyName("Id")]
	[SchemaDescription("The identifier placeables and visual effects name this profile by.")]
	[SchemaSensitive]
	public string Id { get; set; } = string.Empty;

	[JsonPropertyName("Name")]
	[SchemaDescription("The name the profile shows in the editor.")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("MotionType")]
	[SchemaDescription("The kind of motion the profile applies, which decides which of its settings are used.")]
	public ProceduralMotionType MotionType { get; set; } = ProceduralMotionType.ClothSway;

	[JsonPropertyName("MaskMode")]
	[SchemaDescription("How the profile decides which parts of a model move.")]
	public SpatialMaskMode MaskMode { get; set; } = SpatialMaskMode.HeightGradient;

	[JsonPropertyName("MaskMin")]
	[SchemaDescription("The lowest value of the mask's measure at which a vertex starts to move.")]
	public float MaskMin { get; set; } = 0.0f;

	[JsonPropertyName("MaskMax")]
	[SchemaDescription("The highest value of the mask's measure, at which a vertex moves fully.")]
	public float MaskMax { get; set; } = 2.0f;

	[JsonPropertyName("MaskPower")]
	[SchemaDescription("How sharply motion falls off between the mask's low and high values.")]
	public float MaskPower { get; set; } = 1.0f;

	[JsonPropertyName("MaskInvert")]
	[SchemaDescription("Whether the mask is inverted, so the parts that would stay still are the parts that move.")]
	public bool MaskInvert { get; set; } = false;

	[JsonPropertyName("MaskRegionMin")]
	[SchemaDescription("The lower corner of the box only the inside of which moves, when the mask mode is a region box.")]
	public Vector3Data MaskRegionMin { get; set; } = new(-1.0f, -1.0f, -1.0f);

	[JsonPropertyName("MaskRegionMax")]
	[SchemaDescription("The upper corner of the box only the inside of which moves, when the mask mode is a region box.")]
	public Vector3Data MaskRegionMax { get; set; } = new(1.0f, 3.0f, 1.0f);

	[JsonPropertyName("SwayRegions")]
	[SchemaDescription("The named regions of the model that sway with their own falloff instead of the profile's mask.")]
	public List<ProceduralSwayRegionConfig> SwayRegions { get; set; } = new();

	[JsonPropertyName("SwayFrequency")]
	[SchemaDescription("How many times per second the model sways back and forth.")]
	public float SwayFrequency { get; set; } = 1.5f;

	[JsonPropertyName("SwayAmplitude")]
	[SchemaDescription("How far the model sways from its rest pose.")]
	public float SwayAmplitude { get; set; } = 0.15f;

	[JsonPropertyName("FlutterFrequency")]
	[SchemaDescription("How many times per second the model flutters in the wind, which is the fine motion on top of the sway.")]
	public float FlutterFrequency { get; set; } = 5.0f;

	[JsonPropertyName("FlutterAmplitude")]
	[SchemaDescription("How far the model flutters from its rest pose.")]
	public float FlutterAmplitude { get; set; } = 0.03f;

	[JsonPropertyName("WindInfluence")]
	[SchemaDescription("How strongly the match's wind pushes the model around.")]
	public float WindInfluence { get; set; } = 1.0f;

	[JsonPropertyName("VelocityDragInfluence")]
	[SchemaDescription("How strongly the model lags behind its own movement, which is what makes a banner trail a running unit.")]
	public float VelocityDragInfluence { get; set; } = 1.0f;

	[JsonPropertyName("WaveTurbulence")]
	[SchemaDescription("How irregular the wind's motion is, from a steady sway to a choppy one.")]
	public float WaveTurbulence { get; set; } = 0.5f;

	[JsonPropertyName("ImpulseDecay")]
	[SchemaDescription("How quickly the shake from a hit dies away.")]
	public float ImpulseDecay { get; set; } = 4.0f;

	[JsonPropertyName("ImpulseFrequency")]
	[SchemaDescription("How fast the model shakes when it is struck.")]
	public float ImpulseFrequency { get; set; } = 12.0f;

	[JsonPropertyName("ImpulseAmplitude")]
	[SchemaDescription("How far the model moves when it is struck.")]
	public float ImpulseAmplitude { get; set; } = 0.25f;

	[JsonPropertyName("ImpulseDuration")]
	[SchemaDescription("How long the shake from a hit lasts.")]
	[SchemaUnit("s")]
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
			MaskRegionMin = MaskRegionMin,
			MaskRegionMax = MaskRegionMax,
			SwayRegions = CloneSwayRegions(),
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

	private List<ProceduralSwayRegionConfig> CloneSwayRegions()
	{
		var clone = new List<ProceduralSwayRegionConfig>();
		if (SwayRegions == null) return clone;

		foreach (ProceduralSwayRegionConfig region in SwayRegions)
		{
			if (region != null) clone.Add(region.Clone());
		}

		return clone;
	}
}