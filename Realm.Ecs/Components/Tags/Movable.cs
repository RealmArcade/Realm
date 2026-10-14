using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Tags;

/// <summary>
///     Tag indicating the entity is capable of movement.
/// </summary>
[TagDefinition("Movable", "Movable", "Grants the entity the ability to move across the map.")]
public readonly record struct Movable;