using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Stats;

/// <summary>
///     Represents the Armor stat.
/// </summary>
[StatDefinition("Armor", "Armor", "Reduces incoming physical damage.")]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct ArmorStat;