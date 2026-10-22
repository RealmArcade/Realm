using System.Collections.Generic;
using System.Text.Json.Serialization;
using Realm.Shared.Terrain;

namespace Realm.Shared.Metadata;

public class ProceduralSwayRegionConfig
{
	[JsonPropertyName("ModelKey")] public string ModelKey { get; set; } = string.Empty;

	[JsonPropertyName("RegionName")] public string RegionName { get; set; } = string.Empty;

	[JsonPropertyName("NormalizedMin")] public Vector3Data NormalizedMin { get; set; } = new(0.0f, 0.5f, 0.0f);

	[JsonPropertyName("NormalizedMax")] public Vector3Data NormalizedMax { get; set; } = new(1.0f, 1.0f, 1.0f);

	[JsonPropertyName("Feather")] public float Feather { get; set; } = 0.15f;

	[JsonPropertyName("MaskPower")] public float MaskPower { get; set; } = 1.0f;

	[JsonPropertyName("MaskInvert")] public bool MaskInvert { get; set; } = false;

	[JsonPropertyName("MeshNamePatterns")] public List<string> MeshNamePatterns { get; set; } = new();

	[JsonPropertyName("MaterialNamePatterns")] public List<string> MaterialNamePatterns { get; set; } = new();

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
			MaskInvert = MaskInvert,
			MeshNamePatterns = new List<string>(MeshNamePatterns ?? new List<string>()),
			MaterialNamePatterns = new List<string>(MaterialNamePatterns ?? new List<string>())
		};
	}
}
