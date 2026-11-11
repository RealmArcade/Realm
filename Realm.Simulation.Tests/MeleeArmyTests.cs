using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Tags;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays the autonomous army on the authority: the level it inherits from its hero, the capacity its buildings hold and
///     what losing the hero does to it.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeArmyTests
{
	private static readonly Vector3 SitePosition = new(-50f, 0f, -50f);

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
	public void EverySoldierIsSetToTheHerosLevel()
	{
		UnitId first = _match.SpawnSoldier(1, "unit/shieldbearer", new Vector3(-46f, 0f, -52f));
		UnitId second = _match.SpawnSoldier(1, "unit/axeman", new Vector3(-44f, 0f, -52f));
		Assert.That(_match.IsAlive(first), Is.True);
		Assert.That(_match.IsAlive(second), Is.True);

		_match.Api.SetUnitLevel(_match.Hero(1), 4);
		_match.Step(0.2f);

		Assert.That(_match.LevelOf(first), Is.EqualTo(4), "A soldier has to carry the army level its hero sets.");
		Assert.That(_match.LevelOf(second), Is.EqualTo(4), "Every soldier of the army shares the hero's level.");
	}

	[Test]
	public void EverySoldierScalesWithTheHerosLevel()
	{
		UnitId soldier = _match.SpawnSoldier(1, "unit/shieldbearer", new Vector3(-46f, 0f, -52f));
		float maxHealthBefore = _match.MaxHealthOf(soldier);

		_match.Api.SetUnitLevel(_match.Hero(1), 4);
		_match.Step(0.2f);

		Assert.That(_match.LevelOf(soldier), Is.EqualTo(4), "The soldier has to carry the hero's level.");
		Assert.That(_match.MaxHealthOf(soldier), Is.GreaterThan(maxHealthBefore), "The soldier's authored per-level growth has to raise its stats, not only its level number.");
	}

	[Test]
	public void LosingTheHeroClearsEveryProductionQueue()
	{
		UnitId building = _match.PlaceCompletedBuilding(SitePosition);
		Assert.That(_match.StepUntilSoldiers(1), Is.EqualTo(1), "The building has to raise its first soldier first.");

		_match.Api.SetUnitChargeCount(building, 1);
		_match.Step(0.2f);

		_match.Kill(_match.Hero(1), _match.Hero(2));
		_match.Step(10f);

		Assert.That(_match.Soldiers(1).Count, Is.EqualTo(0), "A queue that was cleared by the hero's death must not field a soldier while the hero is gone.");

		_match.Step(15f);
		Assert.That(_match.Soldiers(1).Count, Is.GreaterThan(0), "Production has to resume once the hero revives.");
	}

	[Test]
	public void ABuildingStopsProducingOnceItsCapacityIsFilled()
	{
		UnitId building = _match.PlaceCompletedBuilding(SitePosition);
		string templateId = _match.TemplateOf(building);
		string soldierType = MeleeContent.SoldierFor(templateId);
		int capacity = MeleeContent.CapacityOf(templateId);

		int produced = _match.StepUntilSoldiers(1);
		Assert.That(produced, Is.EqualTo(1), "The building has to raise its first soldier from its starting charge.");

		for (int index = produced; index < capacity; index++)
		{
			_match.SpawnSoldier(1, soldierType, new Vector3(-40f + index * 1.5f, 0f, -56f));
		}

		Assert.That(_match.Soldiers(1).Count, Is.EqualTo(capacity), "The test has to fill the building's capacity first.");

		_match.Step(30f);

		Assert.That(_match.Soldiers(1).Count, Is.EqualTo(capacity), "A full capacity slot must stop the building from spending a charge.");
		Assert.That(_match.CountCharges(building), Is.EqualTo(MeleeContent.MaximumChargesOf(templateId)), "The charge the building recharged has to wait for a free capacity slot.");
	}

	[Test]
	public void AChargedBuildingRechargesOnItsAuthoredClock()
	{
		UnitId building = _match.PlaceCompletedBuilding(SitePosition);
		string templateId = _match.TemplateOf(building);

		Assert.That(_match.CountCharges(building), Is.EqualTo(1), "A finished building opens with one charge.");

		int produced = _match.StepUntilSoldiers(1);
		Assert.That(produced, Is.EqualTo(1), "The first charge raises a soldier.");
		Assert.That(_match.CountCharges(building), Is.EqualTo(MeleeContent.MaximumChargesOf(templateId) - 1), "Raising that soldier spends the charge.");

		string soldierType = MeleeContent.SoldierFor(templateId);
		for (int index = produced; index < MeleeContent.CapacityOf(templateId); index++)
		{
			_match.SpawnSoldier(1, soldierType, new Vector3(-40f + index * 1.5f, 0f, -56f));
		}

		_match.Step(MeleeContent.RechargeSecondsOf(templateId) - 1f);
		Assert.That(_match.CountCharges(building), Is.EqualTo(MeleeContent.MaximumChargesOf(templateId) - 1), "The charge returns on its authored recharge clock, not earlier.");

		_match.Step(2f);
		Assert.That(_match.CountCharges(building), Is.GreaterThan(MeleeContent.MaximumChargesOf(templateId) - 1), "The recharged charge has to be on the building once its clock runs out.");
	}

	[Test]
	public void LosingTheHeroDestroysTheArmy()
	{
		UnitId building = _match.PlaceCompletedBuilding(SitePosition);
		_match.SpawnSoldier(1, MeleeContent.SoldierFor(_match.TemplateOf(building)), new Vector3(-44f, 0f, -54f));
		Assert.That(_match.Soldiers(1).Count, Is.GreaterThan(0), "The player has to have an army before it can lose one.");

		UnitId hero = _match.Hero(1);
		_match.Kill(hero, _match.Hero(2));

		Assert.That(_match.IsAlive(hero), Is.False, "The hero has to die from the damage.");
		Assert.That(_match.Soldiers(1).Count, Is.EqualTo(0), "Losing the hero destroys the whole army.");
		Assert.That(_match.Buildings(1).Count, Is.EqualTo(2), "The buildings survive the hero they raised an army for.");
	}

	[Test]
	public void TheHeroReturnsAtTheNexusWithItsBuildingsRecharged()
	{
		UnitId building = _match.PlaceCompletedBuilding(SitePosition);
		string templateId = _match.TemplateOf(building);

		_match.StepUntilSoldiers(1);
		Assert.That(_match.CountCharges(building), Is.EqualTo(MeleeContent.MaximumChargesOf(templateId) - 1), "The building spends a charge on its first soldier.");

		_match.Kill(_match.Hero(1), _match.Hero(2));
		_match.Step(11.5f);
		Assert.That(_match.IsAlive(_match.Hero(1)), Is.False, "The first revival takes twelve seconds.");

		_match.Step(1f);
		UnitId revived = _match.Hero(1);
		Assert.That(_match.IsAlive(revived), Is.True, "The hero has to return when its revival timer expires.");
		Assert.That(_match.HeroLevel(1), Is.EqualTo(1), "A revived hero starts at level one again.");
		Assert.That(_match.CountCharges(building), Is.EqualTo(MeleeContent.MaximumChargesOf(templateId)), "A revival refills every soldier-building to its maximum charges.");
	}

	[Test]
	public void EachRevivalTakesThreeSecondsLongerThanTheLast()
	{
		_match.Kill(_match.Hero(1), _match.Hero(2));
		_match.Step(12.5f);
		Assert.That(_match.IsAlive(_match.Hero(1)), Is.True, "The first revival happens twelve seconds after the death.");

		_match.Kill(_match.Hero(1), _match.Hero(2));
		_match.Step(14.5f);
		Assert.That(_match.IsAlive(_match.Hero(1)), Is.False, "The second revival waits fifteen seconds, not twelve.");

		_match.Step(1.5f);
		Assert.That(_match.IsAlive(_match.Hero(1)), Is.True, "The second hero returns once fifteen seconds have passed.");
	}

	[Test]
	public void TheHeroRevivesAtADestroyedNexusWhileAnAllyHoldsTheTeam()
	{
		using MeleeMatch allied = MeleeMatch.StartWithTeams(new[] { "Vale", "Sable" }, new[] { 1, 1 });
		Vector3 lostNexusPosition = allied.PositionOf(allied.Nexus(2));

		allied.Kill(allied.Nexus(2), allied.Hero(1));
		allied.Step(0.2f);
		Assert.That(allied.ReadPlayer(2).Active, Is.True, "A seat whose ally still holds a Nexus stays in the match.");

		allied.Kill(allied.Hero(2), allied.Hero(1));
		allied.Step(12.5f);

		UnitId revived = allied.Hero(2);
		Assert.That(allied.IsAlive(revived), Is.True, "The hero of a seat that lost its Nexus still revives.");
		Assert.That(Vector3.Distance(allied.PositionOf(revived), lostNexusPosition), Is.LessThan(8f), "The revival has to happen where the destroyed Nexus stood.");
	}

	[Test]
	public void DestroyingANexusEliminatesItsPlayerAndEndsTheMatch()
	{
		UnitId nexus = _match.Nexus(2);
		_match.Kill(nexus, _match.Hero(1));

		Assert.That(_match.IsEliminated(2), Is.True, "A player whose Nexus falls is eliminated.");
		Assert.That(_match.Simulation.IsGameOver, Is.True, "The last team holding a Nexus wins the match.");
		Assert.That(_match.CountEventsFor(2, Realm.Shared.Network.PresentationKind.PlayerDefeated), Is.GreaterThan(0), "The eliminated player has to be told.");
		Assert.That(_match.CountEventsFor(1, Realm.Shared.Network.PresentationKind.PlayerVictory), Is.GreaterThan(0), "The last player standing has to be told it won.");
		Assert.That(_match.CountEventsFor(2, Realm.Shared.Network.PresentationKind.PlayerVictory), Is.EqualTo(0), "A defeated player must not be handed the victory.");
	}

}
