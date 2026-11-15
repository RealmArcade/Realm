using System.Text.Json.Serialization;
using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

/// <summary>
///     Who a unit picks when it casts an ability on its own.
/// </summary>
[SchemaDescription("Who a unit picks when it casts an ability on its own.")]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AutoCastTargetRule
{
	[SchemaDescription("The caster itself, for a spell that buffs or heals the unit that cast it.")]
	Self,

	[SchemaDescription("The closest enemy the caster can reach with the ability.")]
	NearestEnemy,

	[SchemaDescription("The friendly unit that has lost the largest share of its health.")]
	MostWoundedAlly,

	[SchemaDescription("The friendly unit with the least health left in absolute terms.")]
	LowestHealthAlly,

	[SchemaDescription("A friendly unit that does not already carry the effect the ability applies.")]
	AllyWithoutEffect,
}
