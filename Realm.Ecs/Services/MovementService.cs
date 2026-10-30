using System.Numerics;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;

namespace Realm.Ecs.Services;

/// <summary>
///     Demonstrates how Movement components are used, particularly "intent" components.
/// </summary>
public class MovementService
{
	private readonly SimulationContext _context;

	public MovementService(SimulationContext context)
	{
		_context = context;
	}

	/// <summary>
	/// Sets or updates the movement target for an entity.
	/// This demonstrates the "intent" pattern. You don't move the entity here.
	/// You simply add the 'MoveTo' component. A 'MovementSystem' would then query
	/// for all entities with this component and perform the actual pathfinding and movement.
	/// </summary>
	public void SetMoveTarget(Entity entity, Vector3 targetPosition)
	{
		EcsWorld? world = _context.World;
		if (world == null || !world.Has<Movable>(entity)) return;

		world.SetOrAdd(entity, new MoveTo(targetPosition));
	}
}
