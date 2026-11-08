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

/// <summary>map_registered / map_published events.</summary>
public partial class ClusterEventService
	{
	private bool ApplyMapPublished(ClusterEventDto evt, DataStoreService db, ContentAddressableStorage cas)
	{
		if (!TryGetMapPublishedContext(evt, db, out var manifestJson, out var mapTitle, out var mapVersion, out var publicKey, out var compositeKey))
			return false;

		return WriteManifestAndUpsert(manifestJson, mapTitle, mapVersion, compositeKey, publicKey, db, cas);
	}

	private bool TryGetMapPublishedContext(ClusterEventDto evt, DataStoreService db, out string manifestJson, out string mapTitle, out string mapVersion, out string publicKey, out string compositeKey)
	{
		PopulateMapPublishedValues(evt, out manifestJson, out mapTitle, out mapVersion, out publicKey, out var signature);

		if (!ValidateMapPublishedRequiredFields(manifestJson, publicKey, signature, ref mapTitle, ref mapVersion))
		{
			compositeKey = "";
			return false;
		}

		if (!ValidateMapPublishedAuthorization(db, mapTitle, publicKey))
		{
			compositeKey = "";
			return false;
		}

		compositeKey = $"{mapTitle}_{mapVersion}";
		return ValidateMapPublishedGreenlight(db, mapTitle, mapVersion, publicKey);
	}

	private void PopulateMapPublishedValues(ClusterEventDto evt, out string manifestJson, out string mapTitle, out string mapVersion, out string publicKey, out string signature)
	{
		manifestJson = "";
		publicKey = evt.PublicKey;
		signature = evt.Signature;
		mapTitle = "";
		mapVersion = "1.0";

		var payload = ParseMapPublishedPayload(evt);
		if (payload == null) return;

		if (payload.ManifestJson != null) manifestJson = payload.ManifestJson;
		if (payload.PublicKey != null) publicKey = payload.PublicKey;
		if (payload.Signature != null) signature = payload.Signature;
		if (payload.MapTitle != null) mapTitle = payload.MapTitle;
		if (payload.MapVersion != null) mapVersion = payload.MapVersion;
	}

	private bool ValidateMapPublishedRequiredFields(string manifestJson, string publicKey, string signature, ref string mapTitle, ref string mapVersion)
	{
		if (string.IsNullOrWhiteSpace(manifestJson) || string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(signature))
			return false;

		if (string.IsNullOrWhiteSpace(mapTitle))
			ExtractMapDetailsFromManifest(manifestJson, ref mapTitle, ref mapVersion);

		if (string.IsNullOrWhiteSpace(mapTitle) || !VerifyMapPublishedSignature(manifestJson, publicKey, signature))
			return false;

		return true;
	}

	private bool ValidateMapPublishedAuthorization(DataStoreService db, string mapTitle, string publicKey)
	{
		if (!MapMaintainerHelper.IsAuthorizedMaintainer(db, mapTitle, publicKey))
		{
			Console.WriteLine($"[ClusterEventService] Rejected map_published event: Map '{mapTitle}' is owned by another key");
			return false;
		}

		return true;
	}

	private bool ValidateMapPublishedGreenlight(DataStoreService db, string mapTitle, string mapVersion, string publicKey)
	{
		var stats = MapStatsHelper.GetStats(db, mapTitle, mapVersion, publicKey);
		if (stats == null || !stats.IsGreenlit)
		{
			Console.WriteLine($"[ClusterEventService] Rejected map_published event: Map '{mapTitle}' is not greenlit");
			return false;
		}

		return true;
	}


	private MapPublishedEventPayload? ParseMapPublishedPayload(ClusterEventDto evt)
	{
		if (string.IsNullOrWhiteSpace(evt.PayloadJson)) return null;
		try
		{
			return JsonSerializer.Deserialize<MapPublishedEventPayload>(evt.PayloadJson, JsonOpts);
		}
		catch
		{
			return null;
		}
	}

	private void ExtractMapDetailsFromManifest(string manifestJson, ref string mapTitle, ref string mapVersion)
	{
		try
		{
			using var doc = JsonDocument.Parse(manifestJson);
			if (doc.RootElement.TryGetProperty("MapName", out var mn)) mapTitle = mn.GetString() ?? "";
			if (doc.RootElement.TryGetProperty("Version", out var mv)) mapVersion = mv.GetString() ?? "1.0";
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ClusterEvents] could not read the title and version from a manifest: {ex.GetType().Name}: {ex.Message}");
		}
	}

	private bool VerifyMapPublishedSignature(string manifestJson, string publicKey, string signature)
	{
		byte[] mapBytes = Encoding.UTF8.GetBytes(manifestJson);
		string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(mapBytes, ".json");
		string mapHashStr = $"{canonicalBlake3}.json";

		bool sigValid = AuthorSignatureHelper.VerifySignature(publicKey, mapHashStr, signature)
		                || AuthorSignatureHelper.VerifySignature(publicKey, canonicalBlake3, signature)
		                || AuthorSignatureHelper.VerifySignature(publicKey, manifestJson, signature);

		if (!sigValid)
			Console.WriteLine($"[ClusterEventService] Rejected map_published event: Invalid signature for key {publicKey}");

		return sigValid;
	}

	private bool WriteManifestAndUpsert(string manifestJson, string mapTitle, string mapVersion, string compositeKey, string publicKey, DataStoreService db, ContentAddressableStorage cas)
	{
		string manifestDir = Path.Combine(cas.RootDirectory, "manifests");
		if (!Directory.Exists(manifestDir)) Directory.CreateDirectory(manifestDir);

		string manifestPath = Path.Combine(manifestDir, $"{compositeKey}_manifest.json");
		File.WriteAllText(manifestPath, manifestJson);
		string defaultPath = Path.Combine(manifestDir, $"{mapTitle}_manifest.json");
		File.WriteAllText(defaultPath, manifestJson);

		try
		{
			var doc = JsonDocument.Parse(manifestJson);
			db.Upsert("published_maps", compositeKey, doc);
			db.Upsert("published_maps", mapTitle, doc);
			MapMaintainerHelper.AddMaintainer(db, mapTitle, publicKey);
		}
		catch
		{
			return false;
		}

		return true;
	}
}
