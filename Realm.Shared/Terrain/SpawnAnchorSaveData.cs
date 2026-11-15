using Realm.Shared.Serialization;

namespace Realm.Shared.Terrain;

[SchemaDescription("Where one player's units start a match on this map, named after the coordinate that holds the starting area.")]
public class SpawnAnchorSaveData
{
	[SchemaDescription("The slot whose starting area this anchor is.")]
	public int Player { get; set; }

	[SchemaDescription("The coordinate that holds the slot's starting area.")]
	public string CoordinateName { get; set; } = string.Empty;
}
