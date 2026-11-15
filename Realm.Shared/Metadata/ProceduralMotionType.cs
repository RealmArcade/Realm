using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("The kind of motion a procedural animation profile applies to a model, which decides which of its settings are used.")]
public enum ProceduralMotionType
{
	[SchemaDescription("Cloth and banners sway with the wind and lag behind the model's movement.")]
	ClothSway = 1,

	[SchemaDescription("Foliage and grass bend with the wind and flutter in the gusts.")]
	FoliageWind = 2,

	[SchemaDescription("Wings flap in a repeating stroke, used for birds and flying units.")]
	WingFlap = 3,

	[SchemaDescription("The model shakes briefly when it is struck or lands.")]
	TransientShake = 4
}
