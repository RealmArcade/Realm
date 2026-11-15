using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Tags;
using Realm.MapAPI;
using Realm.Shared.Abilities;
using Realm.Shared.Animation;
using Realm.Shared.Network;

namespace Realm.ListenServer;

public partial class ServerGameAPI
{
	public void CastAbility(UnitId unit, string abilityId, Vector3 targetPosition)
	{
		Entity caster = ResolveUnit(unit);
		if (caster == Entity.Null) return;

		AbilityDefinition definition = _abilities.GetDefinition(abilityId);
		Vector3 casterPosition = _context.World.TryGet<Position>(caster, out Position position) ? position.Value : targetPosition;

		if (AbilityEffectRunner.HasEffects(definition)) _effects.Execute(caster, casterPosition, definition, targetPosition);
		else OnSpellCast?.Invoke(unit, abilityId, targetPosition);

		PublishCast(caster, unit, definition, casterPosition, targetPosition);
	}

	public void IssueCastOrder(UnitId caster, string abilityId, UnitId target)
	{
		Entity casterEntity = ResolveUnit(caster);
		Entity targetEntity = ResolveUnit(target);
		if (casterEntity == Entity.Null || targetEntity == Entity.Null) return;

		IssueAttackOrder(caster, target);
		Vector3 position = _context.World.TryGet<Position>(targetEntity, out Position targetPosition) ? targetPosition.Value : Vector3.Zero;
		OnUnitOrdered?.Invoke(caster, abilityId ?? "Cast", position);
	}

	public void IssueCastOrderAt(UnitId caster, string abilityId, Vector3 position)
	{
		Entity casterEntity = ResolveUnit(caster);
		if (casterEntity == Entity.Null) return;

		IssueAttackMoveOrder(caster, position);
		OnUnitOrdered?.Invoke(caster, abilityId ?? "CastAt", position);
	}

	private void PublishCast(Entity caster, UnitId unit, AbilityDefinition definition, Vector3 casterPosition, Vector3 targetPosition)
	{
		// A unit takes its casting pose towards the point it cast at, which is the pose a player reads a spell by. A building's
		// abilities are its production, which is not something it poses for, so a building casts without one.
		if (!_context.World.Has<Building>(caster)) PulseUnitAnimation(unit, nameof(RealmAnimationType.Spell_Cast));

		// An ability that names a projectile fires it as its own visual, and its landing is reported back to the map.
		LaunchAbilityProjectile(unit, definition, casterPosition, targetPosition);

		if (!string.IsNullOrEmpty(definition.VisualEffect))
		{
			_presentation.Write(PresentationKind.VisualEffect, ServerPresentationEvent.Everyone, unit.Value, definition.VisualEffect, string.Empty, string.Empty, string.Empty, null,
				casterPosition, targetPosition, Vector3.Zero, 0, 0, MathF.Max(definition.AreaOfEffectRadius, 1f), 0f, 0f, false);
		}

		if (!string.IsNullOrEmpty(definition.CastSound))
		{
			_presentation.Write(PresentationKind.CastSound, ServerPresentationEvent.Everyone, unit.Value, definition.CastSound, string.Empty, string.Empty, string.Empty, null,
				casterPosition, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
		}
	}
}
