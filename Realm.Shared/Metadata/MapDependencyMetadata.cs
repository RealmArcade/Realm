using System.Text.Json;
using System.Text.Json.Serialization;

namespace Realm.Shared.Metadata;

public class MapDependencyMetadata
{
	public string Id { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Version { get; set; } = "1.0.0";
	public string? Hash { get; set; }
	public bool IsOptional { get; set; }
	public string? Url { get; set; }

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}