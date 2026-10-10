namespace Realm.Ecs.Components.Combat;

/// <summary>
///     Component holding multiple weapon slots for an entity.
/// </summary>
public record struct Weapons(List<Weapon> Slots);
