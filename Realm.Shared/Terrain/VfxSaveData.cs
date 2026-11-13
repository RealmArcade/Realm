using Realm.Shared.Metadata;
using Realm.Shared.Serialization;

namespace Realm.Shared.Terrain;

[SchemaDescription("One visual effect the map places in the world, with where it sits, how it is turned and scaled, and the configuration this placement uses.")]
public class VfxSaveData
{
	[SchemaDescription("The visual effect template placed here.")]
	[SchemaReference("Vfx")]
	public string VfxId { get; set; } = string.Empty;

	[SchemaDescription("Where the effect sits across the map.")]
	[SchemaUnit("m")]
	public float PosX { get; set; }

	[SchemaDescription("How high above the ground the effect sits.")]
	[SchemaUnit("m")]
	public float PosY { get; set; }

	[SchemaDescription("Where the effect sits along the map.")]
	[SchemaUnit("m")]
	public float PosZ { get; set; }

	[SchemaDescription("How far the effect is tipped over its sideways axis.")]
	[SchemaUnit("°")]
	public float RotationX { get; set; }

	[SchemaDescription("How far the effect is turned around the vertical axis.")]
	[SchemaUnit("°")]
	public float RotationY { get; set; }

	[SchemaDescription("How far the effect is rolled over its forward axis.")]
	[SchemaUnit("°")]
	public float RotationZ { get; set; }

	[SchemaDescription("The multiplier applied to the effect's width for this placement.")]
	public float ScaleX { get; set; } = 1.0f;

	[SchemaDescription("The multiplier applied to the effect's height for this placement.")]
	public float ScaleY { get; set; } = 1.0f;

	[SchemaDescription("The multiplier applied to the effect's depth for this placement.")]
	public float ScaleZ { get; set; } = 1.0f;

	[SchemaDescription("How far the effect is lifted along the surface it sits on, so it does not sink into it.")]
	[SchemaUnit("m")]
	public float NormalOffset { get; set; }

	[SchemaDescription("The configuration this placement spawns the effect with, when it should differ from the template it names.")]
	public VfxAttachmentConfig? Config { get; set; }
}
