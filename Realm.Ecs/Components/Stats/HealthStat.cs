using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Stats;

/// <summary>
///     Represents the Health stat.
/// </summary>
[StatDefinition("Health", "Health", "The entity's maximum hit points.")]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct HealthStat;