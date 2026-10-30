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

	public DateTime ExpiresAtUtc { get; set; }
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

public class RegisterBasicAuthRequest
{
	public string DisplayName { get; set; } = string.Empty;

	public string Password { get; set; } = string.Empty;
}

public class LoginBasicAuthRequest
{
	public string DisplayName { get; set; } = string.Empty;

	public string Password { get; set; } = string.Empty;
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

/// <summary>
///     A browser sign-in flow the client is waiting on. The port is the client's own loopback listener, chosen when the flow
///     starts, and the client state is echoed back so the client can refuse a callback it did not ask for.
/// </summary>
public class AuthFlowStartRequest
{
	public int LoopbackPort { get; set; }

	public string ClientState { get; set; } = string.Empty;

	public string Provider { get; set; } = string.Empty;
}

public class AuthFlowStartResponse
{
	public string Provider { get; set; } = string.Empty;

	public string AuthorizationUrl { get; set; } = string.Empty;

	public string State { get; set; } = string.Empty;
}

/// <summary>
///     The name a provider suggests during registration. The player may edit it, but it is never silently replaced: an
///     unusable suggestion is reported so the player picks their own name.
/// </summary>
public class ProviderRegistrationPreviewDto
{
	public string Provider { get; set; } = string.Empty;

	public string SuggestedDisplayName { get; set; } = string.Empty;

	public string? SuggestedDisplayNameErrorCode { get; set; }
}

public class AccountProviderInfoDto
{
	public string Kind { get; set; } = string.Empty;

	public bool Available { get; set; }

	public bool Recommended { get; set; }
}

public class AccountProvidersDto
{
	public AccountProviderInfoDto[] Providers { get; set; } = Array.Empty<AccountProviderInfoDto>();

	public string[] Warnings { get; set; } = Array.Empty<string>();
}

/// <summary>
///     The second half of a provider registration: the one-shot ticket the authority issued for a verified provider identity,
///     and the DisplayName the player chose. The provider's account token never reaches the client.
/// </summary>
public class ProviderRegistrationRequest
{
	public string RegistrationTicket { get; set; } = string.Empty;

	public string DisplayName { get; set; } = string.Empty;
}

public static class AccountQueryParameters
{
	public const string SessionToken = "token";

	public const string ClientState = "state";

	public const string DisplayName = "display_name";

	public const string AccountId = "account_id";

	public const string ErrorCode = "error_code";

	public const string Provider = "provider";
}
