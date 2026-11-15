using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("The shape a visual effect is drawn with, which decides how its texture is wrapped and how it moves over its life.")]
public enum VfxPrimitiveType
{
	[SchemaDescription("A flat disc that spins, used for ground auras and vortex effects.")]
	VortexDisc,

	[SchemaDescription("A cone opening upwards, used for funnels and rising columns of energy.")]
	FunnelCone,

	[SchemaDescription("A ring of ribbon geometry, used for shockwaves that expand outwards.")]
	RibbonRing,

	[SchemaDescription("A dome over the ground, used for barriers and area shields.")]
	HemisphereDome,

	[SchemaDescription("A flat plane lying on the ground, used for decals and ground-projected effects.")]
	GroundPlane,

	[SchemaDescription("A thin fin attached to a weapon, used for blade trails during a swing.")]
	WeaponFin,

	[SchemaDescription("Two planes crossed through each other, used for an effect that has to read from any angle.")]
	CrossQuad,

	[SchemaDescription("A curved arc, used for a slash that follows a swing.")]
	SlashArc,

	[SchemaDescription("A shaft of light rising from the ground, used for beacons and beams.")]
	LightShaft,

	[SchemaDescription("A capsule wrapped around a unit, used for auras that follow their caster.")]
	AuraCapsule,

	[SchemaDescription("A sphere around a unit, used for auras that read the same from every side.")]
	AuraSphere,

	[SchemaDescription("A box projected onto surfaces, used for effects that stretch over terrain.")]
	ProjectedVolumeCube,

	[SchemaDescription("A real particle system rather than wrapped geometry, used for effects fired from an emitter.")]
	ParticleSystem
}
