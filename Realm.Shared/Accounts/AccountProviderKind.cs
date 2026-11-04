namespace Realm.Shared.Accounts;

/// <summary>
///     The identity provider an account link was created through. An account only ever exists because the authority itself
///     verified a provider identity, so this names the one check that happened. A player who cannot complete it plays unverified
///     under their own name instead of holding an account.
/// </summary>
public enum AccountProviderKind
{
	Steam = 1
}

public static class AccountProviderNames
{
	public const string Steam = "steam";

	/// <summary>
	///     The providers an account can be created through. Steam is the only one because it is the only identity the authority
	///     can verify without a browser, which is what keeps sign-in reachable from networks where Valve's web pages are not.
	/// </summary>
	public static readonly string[] All = { Steam };

	public static bool TryParse(string? value, out AccountProviderKind provider)
	{
		provider = default;
		if (string.IsNullOrWhiteSpace(value)) return false;

		if (!string.Equals(value.Trim(), Steam, System.StringComparison.OrdinalIgnoreCase)) return false;

		provider = AccountProviderKind.Steam;
		return true;
	}

	public static string ToWireName(this AccountProviderKind provider)
	{
		return provider switch
		{
			AccountProviderKind.Steam => Steam,
			_ => Steam
		};
	}
}
