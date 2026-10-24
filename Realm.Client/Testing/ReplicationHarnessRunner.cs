using System;
using System.IO;
using Godot;

namespace Realm.Client.Testing;

public partial class ReplicationHarnessRunner : SceneTree
{
	public override void _Initialize()
	{
		RunAsync();
	}

	private async void RunAsync()
	{
		try
		{
			string durationText = System.Environment.GetEnvironmentVariable("REALM_MULTIPLAYER_ECS_DURATION") ?? "12";
			float durationSeconds = float.Parse(durationText, System.Globalization.CultureInfo.InvariantCulture);
			string pingText = System.Environment.GetEnvironmentVariable("REALM_MULTIPLAYER_ECS_PING_MS") ?? "120";
			int pingMilliseconds = int.Parse(pingText, System.Globalization.CultureInfo.InvariantCulture);

			string outputDirectory = Path.Combine(Path.GetTempPath(), "Realm_MultiplayerEcsSync");
			MultiplayerEcsRunSettings settings = new()
			{
				GodotBinary = MultiplayerEcsSyncTestSupport.ResolveGodotBinary(),
				ProjectDirectory = ProjectSettings.GlobalizePath("res://"),
				MapFolder = @"c:\temp\ugc-ChaosArena",
				WasmPath = MultiplayerEcsSyncTestSupport.EnsureChaosArenaWasm(@"c:\temp\ugc-ChaosArena"),
				OutputDirectory = outputDirectory,
				DurationSeconds = durationSeconds,
				Port = 28417,
				PingMilliseconds = pingMilliseconds
			};

			await MultiplayerEcsSyncTestSupport.RunAsync(settings);
			MultiplayerEcsSyncReport report = MultiplayerEcsSyncTestSupport.Verify(settings);
			string rendered = report.Render();
			File.WriteAllText(Path.Combine(outputDirectory, "multiplayer_ecs_sync_report.txt"), rendered);
			GD.Print(rendered);
			Quit(report.Failures.Count > 0 ? 1 : 0);
		}
		catch (Exception exception)
		{
			GD.PrintErr(exception.ToString());
			Quit(2);
		}
	}
}
