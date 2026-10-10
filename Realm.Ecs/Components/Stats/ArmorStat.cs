using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Stats;

/// <summary>
///     Represents the Armor stat.
/// </summary>
[StatDefinition("Armor", "Armor", "Reduces incoming physical damage.")]
public readonly record struct ArmorStat;