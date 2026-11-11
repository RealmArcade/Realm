using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays the Nexus relic vendor on the authority: the six relics it offers, the gold they cost, the slot they take in
///     the hero's inventory, the effect they switch on at the moment of purchase, and what happens when the hero is dead,
///     already holds the relic, has no slot left or cannot afford it.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeRelicVendorTests
{
	private const float RelicCost = 100f;
	private const int RelicSlotCount = 6;
	private const float SigilMaxHealthFactor = 1.1f;

	private static readonly Vector3 FrontFormationPosition = new(-46f, 0f, -52f);

	private static readonly string[] RelicIds =
	{
		"item/sigil_of_the_wall",
		"item/couriers_knot",
		"item/spiked_barricades",
		"item/deep_granaries",
		"item/wardens_writ",
		"item/muster_bell"
	};

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
	public void TheNexusOffersEveryRelicAtItsAuthoredPrice()
	{
		UnitId nexus = _match.Nexus(1);
		Assert.That(nexus.IsValid, Is.True, "The test needs the Nexus the relics are sold from.");

		for (int index = 0; index < RelicIds.Length; index++)
		{
			string relicId = RelicIds[index];
			Assert.That(_match.IsAbilityVisible(nexus, relicId), Is.True, $"The Nexus has to offer '{relicId}'.");
			Assert.That(_match.AbilityTooltip(1, relicId), Does.Contain("100"), $"The offer for '{relicId}' has to state its price.");
		}
	}

	[Test]
	public void TheVendorFillsTheCommandGridWithoutDroppingARelic()
	{
		var occupiedSlots = new HashSet<(int X, int Y)>();

		for (int index = 0; index < RelicIds.Length; index++)
		{
			var definition = _match.Api.Abilities.GetDefinition(RelicIds[index]);
			Assert.That(definition.GridX, Is.InRange(0, 3), $"'{RelicIds[index]}' has to sit inside the four-column command grid.");
			Assert.That(definition.GridY, Is.InRange(0, 2), $"'{RelicIds[index]}' has to sit inside the three-row command grid.");
			Assert.That(occupiedSlots.Add((definition.GridX, definition.GridY)), Is.True, $"'{RelicIds[index]}' must not share a command slot with another relic.");
		}

		Assert.That(occupiedSlots.Count, Is.EqualTo(RelicIds.Length), "Every relic needs its own visible command slot.");
	}

	[Test]
	public void BuyingARelicSpendsAHundredGoldAndEquipsIt()
	{
		const string relicId = "item/sigil_of_the_wall";
		UnitId hero = _match.Hero(1);
		UnitId nexus = _match.Nexus(1);
		float goldBefore = _match.Gold(1);

		_match.Cast(1, relicId, Vector3.Zero);

		Assert.That(_match.Gold(1), Is.EqualTo(goldBefore - RelicCost).Within(0.01f), "A relic costs one hundred gold.");
		Assert.That(_match.UnitHasItem(hero, relicId), Is.True, "The relic has to land in the hero's inventory.");
		Assert.That(_match.IsAbilityDisabled(nexus, relicId), Is.True, "A relic the hero already holds has to be disabled in the vendor.");
	}

	[Test]
	public void BuyingFromTheSelectedNexusReachesThePlayersHero()
	{
		const string relicId = "item/wardens_writ";
		UnitId nexus = _match.Nexus(1);
		UnitId hero = _match.Hero(1);

		_match.CastFrom(1, nexus, relicId, _match.PositionOf(nexus));

		Assert.That(_match.UnitHasItem(hero, relicId), Is.True, "Casting the vendor ability from the Nexus has to buy the relic for its owner's hero.");
		Assert.That(_match.Gold(1), Is.EqualTo(200f - RelicCost).Within(0.01f), "The Nexus purchase spends the same gold as any other.");
	}

	[Test]
	public void ARelicCannotBeBoughtTwice()
	{
		const string relicId = "item/sigil_of_the_wall";
		_match.Cast(1, relicId, Vector3.Zero);
		float goldAfterFirstPurchase = _match.Gold(1);

		_match.Cast(1, relicId, Vector3.Zero);

		Assert.That(_match.Gold(1), Is.EqualTo(goldAfterFirstPurchase).Within(0.01f), "A second copy must not be charged for.");
		Assert.That(_match.LastFeedbackText(1), Does.Contain("already holds"), "The refusal has to explain that the hero already holds it.");
	}

	[Test]
	public void ARelicPurchaseNeedsItsGold()
	{
		_match.SetGold(1, 50f);

		_match.Cast(1, "item/sigil_of_the_wall", Vector3.Zero);

		Assert.That(_match.Gold(1), Is.EqualTo(50f).Within(0.01f), "A purchase the player cannot afford must not be charged for.");
		Assert.That(_match.UnitHasItem(_match.Hero(1), "item/sigil_of_the_wall"), Is.False, "Nothing may be equipped when the gold is short.");
		Assert.That(_match.LastFeedbackText(1), Does.Contain("100"), "The refusal has to state what the relic costs.");
	}

	[Test]
	public void AllSixSlotsCanBeFilledAndTheVendorThenRefusesAnotherPurchase()
	{
		_match.SetGold(1, 1000f);
		UnitId hero = _match.Hero(1);
		UnitId nexus = _match.Nexus(1);

		for (int index = 0; index < RelicIds.Length; index++)
		{
			_match.Cast(1, RelicIds[index], Vector3.Zero);
			Assert.That(_match.UnitHasItem(hero, RelicIds[index]), Is.True, $"Relic slot {index + 1} has to hold '{RelicIds[index]}'.");
			Assert.That(_match.IsAbilityDisabled(nexus, RelicIds[index]), Is.True, "A relic the hero holds is no longer offerable.");
		}

		Assert.That(_match.Gold(1), Is.EqualTo(1000f - RelicCost * RelicSlotCount).Within(0.01f), "Six relics cost six hundred gold.");
		Assert.That(_match.TotalItemCount(1, RelicIds[RelicIds.Length - 1]), Is.EqualTo(1), "The sixth relic has to sit in the hero's inventory like every other one.");
	}

	[Test]
	public void ARelicPurchaseAppliesItsEffectImmediately()
	{
		UnitId soldier = _match.SpawnSoldier(1, "unit/shieldbearer", FrontFormationPosition);
		float maxHealthBefore = _match.MaxHealthOf(soldier);

		_match.Cast(1, "item/sigil_of_the_wall", Vector3.Zero);
		_match.Step(0.2f);

		Assert.That(_match.MaxHealthOf(soldier), Is.EqualTo(maxHealthBefore * SigilMaxHealthFactor).Within(1f), "The Sigil has to harden the front formation the moment it is bought.");
	}

	[Test]
	public void ADeadHeroCannotBuyARelic()
	{
		UnitId nexus = _match.Nexus(1);
		_match.Kill(_match.Hero(1), _match.Hero(2));
		_match.Step(0.2f);
		float goldBefore = _match.Gold(1);

		_match.CastFrom(1, nexus, "item/deep_granaries", _match.PositionOf(nexus));

		Assert.That(_match.Gold(1), Is.EqualTo(goldBefore).Within(0.01f), "A dead hero may not spend the seat's gold.");
		Assert.That(_match.LastFeedbackText(1), Does.Contain("alive"), "The refusal has to explain that the hero has to be alive.");
		Assert.That(_match.IsAbilityDisabled(nexus, "item/deep_granaries"), Is.True, "Every relic has to be disabled while the hero is dead.");
	}

	[Test]
	public void ARelicStaysWithTheHeroThroughRevival()
	{
		_match.Cast(1, "item/wardens_writ", Vector3.Zero);
		UnitId hero = _match.Hero(1);
		Assert.That(_match.UnitHasItem(hero, "item/wardens_writ"), Is.True, "The test needs the relic equipped first.");

		_match.Kill(hero, _match.Hero(2));
		_match.Step(12.5f);

		UnitId revived = _match.Hero(1);
		Assert.That(_match.IsAlive(revived), Is.True, "The hero has to return.");
		Assert.That(_match.UnitHasItem(revived, "item/wardens_writ"), Is.True, "A relic is not lost to death.");
		Assert.That(_match.ModifierTotalFrom(revived, "relic/wardens_writ", "Tenacity"), Is.EqualTo(0.1f).Within(0.001f), "The relic's effect is reapplied to the revived hero.");
	}
}
