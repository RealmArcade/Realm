using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("Where a procedural animation's sway region came from, which tells an author whether their own region or a fallback is in use.")]
public enum ProceduralSwayRegionSource
{
	[SchemaDescription("The region was declared on the model by the map author.")]
	DeclaredModelRegion = 0,

	[SchemaDescription("No region was declared, so one was derived from the motion type.")]
	MotionTypeFallback = 1,

	[SchemaDescription("No region was declared, so the profile's own mask was used.")]
	PresetMask = 2
}
