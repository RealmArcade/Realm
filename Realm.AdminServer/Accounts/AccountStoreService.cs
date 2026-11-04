using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using DBreeze.Transactions;
using Realm.AdminServer.Services;
using Realm.Shared;
using Realm.Shared.Accounts;
using Realm.Shared.Distribution;

namespace Realm.AdminServer.Accounts;

public enum AccountMutationOutcome
{
	Success = 0,

	RefusedNotAuthority = 1,

	DisplayNameInvalid = 3,

	DisplayNameTaken = 4,

	DisplayNameVisuallySimilar = 5,

	AccountNotFound = 7,

	CredentialsInvalid = 8,

	SessionInvalid = 11,

	AdminProofMalformed = 12,

	AdminProofInvalid = 13,

	AdminProofStale = 14,

	AdminProofReplayed = 15
}

public class AccountMutationResult
{
	public AccountMutationOutcome Outcome { get; init; } = AccountMutationOutcome.Success;

	public AccountRecord? Account { get; init; }

	public AccountSessionDto? Session { get; init; }

	public DisplayNameError DisplayNameError { get; init; } = DisplayNameError.None;

	public string Message { get; init; } = string.Empty;

	public bool Succeeded => Outcome == AccountMutationOutcome.Success;

	public static AccountMutationResult Success(AccountRecord account, AccountSessionDto? session = null) => new()
	{
		Outcome = AccountMutationOutcome.Success,
		Account = account,
		Session = session
	};

	public static AccountMutationResult Refused(AccountMutationOutcome outcome, string message) => new()
	{
		Outcome = outcome,
		Message = message
	};

	public static AccountMutationResult InvalidName(DisplayNameError error) => new()
	{
		Outcome = error == DisplayNameError.AlreadyTaken ? AccountMutationOutcome.DisplayNameTaken
			: error == DisplayNameError.VisuallySimilar ? AccountMutationOutcome.DisplayNameVisuallySimilar
			: AccountMutationOutcome.DisplayNameInvalid,
		DisplayNameError = error,
		Message = DisplayNameRules.DescribeError(error)
	};
}

/// <summary>
///     Owns every account, DisplayName and provider link write. Only the configured account authority runs these mutations;
///     other nodes answer with a refusal instead of writing locally, because global uniqueness cannot be produced by an
///     eventually-consistent peer cluster.
/// </summary>
public class AccountStoreService
{
	public const string AccountsCollection = "accounts";

	public const string DisplayNamesCollection = "account_display_names";

	public const string ConfusableNamesCollection = "account_confusable_names";

	public const string LinksCollection = "account_links";

	public const string SessionsCollection = "account_sessions";

	public const string RenameHistoryCollection = "account_rename_history";

	public const string AdminNoncesCollection = "account_admin_proof_nonces";

	/// <summary>
	///     Creator names live in the same namespace as account DisplayNames. The legacy creator index is read here so an
	///     account cannot claim a name a creator already owns.
	/// </summary>
	public const string LegacyCreatorNamesCollection = "name_locks";

	private readonly DataStoreService _dataStore;
	private readonly AccountAuthorityOptions _options;
	private readonly IAccountReplicationSink _replication;

	public AccountStoreService(DataStoreService dataStore, AccountAuthorityOptions options, IAccountReplicationSink replication)
	{
		_dataStore = dataStore;
		_options = options;
		_replication = replication;
	}

	public bool IsAuthority => _options.IsAuthority;

	public static string[] MutatedCollections { get; } =
	{
		AccountsCollection,
		DisplayNamesCollection,
		ConfusableNamesCollection,
		LinksCollection,
		RenameHistoryCollection,
		AdminNoncesCollection,
		LegacyCreatorNamesCollection
	};

	/// <summary>
	///     Signs a verified provider identity in. A provider token that is already linked signs straight into its account and
	///     never touches the DisplayName; an unlinked token registers a new account with the name the player confirmed, which
	///     has already passed the shared validation.
	/// </summary>
	public AccountMutationResult SignInOrRegisterProvider(AccountProviderKind provider, string providerAccountToken, string? confirmedDisplayName)
	{
		if (!IsAuthority) return NotAuthority();

		string providerName = provider.ToWireName();
		if (string.IsNullOrWhiteSpace(providerAccountToken)) return AccountMutationResult.Refused(AccountMutationOutcome.CredentialsInvalid, "The provider did not return an account token.");

		long linkedAccountId = _dataStore.Get<long>(LinksCollection, BuildLinkKey(providerName, providerAccountToken));
		if (linkedAccountId != 0)
		{
			AccountRecord? existing = _dataStore.Get<AccountRecord>(AccountsCollection, linkedAccountId.ToString());
			if (existing != null) return AccountMutationResult.Success(existing, IssueSession(existing));
		}

		if (!DisplayNameRules.TryCanonicalize(confirmedDisplayName, out string canonical, out DisplayNameError nameError))
		{
			return AccountMutationResult.InvalidName(nameError);
		}

		string uniquenessKey = DisplayNameRules.ComputeUniquenessKey(canonical);
		string skeleton = DisplayNameRules.ComputeConfusableSkeleton(canonical);
		AccountRecord? created = null;

		bool stored = _dataStore.TryMutate(MutatedCollections, transaction =>
		{
			long alreadyLinked = DataStoreService.ReadFrom<long>(transaction, LinksCollection, BuildLinkKey(providerName, providerAccountToken));
			if (alreadyLinked != 0) return false;

			if (CheckNameAvailability(transaction, canonical, uniquenessKey, skeleton, 0).Outcome != AccountMutationOutcome.Success) return false;

			long accountId = MintAccountId(transaction);
			AccountRecord account = new()
			{
				AccountId = accountId,
				DisplayName = canonical,
				UniquenessKey = uniquenessKey,
				CreatedAtUtc = DateTime.UtcNow,
				UpdatedAtUtc = DateTime.UtcNow,
				Links = { new AccountProviderLinkRecord { Provider = providerName, ProviderAccountToken = providerAccountToken, LinkedAtUtc = DateTime.UtcNow } }
			};

			DataStoreService.WriteTo(transaction, AccountsCollection, accountId.ToString(), account);
			DataStoreService.WriteTo(transaction, DisplayNamesCollection, uniquenessKey, accountId);
			DataStoreService.WriteTo(transaction, ConfusableNamesCollection, skeleton, accountId);
			DataStoreService.WriteTo(transaction, LinksCollection, BuildLinkKey(providerName, providerAccountToken), accountId);
			created = account;
			return true;
		});

		if (stored && created != null)
		{
			_replication.Publish(AccountClusterEventPayload.CreatedEvent, BuildEventPayload(created, null, null));
			return AccountMutationResult.Success(created, IssueSession(created));
		}

		long racedAccountId = _dataStore.Get<long>(LinksCollection, BuildLinkKey(providerName, providerAccountToken));
		if (racedAccountId != 0)
		{
			AccountRecord? raced = _dataStore.Get<AccountRecord>(AccountsCollection, racedAccountId.ToString());
			if (raced != null) return AccountMutationResult.Success(raced, IssueSession(raced));
		}

		return ResolveNameConflict(canonical, uniquenessKey, skeleton, 0);
	}

	/// <summary>
	///     Moves a DisplayName in one transaction: the old fold entry is released, the new one installed, the account record and
	///     the audit history updated, and (for an administrator) the signed proof's nonce consumed. There is no window in which
	///     the old name is unregistrable or owned twice, and a case-only change of an account's own name is a success that
	///     changes nothing.
	/// </summary>
	public AccountMutationResult Rename(long accountId, string? newDisplayName, string actor, string? adminNonce = null)
	{
		if (!IsAuthority) return NotAuthority();

		if (!DisplayNameRules.TryCanonicalize(newDisplayName, out string canonical, out DisplayNameError nameError))
		{
			return AccountMutationResult.InvalidName(nameError);
		}

		string newUniquenessKey = DisplayNameRules.ComputeUniquenessKey(canonical);
		string newSkeleton = DisplayNameRules.ComputeConfusableSkeleton(canonical);

		AccountRecord? renamed = null;
		AccountMutationOutcome failure = AccountMutationOutcome.Success;
		string previousUniquenessKey = string.Empty;

		bool stored = _dataStore.TryMutate(MutatedCollections, transaction =>
		{
			if (accountId <= 0)
			{
				failure = AccountMutationOutcome.AccountNotFound;
				return false;
			}

			AccountRecord? account = DataStoreService.ReadFrom<AccountRecord>(transaction, AccountsCollection, accountId.ToString());
			if (account == null)
			{
				failure = AccountMutationOutcome.AccountNotFound;
				return false;
			}

			previousUniquenessKey = account.UniquenessKey;

			if (!string.IsNullOrEmpty(adminNonce))
			{
				long consumedAt = DataStoreService.ReadFrom<long>(transaction, AdminNoncesCollection, adminNonce);
				if (consumedAt != 0)
				{
					failure = AccountMutationOutcome.AdminProofReplayed;
					return false;
				}

				DataStoreService.WriteTo(transaction, AdminNoncesCollection, adminNonce, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
			}

			if (string.Equals(account.UniquenessKey, newUniquenessKey, StringComparison.Ordinal))
			{
				renamed = account;
				return true;
			}

			AccountMutationResult conflict = CheckNameAvailability(transaction, canonical, newUniquenessKey, newSkeleton, accountId);
			if (conflict.Outcome != AccountMutationOutcome.Success)
			{
				failure = conflict.Outcome;
				return false;
			}

			long currentOwner = DataStoreService.ReadFrom<long>(transaction, DisplayNamesCollection, account.UniquenessKey);
			if (currentOwner == accountId) DataStoreService.RemoveFrom(transaction, DisplayNamesCollection, account.UniquenessKey);

			string previousSkeleton = DisplayNameRules.ComputeConfusableSkeleton(account.DisplayName);
			long currentSkeletonOwner = DataStoreService.ReadFrom<long>(transaction, ConfusableNamesCollection, previousSkeleton);
			if (currentSkeletonOwner == accountId) DataStoreService.RemoveFrom(transaction, ConfusableNamesCollection, previousSkeleton);

			AccountRenameHistoryRecord history = new()
			{
				AccountId = accountId,
				PreviousDisplayName = account.DisplayName,
				PreviousUniquenessKey = account.UniquenessKey,
				NewDisplayName = canonical,
				NewUniquenessKey = newUniquenessKey,
				Actor = actor,
				RecordedAtUtc = DateTime.UtcNow
			};

			account.DisplayName = canonical;
			account.UniquenessKey = newUniquenessKey;
			account.UpdatedAtUtc = DateTime.UtcNow;

			DataStoreService.WriteTo(transaction, AccountsCollection, accountId.ToString(), account);
			DataStoreService.WriteTo(transaction, DisplayNamesCollection, newUniquenessKey, accountId);
			DataStoreService.WriteTo(transaction, ConfusableNamesCollection, newSkeleton, accountId);
			DataStoreService.WriteTo(transaction, RenameHistoryCollection, history.HistoryId, history);
			renamed = account;
			return true;
		});

		if (!stored || renamed == null)
		{
			return failure switch
			{
				AccountMutationOutcome.DisplayNameTaken => AccountMutationResult.InvalidName(DisplayNameError.AlreadyTaken),
				AccountMutationOutcome.DisplayNameVisuallySimilar => AccountMutationResult.InvalidName(DisplayNameError.VisuallySimilar),
				AccountMutationOutcome.DisplayNameInvalid => AccountMutationResult.InvalidName(DisplayNameError.DisallowedCharacter),
				AccountMutationOutcome.AdminProofReplayed => AccountMutationResult.Refused(AccountMutationOutcome.AdminProofReplayed, "That administrator proof has already been used."),
				_ => AccountMutationResult.Refused(AccountMutationOutcome.AccountNotFound, "No such account.")
			};
		}

		if (!string.Equals(previousUniquenessKey, renamed.UniquenessKey, StringComparison.Ordinal))
		{
			AccountClusterEventPayload payload = BuildEventPayload(renamed, previousUniquenessKey, null);
			_replication.Publish(AccountClusterEventPayload.RenamedEvent, payload);
		}

		return AccountMutationResult.Success(renamed);
	}

	public AccountSessionDto IssueSession(AccountRecord account)
	{
		DateTime now = DateTime.UtcNow;
		string token = CreateSessionToken();
		string tokenHash = HashSessionToken(token);
		DateTime? expiresAt = _options.SessionLifetimeHours == 0 ? null : now.AddHours(_options.SessionLifetimeHours);
		_dataStore.Upsert(SessionsCollection, tokenHash, new AccountSessionRecord
		{
			TokenHash = tokenHash,
			AccountId = account.AccountId,
			CreatedAtUtc = now,
			AuthenticatedAtUtc = now,
			ExpiresAtUtc = expiresAt
		});

		return BuildSessionDto(token, account, expiresAt);
	}

	/// <summary>
	///     Validates the bearer token a client presents. The token is only ever looked up by its hash, so the store never holds
	///     the secret itself.
	/// </summary>
	public bool TryValidateSession(string? token, out AccountRecord? account, out AccountSessionDto? session, out AccountMutationOutcome refusal)
	{
		account = null;
		session = null;
		refusal = AccountMutationOutcome.SessionInvalid;

		if (!TryReadSessionRecord(token, out AccountSessionRecord? record, out _) || record == null) return false;

		AccountRecord? found = _dataStore.Get<AccountRecord>(AccountsCollection, record.AccountId.ToString());
		if (found == null) return false;

		account = found;
		session = BuildSessionDto(token!.Trim(), found, record.ExpiresAtUtc);
		return true;
	}

	/// <summary>
	///     Whether the session behind this token was proved recently enough for a change that outlives it.
	/// </summary>
	public bool HasRecentCredentialProof(string? token)
	{
		if (!TryReadSessionRecord(token, out AccountSessionRecord? record, out _) || record == null) return false;
		return record.AuthenticatedAtUtc.AddMinutes(_options.SensitiveActionWindowMinutes) > DateTime.UtcNow;
	}

	private bool TryReadSessionRecord(string? token, out AccountSessionRecord? record, out string tokenHash)
	{
		record = null;
		tokenHash = string.Empty;

		if (string.IsNullOrWhiteSpace(token)) return false;

		tokenHash = HashSessionToken(token.Trim());
		record = _dataStore.Get<AccountSessionRecord>(SessionsCollection, tokenHash);
		if (record == null || record.RevokedAtUtc != null) return false;

		return !record.ExpiresAtUtc.HasValue || record.ExpiresAtUtc.Value > DateTime.UtcNow;
	}

	public bool RevokeSession(string? token)
	{
		if (!IsAuthority || string.IsNullOrWhiteSpace(token)) return false;

		string tokenHash = HashSessionToken(token.Trim());
		AccountSessionRecord? record = _dataStore.Get<AccountSessionRecord>(SessionsCollection, tokenHash);
		if (record == null) return false;

		record.RevokedAtUtc = DateTime.UtcNow;
		_dataStore.Upsert(SessionsCollection, tokenHash, record);
		return true;
	}

	public AccountRecord? FindAccountByUniquenessKey(string uniquenessKey)
	{
		long accountId = _dataStore.Get<long>(DisplayNamesCollection, uniquenessKey);
		return accountId == 0 ? null : _dataStore.Get<AccountRecord>(AccountsCollection, accountId.ToString());
	}

	public AccountRecord? FindAccount(long accountId)
	{
		return accountId <= 0 ? null : _dataStore.Get<AccountRecord>(AccountsCollection, accountId.ToString());
	}

	public AccountRecord? FindAccountByDisplayName(string? displayName)
	{
		if (!DisplayNameRules.TryCanonicalize(displayName, out string canonical, out _)) return null;
		return FindAccountByUniquenessKey(DisplayNameRules.ComputeUniquenessKey(canonical));
	}

	/// <summary>
	///     Whether a creator already owns this name in the shared namespace. The creator index is keyed by the legacy ASCII
	///     slug, so the candidate is reduced the same way before it is looked up.
	/// </summary>
	public bool IsCreatorNameTaken(string canonicalDisplayName)
	{
		string legacySlug = NameNormalizationHelper.NormalizeUsername(canonicalDisplayName);
		if (legacySlug.Length == 0) return false;
		return _dataStore.Get<System.Text.Json.JsonDocument>(LegacyCreatorNamesCollection, legacySlug) != null;
	}

	public static AccountProfileDto ToProfile(AccountRecord account)
	{
		return new AccountProfileDto
		{
			AccountId = account.AccountId,
			DisplayName = account.DisplayName,
			LinkedProviders = account.LinkedProviderNames(),
			CreatedAtUtc = account.CreatedAtUtc
		};
	}

	public static string HashSessionToken(string token)
	{
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
	}

	public static string CreateSessionToken()
	{
		return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	}

	public static string BuildLinkKey(string provider, string providerAccountToken)
	{
		return $"{provider.ToLowerInvariant()}:{providerAccountToken}";
	}

	private AccountMutationResult NotAuthority()
	{
		return AccountMutationResult.Refused(AccountMutationOutcome.RefusedNotAuthority, "This node is not the configured account authority.");
	}

	private static AccountSessionDto BuildSessionDto(string token, AccountRecord account, DateTime? expiresAt)
	{
		return new AccountSessionDto
		{
			SessionToken = token,
			AccountId = account.AccountId,
			DisplayName = account.DisplayName,
			LinkedProviders = account.LinkedProviderNames(),
			ExpiresAtUtc = expiresAt
		};
	}

	private AccountClusterEventPayload BuildEventPayload(AccountRecord account, string? previousUniquenessKey, AccountProviderLinkRecord? link)
	{
		return new AccountClusterEventPayload
		{
			AccountId = account.AccountId,
			DisplayName = account.DisplayName,
			UniquenessKey = account.UniquenessKey,
			PreviousUniquenessKey = previousUniquenessKey,
			Provider = link?.Provider,
			ProviderAccountToken = link?.ProviderAccountToken,
			MutationTimestampUtc = DateTime.UtcNow
		};
	}

	private AccountMutationResult CheckNameAvailability(Transaction transaction, string canonical, string uniquenessKey, string skeleton, long ownAccountId)
	{
		long nameOwner = DataStoreService.ReadFrom<long>(transaction, DisplayNamesCollection, uniquenessKey);
		if (nameOwner != 0 && nameOwner != ownAccountId) return AccountMutationResult.InvalidName(DisplayNameError.AlreadyTaken);

		long skeletonOwner = DataStoreService.ReadFrom<long>(transaction, ConfusableNamesCollection, skeleton);
		if (skeletonOwner != 0 && skeletonOwner != ownAccountId) return AccountMutationResult.InvalidName(DisplayNameError.VisuallySimilar);

		string legacySlug = NameNormalizationHelper.NormalizeUsername(canonical);
		if (legacySlug.Length > 0 && DataStoreService.ReadFrom<System.Text.Json.JsonDocument>(transaction, LegacyCreatorNamesCollection, legacySlug) != null)
		{
			return AccountMutationResult.InvalidName(DisplayNameError.AlreadyTaken);
		}

		return AccountMutationResult.Success(new AccountRecord { AccountId = ownAccountId, DisplayName = canonical, UniquenessKey = uniquenessKey });
	}

	private AccountMutationResult ResolveNameConflict(string canonical, string uniquenessKey, string skeleton, long ownAccountId)
	{
		long nameOwner = _dataStore.Get<long>(DisplayNamesCollection, uniquenessKey);
		if (nameOwner != 0 && nameOwner != ownAccountId) return AccountMutationResult.InvalidName(DisplayNameError.AlreadyTaken);

		long skeletonOwner = _dataStore.Get<long>(ConfusableNamesCollection, skeleton);
		if (skeletonOwner != 0 && skeletonOwner != ownAccountId) return AccountMutationResult.InvalidName(DisplayNameError.VisuallySimilar);

		if (IsCreatorNameTaken(canonical)) return AccountMutationResult.InvalidName(DisplayNameError.AlreadyTaken);

		return AccountMutationResult.Refused(AccountMutationOutcome.CredentialsInvalid, "The account could not be created.");
	}

	private long MintAccountId(Transaction transaction)
	{
		while (true)
		{
			Span<byte> bytes = stackalloc byte[8];
			RandomNumberGenerator.Fill(bytes);
			long candidate = BitConverter.ToInt64(bytes) & long.MaxValue;
			if (candidate == 0) continue;
			if (DataStoreService.ReadFrom<AccountRecord>(transaction, AccountsCollection, candidate.ToString()) != null) continue;
			return candidate;
		}
	}
}
