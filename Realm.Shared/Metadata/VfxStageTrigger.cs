using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("The moment in a visual effect's life at which one of its stages is played.")]
public enum VfxStageTrigger
{
	[SchemaDescription("The stage plays the moment the effect is spawned.")]
	Immediately,

	[SchemaDescription("The stage plays once the delay its stage entry states has passed.")]
	AfterDelay,

	[SchemaDescription("The stage plays when the effect's projectile lands.")]
	OnImpact,

	[SchemaDescription("The stage plays when the unit the effect is attached to dies.")]
	OnTargetDeath
}
