using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays combat veterancy on the authority: every kill pays the bounty its victim's template states, the experience
///     raises the hero and the whole army follows its level up to the authored cap, and a hero keeps the progression it
///     earned when it revives.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeProgressionTests
{
	private const string AxemanTemplateId = "unit/axeman";
	private const int HeroLevelCap = 6;
	private const float ExperiencePerLevel = 200f;
	private const float AxemanGoldBounty = 10f;
	private const float AxemanExperienceBounty = 8f;

	private static readonly Vector3 EnemyPosition = new(-40f, 0f, -50f);
	private static readonly Vector3 KillerPosition = new(-42f, 0f, -50f);

	private MeleeMatch _match = null!;

	[SetUp]
	public void SetUp()
	{
		MeleeTestMaps.RequireMaps();
		_match = MeleeMatch.StartDuel();
	}

	[TearDown]
	public void TearDown()
	{
		_match.Dispose();
	}

	[Test]
	public void AKillPaysTheAuthoredGoldAndExperienceToTheKillingHero()
	{
		UnitId hero = _match.Hero(1);
		UnitId victim = _match.SpawnSoldier(2, AxemanTemplateId, EnemyPosition);
		float goldBefore = _match.Gold(1);
		float experienceBefore = _match.ExperienceOf(hero);

		_match.Kill(victim, hero);

		Assert.That(_match.IsAlive(victim), Is.False, "The victim has to die from the killing blow.");
		Assert.That(_match.Gold(1) - goldBefore, Is.EqualTo(AxemanGoldBounty).Within(0.01f), "The whole gold bounty reaches a team of one.");
		Assert.That(_match.ExperienceOf(hero) - experienceBefore, Is.EqualTo(AxemanExperienceBounty).Within(0.01f), "The experience bounty reaches the hero whose level the army follows.");
	}

	[Test]
	public void ABountyIsPaidToTheHerosLevelNotTheSoldierThatLandedTheBlow()
	{
		UnitId hero = _match.Hero(1);
		UnitId killer = _match.SpawnSoldier(1, AxemanTemplateId, KillerPosition);
		UnitId victim = _match.SpawnSoldier(2, AxemanTemplateId, EnemyPosition);
		float heroExperienceBefore = _match.ExperienceOf(hero);
		float killerExperienceBefore = _match.ExperienceOf(killer);

		_match.Kill(victim, killer);

		Assert.That(_match.ExperienceOf(hero) - heroExperienceBefore, Is.EqualTo(AxemanExperienceBounty).Within(0.01f), "A soldier's kill has to advance the hero's veterancy.");
		Assert.That(_match.ExperienceOf(killer) - killerExperienceBefore, Is.EqualTo(0f).Within(0.01f), "A soldier is never the unit veterancy is tracked on.");
	}

	[Test]
	public void KillingAnEnemyHeroPaysItsLargerBounty()
	{
		UnitId hero = _match.Hero(1);
		UnitId enemyHero = _match.Hero(2);
		float goldBefore = _match.Gold(1);
		float experienceBefore = _match.ExperienceOf(hero);

		_match.Kill(enemyHero, hero);

		Assert.That(_match.Gold(1) - goldBefore, Is.EqualTo(100f).Within(0.01f), "An enemy hero pays the larger bounty its template states.");
		Assert.That(_match.ExperienceOf(hero) - experienceBefore, Is.EqualTo(120f).Within(0.01f), "The experience portion of a hero's bounty reaches the killer's hero.");
	}

	[Test]
	public void KillingAnEnemyBuildingPaysTheBountyItsTemplateStates()
	{
		UnitId hero = _match.Hero(1);
		UnitId building = _match.Api.SpawnUnitForPlayer("building/spear_lodge", new Vector2(EnemyPosition.X, EnemyPosition.Z), 2, executeSpawnShader: false, bypassPopulation: true);
		Assert.That(_match.IsAlive(building), Is.True, "The test needs an enemy building to destroy.");

		float goldBefore = _match.Gold(1);
		float experienceBefore = _match.ExperienceOf(hero);

		_match.Kill(building, hero);

		Assert.That(_match.IsAlive(building), Is.False, "The building has to fall.");
		Assert.That(_match.Gold(1) - goldBefore, Is.EqualTo(50f).Within(0.01f), "A building pays the gold bounty its template states.");
		Assert.That(_match.ExperienceOf(hero) - experienceBefore, Is.EqualTo(40f).Within(0.01f), "A building pays the experience bounty its template states.");
	}

	[Test]
	public void AKillByNothingPaysNoBounty()
	{
		UnitId hero = _match.Hero(1);
		UnitId victim = _match.SpawnSoldier(2, AxemanTemplateId, EnemyPosition);
		float goldBefore = _match.Gold(1);
		float experienceBefore = _match.ExperienceOf(hero);

		_match.Api.KillUnit(victim, executeDespawnShader: false, playDeathAnimation: false);
		_match.Step(0.2f);

		Assert.That(_match.IsAlive(victim), Is.False, "The victim has to be gone.");
		Assert.That(_match.Gold(1), Is.EqualTo(goldBefore).Within(0.01f), "A kill nothing landed belongs to no team and pays no gold.");
		Assert.That(_match.ExperienceOf(hero), Is.EqualTo(experienceBefore).Within(0.01f), "A kill nothing landed pays no experience either.");
	}

	[Test]
	public void ExperienceRaisesTheHeroAndEverySoldierToTheAuthoredCap()
	{
		UnitId hero = _match.Hero(1);
		UnitId soldier = _match.SpawnSoldier(1, AxemanTemplateId, KillerPosition);

		_match.Api.SetUnitExperience(hero, ExperiencePerLevel * 2f + 30f);
		_match.Step(0.2f);

		Assert.That(_match.HeroLevel(1), Is.EqualTo(3), "Experience has to be spent on every level its threshold reaches.");
		Assert.That(_match.LevelOf(soldier), Is.EqualTo(3), "The whole army follows the hero's level.");
		Assert.That(_match.ExperienceOf(hero), Is.EqualTo(30f).Within(0.01f), "The experience spent on levels is removed from the hero's progress.");

		_match.Api.SetUnitExperience(hero, 100000f);
		_match.Step(0.2f);

		Assert.That(_match.HeroLevel(1), Is.EqualTo(HeroLevelCap), "A hero may not pass its authored level cap.");
		Assert.That(_match.LevelOf(soldier), Is.EqualTo(HeroLevelCap), "The army stops at the same cap as its hero.");
	}

	[Test]
	public void TheHeroRevivesWithTheLevelItEarned()
	{
		UnitId hero = _match.Hero(1);
		_match.Api.SetUnitExperience(hero, ExperiencePerLevel * 2f + 45f);
		_match.Step(0.2f);
		Assert.That(_match.HeroLevel(1), Is.EqualTo(3), "The test needs a hero that has earned a level first.");

		_match.Kill(hero, _match.Hero(2));
		_match.Step(12.5f);

		UnitId revived = _match.Hero(1);
		Assert.That(_match.IsAlive(revived), Is.True, "The hero has to return after its revival timer.");
		Assert.That(_match.LevelOf(revived), Is.EqualTo(3), "A revival returns the level the hero earned, not a reset one.");
		Assert.That(_match.ExperienceOf(revived), Is.EqualTo(45f).Within(0.01f), "The progress toward the next level is kept as well.");

		UnitId soldier = _match.SpawnSoldier(1, AxemanTemplateId, KillerPosition);
		_match.Step(0.2f);
		Assert.That(_match.LevelOf(soldier), Is.EqualTo(3), "A soldier raised after the revival still follows the hero's level.");
	}
}
