using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Tags;

/// <summary>
///     Tag indicating the entity is immune to spell and magic-type attacks.
/// </summary>
[TagDefinition("SpellImmune", "SpellImmune", "Marks the entity as immune to spell and magic-type damage.")]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct SpellImmune;