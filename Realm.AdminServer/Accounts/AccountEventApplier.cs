using System.Text.Json;
using DBreeze.Transactions;
using Realm.AdminServer.Services;
using Realm.Shared;
using Realm.Shared.Accounts;
using Realm.Shared.Distribution;

namespace Realm.AdminServer.Accounts;

/// <summary>
///     Applies the account events an authority publishes, inside one DBreeze transaction so a refused event leaves no trace.
///     The store only accepts a key the node already trusts, so a peer cannot be talked into a merge by a foreign signer.
/// </summary>
public class AccountEventApplier
{
	private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
	private readonly AccountAuthorityOptions _accountAuthorityOptions;

	internal AccountEventApplier(AccountAuthorityOptions accountAuthorityOptions)
	{
		_accountAuthorityOptions = accountAuthorityOptions;
	}

	/// <summary>
	///     Applies an account event published by the account authority. A peer refuses an event whose key is not a configured
	///     authority key or whose signature does not verify, and logs the refusal instead of merging it. Re-applying the same
	///     event leaves the store unchanged.
	/// </summary>
	internal bool Apply(ClusterEventDto evt, DataStoreService db)
	{
		string eventType = evt.EventType.ToLowerInvariant();
		AccountClusterEventPayload? payload = ParsePayload(evt);
		if (payload == null || payload.AccountId <= 0 || string.IsNullOrWhiteSpace(payload.UniquenessKey) || string.IsNullOrWhiteSpace(payload.DisplayName))
		{
			Console.WriteLine($"[ClusterEventService] Refused {eventType}: the payload is incomplete");
			return false;
		}

		string publicKey = evt.PublicKey?.Trim() ?? string.Empty;
		if (publicKey.Length == 0 || !_accountAuthorityOptions.AuthorityPublicKeys.Contains(publicKey))
		{
			Console.WriteLine($"[ClusterEventService] Refused {eventType} for account {payload.AccountId}: the signing key is not a configured account authority key");
			return false;
		}

		if (!AuthorSignatureHelper.VerifySignature(publicKey, payload.BuildSignaturePayload(eventType), evt.Signature))
		{
			Console.WriteLine($"[ClusterEventService] Refused {eventType} for account {payload.AccountId}: the signature did not verify");
			return false;
		}

		bool applied = db.TryMutate(
			new[]
			{
				AccountStoreService.AccountsCollection,
				AccountStoreService.DisplayNamesCollection,
				AccountStoreService.ConfusableNamesCollection,
				AccountStoreService.LinksCollection,
				AccountStoreService.RenameHistoryCollection
			},
			transaction => ApplyInTransaction(eventType, payload, transaction));

		if (!applied) Console.WriteLine($"[ClusterEventService] Refused {eventType} for account {payload.AccountId}: it conflicts with the local account store");
		return applied;
	}

	private bool ApplyInTransaction(string eventType, AccountClusterEventPayload payload, Transaction transaction)
	{
		string accountKey = payload.AccountId.ToString(System.Globalization.CultureInfo.InvariantCulture);
		AccountRecord? existing = DataStoreService.ReadFrom<AccountRecord>(transaction, AccountStoreService.AccountsCollection, accountKey);
		string skeleton = DisplayNameRules.ComputeConfusableSkeleton(payload.DisplayName);

		long nameOwner = DataStoreService.ReadFrom<long>(transaction, AccountStoreService.DisplayNamesCollection, payload.UniquenessKey);
		if (nameOwner != 0 && nameOwner != payload.AccountId) return false;

		long skeletonOwner = DataStoreService.ReadFrom<long>(transaction, AccountStoreService.ConfusableNamesCollection, skeleton);
		if (skeletonOwner != 0 && skeletonOwner != payload.AccountId) return false;

		switch (eventType)
		{
			case AccountClusterEventPayload.CreatedEvent:
				if (existing != null) return true;

				var created = new AccountRecord
				{
					AccountId = payload.AccountId,
					DisplayName = payload.DisplayName,
					UniquenessKey = payload.UniquenessKey,
					CreatedAtUtc = payload.MutationTimestampUtc,
					UpdatedAtUtc = payload.MutationTimestampUtc
				};

				if (!string.IsNullOrEmpty(payload.Provider) && !string.IsNullOrEmpty(payload.ProviderAccountToken))
				{
					created.Links.Add(new AccountProviderLinkRecord
					{
						Provider = payload.Provider,
						ProviderAccountToken = payload.ProviderAccountToken,
						LinkedAtUtc = payload.MutationTimestampUtc
					});
					DataStoreService.WriteTo(transaction, AccountStoreService.LinksCollection, AccountStoreService.BuildLinkKey(payload.Provider, payload.ProviderAccountToken), payload.AccountId);
				}

				DataStoreService.WriteTo(transaction, AccountStoreService.AccountsCollection, accountKey, created);
				DataStoreService.WriteTo(transaction, AccountStoreService.DisplayNamesCollection, payload.UniquenessKey, payload.AccountId);
				DataStoreService.WriteTo(transaction, AccountStoreService.ConfusableNamesCollection, skeleton, payload.AccountId);
				return true;

			case AccountClusterEventPayload.RenamedEvent:
				if (existing == null) return false;
				if (string.Equals(existing.UniquenessKey, payload.UniquenessKey, StringComparison.Ordinal)
				    && string.Equals(existing.DisplayName, payload.DisplayName, StringComparison.Ordinal)) return true;

				if (!string.IsNullOrEmpty(payload.PreviousUniquenessKey) && !string.Equals(payload.PreviousUniquenessKey, payload.UniquenessKey, StringComparison.Ordinal))
				{
					long previousOwner = DataStoreService.ReadFrom<long>(transaction, AccountStoreService.DisplayNamesCollection, payload.PreviousUniquenessKey);
					if (previousOwner != payload.AccountId) return false;
					DataStoreService.RemoveFrom(transaction, AccountStoreService.DisplayNamesCollection, payload.PreviousUniquenessKey);
				}

				string previousSkeleton = DisplayNameRules.ComputeConfusableSkeleton(existing.DisplayName);
				long previousSkeletonOwner = DataStoreService.ReadFrom<long>(transaction, AccountStoreService.ConfusableNamesCollection, previousSkeleton);
				if (previousSkeletonOwner == payload.AccountId) DataStoreService.RemoveFrom(transaction, AccountStoreService.ConfusableNamesCollection, previousSkeleton);

				var history = new AccountRenameHistoryRecord
				{
					AccountId = existing.AccountId,
					PreviousDisplayName = existing.DisplayName,
					PreviousUniquenessKey = existing.UniquenessKey,
					NewDisplayName = payload.DisplayName,
					NewUniquenessKey = payload.UniquenessKey,
					Actor = "authority",
					RecordedAtUtc = payload.MutationTimestampUtc
				};

				existing.DisplayName = payload.DisplayName;
				existing.UniquenessKey = payload.UniquenessKey;
				existing.UpdatedAtUtc = payload.MutationTimestampUtc;

				DataStoreService.WriteTo(transaction, AccountStoreService.AccountsCollection, accountKey, existing);
				DataStoreService.WriteTo(transaction, AccountStoreService.DisplayNamesCollection, payload.UniquenessKey, payload.AccountId);
				DataStoreService.WriteTo(transaction, AccountStoreService.ConfusableNamesCollection, skeleton, payload.AccountId);
				DataStoreService.WriteTo(transaction, AccountStoreService.RenameHistoryCollection, history.HistoryId, history);
				return true;

			case AccountClusterEventPayload.LinkAddedEvent:
				if (existing == null) return false;
				if (string.IsNullOrEmpty(payload.Provider) || string.IsNullOrEmpty(payload.ProviderAccountToken)) return false;

				string linkKey = AccountStoreService.BuildLinkKey(payload.Provider, payload.ProviderAccountToken);
				long linkOwner = DataStoreService.ReadFrom<long>(transaction, AccountStoreService.LinksCollection, linkKey);
				if (linkOwner != 0 && linkOwner != payload.AccountId) return false;

				if (!existing.Links.Exists(link => string.Equals(link.Provider, payload.Provider, StringComparison.Ordinal)))
				{
					existing.Links.Add(new AccountProviderLinkRecord
					{
						Provider = payload.Provider,
						ProviderAccountToken = payload.ProviderAccountToken,
						LinkedAtUtc = payload.MutationTimestampUtc
					});
					existing.UpdatedAtUtc = payload.MutationTimestampUtc;
					DataStoreService.WriteTo(transaction, AccountStoreService.AccountsCollection, accountKey, existing);
				}

				DataStoreService.WriteTo(transaction, AccountStoreService.LinksCollection, linkKey, payload.AccountId);
				return true;

			default:
				return false;
		}
	}

	private static AccountClusterEventPayload? ParsePayload(ClusterEventDto evt)
	{
		if (string.IsNullOrWhiteSpace(evt.PayloadJson)) return null;
		try
		{
			return JsonSerializer.Deserialize<AccountClusterEventPayload>(evt.PayloadJson, JsonOpts);
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
