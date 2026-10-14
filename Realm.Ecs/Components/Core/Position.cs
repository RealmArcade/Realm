using System.Numerics;

namespace Realm.Ecs.Components.Core;

/// <summary>
///     Represents the position of an entity in the game world.
/// </summary>
public record struct Position(Vector3 Value);