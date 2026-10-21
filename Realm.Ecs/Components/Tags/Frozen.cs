using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Tags;

/// <summary>
///     Tag indicating the entity is pinned in place with its animation suspended.
/// </summary>
[TagDefinition("Frozen", "Frozen", "Marks the entity as frozen: it never moves, acquires targets or animates.")]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct Frozen;
