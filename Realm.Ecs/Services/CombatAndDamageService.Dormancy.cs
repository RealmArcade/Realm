using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;

namespace Realm.Ecs.Services;

/// <summary>The building lifecycle: which buildings fall dormant at zero health instead of dying, and what dormant means.</summary>
public partial class CombatAndDamageService
{
	/// <summary>
	///     Called when a repairable building has just lost its last point of health, so the authority can stop what the
	///     building was doing and pay whoever destroyed it. The building stays in the world, dormant, until a repair
	///     restores it.
	/// </summary>
	public System.Action<Entity>? OnBuildingDormantRequested;

	/// <summary>
	///     Moves a repairable building into the dormant state, which is the state a destroyed but standing building is
	///     drawn and read in: nothing targets it, it takes no damage, it attacks nothing, it earns nothing, it recharges
	///     nothing and it produces nothing, while its charges and queue are kept for the repair.
	/// </summary>
	/// <param name="entity">The entity that ran out of health.</param>
	/// <returns>True when the entity is dormant and must not be removed from the world.</returns>
	private bool TryFallDormant(Entity entity)
	{
		bool alreadyDormant = _context.World.Has<Dormant>(entity);
		if (!alreadyDormant)
		{
			if (!_context.World.Has<Building>(entity) || !_context.World.Has<RepairableBuilding>(entity)) return false;

			_context.World.Add<Dormant>(entity);
			StopOperating(entity);
			ClearReferencesTo(entity);
			ClearChaseTracking(entity);
			_abandonedTargetCooldown.Remove(entity);
			OnBuildingDormantRequested?.Invoke(entity);
		}

		if (_context.World.TryGet<Health>(entity, out Health health) && health.Current != 0f)
		{
			health.Current = 0f;
			_context.World.Set(entity, health);
		}

		return true;
	}

	/// <summary>Ends the work a building was doing, keeping what it had banked for when it is repaired.</summary>
	private void StopOperating(Entity entity)
	{
		_context.World.Remove<MoveTo>(entity);
		_context.World.Remove<AttackMove>(entity);
		_context.World.Remove<Patrol>(entity);
		_context.World.Remove<AttackTarget>(entity);
		_context.World.Remove<Follow>(entity);
		_context.World.Remove<Velocity>(entity);
	}
}
