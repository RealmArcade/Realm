using System;

namespace Realm.Shared.Accounts;

/// <summary>
///     The account a session belongs to. It is safe to publish anywhere: an account holds an id, a DisplayName and the kinds
///     of provider linked to it, and nothing else.
/// </summary>
public class AccountProfileDto
{
	public long AccountId { get; set; }

	public string DisplayName { get; set; } = string.Empty;

	public string[] LinkedProviders { get; set; } = Array.Empty<string>();

	public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
///     What the account authority answers a successful sign-in, registration or session validation with. The token is the only
///     copy of the bearer secret that exists; the authority keeps a hash.
/// </summary>
public class AccountSessionDto
{
	public string SessionToken { get; set; } = string.Empty;

	public long AccountId { get; set; }

	public string DisplayName { get; set; } = string.Empty;

	public string[] LinkedProviders { get; set; } = Array.Empty<string>();

	/// <summary>
	///     When the session stops being accepted, or <c>null</c> when it stays valid until it is revoked. A client that holds one
	///     keeps it across launches, so a null expiry is what an ordinary player sees: signed in until they sign out.
	/// </summary>
	public DateTime? ExpiresAtUtc { get; set; }
}

public class AccountErrorDto
{
	public string Code { get; set; } = string.Empty;

	public string Message { get; set; } = string.Empty;

	public static AccountErrorDto Of(string code, string message)
	{
		return new AccountErrorDto { Code = code, Message = message };
	}
}

/// <summary>
///     A Steam sign-in the client already holds a ticket for. Steamworks mints the ticket on the player's own machine, so no
///     browser is opened and no Valve web page has to be reachable from the player's network; the authority is the only party
///     that exchanges it for a SteamID64, using the publisher key it holds. The suggested name is only read when the ticket
///     belongs to a Steam account no Realm account has seen before.
/// </summary>
public class SteamTicketSignInRequest
{
	public string Ticket { get; set; } = string.Empty;

	public uint AppId { get; set; }

	public string DisplayName { get; set; } = string.Empty;
}

public class RenameAccountRequest
{
	public string DisplayName { get; set; } = string.Empty;
}

/// <summary>
///     An administrator's rename of any account. The signature covers the canonical name and a nonce, so it cannot be
///     re-pointed at another account or replayed.
/// </summary>
public class AdminRenameAccountRequest
{
	public long AccountId { get; set; }

	public string DisplayName { get; set; } = string.Empty;

	public string Nonce { get; set; } = string.Empty;

	public long TimestampUnixSeconds { get; set; }

	public string Signature { get; set; } = string.Empty;

	public string AdminPublicKey { get; set; } = string.Empty;
}

public class AccountProviderInfoDto
{
	public string Kind { get; set; } = string.Empty;

	public bool Available { get; set; }
}

public class AccountProvidersDto
{
	public AccountProviderInfoDto[] Providers { get; set; } = Array.Empty<AccountProviderInfoDto>();

	public string[] Warnings { get; set; } = Array.Empty<string>();
}
