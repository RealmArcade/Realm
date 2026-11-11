using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays the three active abilities each hero brings: Vale's shout, bulwark order and bash, Sable's lance, field
///     and backdraft, and Corvus' forced march. Each cast is made from the hero the player controls, at a position the
///     command card would send, and the result is read from the world the match plays in.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeHeroAbilityTests
{
	private MeleeMatch _match = null!;
	private Vector3 _heroPosition;

	[SetUp]
	public void SetUp()
	{
		MeleeTestMaps.RequireMaps();
	}

	[TearDown]
	public void TearDown()
	{
		_match?.Dispose();
	}

	private void Start(string faction)
	{
		_match = MeleeMatch.StartDuel(faction, "Vale");
		_heroPosition = _match.PositionOf(_match.Hero(1));
	}

	private UnitId SpawnEnemy(Vector3 position)
	{
		UnitId enemy = _match.SpawnSoldier(2, "unit/shieldbearer", position);
		_match.Api.EnableUnitAutoCombat(enemy, false);
		return enemy;
	}

	[Test]
	public void ValesCommandCardOffersEveryActive()
	{
		Start("Vale");

		Assert.That(_match.IsAbilityVisible(_match.Hero(1), "ability/grounding_shout"), Is.True, "Grounding Shout has to be on the card.");
		Assert.That(_match.IsAbilityVisible(_match.Hero(1), "ability/hold_fast"), Is.True, "Hold Fast has to be on the card.");
		Assert.That(_match.IsAbilityVisible(_match.Hero(1), "ability/shield_bash"), Is.True, "Shield Bash has to be on the card.");
	}

	[Test]
	public void StartingAnotherMatchDoesNotStackTheMapsBuffs()
	{
		using (MeleeMatch previous = MeleeMatch.StartDuel())
		{
			Assert.That(previous.Hero(1).IsValid, Is.True, "The first match has to initialize its map before the second one starts.");
		}

		Start("Vale");
		UnitId ally = _match.SpawnSoldier(1, "unit/shieldbearer", _heroPosition + new Vector3(2f, 0f, 0f));
		float armorBefore = _match.ArmorOf(ally);

		_match.Cast(1, "ability/hold_fast", _heroPosition);
		_match.Step(0.3f);

		Assert.That(_match.ModifierTotalFrom(ally, "buff:buff/hold_fast", "Armor"), Is.EqualTo(4f).Within(0.01f), "A second match must not inherit the buff definitions the first one registered.");
		Assert.That(_match.ArmorOf(ally), Is.EqualTo(armorBefore + 4f).Within(0.1f), "The soldier's armour has to rise by four, not by a multiple of it.");
	}

	[Test]
	public void GroundingShoutDamagesAndSlowsTheEnemiesAroundTheHero()
	{
		Start("Vale");
		UnitId inside = SpawnEnemy(_heroPosition + new Vector3(3f, 0f, 0f));
		UnitId outside = SpawnEnemy(_heroPosition + new Vector3(20f, 0f, 0f));
		float insideBefore = _match.HealthOf(inside);
		float outsideBefore = _match.HealthOf(outside);
		float insideSpeedBefore = _match.SpeedOf(inside);

		_match.Cast(1, "ability/grounding_shout", _heroPosition);
		_match.Step(0.3f);

		Assert.That(_match.HealthOf(inside), Is.LessThan(insideBefore - 20f), "The shout has to damage an enemy inside its radius.");
		Assert.That(_match.Api.UnitHasBuff(inside, "buff/grounding_shout"), Is.True, "The shout has to leave its slow on the enemies it caught.");
		Assert.That(_match.SpeedOf(inside), Is.EqualTo(insideSpeedBefore * 0.75f).Within(insideSpeedBefore * 0.05f), "The shout's slow has to take a quarter of the caught enemy's speed.");
		Assert.That(_match.HealthOf(outside), Is.GreaterThanOrEqualTo(outsideBefore - 1f), "An enemy beyond the shout's radius must not be caught.");
	}

	[Test]
	public void HoldFastPutsItsArmourBuffOnTheSoldiersBesideTheHero()
	{
		Start("Vale");
		UnitId ally = _match.SpawnSoldier(1, "unit/shieldbearer", _heroPosition + new Vector3(2f, 0f, 0f));
		float armorBefore = _match.ArmorOf(ally);

		_match.Cast(1, "ability/hold_fast", _heroPosition);
		_match.Step(0.3f);

		Assert.That(_match.Api.UnitHasBuff(ally, "buff/hold_fast"), Is.True, "Hold Fast has to reach the soldier standing beside the hero.");
		Assert.That(_match.ModifierTotalFrom(ally, "buff:buff/hold_fast", "Armor"), Is.EqualTo(4f).Within(0.01f), "Hold Fast has to add its four points of armour to the soldier.");
		Assert.That(_match.ArmorOf(ally), Is.EqualTo(armorBefore + 4f).Within(0.1f), "The soldier's derived armour has to rise by the buff's value.");
	}

	[Test]
	public void ShieldBashDamagesTheEnemyItIsAimedAt()
	{
		Start("Vale");
		UnitId enemy = SpawnEnemy(_heroPosition + new Vector3(3f, 0f, 0f));
		float before = _match.HealthOf(enemy);

		_match.Cast(1, "ability/shield_bash", _match.PositionOf(enemy));
		_match.Step(0.3f);

		Assert.That(_match.HealthOf(enemy), Is.LessThan(before - 15f), "Shield Bash has to land its single-target blow.");
	}

	[Test]
	public void ShieldBashStunsTheEnemyItIsAimedAt()
	{
		Start("Vale");
		Vector3 spawnedAt = _heroPosition + new Vector3(3f, 0f, 0f);
		UnitId enemy = SpawnEnemy(spawnedAt);
		_match.Api.IssueMoveOrder(enemy, _heroPosition + new Vector3(15f, 0f, 0f));
		_match.Step(0.1f);
		Assert.That(Vector3.Distance(_match.PositionOf(enemy), spawnedAt), Is.GreaterThan(0.1f), "The enemy has to be walking before the bash so the stun has movement to stop.");

		_match.Cast(1, "ability/shield_bash", _match.PositionOf(enemy));
		_match.Step(0.3f);

		Assert.That(_match.Api.UnitHasBuff(enemy, "stun"), Is.True, "Shield Bash has to stun the enemy it hit.");
		Vector3 stunnedPosition = _match.PositionOf(enemy);

		_match.Step(0.6f);
		Assert.That(Vector3.Distance(_match.PositionOf(enemy), stunnedPosition), Is.LessThan(0.5f), "A stunned enemy must not walk away during the stun.");

		_match.Step(0.6f);
		Assert.That(_match.Api.UnitHasBuff(enemy, "stun"), Is.False, "The stun has to end when its authored 1.25 seconds run out.");
	}

	[Test]
	public void SablessCommandCardOffersEveryActive()
	{
		Start("Sable");

		Assert.That(_match.IsAbilityVisible(_match.Hero(1), "ability/ember_lance"), Is.True, "Ember Lance has to be on the card.");
		Assert.That(_match.IsAbilityVisible(_match.Hero(1), "ability/cinderfield"), Is.True, "Cinderfield has to be on the card.");
		Assert.That(_match.IsAbilityVisible(_match.Hero(1), "ability/backdraft"), Is.True, "Backdraft has to be on the card.");
	}

	[Test]
	public void EmberLanceBurnsTheEnemiesInItsLine()
	{
		Start("Sable");
		UnitId inLine = SpawnEnemy(_heroPosition + new Vector3(0f, 0f, 6f));
		UnitId besideLine = SpawnEnemy(_heroPosition + new Vector3(18f, 0f, 0f));
		float inLineBefore = _match.HealthOf(inLine);
		float besideLineBefore = _match.HealthOf(besideLine);

		_match.Cast(1, "ability/ember_lance", _heroPosition + new Vector3(0f, 0f, 8f));
		_match.Step(0.3f);

		Assert.That(_match.HealthOf(inLine), Is.LessThan(inLineBefore - 30f), "Ember Lance has to burn the enemy standing in its line.");
		Assert.That(_match.HealthOf(besideLine), Is.GreaterThanOrEqualTo(besideLineBefore - 1f), "An enemy outside the lance's line must not be burned.");
	}

	[Test]
	public void CinderfieldBurnsTheEnemiesAroundItsCentre()
	{
		Start("Sable");
		UnitId inside = SpawnEnemy(_heroPosition + new Vector3(3f, 0f, 0f));
		float before = _match.HealthOf(inside);

		_match.Cast(1, "ability/cinderfield", _heroPosition);
		_match.Step(0.3f);

		Assert.That(_match.HealthOf(inside), Is.LessThan(before - 30f), "Cinderfield has to burn the enemy inside its radius.");
	}

	[Test]
	public void BackdraftBurnsAndPushesTheEnemiesAwayFromTheHero()
	{
		Start("Sable");
		UnitId enemy = SpawnEnemy(_heroPosition + new Vector3(3f, 0f, 0f));
		float healthBefore = _match.HealthOf(enemy);
		float distanceBefore = Vector3.Distance(_match.PositionOf(enemy), _heroPosition);

		_match.Cast(1, "ability/backdraft", _match.PositionOf(enemy));
		_match.Step(0.3f);

		float distanceAfter = Vector3.Distance(_match.PositionOf(enemy), _heroPosition);
		Assert.That(_match.HealthOf(enemy), Is.LessThan(healthBefore - 20f), "Backdraft has to burn the enemy it caught.");
		Assert.That(distanceAfter, Is.GreaterThan(distanceBefore), "Backdraft has to push the enemy away from the hero.");
	}

	[Test]
	public void CorvussCommandCardOffersEveryActive()
	{
		Start("Corvus");

		Assert.That(_match.IsAbilityVisible(_match.Hero(1), "ability/requisition"), Is.True, "Requisition has to be on the card.");
		Assert.That(_match.IsAbilityVisible(_match.Hero(1), "ability/forced_march"), Is.True, "Forced March has to be on the card.");
		Assert.That(_match.IsAbilityVisible(_match.Hero(1), "ability/emergency_levy"), Is.True, "Emergency Levy has to be on the card.");
	}

	[Test]
	public void ForcedMarchSpeedsUpTheSoldiersBesideTheHero()
	{
		Start("Corvus");
		UnitId ally = _match.SpawnSoldier(1, "unit/shieldbearer", _heroPosition + new Vector3(2f, 0f, 0f));
		float speedBefore = _match.SpeedOf(ally);

		_match.Cast(1, "ability/forced_march", _heroPosition);
		_match.Step(0.3f);

		Assert.That(_match.Api.UnitHasBuff(ally, "buff/forced_march"), Is.True, "Forced March has to reach the soldier standing beside the hero.");
		Assert.That(_match.SpeedOf(ally), Is.EqualTo(speedBefore * 1.2f).Within(speedBefore * 0.05f), "Forced March's twenty percent has to be a fifth of the soldier's own speed.");
	}
}
