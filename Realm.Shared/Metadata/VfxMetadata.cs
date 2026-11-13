using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("One animated sprite sheet effect: how its frames are laid out, how fast they play and the texture they are cut from.")]
public class VfxMetadata
{
	[SchemaDescription("How many frames the sheet holds across.")]
	public int Columns { get; set; } = 1;

	[SchemaDescription("How many frames the sheet holds down.")]
	public int Rows { get; set; } = 1;

	[SchemaDescription("How many frames the sheet plays per second.")]
	public float Fps { get; set; } = 20.0f;

	[SchemaDescription("Whether two neighbouring frames are blended while the effect plays, so a fast sheet does not read as a slide show.")]
	public bool SubframeBlend { get; set; }

	[SchemaDescription("Path to the sprite sheet texture the frames are cut from.")]
	[SchemaAsset(new[] { "rtex" }, new[] { "Assets/vfx_spritesheets" })]
	public string? TexturePath { get; set; }

	[SchemaDescription("The map the effect was imported from, kept so attribution survives a round trip through the editor.")]
	public string? Attribution_ImportedFromMap { get; set; }
}
