using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.MapAPI;

namespace Realm.Simulation.Tests;

/// <summary>
///     Boots the authored melee map on the authority and checks the board it lays out before a player does anything.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeRulesetSetupTests
{
	private const int MaximumBuildingsPerPlayer = 8;

	[SetUp]
	public void SetUp()
	{
		MeleeTestMaps.RequireMaps();
	}

	[Test]
	public void TheMatchSeatsOnlyThePlayersTheRosterNames()
	{
		using MeleeMatch match = MeleeMatch.StartDuel();

		Assert.That(match.ReadPlayer(1).Active, Is.True, "The first seated slot has to be active.");
		Assert.That(match.ReadPlayer(1).IsComputer, Is.False, "A seated human slot must not be handed to the bots.");
		Assert.That(match.ReadPlayer(2).Active, Is.True, "The second seated slot has to be active.");
		Assert.That(match.ReadPlayer(3).Active, Is.False, "A slot the roster does not name must stay unseated.");
	}

	[Test]
	public void EveryPlayerStartsWithOneHeroAndOneNexus()
	{
		using MeleeMatch match = MeleeMatch.StartDuel();

		for (int slot = 1; slot <= match.PlayerCount; slot++)
		{
			UnitId hero = match.Hero(slot);
			Assert.That(match.IsAlive(hero), Is.True, $"Player {slot} must be given a living hero.");
			Assert.That(match.Has<Hero>(hero), Is.True, "The hero has to carry the hero tag the engine and the HUD read.");

			UnitId nexus = match.Nexus(slot);
			Assert.That(match.IsAlive(nexus), Is.True, $"Player {slot} must be given a living Nexus.");
			Assert.That(match.Buildings(slot).Count, Is.EqualTo(1), $"Player {slot} must start with exactly its Nexus and no drafted building.");
		}

		Assert.That(match.TemplateOf(match.Hero(1)), Is.EqualTo("hero/vale"), "The lobby faction has to select the hero template.");
		Assert.That(match.TemplateOf(match.Hero(2)), Is.EqualTo("hero/sable"), "A faction other than the first has to select its own hero.");
	}

	[Test]
	public void TheEconomyStartsAtTheAuthoredValues()
	{
		using MeleeMatch match = MeleeMatch.StartDuel();

		for (int slot = 1; slot <= match.PlayerCount; slot++)
		{
			Assert.That(match.Gold(slot), Is.EqualTo(200f).Within(0.01f), "Every player starts with 200 gold.");
			Assert.That(match.Wood(slot), Is.EqualTo(100f).Within(0.01f), "Every player starts with exactly one construction's wood.");
			Assert.That(match.HeroLevel(slot), Is.EqualTo(1), "A match starts at army level one.");
		}
	}

	[Test]
	public void TheBuildingLimitLeavesRoomForTheAuthoredBuildingsAndTheNexus()
	{
		using MeleeMatch match = MeleeMatch.StartDuel();

		for (int slot = 1; slot <= match.PlayerCount; slot++)
		{
			Assert.That(match.Api.GetPlayerBuildingCount(slot), Is.EqualTo(1), "The Nexus is the only building a player starts with.");
			Assert.That(match.BuildingLimitOf(slot), Is.EqualTo(MaximumBuildingsPerPlayer + 1), "The engine limit has to allow the authored building count beside the Nexus.");
		}
	}

	[Test]
	public void TheBoardSeatsEveryPlayerAtTheAnchorItDeclares()
	{
		using MeleeMatch match = MeleeMatch.StartDuel();

		Assert.That(match.Api.GetSpawnAnchorCount(), Is.EqualTo(4), "The board states where each of its four seats begins.");

		for (int slot = 1; slot <= match.PlayerCount; slot++)
		{
			Vector3 anchor = match.Api.GetSpawnAnchor(slot);
			Assert.That(anchor, Is.Not.EqualTo(Vector3.Zero), $"Slot {slot} has to have an anchor rather than the empty point that means it has none.");
			Assert.That(
				Vector2.Distance(new Vector2(anchor.X, anchor.Z), new Vector2(match.PositionOf(match.Nexus(slot)).X, match.PositionOf(match.Nexus(slot)).Z)),
				Is.LessThan(1f),
				$"Slot {slot} has to start its Nexus at the anchor the board declares.");
		}
	}

	[Test]
	public void TheRulesetSeatsNobodyOnABoardThatDeclaresNoAnchors()
	{
		using MeleeMatch match = MeleeMatch.StartOnMap(MeleeTestMaps.MeleeFolder, new[] { "Vale", "Sable" }, MeleeTestMaps.ScriptPath);

		Assert.That(match.Api.GetSpawnAnchorCount(), Is.Zero, "The ruleset holds no board of its own, so a board it is pointed at supplies the anchors.");
		Assert.That(match.IsAlive(match.Nexus(1)), Is.False, "A board without anchors must not be handed a Nexus the ruleset made up a position for.");
		Assert.That(match.IsAlive(match.Hero(1)), Is.False, "A hero arrives with the seat its board declares, so there is none here either.");
	}

	[Test]
	public void TheAuthoredResourceNodesAreOnTheBoard()
	{
		using MeleeMatch match = MeleeMatch.StartDuel();

		Assert.That(match.Api.ResourceNodeCount, Is.EqualTo(12), "The board places six gold and six wood nodes.");

		int goldNodes = 0;
		int woodNodes = 0;
		foreach (ResourceNodeId node in match.Api.GetResourceNodes())
		{
			string type = match.Api.GetResourceNodeType(node);
			if (type == "gold") goldNodes++;
			if (type == "wood") woodNodes++;
			Assert.That(match.Api.GetResourceNodeAmount(node), Is.GreaterThan(0f), "A spawned node must carry the amount it was spawned with.");
		}

		Assert.That(goldNodes, Is.EqualTo(6), "Every gold node the map places has to be registered as gold.");
		Assert.That(woodNodes, Is.EqualTo(6), "Every wood node the map places has to be registered as wood.");
	}

	[Test]
	public void TheAuthoredStartingUnitsCarryOnlyTheBuffsTheyEarned()
	{
		using MeleeMatch match = MeleeMatch.StartDuel();

		UnitId sable = match.Hero(2);
		Assert.That(match.Api.UnitHasBuff(sable, "buff/cinder_mark"), Is.True, "Sable has to carry the Cinder Mark buff from the first tick.");

		match.Step(1.2f);

		UnitId vale = match.Hero(1);
		Assert.That(match.Api.UnitHasBuff(vale, "buff/bulwark"), Is.True, "Every hero carries the bulwark as a buff once the match ticks.");
		Assert.That(match.TryGet(vale, out Buffs valeBuffs), Is.True, "Vale's bulwark has to be listed with its other status effects.");
		Assert.That(valeBuffs.Count, Is.EqualTo(1), "Vale's innate is a stat modifier, so its bulwark is the only buff it carries.");
	}
}
