namespace Realm.Ecs.Components.Combat;

/// <summary>
///     Component holding multiple weapon slots for an entity.
/// </summary>
internal record struct Weapons(List<Weapon> Slots);
