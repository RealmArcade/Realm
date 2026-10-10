namespace Realm.Ecs.Components.Core;

/// <summary>
///     Tracks cooldown timers for commander abilities/spells.
/// </summary>
public record struct SpellCooldowns(Dictionary<string, float> Value);
