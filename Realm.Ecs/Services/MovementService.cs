using System.Numerics;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;

namespace Realm.Ecs.Services;

/// <summary>
///     Demonstrates how Movement components are used, particularly "intent" components.
/// </summary>
public class MovementService
{
	public MovementService()
	{
	}

	/// <summary>
	/// Sets or updates the movement target for an entity.
	/// This demonstrates the "intent" pattern. You don't move the entity here.
	/// You simply add the 'MoveTo' component. A 'MovementSystem' would then query
	/// for all entities with this component and perform the actual pathfinding and movement.
	/// </summary>
	public void SetMoveTarget(Entity entity, Vector3 targetPosition)
	{
		if (ECSContext.World == null || !ECSContext.World.Has<Movable>(entity)) return;

		ECSContext.World.SetOrAdd(entity, new MoveTo(targetPosition));
	}
}
