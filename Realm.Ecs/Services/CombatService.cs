using Arch.Core;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Tags;

namespace Realm.Ecs.Services;

/// <summary>
///     Demonstrates how Combat-related components are used.
///     A real game would have systems that perform these actions every frame.
/// </summary>
internal class CombatService
{
	private readonly WorldAccessor _ecsWorldAccessor;

	public CombatService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	/// <summary>
	///     Makes one entity attack another using the 3-stage combat resolution pipeline.
	/// </summary>
	public void PerformAttack(Entity attacker, Entity defender)
	{
		if (!_ecsWorldAccessor.Current.Has<Attack>(attacker) || !_ecsWorldAccessor.Current.Has<Health>(defender)) return;
		if (_ecsWorldAccessor.Current.Has<Invulnerable>(defender)) return;

		var attack = _ecsWorldAccessor.Current.Get<Attack>(attacker);
		var health = _ecsWorldAccessor.Current.Get<Health>(defender);
		var armor = _ecsWorldAccessor.Current.Has<Armor>(defender) ? _ecsWorldAccessor.Current.Get<Armor>(defender) : new Armor(0f);

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

		health.Current -= result.FinalDamage;
		health.TimeSinceLastDamage = 0f;

		if (health.Current <= 0)
		{
			health.Current = 0;
			_ecsWorldAccessor.Current.Add<Dead>(defender);
			Console.WriteLine($"{_ecsWorldAccessor.Current.Get<Name>(defender).Value} has been slain!");
		}

		_ecsWorldAccessor.Current.Set(defender, health);
	}
}
