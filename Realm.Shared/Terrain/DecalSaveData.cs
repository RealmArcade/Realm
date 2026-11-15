using Realm.Shared.Serialization;

namespace Realm.Shared.Terrain;

[SchemaDescription("One decal the map projects onto the ground, with where it sits and which way it is turned.")]
public class DecalSaveData
{
	[SchemaDescription("The decal template placed here.")]
	[SchemaDictionaryReference("decals")]
	public string TemplateId { get; set; } = string.Empty;

	[SchemaDescription("Where the decal sits across the map.")]
	[SchemaUnit("m")]
	public float PosX { get; set; }

	[SchemaDescription("How high above the ground the decal sits.")]
	[SchemaUnit("m")]
	public float PosY { get; set; }

	[SchemaDescription("Where the decal sits along the map.")]
	[SchemaUnit("m")]
	public float PosZ { get; set; }

	[SchemaDescription("How far the decal is tipped over its sideways axis.")]
	[SchemaUnit("°")]
	public float RotationX { get; set; }

	[SchemaDescription("How far the decal is turned around the vertical axis.")]
	[SchemaUnit("°")]
	public float RotationY { get; set; }

	[SchemaDescription("How far the decal is rolled over its forward axis.")]
	[SchemaUnit("°")]
	public float RotationZ { get; set; }

	[SchemaDescription("The multiplier applied to the decal's size for this placement.")]
	public float Scale { get; set; } = 1.0f;
}
