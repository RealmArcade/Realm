using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Ecs.Components.Movement;
using Realm.Shared.Simulation;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays the movement orders the command card offers beside attack: a unit stops where it is told, a held unit stays
///     put, an attack-move fights what it meets on the way, and a patrol walks the line it was given and comes back.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeOrderTests
{
	private const float WalkingSeconds = 25f;

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
	public void StopHaltsAWalkingHeroWhereItStands()
	{
		UnitId hero = _match.Hero(1);
		Vector3 start = _match.PositionOf(hero);

		ServerCommandResult move = _match.Order(1, "move", start + new Vector3(0f, 0f, 30f), hero);
		Assert.That(move.HasRejection, Is.False, $"The hero's move order is accepted: {move.RejectionReason}");

		_match.Step(2f);
		Vector3 beforeStop = _match.PositionOf(hero);
		Assert.That(Vector3.Distance(start, beforeStop), Is.GreaterThan(2f), "The hero has to be walking before the stop is sent.");

		ServerCommandResult stop = _match.Order(1, "stop", Vector3.Zero, hero);
		Assert.That(stop.HasRejection, Is.False, $"The hero's stop order is accepted: {stop.RejectionReason}");
		Assert.That(_match.Has<HoldPosition>(hero), Is.False, "Stop clears the order without pinning the unit.");

		_match.Step(4f);

		Assert.That(Vector3.Distance(beforeStop, _match.PositionOf(hero)), Is.LessThan(2f), "A stopped unit has to stay where the order caught it.");
	}

	[Test]
	public void HoldPositionKeepsTheHeroWhereItWasPinned()
	{
		UnitId hero = _match.Hero(1);
		Vector3 start = _match.PositionOf(hero);

		_match.Order(1, "move", start + new Vector3(0f, 0f, 30f), hero);
		_match.Step(2f);

		Vector3 holdPoint = _match.PositionOf(hero);
		ServerCommandResult hold = _match.Order(1, "hold", Vector3.Zero, hero);
		Assert.That(hold.HasRejection, Is.False, $"The hero's hold order is accepted: {hold.RejectionReason}");
		Assert.That(_match.Has<HoldPosition>(hero), Is.True, "Hold has to pin the unit in place.");

		_match.Step(6f);

		Assert.That(Vector3.Distance(holdPoint, _match.PositionOf(hero)), Is.LessThan(2f), "A held unit has to stay at the point it was pinned to.");
	}

	[Test]
	public void AnAttackMoveFightsTheEnemyItMeetsOnTheWay()
	{
		UnitId hero = _match.Hero(1);
		Vector3 start = _match.PositionOf(hero);
		Vector3 destination = start + new Vector3(0f, 0f, 40f);
		UnitId soldier = _match.SpawnSoldier(2, "unit/axeman", start + new Vector3(0f, 0f, 14f));

		ServerCommandResult order = _match.Order(1, "attack_move", destination, hero);
		Assert.That(order.HasRejection, Is.False, $"The hero's attack-move order is accepted: {order.RejectionReason}");

		float healthBefore = _match.HealthOf(soldier);
		float walked = 0f;
		while (walked < WalkingSeconds && _match.HealthOf(soldier) >= healthBefore)
		{
			_match.Step(1f);
			walked += 1f;
		}

		Assert.That(_match.HealthOf(soldier), Is.LessThan(healthBefore), "An attack-move has to stop for the enemy standing in its path.");
		Assert.That(Vector3.Distance(start, _match.PositionOf(hero)), Is.GreaterThan(2f), "An attack-move has to walk the order.");
	}

	[Test]
	public void APatrolWalksTheLineItWasGivenAndTurnsBack()
	{
		UnitId hero = _match.Hero(1);
		Vector3 start = _match.PositionOf(hero);
		Vector3 farPoint = start + new Vector3(0f, 0f, 16f);

		ServerCommandResult order = _match.Order(1, "patrol", farPoint, hero);
		Assert.That(order.HasRejection, Is.False, $"The hero's patrol order is accepted: {order.RejectionReason}");
		Assert.That(_match.Has<Patrol>(hero), Is.True, "Patrol has to leave the line on the unit.");

		bool reachedFarPoint = false;
		float walked = 0f;
		while (walked < WalkingSeconds)
		{
			_match.Step(1f);
			walked += 1f;
			if (Vector3.Distance(_match.PositionOf(hero), farPoint) < 1.5f)
			{
				reachedFarPoint = true;
				break;
			}
		}

		Assert.That(reachedFarPoint, Is.True, $"The patrol has to walk to the point it was given: hero at {_match.PositionOf(hero)}, point at {farPoint}");

		_match.Step(4f);
		Assert.That(Vector3.Distance(_match.PositionOf(hero), farPoint), Is.GreaterThan(4f), "A patrol has to turn back once it reaches its point.");
	}
}
