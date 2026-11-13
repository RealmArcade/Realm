using Realm.Ecs.Services;
using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("How much each of a hero's attributes gains for every level it reaches.")]
public class LevelGrowthMetadata
{
	[SchemaDescription("How much strength the unit gains per level.")]
	public float StrengthPerLevel { get; set; }

	[SchemaDescription("How much agility the unit gains per level.")]
	public float AgilityPerLevel { get; set; }

	[SchemaDescription("How much vitality the unit gains per level.")]
	public float VitalityPerLevel { get; set; }

	[SchemaDescription("How much intelligence the unit gains per level.")]
	public float IntelligencePerLevel { get; set; }

	[SchemaDescription("How much wisdom the unit gains per level.")]
	public float WisdomPerLevel { get; set; }

	[SchemaDescription("How much fortune the unit gains per level.")]
	public float FortunePerLevel { get; set; }

	[SchemaDescription("How much the gold and experience a killer is paid for this unit grows per level.")]
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
