using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LiteDB;
using Realm.Client.Network;
using Realm.Client.UI.MapEditor;
using Realm.Client.Utils;
using Realm.EditorAPI;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

/// <summary>
///     The directories the user has asked to have indexed: registering one, scanning its files into the
///     index, dropping the rows of files that are gone, unregistering it again, and rescanning one or all
///     of them. The set of directories being scanned right now lives here, so a scan started from the
///     editor and a rescan started from the asset browser cannot both claim the same directory.
/// </summary>
public sealed class AssetIndexDirectoryIndexer
{
	private readonly Func<AssetIndexTarget> _target;
	private readonly object _syncLock;
	private readonly HashSet<string> _indexingDirectories = new(StringComparer.OrdinalIgnoreCase);
	private readonly Action<string, bool> _indexingStateChanged;
	private readonly Action<string> _scanCompleted;
	private readonly Action _scanAllCasManifests;

	public AssetIndexDirectoryIndexer(
		Func<AssetIndexTarget> target,
		object syncLock,
		Action<string, bool> indexingStateChanged,
		Action<string> scanCompleted,
		Action scanAllCasManifests)
	{
		_target = target;
		_syncLock = syncLock;
		_indexingStateChanged = indexingStateChanged;
		_scanCompleted = scanCompleted;
		_scanAllCasManifests = scanAllCasManifests;
	}

	public bool IsDirectoryIndexing(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath)) return false;
		lock (_syncLock)
		{
			return _indexingDirectories.Contains(AssetIndexPaths.Normalize(directoryPath));
		}
	}

	public IReadOnlyList<string> GetIndexedDirectories()
	{
		var target = _target();
		lock (_syncLock)
		{
			return target.Folders.FindAll()
				.Select(f => f.DirectoryPath)
				.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}
	}

	public void AddDirectory(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath)) return;

		string normalizedPath = AssetIndexPaths.Normalize(directoryPath);
		if (!IsValidDirectoryForIndexing(normalizedPath)) return;

		if (!TryRegisterDirectory(normalizedPath)) return;

		_indexingStateChanged(normalizedPath, true);
		StartDirectoryScanTask(normalizedPath);
	}

	private bool IsValidDirectoryForIndexing(string normalizedPath)
	{
		if (AssetIndexPaths.IsForbidden(normalizedPath) || !Directory.Exists(normalizedPath)) return false;

		if (normalizedPath.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase) ||
		    normalizedPath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
		    normalizedPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
		    normalizedPath.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ||
		    normalizedPath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ||
		    normalizedPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		return true;
	}

	private bool TryRegisterDirectory(string normalizedPath)
	{
		var target = _target();
		lock (_syncLock)
		{
			var existingFolder = target.Folders.FindOne(x => x.DirectoryPath == normalizedPath);
			if (existingFolder == null)
			{
				existingFolder = new IndexedFolder
				{
					DirectoryPath = normalizedPath,
					LastScannedUtc = DateTime.MinValue
				};
				target.Folders.Insert(existingFolder);
				target.Database.Checkpoint();
			}

			if (_indexingDirectories.Contains(normalizedPath)) return false;
			_indexingDirectories.Add(normalizedPath);
			return true;
		}
	}

	private void StartDirectoryScanTask(string normalizedPath)
	{
		Task.Run(() =>
		{
			try
			{
				ScanDirectoryInternal(normalizedPath);
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[AssetIndexService] Scan error on {normalizedPath}: {ex.Message}");
			}
			finally
			{
				lock (_syncLock)
				{
					_indexingDirectories.Remove(normalizedPath);
				}

				_indexingStateChanged(normalizedPath, false);
				_scanCompleted(normalizedPath);
			}
		});
	}

	public void RemoveDirectory(string directoryPath)
	{
		var target = _target();
		if (string.IsNullOrWhiteSpace(directoryPath))
		{
			return;
		}

		string normalizedPath = AssetIndexPaths.Normalize(directoryPath);
		if (string.Equals(normalizedPath, target.StorageDirectory, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		lock (_syncLock)
		{
			_indexingDirectories.Remove(normalizedPath);

			target.Folders.DeleteMany(f => f.DirectoryPath == normalizedPath);
			var leftoverFolders = target.Folders.FindAll()
				.Where(f => string.Equals(f.DirectoryPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
				.Select(f => (BsonValue)f.Id)
				.ToArray();
			if (leftoverFolders.Length > 0)
			{
				target.Folders.DeleteMany(Query.In("_id", leftoverFolders));
			}

			target.Assets.DeleteMany(Query.EQ("DirectoryPath", normalizedPath));
			target.Assets.DeleteMany(Query.StartsWith("FilePath", normalizedPath + "/"));
			target.Assets.DeleteMany(Query.StartsWith("FilePath", normalizedPath + "\\"));

			target.Database.Checkpoint();
		}

		_indexingStateChanged(normalizedPath, false);
	}

	public void RescanDirectory(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath))
		{
			return;
		}

		string normalizedPath = AssetIndexPaths.Normalize(directoryPath);
		if (AssetIndexPaths.IsForbidden(normalizedPath) || !Directory.Exists(normalizedPath))
		{
			return;
		}

		lock (_syncLock)
		{
			if (_indexingDirectories.Contains(normalizedPath))
			{
				return;
			}

			_indexingDirectories.Add(normalizedPath);
		}

		_indexingStateChanged(normalizedPath, true);

		Task.Run(() =>
		{
			try
			{
				ScanDirectoryInternal(normalizedPath);
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[AssetIndexService] Rescan error on {normalizedPath}: {ex.Message}");
			}
			finally
			{
				lock (_syncLock)
				{
					_indexingDirectories.Remove(normalizedPath);
				}

				_indexingStateChanged(normalizedPath, false);
				_scanCompleted(normalizedPath);
			}
		});
	}

	public void RescanAllDirectories()
	{
		List<string> dirsToScan = PrepareDirectoriesForRescan();

		foreach (var dir in dirsToScan)
		{
			_indexingStateChanged(dir, true);
		}

		Task.Run(() => ExecuteRescanTask(dirsToScan));
	}

	private List<string> PrepareDirectoriesForRescan()
	{
		var target = _target();
		List<string> dirsToScan;
		lock (_syncLock)
		{
			CleanOrphanedAssetsFromIndex();

			dirsToScan = target.Folders.FindAll()
				.Select(f => f.DirectoryPath)
				.Where(p => Directory.Exists(p) && !_indexingDirectories.Contains(p) && !string.Equals(p, target.StorageDirectory, StringComparison.OrdinalIgnoreCase))
				.ToList();

			foreach (var dir in dirsToScan)
			{
				_indexingDirectories.Add(dir);
			}

			target.Database.Checkpoint();
		}

		return dirsToScan;
	}

	private void CleanOrphanedAssetsFromIndex()
	{
		var target = _target();
		var validFolders = target.Folders.FindAll()
			.Select(f => f.DirectoryPath)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		string globalArchive = AssetIndexPaths.Normalize(MapAssetManager.GlobalArchiveDirectory);
		string p2pArchive = AssetIndexPaths.Normalize(MapAssetManager.P2PArchiveDirectory);

		var orphanedAssets = target.Assets.FindAll()
			.Where(a => (!validFolders.Contains(a.DirectoryPath) &&
			             !string.Equals(a.DirectoryPath, target.StorageDirectory, StringComparison.OrdinalIgnoreCase) &&
			             !a.FilePath.StartsWith(globalArchive, StringComparison.OrdinalIgnoreCase) &&
			             !a.FilePath.StartsWith(p2pArchive, StringComparison.OrdinalIgnoreCase) &&
			             string.IsNullOrEmpty(a.MapName)) ||
			            AssetIndexPaths.IsForbidden(a.DirectoryPath))
			.Select(a => (BsonValue)a.Id)
			.ToArray();

		if (orphanedAssets.Length > 0)
		{
			target.Assets.DeleteMany(Query.In("_id", orphanedAssets));
		}
	}

	private void ExecuteRescanTask(List<string> dirsToScan)
	{
		var target = _target();
		try
		{
			_scanAllCasManifests();
			_scanCompleted(target.StorageDirectory);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[AssetIndexService] RescanAll CAS manifest scan error: {ex.Message}");
		}

		foreach (var dir in dirsToScan)
		{
			RescanSingleDirectory(dir);
		}
	}

	private void RescanSingleDirectory(string dir)
	{
		try
		{
			ScanDirectoryInternal(dir);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[AssetIndexService] RescanAll error on {dir}: {ex.Message}");
		}
		finally
		{
			lock (_syncLock)
			{
				_indexingDirectories.Remove(dir);
			}

			_indexingStateChanged(dir, false);
			_scanCompleted(dir);
		}
	}

	private void ScanDirectoryInternal(string normalizedDirectoryPath)
	{
		var target = _target();
		if (!Directory.Exists(normalizedDirectoryPath)) return;

		if (string.Equals(normalizedDirectoryPath, target.StorageDirectory, StringComparison.OrdinalIgnoreCase))
		{
			_scanAllCasManifests();
			_scanCompleted(target.StorageDirectory);
			return;
		}

		var discoveredFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var files = Directory.EnumerateFiles(normalizedDirectoryPath, "*.*", SearchOption.AllDirectories);
		var existingAssets = GetExistingAssetsInDirectory(normalizedDirectoryPath);
		var batchToUpsert = new List<IndexedAsset>();

		foreach (string filePath in files)
		{
			ProcessScannedFile(filePath, normalizedDirectoryPath, discoveredFiles, existingAssets, batchToUpsert);
		}

		if (batchToUpsert.Count > 0)
		{
			lock (_syncLock)
			{
				target.Assets.Upsert(batchToUpsert);
			}

			batchToUpsert.Clear();
		}

		CleanupStaleAssetsAndRecordScan(normalizedDirectoryPath, existingAssets, discoveredFiles);
	}

	private Dictionary<string, IndexedAsset> GetExistingAssetsInDirectory(string normalizedDirectoryPath)
	{
		var target = _target();
		lock (_syncLock)
		{
			return target.Assets.Find(Query.EQ("DirectoryPath", normalizedDirectoryPath))
				.ToDictionary(x => x.FilePath, StringComparer.OrdinalIgnoreCase);
		}
	}

	private void ProcessScannedFile(string filePath, string normalizedDirectoryPath, HashSet<string> discoveredFiles, Dictionary<string, IndexedAsset> existingAssets, List<IndexedAsset> batchToUpsert)
	{
		var target = _target();
		string normalizedFilePath = AssetIndexPaths.Normalize(filePath);
		string extension = Path.GetExtension(normalizedFilePath).ToLowerInvariant();

		if (AssetRecordFactory.IsIgnoredFileExtension(extension) || AssetRecordFactory.IsMetadataSidecarFile(normalizedFilePath)) return;

		discoveredFiles.Add(normalizedFilePath);
		try
		{
			var fileInfo = new FileInfo(normalizedFilePath);
			AssetType? expectedDirType = AssetRecordFactory.GetExpectedAssetTypeFromDirectory(normalizedFilePath);

			existingAssets.TryGetValue(normalizedFilePath, out var existingAsset);
			if (AssetRecordFactory.IsExistingAssetUpToDate(existingAsset, fileInfo, expectedDirType, normalizedFilePath)) return;

			var newAsset = AssetRecordFactory.GenerateIndexedAssetFromFile(normalizedFilePath, normalizedDirectoryPath, fileInfo, extension, expectedDirType, existingAsset);
			batchToUpsert.Add(newAsset);

			if (newAsset.Extension == ".rmesh" && !GlbThumbnailRenderer.HasDiskCache(normalizedFilePath, newAsset.Blake3))
			{
				GlbThumbnailRenderer.EnqueueRequest(normalizedFilePath, fileInfo.LastWriteTimeUtc, newAsset.Blake3, isHighPriority: false);
			}

			if (batchToUpsert.Count >= 250)
			{
				lock (_syncLock)
				{
					target.Assets.Upsert(batchToUpsert);
				}

				batchToUpsert.Clear();
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[AssetIndexService] Scan error on {normalizedFilePath}: {ex.Message}");
		}
	}

	private void CleanupStaleAssetsAndRecordScan(string normalizedDirectoryPath, Dictionary<string, IndexedAsset> existingAssets, HashSet<string> discoveredFiles)
	{
		var target = _target();
		lock (_syncLock)
		{
			var idsToDelete = existingAssets.Values
				.Where(x => !discoveredFiles.Contains(x.FilePath))
				.Select(x => (BsonValue)x.Id)
				.ToArray();

			if (idsToDelete.Length > 0)
			{
				target.Assets.DeleteMany(Query.In("_id", idsToDelete));
			}

			var folderRecord = target.Folders.FindOne(x => x.DirectoryPath == normalizedDirectoryPath);
			if (folderRecord != null)
			{
				folderRecord.LastScannedUtc = DateTime.UtcNow;
				target.Folders.Update(folderRecord);
			}

			target.Database.Checkpoint();
		}
	}
}
