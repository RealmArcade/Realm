using System.Text.Json.Serialization;
using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("Another published map this map builds on, pinned to the version and content hash its author expects.")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class MapDependencyMetadata
{
	[SchemaDescription("The identifier the dependency is published under.")]
	[SchemaSensitive]
	public string Id { get; set; } = string.Empty;

	[SchemaDescription("The name the dependency is listed under, shown to the author.")]
	public string Name { get; set; } = string.Empty;

	[SchemaDescription("The exact version of the dependency this map was authored against.")]
	[SchemaSensitive]
	public string Version { get; set; } = "1.0.0";

	[SchemaDescription("The content hash of the dependency this map was authored against, so the same bytes are merged.")]
	[SchemaSensitive]
	public string? Hash { get; set; }

	[SchemaDescription("Whether the map still loads when the dependency cannot be found.")]
	public bool IsOptional { get; set; }

	[SchemaDescription("Where the dependency may be downloaded from when it is not already installed.")]
	public string? Url { get; set; }
}
