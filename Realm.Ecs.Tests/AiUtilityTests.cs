using NUnit.Framework;
using Realm.Ecs.AI.Affordances;
using Realm.Ecs.AI.Balancer;
using Realm.Ecs.AI.Genres;
using Realm.Ecs.AI.Policy;
using Realm.Ecs.AI.Simulation;
using Realm.Ecs.AI.Training;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Realm.Ecs.Tests;

[TestFixture]
public class AiUtilityTests
{
	[Test]
	public void TestAffordanceScannerGeneratesAffordances()
	{
		var runner = new HeadlessSimulationRunner();
		var scanner = new AffordanceScanner();

		var affordances = scanner.ScanAffordances(runner.World, 0);

		Assert.That(affordances, Is.Not.Null);
		Assert.That(affordances.Count, Is.GreaterThan(0), "Scanner should discover legal candidate affordances for Player 0");

		var first = affordances[0];
		Assert.That(first.FeatureVector, Is.Not.Null);
		Assert.That(first.FeatureVector.Length, Is.EqualTo(AffordanceScanner.FeatureCount));
	}

	[Test]
	public void TestLinearUtilityPolicySelection()
	{
		var policy = new LinearUtilityPolicy();
		var affordances = new List<GenericAffordance>
		{
			new GenericAffordance(Arch.Core.Entity.Null, CommandIntent.MoveTo, Arch.Core.Entity.Null, System.Numerics.Vector3.Zero, "move1", new float[] { 0.1f, 0.2f, 0.1f, 0.5f, 0.2f, 0.0f, 0.1f, 0.1f }),
			new GenericAffordance(Arch.Core.Entity.Null, CommandIntent.Attack, Arch.Core.Entity.Null, System.Numerics.Vector3.Zero, "attack1", new float[] { 0.0f, 0.9f, 0.8f, 1.0f, 0.9f, 0.0f, 0.1f, 0.8f })
		};

		var weights = new float[] { -0.5f, 1.0f, 0.5f, 0.5f, 0.5f, 0.5f, -0.5f, 0.5f };

		var bestAction = policy.SelectAction(affordances, weights);

		Assert.That(bestAction.HasValue, Is.True);
		Assert.That(bestAction!.Value.PayloadId, Is.EqualTo("attack1"), "Policy should pick the highest scoring affordance");
	}

	[Test]
	public void TestHeadlessSimulationRunnerRunsMatch()
	{
		var runner = new HeadlessSimulationRunner();
		var p0Weights = new float[] { -0.5f, 0.8f, 0.2f, 0.5f, 0.7f, 0.9f, 0.4f, 0.6f };
		var p1Weights = new float[] { -0.5f, 0.8f, 0.2f, 0.5f, 0.7f, 0.9f, 0.4f, 0.6f };

		var result = runner.RunMatch(p0Weights, p1Weights, maxTicks: 200);

		Assert.That(result, Is.Not.Null);
		Assert.That(result.TotalTicksExecuted, Is.GreaterThan(0), "Simulation match should execute ticks");
	}

	[Test]
	public void TestSelfPlayTrainerGeneratesAndSerializesProfile()
	{
		var trainer = new SelfPlayTrainer();
		var profile = trainer.TrainSelfPlay("TestMap", generations: 2, populationSize: 4, matchesPerEvaluation: 2);

		Assert.That(profile, Is.Not.Null);
		Assert.That(profile.MapName, Is.EqualTo("TestMap"));
		Assert.That(profile.Weights.Length, Is.EqualTo(AffordanceScanner.FeatureCount));
		Assert.That(profile.SchemaVersion, Is.EqualTo("1.0.0"));
		Assert.That(profile.ProfileId, Is.EqualTo("TestMap_AutoTrained"));

		string json = profile.ToJson();
		Assert.That(string.IsNullOrWhiteSpace(json), Is.False);

		var restored = BotProfile.FromJson(json);
		Assert.That(restored.MapName, Is.EqualTo(profile.MapName));
		Assert.That(restored.Weights.Length, Is.EqualTo(profile.Weights.Length));
		Assert.That(restored.SchemaVersion, Is.EqualTo(profile.SchemaVersion));
		Assert.That(restored.DecisionIntervalSeconds, Is.EqualTo(profile.DecisionIntervalSeconds));
		Assert.That(restored.AggressionMultiplier, Is.EqualTo(profile.AggressionMultiplier));
	}

	[Test]
	public void TestIndependentBotControllersMaintainSeparateState()
	{
		var profile0 = BotProfile.CreateDefault("MapA");
		profile0.DecisionIntervalSeconds = 0.5f;
		profile0.AggressionMultiplier = 1.5f;

		var profile1 = BotProfile.CreateDefault("MapB");
		profile1.DecisionIntervalSeconds = 2.0f;
		profile1.AggressionMultiplier = 0.5f;

		var bot0 = new Realm.Ecs.AI.BotController(0, profile0);
		var bot1 = new Realm.Ecs.AI.BotController(1, profile1);

		Assert.That(bot0.PlayerIndex, Is.EqualTo(0));
		Assert.That(bot1.PlayerIndex, Is.EqualTo(1));
		Assert.That(bot0.DecisionInterval, Is.EqualTo(0.5f));
		Assert.That(bot1.DecisionInterval, Is.EqualTo(2.0f));
		Assert.That(bot0.AggressionMultiplier, Is.EqualTo(1.5f));
		Assert.That(bot1.AggressionMultiplier, Is.EqualTo(0.5f));

		var runner = new HeadlessSimulationRunner();
		bot0.Tick(runner.World, 0, 0.6f);
		bot1.Tick(runner.World, 1, 0.6f);

		Assert.That(bot0.Profile.MapName, Is.EqualTo("MapA"));
		Assert.That(bot1.Profile.MapName, Is.EqualTo("MapB"));
	}

	[Test]
	public void TestHeadlessSimulationRunnerCustomWinConditionAndInitializer()
	{
		var runner = new HeadlessSimulationRunner();
		var p0Weights = new float[] { -0.5f, 0.8f, 0.2f, 0.5f, 0.7f, 0.9f, 0.4f, 0.6f };
		var p1Weights = new float[] { -0.5f, 0.8f, 0.2f, 0.5f, 0.7f, 0.9f, 0.4f, 0.6f };

		bool customInitCalled = false;
		MapSimulationInitializer initializer = (world) =>
		{
			customInitCalled = true;
			world.Create(
				new Realm.Ecs.Components.Tags.Player(),
				new Realm.Ecs.Components.Core.UnitOwnerPlayer(0)
			);
			world.Create(
				new Realm.Ecs.Components.Tags.Player(),
				new Realm.Ecs.Components.Core.UnitOwnerPlayer(1)
			);
		};

		WinConditionEvaluator customWinCondition = (world, duration) =>
		{
			if (duration >= 0.3f)
			{
				return 0;
			}
			return null;
		};

		var result = runner.RunMatch(
			p0Weights,
			p1Weights,
			maxTicks: 50,
			fixedDelta: 0.1f,
			winConditionEvaluator: customWinCondition,
			customInitializer: initializer
		);

		Assert.That(customInitCalled, Is.True);
		Assert.That(result, Is.Not.Null);
		Assert.That(result.WinnerPlayerIndex, Is.EqualTo(0));
		Assert.That(result.TotalTicksExecuted, Is.EqualTo(3));
	}

	[Test]
	public void TestSelfPlayTrainerWithCustomWinCondition()
	{
		var trainer = new SelfPlayTrainer();
		WinConditionEvaluator customWin = (world, duration) => duration >= 0.2f ? 0 : null;

		var profile = trainer.TrainSelfPlay(
			"CustomWinMap",
			generations: 2,
			populationSize: 4,
			matchesPerEvaluation: 2,
			winConditionEvaluator: customWin,
			maxTicks: 10
		);

		Assert.That(profile, Is.Not.Null);
		Assert.That(profile.MapName, Is.EqualTo("CustomWinMap"));
		Assert.That(profile.FitnessScore, Is.GreaterThanOrEqualTo(0.0f));
	}

	[Test]
	public void TestAffordanceScannerReusableBufferSupport()
	{
		var runner = new HeadlessSimulationRunner();
		var scanner = new AffordanceScanner();
		var destination = new List<GenericAffordance>();

		scanner.ScanAffordances(runner.World, 0, destination);

		Assert.That(destination.Count, Is.GreaterThan(0));
		int initialCount = destination.Count;

		scanner.ScanAffordances(runner.World, 0, destination);
		Assert.That(destination.Count, Is.EqualTo(initialCount));
	}

	[Test]
	public void TestStrategicAutoBalancerMutatesAndOutputsGenome()
	{
		var balancer = new StrategicAutoBalancer();
		var initialGenome = new BalanceGenome();
		var agentWeights = new float[] { -0.5f, 0.8f, 0.2f, 0.5f, 0.7f, 0.9f, 0.4f, 0.6f };

		var optimized = balancer.OptimizeBalance(initialGenome, agentWeights, generations: 2, matchesPerGeneration: 2);

		Assert.That(optimized, Is.Not.Null);
		Assert.That(optimized.Unit0Cost, Is.GreaterThan(0f));

		string json = optimized.ToJson();
		Assert.That(string.IsNullOrWhiteSpace(json), Is.False);

		var restored = BalanceGenome.FromJson(json);
		Assert.That(restored.Unit0Cost, Is.EqualTo(optimized.Unit0Cost));
	}

	[Test]
	public void TestTugOfWarGenreProviderGeneratesAffordances()
	{
		var runner = new HeadlessSimulationRunner();
		var provider = AiGenreRegistry.Get("tug_of_war");
		provider.ConfigureFromParameters(new Dictionary<string, string>
		{
			{ "BuildSpotsJson", System.Text.Json.JsonSerializer.Serialize(new List<Vector3> { new(0, 0, 0), new(10, 0, 10) }) },
			{ "IncomeUpgradeCost", "100" }
		});

		var affordances = new List<GenericAffordance>();
		provider.ScanAffordances(runner.World, 0, affordances);

		Assert.That(affordances.Count, Is.GreaterThan(0));
		Assert.That(affordances.Any(a => a.Intent == CommandIntent.Build), Is.True);
		Assert.That(affordances.Any(a => a.PayloadId == "upgrade_income"), Is.True);
	}

	[Test]
	public void TestHeroArenaGenreProviderGeneratesAffordances()
	{
		var runner = new HeadlessSimulationRunner();
		var provider = AiGenreRegistry.Get("hero_arena");
		provider.ConfigureFromParameters(new Dictionary<string, string>
		{
			{ "FountainPositionJson", System.Text.Json.JsonSerializer.Serialize(new Vector3(-50, 0, -50)) },
			{ "RetreatHealthPercent", "0.4" },
			{ "RunePositionsJson", System.Text.Json.JsonSerializer.Serialize(new List<Vector3> { new(0, 0, 0) }) },
			{ "ShopBuildOrderJson", System.Text.Json.JsonSerializer.Serialize(new List<string> { "boots_of_speed", "iron_blade" }) }
		});

		var affordances = new List<GenericAffordance>();
		provider.ScanAffordances(runner.World, 0, affordances);

		Assert.That(affordances.Count, Is.GreaterThan(0));
		Assert.That(affordances.Any(a => a.PayloadId.StartsWith("buy_item_") || a.PayloadId.StartsWith("grab_rune_") || a.PayloadId.StartsWith("harass_hero_")), Is.True);
	}
}
