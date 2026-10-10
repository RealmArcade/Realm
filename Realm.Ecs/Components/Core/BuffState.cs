namespace Realm.Ecs.Components.Core;

/// <summary>
/// Represents the active buffs on an entity.
/// </summary>
public record struct BuffState(Dictionary<string, float> Value);
