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
	/// <summary>
	///     Makes one entity attack another using the 3-stage combat resolution pipeline.
	/// </summary>
	public void PerformAttack(Entity attacker, Entity defender)
	{
		if (ECSContext.World == null || !ECSContext.World.Has<Attack>(attacker) || !ECSContext.World.Has<Health>(defender)) return;
		if (ECSContext.World.Has<Invulnerable>(defender)) return;

		var attack = ECSContext.World.Get<Attack>(attacker);
		var health = ECSContext.World.Get<Health>(defender);
		var resistances = ECSContext.World.Has<Resistances>(defender) ? ECSContext.World.Get<Resistances>(defender) : default;
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
			damageType: attack.DamageType
		);

		health.CurrentHp -= result.FinalDamage;
		health.TimeSinceLastDamage = 0f;

		if (health.CurrentHp <= 0)
		{
			health.CurrentHp = 0;
			ECSContext.World.Add<Dead>(defender);
			if (ECSContext.World.Has<Name>(defender))
			{
				Console.WriteLine($"{ECSContext.World.Get<Name>(defender).Value} has been slain!");
			}
		}

		ECSContext.World.Set(defender, health);
	}

	private static float ReadStat(Entity entity, string statKey)
	{
		return ECSContext.StatService?.GetStatValue(entity, new StatId(statKey)) ?? 0f;
	}
}
