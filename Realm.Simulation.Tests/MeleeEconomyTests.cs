using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Tags;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays the authored economy on the authority: a building raises its own workers on its own production profile, each
///     worker attacks the node its building assigns with its own combat statistics, every point of damage becomes a piece
///     of the node's item in the worker's inventory, and the pieces are delivered at a drop-off. Workers are attackable
///     units, not an abstract income rate.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeEconomyTests
{
	private const string GoldMineTemplateId = "resource/melee_gold_mine";
	private const string WoodlandTemplateId = "resource/melee_woodland";
	private const string GoldItemId = "item/gold";
	private const string WoodItemId = "item/wood";

	private static readonly Vector3 SitePosition = new(-50f, 0f, -50f);
	private static readonly Vector3 SecondSitePosition = new(-26f, 0f, 9f);

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
	public void AFinishedBuildingRaisesItsWorkerAfterItsOwnProductionTime()
	{
		UnitId building = _match.Api.SpawnBuildingUnderConstruction("building/bulwark_hall", SitePosition, 1, 0f);
		Assert.That(building.IsValid, Is.True, "The test has to raise the building its worker belongs to.");

		_match.Step(2f);
		Assert.That(WorkersOf(1), Is.Empty, "A worker is produced on the building's worker production timer, not the instant it stands.");

		_match.Step(MeleeContent.WorkerProductionSecondsOf("building/bulwark_hall") + 1f);

		List<UnitId> workers = WorkersOf(1);
		Assert.That(workers.Count, Is.EqualTo(1), "The building has to raise one worker from its opening charge.");
		Assert.That(_match.World.Has<Worker>(new Entity(workers[0].Value)), Is.True, "Every worker the ruleset raises carries the worker tag.");
		Assert.That(_match.World.Has<Invulnerable>(new Entity(workers[0].Value)), Is.False, "Workers are attackable units.");
		Assert.That(_match.Api.GetUnitWorkerAssignment(workers[0]), Is.EqualTo("gold"),
			"The building has to assign the resource its worker gathers.");
	}

	[Test]
	public void WorkersAreRaisedAtTheConstructionOrderOfTheirBuilding()
	{
		UnitId firstBuilding = _match.Api.SpawnBuildingUnderConstruction("building/bulwark_hall", SitePosition, 1, 0f);
		_match.Step(1f);
		UnitId secondBuilding = _match.Api.SpawnBuildingUnderConstruction("building/archery_range", SecondSitePosition, 1, 0f);
		_match.Step(2f);

		Assert.That(firstBuilding.IsValid && secondBuilding.IsValid, Is.True, "Both buildings have to stand.");
		_match.Step(20f);

		var levels = new HashSet<int>();
		foreach (UnitId worker in WorkersOf(1))
		{
			int level = _match.World.Get<Level>(new Entity(worker.Value)).Value;
			levels.Add(level);

			string expectedAssignment = level == 2 ? "wood" : "gold";
			Assert.That(_match.Api.GetUnitWorkerAssignment(worker), Is.EqualTo(expectedAssignment),
				$"A level {level} worker has to gather the resource of the building that raised it.");
		}

		Assert.That(levels, Does.Contain(1), "The first building has to raise level 1 workers.");
		Assert.That(levels, Does.Contain(2), "The second building has to raise level 2 workers.");
	}

	[Test]
	public void ALaterBuildingRaisesStrongerWorkersOfTheSameTemplate()
	{
		_match.Api.SpawnBuildingUnderConstruction("building/bulwark_hall", SitePosition, 1, 0f);
		_match.Step(1f);
		_match.Api.SpawnBuildingUnderConstruction("building/bulwark_hall", SecondSitePosition, 1, 0f);
		_match.Step(20f);

		float firstWorkerHealth = 0f;
		float secondWorkerHealth = 0f;
		foreach (UnitId worker in WorkersOf(1))
		{
			Assert.That(_match.World.Get<DefinitionId>(new Entity(worker.Value)).Value, Is.EqualTo(MeleeContent.WorkerTemplateId),
				"Every building raises the same worker template.");

			float maxHealth = _match.MaxHealthOf(worker);
			if (_match.World.Get<Level>(new Entity(worker.Value)).Value == 1) firstWorkerHealth = maxHealth;
			else secondWorkerHealth = maxHealth;
		}

		Assert.That(firstWorkerHealth, Is.GreaterThan(0f), "The first building has to raise a level 1 worker.");
		Assert.That(secondWorkerHealth, Is.GreaterThan(firstWorkerHealth),
			"The second building's level 2 worker has to be stronger than the first building's level 1 worker.");
	}

	[Test]
	public void AWorkersLevelStaysAtItsBuildingsConstructionOrder()
	{
		_match.Api.SpawnBuildingUnderConstruction("building/bulwark_hall", SitePosition, 1, 0f);
		_match.Step(20f);

		List<UnitId> workers = WorkersOf(1);
		Assert.That(workers, Is.Not.Empty, "The building has to raise a worker to check its level.");

		UnitId worker = workers[0];
		Assert.That(_match.LevelOf(worker), Is.EqualTo(1), "The first building raises level 1 workers.");

		_match.Api.SetUnitLevel(_match.Hero(1), 4);
		_match.Step(1f);

		Assert.That(_match.LevelOf(worker), Is.EqualTo(1),
			"The army's level growth must not overwrite the level a worker was raised at.");
	}

	[Test]
	public void AWorkerTakesTheNodesItemAndDeliversItToThePurse()
	{
		_match.Api.SpawnBuildingUnderConstruction("building/warband_yard", SitePosition, 1, 0f);
		UnitId node = _match.ResourceNode(GoldMineTemplateId, SitePosition);
		Assert.That(node.IsValid, Is.True, "The board has to place the node the building works.");

		float goldBefore = _match.Gold(1);
		float nodeBefore = _match.ResourceAmount(node);

		bool delivered = false;
		for (int step = 0; step < 240 && !delivered; step++)
		{
			_match.Step(0.5f);
			delivered = _match.Gold(1) > goldBefore;
		}

		Assert.That(delivered, Is.True, "A worker has to take gold from the node and pay it into the player's purse.");
		Assert.That(_match.ResourceAmount(node), Is.LessThan(nodeBefore), "The node has to lose the capacity the worker took.");

		int carried = _match.TotalItemCount(1, GoldItemId);
		float nodeLoss = nodeBefore - _match.ResourceAmount(node);
		float gained = _match.Gold(1) - goldBefore;
		Assert.That(nodeLoss, Is.EqualTo(gained + carried).Within(2f),
			$"Every point the node lost must be one gold piece, whether it was delivered or is still in hand: lost {nodeLoss:0.#}, paid {gained:0.#}, carried {carried}.");
	}

	[Test]
	public void AWorkerWhoseNodeRunsDryMovesToTheNextOne()
	{
		_match.Api.SpawnBuildingUnderConstruction("building/bulwark_hall", SitePosition, 1, 0f);
		UnitId firstNode = _match.ResourceNode(GoldMineTemplateId, SitePosition);
		Assert.That(firstNode.IsValid, Is.True, "The board has to place the first node the building works.");

		_match.Step(20f);
		_match.EmptyResourceNode(firstNode);

		var secondPosition = SitePosition + new Vector3(12f, 0f, 0f);
		_match.Api.SpawnResourceNode(GoldMineTemplateId, secondPosition, 800f);
		UnitId secondNode = _match.ResourceNode(GoldMineTemplateId, secondPosition);
		Assert.That(secondNode.IsValid, Is.True, "The board has to place a second node of the same resource.");
		Assert.That(_match.ResourceAmount(secondNode), Is.GreaterThan(0f), "A node spawned during the match has to hold the resource it was asked for.");

		float secondBefore = _match.ResourceAmount(secondNode);
		_match.Step(45f);

		Assert.That(_match.ResourceAmount(secondNode), Is.LessThan(secondBefore),
			"A worker whose node is exhausted has to move to the next node of its resource.");
	}

	[Test]
	public void ASitePaysNothingBeforeItStands()
	{
		UnitId site = _match.Api.SpawnBuildingUnderConstruction("building/bulwark_hall", SitePosition, 1, 60f);
		Assert.That(_match.IsUnderConstruction(site), Is.True, "The test has to hold a site that is still building.");

		float nodesBefore = _match.TotalResourceAmount();
		_match.Step(10f);

		Assert.That(_match.IsUnderConstruction(site), Is.True, "Ten seconds must not finish a sixty second site.");
		Assert.That(WorkersOf(1), Is.Empty, "A building that does not stand raises no worker.");
		Assert.That(_match.TotalResourceAmount(), Is.EqualTo(nodesBefore).Within(0.01f), "A site nobody works takes nothing from a node.");
	}

	[Test]
	public void WoodStacksOnePerSlotAndGoldStacksFive()
	{
		UnitId worker = _match.Api.SpawnUnitForPlayer(MeleeContent.WorkerTemplateId, new Vector2(SitePosition.X, SitePosition.Z), 1);
		Assert.That(worker.IsValid, Is.True, "The test has to hold a worker to fill.");

		Assert.That(_match.Api.AddItemToUnitWithCharges(worker, WoodItemId, 3), Is.True, "Wood has to fit in the worker's inventory.");
		Assert.That(_match.TryGet(worker, out Inventory wood), Is.True, "The worker has to carry an inventory.");
		Assert.That(wood.Count, Is.EqualTo(3), "Wood holds one piece per slot, so three pieces open three slots.");

		Assert.That(_match.Api.AddItemToUnitWithCharges(worker, GoldItemId, 7), Is.True, "Gold has to fit in the worker's inventory.");
		Assert.That(_match.TryGet(worker, out Inventory inventory), Is.True);
		Assert.That(inventory.GetItemCount(GoldItemId), Is.EqualTo(7), "All seven gold pieces have to be carried.");
		Assert.That(inventory.Count, Is.EqualTo(5), "Seven gold fills five, then opens a second slot for the remaining two.");
	}

	private List<UnitId> WorkersOf(int slot)
	{
		var workers = new List<UnitId>();
		var query = QueryCache.AllPositionAndDefinitionIdNoneDeadQuery;

		_match.World.Query(in query, (Entity entity, ref Position position) =>
		{
			if (_match.World.Get<DefinitionId>(entity).Value != MeleeContent.WorkerTemplateId) return;
			if (!_match.World.Has<UnitOwnerPlayer>(entity)) return;
			if (_match.World.Get<UnitOwnerPlayer>(entity).PlayerIndex != slot) return;

			workers.Add(new UnitId(entity.Id));
		});

		return workers;
	}
}
