using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Tags;
using Realm.Shared.Simulation;

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
	public void LosingTheHeroStopsProductionUntilItRevives()
	{
		UnitId building = _match.PlaceCompletedBuilding(SitePosition);
		Assert.That(_match.StepUntilSoldiers(1), Is.EqualTo(1), "The building has to raise its first soldier first.");

		_match.Api.SetUnitMana(building, _match.MaxManaOf(building));
		_match.Step(0.2f);

		_match.Kill(_match.Hero(1), _match.Hero(2));
		_match.Step(10f);

		Assert.That(_match.Soldiers(1).Count, Is.EqualTo(0), "A building whose hero is gone must not field a soldier, even with the mana to pay for one.");

		_match.Step(15f);
		Assert.That(_match.Soldiers(1).Count, Is.GreaterThan(0), "Production has to resume once the hero revives.");
	}

	[Test]
	public void ASoldierShowsItsAbilitiesButTakesNoPlayerOrder()
	{
		string menderLabel = _match.Api.GetUnitName("building/mender_shrine");
		UnitId building = _match.PlaceCompletedBuilding(SitePosition, label => label == menderLabel);
		Assert.That(_match.TemplateOf(building), Is.EqualTo("building/mender_shrine"), "The test needs the package whose soldier carries an ability card.");

		Assert.That(_match.StepUntilSoldiers(1), Is.EqualTo(1), "The shrine has to raise its mender.");
		UnitId soldier = _match.Soldiers(1)[0];

		Assert.That(_match.Has<PlayerOrdersLocked>(soldier), Is.True, "A soldier the map drives has to be locked to it.");

		var abilities = _match.Api.GetUnitTypeAbilities(_match.TemplateOf(soldier));
		Assert.That(abilities, Does.Contain("ability/mend"), "Selecting the soldier still has to list the card its tooltip describes.");
		foreach (string abilityId in abilities)
		{
			Assert.That(_match.Api.Abilities.IsUnitAbilityHidden(soldier.UniqueId, abilityId), Is.False, $"'{abilityId}' has to stay on the soldier's card so the player can read what the army does.");
		}

		ServerCommandResult order = _match.Order(1, "move", new Vector3(-40f, 0f, -60f), soldier);
		Assert.That(order.HasRejection, Is.True, "A soldier the map drives may not be walked around by its player.");
		Assert.That(order.RejectionReason, Does.Contain("driven by its map"), "The refusal has to say why the soldier did not move.");
	}

	[Test]
	public void EveryBuildingConsumesOneOfTheEightSupply()
	{
		int foodCap = _match.MaxPopulationOf(1);
		Assert.That(foodCap, Is.EqualTo(8), "The ruleset's building supply cap has to be the one the seat is given.");

		int before = _match.PopulationOf(1);
		UnitId building = _match.PlaceCompletedBuilding(SitePosition);
		_match.Step(1f);
		int after = _match.PopulationOf(1);

		Assert.That(_match.IsAlive(building), Is.True, "The building has to stand for its supply to be counted.");
		Assert.That(after - before, Is.EqualTo(1), "A building has to consume the one supply it costs, or a player could raise buildings without limit.");
	}

	[Test]
	public void ABuildingStopsProducingOnceItsCapacityIsFilled()
	{
		UnitId building = _match.PlaceCompletedBuilding(SitePosition);
		string templateId = _match.TemplateOf(building);
		string soldierType = MeleeContent.SoldierFor(templateId);
		int capacity = MeleeContent.CapacityOf(templateId);

		int produced = _match.StepUntilSoldiers(1);
		Assert.That(produced, Is.EqualTo(1), "The building has to raise its first soldier from the mana it stores.");

		for (int index = produced; index < capacity; index++)
		{
			_match.SpawnSoldier(1, soldierType, new Vector3(-40f + index * 1.5f, 0f, -56f));
		}

		Assert.That(_match.Soldiers(1).Count, Is.EqualTo(capacity), "The test has to fill the building's capacity first.");

		_match.Step(MeleeContent.SoldierProductionSecondsOf(templateId) + 1f);

		Assert.That(_match.Soldiers(1).Count, Is.EqualTo(capacity), "A full capacity slot must stop the building from raising another soldier, whatever mana it has left.");
		Assert.That(_match.IsAbilityDisabled(building, MeleeContent.SoldierAbilityFor(templateId)), Is.True, "The ruleset has to switch the soldier ability off while its capacity is full.");
	}

	[Test]
	public void ABuildingRefillsItsManaOnItsAuthoredClock()
	{
		UnitId building = _match.PlaceCompletedBuilding(SitePosition);
		string templateId = _match.TemplateOf(building);

		int produced = _match.StepUntilSoldiers(1);
		Assert.That(produced, Is.EqualTo(1), "The building has to raise a soldier from the mana it stores.");

		string soldierType = MeleeContent.SoldierFor(templateId);
		for (int index = produced; index < MeleeContent.CapacityOf(templateId); index++)
		{
			_match.SpawnSoldier(1, soldierType, new Vector3(-40f + index * 1.5f, 0f, -56f));
		}

		_match.Api.SetUnitMana(building, 0f);
		float regen = _match.ManaRegenOf(building);
		_match.Step(5f);

		Assert.That(_match.ManaOf(building), Is.EqualTo(regen * 5f).Within(2f), "A building's mana has to return at the rate of one soldier and one worker production per their authored clocks.");
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
	public void TheHeroReturnsAtTheNexusWithItsBuildingsRefilled()
	{
		UnitId building = _match.PlaceCompletedBuilding(SitePosition);
		string templateId = _match.TemplateOf(building);

		// Both production lines are switched off so the refill can be read before an auto-cast spends it.
		_match.Api.SetAbilityAutoCast(building, MeleeContent.SoldierAbilityFor(templateId), false);
		_match.Api.SetAbilityAutoCast(building, MeleeContent.WorkerAbilityFor(templateId), false);
		_match.Api.SetUnitMana(building, 0f);

		_match.Kill(_match.Hero(1), _match.Hero(2));
		_match.Step(11.5f);
		Assert.That(_match.IsAlive(_match.Hero(1)), Is.False, "The first revival takes twelve seconds.");

		_match.Step(1f);
		UnitId revived = _match.Hero(1);
		Assert.That(_match.IsAlive(revived), Is.True, "The hero has to return when its revival timer expires.");
		Assert.That(_match.HeroLevel(1), Is.EqualTo(1), "A revived hero starts at level one again.");
		Assert.That(_match.ManaOf(building), Is.EqualTo(_match.MaxManaOf(building)).Within(0.5f), "A revival refills every building's production mana to its maximum.");
	}

	[Test]
	public void TheDeathTellsThePlayerWhenTheHeroReturns()
	{
		_match.Kill(_match.Hero(1), _match.Hero(2));
		_match.Step(0.2f);

		Assert.That(_match.LastFeedbackText(1), Does.Contain("12"), "The death has to state how long the revival takes.");

		_match.Step(12.5f);
		Assert.That(_match.LastFeedbackText(1), Does.Contain("returns"), "The revival has to tell the player the hero is back.");
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
