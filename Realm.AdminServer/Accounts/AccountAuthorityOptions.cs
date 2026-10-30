using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace Realm.AdminServer.Accounts;

/// <summary>
///     The one node that owns account and DisplayName writes, and the provider secrets it needs to verify an identity itself.
///     Only key names live in the committed configuration template; the values come from the environment or an uncommitted
///     settings file.
/// </summary>
public class AccountAuthorityOptions
{
	public bool IsAuthority { get; init; } = true;

	public string AuthorityUrl { get; init; } = string.Empty;

	public int SessionLifetimeHours { get; init; } = 720;

	public int ProviderFlowTimeoutSeconds { get; init; } = 600;

	public string SigningPrivateKeyBase64 { get; init; } = string.Empty;

	public HashSet<string> AuthorityPublicKeys { get; init; } = new(StringComparer.OrdinalIgnoreCase);

	public DiscordProviderOptions Discord { get; init; } = new();

	public SteamProviderOptions Steam { get; init; } = new();

	public static AccountAuthorityOptions FromConfiguration(IConfiguration configuration, string? selfUrl)
	{
		string authorityUrl = configuration.GetValue<string>("AccountAuthorityUrl")?.Trim() ?? string.Empty;
		bool? declaredAuthority = configuration.GetValue<bool?>("AccountAuthority");
		bool isAuthority = declaredAuthority ?? (authorityUrl.Length == 0 || IsSameNode(authorityUrl, selfUrl));

		var authorityPublicKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string key in configuration.GetSection("AccountAuthorityPublicKeys").Get<List<string>>() ?? new List<string>())
		{
			if (!string.IsNullOrWhiteSpace(key)) authorityPublicKeys.Add(key.Trim());
		}

		return new AccountAuthorityOptions
		{
			IsAuthority = isAuthority,
			AuthorityUrl = authorityUrl,
			SessionLifetimeHours = Math.Clamp(configuration.GetValue<int?>("AccountSessionLifetimeHours") ?? 720, 1, 24 * 365),
			ProviderFlowTimeoutSeconds = Math.Clamp(configuration.GetValue<int?>("AccountProviderFlowTimeoutSeconds") ?? 600, 60, 3600),
			SigningPrivateKeyBase64 = configuration.GetValue<string>("AccountAuthoritySigningPrivateKey")?.Trim() ?? string.Empty,
			AuthorityPublicKeys = authorityPublicKeys,
			Discord = new DiscordProviderOptions
			{
				ClientId = configuration.GetValue<string>("Discord:ClientId")?.Trim() ?? string.Empty,
				ClientSecret = configuration.GetValue<string>("Discord:ClientSecret")?.Trim() ?? string.Empty,
				RedirectUri = configuration.GetValue<string>("Discord:RedirectUri")?.Trim() ?? string.Empty
			},
			Steam = new SteamProviderOptions
			{
				WebApiKey = configuration.GetValue<string>("Steam:WebApiKey")?.Trim() ?? string.Empty,
				Realm = configuration.GetValue<string>("Steam:Realm")?.Trim() ?? string.Empty,
				ReturnUrl = configuration.GetValue<string>("Steam:ReturnUrl")?.Trim() ?? string.Empty
			}
		};
	}

	private static bool IsSameNode(string authorityUrl, string? selfUrl)
	{
		if (string.IsNullOrWhiteSpace(selfUrl)) return false;
		return string.Equals(authorityUrl.TrimEnd('/'), selfUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
	}
}

public class DiscordProviderOptions
{
	public string ClientId { get; init; } = string.Empty;

	public string ClientSecret { get; init; } = string.Empty;

	public string RedirectUri { get; init; } = string.Empty;

	public bool IsConfigured => ClientId.Length > 0 && ClientSecret.Length > 0 && RedirectUri.Length > 0;

	public IEnumerable<string> MissingSettings()
	{
		if (ClientId.Length == 0) yield return "Discord:ClientId";
		if (ClientSecret.Length == 0) yield return "Discord:ClientSecret";
		if (RedirectUri.Length == 0) yield return "Discord:RedirectUri";
	}
}

public class SteamProviderOptions
{
	public string WebApiKey { get; init; } = string.Empty;

	public string Realm { get; init; } = string.Empty;

	public string ReturnUrl { get; init; } = string.Empty;

	public bool IsConfigured => Realm.Length > 0 && ReturnUrl.Length > 0;

	/// <summary>
	///     The settings a Steam sign-in cannot run without. <c>WebApiKey</c> is not among them: it only prefills the registration
	///     form with the player's current persona name, and the identity comes from the verified assertion.
	/// </summary>
	public IEnumerable<string> MissingSettings()
	{
		if (Realm.Length == 0) yield return "Steam:Realm";
		if (ReturnUrl.Length == 0) yield return "Steam:ReturnUrl";
	}
}
