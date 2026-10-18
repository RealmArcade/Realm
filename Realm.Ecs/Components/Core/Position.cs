using System.Runtime.InteropServices;
using System.Numerics;

namespace Realm.Ecs.Components.Core;

/// <summary>
///     Represents the position of an entity in the game world.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct Position(Vector3 Value);