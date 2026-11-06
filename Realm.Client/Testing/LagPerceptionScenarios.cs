using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Realm.Client.Testing;

public static class LagPerceptionScenarios
{
	public const int PlayableSeatCount = 12;

	public const int LocalClientCount = 1;

	public const int BotSeatCount = PlayableSeatCount - LocalClientCount;

	public const int SeededPacketLossPercent = 25;

	public const int HighLatencyRoundTripMilliseconds = 250;

	public const int SeededLinkSeed = 20261023;

	/// <summary>
	///     The scenarios this harness measures, each with the perception ceilings its link allows. The ceilings sit above
	///     what the scenarios measured when the link was last characterised, not at it: a link paying a quarter of its
	///     datagrams has a worst case that moves with where the loss falls, and a budget inside that spread would fail on
	///     its own noise instead of on a regression. They are tight enough to have caught every state this harness was
	///     written to find, which is what they are for.
	/// </summary>
	public static readonly LagScenario[] All =
	{
		new("clean", 0, 0)
		{
			MaximumPresentationDelayMilliseconds = 130,
			MaximumDrawnWorldAgeMilliseconds = 450,
			MaximumBaselines = 8,
			MaximumLongestFreezeMilliseconds = 900
		},
		new("loss25", 0, SeededPacketLossPercent)
		{
			MaximumPresentationDelayMilliseconds = 220,
			MaximumDrawnWorldAgeMilliseconds = 550,
			MaximumBaselines = 10,
			MaximumLongestFreezeMilliseconds = 900
		},
		new("ping250", HighLatencyRoundTripMilliseconds, 0)
		{
			MaximumPresentationDelayMilliseconds = 340,
			MaximumDrawnWorldAgeMilliseconds = 720,
			MaximumBaselines = 10,
			MaximumLongestFreezeMilliseconds = 900
		},
		new("ping250_loss25", HighLatencyRoundTripMilliseconds, SeededPacketLossPercent)
		{
			MaximumPresentationDelayMilliseconds = 460,
			MaximumDrawnWorldAgeMilliseconds = 1150,
			MaximumBaselines = 34,
			MaximumLongestFreezeMilliseconds = 1800
		}
	};

	public static IReadOnlyList<LagScenario> ResolveScenarios(string? filter)
	{
		if (string.IsNullOrWhiteSpace(filter)) return All;

		List<LagScenario> selected = new();
		foreach (string name in filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			LagScenario? match = null;
			for (int index = 0; index < All.Length; index++)
			{
				if (!string.Equals(All[index].Name, name, StringComparison.OrdinalIgnoreCase)) continue;
				match = All[index];
				break;
			}

			if (match == null) throw new ArgumentException($"'{name}' is not a lag scenario; known scenarios are {string.Join(", ", Array.ConvertAll(All, scenario => scenario.Name))}.");
			selected.Add(match);
		}

		return selected;
	}

	public static async Task<string> RunAsync(
		string rootDirectory,
		int passes,
		IReadOnlyList<LagScenario> scenarios,
		float matchSeconds,
		string mapFolder,
		Action<string> log)
	{
		Directory.CreateDirectory(rootDirectory);
		string wasmPath = MultiplayerEcsSyncTestSupport.EnsureMapWasm(mapFolder);
		string godotBinary = MultiplayerEcsSyncTestSupport.ResolveGodotBinary();
		string projectDirectory = MultiplayerEcsSyncTestSupport.ResolveClientProjectDirectory();

		log($"lag perception matrix: {scenarios.Count} scenario(s) x {passes} pass(es) of {matchSeconds:F0}s of match time on '{mapFolder}'");
		log($"  {PlayableSeatCount} playable seats: {LocalClientCount} local client over loopback, {BotSeatCount} AI, every tile unlocked");

		for (int pass = 1; pass <= passes; pass++)
		{
			foreach (LagScenario scenario in scenarios)
			{
				string scenarioDirectory = LagPerceptionReport.ScenarioDirectory(rootDirectory, pass, scenario);
				MultiplayerEcsRunSettings settings = new()
				{
					GodotBinary = godotBinary,
					ProjectDirectory = projectDirectory,
					MapFolder = mapFolder,
					WasmPath = wasmPath,
					OutputDirectory = scenarioDirectory,
					MetricsDirectory = scenarioDirectory,
					DurationSeconds = matchSeconds,
					Port = FindAvailableUdpPort(),
					Profile = scenario.PacketLossPercent > 0 ? NetworkProfile.LatencyLoss : scenario.AddedRoundTripMilliseconds > 0 ? NetworkProfile.Latency : NetworkProfile.Clean,
					PingMilliseconds = scenario.AddedRoundTripMilliseconds,
					PacketLossPercent = scenario.PacketLossPercent,
					RelaySeed = SeededLinkSeed,
					PeerCount = LocalClientCount,
					BotCount = BotSeatCount,
					CheatsEnabled = true,
					UnlockTiles = true
				};

				log($"[pass {pass}] {scenario.Name}: {scenario.Describe()} into '{scenarioDirectory}'");
				DateTime startedAt = DateTime.UtcNow;
				await MultiplayerEcsSyncTestSupport.RunAsync(settings);
				MultiplayerEcsSyncReport syncReport = MultiplayerEcsSyncTestSupport.Verify(settings, Path.Combine(scenarioDirectory, "host"));
				File.WriteAllText(Path.Combine(scenarioDirectory, "multiplayer_ecs_sync_report.txt"), syncReport.Render());
				log($"[pass {pass}] {scenario.Name} finished in {(DateTime.UtcNow - startedAt).TotalSeconds:F0}s with {syncReport.Failures.Count} sync verification failure(s)");

				foreach (string failure in syncReport.Failures) log($"  sync failure: {failure}");
			}

			LagPerceptionReport.WritePassReport(rootDirectory, pass, scenarios);
		}

		LagPerceptionReport.WriteComparisonReport(rootDirectory, passes, scenarios);
		return Path.Combine(rootDirectory, "lag_comparison.txt");
	}

	private static int FindAvailableUdpPort()
	{
		for (int attempt = 0; attempt < 40; attempt++)
		{
			int port = Random.Shared.Next(20000, 30000);
			try
			{
				using System.Net.Sockets.UdpClient probe = new(port);
				return port;
			}
			catch (System.Net.Sockets.SocketException)
			{
			}
		}

		throw new InvalidOperationException("No free UDP port could be reserved for the lag perception harness.");
	}
}
