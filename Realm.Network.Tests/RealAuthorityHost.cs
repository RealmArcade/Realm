using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Realm.Network.Tests;

/// <summary>
///     Brings up the authority this game actually ships, headless, holding a lobby on a port of its choosing, so the
///     reachability check is measured against real ENet rather than against a stand-in. It is skipped, with the reason
///     stated, on a machine that has no Godot, no authority project or no map to open.
/// </summary>
public class RealAuthorityHost : IAsyncDisposable, IDisposable
{
	private const string ReadyLine = "REALM_RESIDENT_AUTHORITY_READY";
	private const string ReplyPrefix = "REALM_CONTROL_REPLY ";
	private const string CommandPrefix = "REALM_CONTROL ";

	private readonly Process _process;
	private readonly BlockingCollection<string> _lines = new();
	private readonly string _logPath;

	private RealAuthorityHost(Process process, int port, string logPath)
	{
		_process = process;
		Port = port;
		_logPath = logPath;
	}

	public int Port { get; }

	public string AuthorityLogPath => _logPath;

	public static string? TryResolveGodotBinary()
	{
		string fromEnvironment = Environment.GetEnvironmentVariable("GODOT_BIN") ?? string.Empty;
		if (!string.IsNullOrEmpty(fromEnvironment) && File.Exists(fromEnvironment)) return fromEnvironment;

		const string windowsFallback = @"C:\Program Files\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64.exe";
		return File.Exists(windowsFallback) ? windowsFallback : null;
	}

	public static string? TryResolveAuthorityProject()
	{
		DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
		for (int level = 0; current != null && level < 8; level++, current = current.Parent)
		{
			string candidate = Path.Combine(current.FullName, "Realm.ListenServerHost");
			if (File.Exists(Path.Combine(candidate, "project.godot"))) return candidate;
		}

		return null;
	}

	public static string? TryResolveMapFolder()
	{
		string fromEnvironment = Environment.GetEnvironmentVariable("REALM_TEST_MAP_LOBBY") ?? string.Empty;
		var candidates = new[]
		{
			fromEnvironment,
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Realm", "Untitled Map"),
			@"D:\git\Realm\ugc-ChaosArena",
			@"c:\temp\ugc-ChaosArena"
		};

		foreach (string candidate in candidates)
		{
			if (string.IsNullOrWhiteSpace(candidate)) continue;
			if (File.Exists(Path.Combine(candidate, "metadata.json")) && File.Exists(Path.Combine(candidate, "terrain.json"))) return candidate;
		}

		return null;
	}

	public static void IgnoreWhenUnavailable()
	{
		string? godot = TryResolveGodotBinary();
		if (godot == null) Assert.Ignore("the Godot binary is not configured in GODOT_BIN, so the real authority cannot be brought up");

		string? project = TryResolveAuthorityProject();
		if (project == null) Assert.Ignore("the Realm.ListenServerHost project was not found beside this build");

		string? map = TryResolveMapFolder();
		if (map == null) Assert.Ignore("no map folder with metadata.json and terrain.json was found; set REALM_TEST_MAP_LOBBY");
	}

	public static async Task<RealAuthorityHost> StartLobbyAsync(int port, bool loopbackOnly = false)
	{
		string godot = TryResolveGodotBinary() ?? throw new InvalidOperationException("the Godot binary is not configured");
		string project = TryResolveAuthorityProject() ?? throw new InvalidOperationException("the authority project was not found");
		string mapFolder = TryResolveMapFolder() ?? throw new InvalidOperationException("no map folder was found");

		string logDirectory = Path.Combine(Path.GetTempPath(), "Realm_Reachability_Tests");
		Directory.CreateDirectory(logDirectory);
		string authorityLog = Path.Combine(logDirectory, $"authority_{port}_{Environment.ProcessId}.log");

		var startInfo = new ProcessStartInfo(godot)
		{
			UseShellExecute = false,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};

		startInfo.ArgumentList.Add("--headless");
		startInfo.ArgumentList.Add("--path");
		startInfo.ArgumentList.Add(project);
		startInfo.ArgumentList.Add("--");
		startInfo.ArgumentList.Add("--log");
		startInfo.ArgumentList.Add(authorityLog);

		Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("the authority did not start");
		var host = new RealAuthorityHost(process, port, authorityLog);

		process.OutputDataReceived += (_, args) =>
		{
			if (args.Data != null) host._lines.Add(args.Data);
		};
		process.ErrorDataReceived += (_, args) =>
		{
			if (args.Data != null) host._lines.Add(args.Data);
		};
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();

		await host.WaitForReadyAsync();
		await host.StartMatchAsync(mapFolder, port, loopbackOnly);
		return host;
	}

	private async Task WaitForReadyAsync()
	{
		DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);

		while (DateTime.UtcNow < deadline)
		{
			if (_process.HasExited) throw new InvalidOperationException($"the authority exited before it was ready{DescribeLog()}");
			if (_lines.TryTake(out string? line, TimeSpan.FromMilliseconds(100)) && line.Contains(ReadyLine, StringComparison.Ordinal)) return;
		}

		throw new InvalidOperationException($"the authority did not report that it was up{DescribeLog()}");
	}

	private async Task StartMatchAsync(string mapFolder, int port, bool loopbackOnly)
	{
		var command = new
		{
			RequestId = 1,
			Kind = "StartMatch",
			MapFolderPath = mapFolder,
			Port = port,
			Seed = 4242,
			Cheats = true,
			Lobby = true,
			LoopbackOnly = loopbackOnly,
			AdminToken = "reachability-tests",
			MapName = Path.GetFileName(mapFolder),
			MaxPlayers = 4
		};

		await _process.StandardInput.WriteLineAsync(CommandPrefix + JsonSerializer.Serialize(command, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
		await _process.StandardInput.FlushAsync();

		DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(180);
		while (DateTime.UtcNow < deadline)
		{
			if (_process.HasExited) throw new InvalidOperationException($"the authority exited before it held a lobby{DescribeLog()}");
			if (!_lines.TryTake(out string? line, TimeSpan.FromMilliseconds(200)) || line == null) continue;
			if (!line.StartsWith(ReplyPrefix, StringComparison.Ordinal)) continue;

			using var reply = JsonDocument.Parse(line[ReplyPrefix.Length..]);
			if (reply.RootElement.TryGetProperty("accepted", out JsonElement accepted) && accepted.GetBoolean()) return;

			string failure = reply.RootElement.TryGetProperty("failure", out JsonElement failureElement) ? failureElement.GetString() ?? string.Empty : string.Empty;
			throw new InvalidOperationException($"the authority refused to hold a lobby: {failure}{DescribeLog()}");
		}

		throw new InvalidOperationException($"the authority never answered the start command{DescribeLog()}");
	}

	private string DescribeLog()
	{
		try
		{
			return File.Exists(AuthorityLogPath) ? $"\n{File.ReadAllText(AuthorityLogPath)}" : string.Empty;
		}
		catch (IOException)
		{
			return string.Empty;
		}
	}

	public void Dispose()
	{
		try
		{
			if (!_process.HasExited)
			{
				_process.StandardInput.Close();
				if (!_process.WaitForExit(10_000)) _process.Kill(true);
			}
		}
		catch (Exception exception) when (exception is InvalidOperationException or IOException)
		{
		}

		_process.Dispose();
		_lines.Dispose();
	}

	public ValueTask DisposeAsync()
	{
		Dispose();
		return ValueTask.CompletedTask;
	}
}
