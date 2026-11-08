using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LiteDB;
using Realm.Client.Utils;
using Realm.EditorAPI;
using Realm.Shared.Distribution;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Realm.Client.Services;

/// <summary>
///     One index and the rows that belong to it: the database to checkpoint, the asset, folder and package
///     collections, the content-addressed store it reads, and the directory that store keeps its files in.
///     Built once per operation so a reopened or rebuilt database is never captured by a stale reference.
/// </summary>
internal readonly record struct AssetIndexTarget(
	LiteDatabase Database,
	ILiteCollection<IndexedAsset> Assets,
	ILiteCollection<IndexedFolder> Folders,
	ILiteCollection<IndexedMapPackage> Packages,
	ContentAddressableStorage Storage,
	string StorageDirectory);

/// <summary>
///     Recording a map manifest in the asset index: the package row that says a map at a version is known,
///     and one asset row per manifest entry, updated in place when the same content is already indexed and
///     inserted when it is new. Asset type, tags, team-colour mask and chroma key come from the sidecar
///     metadata first and from the file name, folders and manifest tags after that.
/// </summary>
internal static class AssetIndexManifestRegistration
{
	internal static List<string> Register(AssetIndexTarget target, MapManifest manifest, string manifestPath, bool isP2P)
	{
		var indexedPaths = new List<string>();
		if (manifest == null) return indexedPaths;

		string mapName = !string.IsNullOrWhiteSpace(manifest.MapName) ? manifest.MapName.Trim() : Path.GetFileNameWithoutExtension(manifestPath).Replace("_manifest", "");
		string mapVersion = !string.IsNullOrWhiteSpace(manifest.Version) ? manifest.Version.Trim() : "1.0.0";

		UpsertManifestPackage(manifest, manifestPath, isP2P, mapName, mapVersion, target.Packages);

		if (manifest.Files != null)
		{
			ProcessManifestFiles(manifest, mapName, mapVersion, target.Storage, target.StorageDirectory, target.Assets, indexedPaths);
		}

		target.Database.Checkpoint();
		return indexedPaths;
	}

	private static void UpsertManifestPackage(MapManifest manifest, string manifestPath, bool isP2P, string mapName, string mapVersion, ILiteCollection<IndexedMapPackage> mapPackageCollection)
	{
		var pkg = mapPackageCollection.FindOne(p => p.MapName == mapName && p.MapVersion == mapVersion);
		pkg ??= new IndexedMapPackage();
		pkg.MapName = mapName;
		pkg.MapVersion = mapVersion;
		pkg.ManifestPath = manifestPath;
		pkg.DownloadedUtc = DateTime.UtcNow;
		pkg.IsP2P = isP2P;
		pkg.AssetHashes = manifest.Files != null ? manifest.Files.Values.Select(v => ContentAddressableStorage.NormalizeBlake3Hash(v)).ToList() : new List<string>();
		mapPackageCollection.Upsert(pkg);
	}

	private static void ProcessManifestFiles(MapManifest manifest, string mapName, string mapVersion, ContentAddressableStorage storage, string storageDirectory, ILiteCollection<IndexedAsset> assetCollection, List<string> indexedPaths)
	{
		var existingAssetMap = assetCollection.Find(Query.EQ("DirectoryPath", storageDirectory)).ToDictionary(x => x.FilePath, StringComparer.OrdinalIgnoreCase);
		var processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var assetsToUpdate = new List<IndexedAsset>();
		var assetsToInsert = new List<IndexedAsset>();

		foreach (var kvp in manifest.Files)
		{
			ProcessSingleManifestFile(manifest, kvp.Key, kvp.Value, mapName, mapVersion, storage, storageDirectory, existingAssetMap, processedPaths, assetsToUpdate, assetsToInsert, indexedPaths);
		}

		if (assetsToUpdate.Count > 0) assetCollection.Update(assetsToUpdate);
		if (assetsToInsert.Count > 0) assetCollection.Insert(assetsToInsert);
	}

	private static void ProcessSingleManifestFile(MapManifest manifest, string virtualPath, string hash, string mapName, string mapVersion, ContentAddressableStorage storage, string storageDirectory, Dictionary<string, IndexedAsset> existingAssetMap, HashSet<string> processedPaths, List<IndexedAsset> assetsToUpdate, List<IndexedAsset> assetsToInsert, List<string> indexedPaths)
	{
		string norm = ContentAddressableStorage.NormalizeBlake3Hash(hash);
		string? resolvedPath = storage.FindAssetFilePath(norm);
		if (resolvedPath == null || !File.Exists(resolvedPath)) return;

		string normPath = AssetIndexPaths.Normalize(resolvedPath);
		indexedPaths.Add(normPath);

		if (!processedPaths.Add(normPath)) return;

		if (existingAssetMap.TryGetValue(normPath, out var existingAsset))
		{
			UpdateExistingManifestAsset(existingAsset, virtualPath, norm, normPath, mapName, mapVersion, storage, storageDirectory, assetsToUpdate);
		}
		else
		{
			InsertNewManifestAsset(manifest, virtualPath, norm, normPath, mapName, mapVersion, storage, storageDirectory, existingAssetMap, assetsToInsert);
		}
	}

	private static void UpdateExistingManifestAsset(IndexedAsset existingAsset, string virtualPath, string norm, string normPath, string mapName, string mapVersion, ContentAddressableStorage storage, string storageDirectory, List<IndexedAsset> assetsToUpdate)
	{
		bool needsUpdate = false;
		string? existingMetaJson = storage.GetAssetMetadata(norm);
		var parsed = AssetRecordFactory.ParseMetadataHeaders(existingMetaJson, virtualPath);

		if (!parsed.AssetType.HasValue || parsed.Tags.Count == 0 || !parsed.SupportsTeamColor.HasValue)
		{
			string? diskMeta = RealmMetadataHelper.ExtractMetadata(normPath);
			if (!string.IsNullOrEmpty(diskMeta))
			{
				var diskParsed = AssetRecordFactory.ParseMetadataHeaders(diskMeta, virtualPath);
				if (!parsed.AssetType.HasValue) parsed.AssetType = diskParsed.AssetType;
				if (parsed.Tags.Count == 0) parsed.Tags = diskParsed.Tags;
				if (!parsed.SupportsTeamColor.HasValue) parsed.SupportsTeamColor = diskParsed.SupportsTeamColor;
				if (string.IsNullOrEmpty(parsed.ChromaKey)) parsed.ChromaKey = diskParsed.ChromaKey;
				if (string.IsNullOrEmpty(existingMetaJson)) existingMetaJson = diskMeta;
			}
		}

		needsUpdate |= TryUpdateAssetType(existingAsset, virtualPath, parsed.AssetType, existingMetaJson);
		needsUpdate |= TryUpdateAssetProperties(existingAsset, parsed.SupportsTeamColor, parsed.ChromaKey, existingMetaJson, normPath);
		needsUpdate |= TryUpdateAssetMapInfo(existingAsset, mapName, mapVersion, storageDirectory);

		if (needsUpdate) assetsToUpdate.Add(existingAsset);

		AssetRecordFactory.EnqueueThumbnailRequestIfNeeded(existingAsset.Extension, normPath, norm);
	}

	private static bool TryUpdateAssetType(IndexedAsset existingAsset, string virtualPath, AssetType? parsedType, string? existingMetaJson)
	{
		AssetType? existingAssetType = ResolveAssetTypeFromParsed(parsedType, virtualPath);
		if (existingAsset.AssetType == existingAssetType) return false;

		existingAsset.AssetType = existingAssetType;
		existingAsset.HasRealmMetadata = !string.IsNullOrEmpty(existingMetaJson) || existingAsset.HasRealmMetadata || existingAssetType.HasValue;
		return true;
	}

	private static bool TryUpdateAssetProperties(IndexedAsset existingAsset, bool? parsedSupportsTeamColor, string? parsedChromaKey, string? existingMetaJson, string normPath)
	{
		bool existingHasMask = parsedSupportsTeamColor ?? AssetRecordFactory.DetermineHasPlayerColorMask(existingMetaJson, normPath);
		string resolvedChroma = parsedChromaKey ?? string.Empty;

		if (existingAsset.HasPlayerColorMask == existingHasMask && existingAsset.ChromaKey == resolvedChroma) return false;

		existingAsset.HasPlayerColorMask = existingHasMask;
		existingAsset.ChromaKey = resolvedChroma;
		return true;
	}

	private static bool TryUpdateAssetMapInfo(IndexedAsset existingAsset, string mapName, string mapVersion, string storageDirectory)
	{
		bool updated = false;

		if (string.IsNullOrEmpty(existingAsset.MapName) || existingAsset.MapName != mapName)
		{
			existingAsset.MapName = mapName;
			updated = true;
		}

		if (string.IsNullOrEmpty(existingAsset.MapVersion) || existingAsset.MapVersion != mapVersion)
		{
			existingAsset.MapVersion = mapVersion;
			updated = true;
		}

		if (existingAsset.DirectoryPath != storageDirectory)
		{
			existingAsset.DirectoryPath = storageDirectory;
			updated = true;
		}

		return updated;
	}

	private static AssetType? ResolveAssetTypeFromParsed(AssetType? parsedType, string virtualPath)
	{
		if (parsedType.HasValue) return parsedType;

		string? dir = Path.GetDirectoryName(virtualPath);
		if (string.IsNullOrEmpty(dir)) return null;

		string[] dirParts = dir.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (var part in dirParts.Reverse())
		{
			var norm = RealmMetadataHelper.NormalizeAssetType(part);
			if (norm.HasValue && RealmMetadataHelper.IsValidAssetTypeForExtension(virtualPath, norm.Value, out _))
			{
				return norm;
			}
		}

		return null;
	}

	private static void InsertNewManifestAsset(MapManifest manifest, string virtualPath, string norm, string normPath, string mapName, string mapVersion, ContentAddressableStorage storage, string storageDirectory, Dictionary<string, IndexedAsset> existingAssetMap, List<IndexedAsset> assetsToInsert)
	{
		var fi = new FileInfo(normPath);
		var asset = CreateBaseManifestAsset(virtualPath, norm, normPath, mapName, mapVersion, storageDirectory, fi);

		string? metaJson = storage.GetAssetMetadata(norm);
		var parsedMeta = AssetRecordFactory.ParseMetadataHeaders(metaJson, virtualPath);

		if (!parsedMeta.AssetType.HasValue || parsedMeta.Tags.Count == 0 || !parsedMeta.SupportsTeamColor.HasValue)
		{
			string? diskMeta = RealmMetadataHelper.ExtractMetadata(normPath);
			if (!string.IsNullOrEmpty(diskMeta))
			{
				var diskParsed = AssetRecordFactory.ParseMetadataHeaders(diskMeta, virtualPath);
				if (!parsedMeta.AssetType.HasValue) parsedMeta.AssetType = diskParsed.AssetType;
				if (parsedMeta.Tags.Count == 0) parsedMeta.Tags = diskParsed.Tags;
				if (!parsedMeta.SupportsTeamColor.HasValue) parsedMeta.SupportsTeamColor = diskParsed.SupportsTeamColor;
				if (string.IsNullOrEmpty(parsedMeta.ChromaKey)) parsedMeta.ChromaKey = diskParsed.ChromaKey;
				if (string.IsNullOrEmpty(metaJson)) metaJson = diskMeta;
			}
		}

		var tags = BuildManifestAssetTags(manifest, virtualPath, normPath, storageDirectory, asset.FileName, parsedMeta.Tags);

		AssetType? assetType = ResolveFallbackAssetType(parsedMeta.AssetType, virtualPath, tags);

		asset.Tags = tags;
		asset.MetadataJson = JsonSerializer.Serialize(new AssetMetadataModel { Tags = tags });
		asset.HasPlayerColorMask = parsedMeta.SupportsTeamColor ?? AssetRecordFactory.DetermineHasPlayerColorMask(metaJson, normPath);
		asset.ChromaKey = parsedMeta.ChromaKey ?? string.Empty;
		asset.AssetType = assetType;
		asset.HasRealmMetadata = !string.IsNullOrEmpty(metaJson) || assetType.HasValue;

		assetsToInsert.Add(asset);
		existingAssetMap[normPath] = asset;

		AssetRecordFactory.EnqueueThumbnailRequestIfNeeded(asset.Extension, normPath, norm);
	}

	private static IndexedAsset CreateBaseManifestAsset(string virtualPath, string norm, string normPath, string mapName, string mapVersion, string storageDirectory, FileInfo fi)
	{
		return new IndexedAsset
		{
			FilePath = normPath,
			FileName = Path.GetFileName(virtualPath),
			Extension = !string.IsNullOrEmpty(Path.GetExtension(virtualPath)) ? Path.GetExtension(virtualPath).ToLowerInvariant() : Path.GetExtension(normPath).ToLowerInvariant(),
			DirectoryPath = storageDirectory,
			FileSizeBytes = fi.Length,
			LastModifiedUtc = fi.LastWriteTimeUtc,
			MapName = mapName,
			MapVersion = mapVersion,
			Blake3 = norm
		};
	}

	private static AssetType? ResolveFallbackAssetType(AssetType? parsedType, string virtualPath, List<string> tags)
	{
		AssetType? assetType = ResolveAssetTypeFromParsed(parsedType, virtualPath);
		if (assetType.HasValue) return assetType;

		foreach (var tag in tags)
		{
			var norm = RealmMetadataHelper.NormalizeAssetType(tag);
			if (norm.HasValue && RealmMetadataHelper.IsValidAssetTypeForExtension(virtualPath, norm.Value, out _))
			{
				return norm;
			}
		}

		return null;
	}

	private static List<string> BuildManifestAssetTags(MapManifest manifest, string virtualPath, string normPath, string storageDirectory, string fileName, List<string> parsedTags)
	{
		var tags = parsedTags;
		if (tags.Count == 0) tags = AssetRecordFactory.LoadTagsForFile(normPath, storageDirectory, tags);

		string preferredNameNoExt = Path.GetFileNameWithoutExtension(fileName);
		var nameTokens = preferredNameNoExt.Split(new[] { '_', '-', ' ', '.', '@' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (var token in nameTokens)
		{
			if (token.Length > 1 && !char.IsDigit(token[0]) && !tags.Contains(token, StringComparer.OrdinalIgnoreCase))
			{
				tags.Add(token.ToLowerInvariant());
			}
		}

		if (manifest.Tags != null)
		{
			foreach (var tag in manifest.Tags)
			{
				if (!string.IsNullOrWhiteSpace(tag) && !tags.Contains(tag.Trim(), StringComparer.OrdinalIgnoreCase))
				{
					tags.Add(tag.Trim().ToLowerInvariant());
				}
			}
		}

		return tags;
	}
}
