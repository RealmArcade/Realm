using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays the inspector on the authority: the rows the ruleset publishes for a selected building, soldier and hero,
///     including the capacity a building is holding, the formation and targeting rule a soldier fights with, the source
///     building its charges come from, the hero's innate and relic slots, the offers of an open draft and the bulwark it
///     has earned. The rows are read from the presentation stream the client's stat panel consumes.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeInspectorReadoutTests
{
	private static readonly Vector3 SitePosition = new(-50f, 0f, -50f);
	private static readonly Vector3 SoldierPosition = new(-46f, 0f, -52f);

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
	public void ABuildingReportsWhatItUnlocksItsCapacityItsIncomeAndItsProduction()
	{
		UnitId building = _match.PlaceCompletedBuilding(SitePosition);
		string templateId = _match.TemplateOf(building);
		string soldierName = _match.Api.GetUnitName(MeleeContent.SoldierFor(templateId));
		int capacity = MeleeContent.CapacityOf(templateId);

		_match.Step(0.2f);

		Assert.That(_match.UnitReadout(1, building, 0), Is.EqualTo("Unlocks"), "The first building row is the unlock.");
		Assert.That(_match.UnitReadout(1, building, 0, valueColumn: true), Is.EqualTo(soldierName), "The building has to name the soldier it raises.");
		Assert.That(_match.UnitReadout(1, building, 1), Is.EqualTo("Capacity"), "The building has to report the capacity it holds.");
		Assert.That(_match.UnitReadout(1, building, 1, valueColumn: true), Is.EqualTo($"0 / {capacity}"), "A finished building starts empty.");
		Assert.That(_match.UnitReadout(1, building, 2, valueColumn: true), Is.Not.Empty, "The building has to report its income.");
		Assert.That(_match.UnitReadout(1, building, 3), Is.EqualTo("Production"), "The building has to report what its production is doing.");
		Assert.That(_match.UnitReadout(1, building, 3, valueColumn: true), Is.Not.Empty, "The production row needs a state.");
	}

	[Test]
	public void ABuildingUpdatesItsCapacityWhenItsSoldierArrives()
	{
		UnitId building = _match.PlaceCompletedBuilding(SitePosition);
		string templateId = _match.TemplateOf(building);
		int capacity = MeleeContent.CapacityOf(templateId);

		_match.StepUntilSoldiers(1);
		_match.Step(0.2f);

		Assert.That(_match.UnitReadout(1, building, 1, valueColumn: true), Is.EqualTo($"1 / {capacity}"), "A building that raised a soldier has to report the slot it filled.");
	}

	[Test]
	public void ASoldierReportsItsFormationItsTargetingRuleAndItsSourceBuilding()
	{
		UnitId shieldbearer = _match.SpawnSoldier(1, "unit/shieldbearer", SoldierPosition);
		UnitId spear = _match.SpawnSoldier(1, "unit/spear_militia", SoldierPosition + new Vector3(2f, 0f, 0f));
		UnitId bowman = _match.SpawnSoldier(1, "unit/bowman", SoldierPosition + new Vector3(4f, 0f, 0f));
		_match.Step(0.1f);

		Assert.That(_match.UnitReadout(1, shieldbearer, 0, valueColumn: true), Is.EqualTo("Front"), "A Shieldbearer belongs in the front.");
		Assert.That(_match.UnitReadout(1, shieldbearer, 1, valueColumn: true), Is.EqualTo("Standard"), "A Shieldbearer fights the nearest enemy.");
		Assert.That(_match.UnitReadout(1, shieldbearer, 2, valueColumn: true), Is.EqualTo(_match.Api.GetUnitName("building/bulwark_hall")), "A soldier has to name the building that raised it.");

		Assert.That(_match.UnitReadout(1, spear, 0, valueColumn: true), Is.EqualTo("Front"), "A Spear Militia belongs in the front.");
		Assert.That(_match.UnitReadout(1, spear, 1, valueColumn: true), Is.EqualTo("Tank-Shredder"), "A Spear Militia has to report its Tank-Shredder rule.");
		Assert.That(_match.UnitReadout(1, spear, 2, valueColumn: true), Is.EqualTo(_match.Api.GetUnitName("building/spear_lodge")), "The Spear Militia has to name its own building.");

		Assert.That(_match.UnitReadout(1, bowman, 0, valueColumn: true), Is.EqualTo("Back"), "A Bowman belongs in the back.");
		Assert.That(_match.UnitReadout(1, bowman, 1, valueColumn: true), Is.EqualTo("Assassin"), "A Bowman has to report its Assassin rule.");
		Assert.That(_match.UnitReadout(1, bowman, 2, valueColumn: true), Is.EqualTo(_match.Api.GetUnitName("building/archery_range")), "The Bowman has to name its own building.");
	}

	[Test]
	public void ASoldierReportsTheChargesItsSourceBuildingHasLeft()
	{
		UnitId building = _match.PlaceCompletedBuilding(SitePosition);
		string templateId = _match.TemplateOf(building);
		int maximumCharges = MeleeContent.MaximumChargesOf(templateId);
		string soldierType = MeleeContent.SoldierFor(templateId);

		_match.StepUntilSoldiers(1);
		_match.Step(0.2f);

		UnitId soldier = UnitId.Invalid;
		foreach (UnitId candidate in _match.Soldiers(1))
		{
			if (_match.TemplateOf(candidate) == soldierType) soldier = candidate;
		}

		Assert.That(soldier.IsValid, Is.True, "The building has to raise its authored soldier.");
		Assert.That(_match.UnitReadout(1, soldier, 3), Is.EqualTo("Charges"), "A soldier has to report the charge row.");
		Assert.That(_match.UnitReadout(1, soldier, 3, valueColumn: true), Is.EqualTo($"0 / {maximumCharges}"), "Raising the soldier spends the building's first charge.");
	}

	[Test]
	public void AHeroReportsItsInnateAndItsRelicSlots()
	{
		UnitId hero = _match.Hero(1);
		_match.Step(0.2f);

		Assert.That(_match.UnitReadout(1, hero, 0), Is.EqualTo("Innate"), "The hero has to report its innate.");
		Assert.That(_match.UnitReadout(1, hero, 0, valueColumn: true), Is.EqualTo("Unbroken"), "Vale's hero carries the Unbroken innate.");
		Assert.That(_match.UnitReadout(1, hero, 1), Is.EqualTo("Relics"), "The hero has to report its relic slots.");
		Assert.That(_match.UnitReadout(1, hero, 1, valueColumn: true), Is.EqualTo("0 / 6"), "A fresh hero has all six relic slots open.");
	}

	[Test]
	public void AHeroReportsTheBulwarkItHasEarned()
	{
		UnitId hero = _match.Hero(1);
		_match.Step(1.2f);

		Assert.That(_match.UnitReadout(1, hero, 2), Is.EqualTo("Bulwark"), "The hero has to report the bulwark it is carrying.");
		Assert.That(_match.UnitReadout(1, hero, 2, valueColumn: true), Does.Contain("buildings"), "A lone Nexus is still the building the bulwark counts.");
	}

	[Test]
	public void AnOpenDraftReportsTheOffersAndClearsThemWhenItCloses()
	{
		UnitId hero = _match.Hero(1);
		_match.Step(0.2f);
		MeleeTestMaps.RequireMaps();

		DraftOffer offer = _match.BeginConstruction(1, SitePosition);
		_match.Step(0.2f);

		Assert.That(offer.AbilityIds.Length, Is.EqualTo(3), "The default draw offers three packages.");
		Assert.That(_match.UnitReadout(1, hero, 3), Is.EqualTo("Offered"), "The first offer row has to be labelled.");
		Assert.That(_match.UnitReadout(1, hero, 3, valueColumn: true), Is.Not.Empty, "An open draft has to name its first offer.");
		Assert.That(_match.UnitReadout(1, hero, 4, valueColumn: true), Is.Not.Empty, "An open draft has to name its second offer.");
		Assert.That(_match.UnitReadout(1, hero, 5, valueColumn: true), Is.Not.Empty, "An open draft has to name its third offer.");

		_match.AbandonDraft(1);
		_match.Step(0.2f);

		Assert.That(_match.UnitReadout(1, hero, 3, valueColumn: true), Is.Empty, "A closed draft must clear the offers it showed.");
		Assert.That(_match.UnitReadout(1, hero, 4, valueColumn: true), Is.Empty, "A closed draft must clear every offer row.");
		Assert.That(_match.UnitReadout(1, hero, 5, valueColumn: true), Is.Empty, "A closed draft must clear every offer row.");
	}

	[Test]
	public void TheReadoutRowsReachTheSeatThroughThePresentationStream()
	{
		UnitId building = _match.PlaceCompletedBuilding(SitePosition);
		_match.Step(0.2f);

		Assert.That(_match.CountEventsFor(1, Realm.Shared.Network.PresentationKind.TooltipTextSet), Is.GreaterThan(0), "The inspector rows have to leave the authority as tooltip text the client stores.");
		Assert.That(_match.UnitReadout(1, building, 0, valueColumn: true), Is.Not.Empty, "The seat has to receive the unlock row itself, not just an event count.");
	}
}
