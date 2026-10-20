using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Tags;

/// <summary>
///     Tag indicating the entity is a primary game unit.
/// </summary>
[TagDefinition("Unit", "Unit", "Marks the entity as a primary game unit.")]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct Unit;