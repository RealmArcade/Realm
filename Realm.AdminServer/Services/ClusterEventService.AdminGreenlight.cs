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

/// <summary>admin_greenlight events.</summary>
public partial class ClusterEventService
	{
	private bool ApplyAdminGreenlight(ClusterEventDto evt, DataStoreService db, HashSet<string> adminPublicKeys)
	{
		if (!TryGetAdminGreenlightContext(evt, db, adminPublicKeys, out var mapTitle, out var mapVersion, out var creatorPublicKey))
			return false;

		// The claim is applied before the override so a peer can never be left holding a greenlit title that nobody owns.
		if (!MapMaintainerHelper.TryClaimOwnership(db, mapTitle, creatorPublicKey, out string existingOwner))
		{
			Console.WriteLine($"[ClusterEventService] Rejected admin_greenlight event: '{mapTitle}' belongs to {existingOwner}, not to the creator the override names");
			return false;
		}

		UpsertAdminGreenlightStats(db, mapTitle, mapVersion);

		return true;
	}

	private bool TryGetAdminGreenlightContext(ClusterEventDto evt, DataStoreService db, HashSet<string> adminPublicKeys, out string mapTitle, out string? mapVersion, out string creatorPublicKey)
	{
		PopulateAdminGreenlightValues(evt, out mapTitle, out mapVersion, out var creatorUsername, out var statedCreatorPublicKey, out var adminPublicKey, out var signature);

		creatorPublicKey = "";
		if (!ValidateAdminGreenlightRequiredFields(mapTitle, creatorUsername, adminPublicKey, signature))
			return false;

		if (!ValidateAdminGreenlightAuthorization(adminPublicKeys, adminPublicKey))
			return false;

		// The name lock on this server decides who may publish, so a peer never takes the emitting server's word for it.
		if (!CreatorNamePolicy.TryReadNameLockOwner(db, creatorUsername, out creatorPublicKey))
		{
			Console.WriteLine($"[ClusterEventService] Rejected admin_greenlight event: no creator holds the name '{creatorUsername}'");
			return false;
		}

		if (!string.IsNullOrWhiteSpace(statedCreatorPublicKey) && !string.Equals(statedCreatorPublicKey.Trim(), creatorPublicKey, StringComparison.OrdinalIgnoreCase))
		{
			Console.WriteLine($"[ClusterEventService] admin_greenlight event names '{creatorUsername}' as {statedCreatorPublicKey.Trim()} but this server holds the name for {creatorPublicKey}; the name lock this server holds decides");
		}

		return VerifyAdminGreenlightSignature(mapTitle, creatorUsername, adminPublicKey, signature);
	}

	private void PopulateAdminGreenlightValues(ClusterEventDto evt, out string mapTitle, out string? mapVersion, out string creatorUsername, out string creatorPublicKey, out string adminPublicKey, out string signature)
	{
		var payload = ParseAdminGreenlightPayload(evt);
		mapTitle = payload?.MapTitle ?? "";
		mapVersion = payload?.MapVersion;
		creatorUsername = payload?.CreatorUsername ?? "";
		creatorPublicKey = payload?.CreatorPublicKey ?? "";
		adminPublicKey = payload?.AdminPublicKey ?? evt.PublicKey;
		signature = payload?.Signature ?? evt.Signature;
	}

	private bool ValidateAdminGreenlightRequiredFields(string mapTitle, string creatorUsername, string adminPublicKey, string signature)
	{
		return !string.IsNullOrWhiteSpace(mapTitle)
		       && !string.IsNullOrWhiteSpace(creatorUsername)
		       && !string.IsNullOrWhiteSpace(adminPublicKey)
		       && !string.IsNullOrWhiteSpace(signature);
	}

	private bool ValidateAdminGreenlightAuthorization(HashSet<string> adminPublicKeys, string adminPublicKey)
	{
		if (adminPublicKeys.Count == 0 || !adminPublicKeys.Contains(adminPublicKey.Trim()))
		{
			Console.WriteLine($"[ClusterEventService] Rejected admin_greenlight event: Unauthorized admin key {adminPublicKey}");
			return false;
		}

		return true;
	}


	private AdminGreenlightEventPayload? ParseAdminGreenlightPayload(ClusterEventDto evt)
	{
		if (string.IsNullOrWhiteSpace(evt.PayloadJson)) return null;
		try
		{
			return JsonSerializer.Deserialize<AdminGreenlightEventPayload>(evt.PayloadJson, JsonOpts);
		}
		catch
		{
			return null;
		}
	}

	private bool VerifyAdminGreenlightSignature(string mapTitle, string creatorUsername, string adminPublicKey, string signature)
	{
		string sigPayload = $"greenlight:{mapTitle.ToLowerInvariant()}:{NameNormalizationHelper.GetCreatorSlug(creatorUsername)}";

		bool isValid = AuthorSignatureHelper.VerifySignature(adminPublicKey.Trim(), sigPayload, signature);

		if (!isValid)
			Console.WriteLine($"[ClusterEventService] Rejected admin_greenlight event: Invalid admin signature");

		return isValid;
	}

	private void UpsertAdminGreenlightStats(DataStoreService db, string mapTitle, string? mapVersion)
	{
		var stats = MapStatsHelper.GetStats(db, mapTitle, mapVersion, null) ?? new MapStats();
		stats.AdminOverrideGreenlit = true;

		db.Upsert("map_stats", mapTitle, stats);
		db.Upsert("map_stats", mapTitle.ToLowerInvariant(), stats);

		if (!string.IsNullOrWhiteSpace(mapVersion))
		{
			string compositeKey = $"{mapTitle}_{mapVersion.Trim()}";
			db.Upsert("map_stats", compositeKey, stats);
			db.Upsert("map_stats", compositeKey.ToLowerInvariant(), stats);
		}
	}
}
