using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Tags;

/// <summary>
///     Tag indicating the entity is a unique, powerful hero unit.
/// </summary>
[TagDefinition("Hero", "Hero", "Marks the entity as a unique, powerful hero unit.")]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct Hero;