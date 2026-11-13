using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("How a single particle is drawn.")]
public enum SpellParticleRenderMode
{
	[SchemaDescription("The particle is drawn as a flat quad that always faces the camera, used for sparks, smoke and glows.")]
	BillboardQuad,

	[SchemaDescription("The particle is drawn as a mesh, used for debris that should tumble and catch the light.")]
	Mesh
}
