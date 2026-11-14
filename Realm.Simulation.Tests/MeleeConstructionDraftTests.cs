using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Shared.Simulation;

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
	private const int MaximumBuildingsPerPlayer = 8;

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
	public void EveryDraftCommandComparesItsSoldierAndItsWorkerCrew()
	{
		var tooltips = new List<string>();

		foreach (string buildingTemplateId in MeleeContent.DraftableBuildings)
		{
			string tooltip = _match.AbilityTooltip(1, buildingTemplateId);
			string soldierName = _match.Api.GetUnitName(MeleeContent.SoldierFor(buildingTemplateId));

			Assert.That(tooltip, Is.Not.Empty, $"The draft command for '{buildingTemplateId}' has to explain what it raises.");
			Assert.That(tooltip, Does.Contain(soldierName), $"The draft command for '{buildingTemplateId}' has to name the soldier it raises, so two packages can be told apart before one is locked.");
			Assert.That(tooltip, Does.Match(@"\d+ capacity, one every \d+s\."), $"The draft command for '{buildingTemplateId}' has to state its capacity and its production clock: '{tooltip}'");
			Assert.That(tooltip, Does.Match(@"Workers gather (gold|wood): \d+ at \d+s each, gaining .+\. Both draw from the building's own mana\.$"), $"The draft command for '{buildingTemplateId}' has to state the worker crew, the buffs it gives them and the pool both lines spend: '{tooltip}'");
			Assert.That(tooltip, Does.Not.Contain("Income"), $"A draft command must not advertise a computed income: '{tooltip}'");
			tooltips.Add(tooltip);
		}

		Assert.That(tooltips, Has.Some.Contains("Workers gather gold"), "The deck compares gold buildings as well as wood ones.");
		Assert.That(tooltips, Has.Some.Contains("Workers gather wood"), "The deck compares wood buildings as well as gold ones.");
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
		Assert.That(_match.Blueprint(1).IsValid, Is.True, "The cast has to leave a blueprint on the ground it named.");
		Assert.That(Vector2.Distance(new Vector2(_match.PositionOf(_match.Blueprint(1)).X, _match.PositionOf(_match.Blueprint(1)).Z), new Vector2(SitePosition.X, SitePosition.Z)), Is.LessThan(1f), "The blueprint marks the site that was cast at.");
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

		UnitId blueprint = _match.Blueprint(1);
		UnitId hero = _match.Hero(1);
		UnitId nexus = _match.Nexus(1);
		Assert.That(blueprint.IsValid, Is.True, "The cast has to leave a blueprint to answer the draw from.");
		foreach (string buildingTemplateId in offer.AbilityIds)
		{
			Assert.That(_match.IsAbilityVisible(blueprint, buildingTemplateId), Is.True, "The blueprint's grid has to show the packages the deck drew.");
			Assert.That(_match.IsAbilityVisible(hero, buildingTemplateId), Is.False, "The packages belong to the blueprint, not to the hero that cast it.");
			Assert.That(_match.IsAbilityVisible(nexus, buildingTemplateId), Is.False, "The Nexus must not keep a grid the blueprint now owns.");
		}

		_match.AbandonDraft(1);

		Assert.That(_match.Blueprint(1).IsValid, Is.False, "Answering the draw takes the blueprint away.");
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
	public void PickingTheDrawLeavesASiteThatWaitsForTheHero()
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

		Assert.That(_match.Blueprint(1).IsValid, Is.False, "The blueprint gives way to the site it placed.");

		_match.Step(2f);
		Assert.That(_match.ConstructionProgress(site), Is.LessThan(0.01f), "A site may not raise itself just because the hero stands near it.");

		OrderHeroToRaise(site);
		_match.Step(2f);
		Assert.That(_match.ConstructionProgress(site), Is.GreaterThan(0.01f), "A hero sent to the site has to raise it.");

		Assert.That(_match.IsUnderConstruction(site), Is.True, "The hero has not worked the site long enough for it to finish early.");
		Assert.That(_match.CountProductions(site), Is.EqualTo(0), "A site holds no production mana while it is being built.");

		WaitForCompletion(site);
		Assert.That(_match.IsUnderConstruction(site), Is.False, "The hero finishes the site within the authored window.");
		Assert.That(_match.Has<Building>(site), Is.True, "The finished site is an ordinary building.");
	}

	[Test]
	public void AConstructionSiteIsInvulnerableUntilItStands()
	{
		_match.SetWood(1, 500f);
		DraftOffer offer = _match.BeginConstruction(1, SitePosition);
		_match.LockDraftOption(1, offer.AbilityIds[0], SitePosition);

		UnitId site = FindSite(1);
		Assert.That(site.IsValid, Is.True, "The test needs the site the draw placed.");
		Assert.That(_match.Has<Invulnerable>(site), Is.True, "A building site is invulnerable while it is being raised.");

		float siteHealthBefore = _match.HealthOf(site);
		_match.Kill(site, _match.Hero(2));

		Assert.That(_match.HealthOf(site), Is.EqualTo(siteHealthBefore).Within(0.01f), "A site that is still being raised must take no damage.");
		Assert.That(_match.IsUnderConstruction(site), Is.True, "The hit must not finish or destroy the site.");

		OrderHeroToRaise(site);
		WaitForCompletion(site);

		Assert.That(_match.Has<Invulnerable>(site), Is.False, "A completed building has to become attackable.");
		float standingHealth = _match.HealthOf(site);
		_match.Kill(site, _match.Hero(2));

		Assert.That(_match.HealthOf(site), Is.LessThan(standingHealth), "A completed building has to take the damage a site refused.");
	}

	[Test]
	public void TheAttackOrderOnAnAlliedSiteRaisesIt()
	{
		_match.SetWood(1, 500f);
		DraftOffer offer = _match.BeginConstruction(1, SitePosition);
		_match.LockDraftOption(1, offer.AbilityIds[0], SitePosition);

		UnitId site = FindSite(1);
		Assert.That(site.IsValid, Is.True, "The test needs the site the draw placed.");

		ServerCommandResult result = _match.Attack(1, _match.Hero(1), site);
		Assert.That(result.HasRejection, Is.False, $"The attack command has to read the allied site as work: {result.RejectionReason}");

		_match.Step(2f);

		Assert.That(_match.ConstructionProgress(site), Is.GreaterThan(0.01f), "The attack command has to put the hero to work raising an allied site.");
	}

	[Test]
	public void ASiteRisesOnlyWhileTheHeroWorksAtIt()
	{
		_match.SetWood(1, 500f);
		DraftOffer offer = _match.BeginConstruction(1, SitePosition);
		_match.LockDraftOption(1, offer.AbilityIds[0], SitePosition);

		UnitId site = FindSite(1);
		UnitId hero = _match.Hero(1);
		Assert.That(site.IsValid, Is.True, "The test needs the site the draw placed.");

		OrderHeroToRaise(site);
		_match.Step(2f);
		float worked = _match.ConstructionProgress(site);
		Assert.That(worked, Is.GreaterThan(0.01f), "A hero sent to its site has to raise it.");

		ServerCommandResult march = _match.Order(1, "move", new Vector3(-50f, 0f, -20f), hero);
		Assert.That(march.HasRejection, Is.False, $"The hero has to be able to walk away: {march.RejectionReason}");
		_match.Step(8f);

		float paused = _match.ConstructionProgress(site);
		Assert.That(_match.IsUnderConstruction(site), Is.True, "The site is nowhere near finished when the hero leaves it.");

		_match.Step(8f);
		Assert.That(_match.ConstructionProgress(site), Is.EqualTo(paused).Within(0.001f), "A site nobody works at must freeze instead of raising itself.");

		ServerCommandResult returnMarch = _match.Order(1, "move", new Vector3(-51f, 0f, -51f), hero);
		Assert.That(returnMarch.HasRejection, Is.False, $"The hero has to be able to walk back: {returnMarch.RejectionReason}");
		_match.Step(12f);

		Assert.That(_match.ConstructionProgress(site), Is.EqualTo(paused).Within(0.001f), "Walking back to a site is not a build order, so the work stays stopped.");

		OrderHeroToRaise(site);
		_match.Step(2f);
		Assert.That(_match.ConstructionProgress(site), Is.GreaterThan(paused + 0.01f), "Right-clicking the site again has to resume the work where it stopped.");
	}

	[Test]
	public void AFinishedBuildingRaisesItsCrewFromItsOpeningMana()
	{
		_match.SetWood(1, 500f);
		DraftOffer offer = _match.BeginConstruction(1, SitePosition);
		_match.LockDraftOption(1, offer.AbilityIds[0], SitePosition);
		UnitId site = FindSite(1);
		OrderHeroToRaise(site);

		float secondsToFinish = WaitForCompletion(site);
		Assert.That(secondsToFinish, Is.InRange(ShortestAuthoredConstructionSeconds - 1f, LongestAuthoredConstructionSeconds + 1f), "A drafted site has to finish within its authored construction time.");

		Assert.That(_match.StepUntilWorkers(1), Is.GreaterThan(0), "The building's opening mana has to become its worker crew, which the soldier line reserves a production for.");
		Assert.That(_match.StepUntilSoldiers(1), Is.GreaterThan(0), "Once the crew has room no longer, the same pool has to raise the soldier line.");
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

		_match.CastFrom(1, firstSite, firstDraw.AbilityIds[1], new Vector3(-40f, 0f, -50f));

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
		_match.SubmitBuild(1, _match.ConstructionSiteTemplate, SitePosition);
		Assert.That(_match.DraftOptions(1), Is.Empty, "A player who cannot pay must not be offered a draw.");
		Assert.That(_match.Wood(1), Is.EqualTo(50f).Within(0.01f), "A refused draw costs nothing.");

		_match.SetWood(1, 5000f);
		for (int placement = 0; placement < MaximumBuildingsPerPlayer; placement++)
		{
			_match.PlaceDraftBuilding(new Vector3(-60f + placement * 4f, 0f, -60f));
		}

		Assert.That(_match.Buildings(1).Count, Is.EqualTo(MaximumBuildingsPerPlayer + 1), "Eight drafted sites plus the Nexus fill the player's building count.");

		float woodAtLimit = _match.Wood(1);
		_match.SubmitBuild(1, _match.ConstructionSiteTemplate, new Vector3(-20f, 0f, -60f));
		Assert.That(_match.DraftOptions(1), Is.Empty, "A player at the building limit must not be offered another draw.");
		Assert.That(_match.Wood(1), Is.EqualTo(woodAtLimit).Within(0.01f), "A refused placement must not charge its wood.");
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
	public void EmergencyLevyHandsEveryOperationalBuildingAProduction()
	{
		_match.SetWood(1, 500f);
		DraftOffer offer = _match.BeginConstruction(1, SitePosition);
		_match.LockDraftOption(1, offer.AbilityIds[0], SitePosition);
		UnitId building = FindSite(1);
		OrderHeroToRaise(building);

		WaitForCompletion(building);
		Assert.That(_match.IsUnderConstruction(building), Is.False, "The building has to be finished before the levy applies.");

		_match.Api.SetUnitMana(building, 0f);
		_match.Api.SetUnitMana(_match.Nexus(1), 0f);
		_match.CastEmergencyLevy(1);

		Assert.That(_match.ManaOf(building), Is.EqualTo(MeleeContent.ProductionMana).Within(0.5f), "Emergency Levy has to pay one production into every finished building.");
		Assert.That(_match.ManaOf(_match.Nexus(1)), Is.EqualTo(0f).Within(0.5f), "The Nexus grants no production, so the levy pays it nothing.");
	}

	/// <summary>
	///     Right-clicks the site with the hero, which is the order that puts a hero to work on a building. Nothing about a
	///     site advances from the hero merely being nearby.
	/// </summary>
	private void OrderHeroToRaise(UnitId site)
	{
		ServerCommandResult result = _match.Raise(1, _match.Hero(1), site);
		Assert.That(result.HasRejection, Is.False, $"Right-clicking the site has to put the hero to work: {result.RejectionReason}");
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
