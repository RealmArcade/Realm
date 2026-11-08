using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;
using Realm.Client.Network;

namespace Realm.Client.Services;

/// <summary>
///     Keeping the index free of rows that point nowhere: template folders, archive files that were turned
///     into folders by accident, assets whose directory is no longer indexed and whose map is unknown, and
///     package rows whose manifest file is gone. It also creates the content-addressed store's directory on
///     first run, which is the moment the index is initialised from the archives.
/// </summary>
internal static class AssetIndexOrphanCleanup
{
	internal static void Initialize(AssetIndexTarget target, object syncLock)
	{
		lock (syncLock)
		{
			string legacyArchive = AssetIndexPaths.Normalize(Path.Combine(MapAssetManager.GlobalArchiveDirectory, "global_assets.rmap"));
			string casAssetsDirectory = target.StorageDirectory;
			string globalArchive = AssetIndexPaths.Normalize(MapAssetManager.GlobalArchiveDirectory);
			string p2pArchive = AssetIndexPaths.Normalize(MapAssetManager.P2PArchiveDirectory);

			RemoveForbiddenFolders(target, legacyArchive, casAssetsDirectory);

			if (!Directory.Exists(casAssetsDirectory))
			{
				Directory.CreateDirectory(casAssetsDirectory);
			}

			RemoveOrphanedAssets(target, casAssetsDirectory, globalArchive, p2pArchive);
			RemoveOrphanedPackages(target);

			target.Database.Checkpoint();
		}
	}

	private static void RemoveForbiddenFolders(AssetIndexTarget target, string legacyArchive, string casAssetsDirectory)
	{
		var forbiddenFolders = target.Folders.FindAll()
			.Where(f => AssetIndexPaths.IsForbidden(f.DirectoryPath) ||
			            string.Equals(f.DirectoryPath, legacyArchive, StringComparison.OrdinalIgnoreCase) ||
			            string.Equals(f.DirectoryPath, casAssetsDirectory, StringComparison.OrdinalIgnoreCase) ||
			            f.DirectoryPath.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase) ||
			            f.DirectoryPath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
			.Select(f => (BsonValue)f.Id)
			.ToArray();

		if (forbiddenFolders.Length > 0)
		{
			target.Folders.DeleteMany(Query.In("_id", forbiddenFolders));
		}
	}

	private static void RemoveOrphanedAssets(AssetIndexTarget target, string casAssetsDirectory, string globalArchive, string p2pArchive)
	{
		var validFolders = target.Folders.FindAll()
			.Select(f => f.DirectoryPath)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		var orphanedAssets = target.Assets.FindAll()
			.Where(a => IsAssetOrphaned(a, validFolders, casAssetsDirectory, globalArchive, p2pArchive))
			.Select(a => (BsonValue)a.Id)
			.ToArray();

		if (orphanedAssets.Length > 0)
		{
			target.Assets.DeleteMany(Query.In("_id", orphanedAssets));
		}
	}

	private static bool IsAssetOrphaned(IndexedAsset asset, HashSet<string> validFolders, string casAssetsDirectory, string globalArchive, string p2pArchive)
	{
		if (IsForbiddenAsset(asset)) return true;
		if (IsAssetInValidLocation(asset, validFolders, casAssetsDirectory, globalArchive, p2pArchive)) return false;

		return true;
	}

	private static bool IsForbiddenAsset(IndexedAsset asset)
	{
		if (AssetIndexPaths.IsForbidden(asset.DirectoryPath)) return true;
		if (AssetIndexPaths.IsForbidden(asset.FilePath)) return true;
		if (asset.DirectoryPath.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase)) return true;
		if (asset.DirectoryPath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)) return true;
		if (asset.FilePath.Contains("/extracted/", StringComparison.OrdinalIgnoreCase)) return true;
		if (asset.FilePath.Contains("\\extracted\\", StringComparison.OrdinalIgnoreCase)) return true;

		return false;
	}

	private static bool IsAssetInValidLocation(IndexedAsset asset, HashSet<string> validFolders, string casAssetsDirectory, string globalArchive, string p2pArchive)
	{
		if (validFolders.Contains(asset.DirectoryPath)) return true;
		if (string.Equals(asset.DirectoryPath, casAssetsDirectory, StringComparison.OrdinalIgnoreCase)) return true;
		if (asset.FilePath.StartsWith(globalArchive, StringComparison.OrdinalIgnoreCase)) return true;
		if (asset.FilePath.StartsWith(p2pArchive, StringComparison.OrdinalIgnoreCase)) return true;
		if (!string.IsNullOrEmpty(asset.MapName)) return true;

		return false;
	}

	private static void RemoveOrphanedPackages(AssetIndexTarget target)
	{
		var orphanedPackages = target.Packages.FindAll()
			.Where(p => string.IsNullOrWhiteSpace(p.ManifestPath) || !File.Exists(p.ManifestPath))
			.Select(p => (BsonValue)p.Id)
			.ToArray();

		if (orphanedPackages.Length > 0)
		{
			target.Packages.DeleteMany(Query.In("_id", orphanedPackages));
		}
	}
}
