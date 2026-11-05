using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace Realm.AdminServer.Accounts;

/// <summary>
///     The one node that owns account and DisplayName writes, and the provider secret it needs to verify an identity itself.
///     Only key names live in the committed configuration template; the values come from the environment or an uncommitted
///     settings file.
/// </summary>
public class AccountAuthorityOptions
{
	public bool IsAuthority { get; init; } = true;

	public string AuthorityUrl { get; init; } = string.Empty;

	/// <summary>
	///     How long a session stays accepted, or <c>0</c> for a session that never expires. A player is signed in until they sign
	///     out or the authority revokes the session, and every action that outlives the sign-in asks for the provider again.
	/// </summary>
	public int SessionLifetimeHours { get; init; }

	/// <summary>
	///     How recently the account holder must have proved their identity for a change that outlives the session, such as a
	///     rename. The proof is a fresh Steam ticket, which costs the player nothing and opens no window, so the window only
	///     decides how long one sign-in keeps authorising such changes.
	/// </summary>
	public int SensitiveActionWindowMinutes { get; init; } = 15;

	public string SigningPrivateKeyBase64 { get; init; } = string.Empty;

	public HashSet<string> AuthorityPublicKeys { get; init; } = new(StringComparer.OrdinalIgnoreCase);

	public SteamProviderOptions Steam { get; init; } = new();

	public static AccountAuthorityOptions FromConfiguration(IConfiguration configuration, string? selfUrl, bool hasConfiguredPeers)
	{
		string authorityUrl = configuration.GetValue<string>("AccountAuthorityUrl")?.Trim() ?? string.Empty;
		bool? declaredAuthority = configuration.GetValue<bool?>("AccountAuthority");

		// Being the authority is never inferred on a node that declares peers: two nodes that each guessed they own account
		// writes would fork DisplayName uniqueness. A single standalone node keeps working without any setting.
		bool isAuthority = declaredAuthority ?? (!hasConfiguredPeers && (authorityUrl.Length == 0 || IsSameNode(authorityUrl, selfUrl)));

		var authorityPublicKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string key in configuration.GetSection("AccountAuthorityPublicKeys").Get<List<string>>() ?? new List<string>())
		{
			if (!string.IsNullOrWhiteSpace(key)) authorityPublicKeys.Add(key.Trim());
		}

		var steamAppIds = new List<uint>();
		foreach (long appId in configuration.GetSection("Steam:AppIds").Get<List<long>>() ?? new List<long>())
		{
			if (appId > 0 && appId <= uint.MaxValue) steamAppIds.Add((uint)appId);
		}

		return new AccountAuthorityOptions
		{
			IsAuthority = isAuthority,
			AuthorityUrl = authorityUrl,
			SessionLifetimeHours = Math.Clamp(configuration.GetValue<int?>("AccountSessionLifetimeHours") ?? 0, 0, 24 * 365),
			SensitiveActionWindowMinutes = Math.Clamp(configuration.GetValue<int?>("AccountSensitiveActionWindowMinutes") ?? 15, 1, 24 * 60),
			SigningPrivateKeyBase64 = configuration.GetValue<string>("AccountAuthoritySigningPrivateKey")?.Trim() ?? string.Empty,
			AuthorityPublicKeys = authorityPublicKeys,
			Steam = new SteamProviderOptions
			{
				WebApiKey = configuration.GetValue<string>("Steam:WebApiKey")?.Trim() ?? string.Empty,
				AppIds = steamAppIds.ToArray()
			}
		};
	}

	private static bool IsSameNode(string authorityUrl, string? selfUrl)
	{
		if (string.IsNullOrWhiteSpace(selfUrl)) return false;
		return string.Equals(authorityUrl.TrimEnd('/'), selfUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
	}
}

/// <summary>
///     The publisher key and the applications this authority accepts tickets for. A ticket is minted for exactly one Steam
///     application, so the list is what stops a ticket for an unrelated game from being exchanged here.
/// </summary>
public class SteamProviderOptions
{
	public string WebApiKey { get; init; } = string.Empty;

	public uint[] AppIds { get; init; } = Array.Empty<uint>();

	public bool IsConfigured => WebApiKey.Length > 0 && AppIds.Length > 0;

	public bool AcceptsAppId(uint appId)
	{
		return Array.IndexOf(AppIds, appId) >= 0;
	}

	public IEnumerable<string> MissingSettings()
	{
		if (WebApiKey.Length == 0) yield return "Steam:WebApiKey";
		if (AppIds.Length == 0) yield return "Steam:AppIds";
	}
}
