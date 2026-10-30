using System;
using System.Collections.Generic;

namespace Realm.AdminServer.Accounts;

/// <summary>
///     One link from an account to a provider. The link is keyed on the provider's permanent account token only: a Discord user
///     id, a SteamID64 or the id of a Realm.BasicAuth credential record. Provider names, e-mails and tokens are never stored,
///     so a provider-side profile rename cannot move a Realm account.
/// </summary>
public class AccountProviderLinkRecord
{
	public string Provider { get; set; } = string.Empty;

	public string ProviderAccountToken { get; set; } = string.Empty;

	public DateTime LinkedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
///     What the authority stores for an account. It holds no e-mail, real name, phone, address, IP history, provider profile
///     data or password material beyond the linked credential record's identifier, so it is safe to hand to a peer.
/// </summary>
public class AccountRecord
{
	public long AccountId { get; set; }

	public string DisplayName { get; set; } = string.Empty;

	public string UniquenessKey { get; set; } = string.Empty;

	public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

	public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

	public List<AccountProviderLinkRecord> Links { get; set; } = new();

	public string[] LinkedProviderNames()
	{
		var providers = new string[Links.Count];
		for (int index = 0; index < Links.Count; index++) providers[index] = Links[index].Provider;
		return providers;
	}
}

/// <summary>
///     A bearer session as the authority keeps it. The token itself is never stored: the record is keyed by the SHA-256 hash of
///     the token the client holds, and it carries the account, the expiry and any revocation.
/// </summary>
public class AccountSessionRecord
{
	public string TokenHash { get; set; } = string.Empty;

	public long AccountId { get; set; }

	public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

	public DateTime ExpiresAtUtc { get; set; }

	public DateTime? RevokedAtUtc { get; set; }

	public string? RotatedFromTokenHash { get; set; }
}

/// <summary>
///     The Realm.BasicAuth credential record. Its identifier is the provider account token the account links on, and the
///     password material is a salted, memory-hard scrypt hash verified in constant time.
/// </summary>
public class AccountCredentialRecord
{
	public string CredentialId { get; set; } = string.Empty;

	public long AccountId { get; set; }

	public string PasswordHash { get; set; } = string.Empty;

	public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

	public DateTime? LastVerifiedAtUtc { get; set; }
}

/// <summary>
///     An audit entry for a name that moved. History never blocks a registration; it only records who held a name and when, so a
///     later take-over can be adjudicated.
/// </summary>
public class AccountRenameHistoryRecord
{
	public string HistoryId { get; set; } = Guid.NewGuid().ToString("N");

	public long AccountId { get; set; }

	public string PreviousDisplayName { get; set; } = string.Empty;

	public string PreviousUniquenessKey { get; set; } = string.Empty;

	public string NewDisplayName { get; set; } = string.Empty;

	public string NewUniquenessKey { get; set; } = string.Empty;

	public string Actor { get; set; } = string.Empty;

	public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}
