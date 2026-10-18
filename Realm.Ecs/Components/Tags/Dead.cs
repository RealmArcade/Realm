using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Tags;

/// <summary>
///     Tag indicating the entity is dead and awaiting cleanup.
/// </summary>
[TagDefinition("Dead", "Dead", "Marks the entity as defeated, awaiting cleanup.")]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct Dead;