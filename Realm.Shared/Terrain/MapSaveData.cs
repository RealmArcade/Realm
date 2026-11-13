using System.Text.Json.Serialization;
using Realm.Shared.Serialization;

namespace Realm.Shared.Terrain;

[SchemaDescription("The terrain of a map as the editor saves it: its size, the units, props, decals, items and effects placed on it, and the named coordinates and starting areas a match uses.")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class MapSaveData
{
	[SchemaDescription("The width of the terrain in cells.")]
	public int Width { get; set; }

	[SchemaDescription("The depth of the terrain in cells.")]
	public int Depth { get; set; }

	[SchemaDescription("The units and buildings the map places when a match starts.")]
	public List<UnitSaveData>? Units { get; set; }

	[SchemaDescription("The props the map places for scenery or to block movement.")]
	public List<PropSaveData>? Props { get; set; }

	[SchemaDescription("The decals the map projects onto the ground.")]
	public List<DecalSaveData>? Decals { get; set; }

	[SchemaDescription("The items the map leaves lying on the ground to be collected.")]
	public List<ItemSaveData>? Items { get; set; }

	[SchemaDescription("The visual effects the map places in the world.")]
	public List<VfxSaveData>? Vfx { get; set; }

	[SchemaDescription("The west edge of the area the camera may pan to.")]
	public float? CameraBoundsLeft { get; set; }

	[SchemaDescription("The east edge of the area the camera may pan to.")]
	public float? CameraBoundsRight { get; set; }

	[SchemaDescription("The north edge of the area the camera may pan to.")]
	public float? CameraBoundsTop { get; set; }

	[SchemaDescription("The south edge of the area the camera may pan to.")]
	public float? CameraBoundsBottom { get; set; }

	[SchemaDescription("Path to the skybox drawn behind this map's battlefield.")]
	[SchemaAsset(new[] { "rtex" }, new[] { "Assets/skyboxes" })]
	public string? SkyboxPath { get; set; }

	[SchemaDescription("The named rectangles of the battlefield a map's slots and script refer to.")]
	public List<CoordinateSaveData>? Coordinates { get; set; }

	[SchemaDescription("Where each player's units start a match on this map.")]
	public List<SpawnAnchorSaveData>? SpawnAnchors { get; set; }

	public static string GenerateJsonSchema()
	{
		return RealmJsonSchemaExporter.GenerateJsonSchema(typeof(MapSaveData));
	}
}
