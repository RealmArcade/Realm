using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Realm.AdminServer.Accounts;

/// <summary>
///     The Discord authorization-code + PKCE exchange. The identity is read from Discord's own user endpoint with the access
///     token Discord issued for this client, so nothing the browser sent is trusted as an identity, and the access token is
///     discarded once the permanent user id has been read.
/// </summary>
public class DiscordOAuthService
{
	private const string AuthorizeEndpoint = "https://discord.com/oauth2/authorize";

	private const string TokenEndpoint = "https://discord.com/api/oauth2/token";

	private const string CurrentUserEndpoint = "https://discord.com/api/users/@me";

	private readonly AccountAuthorityOptions _options;
	private readonly IHttpClientFactory _httpClientFactory;

	public DiscordOAuthService(AccountAuthorityOptions options, IHttpClientFactory httpClientFactory)
	{
		_options = options;
		_httpClientFactory = httpClientFactory;
	}

	public string BuildAuthorizationUrl(AuthFlowRecord flow)
	{
		string challenge = AuthFlowService.CreatePkceChallenge(flow.CodeVerifier);
		return $"{AuthorizeEndpoint}?response_type=code"
		       + $"&client_id={Uri.EscapeDataString(_options.Discord.ClientId)}"
		       + $"&redirect_uri={Uri.EscapeDataString(_options.Discord.RedirectUri)}"
		       + "&scope=identify"
		       + $"&state={Uri.EscapeDataString(flow.State)}"
		       + $"&code_challenge={Uri.EscapeDataString(challenge)}"
		       + "&code_challenge_method=S256";
	}

	/// <summary>
	///     Exchanges the authorization code, then reads the permanent Discord user id and the current display name. The name is
	///     only ever used to prefill a registration form; the id is the only thing the account links on.
	/// </summary>
	public async Task<ProviderIdentity?> ExchangeCodeAsync(string code, AuthFlowRecord flow, CancellationToken cancellationToken)
	{
		if (!_options.Discord.IsConfigured || string.IsNullOrWhiteSpace(code)) return null;

		using HttpClient client = _httpClientFactory.CreateClient();
		using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
		{
			Content = new FormUrlEncodedContent(new Dictionary<string, string>
			{
				{ "grant_type", "authorization_code" },
				{ "client_id", _options.Discord.ClientId },
				{ "client_secret", _options.Discord.ClientSecret },
				{ "redirect_uri", _options.Discord.RedirectUri },
				{ "code", code },
				{ "code_verifier", flow.CodeVerifier }
			})
		};

		using HttpResponseMessage tokenResponse = await client.SendAsync(tokenRequest, cancellationToken).ConfigureAwait(false);
		if (!tokenResponse.IsSuccessStatusCode) return null;

		string tokenJson = await tokenResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		string? accessToken = ReadStringProperty(tokenJson, "access_token");
		if (string.IsNullOrEmpty(accessToken)) return null;

		using var userRequest = new HttpRequestMessage(HttpMethod.Get, CurrentUserEndpoint);
		userRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
		using HttpResponseMessage userResponse = await client.SendAsync(userRequest, cancellationToken).ConfigureAwait(false);
		if (!userResponse.IsSuccessStatusCode) return null;

		string userJson = await userResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		string? userId = ReadStringProperty(userJson, "id");
		if (string.IsNullOrEmpty(userId)) return null;

		string? displayName = ReadStringProperty(userJson, "global_name");
		if (string.IsNullOrEmpty(displayName)) displayName = ReadStringProperty(userJson, "username");

		return new ProviderIdentity(userId, displayName ?? string.Empty);
	}

	private static string? ReadStringProperty(string json, string propertyName)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		if (!document.RootElement.TryGetProperty(propertyName, out JsonElement element)) return null;
		return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
	}
}
