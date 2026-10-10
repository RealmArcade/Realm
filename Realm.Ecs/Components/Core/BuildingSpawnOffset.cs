using System.Numerics;

namespace Realm.Ecs.Components.Core;

/// <summary>
///     Stores the world-space forward offset from the building's origin at which newly
///     trained units should be spawned. Set once when the building entity is created so
///     that production logic can derive spawn positions purely from ECS data without
///     touching any Godot scene nodes.
/// </summary>
public record struct BuildingSpawnOffset(Vector3 Value);
