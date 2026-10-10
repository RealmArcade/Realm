namespace Realm.Ecs.Components.Core;

/// <summary>
/// Represents the active abilities configured on an entity.
/// </summary>
public record struct AbilityState(List<string> Abilities);
