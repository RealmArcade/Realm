using System.Text.Json.Serialization;
using Realm.Shared.Serialization;

namespace Realm.Shared.Terrain;

[SchemaDescription("One prop the map places on the battlefield, with where it stands and which way it faces.")]
public class PropSaveData
{
	[SchemaDescription("The prop template placed here.")]
	[SchemaReference("Props")]
	public string TemplateId { get; set; } = string.Empty;

	[SchemaDescription("Where the prop stands across the map.")]
	[SchemaUnit("m")]
	public float PosX { get; set; }

	[SchemaDescription("How high above the ground the prop stands.")]
	[SchemaUnit("m")]
	public float PosY { get; set; }

	[SchemaDescription("Where the prop stands along the map.")]
	[SchemaUnit("m")]
	public float PosZ { get; set; }

	[SchemaDescription("Which way the prop faces.")]
	[SchemaUnit("°")]
	public float RotationY { get; set; }

	[SchemaDescription("The multiplier applied to the prop's model size for this placement.")]
	public float Scale { get; set; } = 1.0f;

	[SchemaDescription("The level this prop starts at, which sets its capacities and resistances. A level of 0 keeps the template's own level.")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int Level { get; set; }
}
