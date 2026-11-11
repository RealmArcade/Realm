using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Ecs.Components.Combat;
using Realm.Shared.Simulation;

namespace Realm.Simulation.Tests;

/// <summary>
///     An attack order names a target rather than a place: a hero sent at an enemy across the board has to walk there.
///     The reachability test that decides whether a route can ever land a blow therefore has to answer about the whole
///     route, not about the first corridor a pathfinder happens to return.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeDistantAttackTests
{
	private ComposedMeleeMap? _board;

	[OneTimeSetUp]
	public void OneTimeSetUp()
	{
		MeleeTestMaps.RequireMaps();
		_board = ComposedMeleeMap.Assemble();
	}

	[OneTimeTearDown]
	public void OneTimeTearDown() => _board?.Dispose();

	[SetUp]
	public void SetUp()
	{
		MeleeTestMaps.RequireMaps();
	}

	[Test]
	public void HeroOrderedOntoAnEnemyAcrossTheDuelChasesIt()
	{
		using MeleeMatch match = MeleeMatch.StartDuel("Vale", "Sable");
		AssertFarAttackChases(match, "The duel");
	}

	[Test]
	public void HeroOrderedOntoAnEnemyAcrossTheComposedBoardChasesIt()
	{
		using MeleeMatch match = MeleeMatch.StartOnMap(_board!.Folder, new[] { "Vale", "Sable" }, _board.ScriptPath);
		AssertFarAttackChases(match, "The composed board");
	}

	[Test]
	public void RevivedHeroOrderedOntoAnEnemyAcrossTheComposedBoardChasesIt()
	{
		using MeleeMatch match = MeleeMatch.StartOnMap(_board!.Folder, new[] { "Vale", "Sable" }, _board.ScriptPath);
		UnitId original = match.Hero(1);
		Assert.That(match.Simulation.Map.Simulation.CombatDamageService, Is.Not.Null, "The match has to have a damage service for the sacrifice.");

		match.Simulation.Map.Simulation.CombatDamageService!.DealResolvedDamage(new Entity(original.Value), new Entity(original.Value), 1_000_000f, "physical");
		match.Step(1f);
		Assert.That(match.IsAlive(original), Is.False, "The sacrifice has to take the hero off the board.");

		UnitId revived = UnitId.Invalid;
		for (int second = 0; second < 30 && !revived.IsValid; second++)
		{
			match.Step(1f);
			UnitId candidate = match.Hero(1);
			if (candidate.IsValid && match.IsAlive(candidate)) revived = candidate;
		}

		Assert.That(revived.IsValid, Is.True, "The hero has to return on its revival timer.");
		Assert.That(revived, Is.Not.EqualTo(original), "A revival is a new body, which is the state the order has to survive.");

		Vector3 start = match.PositionOf(revived);
		float separation = Vector3.Distance(start, match.PositionOf(match.Nexus(2)));
		Assert.That(separation, Is.GreaterThan(100f), $"The revived hero has to stand far enough from the enemy base (distance {separation:0}).");

		ServerCommandResult order = match.Attack(1, revived, match.Nexus(2));
		Assert.That(order.HasRejection, Is.False, order.RejectionReason);

		match.Step(3f);

		Assert.That(match.Has<AttackTarget>(revived), Is.True, "A far attack order given to a revived hero has to be kept while it walks.");
		Assert.That(Vector3.Distance(match.PositionOf(revived), start), Is.GreaterThan(5f), "A revived hero has to walk toward a target across the board.");
	}

	private static void AssertFarAttackChases(MeleeMatch match, string board)
	{
		UnitId hero = match.Hero(1);
		UnitId enemyNexus = match.Nexus(2);
		Vector3 start = match.PositionOf(hero);

		float separation = Vector3.Distance(start, match.PositionOf(enemyNexus));
		Assert.That(separation, Is.GreaterThan(100f), $"{board} has to stand its bases far enough apart for the far order to mean something (distance {separation:0}).");

		ServerCommandResult order = match.Attack(1, hero, enemyNexus);
		Assert.That(order.HasRejection, Is.False, order.RejectionReason);

		match.Step(1f);

		Assert.That(match.Has<AttackTarget>(hero), Is.True, $"{board} has to keep a far attack's target while the hero is still walking, or the order is silently dropped.");
		Assert.That(Vector3.Distance(match.PositionOf(hero), start), Is.GreaterThan(3f), $"{board} has to walk the hero toward a target across the board instead of standing still.");
	}
}
