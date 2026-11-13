using System.Collections.Generic;
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
		_match.Api.EnableUnitAutoCombat(_match.Nexus(1), false);
		_match.Api.EnableUnitAutoCombat(_match.Hero(1), false);
		_heroPosition = _match.PositionOf(_match.Hero(1));
	}

	private UnitId SpawnEnemy(Vector3 position)
	{
		UnitId enemy = _match.SpawnSoldier(2, "unit/shieldbearer", position);
		_match.Api.EnableUnitAutoCombat(enemy, false);
		return enemy;
	}

	private UnitId SpawnDurableEnemy(Vector3 position) => SpawnEnemy(position);

	private float AmplifiedBySpellPower(float authoredDamage) => authoredDamage * (1f + _match.StatOf(_match.Hero(1), "SpellPower"));

	private void LevelHero(int level)
	{
		if (level <= 1) return;

		_match.Api.SetUnitExperience(_match.Hero(1), level == 3 ? 430f : 100000f);
		_match.Step(0.2f);
		Assert.That(_match.HeroLevel(1), Is.EqualTo(level), "The test needs the hero at the level it is measuring.");
		float baseIntelligence = _match.Factions[0] switch
		{
			"Sable" => 26f,
			"Corvus" => 20f,
			_ => 12f
		};
		Assert.That(_match.StatOf(_match.Hero(1), "Intelligence"), Is.EqualTo(baseIntelligence + (3f * (level - 1))).Within(0.01f), "The hero's Intelligence has to grow with its level before an ability can scale from it.");
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
		UnitId inside = SpawnDurableEnemy(_heroPosition + new Vector3(3f, 0f, 0f));
		UnitId outside = SpawnDurableEnemy(_heroPosition + new Vector3(10f, 0f, 0f));
		float insideBefore = _match.HealthOf(inside);
		float outsideBefore = _match.HealthOf(outside);

		_match.Cast(1, "ability/cinderfield", _heroPosition);
		_match.Step(0.6f);

		Assert.That(_match.HealthOf(inside), Is.LessThan(insideBefore - 10f), "Cinderfield has to burn the enemy inside its radius.");
		Assert.That(_match.HealthOf(outside), Is.EqualTo(outsideBefore).Within(0.1f), "An enemy beyond the field must not be burned.");
	}

	[TestCase(1, 60f)]
	[TestCase(3, 90f)]
	[TestCase(6, 120f)]
	public void GroundingShoutDamageGrowsWithTheHerosIntelligence(int level, float expectedDamage)
	{
		Start("Vale");
		LevelHero(level);
		UnitId enemy = SpawnDurableEnemy(_heroPosition + new Vector3(3f, 0f, 0f));
		float before = _match.HealthOf(enemy);

		_match.Cast(1, "ability/grounding_shout", _match.PositionOf(enemy));

		Assert.That(before - _match.HealthOf(enemy), Is.EqualTo(AmplifiedBySpellPower(expectedDamage)).Within(2f), $"Grounding Shout has to deal {expectedDamage} base damage at hero level {level}, amplified by the hero's SpellPower.");
	}

	[TestCase(1, 3f)]
	[TestCase(3, 3.5f)]
	[TestCase(6, 4f)]
	public void HoldFastDurationGrowsWithTheHerosIntelligence(int level, float expectedDuration)
	{
		Start("Vale");
		LevelHero(level);
		UnitId ally = _match.SpawnSoldier(1, "unit/shieldbearer", _heroPosition + new Vector3(2f, 0f, 0f));

		_match.Cast(1, "ability/hold_fast", _heroPosition);
		_match.Step(expectedDuration - 0.2f);
		Assert.That(_match.Api.UnitHasBuff(ally, "buff/hold_fast"), Is.True, $"Hold Fast has to still be on the soldier at hero level {level} before its {expectedDuration} seconds run out.");

		_match.Step(0.4f);
		Assert.That(_match.Api.UnitHasBuff(ally, "buff/hold_fast"), Is.False, $"Hold Fast has to end at the {expectedDuration} seconds its level states.");
	}

	[TestCase(1, 1.25f)]
	[TestCase(3, 1.4f)]
	[TestCase(6, 1.55f)]
	public void ShieldBashStunGrowsWithTheHerosIntelligence(int level, float expectedDuration)
	{
		Start("Vale");
		LevelHero(level);
		UnitId enemy = SpawnDurableEnemy(_heroPosition + new Vector3(3f, 0f, 0f));

		_match.Cast(1, "ability/shield_bash", _match.PositionOf(enemy));
		_match.Step(expectedDuration - 0.2f);
		Assert.That(_match.Api.UnitHasBuff(enemy, "stun"), Is.True, $"Shield Bash has to still be stunning at hero level {level} before its {expectedDuration} seconds run out.");

		_match.Step(0.4f);
		Assert.That(_match.Api.UnitHasBuff(enemy, "stun"), Is.False, $"Shield Bash stun has to end at the {expectedDuration} seconds its level states.");
	}

	[TestCase(1, 90f)]
	[TestCase(3, 130f)]
	[TestCase(6, 170f)]
	public void EmberLanceDamageGrowsWithTheHerosIntelligence(int level, float expectedDamage)
	{
		Start("Sable");
		LevelHero(level);
		UnitId enemy = SpawnDurableEnemy(_heroPosition + new Vector3(0f, 0f, 6f));
		float before = _match.HealthOf(enemy);

		_match.Cast(1, "ability/ember_lance", _heroPosition + new Vector3(0f, 0f, 8f));

		Assert.That(before - _match.HealthOf(enemy), Is.EqualTo(AmplifiedBySpellPower(expectedDamage)).Within(2f), $"Ember Lance has to deal {expectedDamage} base damage at hero level {level}, amplified by the hero's SpellPower.");
	}

	[TestCase(1, 70f)]
	[TestCase(3, 95f)]
	[TestCase(6, 120f)]
	public void BackdraftDamageGrowsWithTheHerosIntelligence(int level, float expectedDamage)
	{
		Start("Sable");
		LevelHero(level);
		UnitId enemy = SpawnDurableEnemy(_heroPosition + new Vector3(3f, 0f, 0f));
		float before = _match.HealthOf(enemy);

		_match.Cast(1, "ability/backdraft", _match.PositionOf(enemy));

		Assert.That(before - _match.HealthOf(enemy), Is.EqualTo(AmplifiedBySpellPower(expectedDamage)).Within(2f), $"Backdraft has to deal {expectedDamage} base damage at hero level {level}, amplified by the hero's SpellPower.");
	}

	[TestCase(1, 8)]
	[TestCase(3, 9)]
	[TestCase(6, 10)]
	public void CinderfieldBurnsItsAuthoredDamageForTheDurationItsLevelStates(int level, int expectedTicks)
	{
		Start("Sable");
		LevelHero(level);
		UnitId enemy = SpawnDurableEnemy(_heroPosition + new Vector3(3f, 0f, 0f));

		_match.Cast(1, "ability/cinderfield", _heroPosition);
		_match.Api.SetUnitCommandsFrozen(_match.Hero(1), true);
		_match.Api.SetUnitCommandsFrozen(enemy, true);

		List<float> ticks = CinderfieldTicks(enemy, 5.4f);
		float expectedTickDamage = AmplifiedBySpellPower(25f * 0.5f);

		Assert.That(ticks.Count, Is.EqualTo(expectedTicks), $"Cinderfield's level {level} duration has to burn for {expectedTicks} half-second ticks, each worth the authored 25 damage per second.");
		foreach (float tick in ticks)
		{
			Assert.That(tick, Is.EqualTo(expectedTickDamage).Within(0.6f), "Each Cinderfield tick has to deal the authored 25 damage per second over half a second, amplified by the caster's SpellPower.");
		}
	}

	private List<float> CinderfieldTicks(UnitId enemy, float seconds)
	{
		var ticks = new List<float>();
		float previous = _match.HealthOf(enemy);
		int frames = (int)MathF.Round(seconds * 30f);

		for (int frame = 0; frame < frames; frame++)
		{
			_match.Step(1f / 30f);
			float current = _match.HealthOf(enemy);
			float lost = previous - current;
			if (lost >= 5f) ticks.Add(lost);
			previous = current;
		}

		return ticks;
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
