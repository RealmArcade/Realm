using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("How a visual effect is placed against the world when it is spawned.")]
public enum VfxPlacementMode
{
	[SchemaDescription("The effect is stuck to the surface it lands on and follows that surface's angle.")]
	SurfaceSnap,

	[SchemaDescription("The effect is placed exactly where it is spawned, whatever it is near.")]
	Free,

	[SchemaDescription("The effect is dropped onto the ground below where it is spawned and kept flat.")]
	GroundProjected
}
