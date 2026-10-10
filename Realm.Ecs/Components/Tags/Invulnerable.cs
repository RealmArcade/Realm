using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Tags;

/// <summary>
///     Tag indicating the entity is invulnerable to damage.
/// </summary>
[TagDefinition("Invulnerable", "Invulnerable", "Marks the entity as invulnerable to damage.")]
public readonly record struct Invulnerable;