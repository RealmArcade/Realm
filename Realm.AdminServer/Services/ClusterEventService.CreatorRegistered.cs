using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using DBreeze.Transactions;
using Realm.AdminServer.Accounts;
using Realm.Shared;
using Realm.Shared.Accounts;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;

namespace Realm.AdminServer.Services;

/// <summary>creator_registered events and the name locks they take.</summary>
public partial class ClusterEventService
	{
	private bool ApplyCreatorRegistered(ClusterEventDto evt, DataStoreService db, HashSet<string> adminPublicKeys)
	{
		if (!TryGetCreatorContext(evt, db, adminPublicKeys, out var username, out var publicKey, out var payload, out var slug, out string previousOwnerPublicKey))
			return false;

		CreatorNamePolicy.ReleaseCreatorKey(db, previousOwnerPublicKey, username);
		UpsertNameLock(db, slug, username, publicKey, payload?.RegisteredAt);
		UpsertCreatorInfo(db, username, publicKey, payload?.DonationLink, payload?.ContactInfo, payload?.RegisteredAt);

		return true;
	}

	private bool TryGetCreatorContext(ClusterEventDto evt, DataStoreService db, HashSet<string> adminPublicKeys, out string username, out string publicKey, out CreatorRegisteredEventPayload? payload, out string slug, out string previousOwnerPublicKey)
	{
		PopulateCreatorValues(evt, out username, out publicKey, out payload, out var signature);

		slug = "";
		previousOwnerPublicKey = "";

		if (!ValidateCreatorRequiredFields(username, publicKey, signature))
			return false;

		if (!VerifyCreatorRegisteredSignature(username, publicKey, payload?.DonationLink, payload?.ContactInfo, signature, payload?.AdminBypassToken, adminPublicKeys))
			return false;

		slug = GetUsernameSlug(username);

		return ValidateNameLock(db, slug, username, publicKey, payload?.AdminBypassToken, adminPublicKeys, out previousOwnerPublicKey);
	}

	private void PopulateCreatorValues(ClusterEventDto evt, out string username, out string publicKey, out CreatorRegisteredEventPayload? payload, out string signature)
	{
		payload = ParseCreatorRegisteredPayload(evt);
		username = payload?.Username ?? "";
		publicKey = payload?.PublicKey ?? evt.PublicKey;
		signature = payload?.Signature ?? evt.Signature;
	}

	private bool ValidateCreatorRequiredFields(string username, string publicKey, string signature)
	{
		return !string.IsNullOrWhiteSpace(username)
		       && !string.IsNullOrWhiteSpace(publicKey)
		       && !string.IsNullOrWhiteSpace(signature);
	}


	private CreatorRegisteredEventPayload? ParseCreatorRegisteredPayload(ClusterEventDto evt)
	{
		if (string.IsNullOrWhiteSpace(evt.PayloadJson)) return null;
		try
		{
			return JsonSerializer.Deserialize<CreatorRegisteredEventPayload>(evt.PayloadJson, JsonOpts);
		}
		catch
		{
			return null;
		}
	}

	private bool VerifyCreatorRegisteredSignature(string username, string publicKey, string? donationLink, string? contactInfo, string signature, string? adminBypassToken, HashSet<string> adminPublicKeys)
	{
		bool sigValid = CreatorRegistrationProof.Verify(publicKey, username, donationLink, contactInfo, signature);

		if (!sigValid
		    && !string.IsNullOrEmpty(adminBypassToken)
		    && adminPublicKeys.Count > 0
		    && AdminBypassAuth.VerifyBypassToken(adminPublicKeys, "admin", "override", adminBypassToken))
		{
			sigValid = CreatorRegistrationProof.VerifyByAnyAuthority(adminPublicKeys, publicKey, username, donationLink, contactInfo, signature);
		}

		if (!sigValid)
			Console.WriteLine($"[ClusterEventService] Rejected creator_registered event: Invalid signature for {username}");

		return sigValid;
	}

	private string GetUsernameSlug(string username)
	{
		return CreatorNamePolicy.GetSlug(username);
	}

	private bool ValidateNameLock(DataStoreService db, string slug, string username, string publicKey, string? adminBypassToken, HashSet<string> adminPublicKeys, out string previousOwnerPublicKey)
	{
		previousOwnerPublicKey = "";

		var existingLock = db.Get<JsonDocument>(CreatorNamePolicy.NameLocksCollection, slug)
		                   ?? db.Get<JsonDocument>(CreatorNamePolicy.NameLocksCollection, username.ToLowerInvariant().Replace(" ", "-"));

		if (existingLock == null) return true;

		var root = existingLock.RootElement;
		string owner = root.TryGetProperty("owner_public_key", out var op) ? op.GetString() ?? "" : "";
		if (string.Equals(owner, publicKey, StringComparison.OrdinalIgnoreCase)) return true;

		bool bypassValid = !string.IsNullOrEmpty(adminBypassToken)
		                   && adminPublicKeys.Count > 0
		                   && AdminBypassAuth.VerifyBypassToken(adminPublicKeys, "admin", "override", adminBypassToken);

		if (bypassValid || !CreatorNamePolicy.HasGreenlitPublishedMap(db, owner))
		{
			previousOwnerPublicKey = owner;
			return true;
		}

		Console.WriteLine($"[ClusterEventService] Rejected creator_registered event: Username '{username}' belongs to a creator with a greenlit map");
		return false;
	}

	private void UpsertNameLock(DataStoreService db, string slug, string username, string publicKey, DateTime? registeredAt)
	{
		var lockDoc = JsonSerializer.SerializeToDocument(new
		{
			username = username,
			owner_public_key = publicKey,
			registered_at = registeredAt ?? DateTime.UtcNow
		});
		db.Upsert("name_locks", slug, lockDoc);
	}

	private void UpsertCreatorInfo(DataStoreService db, string username, string publicKey, string? donationLink, string? contactInfo, DateTime? registeredAt)
	{
		var creatorDoc = JsonSerializer.SerializeToDocument(new
		{
			username = username,
			public_key = publicKey,
			donation_link = donationLink ?? "",
			contact_info = contactInfo ?? "",
			registered_at = registeredAt ?? DateTime.UtcNow
		});
		db.Upsert("creators", publicKey, creatorDoc);
	}
}
