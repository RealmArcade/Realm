using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GdUnit4;
using Realm.Client.Testing;

namespace Realm.Client.Tests;

[TestSuite]
public class LagPerceptionTests
{
	private static string ChaosArenaFolder => MultiplayerEcsSyncTestSupport.ResolveTestMapFolder(
		"REALM_TEST_MAP_CHAOS",
		@"D:\git\Realm\ugc-ChaosArena",
		@"c:\temp\ugc-ChaosArena");

	[TestCase]
	public async Task EveryNetworkedScenarioHoldsItsMetricsAcrossTwoPasses()
	{
		string rootDirectory = Environment.GetEnvironmentVariable("REALM_LAG_ROOT") ?? Path.Combine(Path.GetTempPath(), "Realm_LagPerception");
		int passes = ReadIntegerVariable("REALM_LAG_PASSES", 2);
		float matchSeconds = ReadFloatVariable("REALM_LAG_MATCH_SECONDS", 25f);
		IReadOnlyList<LagScenario> scenarios = LagPerceptionScenarios.ResolveScenarios(Environment.GetEnvironmentVariable("REALM_LAG_SCENARIOS"));

		await LagPerceptionScenarios.RunAsync(rootDirectory, passes, scenarios, matchSeconds, ChaosArenaFolder, Console.WriteLine);

		foreach (LagScenario scenario in scenarios)
		{
			Dictionary<string, double> metrics = LagPerceptionReport.ReadScenarioMetrics(LagPerceptionReport.ScenarioDirectory(rootDirectory, 1, scenario));
			if (metrics.Count == 0) throw new Exception($"The '{scenario.Name}' scenario wrote no lag metrics into '{rootDirectory}'.");

			if (!metrics.TryGetValue("client.stages.ClientPhysicsIntervalMilliseconds.p50", out double physicsInterval))
			{
				throw new Exception($"The '{scenario.Name}' scenario never sampled the client's physics interval, so the screen was not drawing a match.");
			}

			if (physicsInterval <= 0d) throw new Exception($"The '{scenario.Name}' scenario reported a physics interval of {physicsInterval:F3} ms.");
		}
	}

	private static int ReadIntegerVariable(string name, int fallback)
	{
		string? text = Environment.GetEnvironmentVariable(name);
		if (string.IsNullOrWhiteSpace(text)) return fallback;
		return int.Parse(text);
	}

	private static float ReadFloatVariable(string name, float fallback)
	{
		string? text = Environment.GetEnvironmentVariable(name);
		if (string.IsNullOrWhiteSpace(text)) return fallback;
		return float.Parse(text);
	}
}
