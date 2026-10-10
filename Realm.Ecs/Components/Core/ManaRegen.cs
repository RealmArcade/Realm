namespace Realm.Ecs.Components.Core;

/// <summary>
///     Mana points restored per second for an entity that has a <see cref="Mana" /> component.
/// </summary>
public record struct ManaRegen(float PerSecond);
