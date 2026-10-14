namespace Realm.Ecs.Components.Abilities;

/// <summary>
///     Defines the core properties of an ability.
/// </summary>
public record struct Ability(float Cooldown, float CurrentCooldown = 0);