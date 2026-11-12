using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Shared.Network;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays the faction kits on the authority: Vale's unbroken armour, Sable's cinder mark, the bulwark both heroes share
///     and the warning a defender receives when its ground is hit.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeFactionTests
{
	private const float BulwarkArmorPerSoldier = 0.6f;
	private const float BulwarkArmorPerBuilding = 1.5f;
	private const float CinderMarkDamagePerSecond = 6f;
	private const float UnderAttackAlertCooldownSeconds = 8f;

	private static readonly Vector3 SitePosition = new(-50f, 0f, -50f);

	[SetUp]
	public void SetUp()
	{
		MeleeTestMaps.RequireMaps();
	}

	[Test]
	public void ValesInnateHardensItsHero()
	{
		using MeleeMatch match = MeleeMatch.StartDuel("Vale", "Sable");
		UnitId hero = match.Hero(1);

		Assert.That(match.ModifierTotalFrom(hero, "innate/unbroken", "Armor"), Is.EqualTo(6f).Within(0.01f), "Vale's innate has to be worth six points of armour.");
		Assert.That(match.ModifierTotalFrom(hero, "innate/unbroken", "Tenacity"), Is.EqualTo(0.1f).Within(0.001f), "Vale's innate has to raise its hero's tenacity by a tenth.");
		float tenacityWithInnate = match.TenacityOf(hero);

		match.Api.RemoveStatModifiers(hero, "innate/unbroken");
		float tenacityWithoutInnate = match.TenacityOf(hero);
		Assert.That(match.ModifierTotalFrom(hero, "innate/unbroken", "Armor"), Is.EqualTo(0f).Within(0.01f), "The innate has to leave with its source when the map withdraws it.");
		Assert.That(tenacityWithInnate, Is.EqualTo(tenacityWithoutInnate * 1.1f).Within(0.001f), "The derived tenacity has to carry the innate's ten percent.");
	}

	[Test]
	public void TheBulwarkCountsTheSoldiersStandingBesideTheHero()
	{
		using MeleeMatch match = MeleeMatch.StartDuel("Vale", "Sable");
		UnitId hero = match.Hero(1);

		match.Step(1.2f);
		float restedArmor = match.ModifierTotalFrom(hero, "bulwark/hero", "Armor");

		Vector3 heroPosition = match.PositionOf(hero);
		match.SpawnSoldier(1, "unit/shieldbearer", heroPosition + new Vector3(2f, 0f, 0f));
		match.SpawnSoldier(1, "unit/axeman", heroPosition + new Vector3(0f, 0f, 2f));

		match.Step(1.2f);

		Assert.That(match.ModifierTotalFrom(hero, "bulwark/hero", "Armor"), Is.EqualTo(restedArmor + 2f * BulwarkArmorPerSoldier).Within(0.15f), "The bulwark has to count every soldier inside its radius.");
	}

	[Test]
	public void SoldiersOutsideTheBulwarkAreNotCounted()
	{
		using MeleeMatch match = MeleeMatch.StartDuel("Vale", "Sable");
		UnitId hero = match.Hero(1);

		match.Step(1.2f);
		float restedArmor = match.ModifierTotalFrom(hero, "bulwark/hero", "Armor");

		Vector3 heroPosition = match.PositionOf(hero);
		match.SpawnSoldier(1, "unit/shieldbearer", heroPosition + new Vector3(20f, 0f, 0f));
		match.Step(1.2f);

		Assert.That(match.ModifierTotalFrom(hero, "bulwark/hero", "Armor"), Is.EqualTo(restedArmor).Within(0.15f), "A soldier standing beyond the bulwark radius must not add armour.");
	}

	[Test]
	public void SablessHeroBurnsWhatItsArmyMarks()
	{
		using MeleeMatch match = MeleeMatch.StartDuel("Sable", "Vale");
		UnitId victim = match.SpawnSoldier(2, "unit/shieldbearer", new Vector3(0f, 0f, 0f));
		Assert.That(match.IsAlive(victim), Is.True, "The test needs a living victim.");

		match.Api.DealTypedDamage(match.Hero(1), victim, 10f, "physical");
		Assert.That(match.Api.UnitHasBuff(victim, "buff/cinder_mark"), Is.True, "A Sable hit has to leave the cinder mark on its victim.");

		float burningHealth = match.HealthOf(victim);
		match.Step(1f);
		float burned = burningHealth - match.HealthOf(victim);
		Assert.That(burned, Is.InRange(CinderMarkDamagePerSecond - 2f, CinderMarkDamagePerSecond + 2f), "The mark has to burn for its authored damage per second.");

		match.Step(1.5f);
		Assert.That(match.Api.UnitHasBuff(victim, "buff/cinder_mark"), Is.False, "The two second mark has to run out once the hits stop.");

		float cooledHealth = match.HealthOf(victim);
		match.Step(1f);
		Assert.That(match.HealthOf(victim), Is.GreaterThanOrEqualTo(cooledHealth - 0.01f), "A victim that is no longer marked must stop losing health to the burn.");
	}

	[Test]
	public void TheNexusHardensWithEveryWorkingBuilding()
	{
		using MeleeMatch match = MeleeMatch.StartDuel("Vale", "Sable");
		UnitId nexus = match.Nexus(1);

		match.Step(1.2f);
		float restedArmor = match.ModifierTotalFrom(nexus, "bulwark/buildings", "Armor");
		float restedResistance = match.ModifierTotalFrom(nexus, "bulwark/buildings", "MagicResistance");
		Assert.That(restedArmor, Is.EqualTo(BulwarkArmorPerBuilding).Within(0.01f), "A lone Nexus counts as the one building its bulwark covers.");

		UnitId site = match.PlaceDraftBuilding(SitePosition);
		match.Step(1.2f);
		Assert.That(match.ModifierTotalFrom(nexus, "bulwark/buildings", "Armor"), Is.EqualTo(restedArmor).Within(0.01f), "A site still under construction must not harden the Nexus.");

		float waited = 0f;
		while (match.IsUnderConstruction(site) && waited < MeleeMatch.LongestAuthoredConstructionSeconds + 5f)
		{
			match.Step(1f);
			waited += 1f;
		}

		Assert.That(match.IsUnderConstruction(site), Is.False, "The site has to finish within the authored construction time.");
		match.Step(1.2f);

		Assert.That(match.ModifierTotalFrom(nexus, "bulwark/buildings", "Armor"), Is.EqualTo(restedArmor + BulwarkArmorPerBuilding).Within(0.01f), "A finished building adds its share of armour to the Nexus.");
		Assert.That(match.ModifierTotalFrom(nexus, "bulwark/buildings", "MagicResistance"), Is.EqualTo(restedResistance + BulwarkArmorPerBuilding).Within(0.01f), "The bulwark hardens the Nexus against magic as well as steel.");
	}

	[Test]
	public void CorvusGivesEverySoldierBuildingAnExtraProduction()
	{
		using MeleeMatch match = MeleeMatch.StartDuel("Corvus", "Vale");
		UnitId hero = match.Hero(1);
		Assert.That(match.TemplateOf(hero), Is.EqualTo("hero/corvus"), "The Corvus faction has to select the Quartermaster.");
		Assert.That(match.ModifierTotalFrom(hero, "innate/unbroken", "Armor"), Is.EqualTo(0f).Within(0.01f), "Corvus fights on the ledger its buildings keep, not on an innate.");

		UnitId building = match.PlaceCompletedBuilding(SitePosition);
		string templateId = match.TemplateOf(building);
		int ledMaximum = (int)((MeleeContent.SoldierProductionsOf(templateId) + 1 + MeleeContent.WorkerProductionsOf(templateId)) * MeleeContent.ProductionMana);

		Assert.That(match.MaxManaOf(building), Is.EqualTo(ledMaximum), "The Standing Ledger has to let the building hold one production more than its authored maximum.");

		string soldierType = MeleeContent.SoldierFor(templateId);
		for (int index = 0; index < MeleeContent.CapacityOf(templateId); index++)
		{
			match.SpawnSoldier(1, soldierType, new Vector3(-40f + index * 1.5f, 0f, -56f));
		}

		match.Api.SetUnitMana(building, 0f);
		match.Step(MeleeContent.SoldierProductionSecondsOf(templateId) * 8f);

		Assert.That(match.ManaOf(building), Is.EqualTo(ledMaximum).Within(0.5f), "The ledger's extra production has to fill with the building's own mana.");
	}

	[Test]
	public void AHitOnAStructureWarnsItsOwner()
	{
		using MeleeMatch match = MeleeMatch.StartDuel("Vale", "Sable");
		UnitId nexus = match.Nexus(2);

		match.Api.DealTypedDamage(match.Hero(1), nexus, 20f, "physical");
		match.Step(0.1f);

		Assert.That(match.CountEventsFor(2, PresentationKind.PlayerFeedbackText), Is.GreaterThan(0), "A defender has to be told its ground is under attack from the first hit.");
		Assert.That(match.CountEventsFor(2, PresentationKind.MinimapPing), Is.GreaterThan(0), "The warning has to point at what is being hit.");
	}

	[Test]
	public void TheWarningIsThrottledRatherThanRepeatedEveryHit()
	{
		using MeleeMatch match = MeleeMatch.StartDuel("Vale", "Sable");
		UnitId nexus = match.Nexus(2);
		UnitId attacker = match.Hero(1);

		match.Api.DealTypedDamage(attacker, nexus, 20f, "physical");
		match.Step(0.1f);
		int warningsAfterFirstHit = match.CountEventsFor(2, PresentationKind.PlayerFeedbackText);
		Assert.That(warningsAfterFirstHit, Is.GreaterThan(0), "The first hit has to warn the defender.");

		match.Api.DealTypedDamage(attacker, nexus, 20f, "physical");
		match.Step(UnderAttackAlertCooldownSeconds - 1f);
		Assert.That(match.CountEventsFor(2, PresentationKind.PlayerFeedbackText), Is.EqualTo(warningsAfterFirstHit), "A second hit inside the cooldown must not repeat the warning.");

		match.Step(2f);
		match.Api.DealTypedDamage(attacker, nexus, 20f, "physical");
		match.Step(0.1f);
		Assert.That(match.CountEventsFor(2, PresentationKind.PlayerFeedbackText), Is.GreaterThan(warningsAfterFirstHit), "A hit once the cooldown has run out has to warn the defender again.");
	}
}
