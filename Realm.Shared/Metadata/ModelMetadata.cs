using System.Text.Json;
using System.Text.Json.Serialization;

namespace Realm.Shared.Metadata;

public class ModelMetadata
{
	[JsonPropertyName("Brightness")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public float? Brightness { get; set; }

	[JsonPropertyName("CollisionCircleRatios")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public float? CollisionCircleRatios { get; set; }

	[JsonPropertyName("ColorTint")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ColorTint { get; set; }

	[JsonPropertyName("DespillPlayerColor")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? DespillPlayerColor { get; set; }

	[JsonPropertyName("IgnorePlayerColor")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? IgnorePlayerColor { get; set; }

	[JsonPropertyName("NormalizeLuminance")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? NormalizeLuminance { get; set; }

	[JsonPropertyName("ObstacleRadii")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public float? ObstacleRadii { get; set; }

	[JsonPropertyName("Offsets")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public float? Offsets { get; set; }

	[JsonPropertyName("Scales")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public float? Scales { get; set; }

	[JsonPropertyName("SpawnShaders")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? SpawnShaders { get; set; }

	[JsonPropertyName("DeathShaders")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? DeathShaders { get; set; }

	[JsonPropertyName("ProceduralAnimation")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ProceduralAnimation { get; set; }

	[JsonPropertyName("EnableProceduralAnimation")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? EnableProceduralAnimation { get; set; }

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}