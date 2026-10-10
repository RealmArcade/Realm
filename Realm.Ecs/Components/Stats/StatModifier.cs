using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Stats;

/// <summary>
///     A component that modifies a stat on an entity.
/// </summary>
public record struct StatModifier(StatId StatTypeId, ModifierType Type, float Value, float Duration = -1);