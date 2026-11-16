using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using Realm.Shared.Reachability;
using Environment = System.Environment;
using HttpClient = System.Net.Http.HttpClient;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Realm.Ecs;
using Realm.Shared;

namespace Realm.Client.Testing;

/// <summary>
///     Drives the game against a registry node built before endpoint lists existed, which is the node an operator may be
///     running while updated clients reach it. Two things have to hold: a lobby published through it is exactly the lobby
///     this game published before IPv6 existed, and a screen that asks to join it is answered in the shape that screen's
///     build reads. Neither is a guess about the protocol; both are the answer of a node that never heard of it.
/// </summary>
public partial class LobbyDirectConnectHarness
{
	/// <summary>
	///     The registry node to run instead of this build's own, which is the build an operator who has not updated yet is
	///     running.
	/// </summary>
	private string _oldRegistryAssembly = string.Empty;

	/// <summary>
	///     Runs the old-node rig: the node itself, a lobby published through it by this build, and a screen joining that
	///     lobby through the browser path.
	/// </summary>
	/// <returns>The exit code and the lines to report.</returns>
	private async Task<(int ExitCode, string Outcome)> RunOldNodeDriverAsync()
	{
		if (_mapFolder.Length == 0) return (80, "--map is required to drive the old-node rig");
		if (_oldRegistryAssembly.Length == 0) return (80, "--old-registry-dll is required, and states the registry node built before endpoint lists existed");
		if (!File.Exists(_oldRegistryAssembly)) return (81, $"the old registry node assembly '{_oldRegistryAssembly}' does not exist");

		string outputDirectory = Path.Combine(Path.GetTempPath(), "realm-oldnode-harness");
		Directory.CreateDirectory(outputDirectory);
		foreach (string stale in Directory.GetFiles(outputDirectory)) File.Delete(stale);
		DeleteDirectory(Path.Combine(outputDirectory, "registry-data"));

		int registryPort = FreeIpv6TcpPort();
		int probePort = FreeIpv6UdpPort();
		string registryUrl = $"http://[::1]:{registryPort}";
		string projectDirectory = ProjectSettings.GlobalizePath("res://");

		Process registry = StartRegistryNode(_oldRegistryAssembly, registryPort, probePort, Path.Combine(outputDirectory, "registry-data"));
		try
		{
			return await RunOldNodeRigAsync(projectDirectory, outputDirectory, registryUrl);
		}
		finally
		{
			StopProcess(registry);
		}
	}

	private async Task<(int ExitCode, string Outcome)> RunOldNodeRigAsync(string projectDirectory, string outputDirectory, string registryUrl)
	{
		var failures = new List<string>();
		var notes = new List<string>();

		if (!await WaitForAnyAnswerAsync(registryUrl, 90))
		{
			return (82, $"the old registry node never answered on {registryUrl}; see {Path.Combine(outputDirectory, "registry.log")}");
		}

		using (var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
		{
			HttpResponseMessage capabilities = await httpClient.GetAsync($"{registryUrl}{ReachabilityRoutes.Capabilities}");
			if (capabilities.IsSuccessStatusCode)
			{
				return (83, "the registry node under test states the endpoint-list capabilities this build added, so it is not an older node");
			}

			notes.Add($"the old registry node answers on {registryUrl} and states no capabilities route ({(int)capabilities.StatusCode})");
		}

		string inviteFile = Path.Combine(outputDirectory, "old-host.invite");
		string lobbyFile = Path.Combine(outputDirectory, "old-host.lobby");
		string hostDone = Path.Combine(outputDirectory, "old-host.done");

		Process host = StartChild(projectDirectory, ["--role=host", "--mode=listed", "--allow-local-registry=1", $"--registry={registryUrl}", $"--map={_mapFolder}", $"--invite-file={inviteFile}", $"--lobby-file={lobbyFile}", $"--done-file={hostDone}", "--wait-seconds=120"], "0");
		try
		{
			if (!await WaitForFileAsync(lobbyFile, 240))
			{
				(int exitCode, string outcome) = ReadDoneFile(hostDone);
				return (84, $"a lobby could not be published through the old registry node (exit {exitCode}: {outcome})");
			}

			string lobbyId = File.ReadAllText(lobbyFile).Trim();
			if (lobbyId.Length == 0) return (85, "the old registry node registered the lobby without an identifier");

			notes.Add($"this build published a lobby through the old node as {lobbyId}");

			if (!await CheckOldListingAndJoinAsync(registryUrl, lobbyId, notes, failures)) return (86, string.Join("; ", failures));

			await RunListedGuestChildAsync(projectDirectory, outputDirectory, lobbyId, registryUrl, "ipv4", notes, failures);

			await WaitForExitAsync(host, 120);
			(int hostExit, string hostOutcome) = ReadDoneFile(hostDone);
			if (hostExit != 0) failures.Add($"the lobby published through the old node did not seat its guest (exit {hostExit}: {hostOutcome})");
			else notes.Add(hostOutcome);
		}
		finally
		{
			StopProcess(host);
		}

		var report = new StringBuilder();
		for (int index = 0; index < notes.Count; index++) report.AppendLine($"PASS {notes[index]}");
		for (int index = 0; index < failures.Count; index++) report.AppendLine($"FAIL {failures[index]}");
		return failures.Count > 0 ? (1, report.ToString().TrimEnd()) : (0, report.ToString().TrimEnd());
	}

	/// <summary>
	///     Reads the listing and the join answer of an old node: neither may carry an endpoint list, and the join answer has
	///     to state the single address this build has always dialled.
	/// </summary>
	/// <param name="registryUrl">The old registry node.</param>
	/// <param name="lobbyId">The lobby that was published through it.</param>
	/// <param name="notes">The lines to report.</param>
	/// <param name="failures">The lines that failed.</param>
	/// <returns>Whether both answers were in the shape an older node gives.</returns>
	private static async Task<bool> CheckOldListingAndJoinAsync(string registryUrl, string lobbyId, List<string> notes, List<string> failures)
	{
		using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

		string listing = await httpClient.GetStringAsync($"{registryUrl}/lobbies");
		using JsonDocument listingDocument = JsonDocument.Parse(listing);
		bool found = false;
		foreach (JsonElement lobby in listingDocument.RootElement.EnumerateArray())
		{
			if (lobby.GetProperty("lobbyId").GetString() != lobbyId) continue;

			found = true;
			if (lobby.TryGetProperty("endpoints", out JsonElement listedEndpoints) && listedEndpoints.ValueKind == JsonValueKind.Array && listedEndpoints.GetArrayLength() > 0)
			{
				failures.Add($"the old registry node listed {listedEndpoints.GetArrayLength()} endpoint(s), which it cannot have verified");
				return false;
			}

			if (!lobby.TryGetProperty("hostIP", out JsonElement hostIp) || (hostIp.GetString() ?? string.Empty).Length == 0)
			{
				failures.Add("the old registry node listed the lobby without the address it has always stated");
				return false;
			}
		}

		if (!found)
		{
			failures.Add($"the lobby {lobbyId} is missing from the old node's listing");
			return false;
		}

		var oldStyleRequest = new { LobbyId = lobbyId, ClientPublicIP = "127.0.0.1", ClientPublicPort = 8999 };
		HttpResponseMessage join = await httpClient.PostAsJsonAsync($"{registryUrl}/lobbies/join", oldStyleRequest);
		string joinBody = await join.Content.ReadAsStringAsync();
		if (!join.IsSuccessStatusCode)
		{
			failures.Add($"the old registry node refused a join to the lobby it published: {(int)join.StatusCode} {joinBody}");
			return false;
		}

		LobbyJoinAnswer? answer = LobbyJoinAnswers.TryParse(joinBody);
		if (answer == null)
		{
			failures.Add($"the old registry node's join answer carries no address this build can read: {joinBody}");
			return false;
		}

		IReadOnlyList<LobbyEndpointInfo> endpoints = answer.Value.EndpointsOrStatedAddress();
		if (endpoints.Count != 1 || endpoints[0].FamilyValue != LobbyAddressFamily.IPv4)
		{
			failures.Add($"the old registry node's join answer states {endpoints.Count} endpoint(s) rather than the one IPv4 address it always stated");
			return false;
		}

		notes.Add($"the old node lists no endpoint list and answers a join with the single address {endpoints[0].Describe()}");
		return true;
	}

	/// <summary>
	///     Waits for a registry node that has no capabilities route to answer at all, which is the only question an older
	///     node can be asked before its storage is open.
	/// </summary>
	/// <param name="registryUrl">The node to wait for.</param>
	/// <param name="timeoutSeconds">How long to wait.</param>
	/// <returns>Whether the node answered.</returns>
	private static async Task<bool> WaitForAnyAnswerAsync(string registryUrl, int timeoutSeconds)
	{
		using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
		long deadline = Environment.TickCount64 + timeoutSeconds * 1000L;

		while (Environment.TickCount64 < deadline)
		{
			try
			{
				using HttpResponseMessage response = await httpClient.GetAsync($"{registryUrl}/lobbies");
				if (response.IsSuccessStatusCode) return true;
			}
			catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
			{
			}

			await Task.Delay(250);
		}

		return false;
	}
}
