using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Tags;

/// <summary>
///     Tag indicating the entity can be attacked.
/// </summary>
[TagDefinition("Attackable", "Attackable", "Allows the entity to be targeted and damaged by attacks.")]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct Attackable;