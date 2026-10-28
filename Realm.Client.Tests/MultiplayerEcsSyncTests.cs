using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using GdUnit4;
using Realm.Client.Testing;

namespace Realm.Client.Tests;

/// <summary>
/// Runs the end-to-end sync harness: real Godot peers, a real authority process and a UDP relay that injects the lag,
/// so the checks measure the protocol rather than a simulated copy of it. Nothing here needs a Godot runtime host of
/// its own, which is what used to keep the harness from being discovered and executed by <c>dotnet test</c>.
/// </summary>
[TestSuite]
public class MultiplayerEcsSyncTests
{
	private const string ChaosArenaFolder = @"c:\temp\ugc-ChaosArena";
	private const int LaggedPingMilliseconds = 120;
	private const int BotSeatCount = 12;

	[TestCase]
	public async Task LaggedTwoPeerRunPairsWhatTheClientApplied()
	{
		MultiplayerEcsRunSettings settings = CreateSettings("Realm_MultiplayerEcsSync_Lagged", 25f);
		await MultiplayerEcsSyncTestSupport.RunAsync(settings);
		Verify(settings);
	}

	[TestCase]
	public async Task DiscardedSnapshotIsRepairedWithExactlyOneBaseline()
	{
		MultiplayerEcsRunSettings settings = new()
		{
			GodotBinary = MultiplayerEcsSyncTestSupport.ResolveGodotBinary(),
			ProjectDirectory = MultiplayerEcsSyncTestSupport.ResolveClientProjectDirectory(),
			MapFolder = ChaosArenaFolder,
			WasmPath = MultiplayerEcsSyncTestSupport.EnsureChaosArenaWasm(ChaosArenaFolder),
			OutputDirectory = Path.Combine(Path.GetTempPath(), "Realm_MultiplayerEcsSync_Repair"),
			DurationSeconds = 30f,
			Port = FindAvailableUdpPort(),
			PingMilliseconds = LaggedPingMilliseconds,
			PeerCount = 2,
			BotCount = BotSeatCount,
			DiscardSnapshotAtSeconds = 12f
		};

		await MultiplayerEcsSyncTestSupport.RunAsync(settings);

		MultiplayerEcsSyncReport report = new();
		MultiplayerEcsSyncTestSupport.VerifySnapshotDiscardRepair(settings, null, report);
		WriteReport(settings, report);
	}

	private static MultiplayerEcsRunSettings CreateSettings(string directoryName, float durationSeconds)
	{
		return new MultiplayerEcsRunSettings
		{
			GodotBinary = MultiplayerEcsSyncTestSupport.ResolveGodotBinary(),
			ProjectDirectory = MultiplayerEcsSyncTestSupport.ResolveClientProjectDirectory(),
			MapFolder = ChaosArenaFolder,
			WasmPath = MultiplayerEcsSyncTestSupport.EnsureChaosArenaWasm(ChaosArenaFolder),
			OutputDirectory = Path.Combine(Path.GetTempPath(), directoryName),
			DurationSeconds = durationSeconds,
			Port = FindAvailableUdpPort(),
			PingMilliseconds = LaggedPingMilliseconds,
			PeerCount = 2,
			BotCount = BotSeatCount
		};
	}

	private static void Verify(MultiplayerEcsRunSettings settings)
	{
		MultiplayerEcsSyncReport report = MultiplayerEcsSyncTestSupport.Verify(settings);
		WriteReport(settings, report);
	}

	private static void WriteReport(MultiplayerEcsRunSettings settings, MultiplayerEcsSyncReport report)
	{
		string renderedReport = report.Render();
		File.WriteAllText(Path.Combine(settings.OutputDirectory, "multiplayer_ecs_sync_report.txt"), renderedReport);
		if (report.Failures.Count > 0) throw new Exception(renderedReport);
	}

	private static int FindAvailableUdpPort()
	{
		for (int attempt = 0; attempt < 20; attempt++)
		{
			int port = Random.Shared.Next(20000, 30000);
			try
			{
				using UdpClient probe = new(port);
				return port;
			}
			catch (SocketException)
			{
			}
		}

		throw new InvalidOperationException("No free UDP port could be reserved for the multiplayer harness.");
	}
}
