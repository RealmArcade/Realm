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

/// <summary>admin_remove_manifest events and manifest deletion.</summary>
public partial class ClusterEventService
	{
	private bool ApplyAdminRemoveManifest(ClusterEventDto evt, DataStoreService db, ContentAddressableStorage cas, HashSet<string> adminPublicKeys)
	{
		var payload = ParseAdminRemoveManifestPayload(evt);
		string mapTitle = payload?.MapTitle ?? "";
		string? mapVersion = payload?.MapVersion;
		string adminPublicKey = payload?.AdminPublicKey ?? evt.PublicKey;
		string signature = payload?.Signature ?? evt.Signature;

		if (string.IsNullOrWhiteSpace(mapTitle))
			return false;

		if (!VerifyAdminRemoveManifestSignature(mapTitle, mapVersion, adminPublicKey, signature, adminPublicKeys))
			return false;

		var result = RemoveManifest(cas, db, mapTitle, mapVersion);
		return result.Success;
	}

	private AdminRemoveManifestEventPayload? ParseAdminRemoveManifestPayload(ClusterEventDto evt)
	{
		if (string.IsNullOrWhiteSpace(evt.PayloadJson)) return null;
		try
		{
			return JsonSerializer.Deserialize<AdminRemoveManifestEventPayload>(evt.PayloadJson, JsonOpts);
		}
		catch
		{
			return null;
		}
	}

	private bool VerifyAdminRemoveManifestSignature(string mapTitle, string? mapVersion, string adminPublicKey, string signature, HashSet<string> adminPublicKeys)
	{
		if (adminPublicKeys.Count == 0) return true;

		string targetVer = mapVersion ?? "all";
		string sigPayload1 = $"remove_manifest:{mapTitle.Trim().ToLowerInvariant()}:{targetVer.ToLowerInvariant()}";
		string sigPayload2 = $"remove_manifest:{mapTitle.Trim().ToLowerInvariant()}";

		bool isSigValid = false;
		if (!string.IsNullOrWhiteSpace(adminPublicKey) && adminPublicKeys.Contains(adminPublicKey.Trim()))
		{
			isSigValid = AuthorSignatureHelper.VerifySignature(adminPublicKey.Trim(), sigPayload1, signature)
			             || AuthorSignatureHelper.VerifySignature(adminPublicKey.Trim(), sigPayload2, signature)
			             || AdminBypassAuth.VerifyBypassToken(adminPublicKey.Trim(), mapTitle, targetVer, signature);
		}
		else
		{
			isSigValid = AuthorSignatureHelper.VerifySignatureAny(adminPublicKeys, sigPayload1, signature)
			             || AuthorSignatureHelper.VerifySignatureAny(adminPublicKeys, sigPayload2, signature)
			             || AdminBypassAuth.VerifyBypassToken(adminPublicKeys, mapTitle, targetVer, signature);
		}

		if (!isSigValid)
			Console.WriteLine($"[ClusterEventService] Rejected admin_remove_manifest event: Invalid admin signature for {mapTitle}");

		return isSigValid;
	}

	public RemoveManifestResponseDto RemoveManifest(ContentAddressableStorage cas, DataStoreService db, string mapTitle, string? mapVersion = null)
	{
		if (string.IsNullOrWhiteSpace(mapTitle))
			return new RemoveManifestResponseDto { Success = false, Message = "Map title cannot be empty." };

		string targetMap = mapTitle.Trim();
		string? targetVersion = string.IsNullOrWhiteSpace(mapVersion) ? null : mapVersion.Trim();
		bool allVersions = targetVersion == null;

		int manifestsDeleted = 0;
		int dbRecordsRemoved = 0;
		var deletedFiles = new List<string>();

		if (allVersions)
			RemoveAllManifestVersions(cas, db, targetMap, ref manifestsDeleted, ref dbRecordsRemoved, deletedFiles);
		else
			RemoveSpecificManifestVersion(cas, db, targetMap, targetVersion!, ref manifestsDeleted, ref dbRecordsRemoved, deletedFiles);

		return new RemoveManifestResponseDto
		{
			Success = true,
			MapTitle = targetMap,
			MapVersion = targetVersion,
			AllVersionsRemoved = allVersions,
			ManifestsDeleted = manifestsDeleted,
			DbRecordsRemoved = dbRecordsRemoved,
			DeletedManifestFiles = deletedFiles,
			Message = allVersions
				? $"Successfully removed all published manifest versions for map '{targetMap}'."
				: $"Successfully removed published manifest for map '{targetMap}' version '{targetVersion}'."
		};
	}

	private void RemoveAllManifestVersions(ContentAddressableStorage cas, DataStoreService db, string targetMap, ref int manifestsDeleted, ref int dbRecordsRemoved, List<string> deletedFiles)
	{
		string manifestDir = Path.Combine(cas.RootDirectory, "manifests");

		DeleteMatchingManifests(manifestDir, targetMap, ref manifestsDeleted, deletedFiles);
		RemovePublishedMapRecords(db, targetMap, ref dbRecordsRemoved);

		db.DeleteMany("map_ownership", new[] { targetMap, targetMap.ToLowerInvariant().Replace(" ", "-"), targetMap.Replace(" ", "_") });
		db.DeleteMany("map_maintainers", new[] { targetMap, targetMap.ToLowerInvariant().Replace(" ", "-"), targetMap.Replace(" ", "_") });
		db.Delete("map_stats", targetMap);
	}

	private void DeleteMatchingManifests(string manifestDir, string targetMap, ref int manifestsDeleted, List<string> deletedFiles)
	{
		if (!Directory.Exists(manifestDir)) return;

		foreach (var filePath in Directory.EnumerateFiles(manifestDir, "*.json"))
		{
			ProcessManifestForDeletion(filePath, targetMap, ref manifestsDeleted, deletedFiles);
		}
	}

	private void ProcessManifestForDeletion(string filePath, string targetMap, ref int manifestsDeleted, List<string> deletedFiles)
	{
		string fileName = Path.GetFileName(filePath);
		bool shouldDelete = fileName.StartsWith($"{targetMap}_", StringComparison.OrdinalIgnoreCase)
		                    || fileName.StartsWith($"{targetMap.Replace(' ', '_')}_", StringComparison.OrdinalIgnoreCase)
		                    || string.Equals(fileName, $"{targetMap}.json", StringComparison.OrdinalIgnoreCase)
		                    || string.Equals(fileName, $"{targetMap.Replace(' ', '_')}.json", StringComparison.OrdinalIgnoreCase);

		if (!shouldDelete)
			shouldDelete = ShouldDeleteManifestByContent(filePath, targetMap, null);

		if (shouldDelete)
			AttemptDeleteManifest(filePath, fileName, ref manifestsDeleted, deletedFiles);
	}

	private bool ShouldDeleteManifestByContent(string filePath, string targetMap, string? targetVersion)
	{
		try
		{
			var mf = MapManifest.LoadFromFile(filePath);
			if (mf == null) return false;

			bool nameMatch = string.Equals(mf.MapName, targetMap, StringComparison.OrdinalIgnoreCase)
			                 || string.Equals(mf.MapName?.Replace(' ', '_'), targetMap.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase);

			if (!nameMatch) return false;

			if (targetVersion != null && !string.Equals(mf.Version, targetVersion, StringComparison.OrdinalIgnoreCase))
				return false;

			return true;
		}
		catch
		{
			return false;
		}
	}

	private void AttemptDeleteManifest(string filePath, string fileName, ref int manifestsDeleted, List<string> deletedFiles)
	{
		try
		{
			File.Delete(filePath);
			manifestsDeleted++;
			deletedFiles.Add(fileName);
		}
		catch
		{
		}
	}

	private void RemovePublishedMapRecords(DataStoreService db, string targetMap, ref int dbRecordsRemoved)
	{
		var publishedKeysToDelete = new List<string>();
		foreach (var pair in db.GetAllWithKeys<JsonDocument>("published_maps"))
		{
			if (IsPublishedMapRecordMatch(pair, targetMap))
			{
				publishedKeysToDelete.Add(pair.Key);
			}
		}

		if (publishedKeysToDelete.Count > 0)
		{
			db.DeleteMany("published_maps", publishedKeysToDelete);
			dbRecordsRemoved += publishedKeysToDelete.Count;
		}
	}

	private bool IsPublishedMapRecordMatch(KeyValuePair<string, JsonDocument> pair, string targetMap)
	{
		if (IsKeyMatch(pair.Key, targetMap))
			return true;

		return IsDocumentNameMatch(pair.Value, targetMap);
	}

	private bool IsKeyMatch(string key, string targetMap)
	{
		return string.Equals(key, targetMap, StringComparison.OrdinalIgnoreCase)
		       || string.Equals(key.Replace(' ', '_'), targetMap.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase)
		       || key.StartsWith($"{targetMap}_", StringComparison.OrdinalIgnoreCase)
		       || key.StartsWith($"{targetMap.Replace(' ', '_')}_", StringComparison.OrdinalIgnoreCase);
	}

	private bool IsDocumentNameMatch(JsonDocument document, string targetMap)
	{
		try
		{
			var root = document.RootElement;
			string name = root.TryGetProperty("MapName", out var mn) ? mn.GetString() ?? "" : "";
			return string.Equals(name, targetMap, StringComparison.OrdinalIgnoreCase)
			       || string.Equals(name.Replace(' ', '_'), targetMap.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}


	private void RemoveSpecificManifestVersion(ContentAddressableStorage cas, DataStoreService db, string targetMap, string targetVersion, ref int manifestsDeleted, ref int dbRecordsRemoved, List<string> deletedFiles)
	{
		string manifestDir = Path.Combine(cas.RootDirectory, "manifests");
		string compositeKey = $"{targetMap}_{targetVersion}";

		DeleteSpecificManifests(manifestDir, targetMap, targetVersion, compositeKey, ref manifestsDeleted, deletedFiles);
		RemoveSpecificPublishedRecords(db, targetMap, targetVersion, compositeKey, ref dbRecordsRemoved);

		ManifestVersionPruner.HandleRemainingVersionsFallback(cas, db, targetMap, targetVersion, manifestDir, ref manifestsDeleted, deletedFiles);
		db.Delete("map_stats", compositeKey);
	}

	private void DeleteSpecificManifests(string manifestDir, string targetMap, string targetVersion, string compositeKey, ref int manifestsDeleted, List<string> deletedFiles)
	{
		var candidateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			$"{compositeKey}_manifest.json", $"{compositeKey}.json",
			$"{compositeKey.Replace(' ', '_')}_manifest.json", $"{compositeKey.Replace(' ', '_')}.json",
			$"{compositeKey.Replace('_', ' ')}_manifest.json", $"{compositeKey.Replace('_', ' ')}.json"
		};

		if (!Directory.Exists(manifestDir)) return;

		foreach (var filePath in Directory.EnumerateFiles(manifestDir, "*.json"))
		{
			ProcessSpecificManifestForDeletion(filePath, targetMap, targetVersion, candidateNames, ref manifestsDeleted, deletedFiles);
		}
	}

	private void ProcessSpecificManifestForDeletion(string filePath, string targetMap, string targetVersion, HashSet<string> candidateNames, ref int manifestsDeleted, List<string> deletedFiles)
	{
		string fileName = Path.GetFileName(filePath);
		bool shouldDelete = candidateNames.Contains(fileName);

		if (!shouldDelete)
			shouldDelete = ShouldDeleteManifestByContent(filePath, targetMap, targetVersion);

		if (shouldDelete)
			AttemptDeleteManifest(filePath, fileName, ref manifestsDeleted, deletedFiles);
	}

	private void RemoveSpecificPublishedRecords(DataStoreService db, string targetMap, string targetVersion, string compositeKey, ref int dbRecordsRemoved)
	{
		var publishedKeysToDelete = new List<string>();
		foreach (var pair in db.GetAllWithKeys<JsonDocument>("published_maps"))
		{
			if (IsSpecificPublishedRecordMatch(pair, targetMap, targetVersion, compositeKey))
			{
				publishedKeysToDelete.Add(pair.Key);
			}
		}

		if (publishedKeysToDelete.Count > 0)
		{
			db.DeleteMany("published_maps", publishedKeysToDelete);
			dbRecordsRemoved += publishedKeysToDelete.Count;
		}
	}

	private bool IsSpecificPublishedRecordMatch(KeyValuePair<string, JsonDocument> pair, string targetMap, string targetVersion, string compositeKey)
	{
		if (IsSpecificKeyMatch(pair.Key, compositeKey))
			return true;

		return IsSpecificDocumentMatch(pair.Value, targetMap, targetVersion);
	}

	private bool IsSpecificKeyMatch(string key, string compositeKey)
	{
		return string.Equals(key, compositeKey, StringComparison.OrdinalIgnoreCase)
		       || string.Equals(key.Replace(' ', '_'), compositeKey.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase);
	}

	private bool IsSpecificDocumentMatch(JsonDocument document, string targetMap, string targetVersion)
	{
		try
		{
			var root = document.RootElement;
			string name = root.TryGetProperty("MapName", out var mn) ? mn.GetString() ?? "" : "";
			string ver = root.TryGetProperty("Version", out var vr) ? vr.GetString() ?? "" : "";

			bool nameMatch = string.Equals(name, targetMap, StringComparison.OrdinalIgnoreCase)
			                 || string.Equals(name.Replace(' ', '_'), targetMap.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase);
			bool versionMatch = string.Equals(ver, targetVersion, StringComparison.OrdinalIgnoreCase);

			return nameMatch && versionMatch;
		}
		catch
		{
			return false;
		}
	}
}
