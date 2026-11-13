using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("The shape a particle emitter spawns its particles inside.")]
public enum SpellParticleShape
{
	[SchemaDescription("Every particle leaves one point, which makes a tight burst.")]
	Point,

	[SchemaDescription("Particles are spawned anywhere inside a sphere of the stated radius.")]
	Sphere,

	[SchemaDescription("Particles are spawned anywhere inside a box of the stated extents.")]
	Box,

	[SchemaDescription("Particles are spawned around a ring, which makes an expanding shockwave of particles.")]
	Ring
}
