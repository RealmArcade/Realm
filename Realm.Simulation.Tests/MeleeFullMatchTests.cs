using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Shared.Network;
using Realm.Shared.Simulation;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays one match end to end on the authority: the opening economy, a draft and a raised building for both seats,
///     the army each building fields, a real kill, a relic bought at the Nexus, and the assault that ends the match.
///     Every step is driven through the command routing a player's screen uses, so the test reads the same state and the
///     same presentation a player would.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeFullMatchTests
{
	private const string SigilOfTheWall = "item/sigil_of_the_wall";
	private const string WoodlandTemplateId = "resource/melee_woodland";

	private static readonly Vector3 FirstSite = new(-50f, 0f, -50f);
	private static readonly Vector3 SecondSite = new(55f, 0f, -55f);

	[Test]
	public void OneMatchRunsFromTheFirstDraftToTheFallenNexus()
	{
		MeleeTestMaps.RequireMaps();
		using MeleeMatch match = MeleeMatch.StartDuel();

		Assert.That(match.PlayerCount, Is.EqualTo(2), "The duel seats both players.");
		Assert.That(match.Hero(1).IsValid, Is.True, "The first seat starts with a hero.");
		Assert.That(match.Hero(2).IsValid, Is.True, "The second seat starts with a hero.");

		// Opening economy: the hero's own gathering is a real player action and is the first thing a player does.
		UnitId heroOne = match.Hero(1);
		UnitId woodland = match.ResourceNode(WoodlandTemplateId, match.PositionOf(heroOne));
		float woodBeforeGathering = match.Wood(1);
		Assert.That(match.Gather(1, heroOne, match.PositionOf(woodland)).HasRejection, Is.False, "The hero has to accept a gather order on the woodland.");
		match.Step(20f);
		Assert.That(match.Wood(1), Is.GreaterThan(woodBeforeGathering), "The wood the hero gathered has to reach the seat's purse.");

		// Both seats draft and raise a building. The second seat's build is driven through its own command path, because
		// the draft belongs to the hero that paid for it and not to the first seat's screen.
		UnitId firstBuilding = match.PlaceCompletedBuilding(FirstSite);
		UnitId secondBuilding = PlaceCompletedBuildingFor(match, 2, SecondSite);

		Assert.That(match.TemplateOf(firstBuilding), Is.Not.EqualTo(match.TemplateOf(match.Nexus(1))), "The drafted package has to become its own building.");
		Assert.That(match.TemplateOf(secondBuilding), Is.Not.EqualTo(match.TemplateOf(match.Nexus(2))), "The second seat's drafted package has to become its own building.");

		// Automatic production: each building fields its soldier from its starting charge without a training order.
		Assert.That(match.StepUntilSoldiers(1), Is.GreaterThan(0), "The first seat's building has to field its soldier automatically.");
		Assert.That(match.StepUntilSoldiers(2), Is.GreaterThan(0), "The second seat's building has to field its soldier automatically.");

		UnitId enemySoldier = match.Soldiers(2)[0];
		float enemySoldierMaxHealth = match.MaxHealthOf(enemySoldier);
		int goldBeforeKill = (int)match.Gold(1);

		// A real attack order: the hero walks to the enemy soldier and strikes it, and the army follows its hero's target.
		Assert.That(match.Attack(1, heroOne, enemySoldier).HasRejection, Is.False, "The hero has to accept the attack order.");
		bool killed = WaitUntil(match, () => !match.IsAlive(enemySoldier), 70f);
		Assert.That(killed, Is.True, $"The hero and its army have to kill the enemy soldier; it stood at {match.HealthOf(enemySoldier):0.#}/{enemySoldierMaxHealth:0.#} health.");

		Assert.That(match.ExperienceOf(heroOne), Is.GreaterThan(0f), "The kill has to pay experience to the hero.");
		Assert.That(match.Gold(1), Is.GreaterThan(goldBeforeKill), "The kill has to pay gold to the hero's seat.");
		Assert.That(match.CountEventsFor(2, PresentationKind.PlayerFeedbackText), Is.GreaterThan(0), "The defender has to be told its unit is under attack.");

		// A relic bought at the Nexus is a player command, not a test fixture.
		match.SetGold(1, 500f);
		Assert.That(match.UnitHasItem(heroOne, SigilOfTheWall), Is.False, "The hero does not start with the relic.");
		match.CastFrom(1, match.Nexus(1), SigilOfTheWall, Vector3.Zero);
		Assert.That(match.UnitHasItem(heroOne, SigilOfTheWall), Is.True, "Buying from the selected Nexus has to equip the hero.");
		Assert.That(match.Gold(1), Is.EqualTo(400f).Within(1f), "The relic costs its authored hundred gold.");

		// The assault: the seat's own hero and army walk to the enemy Nexus, strike it, and end the match.
		UnitId enemyNexus = match.Nexus(2);
		match.Api.SetUnitHealth(enemyNexus, 120f);
		Assert.That(match.Attack(1, heroOne, enemyNexus).HasRejection, Is.False, "The hero has to accept the attack order on the Nexus.");
		bool nexusFell = WaitUntil(match, () => !match.IsAlive(enemyNexus), 90f);
		Assert.That(nexusFell, Is.True, $"The hero and its army have to break the enemy Nexus; it stood at {match.HealthOf(enemyNexus):0.#} health.");

		Assert.That(match.IsGameOver, Is.True, "The last seat holding a Nexus ends the match.");
		Assert.That(match.IsEliminated(2), Is.True, "The seat whose Nexus fell is eliminated.");
		Assert.That(match.CountEventsFor(2, PresentationKind.PlayerDefeated), Is.GreaterThan(0), "The eliminated seat has to be told it lost.");
		Assert.That(match.CountEventsFor(1, PresentationKind.PlayerVictory), Is.GreaterThan(0), "The surviving seat has to be told it won.");
		Assert.That(match.CountEventsFor(2, PresentationKind.PlayerVictory), Is.Zero, "The eliminated seat must not be told it won.");
		Assert.That(match.IsAlive(firstBuilding), Is.True, "The winner's own base stands after the match.");
		Assert.That(match.Nexus(1).IsValid, Is.True, "The winner's Nexus stands after the match.");
	}

	private static UnitId PlaceCompletedBuildingFor(MeleeMatch match, int slot, Vector3 position)
	{
		match.SetWood(slot, 1000f);
		DraftOffer offer = match.BeginConstruction(slot, position);
		Assert.That(offer.AbilityIds, Is.Not.Empty, $"Seat {slot} has to be offered a package.");

		string packageId = offer.AbilityIds[0];
		match.LockDraftOption(slot, packageId, position);

		UnitId site = match.SiteOf(slot, packageId);
		Assert.That(site.IsValid, Is.True, $"Seat {slot}'s chosen package has to stand as a site.");

		match.Raise(slot, match.Hero(slot), site);
		bool completed = WaitUntil(match, () => !match.IsUnderConstruction(site), MeleeMatch.LongestAuthoredConstructionSeconds + 5f);
		Assert.That(completed, Is.True, $"Seat {slot}'s site has to finish construction.");

		return site;
	}

	private static bool WaitUntil(MeleeMatch match, Func<bool> condition, float seconds)
	{
		float waited = 0f;
		while (waited < seconds)
		{
			if (condition()) return true;

			match.Step(0.5f);
			waited += 0.5f;
		}

		return condition();
	}
}
