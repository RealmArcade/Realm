using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays the relics on the authority: what the hero's inventory does to the army already standing under it, to the
///     buildings that feed it and to whoever hits them.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeRelicTests
{
	private const string SigilOfTheWall = "item/sigil_of_the_wall";
	private const string CouriersKnot = "item/couriers_knot";
	private const string MusterBell = "item/muster_bell";
	private const string SpikedBarricades = "item/spiked_barricades";
	private const string DeepGranaries = "item/deep_granaries";
	private const string WardensWrit = "item/wardens_writ";
	private const float SigilMaxHealthFactor = 1.1f;
	private const float WritTenacity = 0.1f;
	private const float DeepGranariesIncomeFactor = 1.1f;
	private const float CouriersKnotRechargeFactor = 0.9f;
	private const float SpikedBarricadesReflectFraction = 0.25f;
	private const float IncomeSeconds = 60f;

	private static readonly Vector3 FrontFormationPosition = new(-46f, 0f, -52f);
	private static readonly Vector3 BackFormationPosition = new(-44f, 0f, -52f);
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

	private static string[] GoldPayingBuildingIds()
	{
		return new[]
		{
			"building/bulwark_hall",
			"building/warband_yard",
			"building/spear_lodge",
			"building/mender_shrine",
			"building/signal_post",
			"building/vigil_bastion"
		}.Where(templateId => MeleeContent.GoldPerSwingOf(templateId) > 0f).ToArray();
	}

	[Test]
	public void TheSigilOfTheWallHardensOnlyTheFrontFormation()
	{
		UnitId shieldbearer = _match.SpawnSoldier(1, "unit/shieldbearer", FrontFormationPosition);
		UnitId axeman = _match.SpawnSoldier(1, "unit/axeman", BackFormationPosition);

		float shieldbearerMaxHealth = _match.MaxHealthOf(shieldbearer);
		float axemanMaxHealth = _match.MaxHealthOf(axeman);

		Assert.That(_match.AddRelic(1, SigilOfTheWall), Is.True, "The hero has to be able to carry the Sigil of the Wall.");
		_match.Step(0.2f);

		Assert.That(_match.MaxHealthOf(shieldbearer), Is.EqualTo(shieldbearerMaxHealth * SigilMaxHealthFactor).Within(1f), "The sigil has to raise the front formation's health by a tenth.");
		Assert.That(_match.MaxHealthOf(axeman), Is.EqualTo(axemanMaxHealth).Within(0.5f), "A soldier outside the front formation must keep its authored health.");
	}

	[Test]
	public void TheWardensWritGivesTheWholeArmyTenacity()
	{
		UnitId soldier = _match.SpawnSoldier(1, "unit/axeman", BackFormationPosition);
		UnitId hero = _match.Hero(1);
		float soldierTenacityBefore = _match.TenacityOf(soldier);

		Assert.That(_match.AddRelic(1, WardensWrit), Is.True, "The hero has to be able to carry the Warden's Writ.");
		_match.Step(0.2f);

		Assert.That(_match.ModifierTotalFrom(soldier, "relic/wardens_writ", "Tenacity"), Is.EqualTo(WritTenacity).Within(0.001f), "The writ has to add a tenth of tenacity to every soldier.");
		Assert.That(_match.TenacityOf(soldier), Is.EqualTo(soldierTenacityBefore + WritTenacity).Within(0.001f), "The soldier's derived tenacity has to rise by the writ's tenth.");
		Assert.That(_match.ModifierTotalFrom(hero, "relic/wardens_writ", "Tenacity"), Is.EqualTo(WritTenacity).Within(0.001f), "The writ has to give the hero tenacity as well.");
	}

	[Test]
	public void RelicsAreAppliedOnceAndStay()
	{
		UnitId hero = _match.Hero(1);

		Assert.That(_match.AddRelic(1, WardensWrit), Is.True, "The hero has to be able to carry the Warden's Writ.");
		_match.Step(0.2f);
		_match.Step(5f);

		Assert.That(_match.ModifierTotalFrom(hero, "relic/wardens_writ", "Tenacity"), Is.EqualTo(WritTenacity).Within(0.001f), "A relic the hero keeps must not stack with itself over time.");
	}

	[Test]
	public void DeepGranariesRaisesTheGoldIncomeByATenth()
	{
		string[] goldPayingLabels = GoldPayingBuildingIds().Select(_match.Api.GetUnitName).ToArray();
		Func<string, bool> paysGold = label => Array.IndexOf(goldPayingLabels, label) >= 0;

		UnitId building = _match.PlaceCompletedBuilding(SitePosition, paysGold);
		string templateId = _match.TemplateOf(building);

		float plainGoldBefore = _match.Gold(1);
		_match.Step(IncomeSeconds);
		float plainIncome = _match.Gold(1) - plainGoldBefore;

		using MeleeMatch granaried = MeleeMatch.StartDuel();
		string drawnLabel = _match.Api.GetUnitName(templateId);
		UnitId granariedBuilding = granaried.PlaceCompletedBuilding(SitePosition, label => label == drawnLabel);
		Assert.That(granaried.TemplateOf(granariedBuilding), Is.EqualTo(templateId), "Both matches have to draw the same building for the comparison to mean anything.");
		Assert.That(granaried.AddRelic(1, DeepGranaries), Is.True, "The hero has to be able to carry the Deep Granaries.");

		float granariedGoldBefore = granaried.Gold(1);
		granaried.Step(IncomeSeconds);
		float granariedIncome = granaried.Gold(1) - granariedGoldBefore;

		Assert.That(plainIncome, Is.GreaterThan(0f), "The undecorated building's worker has to carry gold home before the comparison means anything.");
		Assert.That(
			granariedIncome,
			Is.EqualTo(plainIncome * DeepGranariesIncomeFactor).Within(MathF.Max(1f, plainIncome * 0.1f)),
			"The Deep Granaries has to raise what a worker carries home by a tenth.");
	}

	[TestCase(CouriersKnot)]
	[TestCase(MusterBell)]
	public void RechargeRelicsShortenTheChargeClock(string relicId)
	{
		UnitId building = _match.PlaceCompletedBuilding(SitePosition);
		string templateId = _match.TemplateOf(building);
		float authoredRecharge = MeleeContent.RechargeSecondsOf(templateId);

		Assert.That(_match.StepUntilSoldiers(1), Is.EqualTo(1), "The building has to raise its first soldier first.");
		Assert.That(_match.AddRelic(1, relicId), Is.True, $"The hero has to be able to carry {relicId}.");

		string soldierType = MeleeContent.SoldierFor(templateId);
		for (int index = 1; index < MeleeContent.CapacityOf(templateId); index++)
		{
			_match.SpawnSoldier(1, soldierType, new Vector3(-40f + index * 1.5f, 0f, -56f));
		}

		float shortenedRecharge = authoredRecharge * CouriersKnotRechargeFactor;
		_match.Step(shortenedRecharge - 1f);
		int chargesBeforeTheClock = _match.CountCharges(building);

		_match.Step(2f);
		Assert.That(_match.CountCharges(building), Is.GreaterThan(chargesBeforeTheClock), $"{relicId} has to bring the charge back on its shortened clock.");
	}

	[Test]
	public void SpikedBarricadesReturnsAQuarterOfTheHitAtWhoeverDealtIt()
	{
		using MeleeMatch defended = MeleeMatch.StartDuel();
		Assert.That(defended.AddRelic(1, SpikedBarricades), Is.True, "The hero has to be able to carry the Spiked Barricades.");
		defended.Step(0.2f);

		UnitId nexus = defended.Nexus(1);
		UnitId attacker = defended.Hero(2);

		float attackerHealthBefore = defended.HealthOf(attacker);
		float nexusHealthBefore = defended.HealthOf(nexus);

		defended.Api.DealTypedDamage(attacker, nexus, 40f, "physical");
		defended.Step(0.1f);

		float nexusDamage = nexusHealthBefore - defended.HealthOf(nexus);
		float returned = attackerHealthBefore - defended.HealthOf(attacker);

		Assert.That(nexusDamage, Is.GreaterThan(0f), "The hit has to land on the Nexus.");
		Assert.That(returned, Is.GreaterThan(0f), "The barricades have to return a share of the hit to the attacker.");
		Assert.That(returned, Is.LessThanOrEqualTo(nexusDamage * SpikedBarricadesReflectFraction + 1f), "The return is a quarter of the hit, never a copy of it.");
	}
}
