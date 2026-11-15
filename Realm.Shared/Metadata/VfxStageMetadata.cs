using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("One stage of a visual effect's life: which effect plays, when it starts, how long it lasts and how large it is drawn.")]
public class VfxStageMetadata
{
	[SchemaDescription("The visual effect the stage plays.")]
	[SchemaReference("Vfx")]
	public string EffectId { get; set; } = string.Empty;

	[SchemaDescription("The moment the stage starts.")]
	public VfxStageTrigger Trigger { get; set; } = VfxStageTrigger.Immediately;

	[SchemaDescription("How long after the trigger the stage starts.")]
	[SchemaUnit("s")]
	public float DelaySeconds { get; set; }

	[SchemaDescription("How long the stage lasts before it is cleaned up.")]
	[SchemaUnit("s")]
	public float DurationSeconds { get; set; }

	[SchemaDescription("The multiplier applied to the stage's size.")]
	public float Scale { get; set; } = 1.0f;

	[SchemaDescription("Whether only one of this stage may be alive at a time, so recasting replaces it instead of stacking another.")]
	public bool SingleInstance { get; set; }

	[SchemaDescription("The particles the stage emits, when it is a particle stage rather than a wrapped effect.")]
	public SpellParticleConfig? ParticleConfig { get; set; }

	public VfxStageMetadata Clone()
	{
		return new VfxStageMetadata
		{
			EffectId = EffectId,
			Trigger = Trigger,
			DelaySeconds = DelaySeconds,
			DurationSeconds = DurationSeconds,
			Scale = Scale,
			SingleInstance = SingleInstance,
			ParticleConfig = ParticleConfig?.Clone()
		};
	}
}
