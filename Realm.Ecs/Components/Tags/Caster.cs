using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Tags;

/// <summary>
///     Tag indicating the entity can cast spells or abilities.
/// </summary>
[TagDefinition("Caster", "Caster", "Indicates the entity can cast spells or abilities.")]
public readonly record struct Caster;