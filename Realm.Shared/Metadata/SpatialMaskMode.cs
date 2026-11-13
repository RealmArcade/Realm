using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("How a procedural animation decides which parts of a model move.")]
public enum SpatialMaskMode
{
	[SchemaDescription("Vertices move according to how high they sit, so an upper hem moves and a lower one stays.")]
	HeightGradient = 0,

	[SchemaDescription("Vertices move according to how far they sit from the model's vertical axis, so outer edges move most.")]
	RadialAxis = 1,

	[SchemaDescription("Vertices move according to which way they face, so surfaces pointing one way move and others stay.")]
	NormalFacing = 2,

	[SchemaDescription("Only vertices inside a declared region move, which is how a named sway region overrides the mask.")]
	RegionBox = 3
}
