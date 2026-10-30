using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Realm.AdminServer.Accounts;

public readonly record struct ProviderIdentity(string ProviderAccountToken, string DisplayName);

/// <summary>
///     Steam does not offer OAuth 2.0, so it is validated as what it is: an OpenID 2.0 assertion. The claimed id must be a
///     SteamID64 under Steam's own claimed-id prefix, the return URL and realm must be the ones this flow recorded, and Steam
///     itself must answer <c>check_authentication</c> with <c>is_valid:true</c>. Nothing the browser claims about its identity is
///     accepted on its own.
/// </summary>
public class SteamOpenIdService
{
	private const string OpenIdEndpoint = "https://steamcommunity.com/openid/login";

	private const string ClaimedIdPrefix = "https://steamcommunity.com/openid/id/";

	private readonly AccountAuthorityOptions _options;
	private readonly IHttpClientFactory _httpClientFactory;

	public SteamOpenIdService(AccountAuthorityOptions options, IHttpClientFactory httpClientFactory)
	{
		_options = options;
		_httpClientFactory = httpClientFactory;
	}

	public string BuildAuthorizationUrl(AuthFlowRecord flow)
	{
		return $"{OpenIdEndpoint}?openid.ns={Uri.EscapeDataString("http://specs.openid.net/auth/2.0")}"
		       + "&openid.mode=checkid_setup"
		       + $"&openid.return_to={Uri.EscapeDataString(flow.ProviderReturnUrl)}"
		       + $"&openid.realm={Uri.EscapeDataString(_options.Steam.Realm)}"
		       + $"&openid.identity={Uri.EscapeDataString("http://specs.openid.net/auth/2.0/identifier_select")}"
		       + $"&openid.claimed_id={Uri.EscapeDataString("http://specs.openid.net/auth/2.0/identifier_select")}";
	}

	/// <summary>
	///     Verifies the assertion Steam redirected back with and returns the SteamID64, with the persona name when the API key
	///     lets it be read for prefill purposes. The persona name is never stored or used as identity.
	/// </summary>
	public async Task<ProviderIdentity?> VerifyAssertionAsync(IReadOnlyDictionary<string, string> assertion, AuthFlowRecord flow, CancellationToken cancellationToken)
	{
		if (!_options.Steam.IsConfigured || assertion.Count == 0) return null;
		if (!assertion.TryGetValue("openid.mode", out string? mode) || !string.Equals(mode, "id_res", StringComparison.Ordinal)) return null;

		if (!assertion.TryGetValue("openid.claimed_id", out string? claimedId) || !claimedId.StartsWith(ClaimedIdPrefix, StringComparison.Ordinal)) return null;
		string steamId64 = claimedId[ClaimedIdPrefix.Length..];
		if (steamId64.Length == 0 || !long.TryParse(steamId64, out _)) return null;

		if (!assertion.TryGetValue("openid.return_to", out string? returnedUrl) || !string.Equals(returnedUrl, flow.ProviderReturnUrl, StringComparison.Ordinal)) return null;
		if (!assertion.TryGetValue("openid.realm", out string? realm) || !string.Equals(realm, _options.Steam.Realm, StringComparison.Ordinal)) return null;

		var verification = new Dictionary<string, string>(assertion, StringComparer.Ordinal)
		{
			["openid.mode"] = "check_authentication"
		};

		using HttpClient client = _httpClientFactory.CreateClient();
		using HttpResponseMessage response = await client.PostAsync(OpenIdEndpoint, new FormUrlEncodedContent(verification), cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode) return null;

		string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		if (!body.Contains("is_valid:true", StringComparison.Ordinal)) return null;

		string personaName = await ReadPersonaNameAsync(client, steamId64, cancellationToken).ConfigureAwait(false) ?? string.Empty;
		return new ProviderIdentity(steamId64, personaName);
	}

	private async Task<string?> ReadPersonaNameAsync(HttpClient client, string steamId64, CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(_options.Steam.WebApiKey)) return null;

		string url = "https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/"
		             + $"?key={Uri.EscapeDataString(_options.Steam.WebApiKey)}"
		             + $"&steamids={Uri.EscapeDataString(steamId64)}";

		try
		{
			string json = await client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
			using JsonDocument document = JsonDocument.Parse(json);
			if (!document.RootElement.TryGetProperty("response", out JsonElement response)) return null;
			if (!response.TryGetProperty("players", out JsonElement players) || players.ValueKind != JsonValueKind.Array || players.GetArrayLength() == 0) return null;
			return players[0].TryGetProperty("personaname", out JsonElement name) ? name.GetString() : null;
		}
		catch (HttpRequestException)
		{
			return null;
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
