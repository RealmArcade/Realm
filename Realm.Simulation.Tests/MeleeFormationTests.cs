using System;
using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Ecs.Components.Combat;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays the autonomous army on the authority: the slots its formation assigns around the hero, the target each
///     soldier's authored targeting rule selects, and the guarantees that an ally is never a target and that a soldier
///     returns to its place once the fight is over.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeFormationTests
{
	private static readonly Vector3 SpawnPosition = new(-50f, 0f, -50f);

	private MeleeMatch _match = null!;
	private Vector3 _heroPosition;

	[SetUp]
	public void SetUp()
	{
		MeleeTestMaps.RequireMaps();
		_match = MeleeMatch.StartDuel();
		_match.Api.EnableUnitAutoCombat(_match.Hero(1), false);
		_heroPosition = _match.PositionOf(_match.Hero(1));
	}

	[TearDown]
	public void TearDown()
	{
		_match.Dispose();
	}

	[Test]
	public void TheArmyFormsUpAroundTheHeroInItsAuthoredSlots()
	{
		UnitId front = _match.SpawnSoldier(1, "unit/shieldbearer", SpawnPosition);
		UnitId flank = _match.SpawnSoldier(1, "unit/axeman", SpawnPosition + new Vector3(2f, 0f, 0f));
		UnitId back = _match.SpawnSoldier(1, "unit/bowman", SpawnPosition + new Vector3(0f, 0f, 2f));

		_match.Step(6f);

		Vector3 frontPosition = _match.PositionOf(front);
		Vector3 flankPosition = _match.PositionOf(flank);
		Vector3 backPosition = _match.PositionOf(back);

		Assert.That(frontPosition.Z, Is.GreaterThan(_heroPosition.Z + 2f), "A front soldier has to stand ahead of its hero.");
		Assert.That(backPosition.Z, Is.LessThan(_heroPosition.Z - 2f), "A back soldier has to stand behind its hero.");
		Assert.That(MathF.Abs(flankPosition.X - _heroPosition.X), Is.GreaterThan(2f), "A flank soldier has to stand to the side of its hero.");
		Assert.That(MathF.Abs(flankPosition.Z - _heroPosition.Z), Is.LessThan(2.5f), "A flank soldier belongs beside the hero, not ahead of or behind it.");
	}

	[Test]
	public void SoldiersHoldTheirSlotWhileNoEnemyIsNear()
	{
		UnitId front = _match.SpawnSoldier(1, "unit/shieldbearer", SpawnPosition);
		_match.Step(6f);
		Vector3 settled = _match.PositionOf(front);

		_match.Step(1.5f);

		Assert.That(Vector3.Distance(settled, _match.PositionOf(front)), Is.LessThan(1f), "A soldier in its slot must not wander without an enemy to chase.");
	}

	[Test]
	public void TheStandardRuleFightsTheNearestEnemy()
	{
		UnitId soldier = _match.SpawnSoldier(1, "unit/shieldbearer", SpawnPosition);
		UnitId near = SpawnStationaryEnemy(_match, SpawnPosition + new Vector3(4f, 0f, 0f), 5000f, 5000f);
		UnitId far = SpawnStationaryEnemy(_match, SpawnPosition + new Vector3(10f, 0f, 0f), 5000f, 5000f);

		float nearBefore = _match.HealthOf(near);
		_match.Step(0.2f);
		Assert.That(_match.CurrentTarget(soldier).Value, Is.EqualTo(near.Value), "The nearest enemy has to be the one a standard soldier fights.");
		Assert.That(_match.CurrentTarget(soldier).Value, Is.Not.EqualTo(far.Value), "A standard soldier must not reach past the nearest enemy.");
		_match.Step(2.3f);

		Assert.That(_match.HealthOf(near), Is.LessThan(nearBefore - 1f), "The target the standard rule selected has to take the blows.");
		Assert.That(_match.IsAlive(soldier), Is.True, "The soldier has to survive the probe.");
	}

	[Test]
	public void ATankShredderTargetsTheHealthiestEnemy()
	{
		UnitId soldier = _match.SpawnSoldier(1, "unit/spear_militia", SpawnPosition);
		UnitId healthy = SpawnStationaryEnemy(_match, SpawnPosition + new Vector3(5f, 0f, 0f), 5000f, 4500f);
		UnitId wounded = SpawnStationaryEnemy(_match, SpawnPosition + new Vector3(7f, 0f, 0f), 5000f, 900f);

		float healthyBefore = _match.HealthOf(healthy);
		_match.Step(0.2f);
		Assert.That(_match.CurrentTarget(soldier).Value, Is.EqualTo(healthy.Value), "A Tank-Shredder has to spend its blows on the healthiest enemy.");
		Assert.That(_match.CurrentTarget(soldier).Value, Is.Not.EqualTo(wounded.Value), "A Tank-Shredder must not settle for the wounded enemy beside it.");
		_match.Step(2.3f);

		Assert.That(_match.HealthOf(healthy), Is.LessThan(healthyBefore - 1f), "The healthiest enemy has to take the Tank-Shredder's blows.");
		Assert.That(_match.IsAlive(soldier), Is.True, "The soldier has to survive the probe.");
	}

	[Test]
	public void AnAssassinTargetsTheWeakestEnemy()
	{
		UnitId soldier = _match.SpawnSoldier(1, "unit/bowman", SpawnPosition);
		UnitId healthy = SpawnStationaryEnemy(_match, SpawnPosition + new Vector3(6f, 0f, 3f), 5000f, 4500f);
		UnitId wounded = SpawnStationaryEnemy(_match, SpawnPosition + new Vector3(0f, 0f, 8f), 5000f, 700f);

		float woundedBefore = _match.HealthOf(wounded);
		Assert.That(woundedBefore, Is.EqualTo(700f).Within(1f), "The test has to set the weak enemy's health before the step.");
		_match.Step(0.2f);
		Assert.That(_match.CurrentTarget(soldier).Value, Is.EqualTo(wounded.Value), "The Assassin has to acquire the weakest enemy as its target.");
		Assert.That(_match.CurrentTarget(soldier).Value, Is.Not.EqualTo(healthy.Value), "An Assassin must not settle for a healthier enemy beside the weak one.");
		_match.Step(2.3f);

		Assert.That(_match.HealthOf(wounded), Is.LessThan(woundedBefore - 1f), "The weakest enemy has to take the Assassin's blows.");
		Assert.That(_match.IsAlive(soldier), Is.True, "The soldier has to survive the probe.");
	}

	[Test]
	public void TheStandardRuleKeepsTheEnemyTheHeroLastHit()
	{
		UnitId soldier = _match.SpawnSoldier(1, "unit/axeman", SpawnPosition);
		UnitId nearest = SpawnStationaryEnemy(_match, SpawnPosition + new Vector3(-4f, 0f, 0f), 5000f, 5000f);
		UnitId marked = SpawnStationaryEnemy(_match, SpawnPosition + new Vector3(0f, 0f, 9f), 5000f, 5000f);

		_match.Api.DealTypedDamage(_match.Hero(1), marked, 10f, "physical");

		float markedBefore = _match.HealthOf(marked);
		_match.Step(0.2f);
		Assert.That(_match.CurrentTarget(soldier).Value, Is.EqualTo(marked.Value), "A soldier has to follow the enemy its hero is fighting instead of the nearest one.");
		Assert.That(_match.CurrentTarget(soldier).Value, Is.Not.EqualTo(nearest.Value), "The enemy the hero has not touched is not the army's target.");
		_match.Step(2.3f);

		Assert.That(_match.HealthOf(marked), Is.LessThan(markedBefore - 1f), "The enemy the hero marked has to take the army's blows.");
		Assert.That(_match.IsAlive(soldier), Is.True, "The soldier has to survive the probe.");
	}

	[Test]
	public void TheArmyNeverAttacksAnAlly()
	{
		using MeleeMatch allied = MeleeMatch.StartWithTeams(new[] { "Vale", "Sable" }, new[] { 1, 1 });
		Vector3 heroPosition = allied.PositionOf(allied.Hero(1));
		UnitId soldier = allied.SpawnSoldier(1, "unit/axeman", heroPosition + new Vector3(2f, 0f, 0f));
		UnitId ally = allied.SpawnSoldier(2, "unit/shieldbearer", heroPosition + new Vector3(4f, 0f, 0f));
		allied.Api.EnableUnitAutoCombat(ally, false);

		float allyHealthBefore = allied.HealthOf(ally);
		allied.Step(2.5f);

		Assert.That(allied.HealthOf(ally), Is.EqualTo(allyHealthBefore).Within(0.1f), "An autonomous soldier must never acquire an ally as a target.");
		Assert.That(allied.IsAlive(soldier), Is.True, "The soldier stays on the field.");
	}

	[Test]
	public void TheArmyReturnsToItsSlotAfterTheTargetDies()
	{
		UnitId soldier = _match.SpawnSoldier(1, "unit/axeman", SpawnPosition);
		UnitId enemy = SpawnStationaryEnemy(_match, SpawnPosition + new Vector3(5f, 0f, 0f), 5000f, 5000f);

		_match.Step(1f);
		_match.Kill(enemy, _match.Hero(1));
		_match.Step(5f);

		float distanceToHero = Vector3.Distance(_match.PositionOf(soldier), _heroPosition);
		Assert.That(distanceToHero, Is.LessThan(12f), "Once its target is gone a soldier has to come back to the formation around its hero.");
	}

	private static UnitId SpawnStationaryEnemy(MeleeMatch match, Vector3 position, float maxHealth, float health)
	{
		UnitId enemy = match.SpawnSoldier(2, "unit/shieldbearer", position);
		match.Api.EnableUnitAutoCombat(enemy, false);
		match.Api.SetUnitMaxHealth(enemy, maxHealth);
		match.Api.SetUnitHealth(enemy, health);
		return enemy;
	}
}
