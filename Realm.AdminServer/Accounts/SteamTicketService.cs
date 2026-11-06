using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Realm.Shared.Accounts;

namespace Realm.AdminServer.Accounts;

/// <summary>
///     Exchanges a Steam auth session ticket for the SteamID64 it belongs to. The player's machine mints the ticket through
///     Steamworks and sends it here, and only this node holds the publisher key that Valve accepts, so verification never needs
///     the player to reach a Valve web page and never needs a browser redirect.
/// </summary>
public class SteamTicketService
{
	private const string AuthenticateUserTicketUrl = "https://api.steampowered.com/ISteamUserAuth/AuthenticateUserTicket/v1/";

	private const int MaximumTicketHexLength = 2048;

	private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

	private readonly SteamProviderOptions _options;

	public SteamTicketService(AccountAuthorityOptions options)
	{
		_options = options.Steam;
	}

	public bool IsConfigured => _options.IsConfigured;

	/// <summary>
	///     The SteamID64 behind a ticket, or <c>null</c> when the ticket is not one Valve accepts for an application this
	///     authority configured. A refusal is the same for every reason on purpose: the caller cannot tell a forged ticket from
	///     an expired one from a ticket for another game, and neither can a player probing the endpoint.
	/// </summary>
	public async Task<string?> ResolveSteamIdAsync(uint appId, string? ticket, CancellationToken cancellationToken)
	{
		if (!IsConfigured) return null;
		if (!_options.AcceptsAppId(appId)) return null;
		if (string.IsNullOrWhiteSpace(ticket)) return null;

		if (ticket.Length > MaximumTicketHexLength) return null;

		string requestUrl = AuthenticateUserTicketUrl
		                    + $"?key={Uri.EscapeDataString(_options.WebApiKey)}"
		                    + $"&appid={appId.ToString(CultureInfo.InvariantCulture)}"
		                    + $"&ticket={Uri.EscapeDataString(ticket.Trim())}"
		                    + $"&identity={Uri.EscapeDataString(SteamAuthTicket.Identity)}";

		try
		{
			using HttpResponseMessage response = await HttpClient.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode) return null;

			string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			return ReadSteamId(json);
		}
		catch (HttpRequestException)
		{
			return null;
		}
		catch (TaskCanceledException)
		{
			return null;
		}
	}

	private static string? ReadSteamId(string json)
	{
		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			if (!document.RootElement.TryGetProperty("response", out JsonElement response)) return null;
			if (!response.TryGetProperty("params", out JsonElement parameters)) return null;

			if (!parameters.TryGetProperty("result", out JsonElement result)) return null;
			if (!string.Equals(result.GetString(), "OK", StringComparison.Ordinal)) return null;

			if (!parameters.TryGetProperty("steamid", out JsonElement steamId)) return null;
			string? steamIdText = steamId.GetString();
			return string.IsNullOrWhiteSpace(steamIdText) ? null : steamIdText.Trim();
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
