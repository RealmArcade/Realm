using Realm.Shared.Serialization;
using Realm.Shared.Terrain;

namespace Realm.Shared.Metadata;

[SchemaDescription("One animated sprite sheet icon drawn over a unit, such as a status marker, a level badge or a hero's overhead crest.")]
public class BillboardIconMetadata
{
	[SchemaDescription("Path to the sprite sheet the icon's frames are cut from.")]
	[SchemaAsset(new[] { "rtex" }, new[] { "Assets/icons", "Assets/vfx_spritesheets" })]
	public string TexturePath { get; set; } = string.Empty;

	[SchemaDescription("How many frames the sheet holds across.")]
	public int Columns { get; set; } = 1;

	[SchemaDescription("How many frames the sheet holds down.")]
	public int Rows { get; set; } = 1;

	[SchemaDescription("How many frames the icon plays per second.")]
	public float FramesPerSecond { get; set; } = 12.0f;

	[SchemaDescription("Whether the icon keeps looping after its last frame, instead of stopping on it.")]
	public bool Loop { get; set; } = true;

	[SchemaDescription("The socket of the unit's model the icon is parented to.")]
	public string Socket { get; set; } = "Overhead";

	[SchemaDescription("Where the icon sits relative to the socket it is parented to.")]
	public Vector3Data PositionOffset { get; set; }

	[SchemaDescription("How large the icon is drawn in the world, in metres.")]
	[SchemaUnit("m")]
	public float WorldHeight { get; set; } = 1.0f;

	[SchemaDescription("Whether the icon keeps the same size on screen however far the camera is, so a distant badge stays readable.")]
	public bool KeepConstantScreenSize { get; set; } = true;

	[SchemaDescription("Whether the icon is drawn through the models in front of it, so it never disappears behind a building.")]
	public bool NoDepthTest { get; set; } = false;

	public BillboardIconMetadata Clone()
	{
		return new BillboardIconMetadata
		{
			TexturePath = TexturePath,
			Columns = Columns,
			Rows = Rows,
			FramesPerSecond = FramesPerSecond,
			Loop = Loop,
			Socket = Socket,
			PositionOffset = PositionOffset,
			WorldHeight = WorldHeight,
			KeepConstantScreenSize = KeepConstantScreenSize,
			NoDepthTest = NoDepthTest
		};
	}
}
