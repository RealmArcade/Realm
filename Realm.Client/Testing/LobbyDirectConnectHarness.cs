using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Client.Services;

namespace Realm.Client.Testing;

/// <summary>
///     Hosts a lobby without the registry in one process and joins it by address in another, so the direct connect path is
///     measured as two screens and not as one screen talking to itself.
/// </summary>
[GlobalClass]
public partial class LobbyDirectConnectHarness : Node
{
	private string _role = "host";
	private string _mapFolder = string.Empty;
	private string _hostAddress = "127.0.0.1";
	private int _port;
	private string _inviteFile = string.Empty;
	private string _doneFile = string.Empty;
	private float _waitSeconds = 45f;
	private string _hostMode = "direct";
	private bool _deadRegistry;

	public override void _Ready()
	{
		_ = RunAsync();
	}

	private async Task RunAsync()
	{
		int exitCode = 1;
		string outcome = "the harness never ran";
		try
		{
			ParseArguments();
			GameContext.EnsureServices();
			(exitCode, outcome) = _role switch
			{
				"driver" => await RunDriverAsync(),
				"host" => await RunHostAsync(),
				"createcheck" => await RunCreateScreenCheckAsync(),
				"roomcheck" => await RunLobbyRoomCheckAsync(),
				_ => await RunGuestAsync()
			};
		}
		catch (Exception exception)
		{
			outcome = $"{exception.GetType().Name}: {exception.Message}";
			GameLog.LogError($"[DIRECT_HARNESS] {outcome}\n{exception}");
		}
		finally
		{
			GameLog.LogToConsole($"[DIRECT_HARNESS] RESULT role={_role} exit={exitCode}: {outcome}");
			if (_doneFile.Length > 0) File.WriteAllText(_doneFile, $"{exitCode}\n{outcome}\n");
			GetTree().Quit(exitCode);
		}
	}

	private void ParseArguments()
	{
		foreach (string argument in OS.GetCmdlineUserArgs())
		{
			int separatorIndex = argument.IndexOf('=');
			if (separatorIndex <= 0) continue;

			string key = argument[..separatorIndex];
			string value = argument[(separatorIndex + 1)..];
			switch (key)
			{
				case "--role":
					_role = value;
					break;
				case "--map":
					_mapFolder = value;
					break;
				case "--host":
					_hostAddress = value;
					break;
				case "--port":
					_port = int.Parse(value, CultureInfo.InvariantCulture);
					break;
				case "--invite-file":
					_inviteFile = value;
					break;
				case "--done-file":
					_doneFile = value;
					break;
				case "--wait-seconds":
					_waitSeconds = float.Parse(value, CultureInfo.InvariantCulture);
					break;
				case "--mode":
					_hostMode = value;
					break;
				case "--dead-registry":
					_deadRegistry = value == "1";
					break;
			}
		}

		if (_role is "host" or "roomcheck" or "driver" && string.IsNullOrEmpty(_mapFolder)) throw new InvalidOperationException("--map is required to host.");
	}

	private async Task<(int ExitCode, string Outcome)> RunHostAsync()
	{
		LobbyManager lobby = GameContext.Lobby ?? throw new InvalidOperationException("The LobbyManager autoload is unavailable.");
		string mapName = Path.GetFileName(Path.TrimEndingDirectorySeparator(_mapFolder));

		if (_deadRegistry) PointTheRegistryAtAnUnreachableServer(lobby);

		bool opened = _hostMode switch
		{
			"listed" => await lobby.HostLobbyAsync(_mapFolder, mapName, "1.0.0"),
			"prepare" => await lobby.PrepareHostedLobbyAsync(_mapFolder, mapName, "1.0.0"),
			_ => await lobby.HostDirectLobbyAsync(_mapFolder, mapName, "1.0.0")
		};
		if (!opened) return (2, $"hosting failed: {lobby.LastHostError ?? "no reason was recorded"}");

		_port = lobby.LobbyPort;
		if (_inviteFile.Length > 0) File.WriteAllText(_inviteFile, _port.ToString(CultureInfo.InvariantCulture));
		GameLog.LogToConsole($"[DIRECT_HARNESS] mode={_hostMode} direct={lobby.IsDirectLobby} address={lobby.DirectConnectAddress} public={lobby.PublicConnectAddress ?? "none"}; waiting for a direct guest.");

		bool seated = await WaitUntilAsync(() => lobby.PlayerList.Exists(player => !player.IsHost && !player.IsComputer), _waitSeconds);
		if (!seated) return (3, $"no guest arrived on port {_port} within {_waitSeconds} s; seats {lobby.PlayerList.Count}");

		return (0, $"the lobby on port {_port} seated {lobby.PlayerList.Count} player(s)");
	}

	/// <summary>
	///     Runs every scenario the direct connect path has to survive, one child process per screen, and states which of them
	///     held. The first scenario reproduces the failure this harness was written for: a host whose lobby server cannot be
	///     reached cannot open a listed lobby. The second opens the same lobby directly, against the same unreachable server,
	///     and joins it by address. The last two read the two screens a host and a joining player meet.
	/// </summary>
	/// <returns>The exit code and the line to report.</returns>
	private async Task<(int ExitCode, string Outcome)> RunDriverAsync()
	{
		if (_mapFolder.Length == 0) return (30, "--map is required to drive the harness");

		string outputDirectory = Path.Combine(Path.GetTempPath(), "realm-direct-connect-harness");
		Directory.CreateDirectory(outputDirectory);
		foreach (string stale in Directory.GetFiles(outputDirectory)) File.Delete(stale);

		string projectDirectory = ProjectSettings.GlobalizePath("res://");
		var failures = new List<string>();
		var notes = new List<string>();

		string listedDone = Path.Combine(outputDirectory, "listed-host.done");
		await RunChildToCompletionAsync(projectDirectory, ["--role=host", "--mode=listed", "--dead-registry=1", $"--map={_mapFolder}", $"--done-file={listedDone}", "--wait-seconds=10"], "host", 240);
		(int listedExit, string listedOutcome) = ReadDoneFile(listedDone);
		if (listedExit != 2) failures.Add($"a listed lobby opened against an unreachable registry (exit {listedExit}: {listedOutcome})");
		else notes.Add("a listed lobby is refused when the registry cannot be reached");

		string inviteFile = Path.Combine(outputDirectory, "direct-host.invite");
		string directHostDone = Path.Combine(outputDirectory, "direct-host.done");
		string guestDone = Path.Combine(outputDirectory, "guest.done");
		Process directHost = StartChild(projectDirectory, ["--role=host", "--mode=direct", "--dead-registry=1", $"--map={_mapFolder}", $"--invite-file={inviteFile}", $"--done-file={directHostDone}", "--wait-seconds=90"], "0");
		bool listening = await WaitForFileAsync(inviteFile, 180);
		if (!listening)
		{
			failures.Add("the direct lobby never named a port");
		}
		else
		{
			await RunChildToCompletionAsync(projectDirectory, ["--role=guest", "--host=127.0.0.1", $"--invite-file={inviteFile}", $"--done-file={guestDone}", "--wait-seconds=30"], "guest", 240);
			(int guestExit, string guestOutcome) = ReadDoneFile(guestDone);
			if (guestExit != 0) failures.Add($"a player could not join the direct lobby (exit {guestExit}: {guestOutcome})");
			else notes.Add(guestOutcome);
		}

		await WaitForExitAsync(directHost, 180);
		(int directHostExit, string directHostOutcome) = ReadDoneFile(directHostDone);
		if (directHostExit != 0) failures.Add($"the direct lobby did not stay open (exit {directHostExit}: {directHostOutcome})");
		else notes.Add(directHostOutcome);

		string createDone = Path.Combine(outputDirectory, "create-screen.done");
		await RunChildToCompletionAsync(projectDirectory, ["--role=createcheck", $"--done-file={createDone}"], "1", 180);
		(int createExit, string createOutcome) = ReadDoneFile(createDone);
		if (createExit != 0) failures.Add($"the create screen check failed (exit {createExit}: {createOutcome})");
		else notes.Add(createOutcome);

		string roomDone = Path.Combine(outputDirectory, "lobby-room.done");
		await RunChildToCompletionAsync(projectDirectory, ["--role=roomcheck", $"--map={_mapFolder}", $"--done-file={roomDone}"], "1", 240);
		(int roomExit, string roomOutcome) = ReadDoneFile(roomDone);
		if (roomExit != 0) failures.Add($"the lobby room check failed (exit {roomExit}: {roomOutcome})");
		else notes.Add(roomOutcome);

		var report = new StringBuilder();
		for (int index = 0; index < notes.Count; index++) report.AppendLine($"PASS {notes[index]}");
		for (int index = 0; index < failures.Count; index++) report.AppendLine($"FAIL {failures[index]}");
		if (failures.Count > 0) return (1, report.ToString().TrimEnd());

		return (0, report.ToString().TrimEnd());
	}

	private static Process StartChild(string projectDirectory, List<string> harnessArguments, string joinOnly)
	{
		var startInfo = new ProcessStartInfo(OS.GetExecutablePath())
		{
			WorkingDirectory = projectDirectory,
			UseShellExecute = false
		};
		startInfo.ArgumentList.Add("--headless");
		startInfo.ArgumentList.Add("--path");
		startInfo.ArgumentList.Add(projectDirectory);
		startInfo.ArgumentList.Add("res://Testing/LobbyDirectConnectHarness.tscn");
		startInfo.ArgumentList.Add("--");
		foreach (string argument in harnessArguments) startInfo.ArgumentList.Add(argument);
		startInfo.Environment["REALM_HARNESS_JOIN_ONLY"] = joinOnly;

		return Process.Start(startInfo) ?? throw new InvalidOperationException("A harness screen could not be started.");
	}

	private static async Task RunChildToCompletionAsync(string projectDirectory, List<string> harnessArguments, string joinOnly, int timeoutSeconds)
	{
		Process child = StartChild(projectDirectory, harnessArguments, joinOnly);
		await WaitForExitAsync(child, timeoutSeconds);
	}

	private static async Task WaitForExitAsync(Process process, int timeoutSeconds)
	{
		int remainingMilliseconds = timeoutSeconds * 1000;
		while (!process.HasExited && remainingMilliseconds > 0)
		{
			await Task.Delay(100);
			remainingMilliseconds -= 100;
		}

		if (process.HasExited) return;

		GameLog.LogError($"[DIRECT_HARNESS] A screen did not finish within {timeoutSeconds} s and is being ended.");
		process.Kill();
		process.WaitForExit(5000);
	}

	private static async Task<bool> WaitForFileAsync(string path, int timeoutSeconds)
	{
		int remainingMilliseconds = timeoutSeconds * 1000;
		while (remainingMilliseconds > 0)
		{
			if (File.Exists(path)) return true;

			await Task.Delay(100);
			remainingMilliseconds -= 100;
		}

		return File.Exists(path);
	}

	private static (int ExitCode, string Outcome) ReadDoneFile(string path)
	{
		if (!File.Exists(path)) return (-1, "the screen never wrote its result");

		string[] lines = File.ReadAllLines(path);
		if (lines.Length == 0 || !int.TryParse(lines[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int exitCode)) return (-1, "the screen wrote a result this run cannot read");

		return (exitCode, lines.Length > 1 ? lines[1] : string.Empty);
	}

	private async Task<(int ExitCode, string Outcome)> RunCreateScreenCheckAsync()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		var screen = GD.Load<PackedScene>("res://UI/LobbyCreate.tscn").Instantiate();
		screen.Name = "LobbyCreateHarnessScreen";
		GetTree().Root.AddChild(screen);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		Control? visibility = screen.GetNodeOrNull<Control>("CentralPanel/ContentContainer/VisibilityContainer");
		OptionButton? selector = visibility?.GetNodeOrNull<OptionButton>("VisibilitySelectButton");
		if (visibility == null || selector == null) return (10, "the create screen draws no lobby type selector");
		if (!visibility.Visible) return (11, "the lobby type selector is hidden on the create screen");
		if (selector.ItemCount != 2) return (12, $"the lobby type selector offers {selector.ItemCount} choice(s)");

		string listed = selector.GetItemText(0);
		string direct = selector.GetItemText(1);
		if (!listed.Contains("PUBLIC", StringComparison.Ordinal) || !direct.Contains("DIRECT", StringComparison.Ordinal))
		{
			return (13, $"the lobby type choices do not name both kinds: '{listed}' / '{direct}'");
		}

		screen.QueueFree();
		return (0, $"the create screen offers '{listed}' and '{direct}'");
	}

	private async Task<(int ExitCode, string Outcome)> RunLobbyRoomCheckAsync()
	{
		LobbyManager lobby = GameContext.Lobby ?? throw new InvalidOperationException("The LobbyManager autoload is unavailable.");
		string mapName = Path.GetFileName(Path.TrimEndingDirectorySeparator(_mapFolder));
		if (!await lobby.HostDirectLobbyAsync(_mapFolder, mapName, "1.0.0")) return (20, $"the direct lobby could not be opened: {lobby.LastHostError} ");

		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		var room = GD.Load<PackedScene>("res://UI/LobbyRoom.tscn").Instantiate();
		room.Name = "LobbyRoomHarnessScreen";
		GetTree().Root.AddChild(room);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		Control? panel = room.GetNodeOrNull<Control>("BriefingPanel/VBoxContainer/DirectConnectPanel_PanelContainer");
		if (panel == null) return (21, "the lobby room draws no direct connect card");

		string address = lobby.DirectConnectAddress;
		bool addressShown = false;
		foreach (Node node in panel.FindChildren("LanAddressLabel", string.Empty, true, false))
		{
			if (node is Label label && label.Text == address) addressShown = true;
		}

		if (!addressShown) return (22, $"the direct connect card does not show '{address}'");

		// A host has to be able to tell the two apart: an address a router forward was measured for works anywhere, and
		// one that was not is only reachable on this network. The card says which of those it is selling.
		if (lobby.PublicConnectAddress is { Length: > 0 } publicAddress)
		{
			bool publicShown = false;
			foreach (Node node in panel.FindChildren("LanAddressPublicLabel", string.Empty, true, false))
			{
				if (node is Label label && label.Text == publicAddress) publicShown = true;
			}

			if (!publicShown) return (24, $"the direct connect card does not show the measured public address '{publicAddress}'");
		}
		else if (panel.GetNodeOrNull<Control>("DirectConnectVBox/LanAddressNoForwardHint_Label") == null)
		{
			return (24, "the direct connect card does not say that no router forward was found");
		}

		Button? inviteButton = room.GetNodeOrNull<Button>("InviteButton");
		if (inviteButton == null || !inviteButton.Text.Contains("SHARE ADDRESS", StringComparison.Ordinal))
		{
			return (23, $"the invite button reads '{inviteButton?.Text}' instead of sharing the address");
		}

		// What the host is handed has to stand on its own in a chat: the map being played, the address, and what the
		// friend does with it. A message that names only one of those is the two-step copy this replaced.
		if (panel.FindChild("CopyInviteMessage_Button", true, false) == null)
		{
			return (25, "the direct connect card offers no ready-to-paste invite message");
		}

		string inviteMessage = lobby.DirectConnectInviteMessage;
		string inviteAddress = lobby.PublicConnectAddress ?? lobby.DirectConnectAddress;
		if (!inviteMessage.Contains(mapName, StringComparison.Ordinal)) return (25, $"the invite message does not name the map '{mapName}': {inviteMessage}");
		if (!inviteMessage.Contains(inviteAddress, StringComparison.Ordinal)) return (25, $"the invite message does not name the address '{inviteAddress}': {inviteMessage}");
		if (!inviteMessage.Contains("DIRECT CONNECT", StringComparison.Ordinal)) return (25, $"the invite message does not say where the address goes: {inviteMessage}");

		room.QueueFree();
		return (0, $"the lobby room shows '{address}' and copies an invite for {inviteAddress}");
	}

	private async Task<(int ExitCode, string Outcome)> RunGuestAsync()
	{
		LobbyManager lobby = GameContext.Lobby ?? throw new InvalidOperationException("The LobbyManager autoload is unavailable.");
		if (!await WaitForInviteFileAsync()) return (4, $"the host never wrote its port to '{_inviteFile}'");

		if (_port <= 0) return (5, "the host never named the port it listens on");

		bool joined = await lobby.JoinByIpAsync(_hostAddress, _port);
		if (!joined) return (6, $"joining {_hostAddress}:{_port} failed: {lobby.LastHostError ?? lobby.LobbyJoinError ?? "no reason was recorded"}");

		bool described = await WaitUntilAsync(() => lobby.HasLobbyState && lobby.LocalPeerId > 0, 20f);
		if (!described) return (7, $"the connection to {_hostAddress}:{_port} was accepted but the lobby never described a seat");

		return (0, $"joined {_hostAddress}:{_port} as peer {lobby.LocalPeerId} with {lobby.PlayerList.Count} seat(s)");
	}

	/// <summary>
	///     Makes the lobby server this screen would publish to answer nowhere, which is the network a host meets when the
	///     registry is down or blocked. The created-lobby path has to fail there and the direct path has to work, so both
	///     are measured against the same unreachable registry.
	/// </summary>
	/// <param name="lobby">The lobby manager whose registry list is replaced.</param>
	private static void PointTheRegistryAtAnUnreachableServer(LobbyManager lobby)
	{
		var servers = lobby.OfficialServers;
		servers.Clear();
		servers.Add("http://127.0.0.1:1");
		GameLog.LogToConsole($"[DIRECT_HARNESS] The registry is set to {servers[0]} for this run.");
	}

	private async Task<bool> WaitForInviteFileAsync()
	{
		if (_inviteFile.Length == 0) return _port > 0;

		ulong deadline = Time.GetTicksMsec() + (ulong)(_waitSeconds * 1000f);
		while (Time.GetTicksMsec() < deadline)
		{
			if (File.Exists(_inviteFile))
			{
				string text = File.ReadAllText(_inviteFile).Trim();
				if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) && port > 0)
				{
					_port = port;
					return true;
				}
			}

			await ToSignal(GetTree().CreateTimer(0.1f), SceneTreeTimer.SignalName.Timeout);
		}

		return false;
	}

	private async Task<bool> WaitUntilAsync(Func<bool> condition, float timeoutSeconds)
	{
		ulong deadline = Time.GetTicksMsec() + (ulong)(timeoutSeconds * 1000f);
		while (Time.GetTicksMsec() < deadline)
		{
			if (condition()) return true;
			await ToSignal(GetTree().CreateTimer(0.1f), SceneTreeTimer.SignalName.Timeout);
		}

		return condition();
	}
}
