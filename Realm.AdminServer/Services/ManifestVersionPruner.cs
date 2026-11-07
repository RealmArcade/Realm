using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Realm.Shared.Distribution;

namespace Realm.AdminServer.Services;

/// <summary>
///     The version-removal fallback of a manifest prune: after one version is deleted, work out what the map's remaining
///     published versions are, rewrite the default manifest to the newest of them, or delete the unversioned copies when
///     none is left, and keep the database rows in step. Every member takes its store, directory and target map as
///     parameters, so the prune reads no service state.
/// </summary>
internal static class ManifestVersionPruner
{
	internal static void HandleRemainingVersionsFallback(ContentAddressableStorage cas, DataStoreService db, string targetMap, string targetVersion, string manifestDir, ref int manifestsDeleted, List<string> deletedFiles)
	{
		var remainingVersions = new List<(string version, JsonDocument doc, string? filePath)>();

		CollectRemainingVersions(db, manifestDir, targetMap, targetVersion, remainingVersions);
		ApplyFallbackOperations(cas, db, manifestDir, targetMap, remainingVersions, ref manifestsDeleted, deletedFiles);
	}

	internal static void CollectRemainingVersions(DataStoreService db, string manifestDir, string targetMap, string targetVersion, List<(string version, JsonDocument doc, string? filePath)> remainingVersions)
	{
		CollectRemainingVersionsFromDirectory(manifestDir, targetMap, targetVersion, remainingVersions);
		CollectRemainingVersionsFromDatabase(db, targetMap, targetVersion, remainingVersions);
	}

	internal static void CollectRemainingVersionsFromDirectory(string manifestDir, string targetMap, string targetVersion, List<(string version, JsonDocument doc, string? filePath)> remainingVersions)
	{
		if (!Directory.Exists(manifestDir)) return;

		foreach (var filePath in Directory.EnumerateFiles(manifestDir, "*.json"))
		{
			TryAddVersionFromManifestFile(filePath, targetMap, targetVersion, remainingVersions);
		}
	}

	internal static void TryAddVersionFromManifestFile(string filePath, string targetMap, string targetVersion, List<(string version, JsonDocument doc, string? filePath)> remainingVersions)
	{
		try
		{
			var mf = MapManifest.LoadFromFile(filePath);

			bool isMapMatch = string.Equals(mf?.MapName, targetMap, StringComparison.OrdinalIgnoreCase)
			                  || string.Equals(mf?.MapName?.Replace(' ', '_'), targetMap.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase);

			if (!isMapMatch || string.Equals(mf?.Version, targetVersion, StringComparison.OrdinalIgnoreCase))
				return;

			var doc = JsonDocument.Parse(File.ReadAllText(filePath));
			remainingVersions.Add((mf!.Version ?? "1.0", doc, filePath));
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ClusterEvents] could not parse a manifest while pruning map versions: {ex.GetType().Name}: {ex.Message}");
		}
	}

	internal static void CollectRemainingVersionsFromDatabase(DataStoreService db, string targetMap, string targetVersion, List<(string version, JsonDocument doc, string? filePath)> remainingVersions)
	{
		foreach (var pair in db.GetAllWithKeys<JsonDocument>("published_maps"))
		{
			if (IsDbKeyMatchingTargetMapExact(pair.Key, targetMap)) continue;

			TryAddVersionFromDatabaseRecord(pair.Value, targetMap, targetVersion, remainingVersions);
		}
	}

	internal static bool IsDbKeyMatchingTargetMapExact(string dbKey, string targetMap)
	{
		return string.Equals(dbKey, targetMap, StringComparison.OrdinalIgnoreCase)
		       || string.Equals(dbKey.Replace(' ', '_'), targetMap.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase);
	}

	internal static void TryAddVersionFromDatabaseRecord(JsonDocument doc, string targetMap, string targetVersion, List<(string version, JsonDocument doc, string? filePath)> remainingVersions)
	{
		try
		{
			var root = doc.RootElement;
			string name = root.TryGetProperty("MapName", out var mn) ? mn.GetString() ?? "" : "";
			string ver = root.TryGetProperty("Version", out var vr) ? vr.GetString() ?? "" : "";

			bool isNameMatch = string.Equals(name, targetMap, StringComparison.OrdinalIgnoreCase)
			                   || string.Equals(name.Replace(' ', '_'), targetMap.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase);

			if (!isNameMatch || string.Equals(ver, targetVersion, StringComparison.OrdinalIgnoreCase))
				return;

			if (!HasVersionAlready(remainingVersions, ver))
				remainingVersions.Add((ver, doc, null));
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ClusterEvents] could not parse a version document while pruning map versions: {ex.GetType().Name}: {ex.Message}");
		}
	}

	internal static bool HasVersionAlready(List<(string version, JsonDocument doc, string? filePath)> remainingVersions, string version)
	{
		return remainingVersions.Any(r => string.Equals(r.version, version, StringComparison.OrdinalIgnoreCase));
	}

	internal static void ApplyFallbackOperations(ContentAddressableStorage cas, DataStoreService db, string manifestDir, string targetMap, List<(string version, JsonDocument doc, string? filePath)> remainingVersions, ref int manifestsDeleted, List<string> deletedFiles)
	{
		if (remainingVersions.Count > 0)
		{
			var latest = remainingVersions.OrderByDescending(r => r.version, StringComparer.OrdinalIgnoreCase).First();
			string defaultManifestPath = Path.Combine(manifestDir, $"{targetMap}_manifest.json");
			try
			{
				File.WriteAllText(defaultManifestPath, JsonSerializer.Serialize(latest.doc));
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"[ClusterEvents] could not rewrite the default manifest after pruning a version: {ex.GetType().Name}: {ex.Message}");
			}

			db.Upsert("published_maps", targetMap, latest.doc);
		}
		else
		{
			if (Directory.Exists(manifestDir))
			{
				var unversionedCandidates = new List<string>
				{
					Path.Combine(manifestDir, $"{targetMap}_manifest.json"),
					Path.Combine(manifestDir, $"{targetMap}.json"),
					Path.Combine(manifestDir, $"{targetMap.Replace(' ', '_')}_manifest.json"),
					Path.Combine(manifestDir, $"{targetMap.Replace(' ', '_')}.json")
				};
				foreach (var unvPath in unversionedCandidates)
				{
					if (File.Exists(unvPath))
					{
						try
						{
							File.Delete(unvPath);
							manifestsDeleted++;
							deletedFiles.Add(Path.GetFileName(unvPath));
						}
						catch
						{
						}
					}
				}
			}

			db.DeleteMany("published_maps", new[] { targetMap, targetMap.Replace(' ', '_'), targetMap.Replace('_', ' ') });
		}
	}
}
