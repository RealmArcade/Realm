namespace Realm.Shared.Metadata;

public class AutoCastMetadata
{
	public bool Enabled { get; set; } = true;

	public AutoCastTargetRule TargetRule { get; set; } = AutoCastTargetRule.NearestEnemy;

	public float AllyHealthPercentBelow { get; set; } = 1f;

	public float ManaReserve { get; set; }

	public float MinManaPercent { get; set; }
}
