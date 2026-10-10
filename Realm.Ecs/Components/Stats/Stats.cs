using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Stats;

/// <summary>
///     A component holding all base stat values for an entity.
/// </summary>
public record struct Stats(Dictionary<StatId, float> Value);