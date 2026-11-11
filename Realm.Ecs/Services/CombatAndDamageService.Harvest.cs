using System;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;

namespace Realm.Ecs.Services;

/// <summary>taking resource out of a gatherable node with the same hit the unit would land in combat.</summary>
public partial class CombatAndDamageService
{
	/// <summary>
	///     Lands one gathering hit on a node: the worker's own attack, critical strike and penetration meet the node's
	///     resistances, and what survives that mitigation is taken off the node's health, which is its remaining resource.
	/// </summary>
	/// <remarks>
	///     A point of the node's health is one resource piece, so only whole points are ever taken. The fraction a hit
	///     leaves over waits in <paramref name="damageBank" /> and is joined by the next hit, which keeps what the node
	///     lost exactly equal to what the worker carries however small each hit's damage is.
	/// </remarks>
	/// <param name="worker">The unit taking the resource.</param>
	/// <param name="node">The gatherable node being worked.</param>
	/// <param name="damageBank">The fraction of a point of damage the worker's previous hit left over.</param>
	/// <param name="maximumPoints">The greatest number of whole points the hit may take, which is the room left in the worker's inventory.</param>
	/// <returns>The whole points of health the node lost, or zero when the hit yields no whole point yet.</returns>
	public float HarvestFromNode(Entity worker, Entity node, ref float damageBank, int maximumPoints)
	{
		if (maximumPoints <= 0) return 0f;
		if (!_context.World.IsAlive(worker) || !_context.World.IsAlive(node)) return 0f;
		if (!_context.World.Has<Harvestable>(node) || !_context.World.Has<Health>(node)) return 0f;
		if (_context.World.Has<Dead>(node)) return 0f;

		float baseDamage = _context.StatService != null
			? _context.StatService.GetStatValue(worker, new StatId(StatKeys.Attack))
			: 0f;

		float damageVariance = 0f;
		float critChance = 0f;
		float critMultiplier = 1f;
		float flatPenetration = 0f;
		float percentPenetration = 0f;
		string damageType = DamageTypes.Physical;

		if (_context.World.TryGet<Attack>(worker, out Attack attack))
		{
			if (baseDamage <= 0f) baseDamage = attack.Damage;
			damageVariance = attack.DamageVariance;
			critChance = attack.CritChance;
			critMultiplier = attack.CritMultiplier;
			damageType = attack.DamageType;
			flatPenetration = attack.FlatArmorPenetration;
			percentPenetration = attack.PercentArmorPenetration;
		}

		if (baseDamage <= 0f) baseDamage = 1f;
		if (critMultiplier <= 0f) critMultiplier = 1f;

		bool magic = DamageTypes.IsMagic(damageType);
		var resistances = _context.World.Has<Resistances>(node) ? _context.World.Get<Resistances>(node) : default;
		var result = CombatResolver.ResolveDamage(
			baseDamage: baseDamage,
			damageVariance: damageVariance,
			critChance: critChance,
			critMultiplier: critMultiplier,
			physicalResistance: resistances.Physical,
			magicResistance: resistances.Magic,
			flatPenetration: ReadPenetration(worker, magic, flatPenetration),
			percentPenetration: ReadPenetrationPercent(worker, magic, percentPenetration),
			damageType: damageType,
			config: Config);

		float finalDamage = result.FinalDamage;
		if (finalDamage <= 0f) return 0f;

		ref Health health = ref _context.World.Get<Health>(node);

		damageBank += finalDamage;
		float wholePoints = MathF.Min(MathF.Floor(damageBank), MathF.Min(maximumPoints, MathF.Floor(health.Current)));
		if (wholePoints <= 0f) return 0f;

		damageBank -= wholePoints;

		float newHp = Math.Max(0f, health.Current - wholePoints);
		health.Current = newHp;
		health.TimeSinceLastDamage = 0f;
		_context.World.Set(node, health);

		if (_context.World.TryGet<ResourceNode>(node, out ResourceNode resourceNode))
		{
			resourceNode.Amount = newHp;
			_context.World.Set(node, resourceNode);
		}

		OnUnitDamagedCallback?.Invoke(node, worker, wholePoints, wholePoints);
		if (newHp <= 0f)
		{
			if (HasAuthority) ClearReferencesTo(node);
		}
		else
		{
			OnDamageFlashRequested?.Invoke(node);
		}

		return wholePoints;
	}

	/// <summary>
	///     States how long the unit waits between gathering hits, which is the attack interval it would swing at in
	///     combat after its own attack speed has been applied.
	/// </summary>
	/// <param name="unit">The unit about to take a hit at a node.</param>
	/// <returns>The seconds between hits, never below a small floor.</returns>
	public float GetGatherInterval(Entity unit)
	{
		if (_context.World.IsAlive(unit) && _context.World.TryGet<Attack>(unit, out Attack attack))
		{
			return attack.Cooldown > 0f ? GetEffectiveAttackCooldown(unit, attack.Cooldown) : 1f;
		}

		return 1f;
	}
}
