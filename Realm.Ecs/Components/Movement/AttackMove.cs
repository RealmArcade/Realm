using System.Numerics;

namespace Realm.Ecs.Components.Movement;

/// <summary>
///     Signals that an entity is moving to a destination while attacking enemies on the way.
/// </summary>
public record struct AttackMove(Vector3 Target);
