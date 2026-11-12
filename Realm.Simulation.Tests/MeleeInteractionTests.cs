using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Shared.Network;
using Realm.Shared.Simulation;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays the interactions the rules promise but that no single feature test owns: what a hero's death does to the
///     worker crew, what the relic pool does to the crew's own clock, the supply and copy parameters a host states, and
///     the warnings a seat is shown.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeInteractionTests
{
	private const string CouriersKnot = "item/couriers_knot";

	private static readonly Vector3 SitePosition = new(-50f, 0f, -50f);
	private static readonly Vector3 SecondSitePosition = new(-44f, 0f, -50f);

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
	public void AWorkerKilledInTheCrewIsReplacedByItsBuilding()
	{
		UnitId building = _match.PlaceCompletedBuilding(SitePosition);
		Assert.That(_match.StepUntilWorkers(1), Is.EqualTo(1), "The building has to raise its starting worker first.");

		UnitId worker = _match.Workers(1)[0];
		int workerLevel = _match.LevelOf(worker);
		_match.Kill(worker, _match.Hero(2));
		Assert.That(_match.Workers(1), Is.Empty, "The killed worker has to leave the crew.");

		float replacementSeconds = SecondsUntilWorkerReturns(_match);

		Assert.That(_match.Workers(1).Count, Is.EqualTo(1), $"The building has to replace its killed worker within its own workbook, which took {replacementSeconds:0.#}s.");
		Assert.That(_match.LevelOf(_match.Workers(1)[0]), Is.EqualTo(workerLevel), "A replacement worker is raised by the same building, so it keeps that building's construction-order level.");
		Assert.That(_match.IsAlive(building), Is.True, "The building that raised the crew is unaffected by one of its workers dying.");
	}

	[Test]
	public void LosingTheHeroLeavesTheWorkerCrewOnTheBoard()
	{
		UnitId building = _match.PlaceCompletedBuilding(SitePosition);
		Assert.That(_match.StepUntilWorkers(1), Is.EqualTo(1), "The building has to raise its starting worker first.");

		UnitId worker = _match.Workers(1)[0];
		_match.Kill(_match.Hero(1), _match.Hero(2));
		_match.Step(2f);

		Assert.That(_match.IsAlive(worker), Is.True, "Workers are the economy, not the army: a hero's death must not wipe the crew.");
		Assert.That(_match.Workers(1).Count, Is.EqualTo(1), "The crew keeps working while the hero is away.");
	}

	[Test]
	public void LosingTheHeroClearsTheWorkerQueueToo()
	{
		UnitId building = _match.PlaceCompletedBuilding(SitePosition);
		Assert.That(_match.Workers(1), Is.Empty, "A worker has to be in production, not already raised, when the hero dies.");

		_match.Kill(_match.Hero(1), _match.Hero(2));
		_match.Step(10f);

		Assert.That(_match.Workers(1), Is.Empty, "The worker production queue has to be cleared with every other queue when the hero dies.");
		Assert.That(_match.IsAlive(building), Is.True, "The building itself stays on the board while the hero is away.");

		float waited = 0f;
		while (_match.Workers(1).Count == 0 && waited < 120f)
		{
			_match.Step(1f);
			waited += 1f;
		}

		Assert.That(_match.Workers(1).Count, Is.EqualTo(1), "Worker production has to resume once the hero is back and the building holds a charge.");
	}

	[Test]
	public void CouriersKnotShortensTheWorkerChargeClock()
	{
		var options = new MatchOptionSet();
		options.Set(MatchOption.MirroredAutoDraft, 1);
		using MeleeMatch plain = MeleeMatch.StartDuel(matchOptions: options);
		using MeleeMatch knotted = MeleeMatch.StartDuel(matchOptions: options);
		Assert.That(AddFirstWorkerAndSpendIt(plain), Is.True, "The plain match has to reach its first worker.");
		Assert.That(AddFirstWorkerAndSpendIt(knotted), Is.True, "The relic match has to reach its first worker.");
		Assert.That(knotted.AddRelic(1, CouriersKnot), Is.True, "The hero has to be able to carry the Courier's Knot.");

		float plainSeconds = SecondsUntilWorkerReturns(plain);
		float knottedSeconds = SecondsUntilWorkerReturns(knotted);

		Assert.That(knottedSeconds, Is.LessThan(plainSeconds - 1f),
			$"The Courier's Knot has to shorten the worker crew's own recharge as well as the soldier charges: plain took {plainSeconds:0.#}s, the Knot took {knottedSeconds:0.#}s.");
	}

	private static bool AddFirstWorkerAndSpendIt(MeleeMatch match)
	{
		match.PlaceCompletedBuilding(SitePosition);
		if (match.StepUntilWorkers(1) == 0) return false;

		match.Kill(match.Workers(1)[0], match.Hero(2));
		return match.Workers(1).Count == 0;
	}

	private static float SecondsUntilWorkerReturns(MeleeMatch match)
	{
		float waited = 0f;
		while (match.Workers(1).Count == 0 && waited < 120f)
		{
			match.Step(0.5f);
			waited += 0.5f;
		}

		return waited;
	}

	[Test]
	public void TheUnderAttackAlertAlsoPlaysTheWarningSound()
	{
		UnitId building = _match.PlaceCompletedBuilding(SitePosition);
		int pingsBefore = _match.CountEventsFor(1, PresentationKind.MinimapPing);

		_match.Api.DealTypedDamage(_match.Hero(2), building, 50f, "physical");
		_match.Step(0.1f);

		Assert.That(_match.CountEventsFor(1, PresentationKind.MinimapPing), Is.GreaterThan(pingsBefore), "A hit on a structure has to point at it on the minimap.");
		Assert.That(_match.CountEventsFor(1, PresentationKind.WarningSound), Is.GreaterThan(0), "A hit on a structure has to play the warning sound, not only write a line.");
		Assert.That(_match.LastFeedbackText(1), Does.Contain("under attack"), "The defender has to read what is under attack.");
	}

	[Test]
	public void TheHostSetsHowManyCopiesOfOneBuildingAPlayerMayHold()
	{
		var options = new MatchOptionSet();
		options.Set(MatchOption.OpenPoolDrafting, 1);
		options.Set(MatchOption.MirroredAutoDraft, 1);
		options.Set(MatchOption.MaximumUniqueBuildingInstances, 2);
		_match.Dispose();
		_match = MeleeMatch.StartDuel(matchOptions: options);

		UnitId first = _match.PlaceCompletedBuilding(SitePosition);
		string templateId = _match.TemplateOf(first);

		DraftOffer second = _match.BeginConstruction(1, SecondSitePosition);
		Assert.That(second.AbilityIds, Does.Contain(templateId),
			"With copies allowed and an open pool, a building the player already owns has to stay eligible for another draw.");

		_match.LockDraftOption(1, templateId, SecondSitePosition);
		UnitId copy = _match.SiteOf(1, templateId);
		Assert.That(copy.IsValid, Is.True, "The second copy has to stand as a site.");

		_match.Raise(1, _match.Hero(1), copy);
		_match.Step(MeleeMatch.LongestAuthoredConstructionSeconds + 2f);

		Assert.That(_match.Buildings(1).Count(building => _match.TemplateOf(building) == templateId), Is.EqualTo(2), "Both copies have to stand.");
		Assert.That(_match.StepUntilSoldiers(1), Is.GreaterThan(0), "The copies have to field soldiers.");
	}

	[Test]
	public void TheDefaultCopyLimitKeepsAnOwnedBuildingOutOfTheNextDraw()
	{
		var options = new MatchOptionSet();
		options.Set(MatchOption.MirroredAutoDraft, 1);
		_match.Dispose();
		_match = MeleeMatch.StartDuel(matchOptions: options);

		UnitId first = _match.PlaceCompletedBuilding(SitePosition);
		string templateId = _match.TemplateOf(first);

		DraftOffer second = _match.BeginConstruction(1, SecondSitePosition);

		Assert.That(second.AbilityIds, Does.Not.Contain(templateId), "The standard lobby keeps a building a player already holds out of that player's next draw.");
	}

	[Test]
	public void TheDeckIsRestoredWhenItRunsOut()
	{
		_match.SetWood(1, 5000f);
		_match.SetWood(2, 5000f);

		for (int index = 0; index < 5; index++)
		{
			DraftASite(1, new Vector3(-60f + index * 4f, 0f, -60f));
		}

		for (int index = 0; index < 5; index++)
		{
			DraftASite(2, new Vector3(52f + index * 4f, 0f, -60f));
		}

		Assert.That(_match.Buildings(1).Count, Is.EqualTo(6), "The first seat drafts its whole supply.");
		Assert.That(_match.Buildings(2).Count, Is.EqualTo(6), "The second seat's fifth draw has to find a restored deck rather than an empty one.");
	}

	private void DraftASite(int slot, Vector3 position)
	{
		DraftOffer offer = _match.BeginConstruction(slot, position);
		Assert.That(offer.AbilityIds, Is.Not.Empty, $"Seat {slot} has to be offered a building while its supply is open.");
		_match.LockDraftOption(slot, offer.AbilityIds[0], position);
	}
}
