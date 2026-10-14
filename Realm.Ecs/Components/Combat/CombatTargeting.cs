namespace Realm.Ecs.Components.Combat;

/// <summary>
///     Defines which target types an entity's attacks can affect.
/// </summary>
public record struct CombatTargeting(bool CanTargetAir, bool CanTargetGround);