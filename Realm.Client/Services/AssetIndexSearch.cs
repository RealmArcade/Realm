using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;
using Realm.EditorAPI;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

public static class AssetIndexSearch
{
	public static List<IndexedAsset> Search(
		ILiteCollection<IndexedAsset> assets, object syncLock, Func<string, string?> latestVersionForMap,
		string? searchTerm,
		IReadOnlyCollection<string>? allowedExtensions = null,
		string? directoryFilter = null,
		bool requireRealmMetadata = false,
		AssetType? requiredAssetType = null,
		string? mapNameFilter = null,
		string? mapVersionFilter = null,
		bool requirePlayerColorMask = false)
	{
		try
		{
			lock (syncLock)
			{
				var query = assets.Query();

				query = ApplyMapAndDirectoryFilters(query, mapNameFilter, mapVersionFilter, directoryFilter, latestVersionForMap);

				HashSet<string>? extensionFilterSet = null;
				query = ApplyExtensionFilters(query, allowedExtensions, out extensionFilterSet);

				query = ApplyMetadataFilters(query, requireRealmMetadata, requiredAssetType, requirePlayerColorMask);

				if (extensionFilterSet != null)
				{
					query = query.Where(x => extensionFilterSet.Contains(x.Extension));
				}

				query = ApplySearchTermFilter(query, searchTerm);
				var results = query.ToList();
				return DeduplicateSearchResults(results).OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase).ToList();
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[AssetIndexService] SearchAssets error: {ex.Message}");
			return new List<IndexedAsset>();
		}
	}

	private static ILiteQueryable<IndexedAsset> ApplyMapAndDirectoryFilters(ILiteQueryable<IndexedAsset> query, string? mapNameFilter, string? mapVersionFilter, string? directoryFilter, Func<string, string?> latestVersionForMap)
	{
		if (!string.IsNullOrWhiteSpace(mapNameFilter))
		{
			string targetVersion = mapVersionFilter ?? "latest";
			if (string.Equals(targetVersion, "latest", StringComparison.OrdinalIgnoreCase))
			{
				targetVersion = latestVersionForMap(mapNameFilter) ?? "";
			}

			if (!string.IsNullOrEmpty(targetVersion))
			{
				return query.Where(x => x.MapName == mapNameFilter && x.MapVersion == targetVersion);
			}

			return query.Where(x => x.MapName == mapNameFilter);
		}
		else if (!string.IsNullOrWhiteSpace(directoryFilter))
		{
			string normalizedDir = NormalizePath(directoryFilter);
			return query.Where(x => x.DirectoryPath == normalizedDir);
		}

		return query;
	}

	private static ILiteQueryable<IndexedAsset> ApplyExtensionFilters(ILiteQueryable<IndexedAsset> query, IReadOnlyCollection<string>? allowedExtensions, out HashSet<string>? extensionFilterSet)
	{
		extensionFilterSet = null;
		if (allowedExtensions != null && allowedExtensions.Count > 0)
		{
			var normalizedExtensions = allowedExtensions
				.Select(e => e.Trim().ToLowerInvariant())
				.Select(e => e.StartsWith(".") ? e : "." + e)
				.ToArray();

			extensionFilterSet = normalizedExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);

			if (normalizedExtensions.Length == 1)
			{
				string singleExt = normalizedExtensions[0];
				return query.Where(x => x.Extension == singleExt);
			}
			else
			{
				var bsonExtensions = normalizedExtensions.Select(e => new BsonValue(e)).ToArray();
				return query.Where(Query.In("Extension", bsonExtensions));
			}
		}

		return query;
	}

	private static ILiteQueryable<IndexedAsset> ApplyMetadataFilters(ILiteQueryable<IndexedAsset> query, bool requireRealmMetadata, AssetType? requiredAssetType, bool requirePlayerColorMask)
	{
		var resultQuery = query;
		if (requireRealmMetadata)
		{
			resultQuery = resultQuery.Where(x => x.HasRealmMetadata);
		}

		if (requiredAssetType.HasValue)
		{
			resultQuery = resultQuery.Where(x => x.AssetType == requiredAssetType.Value);
		}

		if (requirePlayerColorMask)
		{
			resultQuery = resultQuery.Where(x => x.HasPlayerColorMask);
		}

		return resultQuery;
	}

	private static ILiteQueryable<IndexedAsset> ApplySearchTermFilter(ILiteQueryable<IndexedAsset> query, string? searchTerm)
	{
		string[] terms = !string.IsNullOrWhiteSpace(searchTerm) ? searchTerm.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) : [];

		foreach (var queryTerm in terms)
		{
			query = query.Where(x => x.FileName.Contains(queryTerm, StringComparison.OrdinalIgnoreCase) ||
			                          x.FilePath.Contains(queryTerm, StringComparison.OrdinalIgnoreCase) ||
			                          (x.Tags != null && x.Tags.Any(t => t.Contains(queryTerm, StringComparison.OrdinalIgnoreCase)))
			                    );
		}

		return query;
	}

	private static List<IndexedAsset> DeduplicateSearchResults(List<IndexedAsset> filtered)
	{
		var seenBlake3 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var deduplicated = new List<IndexedAsset>();

		foreach (var item in filtered)
		{
			string blake3 = GetCanonicalBlake3(item);
			if (string.IsNullOrEmpty(blake3) || seenBlake3.Add(blake3))
			{
				deduplicated.Add(item);
			}
		}

		return deduplicated;
	}

	public static string GetCanonicalBlake3(IndexedAsset asset)
	{
		if (!string.IsNullOrEmpty(asset.Blake3))
		{
			return asset.Blake3;
		}

		if (asset.FilePath.StartsWith(AssetIndexService.GlobalCasAssetsDirectory + "/", StringComparison.OrdinalIgnoreCase) ||
		    asset.FilePath.StartsWith(AssetIndexService.GlobalCasAssetsDirectory + "\\", StringComparison.OrdinalIgnoreCase))
		{
			return ContentAddressableStorage.NormalizeBlake3Hash(Path.GetFileName(asset.FilePath));
		}

		return ContentAddressableStorage.NormalizeBlake3Hash(asset.FileName);
	}

	private static string NormalizePath(string path) => AssetIndexPaths.Normalize(path);
}
