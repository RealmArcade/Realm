using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using GdUnit4;
using Realm.Client.Testing;

namespace Realm.Client.Tests;

[TestSuite]
public class MultiplayerEcsSyncTests
{
	private static string ChaosArenaFolder => MultiplayerEcsSyncTestSupport.ResolveTestMapFolder(
		"REALM_TEST_MAP_CHAOS",
		@"D:\git\Realm\ugc-ChaosArena",
		@"c:\temp\ugc-ChaosArena");

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
	public async Task CleanTwoPeerRunKeepsBothClientsConverged()
	{
		MultiplayerEcsRunSettings settings = new()
		{
			GodotBinary = MultiplayerEcsSyncTestSupport.ResolveGodotBinary(),
			ProjectDirectory = MultiplayerEcsSyncTestSupport.ResolveClientProjectDirectory(),
			MapFolder = ChaosArenaFolder,
			WasmPath = MultiplayerEcsSyncTestSupport.EnsureMapWasm(ChaosArenaFolder),
			OutputDirectory = Path.Combine(Path.GetTempPath(), "Realm_MultiplayerEcsSync_Clean"),
			DurationSeconds = 20f,
			Port = FindAvailableUdpPort(),
			Profile = NetworkProfile.Clean,
			PeerCount = 2,
			BotCount = BotSeatCount
		};

		await MultiplayerEcsSyncTestSupport.RunAsync(settings);
		Verify(settings);
	}

	[TestCase]
	public async Task JitteredTwoPeerRunKeepsBothClientsConverged()
	{
		MultiplayerEcsRunSettings settings = new()
		{
			GodotBinary = MultiplayerEcsSyncTestSupport.ResolveGodotBinary(),
			ProjectDirectory = MultiplayerEcsSyncTestSupport.ResolveClientProjectDirectory(),
			MapFolder = ChaosArenaFolder,
			WasmPath = MultiplayerEcsSyncTestSupport.EnsureMapWasm(ChaosArenaFolder),
			OutputDirectory = Path.Combine(Path.GetTempPath(), "Realm_MultiplayerEcsSync_Jitter"),
			DurationSeconds = 25f,
			Port = FindAvailableUdpPort(),
			Profile = NetworkProfile.LatencyJitter,
			OneWayDelayMilliseconds = 40,
			JitterMilliseconds = 60,
			PeerCount = 2,
			BotCount = BotSeatCount
		};

		await MultiplayerEcsSyncTestSupport.RunAsync(settings);
		Verify(settings);
	}

	[TestCase]
	public async Task LossyTwoPeerRunKeepsBothClientsConverged()
	{
		MultiplayerEcsRunSettings settings = new()
		{
			GodotBinary = MultiplayerEcsSyncTestSupport.ResolveGodotBinary(),
			ProjectDirectory = MultiplayerEcsSyncTestSupport.ResolveClientProjectDirectory(),
			MapFolder = ChaosArenaFolder,
			WasmPath = MultiplayerEcsSyncTestSupport.EnsureMapWasm(ChaosArenaFolder),
			OutputDirectory = Path.Combine(Path.GetTempPath(), "Realm_MultiplayerEcsSync_Lossy"),
			DurationSeconds = 30f,
			Port = FindAvailableUdpPort(),
			Profile = NetworkProfile.LatencyLoss,
			OneWayDelayMilliseconds = 45,
			JitterMilliseconds = 15,
			PacketLossPercent = 3,
			PeerCount = 2,
			BotCount = BotSeatCount
		};

		await MultiplayerEcsSyncTestSupport.RunAsync(settings);
		Verify(settings);
	}

	[TestCase]
	public async Task DroppedItemsReachTheScreensThatCanSeeThem()
	{
		(string mapFolder, string wasmPath) = MultiplayerEcsSyncTestSupport.EnsureLootSpawningMap(ChaosArenaFolder);
		MultiplayerEcsRunSettings settings = new()
		{
			GodotBinary = MultiplayerEcsSyncTestSupport.ResolveGodotBinary(),
			ProjectDirectory = MultiplayerEcsSyncTestSupport.ResolveClientProjectDirectory(),
			MapFolder = mapFolder,
			WasmPath = wasmPath,
			OutputDirectory = Path.Combine(Path.GetTempPath(), "Realm_MultiplayerEcsSync_Loot"),
			DurationSeconds = 30f,
			Port = FindAvailableUdpPort(),
			PingMilliseconds = LaggedPingMilliseconds,
			PeerCount = 2,
			BotCount = BotSeatCount
		};

		await MultiplayerEcsSyncTestSupport.RunAsync(settings);

		MultiplayerEcsSyncReport report = MultiplayerEcsSyncTestSupport.Verify(settings);
		WriteReport(settings, report);
		if (report.ComparedLootCount == 0) throw new Exception($"The run dropped items but compared none against the authority.\n{report.Render()}");
	}

	[TestCase]
	public async Task SecondMapKeepsItsPeersConverged()
	{
		string mapFolder = MultiplayerEcsSyncTestSupport.ResolveTestMapFolder(
			"REALM_TEST_MAP_TOWER",
			@"D:\git\Realm\ugc-TowerDefense",
			@"c:\temp\ugc-TowerDefense");

		MultiplayerEcsRunSettings settings = new()
		{
			GodotBinary = MultiplayerEcsSyncTestSupport.ResolveGodotBinary(),
			ProjectDirectory = MultiplayerEcsSyncTestSupport.ResolveClientProjectDirectory(),
			MapFolder = mapFolder,
			WasmPath = MultiplayerEcsSyncTestSupport.EnsureMapWasm(mapFolder),
			OutputDirectory = Path.Combine(Path.GetTempPath(), "Realm_MultiplayerEcsSync_SecondMap"),
			DurationSeconds = 20f,
			Port = FindAvailableUdpPort(),
			PingMilliseconds = LaggedPingMilliseconds,
			PeerCount = 2,
			BotCount = BotSeatCount
		};

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
			WasmPath = MultiplayerEcsSyncTestSupport.EnsureMapWasm(ChaosArenaFolder),
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
			WasmPath = MultiplayerEcsSyncTestSupport.EnsureMapWasm(ChaosArenaFolder),
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
