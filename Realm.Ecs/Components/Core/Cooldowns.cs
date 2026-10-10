namespace Realm.Ecs.Components.Core;

/// <summary>
/// Represents the remaining cooldown durations for an entity's abilities.
/// </summary>
public record struct Cooldowns(Dictionary<string, float> Value);
