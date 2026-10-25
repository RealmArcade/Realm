namespace Realm.Shared.Metadata;

/// <summary>
/// Attribute gain per level. Level 1 is the authored base, so a unit at level N receives
/// (N - 1) multiples of every value here.
/// </summary>
public class LevelGrowthMetadata
{
	public float StrengthPerLevel { get; set; }
	public float AgilityPerLevel { get; set; }
	public float VitalityPerLevel { get; set; }
	public float IntelligencePerLevel { get; set; }
	public float WisdomPerLevel { get; set; }
	public float FortunePerLevel { get; set; }
}
