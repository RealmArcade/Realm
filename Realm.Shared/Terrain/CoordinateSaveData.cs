using Realm.Shared.Serialization;

namespace Realm.Shared.Terrain;

[SchemaDescription("One named rectangle of the battlefield, such as a start location or a capture zone, that a map's script and slots refer to by name.")]
public class CoordinateSaveData
{
	[SchemaDescription("The name the map's slots and script refer to this coordinate by.")]
	[SchemaSensitive]
	public string Name { get; set; } = string.Empty;

	[SchemaDescription("The west edge of the coordinate's rectangle.")]
	[SchemaUnit("m")]
	public float MinX { get; set; }

	[SchemaDescription("The north edge of the coordinate's rectangle.")]
	[SchemaUnit("m")]
	public float MinZ { get; set; }

	[SchemaDescription("The east edge of the coordinate's rectangle.")]
	[SchemaUnit("m")]
	public float MaxX { get; set; }

	[SchemaDescription("The south edge of the coordinate's rectangle.")]
	[SchemaUnit("m")]
	public float MaxZ { get; set; }
}
