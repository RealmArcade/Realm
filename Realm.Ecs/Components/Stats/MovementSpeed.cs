using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Stats;

/// <summary>
///     Represents the Movement Speed stat.
/// </summary>
[StatDefinition("MovementSpeed", "Movement Speed", "The speed at which the entity moves.")]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct MovementSpeed;