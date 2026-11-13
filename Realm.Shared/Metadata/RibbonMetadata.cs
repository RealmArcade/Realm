using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("One ribbon trail texture a weapon can drag behind its projectiles.")]
public class RibbonMetadata
{
	[SchemaDescription("Path to the ribbon texture the trail is drawn with.")]
	[SchemaAsset(new[] { "rtex" }, new[] { "Assets/ribbons" })]
	public string? TexturePath { get; set; }

	[SchemaDescription("The map the ribbon was imported from, kept so attribution survives a round trip through the editor.")]
	public string? Attribution_ImportedFromMap { get; set; }
}
