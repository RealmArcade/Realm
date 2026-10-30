namespace Realm.Shared.Accounts;

/// <summary>
///     The identity provider an account link was created through. The wire form is the lower-case name, so a provider can be
///     named in a URL, a log line and an HTTP payload without a second spelling.
/// </summary>
public enum AccountProviderKind
{
	BasicAuth = 1,

	Discord = 2,

	Steam = 3
}

public static class AccountProviderNames
{
	public const string BasicAuth = "basicauth";

	public const string Discord = "discord";

	public const string Steam = "steam";

	/// <summary>
	///     Providers whose sign-in the game client can hand to a browser. BasicAuth is answered in the game itself.
	/// </summary>
	public static readonly string[] RecommendedProviders = { Discord, Steam };

	public static bool TryParse(string? value, out AccountProviderKind provider)
	{
		provider = default;
		if (string.IsNullOrWhiteSpace(value)) return false;

		switch (value.Trim().ToLowerInvariant())
		{
			case BasicAuth:
				provider = AccountProviderKind.BasicAuth;
				return true;
			case Discord:
				provider = AccountProviderKind.Discord;
				return true;
			case Steam:
				provider = AccountProviderKind.Steam;
				return true;
			default:
				return false;
		}
	}

	public static string ToWireName(this AccountProviderKind provider)
	{
		return provider switch
		{
			AccountProviderKind.Discord => Discord,
			AccountProviderKind.Steam => Steam,
			_ => BasicAuth
		};
	}
}
