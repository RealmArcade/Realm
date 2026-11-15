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

	private string _lobbyFile = string.Empty;

	/// <summary>Whether the registry this run publishes through is a development node on this machine, which cannot be public.</summary>
	private bool _allowLocalRegistry;

	/// <summary>The outcome the browser-path guest has to show: a family name, or "mismatch".</summary>
	private string _expectedFamily = string.Empty;

	/// <summary>How many guests the host role waits to seat, which is two in the run that joins from both families at once.</summary>
	private int _seatGuests = 1;

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
		IReadOnlyList<RigEndpoint>? Endpoints,
		string ReachabilityProbeId,
		string MappingBehaviour = MappingBehaviours.Unknown,
		int? ObservedPort = null);

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

		// The check a host runs on itself to find out whether its own transport answers in IPv6, against the real authority
		// rather than a fake port: this is the code path a machine with a global IPv6 address runs when it opens a lobby.
		if (await LobbyPortCheck.AnswersAsync(IPAddress.Loopback, hostPort, 4000)) notes.Add("the host's own port check answers over IPv4, as the check every host runs expects");
		else failures.Add("the host's own port check found nothing listening over IPv4, so the self-check a hosting machine runs would report a working transport as broken");

		if (!Socket.OSSupportsIPv6)
		{
			notes.Add("this machine has no IPv6 stack, so the self-check's IPv6 half was not asked");
		}
		else if (await LobbyPortCheck.AnswersAsync(IPAddress.IPv6Loopback, hostPort, 4000)) notes.Add("the host's own port check answers over IPv6, which is what a host states before offering an IPv6 address");
		else failures.Add("the host's own port check found nothing listening over IPv6, so a host offering an IPv6 address would be told its transport lost that family");

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
				await CheckBothFamiliesAreVerifiedAsync(registryUrl, hostPort, notes, failures);
				await CheckARouterThatChangesThePortKeepsOnlyTheFamilyItCannotBreakAsync(registryUrl, hostPort, notes, failures);
				await CheckAFilteredIpv6EndpointIsDroppedAsync(registryUrl, hostPort, notes, failures);
				await CheckAnOlderClientCanPublishAsync(registryUrl, hostPort, notes, failures);
				await CheckAJoinWithoutAHostChannelIsRefusedWithAReasonAsync(registryUrl, lobbyId, hostPort, notes, failures);

				// The lobby's own host signalling channel is what a join answer is relayed through when the host is not on
				// the registry node's own network. This rig publishes the lobby without a host screen of its own, so it
				// holds that channel open for the browser-path guest to be answered through.
				using var hostSignalling = new ClientWebSocket();
				Task hostSignallingLoop = HoldHostSignallingAsync(hostSignalling, registryUrl, lobbyId);
				await Task.Delay(250);

				await CheckAnOlderClientIsAnsweredAsync(registryUrl, lobbyId, hostPort, notes, failures);
				await RunListedGuestChildAsync(projectDirectory, outputDirectory, lobbyId, registryUrl, "mismatch", notes, failures);

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

		await RunBothFamiliesAtOnceAsync(projectDirectory, outputDirectory, notes, failures);
		await RunListedIpv4RigAsync(projectDirectory, outputDirectory, registryUrl, notes, failures);

		var report = new StringBuilder();
		for (int index = 0; index < notes.Count; index++) report.AppendLine($"PASS {notes[index]}");
		for (int index = 0; index < failures.Count; index++) report.AppendLine($"FAIL {failures[index]}");
		return failures.Count > 0 ? (1, report.ToString().TrimEnd()) : (0, report.ToString().TrimEnd());
	}

	/// <summary>
	///     Seats one player over IPv4 and another over IPv6 in the same lobby at the same time, which is the whole point of a
	///     dual-stack host: the players a lobby can reach are the union of both families, not one family at a time.
	/// </summary>
	/// <param name="projectDirectory">The client project the screens are started from.</param>
	/// <param name="outputDirectory">Where this run writes the files it passes between screens.</param>
	/// <param name="notes">The lines to report.</param>
	/// <param name="failures">The lines that failed.</param>
	private async Task RunBothFamiliesAtOnceAsync(string projectDirectory, string outputDirectory, List<string> notes, List<string> failures)
	{
		string inviteFile = Path.Combine(outputDirectory, "dual-host.invite");
		string hostDone = Path.Combine(outputDirectory, "dual-host.done");
		string ipv4GuestDone = Path.Combine(outputDirectory, "dual-guest-v4.done");
		string ipv6GuestDone = Path.Combine(outputDirectory, "dual-guest-v6.done");

		Process host = StartChild(projectDirectory, ["--role=host", "--mode=direct", "--dead-registry=1", $"--map={_mapFolder}", $"--invite-file={inviteFile}", $"--done-file={hostDone}", "--seat-count=2", "--wait-seconds=120"], "0");

		if (!await WaitForFileAsync(inviteFile, 240))
		{
			await WaitForExitAsync(host, 5);
			StopProcess(host);
			failures.Add($"the dual-stack host never opened a lobby (exit {ReadDoneFile(hostDone).ExitCode}: {ReadDoneFile(hostDone).Outcome})");
			return;
		}

		// Both guests are started before either is waited for, so the two connections are open in the same lobby at once.
		Process ipv4Guest = StartChild(projectDirectory, ["--role=guest", "--host=127.0.0.1", $"--invite-file={inviteFile}", $"--done-file={ipv4GuestDone}", "--wait-seconds=40"], "0");
		Process ipv6Guest = StartChild(projectDirectory, ["--role=guest", "--host=::1", $"--invite-file={inviteFile}", $"--done-file={ipv6GuestDone}", "--wait-seconds=40"], "0");

		await WaitForExitAsync(ipv4Guest, 120);
		await WaitForExitAsync(ipv6Guest, 120);
		StopProcess(ipv4Guest);
		StopProcess(ipv6Guest);

		(int ipv4Exit, string ipv4Outcome) = ReadDoneFile(ipv4GuestDone);
		(int ipv6Exit, string ipv6Outcome) = ReadDoneFile(ipv6GuestDone);
		if (ipv4Exit != 0) failures.Add($"the IPv4 guest could not join a dual-stack lobby (exit {ipv4Exit}: {ipv4Outcome})");
		if (ipv6Exit != 0) failures.Add($"the IPv6 guest could not join a dual-stack lobby (exit {ipv6Exit}: {ipv6Outcome})");

		await WaitForExitAsync(host, 120);
		StopProcess(host);
		(int hostExit, string hostOutcome) = ReadDoneFile(hostDone);
		if (hostExit != 0)
		{
			failures.Add($"a lobby did not hold both families at once (exit {hostExit}: {hostOutcome})");
			return;
		}

		notes.Add($"one lobby held an IPv4 player and an IPv6 player at the same time ({hostOutcome})");
	}

	/// <summary>
	///     Publishes a lobby the way the game itself does, through the rig's registry node, and joins it through the browser
	///     path. On this machine that lobby is offered over IPv4 alone, which is what makes this the check that the endpoint
	///     list did not change how an IPv4 lobby is hosted and joined.
	/// </summary>
	private async Task RunListedIpv4RigAsync(string projectDirectory, string outputDirectory, string registryUrl, List<string> notes, List<string> failures)
	{
		string inviteFile = Path.Combine(outputDirectory, "listed-host.invite");
		string lobbyFile = Path.Combine(outputDirectory, "listed-host.lobby");
		string hostDone = Path.Combine(outputDirectory, "listed-host.done");

		Process host = StartChild(projectDirectory, ["--role=host", "--mode=listed", $"--registry={registryUrl}", $"--map={_mapFolder}", $"--invite-file={inviteFile}", $"--lobby-file={lobbyFile}", $"--done-file={hostDone}", "--wait-seconds=90"], "0");

		if (!await WaitForFileAsync(lobbyFile, 240))
		{
			await WaitForExitAsync(host, 5);
			StopProcess(host);
			failures.Add($"a lobby published through the registry node over the endpoint list never named itself (exit {ReadDoneFile(hostDone).ExitCode}: {ReadDoneFile(hostDone).Outcome})");
			return;
		}

		string lobbyId = File.ReadAllText(lobbyFile).Trim();
		if (lobbyId.Length == 0)
		{
			StopProcess(host);
			failures.Add("a lobby published through the registry node was registered without an identifier");
			return;
		}

		notes.Add($"the screen published a lobby through the registry node as {lobbyId}");

		await RunListedGuestChildAsync(projectDirectory, outputDirectory, lobbyId, registryUrl, LocalEndpointDiscovery.HasGlobalIpv6() ? "ipv6" : "ipv4", notes, failures);

		await WaitForExitAsync(host, 120);
		StopProcess(host);
		(int hostExit, string hostOutcome) = ReadDoneFile(hostDone);
		if (hostExit != 0) failures.Add($"the published lobby did not seat its guest (exit {hostExit}: {hostOutcome})");
		else notes.Add(hostOutcome);
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
		PointTheRegistryAt(lobby, _liveRegistryUrl);

		bool hasIpv6 = LocalEndpointDiscovery.HasGlobalIpv6();
		string expected = _expectedFamily.Length > 0 ? _expectedFamily : hasIpv6 ? "ipv6" : "mismatch";

		long started = Environment.TickCount64;
		bool joined = await lobby.JoinLobbyAsync(_joinedLobbyId);
		long elapsed = Environment.TickCount64 - started;

		if (expected == "mismatch")
		{
			if (joined) return (73, $"the lobby was joined over {lobby.JoinedLobbyFamily} instead of being refused");

			string reason = lobby.LobbyJoinError ?? lobby.LastHostError ?? "no reason was recorded";
			if (!reason.Contains("IPv6", StringComparison.Ordinal)) return (74, $"the refusal does not name the address family that is missing: {reason}");
			if (elapsed > 8000) return (75, $"the family mismatch took {elapsed} ms to state, which is a timeout wearing a sentence: {reason}");

			return (0, $"a screen sharing no family with the lobby is refused in {elapsed} ms with '{reason}'");
		}

		if (!joined) return (76, $"joining the listed lobby failed: {lobby.LastHostError ?? lobby.LobbyJoinError ?? "no reason was recorded"}");

		LobbyAddressFamily wanted = LobbyAddresses.ParseFamilyName(expected);
		if (lobby.JoinedLobbyFamily != wanted) return (77, $"the join connected over {lobby.JoinedLobbyFamily} instead of {expected}");

		bool described = await WaitUntilAsync(() => lobby.HasLobbyState && lobby.LocalPeerId > 0, 20f);
		if (!described) return (78, $"the lobby accepted the connection over {expected} but never described a seat");

		return (0, $"joined the listed lobby over {expected} as peer {lobby.LocalPeerId} with {lobby.PlayerList.Count} seat(s)");
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

	private async Task RunListedGuestChildAsync(string projectDirectory, string outputDirectory, string lobbyId, string registryUrl, string expectedFamily, List<string> notes, List<string> failures)
	{
		string doneFile = Path.Combine(outputDirectory, "listed-guest.done");
		await RunChildToCompletionAsync(projectDirectory, ["--role=listedguest", $"--lobby-id={lobbyId}", $"--registry={registryUrl}", $"--expect={expectedFamily}", $"--done-file={doneFile}", "--wait-seconds=30"], "0", 300);
		(int exitCode, string outcome) = ReadDoneFile(doneFile);
		if (exitCode != 0) failures.Add($"the browser path did not behave as expected for this lobby (exit {exitCode}: {outcome})");
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
	///     Publishes a lobby whose IPv4 endpoint answered and whose IPv6 endpoint never did, which is what a v6-capable
	///     machine behind a carrier or firewall that filters incoming IPv6 looks like from outside. The lobby has to be
	///     published over the family that answered and must not name the one that did not: a listed address nobody can reach
	///     is worse than no address at all, and a filtered endpoint is not a reason to refuse the lobby.
	/// </summary>
	/// <param name="registryUrl">The rig's registry node.</param>
	/// <param name="hostPort">The game port.</param>
	/// <param name="notes">The lines to report.</param>
	/// <param name="failures">The lines that failed.</param>
	private static async Task CheckAFilteredIpv6EndpointIsDroppedAsync(string registryUrl, int hostPort, List<string> notes, List<string> failures)
	{
		string? ipv4ProbeId = await MeasureEndpointAsync(registryUrl, "127.0.0.1", hostPort);
		if (ipv4ProbeId == null)
		{
			failures.Add("the registry node never reached the game port over IPv4, so a filtered IPv6 endpoint could not be tested");
			return;
		}

		// A check the node starts and never hears back from: 2001:db8::/32 is documentation space, so nothing is at that
		// address and the answer cannot arrive. The registration is made while the check is still open, which is the state a
		// filtered endpoint is in when the host registers the endpoints it measured.
		string? unreachableIpv6ProbeId = await BeginUnansweredCheckAsync(registryUrl, "2001:db8::d1:1", hostPort);
		if (unreachableIpv6ProbeId == null)
		{
			failures.Add("the registry node refused to start a check of an unreachable IPv6 endpoint");
			return;
		}

		var request = new RigRegisterRequest(
			"IPv6 Rig Filtered Map",
			hostPort,
			"Open",
			"127.0.0.1",
			string.Empty,
			4,
			1,
			20,
			RealmVersion.GameBinaryVersion,
			"127.0.0.1",
			"1.0.0",
			new List<RigEndpoint>
			{
				new("2001:db8::d1:1", hostPort, LobbyAddresses.FamilyName(LobbyAddressFamily.IPv6), unreachableIpv6ProbeId),
				new("127.0.0.1", hostPort, LobbyAddresses.FamilyName(LobbyAddressFamily.IPv4), ipv4ProbeId)
			},
			ipv4ProbeId);

		using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync($"{registryUrl}/lobbies/register", request);
		if (!response.IsSuccessStatusCode)
		{
			failures.Add($"a lobby with one reachable endpoint was refused with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
			return;
		}

		using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		string lobbyId = document.RootElement.GetProperty("lobbyId").GetString() ?? string.Empty;

		string listing = await httpClient.GetStringAsync($"{registryUrl}/lobbies");
		using JsonDocument listingDocument = JsonDocument.Parse(listing);
		foreach (JsonElement lobby in listingDocument.RootElement.EnumerateArray())
		{
			if (lobby.GetProperty("lobbyId").GetString() != lobbyId) continue;

			var families = new List<string>();
			foreach (JsonElement endpoint in lobby.GetProperty("endpoints").EnumerateArray())
			{
				if (!endpoint.GetProperty("verified").GetBoolean()) failures.Add("a filtered endpoint was listed anyway");
				families.Add(endpoint.GetProperty("family").GetString() ?? string.Empty);
			}

			if (families.Count != 1 || families[0] != LobbyAddresses.FamilyName(LobbyAddressFamily.IPv4))
			{
				failures.Add($"a lobby whose IPv6 endpoint never answered states {string.Join(" + ", families)} instead of IPv4 only");
				return;
			}

			notes.Add("a lobby whose IPv6 endpoint never answered is published over IPv4 alone, with no IPv6 endpoint listed");
			return;
		}

		failures.Add($"the lobby {lobbyId} is missing from the listing");
	}

	/// <summary>
	///     Publishes a lobby the way a screen that predates endpoint lists does: one address, one check, and no list. The node
	///     has to accept it and store it as the lobby this game has always listed, because an operator's node is updated
	///     before every player's copy of the game is.
	/// </summary>
	/// <param name="registryUrl">The rig's registry node.</param>
	/// <param name="hostPort">The game port.</param>
	/// <param name="notes">The lines to report.</param>
	/// <param name="failures">The lines that failed.</param>
	private static async Task CheckAnOlderClientCanPublishAsync(string registryUrl, int hostPort, List<string> notes, List<string> failures)
	{
		string? probeId = await MeasureEndpointAsync(registryUrl, "127.0.0.1", hostPort);
		if (probeId == null)
		{
			failures.Add("the registry node never reached the game port over IPv4, so a lobby published by an older screen could not be tested");
			return;
		}

		var request = new RigRegisterRequest(
			"IPv6 Rig Old Client Map",
			hostPort,
			"Open",
			"127.0.0.1",
			string.Empty,
			4,
			1,
			20,
			RealmVersion.GameBinaryVersion,
			"127.0.0.1",
			"1.0.0",
			null,
			probeId);

		using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync($"{registryUrl}/lobbies/register", request);
		if (!response.IsSuccessStatusCode)
		{
			failures.Add($"a lobby registered without an endpoint list was refused with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
			return;
		}

		using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		string lobbyId = document.RootElement.GetProperty("lobbyId").GetString() ?? string.Empty;

		string listing = await httpClient.GetStringAsync($"{registryUrl}/lobbies");
		using JsonDocument listingDocument = JsonDocument.Parse(listing);
		foreach (JsonElement lobby in listingDocument.RootElement.EnumerateArray())
		{
			if (lobby.GetProperty("lobbyId").GetString() != lobbyId) continue;

			var families = new List<string>();
			foreach (JsonElement endpoint in lobby.GetProperty("endpoints").EnumerateArray())
			{
				if (!endpoint.GetProperty("verified").GetBoolean()) failures.Add("a lobby published without an endpoint list carries an endpoint the node never reached");
				families.Add(endpoint.GetProperty("family").GetString() ?? string.Empty);
			}

			if (families.Count != 1 || families[0] != LobbyAddresses.FamilyName(LobbyAddressFamily.IPv4))
			{
				failures.Add($"a lobby published by an older screen states {string.Join(" + ", families)} instead of the IPv4 endpoint that screen measured");
				return;
			}

			notes.Add("a lobby published without an endpoint list is stored as the single shared-IPv4 endpoint older screens have always stated");
			return;
		}

		failures.Add($"the lobby {lobbyId} published without an endpoint list is missing from the listing");
	}

	/// <summary>
	///     Asks to join a lobby whose host has no signalling channel open, which is the answer a registry node gives when the
	///     lobby's host screen has gone. The screen that receives it has to be able to read why, because a refusal with no
	///     reason is what leaves a player looking at a timeout that described nothing.
	/// </summary>
	/// <param name="registryUrl">The rig's registry node.</param>
	/// <param name="lobbyId">The lobby being joined.</param>
	/// <param name="hostPort">The port the lobby listens on.</param>
	/// <param name="notes">The lines to report.</param>
	/// <param name="failures">The lines that failed.</param>
	private static async Task CheckAJoinWithoutAHostChannelIsRefusedWithAReasonAsync(string registryUrl, string lobbyId, int hostPort, List<string> notes, List<string> failures)
	{
		using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
		var request = new { LobbyId = lobbyId, ClientPublicIP = "127.0.0.1", ClientPublicPort = hostPort, ClientEndpointFamilies = new[] { "ipv4" } };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync($"{registryUrl}/lobbies/join", request);
		string body = await response.Content.ReadAsStringAsync();

		if (response.IsSuccessStatusCode)
		{
			failures.Add("a join to a lobby whose host channel is closed was answered as if it could be dialled");
			return;
		}

		if (!body.Contains("message", StringComparison.OrdinalIgnoreCase))
		{
			failures.Add($"a refused join states no reason a screen could show: {(int)response.StatusCode} {body}");
			return;
		}

		notes.Add($"a join to a lobby whose host channel is closed is refused with a readable reason: {body.Trim()}");
	}

	/// <summary>
	///     Reads the answer a screen that predates endpoint lists receives when it asks to join, by asking the same way it
	///     asks: no families stated, no endpoint list expected.
	/// </summary>
	/// <param name="registryUrl">The rig's registry node.</param>
	/// <param name="lobbyId">The IPv6-only lobby.</param>
	/// <param name="hostPort">The port the lobby listens on.</param>
	/// <param name="notes">The lines to report.</param>
	/// <param name="failures">The lines that failed.</param>
	private static async Task CheckAnOlderClientIsAnsweredAsync(string registryUrl, string lobbyId, int hostPort, List<string> notes, List<string> failures)
	{
		using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
		var oldStyleRequest = new { LobbyId = lobbyId, ClientPublicIP = "127.0.0.1", ClientPublicPort = hostPort };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync($"{registryUrl}/lobbies/join", oldStyleRequest);
		string body = await response.Content.ReadAsStringAsync();

		if (!response.IsSuccessStatusCode)
		{
			failures.Add($"a screen that states no address family is refused rather than answered: {(int)response.StatusCode} {body}");
			return;
		}

		LobbyJoinAnswer? answer = LobbyJoinAnswers.TryParse(body);
		if (answer == null)
		{
			failures.Add($"a screen that states no address family is answered with something unreadable: {body}");
			return;
		}

		IReadOnlyList<LobbyEndpointInfo> endpoints = answer.Value.EndpointsOrStatedAddress();
		if (endpoints.Count == 0) failures.Add("a join answer states no endpoint at all");
		else if (endpoints[0].FamilyValue != LobbyAddressFamily.IPv6) failures.Add($"a lobby published over IPv6 is answered to an older screen with a {endpoints[0].FamilyValue} endpoint");

		if (failures.Count == 0) notes.Add("a screen that states no address family is answered with the lobby's IPv6 endpoint instead of a refusal");
	}

	/// <summary>
	///     Starts a check of an endpoint and reads its ticket without waiting for an answer, which is the state a check of a
	///     filtered endpoint is in.
	/// </summary>
	/// <param name="registryUrl">The rig's registry node.</param>
	/// <param name="advertisedIp">The address nothing answers at.</param>
	/// <param name="port">The port the check asks at.</param>
	/// <returns>The probe id, or nothing when the node refused to start the check.</returns>
	private static async Task<string?> BeginUnansweredCheckAsync(string registryUrl, string advertisedIp, int port)
	{
		using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
		var begin = new { AdvertisedIp = advertisedIp, AdvertisedPort = port, WindowMilliseconds = 3000 };
		HttpResponseMessage started = await httpClient.PostAsJsonAsync($"{registryUrl}{ReachabilityRoutes.Begin}", begin);
		if (!started.IsSuccessStatusCode) return null;

		using JsonDocument document = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
		string probeId = document.RootElement.GetProperty("probeId").GetString() ?? string.Empty;
		return probeId.Length > 0 ? probeId : null;
	}

	/// <summary>
	///     Publishes the same game port at both of its addresses, which is what a dual-stack host does, and reads back what
	///     the node stored. Each endpoint is measured from the socket of its own family, so a node that mixed them up would
	///     report a family it never reached.
	/// </summary>
	/// <param name="registryUrl">The rig's registry node.</param>
	/// <param name="hostPort">The game port the authority listens on in both families.</param>
	/// <param name="notes">The lines to report.</param>
	/// <param name="failures">The lines that failed.</param>
	private static async Task CheckBothFamiliesAreVerifiedAsync(string registryUrl, int hostPort, List<string> notes, List<string> failures)
	{
		// A check is consumed by the registration that reads it, so a lobby published at two addresses asks for one check
		// per address rather than reusing the one the IPv6-only lobby already spent.
		string? ipv6ProbeId = await MeasureEndpointAsync(registryUrl, "::1", hostPort);
		if (ipv6ProbeId == null)
		{
			failures.Add("the registry node never reached the game port over IPv6 a second time, so a dual-stack lobby could not be published");
			return;
		}

		string? ipv4ProbeId = await MeasureEndpointAsync(registryUrl, "127.0.0.1", hostPort);
		if (ipv4ProbeId == null)
		{
			failures.Add("the registry node never reached the game port over IPv4, so a dual-stack lobby could not be published");
			return;
		}

		var request = new RigRegisterRequest(
			"IPv6 Rig Dual Stack Map",
			hostPort,
			"Open",
			"127.0.0.1",
			string.Empty,
			4,
			1,
			20,
			RealmVersion.GameBinaryVersion,
			"127.0.0.1",
			"1.0.0",
			new List<RigEndpoint>
			{
				new("::1", hostPort, LobbyAddresses.FamilyName(LobbyAddressFamily.IPv6), ipv6ProbeId),
				new("127.0.0.1", hostPort, LobbyAddresses.FamilyName(LobbyAddressFamily.IPv4), ipv4ProbeId)
			},
			ipv6ProbeId);

		using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync($"{registryUrl}/lobbies/register", request);
		if (!response.IsSuccessStatusCode)
		{
			failures.Add($"a dual-stack lobby was refused with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
			return;
		}

		using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		string lobbyId = document.RootElement.GetProperty("lobbyId").GetString() ?? string.Empty;

		string listing = await httpClient.GetStringAsync($"{registryUrl}/lobbies");
		using JsonDocument listingDocument = JsonDocument.Parse(listing);
		foreach (JsonElement lobby in listingDocument.RootElement.EnumerateArray())
		{
			if (lobby.GetProperty("lobbyId").GetString() != lobbyId) continue;

			var families = new List<string>();
			var addresses = new List<string>();
			foreach (JsonElement endpoint in lobby.GetProperty("endpoints").EnumerateArray())
			{
				if (!endpoint.GetProperty("verified").GetBoolean()) failures.Add("a dual-stack lobby carries an endpoint the registry node never reached");
				families.Add(endpoint.GetProperty("family").GetString() ?? string.Empty);
				addresses.Add(LobbyAddresses.Describe(endpoint.GetProperty("address").GetString() ?? string.Empty, endpoint.GetProperty("port").GetInt32()));
			}

			if (families.Count != 2 || !families.Contains(LobbyAddresses.FamilyName(LobbyAddressFamily.IPv4)) || !families.Contains(LobbyAddresses.FamilyName(LobbyAddressFamily.IPv6)))
			{
				failures.Add($"a dual-stack lobby states {string.Join(" + ", families)} instead of both families");
				return;
			}

			notes.Add($"a dual-stack lobby states both verified endpoints, {string.Join(" and ", addresses)}");
			return;
		}

		failures.Add($"the dual-stack lobby {lobbyId} is missing from the listing");
	}

	/// <summary>
	///     Publishes a dual-stack lobby from a host whose router gives every destination its own outside port, and reads back
	///     what the node kept. Such a router hands an IPv4 endpoint a different outside port for every peer, so a punch can
	///     open a way for the one destination it was aimed at and for nobody else: the IPv4 endpoints are dropped, and the
	///     IPv6 endpoint is untouched because no router stands in front of it. Keeping both would list a lobby at an address
	///     no player could join, and dropping both would refuse a lobby that is reachable over IPv6.
	/// </summary>
	/// <param name="registryUrl">The rig's registry node.</param>
	/// <param name="hostPort">The game port the authority listens on in both families.</param>
	/// <param name="notes">The lines to report.</param>
	/// <param name="failures">The lines that failed.</param>
	private static async Task CheckARouterThatChangesThePortKeepsOnlyTheFamilyItCannotBreakAsync(string registryUrl, int hostPort, List<string> notes, List<string> failures)
	{
		string? ipv6ProbeId = await MeasureEndpointAsync(registryUrl, "::1", hostPort);
		string? ipv4ProbeId = await MeasureEndpointAsync(registryUrl, "127.0.0.1", hostPort);
		if (ipv6ProbeId == null || ipv4ProbeId == null)
		{
			failures.Add("the registry node could not measure both endpoints of the lobby a port-changing router is published from");
			return;
		}

		var request = new RigRegisterRequest(
			"IPv6 Rig Port Changing Router Map",
			hostPort,
			"Open",
			"127.0.0.1",
			string.Empty,
			4,
			1,
			20,
			RealmVersion.GameBinaryVersion,
			"127.0.0.1",
			"1.0.0",
			new List<RigEndpoint>
			{
				new("::1", hostPort, LobbyAddresses.FamilyName(LobbyAddressFamily.IPv6), ipv6ProbeId),
				new("127.0.0.1", hostPort, LobbyAddresses.FamilyName(LobbyAddressFamily.IPv4), ipv4ProbeId)
			},
			ipv6ProbeId,
			MappingBehaviours.PerDestination,
			hostPort + 7);

		using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
		HttpResponseMessage response = await httpClient.PostAsJsonAsync($"{registryUrl}/lobbies/register", request);
		if (!response.IsSuccessStatusCode)
		{
			failures.Add($"a dual-stack lobby behind a port-changing router was refused with {(int)response.StatusCode} instead of being published over the family that router cannot break: {await response.Content.ReadAsStringAsync()}");
			return;
		}

		using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		string lobbyId = document.RootElement.GetProperty("lobbyId").GetString() ?? string.Empty;

		string listing = await httpClient.GetStringAsync($"{registryUrl}/lobbies");
		using JsonDocument listingDocument = JsonDocument.Parse(listing);
		foreach (JsonElement lobby in listingDocument.RootElement.EnumerateArray())
		{
			if (lobby.GetProperty("lobbyId").GetString() != lobbyId) continue;

			var families = new List<string>();
			foreach (JsonElement endpoint in lobby.GetProperty("endpoints").EnumerateArray())
			{
				families.Add(endpoint.GetProperty("family").GetString() ?? string.Empty);
				int port = endpoint.GetProperty("port").GetInt32();
				if (port != hostPort) failures.Add($"the lobby a port-changing router published states port {port} instead of the port its transport holds ({hostPort})");
			}

			if (families.Count != 1 || families[0] != LobbyAddresses.FamilyName(LobbyAddressFamily.IPv6))
			{
				failures.Add($"a lobby behind a router that gives every destination its own outside port states {string.Join(" + ", families)} instead of the ipv6 endpoint alone");
				return;
			}

			notes.Add("a router that gives every destination its own outside port cost a dual-stack lobby its ipv4 endpoint and nothing else");
			return;
		}

		failures.Add($"the lobby a port-changing router published ({lobbyId}) is missing from the listing");
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

		// The node and the host share one machine here, so every check it makes comes from inside its own network. A node on
		// the internet refuses exactly that, and this is the setting that tells it this one is a development node.
		startInfo.Environment["ReachabilityAllowLocalCaller"] = "true";
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
