using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("A decorative piece of scenery the map places for looks or to block movement, such as a rock, a fence or a ruin.")]
public class PropMetadata : PlaceableMetadata
{
	[SchemaDescription("The multiplier applied to the prop's model size, which a rock cluster or a fence post uses to sit at the right height.")]
	[SchemaUnit("x")]
	public override float Scale { get; set; } = 1.25f;
}
