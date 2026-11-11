using System;
using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays the authored economy on the authority: a finished building's worker carries its advertised swing out of the
///     nearest node and back, a site that still stands unfinished takes nothing, and a node the worker has to walk to is
///     worked less often than one beside the building. The tests read what the nodes lost rather than what the player was
///     paid, because a bounty from a skirmish lands in the same purse as a delivery.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeEconomyTests
{
	private const float IncomeSeconds = 60f;
	private const string GoldMineTemplateId = "resource/melee_gold_mine";
	private const string WoodlandTemplateId = "resource/melee_woodland";

	private static readonly Vector3 NearNodeSitePosition = new(-50f, 0f, -50f);

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
	public void AFinishedBuildingsWorkerTakesItsAdvertisedSwingOutOfTheNode()
	{
		UnitId building = _match.PlaceCompletedBuilding(NearNodeSitePosition);
		string templateId = _match.TemplateOf(building);
		float swing = MeleeContent.GoldPerSwingOf(templateId) + MeleeContent.WoodPerSwingOf(templateId);
		Assert.That(swing, Is.GreaterThan(0f), "The drawn package has to advertise something its worker can carry.");

		float nodesBefore = _match.TotalResourceAmount();
		float carriedInHands = _match.TotalCarriedAmount(1);

		_match.Step(IncomeSeconds);

		float nodeLoss = nodesBefore - _match.TotalResourceAmount();

		Assert.That(nodeLoss, Is.GreaterThan(0f), "A finished building has to put a worker on a node.");
		Assert.That(carriedInHands, Is.EqualTo(0f), "Nothing is in hand before the worker is spawned.");

		float swings = nodeLoss / swing;
		Assert.That(swings, Is.EqualTo(MathF.Round(swings)).Within(0.02f), "Every swing has to take exactly the amount the building advertises.");
	}

	[Test]
	public void ASitePaysNothingBeforeItStands()
	{
		UnitId site = _match.PlaceDraftBuilding(NearNodeSitePosition);
		Assert.That(_match.IsUnderConstruction(site), Is.True, "The test has to hold a site that is still building.");

		float goldBefore = _match.Gold(1);
		float woodBefore = _match.Wood(1);
		float nodesBefore = _match.TotalResourceAmount();

		_match.Step(10f);

		Assert.That(_match.IsUnderConstruction(site), Is.True, "Ten seconds must not finish the site.");
		Assert.That(_match.Gold(1), Is.EqualTo(goldBefore).Within(0.01f), "A building that does not stand pays nothing.");
		Assert.That(_match.Wood(1), Is.EqualTo(woodBefore).Within(0.01f), "A building that does not stand pays nothing.");
		Assert.That(_match.TotalResourceAmount(), Is.EqualTo(nodesBefore).Within(0.01f), "A site nobody works takes nothing from a node.");
	}

	[Test]
	public void AWorkerWhoseNodeRunsDryMovesToTheNextOne()
	{
		UnitId building = _match.PlaceCompletedBuilding(NearNodeSitePosition);
		string templateId = _match.TemplateOf(building);
		string nodeTemplate = MeleeContent.GoldPerSwingOf(templateId) > 0f ? GoldMineTemplateId : WoodlandTemplateId;

		UnitId firstNode = _match.ResourceNode(nodeTemplate, NearNodeSitePosition);
		Assert.That(firstNode.IsValid, Is.True, "The board has to place the first node the building works.");

		_match.Step(8f);
		_match.EmptyResourceNode(firstNode);
		Assert.That(_match.ResourceAmount(firstNode), Is.EqualTo(0f).Within(0.01f), "The test has to empty the node the worker was using.");

		float nodesBefore = _match.TotalResourceAmount();
		_match.Step(IncomeSeconds);
		float nodesAfter = _match.TotalResourceAmount();

		Assert.That(_match.ResourceAmount(firstNode), Is.EqualTo(0f).Within(0.01f), "A dry node stays dry.");
		Assert.That(nodesBefore - nodesAfter, Is.GreaterThan(0f), "A worker whose node ran dry has to find the next node of the same resource rather than stand idle.");
	}

	[Test]
	public void TheSecondIncomeBuildingPaysItsRankShareWhileTheFirstKeepsItsWholeSwing()
	{
		// The buildings are raised straight through the engine rather than drawn from the deck, so the two ranks are
		// known before a shuffle decides which package a player would have been offered.
		UnitId firstBuilding = _match.Api.SpawnBuildingUnderConstruction("building/bulwark_hall", NearNodeSitePosition, 1, 0f);
		_match.Step(1f);
		UnitId secondBuilding = _match.Api.SpawnBuildingUnderConstruction("building/warband_yard", new Vector3(-26f, 0f, 9f), 1, 0f);
		_match.Step(1f);

		Assert.That(firstBuilding.IsValid && secondBuilding.IsValid, Is.True, "Both buildings have to be raised.");
		Assert.That(firstBuilding.UniqueId, Is.LessThan(secondBuilding.UniqueId), "The first building has to be the older one, which is what its rank is read from.");
		Assert.That(_match.IsUnderConstruction(firstBuilding) || _match.IsUnderConstruction(secondBuilding), Is.False, "Both buildings have to stand before their workers can be read.");

		UnitId firstNode = _match.ResourceNode(GoldMineTemplateId, _match.PositionOf(firstBuilding));
		UnitId secondNode = _match.ResourceNode(GoldMineTemplateId, _match.PositionOf(secondBuilding));
		Assert.That(firstNode.IsValid && secondNode.IsValid && firstNode.Value != secondNode.Value, Is.True,
			"The two buildings have to work different nodes, or neither worker's delivery can be told apart from the other's.");

		float goldBefore = _match.Gold(1);
		float firstNodeBefore = _match.ResourceAmount(firstNode);
		float secondNodeBefore = _match.ResourceAmount(secondNode);

		bool delivered = false;
		for (int step = 0; step < 240 && !delivered; step++)
		{
			_match.Step(0.5f);
			delivered = _match.Gold(1) > goldBefore
			            && _match.ResourceAmount(firstNode) < firstNodeBefore
			            && _match.ResourceAmount(secondNode) < secondNodeBefore;
		}
		Assert.That(delivered, Is.True, "Both income buildings have to deliver at least once.");

		// Sampling while no worker holds anything makes what the nodes lost exactly what the workers have delivered, so
		// the tax reads off the purse without having to know how much is still being carried home.
		bool handsEmpty = false;
		for (int step = 0; step < 400 && !handsEmpty; step++)
		{
			_match.Step(0.1f);
			handsEmpty = _match.TotalCarriedAmount(1) <= 0.001f;
		}
		Assert.That(handsEmpty, Is.True, "Both workers have to be between trips for the accounting to close.");

		float paid = _match.Gold(1) - goldBefore;
		float firstDelivered = firstNodeBefore - _match.ResourceAmount(firstNode);
		float secondDelivered = secondNodeBefore - _match.ResourceAmount(secondNode);
		float expected = firstDelivered * MeleeContent.IncomeShareOfRank(1) + secondDelivered * MeleeContent.IncomeShareOfRank(2);

		Assert.That(paid, Is.EqualTo(expected).Within(1f),
			$"The first building keeps its whole swing and the second keeps its rank's share: paid {paid:0.##} for {firstDelivered:0.##} and {secondDelivered:0.##} delivered.");
		Assert.That(paid, Is.LessThan(firstDelivered + secondDelivered), "The second building has to be taxed rather than paid in full.");
	}

	[Test]
	public void ANodeAcrossTheBoardIsWorkedLessThanOneBesideTheBuilding()
	{
		UnitId nearBuilding = _match.PlaceCompletedBuilding(NearNodeSitePosition);
		string templateId = _match.TemplateOf(nearBuilding);
		string nodeTemplate = MeleeContent.GoldPerSwingOf(templateId) > 0f ? GoldMineTemplateId : WoodlandTemplateId;

		UnitId nearNode = _match.ResourceNode(nodeTemplate, NearNodeSitePosition);
		Assert.That(nearNode.IsValid, Is.True, "The board has to place the node the building works.");

		float nearBefore = _match.ResourceAmount(nearNode);
		_match.Step(IncomeSeconds);
		float nearLoss = nearBefore - _match.ResourceAmount(nearNode);

		using MeleeMatch distant = MeleeMatch.StartDuel();
		string drawnLabel = _match.Api.GetUnitName(templateId);
		UnitId farBuilding = distant.PlaceCompletedBuilding(NearNodeSitePosition, label => label == drawnLabel);
		Assert.That(distant.TemplateOf(farBuilding), Is.EqualTo(templateId), "Both matches have to draw the same building for the comparison to mean anything.");

		UnitId farNode = distant.ResourceNode(nodeTemplate, NearNodeSitePosition);
		Assert.That(farNode.IsValid, Is.True, "The board has to place the node the building works.");

		// The same node, moved away from the same building, so the only thing that differs between the two runs is how
		// long the worker has to walk.
		distant.MoveProp(farNode, distant.PositionOf(farNode) + new Vector3(60f, 0f, 0f));

		float farBefore = distant.ResourceAmount(farNode);
		distant.Step(IncomeSeconds);
		float farLoss = farBefore - distant.ResourceAmount(farNode);

		Assert.That(nearLoss, Is.GreaterThan(0f), "The building beside its node has to work it.");
		Assert.That(farLoss, Is.LessThan(nearLoss), "Walking further for the same swing has to work the node less, which is what makes node distance matter.");
	}
}
