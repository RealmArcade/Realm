using System;
using System.Collections.Generic;
using System.Text.Json;
using Realm.Shared.Network.Relay;

namespace Realm.Shared.Reachability;

/// <summary>
///     What a lobby server answers when a player asks to join a lobby: the address the lobby has always stated, and the
///     list of endpoints it can be reached at with the family each one belongs to.
/// </summary>
/// <param name="HostAddress">The address the lobby has always stated, which is its IPv4 endpoint.</param>
/// <param name="HostPort">The port the lobby has always stated.</param>
/// <param name="LocalAddress">The address the host holds inside its own network, when the answer states one.</param>
/// <param name="Endpoints">The endpoints the lobby is reachable at, which is empty for a server that predates them.</param>
/// <param name="PunchPort">
///     The UDP port on the node that read the answer that this screen reports its own endpoint to, or zero when the node
///     stated none.
/// </param>
/// <param name="PunchToken">The check the report belongs to, or empty when the node stated none.</param>
/// <param name="ReachabilityClass">The class the node states for this lobby, or empty when it stated none.</param>
/// <param name="Capabilities">
///     What the node that answered can do, or nothing when it stated no capabilities, which is a node older than this build.
/// </param>
/// <param name="Relays">
///     The volunteers this connection may be carried through, in the order the lobby's host was told the same list. Empty
///     when the lobby accepts no relays or the node pools none, which is a join that takes the direct path it always took.
/// </param>
public readonly record struct LobbyJoinAnswer(string HostAddress, int HostPort, string? LocalAddress, IReadOnlyList<LobbyEndpointInfo> Endpoints, int PunchPort = 0, string PunchToken = "", RegistryCapabilities? Capabilities = null, string ReachabilityClass = "", IReadOnlyList<RelayCandidateMessage>? Relays = null)
{
	/// <summary>Whether this lobby is admitted only through the volunteers the answer offers.</summary>
	public bool IsRelayed => ReachabilityClasses.IsRelayed(ReachabilityClass);

	/// <summary>
	///     The endpoints to choose from, in the shape every caller wants them: the list the answer carried, or the single
	///     endpoint the two older fields state. A server from before endpoints could be listed names exactly one, and it is
	///     the one to dial, so the absence of a list is never treated as "this lobby cannot be reached". A lobby carried
	///     through volunteers is the one exception, and states no dialable endpoint at all.
	/// </summary>
	/// <returns>The endpoints to choose from.</returns>
	public IReadOnlyList<LobbyEndpointInfo> EndpointsOrStatedAddress()
	{
		if (Endpoints.Count > 0) return Endpoints;

		// A lobby carried through volunteers has no address a player may dial. The address it states is where its host is,
		// which is exactly the address nothing reached, so falling back to it would spend an attempt on a host already
		// measured unreachable.
		if (IsRelayed) return Endpoints;

		if (HostAddress.Length == 0 || HostPort <= 0) return Endpoints;

		return new[]
		{
			new LobbyEndpointInfo(HostAddress, HostPort, LobbyAddresses.FamilyName(LobbyAddresses.FamilyOf(HostAddress)), true, HostAddress, HostPort)
		};
	}
}

/// <summary>
///     Reads the answer a lobby server gives to a join request. It is here, rather than inside the screen that joins,
///     because the shapes a server can answer in - one address from an older node, an endpoint list from a node that holds
///     several, or a refusal - are a contract with every lobby server in the field, and a contract belongs where a test can
///     read it without a lobby.
/// </summary>
public static class LobbyJoinAnswers
{
	/// <summary>The options the endpoint list is read with: a lobby server writes PascalCase, an older one camelCase.</summary>
	private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

	/// <summary>
	///     Reads a join answer.
	/// </summary>
	/// <param name="json">The body the lobby server answered with.</param>
	/// <returns>The answer, or nothing when the body is not one.</returns>
	public static LobbyJoinAnswer? TryParse(string? json)
	{
		if (string.IsNullOrWhiteSpace(json)) return null;

		try
		{
			using var document = JsonDocument.Parse(json);
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object) return null;

			string hostAddress = ReadString(root, "hostIP", "hostIp", "HostIP", "HostIp");
			int hostPort = ReadPort(root);
			string? localAddress = ReadStringOrNull(root, "localIP", "localIp", "LocalIP", "LocalIp");
			List<LobbyEndpointInfo> endpoints = ReadEndpoints(root);

			if (hostAddress.Length == 0 && endpoints.Count == 0) return null;

			// The reporting ticket belongs to the node that answered, so it is read from the same body: a screen does not need
			// it to dial a lobby nothing is in front of, which is why both members are optional.
			int punchPort = ReadNumber(root, "punchPort", "PunchPort");
			string punchToken = ReadString(root, "punchToken", "PunchToken");
			RegistryCapabilities? capabilities = ReadCapabilities(root);
			string reachabilityClass = ReadString(root, "reachabilityClass", "ReachabilityClass");
			List<RelayCandidateMessage> relays = ReadRelays(root);

			return new LobbyJoinAnswer(hostAddress, hostPort, localAddress, endpoints, punchPort, punchToken, capabilities, reachabilityClass, relays);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>
	///     Reads the endpoint list, keeping the entries that state an address and a port so a malformed entry cannot become
	///     an endpoint a caller tries to dial.
	/// </summary>
	private static List<LobbyEndpointInfo> ReadEndpoints(JsonElement root)
	{
		var endpoints = new List<LobbyEndpointInfo>(2);
		foreach (string propertyName in new[] { "endpoints", "Endpoints" })
		{
			if (!root.TryGetProperty(propertyName, out JsonElement list) || list.ValueKind != JsonValueKind.Array) continue;

			List<LobbyEndpointInfo>? parsed = JsonSerializer.Deserialize<List<LobbyEndpointInfo>>(list.GetRawText(), ReadOptions);
			if (parsed == null) break;

			for (int index = 0; index < parsed.Count; index++)
			{
				LobbyEndpointInfo endpoint = parsed[index];
				if (endpoint == null) continue;
				if (string.IsNullOrWhiteSpace(endpoint.Address) || endpoint.Port is <= 0 or > 65535) continue;

				endpoints.Add(endpoint);
			}

			break;
		}

		return endpoints;
	}

	/// <summary>
	///     Reads what the node that answered can do. A node older than the capabilities says nothing, and is read as stating
	///     none rather than as stating everything.
	/// </summary>
	/// <param name="root">The answer's root object.</param>
	/// <returns>The capabilities the node stated, or null when it stated none.</returns>
	private static RegistryCapabilities? ReadCapabilities(JsonElement root)
	{
		foreach (string propertyName in new[] { "capabilities", "Capabilities" })
		{
			if (!root.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.Object) continue;

			try
			{
				return JsonSerializer.Deserialize<RegistryCapabilities>(value.GetRawText(), ReadOptions);
			}
			catch (JsonException)
			{
				return null;
			}
		}

		return null;
	}

	/// <summary>
	///     Reads the volunteers the answer offers. A node that offers none says nothing, and the list is then empty rather
	///     than an error: a join to such a lobby is the direct join it has always been.
	/// </summary>
	/// <param name="root">The answer's root object.</param>
	/// <returns>The candidates, in the order the node offered them.</returns>
	private static List<RelayCandidateMessage> ReadRelays(JsonElement root)
	{
		var relays = new List<RelayCandidateMessage>(RelayOffers.MaximumOffers);
		foreach (string propertyName in new[] { "relays", "Relays" })
		{
			if (!root.TryGetProperty(propertyName, out JsonElement list) || list.ValueKind != JsonValueKind.Array) continue;

			List<RelayCandidateMessage>? parsed = JsonSerializer.Deserialize<List<RelayCandidateMessage>>(list.GetRawText(), ReadOptions);
			if (parsed == null) break;

			for (int index = 0; index < parsed.Count; index++)
			{
				RelayCandidateMessage? candidate = parsed[index];
				if (candidate == null || !candidate.IsUsable) continue;

				relays.Add(candidate);
			}

			break;
		}

		return relays;
	}

	private static string ReadString(JsonElement root, params string[] propertyNames)
	{
		foreach (string propertyName in propertyNames)
		{
			if (!root.TryGetProperty(propertyName, out JsonElement value)) continue;
			if (value.ValueKind != JsonValueKind.String) continue;

			string text = (value.GetString() ?? string.Empty).Trim();
			if (text.Length > 0) return text;
		}

		return string.Empty;
	}

	private static string? ReadStringOrNull(JsonElement root, params string[] propertyNames)
	{
		string text = ReadString(root, propertyNames);
		return text.Length == 0 ? null : text;
	}

	private static int ReadNumber(JsonElement root, params string[] propertyNames)
	{
		foreach (string propertyName in propertyNames)
		{
			if (!root.TryGetProperty(propertyName, out JsonElement value)) continue;
			if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)) return number;
			if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out int parsed)) return parsed;
		}

		return 0;
	}

	private static int ReadPort(JsonElement root)
	{
		foreach (string propertyName in new[] { "hostPort", "HostPort" })
		{
			if (!root.TryGetProperty(propertyName, out JsonElement value)) continue;
			if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int port) && port is > 0 and <= 65535) return port;
			if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out int parsed) && parsed is > 0 and <= 65535) return parsed;
		}

		return 0;
	}
}
