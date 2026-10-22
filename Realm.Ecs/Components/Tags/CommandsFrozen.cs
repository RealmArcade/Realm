using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Tags;

/// <summary>
///     Tag indicating the entity ignores movement, attack and hold orders while abilities stay available.
/// </summary>
[TagDefinition("CommandsFrozen", "Commands Frozen", "Marks the entity as ignoring movement, attack and hold orders while abilities stay available.")]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct CommandsFrozen;
