using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays the shared building deck from the authority: the draw the construction reveals in the command grid, the
///     package the player casts, the site it leaves behind and the production that site starts when the engine announces
///     it finished.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeConstructionDraftTests
{
	private const float ShortestAuthoredConstructionSeconds = 30f;
	private const float LongestAuthoredConstructionSeconds = 35f;
	private const int MaximumBuildingsPerPlayer = 5;

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

	private static Vector3 SitePosition => new(-50f, 0f, -50f);

	[Test]
	public void EveryDraftCommandComparesItsSoldierChargesRechargeAndIncome()
	{
		var tooltips = new List<string>();

		foreach (string buildingTemplateId in MeleeContent.DraftableBuildings)
		{
			string tooltip = _match.AbilityTooltip(1, buildingTemplateId);
			string soldierName = _match.Api.GetUnitName(MeleeContent.SoldierFor(buildingTemplateId));

			Assert.That(tooltip, Is.Not.Empty, $"The draft command for '{buildingTemplateId}' has to explain what it raises.");
			Assert.That(tooltip, Does.Contain(soldierName), $"The draft command for '{buildingTemplateId}' has to name the soldier it raises, so two packages can be told apart before one is locked.");
			Assert.That(tooltip, Does.Match(@"\d+ charges every \d+s\."), $"The draft command for '{buildingTemplateId}' has to state its charge count and its recharge: '{tooltip}'");
			Assert.That(tooltip, Does.Match(@"Income (\d+(\.\d+)? gold/min|\d+(\.\d+)? wood/min)\.$"), $"The draft command for '{buildingTemplateId}' has to state the income it adds: '{tooltip}'");
			tooltips.Add(tooltip);
		}

		Assert.That(tooltips, Has.Some.Contains("gold/min"), "The deck compares gold buildings as well as resource ones.");
		Assert.That(tooltips, Has.Some.Contains("wood/min"), "The deck compares wood buildings as well as gold ones.");
		Assert.That(tooltips.Distinct().Count(), Is.EqualTo(tooltips.Count), "No two draft commands may read the same, or the choice cannot be made from the tooltips.");
	}

	[Test]
	public void BeginConstructionChargesTheWoodAndOffersADrawOfThree()
	{
		_match.SetWood(1, 500f);

		DraftOffer offer = _match.BeginConstruction(1, SitePosition);

		Assert.That(offer.AbilityIds, Has.Length.EqualTo(3), "A draw offers three packages in the command grid.");
		Assert.That(offer.AbilityIds, Has.All.Not.Empty, "Every offered package has to be a command the grid can draw.");
		Assert.That(_match.Wood(1), Is.EqualTo(400f).Within(0.01f), "The 100 wood is charged when the draw is made.");
		Assert.That(_match.IsDraftAbandonVisible(1), Is.True, "The draw has to offer a way to give the wood back.");
		Assert.That(_match.Buildings(1).Count, Is.EqualTo(1), "Nothing is placed until the player picks.");
	}

	[Test]
	public void TheDraftIsOnlyInTheGridWhileAConstructionIsOpen()
	{
		foreach (string buildingTemplateId in MeleeContent.DraftableBuildings)
		{
			Assert.That(_match.IsAbilityVisible(_match.Hero(1), buildingTemplateId), Is.False, $"{buildingTemplateId} must not be offered before a construction begins.");
		}

		Assert.That(_match.IsDraftAbandonVisible(1), Is.False, "Abandoning a construction that does not exist has nothing to offer.");

		_match.SetWood(1, 500f);
		DraftOffer offer = _match.BeginConstruction(1, SitePosition);

		UnitId hero = _match.Hero(1);
		UnitId nexus = _match.Nexus(1);
		foreach (string buildingTemplateId in offer.AbilityIds)
		{
			Assert.That(_match.IsAbilityVisible(hero, buildingTemplateId), Is.True, "The selected hero has to show the packages the deck drew.");
			Assert.That(_match.IsAbilityVisible(nexus, buildingTemplateId), Is.True, "The Nexus has to show the same packages, because it carries the same grid.");
		}

		_match.AbandonDraft(1);

		foreach (string buildingTemplateId in MeleeContent.DraftableBuildings)
		{
			Assert.That(_match.IsAbilityVisible(hero, buildingTemplateId), Is.False, $"{buildingTemplateId} has to leave the grid once the draft ends.");
		}

		Assert.That(_match.IsDraftAbandonVisible(1), Is.False, "The abandon command belongs to the open draft only.");
	}

	[Test]
	public void ASecondDrawIsRefusedWhileTheDraftIsOpen()
	{
		_match.SetWood(1, 500f);

		DraftOffer offer = _match.BeginConstruction(1, SitePosition);

		_match.Cast(1, MeleeMatch.BeginConstructionAbilityId, new Vector3(-40f, 0f, -50f));

		Assert.That(_match.Wood(1), Is.EqualTo(400f).Within(0.01f), "A refused draw costs nothing.");
		Assert.That(_match.DraftOptions(1), Is.EquivalentTo(offer.AbilityIds), "A refused draw may not replace the packages already offered.");

		_match.AbandonDraft(1);
	}

	[Test]
	public void PickingTheDrawPlacesASiteThatBuildsOnItsOwn()
	{
		_match.SetWood(1, 500f);
		DraftOffer offer = _match.BeginConstruction(1, SitePosition);

		_match.LockDraftOption(1, offer.AbilityIds[0], SitePosition);

		UnitId site = FindSite(1);
		Assert.That(site.IsValid, Is.True, "Picking a package has to leave a construction site.");
		Assert.That(_match.TemplateOf(site), Is.EqualTo(offer.AbilityIds[0]), "The site has to be the package the player cast.");
		Assert.That(_match.Wood(1), Is.EqualTo(400f).Within(0.01f), "Picking a package must not charge a second time.");
		Assert.That(_match.DraftOptions(1), Is.Empty, "The grid goes back to offering nothing once the draw is answered.");
		Assert.That(_match.IsDraftAbandonVisible(1), Is.False, "An answered draw has nothing left to abandon.");
		Assert.That(_match.ConstructionProgress(site), Is.LessThan(0.05f), "A fresh site starts at the beginning of its work.");

		_match.Step(ShortestAuthoredConstructionSeconds - 1f);
		Assert.That(_match.IsUnderConstruction(site), Is.True, "Nothing is working the site, so it must not finish early.");
		Assert.That(_match.CountCharges(site), Is.EqualTo(0), "A site holds no charge while it is being built.");

		_match.Step(ShortestAuthoredConstructionSeconds + 6f);
		Assert.That(_match.IsUnderConstruction(site), Is.False, "The map's own clock finishes the site within the authored window.");
		Assert.That(_match.Has<Building>(site), Is.True, "The finished site is an ordinary building.");
	}

	[Test]
	public void AFinishedBuildingStartsWithOneChargeAndKeepsItUntilItsSoldierIsReady()
	{
		_match.SetWood(1, 500f);
		DraftOffer offer = _match.BeginConstruction(1, SitePosition);
		_match.LockDraftOption(1, offer.AbilityIds[0], SitePosition);
		UnitId site = FindSite(1);

		float secondsToFinish = WaitForCompletion(site);
		Assert.That(secondsToFinish, Is.InRange(ShortestAuthoredConstructionSeconds - 1f, LongestAuthoredConstructionSeconds + 1f), "A drafted site has to finish within its authored construction time.");
		Assert.That(_match.CountCharges(site), Is.EqualTo(1), "A finished building starts with one charge.");
		Assert.That(_match.Soldiers(1).Count, Is.EqualTo(0), "The first soldier is raised on the building's own production timer.");

		_match.Step(10f);
		Assert.That(_match.Soldiers(1).Count, Is.EqualTo(1), "The charge the building started with has to become its first soldier.");
		Assert.That(_match.CountCharges(site), Is.EqualTo(0), "Raising the soldier spends the charge.");
	}

	[Test]
	public void AFinishedBuildingRaisesItsSoldierAndSpendsItsCharge()
	{
		_match.SetWood(1, 500f);
		DraftOffer offer = _match.BeginConstruction(1, SitePosition);
		_match.LockDraftOption(1, offer.AbilityIds[0], SitePosition);
		UnitId site = FindSite(1);

		WaitForCompletion(site);
		Assert.That(_match.Soldiers(1).Count, Is.EqualTo(0), "A building produces on its own timer, not the moment it finishes.");

		_match.Step(12f);

		Assert.That(_match.Soldiers(1).Count, Is.EqualTo(1), "The building has to raise its soldier from its charge.");
		Assert.That(_match.CountCharges(site), Is.EqualTo(0), "Producing a soldier spends the charge it was produced from.");
		Assert.That(_match.TemplateOf(_match.Soldiers(1)[0]), Is.EqualTo(MeleeContent.SoldierFor(_match.TemplateOf(site))), "The soldier has to be the one its building grants.");
	}

	[Test]
	public void AbandoningTheDrawReturnsTheWoodAndLeavesNoSite()
	{
		_match.SetWood(1, 500f);
		_match.BeginConstruction(1, SitePosition);

		_match.AbandonDraft(1);

		Assert.That(_match.Wood(1), Is.EqualTo(500f).Within(0.01f), "Abandoning the construction has to give the 100 wood back.");
		Assert.That(_match.Buildings(1).Count, Is.EqualTo(1), "An abandoned draw may not leave a site behind.");
		Assert.That(_match.DraftOptions(1), Is.Empty, "The grid has to close.");

		_match.BeginConstruction(1, SitePosition);
		Assert.That(_match.DraftOptions(1), Is.Not.Empty, "A player who abandoned a draw may draw again.");
	}

	[Test]
	public void APackageTheDrawDidNotOfferLocksNothing()
	{
		_match.SetWood(1, 500f);
		DraftOffer offer = _match.BeginConstruction(1, SitePosition);

		string unoffered = Array.Find(MeleeContent.DraftableBuildings, buildingTemplateId => Array.IndexOf(offer.AbilityIds, buildingTemplateId) < 0)!;
		Assert.That(unoffered, Is.Not.Null, "A draw of three cannot offer all nine packages.");

		_match.LockDraftOption(1, unoffered, SitePosition);

		Assert.That(_match.Buildings(1).Count, Is.EqualTo(1), "A package the draw did not offer may not place anything.");
		Assert.That(_match.Wood(1), Is.EqualTo(400f).Within(0.01f), "A refused pick costs nothing more.");
		Assert.That(_match.DraftOptions(1), Is.EquivalentTo(offer.AbilityIds), "A refused pick leaves the draw open as it was.");

		_match.AbandonDraft(1);
	}

	[Test]
	public void APackageCastAfterTheDrawIsAnsweredPlacesNothing()
	{
		_match.SetWood(1, 5000f);
		DraftOffer firstDraw = _match.BeginConstruction(1, SitePosition);
		_match.LockDraftOption(1, firstDraw.AbilityIds[0], SitePosition);
		UnitId firstSite = FindSite(1);

		_match.Cast(1, firstDraw.AbilityIds[1], new Vector3(-40f, 0f, -50f));

		Assert.That(_match.Buildings(1).Count, Is.EqualTo(2), "A package cast with no open draw may not place a second site.");
		Assert.That(FindSite(1), Is.EqualTo(firstSite), "The only site has to be the one the answered draw placed.");
	}

	[Test]
	public void TheDeckIsSharedBetweenPlayers()
	{
		_match.SetWood(1, 5000f);
		_match.SetWood(2, 5000f);

		DraftOffer firstDraw = _match.BeginConstruction(1, SitePosition);
		_match.LockDraftOption(1, firstDraw.AbilityIds[0], SitePosition);
		UnitId takenSite = FindSite(1);
		string takenPackage = _match.TemplateOf(takenSite);
		Assert.That(_match.Api.GetUnitName(takenPackage), Is.Not.Empty, "The taken package has to have a name to compare against.");

		for (int draw = 0; draw < 12; draw++)
		{
			DraftOffer offer = _match.BeginConstruction(2, SitePosition);
			Assert.That(offer.AbilityIds, Has.None.EqualTo(takenPackage), $"Draw {draw} offered '{takenPackage}', which player 1 already took from the shared deck.");
			_match.AbandonDraft(2);
		}
	}

	[Test]
	public void ADrawNeverOffersABuildingThePlayerAlreadyStandsOn()
	{
		_match.SetWood(1, 5000f);

		DraftOffer firstDraw = _match.BeginConstruction(1, SitePosition);
		_match.LockDraftOption(1, firstDraw.AbilityIds[0], SitePosition);
		string ownedPackage = _match.TemplateOf(FindSite(1));

		for (int draw = 0; draw < 8; draw++)
		{
			DraftOffer offer = _match.BeginConstruction(1, new Vector3(-45f, 0f, -45f));
			Assert.That(offer.AbilityIds, Has.None.EqualTo(ownedPackage), $"Draw {draw} offered '{ownedPackage}', which this player already owns.");
			_match.AbandonDraft(1);
		}
	}

	[Test]
	public void ConstructionCostsWoodAndStopsAtTheAuthoredBuildingLimit()
	{
		_match.SetWood(1, 50f);
		_match.Cast(1, MeleeMatch.BeginConstructionAbilityId, SitePosition);
		Assert.That(_match.DraftOptions(1), Is.Empty, "A player who cannot pay must not be offered a draw.");
		Assert.That(_match.Wood(1), Is.EqualTo(50f).Within(0.01f), "A refused draw costs nothing.");

		_match.SetWood(1, 5000f);
		for (int placement = 0; placement < MaximumBuildingsPerPlayer; placement++)
		{
			_match.PlaceDraftBuilding(new Vector3(-60f + placement * 4f, 0f, -60f));
		}

		Assert.That(_match.Buildings(1).Count, Is.EqualTo(MaximumBuildingsPerPlayer + 1), "Five drafted sites plus the Nexus fill the player's building count.");

		_match.Cast(1, MeleeMatch.BeginConstructionAbilityId, new Vector3(-20f, 0f, -60f));
		Assert.That(_match.DraftOptions(1), Is.Empty, "A player at the building limit must not be offered another draw.");
	}

	[Test]
	public void RequisitionAdvancesTheSiteUnderConstruction()
	{
		_match.SetWood(1, 500f);
		DraftOffer offer = _match.BeginConstruction(1, SitePosition);
		_match.LockDraftOption(1, offer.AbilityIds[0], SitePosition);
		UnitId site = FindSite(1);

		_match.StepTicks(2);
		float progressBefore = _match.ConstructionProgress(site);
		_match.CastRequisition(1);
		float progressAfter = _match.ConstructionProgress(site);

		float expectedAdvance = (1f - progressBefore) * 0.3f;
		Assert.That(progressAfter - progressBefore, Is.EqualTo(expectedAdvance).Within(0.02f), "Requisition has to advance the active site by 30% of its remaining work.");
	}

	[TestCase(1, 0.3f)]
	[TestCase(3, 0.4f)]
	[TestCase(6, 0.5f)]
	public void RequisitionCompletionGrowsWithCorvussFortune(int level, float expectedFraction)
	{
		_match.Dispose();
		_match = MeleeMatch.StartDuel("Corvus", "Vale");
		if (level > 1)
		{
			_match.Api.SetUnitExperience(_match.Hero(1), level == 3 ? 430f : 100000f);
			_match.Step(0.2f);
		}

		Assert.That(_match.HeroLevel(1), Is.EqualTo(level), "The test needs Corvus at the level it is measuring.");
		UnitId site = _match.PlaceDraftBuilding(SitePosition);
		_match.StepTicks(2);

		float progressBefore = _match.ConstructionProgress(site);
		_match.CastRequisition(1);
		float progressAfter = _match.ConstructionProgress(site);

		float expectedAdvance = (1f - progressBefore) * expectedFraction;
		Assert.That(progressAfter - progressBefore, Is.EqualTo(expectedAdvance).Within(0.02f), $"Requisition has to complete {expectedFraction:P0} of the remaining work at hero level {level}.");
	}

	[Test]
	public void EmergencyLevyHandsEveryOperationalBuildingACharge()
	{
		_match.SetWood(1, 500f);
		DraftOffer offer = _match.BeginConstruction(1, SitePosition);
		_match.LockDraftOption(1, offer.AbilityIds[0], SitePosition);
		UnitId building = FindSite(1);

		WaitForCompletion(building);
		_match.Step(12f);
		Assert.That(_match.IsUnderConstruction(building), Is.False, "The building has to be finished before the levy applies.");
		Assert.That(_match.CountCharges(building), Is.EqualTo(0), "Its first charge was spent on its first soldier.");

		_match.CastEmergencyLevy(1);

		Assert.That(_match.CountCharges(building), Is.EqualTo(1), "Emergency Levy has to add a charge to the finished building.");
		Assert.That(_match.CountCharges(_match.Nexus(1)), Is.EqualTo(0), "The Nexus grants no soldier, so it holds no charge.");
	}

	/// <summary>
	///     Steps until the site finishes and answers how long that took. Completion is detected rather than assumed
	///     because the building a draw offers carries one of two authored construction times.
	/// </summary>
	private float WaitForCompletion(UnitId site)
	{
		const float stepSeconds = 0.5f;
		float elapsed = 0f;
		while (_match.IsUnderConstruction(site) && elapsed < LongestAuthoredConstructionSeconds + 5f)
		{
			_match.Step(stepSeconds);
			elapsed += stepSeconds;
		}

		Assert.That(_match.IsUnderConstruction(site), Is.False, "The site has to finish within its authored construction time.");
		return elapsed;
	}

	private UnitId FindSite(int slot)
	{
		foreach (UnitId building in _match.Buildings(slot))
		{
			if (_match.Has<UnderConstruction>(building)) return building;
		}

		return UnitId.Invalid;
	}
}
