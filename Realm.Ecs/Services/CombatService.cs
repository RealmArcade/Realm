using System;
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
		var armor = ECSContext.World.Has<Armor>(defender) ? ECSContext.World.Get<Armor>(defender) : new Armor(0f);

		var result = CombatResolver.ResolveDamage(
			baseDamage: attack.Damage,
			damageVariance: attack.DamageVariance,
			critChance: attack.CritChance,
			critMultiplier: attack.CritMultiplier,
			flatArmor: armor.FlatArmor,
			ratedArmor: armor.RatedArmor,
			flatArmorPen: attack.FlatArmorPenetration,
			percentArmorPen: attack.PercentArmorPenetration,
			damageType: attack.DamageType,
			armorType: armor.ArmorType
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
}
