using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Shared.Simulation;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays the building lifecycle the design states: a drafted building that is reduced to zero health stays on the
///     board as a dormant ruin, takes no more damage, earns and produces nothing, keeps what it had banked, and is only
///     working again once a hero has mended it back to whole.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeBuildingLifecycleTests
{
	private const float RepairHealthPercentPerSecond = 0.03f;
	private const float LongestRepairSeconds = 40f;

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

	private float BankedResources(int slot) => _match.Gold(slot) + _match.Wood(slot);

	private UnitId BuildNearTheHero()
	{
		Vector3 heroPosition = _match.PositionOf(_match.Hero(1));
		return _match.PlaceCompletedBuilding(heroPosition + new Vector3(8f, 0f, 0f));
	}

	[Test]
	public void ADestroyedBuildingFallsDormantInsteadOfLeavingTheBoard()
	{
		UnitId building = BuildNearTheHero();
		Assert.That(_match.IsRepairable(building), Is.True, "The ruleset lets a drafted building be repaired.");

		_match.Kill(building, _match.Hero(2));

		Assert.That(_match.IsAlive(building), Is.True, "A destroyed building stays standing as a ruin.");
		Assert.That(_match.IsDormant(building), Is.True, "Zero health puts the building in the dormant state.");
		Assert.That(_match.HealthOf(building), Is.EqualTo(0f).Within(0.01f), "A dormant building reads at zero health.");
		Assert.That(_match.Buildings(1), Does.Contain(building), "The ruin is still the owner's building.");
	}

	[Test]
	public void ADormantBuildingTakesNoMoreDamage()
	{
		UnitId building = BuildNearTheHero();
		UnitId enemyHero = _match.Hero(2);
		_match.Kill(building, enemyHero);

		_match.Kill(building, enemyHero);
		_match.Step(2f);

		Assert.That(_match.IsAlive(building), Is.True, "A dormant building cannot be destroyed a second time.");
		Assert.That(_match.HealthOf(building), Is.EqualTo(0f).Within(0.01f), "Damage on a dormant building lands for nothing.");
	}

	[Test]
	public void AHeroMendsADestroyedBuildingAtItsOwnRateOfMaximumHealthPerSecond()
	{
		UnitId building = BuildNearTheHero();
		_match.Kill(building, _match.Hero(2));

		UnitId hero = _match.Hero(1);
		ServerCommandResult result = _match.Repair(1, hero, building);
		Assert.That(result.HasRejection, Is.False, $"The hero's repair order is accepted: {result.RejectionReason}");

		_match.Step(6f);
		float settled = _match.HealthOf(building);
		Assert.That(settled, Is.GreaterThan(0f), "The hero mends the ruin once it stands at it.");

		_match.Step(10f);
		float mendPerTenSeconds = _match.MaxHealthOf(building) * RepairHealthPercentPerSecond * 10f;
		Assert.That(_match.HealthOf(building) - settled, Is.EqualTo(mendPerTenSeconds).Within(2f),
			"Repair is a share of maximum health per second, so the same ten seconds mends the same share of any building.");

		_match.Step(LongestRepairSeconds);
		Assert.That(_match.HealthOf(building), Is.EqualTo(_match.MaxHealthOf(building)).Within(0.5f), "The building is whole again.");
		Assert.That(_match.IsDormant(building), Is.False, "A building that is whole is no longer dormant.");
	}

	[Test]
	public void WalkingAwayLeavesTheRepairExactlyWhereTheHeroStopped()
	{
		UnitId building = BuildNearTheHero();
		_match.Kill(building, _match.Hero(2));

		UnitId hero = _match.Hero(1);
		Assert.That(_match.Repair(1, hero, building).HasRejection, Is.False, "The repair order is accepted.");

		_match.Step(8f);
		float stoppedAt = _match.HealthOf(building);
		Assert.That(stoppedAt, Is.GreaterThan(0f), "The hero has to be mending before the test stops it.");

		_match.Order(1, "stop", Vector3.Zero, hero);
		_match.Step(10f);

		Assert.That(_match.HealthOf(building), Is.EqualTo(stoppedAt).Within(0.5f), "A repair does not advance without the hero at it.");
		Assert.That(_match.IsDormant(building), Is.True, "A partly mended building is still dormant until it is whole.");
	}

	[Test]
	public void TheAttackOrderOnAnAlliedRuinMendsItInsteadOfStrikingIt()
	{
		UnitId building = BuildNearTheHero();
		_match.Kill(building, _match.Hero(2));

		UnitId hero = _match.Hero(1);
		ServerCommandResult result = _match.Attack(1, hero, building);
		Assert.That(result.HasRejection, Is.False, $"The attack command has to read the allied ruin as work: {result.RejectionReason}");

		_match.Step(10f);

		Assert.That(_match.HealthOf(building), Is.GreaterThan(0f), "The attack command has to send the hero to mend an allied ruin instead of striking it.");
		Assert.That(_match.IsDormant(building), Is.True, "A partly mended ruin is still dormant until it is whole.");
	}

	[Test]
	public void AStandingHeroDoesNotMendARuinWithoutAnOrder()
	{
		UnitId building = BuildNearTheHero();
		_match.Kill(building, _match.Hero(2));

		_match.Step(10f);

		Assert.That(_match.HealthOf(building), Is.EqualTo(0f).Within(0.01f), "Being near a ruin is not an order to mend it.");
		Assert.That(_match.IsDormant(building), Is.True, "The ruin stays dormant until a hero is sent to it.");
	}

	[Test]
	public void ADormantBuildingStopsPayingItsIncome()
	{
		UnitId building = BuildNearTheHero();

		_match.Step(30f);
		float paidBefore = BankedResources(1);
		_match.Step(30f);
		Assert.That(BankedResources(1), Is.GreaterThan(paidBefore), "The building's worker pays its income while the building stands.");

		_match.Kill(building, _match.Hero(2));
		float paidAtDestruction = BankedResources(1);

		_match.Step(20f);

		Assert.That(BankedResources(1), Is.EqualTo(paidAtDestruction).Within(1f), "A dormant building accepts no deliveries, so it pays nothing.");
	}

	[Test]
	public void ADormantBuildingKeepsItsChargesAndResumesProducingWhenMended()
	{
		UnitId building = BuildNearTheHero();
		_match.StepUntilSoldiers(1);

		int reservesBefore = _match.CountCharges(building);
		int soldiersBefore = _match.Soldiers(1).Count;
		_match.Kill(building, _match.Hero(2));

		_match.Step(20f);

		Assert.That(_match.CountCharges(building), Is.EqualTo(reservesBefore), "The charges a destroyed building held are kept.");
		Assert.That(_match.Soldiers(1).Count, Is.EqualTo(soldiersBefore), "A dormant building fields no soldiers.");

		UnitId hero = _match.Hero(1);
		Assert.That(_match.Repair(1, hero, building).HasRejection, Is.False, "The repair order is accepted.");
		_match.Step(20f + LongestRepairSeconds);

		Assert.That(_match.IsDormant(building), Is.False, "The building is mended back to whole.");

		float regaining = 0f;
		while (_match.CountCharges(building) == 0 && regaining < LongestRepairSeconds)
		{
			_match.Step(5f);
			regaining += 5f;
		}

		Assert.That(_match.CountCharges(building), Is.GreaterThanOrEqualTo(1), "A mended building regains its charges.");
		Assert.That(_match.StepUntilSoldiers(1), Is.GreaterThan(0), "A mended building fields soldiers again.");
	}

	[Test]
	public void AUnitTheRulesetDidNotMakeARepairerIsRefused()
	{
		UnitId building = BuildNearTheHero();
		_match.Kill(building, _match.Hero(2));

		ServerCommandResult result = _match.Repair(1, _match.Nexus(1), building);

		Assert.That(result.HasRejection, Is.True, "The nexus is refused the job of mending.");
		Assert.That(result.RejectionReason, Does.Contain("cannot repair"));
		Assert.That(_match.IsDormant(building), Is.True, "The refusal leaves the ruin dormant.");
	}

	[Test]
	public void TheNexusIsDestroyedForGoodInsteadOfFallingDormant()
	{
		UnitId nexus = _match.Nexus(1);
		Assert.That(_match.IsRepairable(nexus), Is.False, "The ruleset never marks a nexus repairable.");

		_match.Kill(nexus, _match.Hero(2));

		Assert.That(_match.IsAlive(nexus), Is.False, "A nexus at zero health is destroyed, not left dormant.");
		Assert.That(_match.IsDormant(nexus), Is.False, "A nexus never enters the dormant state.");
	}
}
