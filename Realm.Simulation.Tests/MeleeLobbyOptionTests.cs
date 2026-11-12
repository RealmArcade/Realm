using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Shared.Simulation;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays a match with the parameters a lobby's host configured: how many building options a draw offers, how much
///     building supply a player holds, the capacity and charge multipliers, and the vision mode the authority publishes
///     with. A lobby that configures nothing plays the ruleset as it was authored, which the default tests cover.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeLobbyOptionTests
{
	private static readonly Vector3 FirstSite = new(-50f, 0f, -50f);
	private static readonly Vector3 SecondSite = new(-44f, 0f, -50f);

	private MeleeMatch _match = null!;

	[TearDown]
	public void TearDown()
	{
		_match?.Dispose();
	}

	[Test]
	public void TheHostSetsHowManyBuildingOptionsADrawOffers()
	{
		var options = new MatchOptionSet();
		options.Set(MatchOption.MaximumBuildingDraftCount, 1);
		MeleeTestMaps.RequireMaps();
		_match = MeleeMatch.StartDuel(matchOptions: options);

		DraftOffer offer = _match.BeginConstruction(1, FirstSite);

		Assert.That(offer.AbilityIds.Length, Is.EqualTo(1), "A host that asks for one option is offered exactly one building.");
	}

	[Test]
	public void TheHostSetsTheBuildingSupply()
	{
		var options = new MatchOptionSet();
		options.Set(MatchOption.MaximumBuildingSupply, 1);
		MeleeTestMaps.RequireMaps();
		_match = MeleeMatch.StartDuel(matchOptions: options);

		UnitId firstBuilding = _match.PlaceDraftBuilding(FirstSite);
		Assert.That(_match.IsAlive(firstBuilding), Is.True, "The first construction has to be placed.");

		_match.SetWood(1, 500f);
		DraftOffer second = _match.BeginConstruction(1, SecondSite);

		Assert.That(second.AbilityIds, Is.Empty, "A player at the host's building supply may not be offered another draw.");
		Assert.That(_match.DraftOptions(1), Is.Empty, "The command grid stays closed at the host's building supply.");
		Assert.That(_match.LastFeedbackText(1), Does.Contain("1 building locations"), "The refusal has to name the supply the host stated.");
	}

	[Test]
	public void TheHostMultipliesStoredProductions()
	{
		var options = new MatchOptionSet();
		options.Set(MatchOption.ChargeMultiplierPercent, 200);
		MeleeTestMaps.RequireMaps();
		_match = MeleeMatch.StartDuel(matchOptions: options);

		UnitId building = _match.PlaceCompletedBuilding(FirstSite);
		string templateId = _match.TemplateOf(building);
		int authoredProductions = MeleeContent.SoldierProductionsOf(templateId);

		Assert.That(_match.MaxManaOf(building), Is.EqualTo((authoredProductions * 2 + MeleeContent.WorkerProductionsOf(templateId)) * MeleeContent.ProductionMana),
			"Doubling the charge multiplier doubles the production mana a building stores, so it can hold twice the soldiers' worth.");
	}

	[Test]
	public void TheHostMultipliesSoldierCapacity()
	{
		var options = new MatchOptionSet();
		options.Set(MatchOption.CapacityMultiplierPercent, 200);
		MeleeTestMaps.RequireMaps();
		_match = MeleeMatch.StartDuel(matchOptions: options);

		UnitId building = _match.PlaceCompletedBuilding(FirstSite);
		string templateId = _match.TemplateOf(building);
		string soldierType = MeleeContent.SoldierFor(templateId);
		int authoredCapacity = MeleeContent.CapacityOf(templateId);

		int produced = _match.StepUntilSoldiers(1);
		Assert.That(produced, Is.EqualTo(1), "The building raises its first soldier from its starting production.");

		float waited = 0f;
		while (waited < 120f)
		{
			_match.Api.SetUnitMana(building, _match.MaxManaOf(building));
			_match.Step(1f);
			waited += 1f;
		}

		IReadOnlyList<UnitId> soldiers = _match.Soldiers(1);
		Assert.That(soldiers.Count, Is.EqualTo(authoredCapacity * 2), "Doubling the capacity multiplier doubles the soldiers one building supports.");

		for (int index = 0; index < soldiers.Count; index++)
		{
			Assert.That(_match.TemplateOf(soldiers[index]), Is.EqualTo(soldierType), "Every soldier the building raises is the one its profile states.");
		}
	}

	[Test]
	public void TheHostRevealsTheBoardToEveryPlayer()
	{
		var options = new MatchOptionSet();
		options.Set(MatchOption.ShroudMode, (int)MatchShroudMode.Revealed);
		MeleeTestMaps.RequireMaps();
		_match = MeleeMatch.StartDuel(matchOptions: options);

		Assert.That(_match.Simulation.Map.Simulation.ShroudDisabled, Is.True, "A host that revealed the board leaves the authority publishing every unit to every peer.");
	}

	[Test]
	public void AStandardLobbyPlaysTheAuthoredRuleset()
	{
		MeleeTestMaps.RequireMaps();
		_match = MeleeMatch.StartDuel();

		Assert.That(_match.Simulation.MatchOptions.IsEmpty, Is.True, "A lobby that configured nothing states no option at all.");
		Assert.That(_match.Simulation.Map.Simulation.ShroudDisabled, Is.False, "The map's own shroud mode applies when the host states none.");

		DraftOffer offer = _match.BeginConstruction(1, FirstSite);
		Assert.That(offer.AbilityIds.Length, Is.EqualTo(3), "The authored draw offers three buildings.");
	}

	[Test]
	public void TheAuthorityReportsAnOptionBackToTheScript()
	{
		var options = new MatchOptionSet();
		options.Set(MatchOption.MaximumUniqueBuildingInstances, 4);
		MeleeTestMaps.RequireMaps();
		_match = MeleeMatch.StartDuel(matchOptions: options);

		Assert.That(_match.Api.GetMatchOption(MatchOption.MaximumUniqueBuildingInstances), Is.EqualTo(4), "The script reads the value the host set.");
		Assert.That(_match.Api.GetMatchOption(MatchOption.CapacityMultiplierPercent), Is.Zero, "An option the host left alone answers zero, so the map's own default applies.");
		Assert.That(_match.Api.MatchOptions.Get(MatchOption.MaximumUniqueBuildingInstances), Is.EqualTo(4), "The authority holds the options the lobby configured.");
	}
}
