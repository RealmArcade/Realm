namespace Realm.Shared.Metadata;

public class VfxStageMetadata
{
	public string EffectId { get; set; } = string.Empty;
	public VfxStageTrigger Trigger { get; set; } = VfxStageTrigger.Immediately;
	public float DelaySeconds { get; set; }
	public float DurationSeconds { get; set; }
	public float Scale { get; set; } = 1.0f;
	public bool SingleInstance { get; set; }
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
