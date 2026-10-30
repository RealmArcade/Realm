using System;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Tags;

namespace Realm.Ecs.Services;

/// <summary>
///     Demonstrates how Combat-related components are used.
///     A real game would have systems that perform these actions every frame.
/// </summary>
public class CombatService
{
	private readonly SimulationContext _context;

	public CombatService(SimulationContext context)
	{
		_context = context;
	}

	/// <summary>
	///     Makes one entity attack another using the 3-stage combat resolution pipeline.
	/// </summary>
	public void PerformAttack(Entity attacker, Entity defender)
	{
		EcsWorld? world = _context.World;
		if (world == null || !world.Has<Attack>(attacker) || !world.Has<Health>(defender)) return;
		if (world.Has<Invulnerable>(defender)) return;

		var attack = world.Get<Attack>(attacker);
		var health = world.Get<Health>(defender);
		var resistances = world.Has<Resistances>(defender) ? world.Get<Resistances>(defender) : default;
		bool magic = DamageTypes.IsMagic(attack.DamageType);

		var result = CombatResolver.ResolveDamage(
			baseDamage: attack.Damage,
			damageVariance: attack.DamageVariance,
			critChance: attack.CritChance,
			critMultiplier: attack.CritMultiplier,
			physicalResistance: resistances.Physical,
			magicResistance: resistances.Magic,
			flatPenetration: magic ? ReadStat(attacker, StatKeys.FlatMagicPenetration) : attack.FlatArmorPenetration,
			percentPenetration: magic ? ReadStat(attacker, StatKeys.PercentMagicPenetration) : attack.PercentArmorPenetration,
			damageType: attack.DamageType,
			context: _context
		);

		health.CurrentHp -= result.FinalDamage;
		health.TimeSinceLastDamage = 0f;

		if (health.CurrentHp <= 0)
		{
			health.CurrentHp = 0;
			world.Add<Dead>(defender);
			if (world.Has<Name>(defender))
			{
				Console.WriteLine($"{world.Get<Name>(defender).Value} has been slain!");
			}
		}

		world.Set(defender, health);
	}

	private float ReadStat(Entity entity, string statKey)
	{
		return _context.StatService?.GetStatValue(entity, new StatId(statKey)) ?? 0f;
	}
}
