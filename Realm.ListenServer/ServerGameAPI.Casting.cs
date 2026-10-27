using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.MapAPI;
using Realm.Shared.Abilities;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>
///     What a cast does when a map script asks for one. The effects land in the authority's own world through the same
///     executor every other cast goes through, and only what a screen has to draw travels: a cast whose effect the
///     engine carries out itself never reaches a client as a decision, only as the state it changed and the effect it is
///     drawn with.
/// </summary>
public partial class ServerGameAPI
{
	/// <summary>
	///     Casts an ability from a unit at a place. Damage, healing, shields, statuses and summons are applied here, so a
	///     spell a map fires off is simulated once and every client sees the same outcome; an ability with no effects of
	///     its own is the map's own and is handed back to the map to resolve through its cast event.
	/// </summary>
	public void CastAbility(UnitId unit, string abilityId, Vector3 targetPosition)
	{
		Entity caster = ResolveUnit(unit);
		if (caster == Entity.Null) return;

		AbilityDefinition definition = _abilities.GetDefinition(abilityId);
		Vector3 casterPosition = _context.World.TryGet<Position>(caster, out Position position) ? position.Value : targetPosition;

		if (AbilityEffectRunner.HasEffects(definition)) _effects.Execute(caster, casterPosition, definition, targetPosition);
		else OnSpellCast?.Invoke(unit, abilityId, targetPosition);

		PublishCast(unit, definition, casterPosition, targetPosition);
	}

	/// <summary>
	///     Orders a unit to use an ability on another unit. The order is an attack the ability rides along with: the unit
	///     walks into reach and its cast resolves through the same path every other cast does, which is what a map means
	///     when it points one unit at another.
	/// </summary>
	public void IssueCastOrder(UnitId caster, string abilityId, UnitId target)
	{
		Entity casterEntity = ResolveUnit(caster);
		Entity targetEntity = ResolveUnit(target);
		if (casterEntity == Entity.Null || targetEntity == Entity.Null) return;

		IssueAttackOrder(caster, target);
		Vector3 position = _context.World.TryGet<Position>(targetEntity, out Position targetPosition) ? targetPosition.Value : Vector3.Zero;
		OnUnitOrdered?.Invoke(caster, abilityId ?? "Cast", position);
	}

	/// <summary>Orders a unit to use an ability at a place, which is an attack-move the ability rides along with.</summary>
	public void IssueCastOrderAt(UnitId caster, string abilityId, Vector3 position)
	{
		Entity casterEntity = ResolveUnit(caster);
		if (casterEntity == Entity.Null) return;

		IssueAttackMoveOrder(caster, position);
		OnUnitOrdered?.Invoke(caster, abilityId ?? "CastAt", position);
	}

	/// <summary>
	///     Tells every screen what the cast is drawn as. A cast that changed the world has already been described in the
	///     snapshot that carries the change, so this says nothing about what the cast did: it says where the effect
	///     belongs and what it looks like.
	/// </summary>
	private void PublishCast(UnitId unit, AbilityDefinition definition, Vector3 casterPosition, Vector3 targetPosition)
	{
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
