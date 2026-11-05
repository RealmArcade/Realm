using System;
using System.Collections.Generic;

namespace Realm.AdminServer.Accounts;

/// <summary>
///     One link from an account to a provider. The link is keyed on the provider's permanent account token only, which for the
///     one provider is a SteamID64. Persona names, e-mails and tickets are never stored, so a Steam-side profile rename cannot
///     move a Realm account.
/// </summary>
public class AccountProviderLinkRecord
{
	public string Provider { get; set; } = string.Empty;

	public string ProviderAccountToken { get; set; } = string.Empty;

	public DateTime LinkedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
///     What the authority stores for an account. It holds no e-mail, real name, phone, address, IP history or provider profile
///     data beyond the linked SteamID64, so it is safe to hand to a peer.
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
///     the token the client holds, and it carries the account, an optional expiry, the moment the account holder last proved
///     themselves, and any revocation.
/// </summary>
public class AccountSessionRecord
{
	public string TokenHash { get; set; } = string.Empty;

	public long AccountId { get; set; }

	public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

	public DateTime? ExpiresAtUtc { get; set; }

	public DateTime? RevokedAtUtc { get; set; }

	public string? RotatedFromTokenHash { get; set; }

	/// <summary>
	///     When the identity behind this session was last proved by a Steam ticket, either when it was issued or by a later
	///     confirmation. A session can outlive the proof, and an action that changes who can reach the account requires one that
	///     is recent.
	/// </summary>
	public DateTime AuthenticatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
///     A player's multiplayer record as the authority keeps it. Only matches played with other people are counted, so
///     these numbers never move for a single-player game or a playtest.
/// </summary>
public class AccountMatchStatsRecord
{
	public long AccountId { get; set; }

	public int MatchesPlayed { get; set; }

	public int Victories { get; set; }

	public int Defeats { get; set; }

	public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
///     One match result an account has already had counted. It is keyed by the account and the match the screen named,
///     so the same finished match reported twice — a reload, a retried request — is counted exactly once.
/// </summary>
public class AccountMatchResultRecord
{
	public long AccountId { get; set; }

	public string MatchId { get; set; } = string.Empty;

	public bool Victory { get; set; }

	public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
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
