using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Tags;

/// <summary>
///     Tag indicating the entity is a static or destructible world object.
/// </summary>
[TagDefinition("Prop", "Prop", "Marks the entity as a static or destructible world object.")]
public readonly record struct Prop;