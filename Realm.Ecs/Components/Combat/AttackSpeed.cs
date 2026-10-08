namespace Realm.Ecs.Components.Combat;

/// <summary>
///     Scales an entity's attack rate. A multiplier of 1 uses the base attack cooldown, 2 attacks twice as often.
/// </summary>
internal record struct AttackSpeed(float Multiplier);
