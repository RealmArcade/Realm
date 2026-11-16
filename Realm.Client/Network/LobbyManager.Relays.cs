using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Shared.Network;
using Realm.Shared.Network.Relay;
using Realm.Shared.Reachability;

namespace Realm.Client.Network;

public partial class LobbyManager
{
	private RelayUdpShim? _relayShim;

	private RelayConnectionPlan? _relayedConnectionPlan;

	private RelayPathSummary _lastRelayedPathSummary = RelayPathSummary.Empty;

	private long _relayedPathStatementMilliseconds;

	private const int RelayedPathStatementIntervalMilliseconds = 1000;

	/// <summary>
	///     The volunteers one joining connection is carried through, as this screen opened them, or empty when the connection
	///     takes the direct path. The list is what the connection status and the diagnostics page name, and it is always
	///     read from paths that were opened rather than from a list that was merely offered.
	/// </summary>
	public IReadOnlyList<string> RelayedVolunteers => _relayedVolunteerIds;

	private readonly List<string> _relayedVolunteerIds = new();

	/// <summary>Whether this screen's connection to the lobby it joined is being carried through volunteers.</summary>
	public bool IsConnectionRelayed => _relayShim != null;

	/// <summary>
	///     What this screen's relayed connection held the last time its path layer stated them, and what its best path
	///     measured. An empty statement means the connection takes the direct path.
	/// </summary>
	public RelayPathSummary RelayedPathSummary => _relayShim?.Summary ?? RelayPathSummary.Empty;

	/// <summary>
	///     Whether a lobby this screen hosts accepts its connections being carried through volunteers. It is the host's own
	///     setting and is stated when the lobby is registered, so a lobby that turns it off is published exactly as it was
	///     before relays existed.
	/// </summary>
	public bool AcceptsRelayedConnections => GameSettings.AcceptRelayedConnections && !IsSinglePlayer;

	/// <summary>
	///     Asks the lobby server how many volunteers it currently pools. It is asked before a lobby that nothing reached is
	///     published, so a host learns that no volunteer is available instead of publishing a lobby with no way in.
	/// </summary>
	/// <param name="registryUrl">The lobby server about to publish the lobby.</param>
	/// <returns>How many candidates it offered, or zero when it offered none or could not be asked.</returns>
	private async Task<int> CountVolunteerCandidatesAsync(string registryUrl)
	{
		if (string.IsNullOrEmpty(registryUrl)) return 0;

		try
		{
			using HttpResponseMessage response = await _httpClient.GetAsync($"{registryUrl.TrimEnd('/')}/relays/candidates");
			if (!response.IsSuccessStatusCode) return 0;

			using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
			foreach (string propertyName in new[] { "Candidates", "candidates", "Relays", "relays" })
			{
				if (!document.RootElement.TryGetProperty(propertyName, out System.Text.Json.JsonElement list)) continue;
				return list.ValueKind == System.Text.Json.JsonValueKind.Array ? list.GetArrayLength() : 0;
			}

			return 0;
		}
		catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
		{
			GameLog.LogToConsole($"[Relay] {registryUrl} could not say which volunteers it pools, so a lobby nothing reached stays unpublished: {exception.Message}");
			return 0;
		}
	}

	/// <summary>
	///     Reads the volunteers a join answer stated into the plan both peers open. A lobby server that offered none, or a
	///     screen that turned relayed connections off, produces no plan and the join keeps the direct path.
	/// </summary>
	/// <param name="answer">The answer the lobby server gave this join.</param>
	/// <returns>The plan to carry the connection, or null when it stays direct.</returns>
	private RelayConnectionPlan? RelayPlanFor(LobbyJoinAnswer answer)
	{
		RememberRelayOffers(answer.Relays?.Count ?? 0);
		if (answer.Relays is not { Count: > 0 }) return null;
		if (!RelayJoinSecret.IsUsable(answer.PunchToken)) return null;

		return RelayConnectionPlan.From(answer.PunchToken, answer.Relays, GameSettings.RelayDuplication, GameSettings.RelayDuplicationBudgetBytesPerSecond);
	}

	/// <summary>
	///     Opens the volunteers a join answer offered. Every offer the node stated becomes a path of one shim, so the
	///     connection ENet makes speaks to one loopback port for its whole life while the paths behind that port race each
	///     other and are chosen by what they measured. A plan that names no usable volunteer leaves the join on the direct
	///     path rather than failing it.
	/// </summary>
	/// <param name="plan">The plan both peers were given for this join.</param>
	/// <param name="loopbackPort">The port this screen's connection attempt speaks to.</param>
	/// <returns>Whether the connection is being carried through a volunteer.</returns>
	private bool TryStartRelayedConnection(RelayConnectionPlan plan, out int loopbackPort)
	{
		loopbackPort = 0;
		if (!plan.IsUsable) return false;

		RelayPathMux? mux = RelayConnectionFactory.TryBuild(plan, isAuthority: false, out string failure);
		if (mux == null)
		{
			GameLog.LogToConsole($"[Relay] This join offered {plan.Offers.Count} volunteer(s) and none of them could be opened ({failure}), so it takes the direct path.");
			return false;
		}

		var shim = new RelayUdpShim(mux, innerPort: 0);
		if (!shim.Start(out string shimFailure))
		{
			shim.Dispose();
			GameLog.LogError($"[Relay] The connection could not be carried through a volunteer ({shimFailure}), so it takes the direct path.");
			return false;
		}

		StopRelayedConnection();

		_relayShim = shim;
		_relayedConnectionPlan = plan;
		_relayedVolunteerIds.Clear();
		for (int index = 0; index < plan.Offers.Count; index++) _relayedVolunteerIds.Add(plan.Offers[index].Candidate.RelayId);

		_lastRelayedPathSummary = RelayPathSummary.Empty;
		_relayedPathStatementMilliseconds = 0;

		loopbackPort = shim.Port;
		GameLog.LogToConsole($"[Relay] This connection is carried through {string.Join(", ", _relayedVolunteerIds)} on loopback port {loopbackPort}.");
		return true;
	}

	/// <summary>
	///     Gives up every path of the current connection and the loopback port it spoke to. It is what leaving, quitting or a
	///     join that fell back to the direct path does, so a volunteer this screen no longer uses is closed at once.
	/// </summary>
	private void StopRelayedConnection()
	{
		_lastRelayedPathSummary = RelayPathSummary.Empty;
		_relayedConnectionPlan = null;
		_relayedVolunteerIds.Clear();

		RelayUdpShim? shim = _relayShim;
		_relayShim = null;
		shim?.Dispose();
	}

	/// <summary>
	///     Reads the connection's paths once a second and writes every change to the log, so a capture says which path carried
	///     the peer, what the path it left measured and whether a path was answered again. It runs while the connection exists
	///     rather than while any screen is open, because the changes that matter happen during the match, and it runs on the
	///     frame the rest of this screen runs on so the line it writes is never printed from a thread Godot does not own.
	/// </summary>
	private void TickRelayedPathStatements()
	{
		RelayUdpShim? shim = _relayShim;
		if (shim == null) return;

		long now = System.Environment.TickCount64;
		if (now - _relayedPathStatementMilliseconds < RelayedPathStatementIntervalMilliseconds) return;

		_relayedPathStatementMilliseconds = now;

		RelayPathSummary current = shim.Summary;
		string change = RelayPathChanges.Describe(_lastRelayedPathSummary, current);
		_lastRelayedPathSummary = current;
		if (change.Length == 0) return;

		GameLog.LogToConsole($"[Relay] Path change on loopback port {shim.Port}: {change}.");
	}

	/// <summary>
	///     Tells this lobby's authority which volunteers one joining connection may be carried through. The host is told the
	///     same ordered list the player was, so both open identical paths; a host that was told nothing leaves the join on the
	///     direct path, which is what a lobby server that pools no volunteers answers.
	/// </summary>
	/// <param name="plan">The plan the lobby server stated for this join.</param>
	private void SendRelayedPeerToAuthority(RelayConnectionPlan plan)
	{
		if (!IsHost || !plan.IsUsable) return;

		bool sent = _lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.SetRelayedPeer,
			Text = plan.Serialize()
		}) ?? false;

		if (!sent)
		{
			GameLog.LogError("[Relay] The volunteers for a joining connection could not be stated to this lobby's authority, so that join takes the direct path.");
			return;
		}

		GameLog.LogToConsole($"[Relay] This lobby's authority was told to carry a join through {plan.Offers.Count} volunteer(s): {string.Join(", ", plan.Offers.Select(offer => offer.Candidate.RelayId))}.");
	}

	/// <summary>
	///     Reads the volunteers the lobby server wrote to this host for one joining connection. The message names the join it
	///     belongs to, and a host that has already left the lobby it was told about ignores it.
	/// </summary>
	/// <param name="json">The message the lobby server sent.</param>
	private void HandleHostRelayMessage(string json)
	{
		try
		{
			using var document = System.Text.Json.JsonDocument.Parse(json);
			System.Text.Json.JsonElement root = document.RootElement;
			if (!root.TryGetProperty("Token", out System.Text.Json.JsonElement tokenElement)) return;

			string joinTicket = tokenElement.GetString() ?? string.Empty;
			if (string.IsNullOrEmpty(joinTicket)) return;

			var candidates = new List<RelayCandidateMessage>(RelayOffers.MaximumOffers);
			if (root.TryGetProperty("Relays", out System.Text.Json.JsonElement relaysElement) && relaysElement.ValueKind == System.Text.Json.JsonValueKind.Array)
			{
				List<RelayCandidateMessage>? parsed = System.Text.Json.JsonSerializer.Deserialize<List<RelayCandidateMessage>>(relaysElement.GetRawText(), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
				if (parsed != null) candidates.AddRange(parsed);
			}

			RelayConnectionPlan plan = RelayConnectionPlan.From(joinTicket, candidates, GameSettings.RelayDuplication, GameSettings.RelayDuplicationBudgetBytesPerSecond);
			Callable.From(() => SendRelayedPeerToAuthority(plan)).CallDeferred();
		}
		catch (System.Text.Json.JsonException exception)
		{
			GameLog.LogError($"[Relay] The volunteers a lobby server stated could not be read: {exception.Message}");
		}
	}
}
