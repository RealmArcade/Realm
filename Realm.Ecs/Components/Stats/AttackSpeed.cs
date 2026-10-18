using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Stats;

/// <summary>
///     Represents the Attack Speed stat.
/// </summary>
[StatDefinition("AttackSpeed", "Attack Speed", "The speed at which attacks are performed.")]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct AttackSpeed;