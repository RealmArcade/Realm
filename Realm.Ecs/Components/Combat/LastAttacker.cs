namespace Realm.Ecs.Components.Combat;

/// <summary>
///     Represents the entity that last attacked/damaged this entity.
/// </summary>
public record struct LastAttacker(Arch.Core.Entity Value);
