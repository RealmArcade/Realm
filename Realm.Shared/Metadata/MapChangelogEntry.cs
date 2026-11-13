using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("One entry of a map's changelog: which version changed, when, and what changed in it.")]
public class MapChangelogEntry
{
	[SchemaDescription("The version this entry describes.")]
	[SchemaSensitive]
	public string Version { get; set; } = string.Empty;

	[SchemaDescription("The date the version was published, written the way the author wants it shown.")]
	public string Date { get; set; } = string.Empty;

	[SchemaDescription("What changed in this version, in the author's own words.")]
	[SchemaControl(RealmSchemaControl.Textarea)]
	public string Details { get; set; } = string.Empty;
}
