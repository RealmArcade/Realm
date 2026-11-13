using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Ecs.Components.Tags;
using Realm.Shared.Simulation;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays the rule that gathering is a job the map hands out: the order a seat sends reaches the unit the ruleset
///     registered as a resource worker, the hero carries what it mines back to the Nexus, and the node loses exactly
///     what it gave up.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeGatheringTests
{
	private const string GoldMineTemplateId = "resource/melee_gold_mine";
	private const string WoodlandTemplateId = "resource/melee_woodland";
	private const float GatheringSeconds = 30f;

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
	public void AHeroGathersGoldAndPaysItBackAtTheNexus()
	{
		UnitId hero = _match.Hero(1);
		UnitId mine = _match.ResourceNode(GoldMineTemplateId, _match.PositionOf(hero));
		Assert.That(mine.IsValid, Is.True, "The board places a gold mine near the first seat.");

		float goldBefore = _match.Gold(1);
		float nodeBefore = _match.ResourceAmount(mine);

		ServerCommandResult result = _match.Gather(1, hero, _match.PositionOf(mine));
		Assert.That(result.HasRejection, Is.False, $"The hero's gather order is accepted: {result.RejectionReason}");
		Assert.That(_match.IsGathering(hero), Is.True, "The hero is a resource worker because the ruleset registered its template.");

		_match.Step(GatheringSeconds);

		Assert.That(_match.Gold(1), Is.GreaterThan(goldBefore), "The gold the hero mined reaches the player.");
		Assert.That(_match.ResourceAmount(mine), Is.LessThan(nodeBefore), "The node loses the capacity the hero took.");
	}

	[Test]
	public void AHeroGathersWoodAndPaysItBackAtTheNexus()
	{
		UnitId hero = _match.Hero(1);
		UnitId woodland = _match.ResourceNode(WoodlandTemplateId, _match.PositionOf(hero));
		Assert.That(woodland.IsValid, Is.True, "The board places woodland near the first seat.");

		float woodBefore = _match.Wood(1);
		float nodeBefore = _match.ResourceAmount(woodland);

		ServerCommandResult result = _match.Gather(1, hero, _match.PositionOf(woodland));
		Assert.That(result.HasRejection, Is.False, $"The hero's gather order is accepted: {result.RejectionReason}");

		_match.Step(GatheringSeconds);

		Assert.That(_match.Wood(1), Is.GreaterThan(woodBefore), "The wood the hero gathered reaches the player.");
		Assert.That(_match.ResourceAmount(woodland), Is.LessThan(nodeBefore), "The woodland loses the capacity the hero took.");
	}

	[Test]
	public void AnExhaustedNodeDiesAndStopsBeingAGatherTarget()
	{
		UnitId hero = _match.Hero(1);
		Vector3 position = _match.PositionOf(hero);
		_match.Api.SpawnResourceNode(GoldMineTemplateId, position, 12f);
		UnitId mine = _match.ResourceNode(GoldMineTemplateId, position);
		Assert.That(mine.IsValid, Is.True, "The small node has to stand before it can be worked.");

		ServerCommandResult result = _match.Gather(1, hero, _match.PositionOf(mine));
		Assert.That(result.HasRejection, Is.False, $"The hero's gather order is accepted: {result.RejectionReason}");

		_match.Step(GatheringSeconds);

		Assert.That(_match.ResourceAmount(mine), Is.EqualTo(0f).Within(0.01f), "The node has to lose every point of capacity before it is called exhausted.");
		Assert.That(_match.World.Has<Dead>(new Entity(mine.Id)), Is.True, "A node at zero health is destroyed rather than left standing as an empty node.");
		Assert.That(_match.Api.GetResourceNodes().ToArray(), Does.Not.Contain(mine), "An exhausted node has to leave the pool the map and its workers search.");
		Assert.That(_match.World.Get<Realm.Ecs.Components.Core.Health>(new Entity(mine.Id)).Current, Is.EqualTo(0f).Within(0.01f), "An exhausted node has no health left, which is what makes it destroyed.");
	}

	[Test]
	public void AUnitTheRulesetDidNotRegisterCannotGather()
	{
		UnitId nexus = _match.Nexus(1);
		UnitId mine = _match.ResourceNode(GoldMineTemplateId, _match.PositionOf(nexus));
		Assert.That(mine.IsValid, Is.True, "The board places a gold mine the order can point at.");

		float goldBefore = _match.Gold(1);
		ServerCommandResult result = _match.Gather(1, nexus, _match.PositionOf(mine));

		Assert.That(result.HasRejection, Is.True, "A building is refused the job of gathering.");
		Assert.That(result.RejectionReason, Does.Contain("cannot gather"));
		Assert.That(_match.IsGathering(nexus), Is.False, "The refusal leaves no gatherer on the building.");

		_match.Step(10f);

		Assert.That(_match.Gold(1), Is.EqualTo(goldBefore).Within(0.01f), "A refused order pays nothing.");
	}
}
