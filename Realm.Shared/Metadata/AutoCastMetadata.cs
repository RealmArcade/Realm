using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("The conditions under which a unit casts an ability on its own, and how much mana it keeps back while doing so.")]
public class AutoCastMetadata
{
	[SchemaDescription("Whether the unit casts the ability on its own at all.")]
	public bool Enabled { get; set; } = true;

	[SchemaDescription("Who the unit picks when it casts on its own.")]
	public AutoCastTargetRule TargetRule { get; set; } = AutoCastTargetRule.NearestEnemy;

	[SchemaDescription("How wounded a friendly unit has to be before a healing auto cast picks it, as a fraction of its maximum health.")]
	[SchemaRange(0, 1)]
	public float AllyHealthPercentBelow { get; set; } = 1f;

	[SchemaDescription("How much mana the unit keeps in reserve, and therefore never spends on auto cast.")]
	public float ManaReserve { get; set; }

	[SchemaDescription("The share of the unit's maximum mana below which it stops casting on its own, as a fraction of one.")]
	[SchemaRange(0, 1)]
	public float MinManaPercent { get; set; }
}
