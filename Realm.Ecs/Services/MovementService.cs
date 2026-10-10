using Arch.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using System.Numerics;

namespace Realm.Ecs.Services;

/// <summary>
///     Demonstrates how Movement components are used, particularly "intent" components.
/// </summary>
public class MovementService
{
	private readonly WorldAccessor _ecsWorldAccessor;

	public MovementService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	/// <summary>
	/// Sets or updates the movement target for an entity.
	/// This demonstrates the "intent" pattern. You don't move the entity here.
	/// You simply add the 'MoveTo' component. A 'MovementSystem' would then query
	/// for all entities with this component and perform the actual pathfinding and movement.
	/// </summary>
	public void SetMoveTarget(Entity entity, Vector3 targetPosition)
	{
		if (!_ecsWorldAccessor.Current.Has<Movable>(entity)) return;

		_ecsWorldAccessor.Current.Set(entity, new MoveTo(targetPosition));
	}
}