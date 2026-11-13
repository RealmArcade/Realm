using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("One command card icon: the picture an ability, item or upgrade shows when it is offered to a player.")]
public class IconMetadata
{
	[SchemaDescription("Path to the icon image.")]
	[SchemaAsset(new[] { "rtex" }, new[] { "Assets/icons" })]
	public string? TexturePath { get; set; }

	[SchemaDescription("The map the icon was imported from, kept so attribution survives a round trip through the editor.")]
	public string? Attribution_ImportedFromMap { get; set; }
}
