using Realm.Shared.Serialization;

namespace Realm.Shared.Terrain;

[SchemaDescription("One unit or building the map places on the battlefield when a match starts, with where it stands and whose it is.")]
public class UnitSaveData
{
	[SchemaDescription("The unit or building template placed here.")]
	[SchemaReference("Units", "Buildings")]
	public string TemplateId { get; set; } = string.Empty;

	[SchemaDescription("Where the unit stands across the map.")]
	[SchemaUnit("m")]
	public float PosX { get; set; }

	[SchemaDescription("How high above the ground the unit stands.")]
	[SchemaUnit("m")]
	public float PosY { get; set; }

	[SchemaDescription("Where the unit stands along the map.")]
	[SchemaUnit("m")]
	public float PosZ { get; set; }

	[SchemaDescription("Which way the unit faces.")]
	[SchemaUnit("°")]
	public float RotationY { get; set; }

	[SchemaDescription("The multiplier applied to the unit's model size for this placement.")]
	public float Scale { get; set; } = 1.0f;

	[SchemaDescription("The slot the unit belongs to, which decides the player whose colour it wears.")]
	public int Player { get; set; }
}
