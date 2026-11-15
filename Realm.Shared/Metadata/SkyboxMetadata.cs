using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("One skybox the map offers as a background, drawn behind the whole battlefield.")]
public class SkyboxMetadata
{
	[SchemaDescription("Path to the skybox texture.")]
	[SchemaAsset(new[] { "rtex" }, new[] { "Assets/skyboxes" })]
	public string? TexturePath { get; set; }

	[SchemaDescription("The map the skybox was imported from, kept so attribution survives a round trip through the editor.")]
	public string? Attribution_ImportedFromMap { get; set; }
}
