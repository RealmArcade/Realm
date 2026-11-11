using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Shared.Simulation;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays the attack order a player sends by right-clicking an enemy: the order has to survive the authority's routing,
///     a target out of reach has to be walked to, and a target in reach has to be struck.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeCombatOrderTests
{
	private const float WalkingSeconds = 60f;

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
	public void AHeroOrderedOntoADistantNexusWalksThereAndStrikesIt()
	{
		UnitId hero = _match.Hero(1);
		UnitId nexus = _match.Nexus(2);
		Assert.That(nexus.IsValid, Is.True, "The board seats a Nexus for the second player.");

		Vector3 heroStart = _match.PositionOf(hero);
		Vector3 nexusPosition = _match.PositionOf(nexus);
		float startDistance = Vector3.Distance(heroStart, nexusPosition);
		Assert.That(startDistance, Is.GreaterThan(20f), "The board starts the two seats apart, so the order has to be walked.");

		ServerCommandResult result = _match.Attack(1, hero, nexus);
		Assert.That(result.HasRejection, Is.False, $"The hero's attack order on the enemy Nexus is accepted: {result.RejectionReason}");
		Assert.That(_match.CurrentTarget(hero), Is.EqualTo(nexus), "The order has to leave the Nexus as the hero's target.");

		float healthBefore = _match.HealthOf(nexus);
		float closest = startDistance;
		float walked = 0f;
		while (walked < WalkingSeconds)
		{
			_match.Step(1f);
			walked += 1f;
			closest = MathF.Min(closest, Vector3.Distance(_match.PositionOf(hero), nexusPosition));
			if (_match.HealthOf(nexus) < healthBefore) break;
		}

		Assert.That(closest, Is.LessThan(startDistance - 10f), "The hero has to walk the order to the Nexus instead of dropping it.");
		Assert.That(_match.HealthOf(nexus), Is.LessThan(healthBefore), "The attack has to land once the hero is beside the Nexus.");
	}

	[Test]
	public void AHeroOrderedOntoASoldierBesideItStrikesIt()
	{
		UnitId hero = _match.Hero(1);
		Vector3 beside = _match.PositionOf(hero) + new Vector3(2.5f, 0f, 0f);
		UnitId soldier = _match.SpawnSoldier(2, "unit/axeman", beside);

		ServerCommandResult result = _match.Attack(1, hero, soldier);
		Assert.That(result.HasRejection, Is.False, $"The hero's attack order on the soldier is accepted: {result.RejectionReason}");

		float healthBefore = _match.HealthOf(soldier);
		_match.Step(3f);

		Assert.That(_match.HealthOf(soldier), Is.LessThan(healthBefore), "A soldier standing in reach takes the hero's blows.");
	}
}
