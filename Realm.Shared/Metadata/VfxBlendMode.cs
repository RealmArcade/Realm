using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("How a visual effect is mixed with what is behind it.")]
public enum VfxBlendMode
{
	[SchemaDescription("The effect adds its colour to what is behind it, which makes bright effects glow and hides their dark areas.")]
	Additive,

	[SchemaDescription("The effect blends over what is behind it by its own alpha, which lets it darken as well as brighten.")]
	AlphaBlend
}
