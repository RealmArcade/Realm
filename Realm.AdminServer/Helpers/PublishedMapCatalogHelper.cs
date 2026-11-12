using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Realm.AdminServer.Services;
using Realm.Shared.Distribution;

namespace Realm.AdminServer.Helpers;

public class PublishedMapEntry
{
	public DiscoveryMapDto Dto { get; init; } = new();
	public string AuthorPublicKey { get; init; } = string.Empty;
	public List<string> MaintainerPublicKeys { get; init; } = new();
	public List<string> Contributors { get; init; } = new();
}

public static class PublishedMapCatalogHelper
{
	public static List<PublishedMapEntry> BuildCatalog(
		DataStoreService db,
		ContentAddressableStorage cas,
		bool includeTestVersions = false)
	{
		var catalog = new List<PublishedMapEntry>();
		var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		var publishedDocs = db.GetAll<JsonDocument>("published_maps");
		foreach (var doc in publishedDocs)
		{
			var entry = TryBuildEntryFromDocument(doc.RootElement, db, cas, includeTestVersions, seenKeys);
			if (entry != null)
			{
				catalog.Add(entry);
			}
		}

		string manifestDir = Path.Combine(cas.RootDirectory, "manifests");
		if (Directory.Exists(manifestDir))
		{
			foreach (string file in Directory.GetFiles(manifestDir, "*_manifest.json"))
			{
				var entry = TryBuildEntryFromManifestFile(file, db, cas, includeTestVersions, seenKeys);
				if (entry != null)
				{
					catalog.Add(entry);
				}
			}
		}

		return catalog;
	}

	public static List<PublishedMapEntry> GroupLatestByTitle(IEnumerable<PublishedMapEntry> entries)
	{
		return entries
			.GroupBy(e => e.Dto.Title.Trim(), StringComparer.OrdinalIgnoreCase)
			.Select(g => g
				.OrderBy(e => e.Dto.IsTestVersion ? 1 : 0)
				.ThenByDescending(e => e.Dto.Version, Comparer<string>.Create(CompareVersions))
				.First())
			.ToList();
	}

	public static bool MatchesCreator(PublishedMapEntry entry, string pubKey, string creatorName)
	{
		if (!string.IsNullOrWhiteSpace(pubKey))
		{
			if (string.Equals(entry.AuthorPublicKey, pubKey, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			if (entry.MaintainerPublicKeys.Any(k => string.Equals(k, pubKey, StringComparison.OrdinalIgnoreCase)))
			{
				return true;
			}
		}

		if (!string.IsNullOrWhiteSpace(creatorName))
		{
			if (string.Equals(entry.Dto.Creator, creatorName, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			if (entry.Contributors.Any(c => string.Equals(c, creatorName, StringComparison.OrdinalIgnoreCase)))
			{
				return true;
			}
		}

		return false;
	}

	public static int CompareVersions(string? a, string? b)
	{
		string[] partsA = (a ?? string.Empty).Split('.', '-', '+');
		string[] partsB = (b ?? string.Empty).Split('.', '-', '+');
		int max = Math.Max(partsA.Length, partsB.Length);
		for (int i = 0; i < max; i++)
		{
			string segA = i < partsA.Length ? partsA[i] : "0";
			string segB = i < partsB.Length ? partsB[i] : "0";
			bool isNumA = int.TryParse(segA, out int numA);
			bool isNumB = int.TryParse(segB, out int numB);
			int cmp = isNumA && isNumB
				? numA.CompareTo(numB)
				: string.Compare(segA, segB, StringComparison.OrdinalIgnoreCase);
			if (cmp != 0)
			{
				return cmp;
			}
		}

		return 0;
	}

	private static PublishedMapEntry? TryBuildEntryFromDocument(
		JsonElement root,
		DataStoreService db,
		ContentAddressableStorage cas,
		bool includeTestVersions,
		HashSet<string> seenKeys)
	{
		try
		{
			string mapTitle = ReadString(root, "MapName");
			string mapVersion = ReadString(root, "Version", "1.0.0");
			if (string.IsNullOrWhiteSpace(mapTitle))
			{
				return null;
			}

			bool isTestVersion = root.TryGetProperty("IsTestVersion", out var testProp) && testProp.ValueKind == JsonValueKind.True;
			if (isTestVersion && !includeTestVersions)
			{
				return null;
			}

			string compositeKey = isTestVersion
				? MapMaintainerHelper.GetTestCompositeKey(mapTitle, mapVersion)
				: $"{mapTitle}_{mapVersion}";
			if (!seenKeys.Add(compositeKey))
			{
				return null;
			}

			var manifestFiles = ExtractFilesDictionary(root);
			return BuildEntryCore(compositeKey, mapTitle, mapVersion, isTestVersion, root, manifestFiles, db, cas);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[AdminServer] skipped catalog map document: {ex.GetType().Name}: {ex.Message}");
			return null;
		}
	}

	private static PublishedMapEntry? TryBuildEntryFromManifestFile(
		string filePath,
		DataStoreService db,
		ContentAddressableStorage cas,
		bool includeTestVersions,
		HashSet<string> seenKeys)
	{
		try
		{
			string json = File.ReadAllText(filePath);
			var manifest = MapManifest.LoadFromJson(json);
			if (manifest == null || string.IsNullOrWhiteSpace(manifest.MapName))
			{
				return null;
			}

			string mapTitle = manifest.MapName;
			string mapVersion = string.IsNullOrWhiteSpace(manifest.Version) ? "1.0.0" : manifest.Version;
			string fileName = Path.GetFileName(filePath);
			bool isTestVersion = manifest.IsTestVersion || fileName.Contains("_test_", StringComparison.OrdinalIgnoreCase);
			if (isTestVersion && !includeTestVersions)
			{
				return null;
			}

			string compositeKey = isTestVersion
				? MapMaintainerHelper.GetTestCompositeKey(mapTitle, mapVersion)
				: $"{mapTitle}_{mapVersion}";
			if (!seenKeys.Add(compositeKey))
			{
				return null;
			}

			using var doc = JsonDocument.Parse(json);
			return BuildEntryCore(compositeKey, mapTitle, mapVersion, isTestVersion, doc.RootElement, manifest.Files, db, cas);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[AdminServer] skipped manifest catalog file '{filePath}': {ex.GetType().Name}: {ex.Message}");
			return null;
		}
	}

	private static PublishedMapEntry BuildEntryCore(
		string compositeKey,
		string mapTitle,
		string mapVersion,
		bool isTestVersion,
		JsonElement root,
		Dictionary<string, string>? manifestFiles,
		DataStoreService db,
		ContentAddressableStorage cas)
	{
		string authorKey = ReadString(root, "author_key");
		if (string.IsNullOrEmpty(authorKey))
		{
			authorKey = db.Get<string>("map_ownership", mapTitle) ?? string.Empty;
		}

		var contributors = ReadStringArray(root, "Contributors");
		string creator = ResolveCreatorName(root, authorKey, contributors, db);

		string description = ReadString(root, "Description");
		var tags = ReadStringArray(root, "Tags");
		var attributionSourceMaps = ReadStringArray(root, "Attribution_SourceMap");

		long totalBytes = 0;
		string thumbnailHash = string.Empty;
		var screenshotHashes = new List<string>();
		string? metadataFileHash = null;

		if (manifestFiles != null)
		{
			foreach (var fileProp in manifestFiles)
			{
				string norm = ContentAddressableStorage.NormalizeBlake3Hash(fileProp.Value);
				MapMetadataHelper.ParseManifestFileDetails(
					cas,
					fileProp.Key,
					norm,
					ref metadataFileHash,
					ref thumbnailHash,
					screenshotHashes,
					ref totalBytes);
			}
		}

		MapMetadataHelper.ParseMapMetadataDetails(
			cas,
			metadataFileHash,
			ref description,
			tags,
			out string engineVersion,
			out string maxPlayers,
			out string genre);

		if (string.IsNullOrEmpty(thumbnailHash) && screenshotHashes.Count > 0)
		{
			thumbnailHash = screenshotHashes[0];
		}

		var stats = MapStatsHelper.GetStats(db, mapTitle, mapVersion, null);
		bool isGreenlit = stats != null && stats.IsGreenlit;
		float avgRating = stats != null && stats.ReviewsCount > 0 ? (float)stats.AverageRating : 0f;
		var awards = MapAwardsHelper.GetAwards(stats);
		var maintainerKeys = db.Get<List<string>>("map_maintainers", mapTitle) ?? new List<string>();

		var dto = new DiscoveryMapDto
		{
			MapId = compositeKey,
			Title = mapTitle,
			Version = mapVersion,
			Creator = creator,
			Description = description,
			Genre = genre,
			ThumbnailHash = thumbnailHash,
			ThumbnailUrl = string.Empty,
			Screenshots = screenshotHashes.Take(6).ToList(),
			Features = tags.ToList(),
			Tags = tags,
			RatingStars = avgRating,
			TotalReviews = stats?.ReviewsCount ?? 0,
			VerifiedGoodReviews = stats?.VerifiedGoodReviewsCount ?? 0,
			AverageRating = stats != null && stats.ReviewsCount > 0 ? stats.AverageRating : 0.0,
			PlaytimeMinutes = (int)Math.Round(stats?.TotalPlaytimeMinutes ?? 0),
			GamesPlayed = stats?.TotalGamesPlayed ?? 0,
			TotalSizeBytes = totalBytes,
			FileSizeFormatted = FormatFileSize(totalBytes),
			EngineVersion = engineVersion,
			MaxPlayers = maxPlayers,
			IsGreenlit = isGreenlit,
			IsTestVersion = isTestVersion,
			Awards = awards,
			Attribution_SourceMap = attributionSourceMaps
		};

		return new PublishedMapEntry
		{
			Dto = dto,
			AuthorPublicKey = authorKey,
			MaintainerPublicKeys = maintainerKeys,
			Contributors = contributors
		};
	}

	private static string ResolveCreatorName(
		JsonElement root,
		string ownerKey,
		List<string> contributors,
		DataStoreService db)
	{
		string author = ReadString(root, "Author");
		if (!string.IsNullOrWhiteSpace(author))
		{
			return author;
		}

		if (contributors.Count > 0)
		{
			return contributors[0];
		}

		if (!string.IsNullOrWhiteSpace(ownerKey))
		{
			var creatorDoc = db.Get<JsonDocument>("creators", ownerKey);
			if (creatorDoc != null && creatorDoc.RootElement.TryGetProperty("username", out var un))
			{
				string username = un.GetString() ?? string.Empty;
				if (!string.IsNullOrWhiteSpace(username))
				{
					return username;
				}
			}
		}

		return string.Empty;
	}

	private static Dictionary<string, string>? ExtractFilesDictionary(JsonElement root)
	{
		if (root.TryGetProperty("Files", out var filesProp) && filesProp.ValueKind == JsonValueKind.Object)
		{
			var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var fileProp in filesProp.EnumerateObject())
			{
				dict[fileProp.Name] = fileProp.Value.GetString() ?? string.Empty;
			}

			return dict;
		}

		try
		{
			var manifestObj = MapManifest.LoadFromJson(root.GetRawText());
			return manifestObj?.Files;
		}
		catch
		{
			return null;
		}
	}

	private static string ReadString(JsonElement root, string propertyName, string fallback = "")
	{
		if (root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
		{
			return prop.GetString() ?? fallback;
		}

		return fallback;
	}

	private static List<string> ReadStringArray(JsonElement root, string propertyName)
	{
		var result = new List<string>();
		if (root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Array)
		{
			foreach (var el in prop.EnumerateArray())
			{
				if (el.ValueKind == JsonValueKind.String)
				{
					string? val = el.GetString();
					if (!string.IsNullOrWhiteSpace(val))
					{
						result.Add(val.Trim());
					}
				}
			}
		}

		return result;
	}

	private static string FormatFileSize(long totalBytes)
	{
		if (totalBytes <= 0)
		{
			return "0 MB";
		}

		return totalBytes > 1048576
			? $"{(double)totalBytes / 1048576.0:F2} MB"
			: $"{Math.Max(1.0, (double)totalBytes / 1024.0):F0} KB";
	}
}
