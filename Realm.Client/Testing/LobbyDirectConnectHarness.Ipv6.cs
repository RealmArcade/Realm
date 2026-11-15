using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Shared;
using Realm.Shared.Reachability;
using Environment = System.Environment;
using HttpClient = System.Net.Http.HttpClient;

namespace Realm.Client.Testing;

/// <summary>
///     Exercises the IPv6 half of hosting and joining as separate screens. The rig publishes a lobby whose only endpoint is
///     an IPv6 address, has a real registry node verify that endpoint from its own IPv6 socket, and then joins it: through
///     the lobby browser on a machine that holds a global IPv6 address, and by direct connect in every case, which is the
///     path an IPv6 address takes with or without a global address on this machine.
/// </summary>
public partial class LobbyDirectConnectHarness
{
	private string _liveRegistryUrl = string.Empty;

	private string _joinedLobbyId = string.Empty;

	/// <summary>
	///     Holds nothing but endpoints, so a rig that publishes a lobby of its own states them the way a host does.
	/// </summary>
	/// <param name="Address">The address the lobby is reachable at.</param>
	/// <param name="Port">The port the lobby listens on.</param>
	/// <param name="Family">The family of the address.</param>
	/// <param name="ProbeId">The check the registry node measured the endpoint with.</param>
	private sealed record RigEndpoint(string Address, int Port, string Family, string ProbeId);

	private sealed record RigRegisterRequest(
		string Map,
		int HostPort,
		string NatType,
		string ReportedHostIP,
		string PasswordHash,
		int MaxPlayers,
		int SlotsUsed,
		int HostPingBaseline,
		string GameVersion,
		string LocalIP,
		string MapVersion,
		IReadOnlyList<RigEndpoint> Endpoints,
		string ReachabilityProbeId);

	/// <summary>
	///     Runs the whole IPv6 rig: a registry node on the loopback IPv6 address, a host whose lobby is reachable only over
	///     IPv6, the registry node's own measurement of that endpoint, and the two ways a player reaches it.
	/// </summary>
	/// <returns>The exit code and the lines to report.</returns>
	private async Task<(int ExitCode, string Outcome)> RunIpv6DriverAsync()
	{
		if (_mapFolder.Length == 0) return (60, "--map is required to drive the IPv6 rig");
		if (!Socket.OSSupportsIPv6) return (61, "this machine has no IPv6 stack, so the IPv6 rig cannot run here");

		string outputDirectory = Path.Combine(Path.GetTempPath(), "realm-ipv6-harness");
		Directory.CreateDirectory(outputDirectory);
		foreach (string stale in Directory.GetFiles(outputDirectory)) File.Delete(stale);
		DeleteDirectory(Path.Combine(outputDirectory, "registry-data"));

		int registryPort = FreeIpv6TcpPort();
		int probePort = FreeIpv6UdpPort();
		string registryUrl = $"http://[::1]:{registryPort}";
		string projectDirectory = ProjectSettings.GlobalizePath("res://");

		string? registryDll = FindRegistryAssembly();
		if (registryDll == null) return (62, "the built registry node (Realm.AdminServer.dll) could not be found beside this screen");

		Process registry = StartRegistryNode(registryDll, registryPort, probePort, Path.Combine(outputDirectory, "registry-data"));
		try
		{
			return await RunIpv6RigAsync(registry, registryUrl, projectDirectory, outputDirectory);
		}
		finally
		{
			StopProcess(registry);
		}
	}

	/// <summary>
	///     Runs the rig against the registry node it started, which is stopped by the caller whether this returns or throws.
	/// </summary>
	/// <param name="registry">The registry node process.</param>
	/// <param name="registryUrl">The address the registry node answers on.</param>
	/// <param name="projectDirectory">The client project the screens are started from.</param>
	/// <param name="outputDirectory">Where this run writes the files it passes between screens.</param>
	/// <returns>The exit code and the lines to report.</returns>
	private async Task<(int ExitCode, string Outcome)> RunIpv6RigAsync(Process registry, string registryUrl, string projectDirectory, string outputDirectory)
	{
		var failures = new List<string>();
		var notes = new List<string>();

		if (!await WaitForRegistryAsync(registryUrl, 90))
		{
			return (63, $"the registry node never answered on {registryUrl}; see {Path.Combine(outputDirectory, "registry.log")}");
		}

		RegistryCapabilities? capabilities = await ReadCapabilitiesAsync(registryUrl);
		if (capabilities == null || !capabilities.CanCheck(LobbyAddressFamily.IPv6))
		{
			return (64, $"the registry node does not state that it can check IPv6 endpoints: {JsonSerializer.Serialize(capabilities)}");
		}

		notes.Add($"the registry node answers on {registryUrl} and states it can check {string.Join(" and ", capabilities.EndpointFamilies)} endpoints");

		string inviteFile = Path.Combine(outputDirectory, "host.invite");
		string hostDone = Path.Combine(outputDirectory, "host.done");
		Process host = StartChild(projectDirectory, ["--role=host", "--mode=direct", $"--map={_mapFolder}", $"--invite-file={inviteFile}", $"--done-file={hostDone}", "--wait-seconds=120"], "0");

		if (!await WaitForFileAsync(inviteFile, 180))
		{
			await WaitForExitAsync(host, 5);
			return (65, $"{string.Join("; ", failures)}the host never opened its lobby");
		}

		int hostPort = int.Parse(File.ReadAllText(inviteFile).Trim(), CultureInfo.InvariantCulture);
		notes.Add($"the host listens on UDP port {hostPort}, which its authority bound in both families");

		string? probeId = await MeasureEndpointAsync(registryUrl, "::1", hostPort);
		if (probeId == null)
		{
			failures.Add("the registry node never reached the host's game port over IPv6, so no lobby could be published over IPv6");
		}
		else
		{
			notes.Add($"the registry node reached {LobbyAddresses.Describe("::1", hostPort)} from its own IPv6 socket");

			string? lobbyId = await RegisterIpv6OnlyLobbyAsync(registryUrl, hostPort, probeId);
			if (lobbyId == null)
			{
				failures.Add("the registry node refused to publish a lobby whose only endpoint is IPv6");
			}
			else
			{
				notes.Add($"the registry node published an IPv6-only lobby as {lobbyId}");
				await CheckListedFamiliesAsync(registryUrl, lobbyId, notes, failures);

				// The lobby's own host signalling channel is what a join answer is relayed through when the host is not on
				// the registry node's own network. This rig publishes the lobby without a host screen of its own, so it
				// holds that channel open for the browser-path guest to be answered through.
				using var hostSignalling = new ClientWebSocket();
				Task hostSignallingLoop = HoldHostSignallingAsync(hostSignalling, registryUrl, lobbyId);
				await Task.Delay(250);

				await RunListedGuestChildAsync(projectDirectory, outputDirectory, lobbyId, registryUrl, notes, failures);

				hostSignalling.Abort();
				await Task.WhenAny(hostSignallingLoop, Task.Delay(1000));
			}
		}

		await RunIpv6GuestChildAsync(projectDirectory, outputDirectory, hostPort, notes, failures);

		await WaitForExitAsync(host, 120);
		StopProcess(host);
		(int hostExit, string hostOutcome) = ReadDoneFile(hostDone);
		if (hostExit != 0) failures.Add($"the IPv6 host did not seat its guests (exit {hostExit}: {hostOutcome})");
		else notes.Add(hostOutcome);

		var report = new StringBuilder();
		for (int index = 0; index < notes.Count; index++) report.AppendLine($"PASS {notes[index]}");
		for (int index = 0; index < failures.Count; index++) report.AppendLine($"FAIL {failures[index]}");
		return failures.Count > 0 ? (1, report.ToString().TrimEnd()) : (0, report.ToString().TrimEnd());
	}

	/// <summary>
	///     Joins a lobby through the browser path, which is the one that reads an endpoint list and picks the family it
	///     shares with the lobby. On a machine with a global IPv6 address this has to connect over IPv6; on one without, it
	///     has to refuse at once and name the family, which is the same failure a player on an IPv4-only network meets.
	/// </summary>
	/// <returns>The exit code and the line to report.</returns>
	private async Task<(int ExitCode, string Outcome)> RunListedGuestAsync()
	{
		LobbyManager lobby = GameContext.Lobby ?? throw new InvalidOperationException("The LobbyManager autoload is unavailable.");
		if (_joinedLobbyId.Length == 0) return (70, "--lobby-id is required to join a listed lobby");
		if (_liveRegistryUrl.Length == 0) return (71, "--registry is required to join a listed lobby");
		if (!PointTheRegistryAt(lobby, _liveRegistryUrl)) return (72, "the registry list could not be pointed at the rig");

		bool hasIpv6 = LocalEndpointDiscovery.HasGlobalIpv6();
		long started = Environment.TickCount64;
		bool joined = await lobby.JoinLobbyAsync(_joinedLobbyId);
		long elapsed = Environment.TickCount64 - started;

		if (!joined)
		{
			string reason = lobby.LobbyJoinError ?? lobby.LastHostError ?? "no reason was recorded";
			if (hasIpv6) return (73, $"this machine holds a global IPv6 address but could not join the IPv6 lobby: {reason}");
			if (!reason.Contains("IPv6", StringComparison.Ordinal)) return (74, $"the refusal does not name the address family that is missing: {reason}");
			if (elapsed > 8000) return (75, $"the family mismatch took {elapsed} ms to state, which is a timeout wearing a sentence: {reason}");

			return (0, $"a machine without a global IPv6 address is refused in {elapsed} ms with '{reason}'");
		}

		if (!hasIpv6) return (76, "a machine with no global IPv6 address claims to have joined an IPv6-only lobby");
		if (lobby.JoinedLobbyFamily != LobbyAddressFamily.IPv6) return (77, $"the join connected over {lobby.JoinedLobbyFamily} instead of IPv6");

		bool described = await WaitUntilAsync(() => lobby.HasLobbyState && lobby.LocalPeerId > 0, 20f);
		if (!described) return (78, "the lobby accepted the connection over IPv6 but never described a seat");

		return (0, $"joined the listed laptop over IPv6 as peer {lobby.LocalPeerId} with {lobby.PlayerList.Count} seat(s)");
	}

	/// <summary>
	///     Joins a lobby by an IPv6 address typed into direct connect, which needs no global IPv6 address on the joining
	///     machine when the address is on this one. It is the path that proves the game connection itself speaks IPv6.
	/// </summary>
	/// <returns>The exit code and the line to report.</returns>
	private async Task<(int ExitCode, string Outcome)> RunIpv6GuestAsync()
	{
		LobbyManager lobby = GameContext.Lobby ?? throw new InvalidOperationException("The LobbyManager autoload is unavailable.");
		if (_port <= 0) return (80, "the IPv6 guest was given no port to join");

		string host = _hostAddress.Length > 0 ? _hostAddress : "::1";
		bool joined = await lobby.JoinByIpAsync(host, _port);
		if (!joined) return (81, $"joining {LobbyAddresses.Describe(host, _port)} failed: {lobby.LastHostError ?? lobby.LobbyJoinError ?? "no reason was recorded"}");

		bool described = await WaitUntilAsync(() => lobby.HasLobbyState && lobby.LocalPeerId > 0, 20f);
		if (!described) return (82, $"the connection to {LobbyAddresses.Describe(host, _port)} was accepted but the lobby never described a seat");
		if (lobby.JoinedLobbyFamily != LobbyAddressFamily.IPv6) return (83, $"the join connected over {lobby.JoinedLobbyFamily} instead of IPv6");

		string remembered = DirectConnectHistory.DescribeAddress(host, _port);
		bool offered = DirectConnectHistory.LoadRecent().Exists(entry => entry.Address == remembered);
		if (!offered) return (84, $"joining {remembered} was not offered back as a recent address");
		if (!remembered.StartsWith('[')) return (85, $"an IPv6 address is remembered as '{remembered}', which the direct connect field would read as an address plus a port");

		return (0, $"joined {remembered} as peer {lobby.LocalPeerId} over IPv6 with {lobby.PlayerList.Count} seat(s), and it is remembered as '{remembered}'");
	}

	private async Task RunListedGuestChildAsync(string projectDirectory, string outputDirectory, string lobbyId, string registryUrl, List<string> notes, List<string> failures)
	{
		string doneFile = Path.Combine(outputDirectory, "listed-guest.done");
		await RunChildToCompletionAsync(projectDirectory, ["--role=listedguest", $"--lobby-id={lobbyId}", $"--registry={registryUrl}", $"--done-file={doneFile}", "--wait-seconds=30"], "0", 300);
		(int exitCode, string outcome) = ReadDoneFile(doneFile);
		if (exitCode != 0) failures.Add($"the browser path could not join the IPv6 lobby (exit {exitCode}: {outcome})");
		else notes.Add(outcome);
	}

	private async Task RunIpv6GuestChildAsync(string projectDirectory, string outputDirectory, int hostPort, List<string> notes, List<string> failures)
	{
		string doneFile = Path.Combine(outputDirectory, "ipv6-guest.done");
		await RunChildToCompletionAsync(projectDirectory, ["--role=v6guest", "--host=::1", $"--port={hostPort}", $"--done-file={doneFile}", "--wait-seconds=30"], "0", 300);
		(int exitCode, string outcome) = ReadDoneFile(doneFile);
		if (exitCode != 0) failures.Add($"a player could not reach the lobby over IPv6 (exit {exitCode}: {outcome})");
		else notes.Add(outcome);
	}

	/// <summary>
	///     Asks the registry node to measure the host's game port over IPv6 and returns the check it answered with, which is
	///     what a lobby has to name to be published. A check that never answers means no client could reach the lobby over
	///     IPv6 either, so there is nothing worth publishing.
	/// </summary>
	/// <param name="registryUrl">The rig's registry node.</param>
	/// <param name="advertisedIp">The address the lobby would be published at.</param>
	/// <param name="port">The game port.</param>
	/// <returns>The answered check, or nothing when the game port never answered.</returns>
	private static async Task<string?> MeasureEndpointAsync(string registryUrl, string advertisedIp, int port)
	{
		using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
		var begin = new { AdvertisedIp = advertisedIp, AdvertisedPort = port, WindowMilliseconds = 3000 };
		HttpResponseMessage started = await httpClient.PostAsJsonAsync($"{registryUrl}{ReachabilityRoutes.Begin}", begin);
		if (!started.IsSuccessStatusCode) return null;

		using JsonDocument ticketDocument = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
		string probeId = ticketDocument.RootElement.GetProperty("probeId").GetString() ?? string.Empty;
		if (probeId.Length == 0) return null;

		long deadline = Environment.TickCount64 + 15000;
		while (Environment.TickCount64 < deadline)
		{
			using JsonDocument reportDocument = JsonDocument.Parse(await httpClient.GetStringAsync($"{registryUrl}{ReachabilityRoutes.Report}/{probeId}"));
			JsonElement report = reportDocument.RootElement;
			string state = report.GetProperty("state").GetString() ?? string.Empty;
			if (report.GetProperty("answered").GetBoolean()) return probeId;
			if (state == ReachabilityStates.Silent) return null;

			await Task.Delay(100);
		}

		return null;
	}

	private static async Task<string?> RegisterIpv6OnlyLobbyAsync(string registryUrl, int hostPort, string probeId)
	{
		var request = new RigRegisterRequest(
			"IPv6 Rig Map",
			hostPort,
			"Open",
			"::1",
			string.Empty,
			4,
			1,
			20,
			RealmVersion.GameBinaryVersion,
			"127.0.0.1",
			"1.0.0",
			new List<RigEndpoint> { new("::1", hostPort, LobbyAddresses.FamilyName(LobbyAddressFamily.IPv6), probeId) },
			probeId);

		using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync($"{registryUrl}/lobbies/register", request);
		string body = await response.Content.ReadAsStringAsync();
		if (!response.IsSuccessStatusCode) return null;

		using JsonDocument document = JsonDocument.Parse(body);
		string lobbyId = document.RootElement.GetProperty("lobbyId").GetString() ?? string.Empty;
		return lobbyId.Length > 0 ? lobbyId : null;
	}

	/// <summary>
	///     Reads the listing the lobby browser reads, and checks that the lobby is stated as reachable over IPv6 alone. A
	///     listing that states no family is a lobby a browser cannot say anything useful about.
	/// </summary>
	private static async Task CheckListedFamiliesAsync(string registryUrl, string lobbyId, List<string> notes, List<string> failures)
	{
		using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
		string listing = await httpClient.GetStringAsync($"{registryUrl}/lobbies");
		using JsonDocument document = JsonDocument.Parse(listing);

		foreach (JsonElement lobby in document.RootElement.EnumerateArray())
		{
			if (lobby.GetProperty("lobbyId").GetString() != lobbyId) continue;

			if (!lobby.TryGetProperty("endpoints", out JsonElement endpoints) || endpoints.ValueKind != JsonValueKind.Array)
			{
				failures.Add("the listed lobby states no endpoint list");
				return;
			}

			var families = new List<string>();
			bool verified = true;
			foreach (JsonElement endpoint in endpoints.EnumerateArray())
			{
				families.Add(endpoint.GetProperty("family").GetString() ?? string.Empty);
				if (!endpoint.GetProperty("verified").GetBoolean()) verified = false;
			}

			if (!verified) failures.Add("the listed lobby carries an endpoint the registry node never reached");
			if (families.Count != 1 || families[0] != LobbyAddresses.FamilyName(LobbyAddressFamily.IPv6))
			{
				failures.Add($"the listed lobby states {string.Join(" + ", families)} instead of IPv6 only");
				return;
			}

			notes.Add("the lobby listing states one verified IPv6 endpoint and no IPv4 one");
			return;
		}

		failures.Add($"the lobby {lobbyId} is missing from the listing");
	}

	/// <summary>
	///     Holds the signalling socket a published lobby's own host would hold, so the registry node can answer a joining
	///     player through it instead of reporting that the lobby's host channel is gone.
	/// </summary>
	/// <param name="socket">The socket to hold open.</param>
	/// <param name="registryUrl">The rig's registry node.</param>
	/// <param name="lobbyId">The lobby the socket belongs to.</param>
	private static async Task HoldHostSignallingAsync(ClientWebSocket socket, string registryUrl, string lobbyId)
	{
		try
		{
			var uri = new Uri($"{registryUrl.Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase).Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)}/lobbies/ws?lobbyId={lobbyId}");
			await socket.ConnectAsync(uri, System.Threading.CancellationToken.None);

			var buffer = new byte[2048];
			while (socket.State == WebSocketState.Open)
			{
				WebSocketReceiveResult received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), System.Threading.CancellationToken.None);
				if (received.MessageType == WebSocketMessageType.Close) return;
			}
		}
		catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or ObjectDisposedException or UriFormatException)
		{
		}
	}

	private static async Task<RegistryCapabilities?> ReadCapabilitiesAsync(string registryUrl)
	{
		using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
		try
		{
			string json = await httpClient.GetStringAsync($"{registryUrl}{ReachabilityRoutes.Capabilities}");
			return JsonSerializer.Deserialize<RegistryCapabilities>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
		{
			return null;
		}
	}

	private static bool PointTheRegistryAt(LobbyManager lobby, string registryUrl)
	{
		var servers = lobby.OfficialServers;
		servers.Clear();
		servers.Add(registryUrl);
		GameLog.LogToConsole($"[DIRECT_HARNESS] The registry is set to {registryUrl} for this run.");
		return true;
	}

	private static void DeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path)) Directory.Delete(path, true);
		}
		catch (IOException)
		{
		}
	}

	private static void StopProcess(Process process)
	{
		try
		{
			if (!process.HasExited)
			{
				process.Kill(true);
				process.WaitForExit(5000);
			}
		}
		catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
		{
		}

		process.Dispose();
	}

	/// <summary>
	///     Starts the registry node the rig publishes through, on the loopback IPv6 address and with both probe sockets, so
	///     the whole control plane of the rig is reached over IPv6.
	/// </summary>
	private static Process StartRegistryNode(string assemblyPath, int httpPort, int probePort, string storageDirectory)
	{
		Directory.CreateDirectory(storageDirectory);

		var startInfo = new ProcessStartInfo("dotnet")
		{
			WorkingDirectory = Path.GetDirectoryName(assemblyPath) ?? ".",
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};

		startInfo.ArgumentList.Add(assemblyPath);
		startInfo.Environment["ASPNETCORE_URLS"] = $"http://[::1]:{httpPort}";
		startInfo.Environment["StorageDirectory"] = storageDirectory;
		startInfo.Environment["ReachabilityProbePort"] = probePort.ToString(CultureInfo.InvariantCulture);
		startInfo.Environment["ReachabilityProbePublicIp"] = IPAddress.Loopback.ToString();
		startInfo.Environment["ReachabilityProbePublicIpV6"] = IPAddress.IPv6Loopback.ToString();
		startInfo.Environment["AccountAuthority"] = "false";
		startInfo.Environment["Logging__LogLevel__Default"] = "Warning";

		Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("The registry node could not be started.");
		string logPath = Path.Combine(Path.GetDirectoryName(storageDirectory) ?? ".", "registry.log");
		process.OutputDataReceived += (_, line) => AppendLine(logPath, line.Data);
		process.ErrorDataReceived += (_, line) => AppendLine(logPath, line.Data);
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		return process;
	}

	private static void AppendLine(string path, string? line)
	{
		if (line == null) return;

		try
		{
			File.AppendAllText(path, line + Environment.NewLine);
		}
		catch (IOException)
		{
		}
	}

	private static async Task<bool> WaitForRegistryAsync(string registryUrl, int timeoutSeconds)
	{
		using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
		long deadline = Environment.TickCount64 + timeoutSeconds * 1000L;

		while (Environment.TickCount64 < deadline)
		{
			try
			{
				if ((await httpClient.GetAsync($"{registryUrl}{ReachabilityRoutes.Capabilities}")).IsSuccessStatusCode) return true;
			}
			catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
			{
			}

			await Task.Delay(250);
		}

		return false;
	}

	/// <summary>
	///     Finds the built registry node from this screen's own directory, walking up to the repository the client runs out of.
	/// </summary>
	private static string? FindRegistryAssembly()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		for (int level = 0; level < 8 && directory != null; level++, directory = directory.Parent)
		{
			foreach (string configuration in new[] { "Debug", "Release" })
			{
				string candidate = Path.Combine(directory.FullName, "Realm.AdminServer", "bin", configuration, "net10.0", "Realm.AdminServer.dll");
				if (File.Exists(candidate)) return candidate;
			}
		}

		return null;
	}

	private static int FreeIpv6TcpPort()
	{
		var listener = new TcpListener(IPAddress.IPv6Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}

	private static int FreeIpv6UdpPort()
	{
		using var socket = new UdpClient(new IPEndPoint(IPAddress.IPv6Loopback, 0));
		return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
	}
}
