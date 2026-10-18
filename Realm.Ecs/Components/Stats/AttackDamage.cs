using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Stats;

/// <summary>
///     Represents the Attack Damage stat.
/// </summary>
[StatDefinition("Attack", "Attack Damage", "The base damage dealt by attacks.")]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct AttackDamage;