using Realm.Ecs.Services;

namespace Realm.Shared.Metadata;

public class LevelGrowthMetadata
{
	public float StrengthPerLevel { get; set; }
	public float AgilityPerLevel { get; set; }
	public float VitalityPerLevel { get; set; }
	public float IntelligencePerLevel { get; set; }
	public float WisdomPerLevel { get; set; }
	public float FortunePerLevel { get; set; }
	public float BountyMultiplierPerLevel { get; set; }

	public LevelGrowth ToLevelGrowth() => new(
		StrengthPerLevel,
		AgilityPerLevel,
		VitalityPerLevel,
		IntelligencePerLevel,
		WisdomPerLevel,
		FortunePerLevel,
		BountyMultiplierPerLevel);
}
