using System;
using System.Collections.Generic;
using System.Text.Json;

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
public readonly record struct LobbyJoinAnswer(string HostAddress, int HostPort, string? LocalAddress, IReadOnlyList<LobbyEndpointInfo> Endpoints, int PunchPort = 0, string PunchToken = "")
{
	/// <summary>
	///     The endpoints to choose from, in the shape every caller wants them: the list the answer carried, or the single
	///     endpoint the two older fields state. A server from before endpoints could be listed names exactly one, and it is
	///     the one to dial, so the absence of a list is never treated as "this lobby cannot be reached".
	/// </summary>
	/// <returns>The endpoints to choose from.</returns>
	public IReadOnlyList<LobbyEndpointInfo> EndpointsOrStatedAddress()
	{
		if (Endpoints.Count > 0) return Endpoints;
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

			return new LobbyJoinAnswer(hostAddress, hostPort, localAddress, endpoints, punchPort, punchToken);
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
