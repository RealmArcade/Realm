using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using LiteDB;
using Realm.Client.Network;
using Realm.Client.UI.MapEditor;
using Realm.Client.Utils;
using Realm.EditorAPI;
using Realm.Shared;
using Realm.Shared.Distribution;
using Environment = System.Environment;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Realm.Client.Services;

public class AssetIndexService : IDisposable
{
	public static readonly HashSet<string> ValidAssetPrefixes = new(StringComparer.OrdinalIgnoreCase)
	{
		"unit", "building", "prop", "resource", "item", "ability",
		"weapon", "upgrade", "terrain", "spritesheet", "decal", "SpawnShader"
	};

	private const int IndexSchemaVersion = 1;

	public event Action<string, bool>? DirectoryIndexingStateChanged;
	public event Action<string>? DirectoryScanCompleted;

	private LiteDatabase _database = null!;
	private ILiteCollection<IndexedAsset> _assetCollection = null!;
	private ILiteCollection<IndexedFolder> _folderCollection = null!;
	private ILiteCollection<IndexedMapPackage> _mapPackageCollection = null!;
	private readonly object _syncLock = new();
	private HashSet<string> _lastVerificationFailurePaths = new(StringComparer.OrdinalIgnoreCase);

	private LiteDatabase? _p2pDatabase;
	private readonly object _p2pSyncLock = new();

	private static string GetUserDirectory()
	{
		return PathUtils.SafeGlobalizeUserPath();
	}

	public static string GetCacheFilePath()
	{
		string userDirectory = GetUserDirectory();
		if (!Directory.Exists(userDirectory))
		{
			Directory.CreateDirectory(userDirectory);
		}

		return Path.Combine(userDirectory, "asset_index.cache");
	}

	private static void DeleteCacheFiles(string cacheFilePath)
	{
		try
		{
			if (File.Exists(cacheFilePath))
			{
				File.Delete(cacheFilePath);
			}

			string logFile = cacheFilePath + "-log";
			if (File.Exists(logFile))
			{
				File.Delete(logFile);
			}

			string tempFile = cacheFilePath + "-temp";
			if (File.Exists(tempFile))
			{
				File.Delete(tempFile);
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[AssetIndexService] Failed to delete cache files: {ex.Message}");
		}
	}

	private void EnsureIndexes()
	{
		_assetCollection.EnsureIndex(x => x.FilePath, true);
		_assetCollection.EnsureIndex(x => x.DirectoryPath);
		_assetCollection.EnsureIndex(x => x.Extension);
		_assetCollection.EnsureIndex(x => x.Tags);
		_assetCollection.EnsureIndex(x => x.FileName);
		_assetCollection.EnsureIndex(x => x.MapName);
		_assetCollection.EnsureIndex(x => x.MapVersion);
		_assetCollection.EnsureIndex(x => x.Blake3);
		_assetCollection.EnsureIndex(x => x.HasRealmMetadata);
		_assetCollection.EnsureIndex(x => x.AssetType);
		_assetCollection.EnsureIndex(x => x.HasPlayerColorMask);

		_folderCollection.EnsureIndex(x => x.DirectoryPath, true);

		_mapPackageCollection.EnsureIndex(x => x.MapName);
		_mapPackageCollection.EnsureIndex(x => x.MapVersion);
	}

	public string GetIndexGameBuildNumber()
	{
		lock (_syncLock)
		{
			try
			{
				var metaCol = _database.GetCollection<BsonDocument>("_metadata");
				var doc = metaCol.FindById(1);
				if (doc != null && doc.TryGetValue("GameBuildNumber", out var val) && val.IsString)
				{
					return val.AsString;
				}
			}
			catch (Exception ex)
			{
				GameLog.LogError($"could not read the game build number from the asset index metadata, so it is treated as unknown: {ex.GetType().Name}: {ex.Message}");
			}

			return string.Empty;
		}
	}

	public int GetIndexSchemaVersion()
	{
		lock (_syncLock)
		{
			try
			{
				var metaCol = _database.GetCollection<BsonDocument>("_metadata");
				var doc = metaCol.FindById(1);
				if (doc != null && doc.TryGetValue("IndexSchemaVersion", out var val) && val.IsInt32)
				{
					return val.AsInt32;
				}
			}
			catch (Exception ex)
			{
				GameLog.LogError($"could not read the asset index schema version, so the index is treated as the oldest schema: {ex.GetType().Name}: {ex.Message}");
			}

			return 0;
		}
	}

	public void SetIndexGameBuildNumber(string gameBuildNumber)
	{
		lock (_syncLock)
		{
			try
			{
				var metaCol = _database.GetCollection<BsonDocument>("_metadata");
				var doc = new BsonDocument
				{
					["_id"] = 1,
					["GameBuildNumber"] = gameBuildNumber,
					["IndexSchemaVersion"] = IndexSchemaVersion,
					["RequiresContentRebuild"] = false
				};
				metaCol.Upsert(doc);
				_database.Checkpoint();
			}
			catch
			{
			}
		}
	}

	private void MarkIndexContentRebuildRequired()
	{
		lock (_syncLock)
		{
			try
			{
				var metaCol = _database.GetCollection<BsonDocument>("_metadata");
				var doc = new BsonDocument
				{
					["_id"] = 1,
					["GameBuildNumber"] = RealmVersion.GameBuildNumber,
					["IndexSchemaVersion"] = IndexSchemaVersion,
					["RequiresContentRebuild"] = true
				};
				metaCol.Upsert(doc);
				_database.Checkpoint();
			}
			catch
			{
			}
		}
	}

	private bool IsContentRebuildRequired()
	{
		lock (_syncLock)
		{
			try
			{
				var metaCol = _database.GetCollection<BsonDocument>("_metadata");
				var doc = metaCol.FindById(1);
				if (doc != null && doc.TryGetValue("RequiresContentRebuild", out var val) && val.IsBoolean)
				{
					return val.AsBoolean;
				}
			}
			catch
			{
			}

			return false;
		}
	}

	public bool IsIndexVersionMismatch()
	{
		string currentIndexVersion = GetIndexGameBuildNumber();
		if (!string.Equals(currentIndexVersion, RealmVersion.GameBuildNumber, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		if (GetIndexSchemaVersion() != IndexSchemaVersion)
		{
			return true;
		}

		return IsContentRebuildRequired();
	}

	public const int DefaultVerificationSampleSize = AssetIndexVerification.DefaultSampleSize;

	public AssetIndexVerificationResult VerifyIndex(int sampleSize = DefaultVerificationSampleSize)
	{
		return AssetIndexVerification.VerifySample(_assetCollection, _syncLock, ref _lastVerificationFailurePaths, sampleSize);
	}

	public AssetIndexVerificationResult VerifyAllIndexedAssets(bool verifyContentHash, IProgress<AssetIndexProgressUpdate>? progress = null, double progressStart = 0.0, double progressEnd = 1.0)
	{
		return AssetIndexVerification.VerifyAll(_assetCollection, _syncLock, ref _lastVerificationFailurePaths, verifyContentHash, progress, progressStart, progressEnd);
	}

	public int RepairIndexedAssets(IReadOnlyList<AssetIndexVerificationFailure> failures, IProgress<AssetIndexProgressUpdate>? progress = null, double progressStart = 0.0, double progressEnd = 1.0)
	{
		return AssetIndexVerification.RepairAssets(_assetCollection, _database, _syncLock, failures, progress, progressStart, progressEnd);
	}

	public void SynchronizeCasAndMapPackages(IProgress<AssetIndexProgressUpdate>? progress = null)
	{
		AssetIndexCasSynchronization.Synchronize(progress);
	}

	public void RebuildIndexFromCas(IProgress<AssetIndexProgressUpdate>? progress = null)
	{
		lock (_syncLock)
		{
			progress?.Report(new AssetIndexProgressUpdate(0.02, TranslationServer.Translate("Synchronizing CAS and map packages...")));
			SynchronizeCasAndMapPackages(progress);

			progress?.Report(new AssetIndexProgressUpdate(0.12, TranslationServer.Translate("Resetting asset database cache...")));

			try
			{
				_database.Dispose();
			}
			catch
			{
			}

			ClearP2PIndex();

			string cacheFilePath = GetCacheFilePath();
			DeleteCacheFiles(cacheFilePath);

			progress?.Report(new AssetIndexProgressUpdate(0.18, TranslationServer.Translate("Initializing database schema and indexes...")));

			var connectionString = new ConnectionString
			{
				Filename = cacheFilePath,
				Connection = ConnectionType.Shared
			};

			_database = new LiteDatabase(connectionString);
			_assetCollection = _database.GetCollection<IndexedAsset>("assets");
			_folderCollection = _database.GetCollection<IndexedFolder>("folders");
			_mapPackageCollection = _database.GetCollection<IndexedMapPackage>("map_packages");

			EnsureIndexes();
			SetIndexGameBuildNumber(RealmVersion.GameBuildNumber);

			InitializeDefaultDirectories();
			AssetIndexCasSynchronization.InvalidatePathCaches();
			ScanAllCasManifests(progress);

			progress?.Report(new AssetIndexProgressUpdate(0.95, TranslationServer.Translate("Verifying every indexed file...")));
			var verification = VerifyAllIndexedAssets(verifyContentHash: true, progress, 0.95, 0.98);
			progress?.Report(new AssetIndexProgressUpdate(0.98, string.Format(TranslationServer.Translate("Verified {0} indexed file(s): {1} problem(s) found."), verification.CheckedCount, verification.Failures.Count)));

			if (verification.Failures.Count > 0)
			{
				int repairedCount = RepairIndexedAssets(verification.Failures, progress, 0.98, 0.99);
				int remainingCount = AssetIndexVerification.CountRemaining(verification.Failures, GetAssetByPath);
				progress?.Report(new AssetIndexProgressUpdate(0.99, string.Format(TranslationServer.Translate("Repaired {0} indexed file(s); {1} problem(s) remain."), repairedCount, remainingCount)));
			}

			progress?.Report(new AssetIndexProgressUpdate(1.0, TranslationServer.Translate("Asset index repair complete!")));
		}
	}

	private readonly AssetIndexDirectoryIndexer _directoryIndexer;

	public AssetIndexService()
	{
		_directoryIndexer = new AssetIndexDirectoryIndexer(
			() => new AssetIndexTarget(_database, _assetCollection, _folderCollection, _mapPackageCollection, MapAssetManager.Storage, GlobalCasAssetsDirectory),
			_syncLock,
			(path, indexing) => DirectoryIndexingStateChanged?.Invoke(path, indexing),
			path => DirectoryScanCompleted?.Invoke(path),
			() => ScanAllCasManifests());

		if (TryOpenIndexDatabase())
		{
			return;
		}

		RebuildIndexDatabase();
	}

	private bool TryOpenIndexDatabase()
	{
		try
		{
			OpenIndexDatabase(GetCacheFilePath(), false);
			return true;
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[AssetIndexService] Failed to open asset index cache, rebuilding: {ex.Message}");
			DisposeIndexDatabase();
			return false;
		}
	}

	private void RebuildIndexDatabase()
	{
		string cacheFilePath = GetCacheFilePath();

		DisposeIndexDatabase();
		ClearP2PIndex();
		DeleteCacheFiles(cacheFilePath);

		try
		{
			OpenIndexDatabase(cacheFilePath, true);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[AssetIndexService] Failed to rebuild asset index cache, using in-memory index: {ex.Message}");
			DisposeIndexDatabase();
			OpenInMemoryIndexDatabase();
		}
	}

	private void OpenIndexDatabase(string cacheFilePath, bool forceRebuild)
	{
		var connectionString = new ConnectionString
		{
			Filename = cacheFilePath,
			Connection = ConnectionType.Shared
		};

		_database = new LiteDatabase(connectionString);
		_assetCollection = _database.GetCollection<IndexedAsset>("assets");
		_folderCollection = _database.GetCollection<IndexedFolder>("folders");
		_mapPackageCollection = _database.GetCollection<IndexedMapPackage>("map_packages");

		EnsureIndexes();

		if (forceRebuild)
		{
			MarkIndexContentRebuildRequired();
			InitializeDefaultDirectories();
			return;
		}

		if (GetIndexSchemaVersion() != IndexSchemaVersion)
		{
			throw new InvalidOperationException("Asset index cache schema is outdated.");
		}

		if (!IsIndexVersionMismatch())
		{
			InitializeDefaultDirectories();
		}
	}

	private void OpenInMemoryIndexDatabase()
	{
		try
		{
			_database = new LiteDatabase(new MemoryStream());
			_assetCollection = _database.GetCollection<IndexedAsset>("assets");
			_folderCollection = _database.GetCollection<IndexedFolder>("folders");
			_mapPackageCollection = _database.GetCollection<IndexedMapPackage>("map_packages");

			EnsureIndexes();
			InitializeDefaultDirectories();
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[AssetIndexService] In-memory asset index database failed: {ex.Message}");
		}
	}

	private void DisposeIndexDatabase()
	{
		try
		{
			_database?.Dispose();
		}
		catch
		{
		}

		_database = null!;
		_assetCollection = null!;
		_folderCollection = null!;
		_mapPackageCollection = null!;
	}

	private LiteDatabase GetP2PDatabase()
	{
		lock (_p2pSyncLock)
		{
			if (_p2pDatabase != null) return _p2pDatabase;
			string p2pDir = MapAssetManager.P2PArchiveDirectory;
			if (!Directory.Exists(p2pDir)) Directory.CreateDirectory(p2pDir);
			string cacheFilePath = Path.Combine(p2pDir, "p2p_asset_index.cache");

			try
			{
				_p2pDatabase = OpenP2PDatabase(cacheFilePath);
				return _p2pDatabase;
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[AssetIndexService] Failed to open P2P asset index cache, rebuilding: {ex.Message}");
				try
				{
					_p2pDatabase?.Dispose();
				}
				catch
				{
				}

				_p2pDatabase = null;
				DeleteCacheFiles(cacheFilePath);
				_p2pDatabase = OpenP2PDatabase(cacheFilePath);
				return _p2pDatabase;
			}
		}
	}

	private static LiteDatabase OpenP2PDatabase(string cacheFilePath)
	{
		LiteDatabase? database = null;
		try
		{
			var connectionString = new ConnectionString
			{
				Filename = cacheFilePath,
				Connection = ConnectionType.Shared
			};
			database = new LiteDatabase(connectionString);
			var p2pAssetCol = database.GetCollection<IndexedAsset>("assets");
			var p2pMapCol = database.GetCollection<IndexedMapPackage>("map_packages");
			p2pAssetCol.EnsureIndex(x => x.FilePath, true);
			p2pAssetCol.EnsureIndex(x => x.MapName);
			p2pAssetCol.EnsureIndex(x => x.MapVersion);
			p2pAssetCol.EnsureIndex(x => x.HasPlayerColorMask);
			p2pMapCol.EnsureIndex(x => x.MapName);
			p2pMapCol.EnsureIndex(x => x.MapVersion);
			p2pAssetCol.Find(Query.All(), 0, 1).ToList();
			return database;
		}
		catch
		{
			database?.Dispose();
			throw;
		}
	}

	public void ClearP2PIndex()
	{
		lock (_p2pSyncLock)
		{
			if (_p2pDatabase != null)
			{
				_p2pDatabase.Dispose();
				_p2pDatabase = null;
			}
		}
	}

	public static string GlobalCasAssetsDirectory => AssetIndexPaths.Normalize(MapAssetManager.Storage.AssetsDirectory);

	private void InitializeDefaultDirectories()
	{
		AssetIndexOrphanCleanup.Initialize(ResolveIndexTarget(isP2P: false), _syncLock);
	}

	public void ScanAllCasManifests(IProgress<AssetIndexProgressUpdate>? progress = null)
	{
		AssetIndexArchiveScans.ScanAll(() => ResolveIndexTarget(isP2P: false), RegisterManifest, _syncLock, progress);
	}

	private AssetIndexTarget ResolveIndexTarget(bool isP2P)
	{
		if (!isP2P)
		{
			return new AssetIndexTarget(_database, _assetCollection, _folderCollection, _mapPackageCollection, MapAssetManager.Storage, GlobalCasAssetsDirectory);
		}

		var database = GetP2PDatabase();
		return new AssetIndexTarget(
			database,
			database.GetCollection<IndexedAsset>("assets"),
			database.GetCollection<IndexedFolder>("folders"),
			database.GetCollection<IndexedMapPackage>("map_packages"),
			MapAssetManager.P2PStorage,
			MapAssetManager.P2PArchiveDirectory);
	}

	public void RegisterManifest(MapManifest manifest, string manifestPath, bool isP2P = false)
	{
		if (manifest == null) return;
		lock (isP2P ? _p2pSyncLock : _syncLock)
		{
			AssetIndexManifestRegistration.Register(ResolveIndexTarget(isP2P), manifest, manifestPath, isP2P);
		}
	}

	public IReadOnlyList<IndexedMapPackage> GetDownloadedMapPackages()
	{
		lock (_syncLock)
		{
			return _mapPackageCollection.FindAll()
				.Where(p => !p.IsP2P && !string.IsNullOrWhiteSpace(p.ManifestPath) && File.Exists(p.ManifestPath))
				.OrderBy(p => p.MapName, StringComparer.OrdinalIgnoreCase)
				.ThenByDescending(p => p.MapVersion, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}
	}

	public string? GetLatestVersionForMap(string mapName)
	{
		if (string.IsNullOrWhiteSpace(mapName)) return null;
		lock (_syncLock)
		{
			var packages = _mapPackageCollection.Find(p => p.MapName == mapName && !p.IsP2P).ToList();
			if (packages.Count == 0) return null;
			packages.Sort((a, b) => CompareVersions(b.MapVersion, a.MapVersion));
			return packages[0].MapVersion;
		}
	}

	public static int CompareVersions(string v1, string v2)
	{
		if (Version.TryParse(v1, out var ver1) && Version.TryParse(v2, out var ver2))
		{
			return ver1.CompareTo(ver2);
		}

		return string.Compare(v1, v2, StringComparison.OrdinalIgnoreCase);
	}

	public bool IsDirectoryIndexing(string directoryPath)
	{
		return _directoryIndexer.IsDirectoryIndexing(directoryPath);
	}

	public IReadOnlyList<string> GetIndexedDirectories()
	{
		return _directoryIndexer.GetIndexedDirectories();
	}

	public void AddDirectory(string directoryPath)
	{
		_directoryIndexer.AddDirectory(directoryPath);
	}

	public void RemoveDirectory(string directoryPath)
	{
		_directoryIndexer.RemoveDirectory(directoryPath);
	}

	public void RescanDirectory(string directoryPath)
	{
		_directoryIndexer.RescanDirectory(directoryPath);
	}

	public void RescanAllDirectories()
	{
		_directoryIndexer.RescanAllDirectories();
	}

	public int GetAssetCount()
	{
		lock (_syncLock)
		{
			return _assetCollection.Count();
		}
	}

	public void UpdateAssetTags(string filePath, List<string> newTags)
	{
		AssetIndexAssetWrites.UpdateTags(_assetCollection, _database, _syncLock, GlobalCasAssetsDirectory, filePath, newTags);
	}

	public void UpdateAssetType(string filePath, AssetType newAssetType)
	{
		AssetIndexAssetWrites.UpdateType(_assetCollection, _database, _syncLock, GlobalCasAssetsDirectory, filePath, newAssetType);
	}

	public void UpdateAssetType(string filePath, string newAssetType)
	{
		AssetIndexAssetWrites.UpdateType(_assetCollection, _database, _syncLock, GlobalCasAssetsDirectory, filePath, newAssetType);
	}

	public static string GetCanonicalBlake3(IndexedAsset asset)
	{
		return AssetIndexSearch.GetCanonicalBlake3(asset);
	}

	public IndexedAsset? GetAssetByPath(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath))
		{
			return null;
		}

		string normalizedFilePath = NormalizePath(filePath);
		lock (_syncLock)
		{
			return _assetCollection.FindOne(x => x.FilePath == normalizedFilePath);
		}
	}

	public IndexedAsset? GetAssetByBlake3(string blake3Hash)
	{
		if (string.IsNullOrWhiteSpace(blake3Hash))
		{
			return null;
		}

		string norm = ContentAddressableStorage.NormalizeBlake3Hash(blake3Hash);
		lock (_syncLock)
		{
			return _assetCollection.FindOne(x => x.Blake3 == norm);
		}
	}

	public static bool IsHexHash(string? str)
	{
		if (string.IsNullOrWhiteSpace(str) || str.Length != 64) return false;
		for (int i = 0; i < str.Length; i++)
		{
			char c = str[i];
			if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
			{
				return false;
			}
		}

		return true;
	}

	public string ResolvePrettyFileName(string filePath, string? fallbackFileName = null)
	{
		string rawFileName = !string.IsNullOrEmpty(fallbackFileName) ? fallbackFileName : Path.GetFileName(filePath);
		string rawNoExt = Path.GetFileNameWithoutExtension(rawFileName);

		if (!IsHexHash(rawNoExt))
		{
			return rawFileName;
		}

		var asset = GetAssetByPath(filePath) ?? GetAssetByBlake3(rawNoExt);
		if (asset != null && !string.IsNullOrWhiteSpace(asset.FileName) && !IsHexHash(Path.GetFileNameWithoutExtension(asset.FileName)))
		{
			return asset.FileName;
		}

		return rawFileName;
	}

	public List<IndexedAsset> SearchAssets(
		string? searchTerm,
		IReadOnlyCollection<string>? allowedExtensions = null,
		string? directoryFilter = null,
		bool requireRealmMetadata = false,
		AssetType? requiredAssetType = null,
		string? mapNameFilter = null,
		string? mapVersionFilter = null,
		bool requirePlayerColorMask = false)
	{
		return AssetIndexSearch.Search(_assetCollection, _syncLock, GetLatestVersionForMap, searchTerm, allowedExtensions, directoryFilter, requireRealmMetadata, requiredAssetType, mapNameFilter, mapVersionFilter, requirePlayerColorMask);
	}

	public List<IndexedAsset> SearchAssets(
		string? searchTerm,
		IReadOnlyCollection<string>? allowedExtensions,
		string? directoryFilter,
		bool requireRealmMetadata,
		string? requiredAssetType,
		string? mapNameFilter = null,
		string? mapVersionFilter = null,
		bool requirePlayerColorMask = false)
	{
		AssetType? normType = RealmMetadataHelper.NormalizeAssetType(requiredAssetType);
		return SearchAssets(searchTerm, allowedExtensions, directoryFilter, requireRealmMetadata, normType, mapNameFilter, mapVersionFilter, requirePlayerColorMask);
	}

	private static string NormalizePath(string path) => AssetIndexPaths.Normalize(path);

	public void Dispose()
	{
		lock (_p2pSyncLock)
		{
			_p2pDatabase?.Dispose();
			_p2pDatabase = null;
		}

		_database?.Dispose();
	}
}