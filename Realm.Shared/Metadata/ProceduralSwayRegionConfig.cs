using System.Text.Json.Serialization;
using Realm.Shared.Serialization;
using Realm.Shared.Terrain;

namespace Realm.Shared.Metadata;

[SchemaDescription("One named region of a model that sways on its own, such as a single banner or one branch, with its own falloff and mask.")]
public class ProceduralSwayRegionConfig
{
	[JsonPropertyName("ModelKey")]
	[SchemaDescription("The model the region belongs to, which lets one profile hold regions for several models.")]
	[SchemaSensitive]
	public string ModelKey { get; set; } = string.Empty;

	[JsonPropertyName("RegionName")]
	[SchemaDescription("The name the model's own region declarations are matched against.")]
	public string RegionName { get; set; } = string.Empty;

	[JsonPropertyName("NormalizedMin")]
	[SchemaDescription("The lower corner of the region, in the model's normalised space where zero is the model's low extreme and one is its high extreme.")]
	public Vector3Data NormalizedMin { get; set; } = new(0.0f, 0.5f, 0.0f);

	[JsonPropertyName("NormalizedMax")]
	[SchemaDescription("The upper corner of the region, in the model's normalised space where zero is the model's low extreme and one is its high extreme.")]
	public Vector3Data NormalizedMax { get; set; } = new(1.0f, 1.0f, 1.0f);

	[JsonPropertyName("Feather")]
	[SchemaDescription("How softly the region's edge is blended, so motion fades out instead of stopping at a seam.")]
	public float Feather { get; set; } = 0.15f;

	[JsonPropertyName("MaskPower")]
	[SchemaDescription("How sharply the region's mask falls off from its centre to its edge.")]
	public float MaskPower { get; set; } = 1.0f;

	[JsonPropertyName("MaskInvert")]
	[SchemaDescription("Whether the region's mask is inverted, so everything outside the region sways instead of everything inside it.")]
	public bool MaskInvert { get; set; } = false;

	public ProceduralSwayRegionConfig Clone()
	{
		return new ProceduralSwayRegionConfig
		{
			ModelKey = ModelKey,
			RegionName = RegionName,
			NormalizedMin = NormalizedMin,
			NormalizedMax = NormalizedMax,
			Feather = Feather,
			MaskPower = MaskPower,
			MaskInvert = MaskInvert
		};
	}
}
