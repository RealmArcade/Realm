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
}
