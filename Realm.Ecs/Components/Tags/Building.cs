using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Tags;

/// <summary>
///     Tag indicating the entity is a static building.
/// </summary>
[TagDefinition("Building", "Building", "Marks the entity as a static structure.")]
public readonly record struct Building;