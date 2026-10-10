using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Stats;

/// <summary>
///     Represents the Health stat.
/// </summary>
[StatDefinition("Health", "Health", "The entity's maximum hit points.")]
public readonly record struct HealthStat;