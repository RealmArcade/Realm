using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays the authored economy on the authority: what a finished building pays, what a site still under construction
///     does not, and how each further building taxes the ones before it.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeEconomyTests
{
	private const float IncomeSeconds = 60f;
	private const float IncomeTolerance = 1f;
	private const float IncomeTaxPerBuilding = 0.15f;

	private static readonly Vector3 FirstSitePosition = new(-50f, 0f, -50f);
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
	public void AFinishedBuildingPaysItsAuthoredIncome()
	{
		UnitId building = _match.PlaceCompletedBuilding(FirstSitePosition);
		string templateId = _match.TemplateOf(building);

		float goldBefore = _match.Gold(1);
		float woodBefore = _match.Wood(1);

		_match.Step(IncomeSeconds);

		Assert.That(_match.Gold(1) - goldBefore, Is.EqualTo(MeleeContent.GoldPerMinuteOf(templateId)).Within(IncomeTolerance), "A building pays its authored gold per minute.");
		Assert.That(_match.Wood(1) - woodBefore, Is.EqualTo(MeleeContent.WoodPerMinuteOf(templateId)).Within(IncomeTolerance), "A building pays its authored wood per minute.");
	}

	[Test]
	public void ASitePaysNothingBeforeItStands()
	{
		UnitId site = _match.PlaceDraftBuilding(FirstSitePosition);
		Assert.That(_match.IsUnderConstruction(site), Is.True, "The test has to hold a site that is still building.");

		float goldBefore = _match.Gold(1);
		float woodBefore = _match.Wood(1);

		_match.Step(10f);

		Assert.That(_match.IsUnderConstruction(site), Is.True, "Ten seconds must not finish the site.");
		Assert.That(_match.Gold(1), Is.EqualTo(goldBefore).Within(0.01f), "A building that does not stand pays nothing.");
		Assert.That(_match.Wood(1), Is.EqualTo(woodBefore).Within(0.01f), "A building that does not stand pays nothing.");
	}

	[Test]
	public void EachFurtherBuildingTaxesTheOnesBeforeIt()
	{
		UnitId first = _match.PlaceCompletedBuilding(FirstSitePosition);
		UnitId second = _match.PlaceCompletedBuilding(SecondSitePosition);

		string firstTemplate = _match.TemplateOf(first);
		string secondTemplate = _match.TemplateOf(second);
		Assert.That(secondTemplate, Is.Not.EqualTo(firstTemplate), "The deck never offers a building the player already stands on.");

		float goldBefore = _match.Gold(1);
		float woodBefore = _match.Wood(1);

		_match.Step(IncomeSeconds);

		float expectedGold = MeleeContent.GoldPerMinuteOf(firstTemplate) + MeleeContent.GoldPerMinuteOf(secondTemplate) / (1f + IncomeTaxPerBuilding);
		float expectedWood = MeleeContent.WoodPerMinuteOf(firstTemplate) + MeleeContent.WoodPerMinuteOf(secondTemplate) / (1f + IncomeTaxPerBuilding);

		Assert.That(_match.Gold(1) - goldBefore, Is.EqualTo(expectedGold).Within(IncomeTolerance), "The second working building pays its authored rate through the tax of the first.");
		Assert.That(_match.Wood(1) - woodBefore, Is.EqualTo(expectedWood).Within(IncomeTolerance), "The tax applies to wood as well as gold.");
	}
}
