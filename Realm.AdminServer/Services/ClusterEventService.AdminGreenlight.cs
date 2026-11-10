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
		if (!TryGetAdminGreenlightContext(evt, adminPublicKeys, out var mapTitle, out var mapVersion))
			return false;

		UpsertAdminGreenlightStats(db, mapTitle, mapVersion);

		return true;
	}

	private bool TryGetAdminGreenlightContext(ClusterEventDto evt, HashSet<string> adminPublicKeys, out string mapTitle, out string? mapVersion)
	{
		PopulateAdminGreenlightValues(evt, out mapTitle, out mapVersion, out var adminPublicKey, out var signature);

		if (!ValidateAdminGreenlightRequiredFields(mapTitle, adminPublicKey, signature))
			return false;

		if (!ValidateAdminGreenlightAuthorization(adminPublicKeys, adminPublicKey))
			return false;

		return VerifyAdminGreenlightSignature(mapTitle, mapVersion, adminPublicKey, signature);
	}

	private void PopulateAdminGreenlightValues(ClusterEventDto evt, out string mapTitle, out string? mapVersion, out string adminPublicKey, out string signature)
	{
		var payload = ParseAdminGreenlightPayload(evt);
		mapTitle = payload?.MapTitle ?? "";
		mapVersion = payload?.MapVersion;
		adminPublicKey = payload?.AdminPublicKey ?? evt.PublicKey;
		signature = payload?.Signature ?? evt.Signature;
	}

	private bool ValidateAdminGreenlightRequiredFields(string mapTitle, string adminPublicKey, string signature)
	{
		return !string.IsNullOrWhiteSpace(mapTitle)
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

	private bool VerifyAdminGreenlightSignature(string mapTitle, string? mapVersion, string adminPublicKey, string signature)
	{
		string sigPayload = $"greenlight:{mapTitle.ToLowerInvariant()}";
		string oldSigPayload = !string.IsNullOrWhiteSpace(mapVersion) ? $"greenlight:{mapTitle.ToLowerInvariant()}:{mapVersion.ToLowerInvariant()}" : sigPayload;

		bool isValid = AuthorSignatureHelper.VerifySignature(adminPublicKey.Trim(), sigPayload, signature) ||
		               AuthorSignatureHelper.VerifySignature(adminPublicKey.Trim(), oldSigPayload, signature);

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
