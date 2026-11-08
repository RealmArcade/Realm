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
	private readonly HashSet<string> _indexingDirectories = new(StringComparer.OrdinalIgnoreCase);
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

	public const int DefaultVerificationSampleSize = 64;

	public AssetIndexVerificationResult VerifyIndex(int sampleSize = DefaultVerificationSampleSize)
	{
		if (sampleSize <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(sampleSize));
		}

		lock (_syncLock)
		{
			var assets = _assetCollection.FindAll()
				.OrderBy(a => a.FilePath, StringComparer.Ordinal)
				.ToList();

			var sampledAssets = SelectDeterministicSample(assets, sampleSize);
			var checkedPaths = new List<string>(sampledAssets.Count);
			var failures = new List<AssetIndexVerificationFailure>();

			foreach (var asset in sampledAssets)
			{
				checkedPaths.Add(asset.FilePath);
				string? failureReason = AssetIndexInspector.Inspect(asset, verifyContentHash: false);
				if (failureReason != null)
				{
					failures.Add(new AssetIndexVerificationFailure(asset.FilePath, failureReason));
				}
			}

			int previousFailureCount = _lastVerificationFailurePaths.Count;
			_lastVerificationFailurePaths = failures.Select(f => f.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

			int stride = CalculateSampleStride(assets.Count, sampleSize);
			string strategy = $"Deterministic stride sample of up to {sampleSize} of {assets.Count} indexed file(s) ordered by path (stride {stride}), always including the {previousFailureCount} file(s) that failed the previous verification.";
			LogVerificationOutcome("sample", strategy, checkedPaths.Count, failures);

			return new AssetIndexVerificationResult(checkedPaths.Count, assets.Count, strategy, checkedPaths, failures);
		}
	}

	public AssetIndexVerificationResult VerifyAllIndexedAssets(bool verifyContentHash, IProgress<AssetIndexProgressUpdate>? progress = null, double progressStart = 0.0, double progressEnd = 1.0)
	{
		lock (_syncLock)
		{
			var assets = _assetCollection.FindAll()
				.OrderBy(a => a.FilePath, StringComparer.Ordinal)
				.ToList();

			var checkedPaths = new List<string>(assets.Count);
			var failures = new List<AssetIndexVerificationFailure>();
			int total = Math.Max(1, assets.Count);

			for (int i = 0; i < assets.Count; i++)
			{
				var asset = assets[i];
				checkedPaths.Add(asset.FilePath);

				string? failureReason = AssetIndexInspector.Inspect(asset, verifyContentHash);
				if (failureReason != null)
				{
					failures.Add(new AssetIndexVerificationFailure(asset.FilePath, failureReason));
				}

				if (progress != null && (i % 128 == 0 || i == assets.Count - 1))
				{
					double fraction = (double)(i + 1) / total;
					progress.Report(new AssetIndexProgressUpdate(progressStart + (progressEnd - progressStart) * fraction, string.Format(TranslationServer.Translate("Verifying indexed file {0}/{1}..."), i + 1, assets.Count)));
				}
			}

			_lastVerificationFailurePaths = failures.Select(f => f.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
			string strategy = verifyContentHash
				? "Full verification of every indexed file, including its content hash."
				: "Full verification of every indexed file.";
			LogVerificationOutcome("full", strategy, checkedPaths.Count, failures);

			return new AssetIndexVerificationResult(checkedPaths.Count, assets.Count, strategy, checkedPaths, failures);
		}
	}

	public int RepairIndexedAssets(IReadOnlyList<AssetIndexVerificationFailure> failures, IProgress<AssetIndexProgressUpdate>? progress = null, double progressStart = 0.0, double progressEnd = 1.0)
	{
		if (failures == null || failures.Count == 0) return 0;

		int repairedCount = 0;
		lock (_syncLock)
		{
			for (int i = 0; i < failures.Count; i++)
			{
				double fraction = (double)(i + 1) / failures.Count;
				progress?.Report(new AssetIndexProgressUpdate(progressStart + (progressEnd - progressStart) * fraction, string.Format(TranslationServer.Translate("Repairing indexed file {0}/{1}..."), i + 1, failures.Count)));
				if (RepairIndexedAsset(failures[i].FilePath))
				{
					repairedCount++;
				}
			}

			if (repairedCount > 0)
			{
				_database.Checkpoint();
			}
		}

		return repairedCount;
	}

	private List<IndexedAsset> SelectDeterministicSample(List<IndexedAsset> assets, int sampleSize)
	{
		var sampledAssets = new List<IndexedAsset>();
		var sampledPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var asset in assets)
		{
			if (sampledAssets.Count >= sampleSize) break;
			if (_lastVerificationFailurePaths.Contains(asset.FilePath) && sampledPaths.Add(asset.FilePath))
			{
				sampledAssets.Add(asset);
			}
		}

		int stride = CalculateSampleStride(assets.Count, sampleSize);
		for (int i = 0; i < assets.Count && sampledAssets.Count < sampleSize; i += stride)
		{
			if (sampledPaths.Add(assets[i].FilePath))
			{
				sampledAssets.Add(assets[i]);
			}
		}

		return sampledAssets;
	}

	private static int CalculateSampleStride(int assetCount, int sampleSize)
	{
		if (assetCount <= sampleSize) return 1;
		return (int)Math.Ceiling((double)assetCount / sampleSize);
	}

	private static void LogVerificationOutcome(string passName, string strategy, int checkedCount, List<AssetIndexVerificationFailure> failures)
	{
		GameLog.LogToConsole($"[AssetIndexService] {passName} verification checked {checkedCount} file(s). {strategy} Problems: {failures.Count}.");
		foreach (var failure in failures)
		{
			GameLog.LogError($"[AssetIndexService] Verification problem in {failure.FilePath}: {failure.Reason}");
		}
	}



	private bool RepairIndexedAsset(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath)) return false;

		string normalizedFilePath = NormalizePath(filePath);
		var asset = _assetCollection.FindOne(x => x.FilePath == normalizedFilePath);
		if (asset == null) return false;

		if (!RestoreCasAssetFileIfNeeded(asset))
		{
			_assetCollection.Delete(asset.Id);
			return true;
		}

		ReindexAssetFromDisk(asset, normalizedFilePath);
		_assetCollection.Update(asset);
		return true;
	}

	private bool RestoreCasAssetFileIfNeeded(IndexedAsset asset)
	{
		if (!AssetIndexInspector.IsCasAsset(asset)) return true;

		string hash = ContentAddressableStorage.NormalizeBlake3Hash(asset.Blake3 ?? Path.GetFileName(asset.FilePath));
		if (hash.Length < 2) return false;

		if (File.Exists(asset.FilePath) && ContentMatchesHash(asset.FilePath, hash)) return true;

		return TryRestoreCasAssetFile(hash, asset.FilePath);
	}

	private static bool TryRestoreCasAssetFile(string hash, string casFilePath)
	{
		foreach (string archiveDirectory in GetArchiveDirectories())
		{
			string[] manifestFiles = Directory.GetFiles(archiveDirectory, "manifest.json", SearchOption.AllDirectories);
			foreach (string manifestFile in manifestFiles)
			{
				string? sourceFilePath = FindManifestSourceForHash(manifestFile, hash);
				if (sourceFilePath == null || !ContentMatchesHash(sourceFilePath, hash)) continue;

				HardLinkHelper.CreateHardLinkOrCopy(casFilePath, sourceFilePath);
				MapAssetManager.Storage.InvalidateAssetPath(hash);
				return File.Exists(casFilePath);
			}
		}

		return false;
	}

	private static IReadOnlyList<string> GetArchiveDirectories()
	{
		var archiveDirectories = new List<string>();
		if (Directory.Exists(MapAssetManager.GlobalArchiveDirectory))
		{
			archiveDirectories.Add(MapAssetManager.GlobalArchiveDirectory);
		}

		if (Directory.Exists(MapAssetManager.P2PArchiveDirectory))
		{
			archiveDirectories.Add(MapAssetManager.P2PArchiveDirectory);
		}

		return archiveDirectories;
	}

	private static string? FindManifestSourceForHash(string manifestFile, string hash)
	{
		var manifest = MapManifest.LoadFromFile(manifestFile);
		if (manifest?.Files == null || manifest.Files.Count == 0) return null;

		string mapDirectory = Path.GetDirectoryName(manifestFile) ?? string.Empty;
		foreach (var entry in manifest.Files)
		{
			if (!string.Equals(ContentAddressableStorage.NormalizeBlake3Hash(entry.Value), hash, StringComparison.OrdinalIgnoreCase)) continue;

			string relativePath = entry.Key.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ? entry.Key.Substring(6) : entry.Key;
			relativePath = relativePath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);

			string candidateFilePath = Path.Combine(mapDirectory, relativePath);
			if (File.Exists(candidateFilePath)) return candidateFilePath;
		}

		return null;
	}

	private static bool ContentMatchesHash(string filePath, string hash)
	{
		try
		{
			string contentHash = ContentAddressableStorage.NormalizeBlake3Hash(RealmMetadataHelper.ComputeBlake3(filePath));
			return string.Equals(contentHash, hash, StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	private void ReindexAssetFromDisk(IndexedAsset asset, string normalizedFilePath)
	{
		var fileInfo = new FileInfo(normalizedFilePath);
		string diskExtension = Path.GetExtension(normalizedFilePath).ToLowerInvariant();

		if (AssetIndexInspector.IsCasAsset(asset))
		{
			asset.FileSizeBytes = fileInfo.Length;
			asset.LastModifiedUtc = fileInfo.LastWriteTimeUtc;
			asset.Extension = diskExtension;
			asset.Blake3 = ContentAddressableStorage.NormalizeBlake3Hash(Path.GetFileName(normalizedFilePath));

			AssetType? diskAssetType = RealmMetadataHelper.NormalizeAssetType(RealmMetadataHelper.ExtractAssetType(normalizedFilePath));
			if (diskAssetType.HasValue)
			{
				asset.AssetType = diskAssetType;
			}

			asset.HasRealmMetadata = asset.HasRealmMetadata || !string.IsNullOrEmpty(RealmMetadataHelper.ExtractMetadata(normalizedFilePath));
			return;
		}

		AssetType? expectedDirType = AssetRecordFactory.GetExpectedAssetTypeFromDirectory(normalizedFilePath);
		AssetRecordFactory.GenerateIndexedAssetFromFile(normalizedFilePath, asset.DirectoryPath, fileInfo, diskExtension, expectedDirType, asset);
	}




	public void SynchronizeCasAndMapPackages(IProgress<AssetIndexProgressUpdate>? progress = null)
	{
		SynchronizeArchiveDirectory(MapAssetManager.GlobalArchiveDirectory, MapAssetManager.Storage, isP2P: false, progress, 0.02, 0.08);
		if (Directory.Exists(MapAssetManager.P2PArchiveDirectory))
		{
			SynchronizeArchiveDirectory(MapAssetManager.P2PArchiveDirectory, MapAssetManager.P2PStorage, isP2P: true, progress, 0.08, 0.12);
		}
	}

	private void SynchronizeArchiveDirectory(string archiveDirectory, ContentAddressableStorage cas, bool isP2P, IProgress<AssetIndexProgressUpdate>? progress, double startPercentage, double endPercentage)
	{
		if (string.IsNullOrEmpty(archiveDirectory) || !Directory.Exists(archiveDirectory) || cas == null) return;

		var manifestFiles = Directory.GetFiles(archiveDirectory, "manifest.json", SearchOption.AllDirectories);
		for (int i = 0; i < manifestFiles.Length; i++)
		{
			ProcessSynchronizeManifest(manifestFiles[i], cas, progress, startPercentage, endPercentage, i, manifestFiles.Length);
		}
	}

	private void ProcessSynchronizeManifest(string manifestFile, ContentAddressableStorage cas, IProgress<AssetIndexProgressUpdate>? progress, double startPercentage, double endPercentage, int index, int totalFiles)
	{
		try
		{
			string mapDir = Path.GetDirectoryName(manifestFile) ?? string.Empty;
			var manifest = MapManifest.LoadFromFile(manifestFile);
			if (manifest?.Files == null || manifest.Files.Count == 0) return;

			string mapName = !string.IsNullOrWhiteSpace(manifest.MapName) ? manifest.MapName : Path.GetFileName(mapDir);
			double currentPercentage = startPercentage + (endPercentage - startPercentage) * ((double)index / Math.Max(1, totalFiles));
			progress?.Report(new AssetIndexProgressUpdate(currentPercentage, string.Format(TranslationServer.Translate("Synchronizing files for {0}..."), mapName)));

			foreach (var kvp in manifest.Files)
			{
				ProcessSynchronizeManifestEntry(kvp.Key, kvp.Value, mapDir, cas);
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[AssetIndexService] SynchronizeArchiveDirectory error on {manifestFile}: {ex.Message}");
		}
	}

	private void ProcessSynchronizeManifestEntry(string virtualPath, string hash, string mapDir, ContentAddressableStorage cas)
	{
		string blake3 = ContentAddressableStorage.NormalizeBlake3Hash(hash);
		if (string.IsNullOrEmpty(blake3) || blake3.Length < 2) return;
		if (virtualPath.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase)) return;

		string mapRelativePath = virtualPath.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ? virtualPath.Substring(6) : virtualPath;
		mapRelativePath = mapRelativePath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
		string mapFilePath = Path.Combine(mapDir, mapRelativePath);

		string? casFilePath = cas.FindAssetFilePath(blake3);
		casFilePath = EnsureCasFileExists(casFilePath, blake3, virtualPath, mapDir, mapFilePath, cas);

		if (casFilePath != null && !File.Exists(mapFilePath))
		{
			HardLinkHelper.CreateHardLinkOrCopy(mapFilePath, casFilePath);
		}

		EnsureSidecarCacheExists(blake3, mapDir, casFilePath, mapFilePath, cas);
	}

	private string? EnsureCasFileExists(string? casFilePath, string blake3, string virtualPath, string mapDir, string mapFilePath, ContentAddressableStorage cas)
	{
		if (casFilePath != null && File.Exists(casFilePath)) return casFilePath;

		string extension = Path.GetExtension(virtualPath);
		if (string.IsNullOrEmpty(extension)) extension = Path.GetExtension(mapFilePath);
		if (string.IsNullOrEmpty(extension)) extension = ".bin";

		string shard = blake3.Substring(0, 2);
		string shardDirectory = Path.Combine(cas.AssetsDirectory, shard);
		if (!Directory.Exists(shardDirectory)) Directory.CreateDirectory(shardDirectory);

		string targetCasPath = Path.Combine(shardDirectory, $"{blake3}{extension.ToLowerInvariant()}");
		if (File.Exists(targetCasPath))
		{
			cas.InvalidateAssetPath(blake3);
			return targetCasPath;
		}

		string? sourceFile = FindSourceFileForCas(mapDir, virtualPath, mapFilePath);
		if (sourceFile == null || !File.Exists(sourceFile)) return null;

		if (!ContentMatchesHash(sourceFile, blake3))
		{
			GameLog.LogError($"[AssetIndexService] Refusing to restore {targetCasPath}: {sourceFile} does not match its recorded hash {blake3}.");
			return null;
		}

		HardLinkHelper.CreateHardLinkOrCopy(targetCasPath, sourceFile, overwrite: false);
		cas.InvalidateAssetPath(blake3);

		if (!File.Exists(mapFilePath) && File.Exists(targetCasPath))
		{
			HardLinkHelper.CreateHardLinkOrCopy(mapFilePath, targetCasPath, overwrite: false);
		}

		return File.Exists(targetCasPath) ? targetCasPath : null;
	}

	private string? FindSourceFileForCas(string mapDir, string virtualPath, string mapFilePath)
	{
		if (File.Exists(mapFilePath)) return mapFilePath;

		string? fallback = MapAssetHelper.FindModelOnDisk(mapDir, null, Path.GetFileName(virtualPath));
		if (!string.IsNullOrEmpty(fallback) && File.Exists(fallback)) return fallback;

		fallback = MapAssetHelper.FindAssetOnDisk(mapDir, "other", virtualPath);
		if (!string.IsNullOrEmpty(fallback) && File.Exists(fallback)) return fallback;

		string candidate = Path.Combine(mapDir, Path.GetFileName(virtualPath));
		if (File.Exists(candidate)) return candidate;

		return null;
	}

	private void EnsureSidecarCacheExists(string blake3, string mapDir, string? casFilePath, string mapFilePath, ContentAddressableStorage cas)
	{
		string shardName = blake3.Substring(0, 2);
		string casSidecarPath = Path.Combine(cas.SidecarCacheDirectory, shardName, $"{blake3}.json");
		string mapSidecarPath = Path.Combine(mapDir, ".sidecarcache", shardName, $"{blake3}.json");
		string mapFlatSidecarPath = Path.Combine(mapDir, ".sidecarcache", $"{blake3}.json");

		if (!File.Exists(casSidecarPath))
		{
			PopulateCasSidecar(blake3, casSidecarPath, mapSidecarPath, mapFlatSidecarPath, casFilePath, mapFilePath, cas);
		}

		if (File.Exists(casSidecarPath) && !File.Exists(mapSidecarPath))
		{
			HardLinkHelper.CreateHardLinkOrCopy(mapSidecarPath, casSidecarPath);
		}
	}

	private static void PopulateCasSidecar(string blake3, string casSidecarPath, string mapSidecarPath, string mapFlatSidecarPath, string? casFilePath, string mapFilePath, ContentAddressableStorage cas)
	{
		if (TryCopyExistingSidecar(casSidecarPath, mapSidecarPath))
		{
			EnsureSidecarHasAssetType(blake3, casSidecarPath, casFilePath, mapFilePath, cas);
			return;
		}
		if (TryCopyExistingSidecar(casSidecarPath, mapFlatSidecarPath))
		{
			EnsureSidecarHasAssetType(blake3, casSidecarPath, casFilePath, mapFilePath, cas);
			return;
		}

		ExtractAndSaveCasSidecar(blake3, casFilePath, mapFilePath, cas);
	}

	private static void EnsureSidecarHasAssetType(string blake3, string casSidecarPath, string? casFilePath, string mapFilePath, ContentAddressableStorage cas)
	{
		try
		{
			if (File.Exists(casSidecarPath))
			{
				string existingJson = File.ReadAllText(casSidecarPath);
				if (JsonNode.Parse(existingJson) is JsonObject obj)
				{
					if (obj.ContainsKey(AssetMetadataKeys.AssetType))
					{
						return;
					}
				}
			}

			string? assetForMetadata = casFilePath ?? (File.Exists(mapFilePath) ? mapFilePath : null);
			if (assetForMetadata != null && File.Exists(assetForMetadata))
			{
				string? extractedMetadata = RealmMetadataHelper.ExtractMetadata(assetForMetadata);
				if (!string.IsNullOrWhiteSpace(extractedMetadata))
				{
					if (File.Exists(casSidecarPath))
					{
						string currentSidecar = File.ReadAllText(casSidecarPath);
						string merged = AuthorSignatureHelper.MergeMetadataHeaders(extractedMetadata, currentSidecar, false);
						File.WriteAllText(casSidecarPath, merged);
						cas.UpdateSidecarCache(blake3, merged);
					}
					else
					{
						cas.UpdateSidecarCache(blake3, extractedMetadata);
					}
				}
			}
		}
		catch
		{
		}
	}

	private static bool TryCopyExistingSidecar(string destPath, string srcPath)
	{
		if (!File.Exists(srcPath)) return false;
		HardLinkHelper.CreateHardLinkOrCopy(destPath, srcPath);
		return true;
	}

	private static void ExtractAndSaveCasSidecar(string blake3, string? casFilePath, string mapFilePath, ContentAddressableStorage cas)
	{
		string? assetForMetadata = casFilePath ?? (File.Exists(mapFilePath) ? mapFilePath : null);
		if (assetForMetadata == null || !File.Exists(assetForMetadata)) return;

		string? extractedMetadata = RealmMetadataHelper.ExtractMetadata(assetForMetadata);
		if (!string.IsNullOrWhiteSpace(extractedMetadata))
		{
			cas.UpdateSidecarCache(blake3, extractedMetadata);
		}
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
			InvalidateCasPathCaches();
			ScanAllCasManifests(progress);

			progress?.Report(new AssetIndexProgressUpdate(0.95, TranslationServer.Translate("Verifying every indexed file...")));
			var verification = VerifyAllIndexedAssets(verifyContentHash: true, progress, 0.95, 0.98);
			progress?.Report(new AssetIndexProgressUpdate(0.98, string.Format(TranslationServer.Translate("Verified {0} indexed file(s): {1} problem(s) found."), verification.CheckedCount, verification.Failures.Count)));

			if (verification.Failures.Count > 0)
			{
				int repairedCount = RepairIndexedAssets(verification.Failures, progress, 0.98, 0.99);
				int remainingCount = CountRemainingVerificationFailures(verification.Failures);
				progress?.Report(new AssetIndexProgressUpdate(0.99, string.Format(TranslationServer.Translate("Repaired {0} indexed file(s); {1} problem(s) remain."), repairedCount, remainingCount)));
			}

			progress?.Report(new AssetIndexProgressUpdate(1.0, TranslationServer.Translate("Asset index repair complete!")));
		}
	}

	private static void InvalidateCasPathCaches()
	{
		MapAssetManager.Storage.InvalidateAllAssetPaths();
		if (Directory.Exists(MapAssetManager.P2PArchiveDirectory))
		{
			MapAssetManager.P2PStorage.InvalidateAllAssetPaths();
		}
	}

	private int CountRemainingVerificationFailures(IReadOnlyList<AssetIndexVerificationFailure> failures)
	{
		int remainingCount = 0;
		foreach (var failure in failures)
		{
			var asset = GetAssetByPath(failure.FilePath);
			if (asset == null) continue;
			if (AssetIndexInspector.Inspect(asset, verifyContentHash: true) != null) remainingCount++;
		}

		return remainingCount;
	}

	public AssetIndexService()
	{
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
		lock (_syncLock)
		{
			string legacyArchive = NormalizePath(Path.Combine(MapAssetManager.GlobalArchiveDirectory, "global_assets.rmap"));
			string casAssetsDirectory = GlobalCasAssetsDirectory;
			string globalArchive = NormalizePath(MapAssetManager.GlobalArchiveDirectory);
			string p2pArchive = NormalizePath(MapAssetManager.P2PArchiveDirectory);

			RemoveForbiddenFolders(legacyArchive, casAssetsDirectory);

			if (!Directory.Exists(casAssetsDirectory))
			{
				Directory.CreateDirectory(casAssetsDirectory);
			}

			RemoveOrphanedAssets(casAssetsDirectory, globalArchive, p2pArchive);
			RemoveOrphanedPackages();

			_database.Checkpoint();
		}
	}

	private void RemoveForbiddenFolders(string legacyArchive, string casAssetsDirectory)
	{
		var forbiddenFolders = _folderCollection.FindAll()
			.Where(f => IsForbiddenPath(f.DirectoryPath) ||
			            string.Equals(f.DirectoryPath, legacyArchive, StringComparison.OrdinalIgnoreCase) ||
			            string.Equals(f.DirectoryPath, casAssetsDirectory, StringComparison.OrdinalIgnoreCase) ||
			            f.DirectoryPath.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase) ||
			            f.DirectoryPath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
			.Select(f => (BsonValue)f.Id)
			.ToArray();

		if (forbiddenFolders.Length > 0)
		{
			_folderCollection.DeleteMany(Query.In("_id", forbiddenFolders));
		}
	}

	private void RemoveOrphanedAssets(string casAssetsDirectory, string globalArchive, string p2pArchive)
	{
		var validFolders = _folderCollection.FindAll()
			.Select(f => f.DirectoryPath)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		var orphanedAssets = _assetCollection.FindAll()
			.Where(a => IsAssetOrphaned(a, validFolders, casAssetsDirectory, globalArchive, p2pArchive))
			.Select(a => (BsonValue)a.Id)
			.ToArray();

		if (orphanedAssets.Length > 0)
		{
			_assetCollection.DeleteMany(Query.In("_id", orphanedAssets));
		}
	}

	private bool IsAssetOrphaned(IndexedAsset asset, HashSet<string> validFolders, string casAssetsDirectory, string globalArchive, string p2pArchive)
	{
		if (IsForbiddenAsset(asset)) return true;
		if (IsAssetInValidLocation(asset, validFolders, casAssetsDirectory, globalArchive, p2pArchive)) return false;

		return true;
	}

	private bool IsForbiddenAsset(IndexedAsset asset)
	{
		if (IsForbiddenPath(asset.DirectoryPath)) return true;
		if (IsForbiddenPath(asset.FilePath)) return true;
		if (asset.DirectoryPath.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase)) return true;
		if (asset.DirectoryPath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)) return true;
		if (asset.FilePath.Contains("/extracted/", StringComparison.OrdinalIgnoreCase)) return true;
		if (asset.FilePath.Contains("\\extracted\\", StringComparison.OrdinalIgnoreCase)) return true;

		return false;
	}

	private bool IsAssetInValidLocation(IndexedAsset asset, HashSet<string> validFolders, string casAssetsDirectory, string globalArchive, string p2pArchive)
	{
		if (validFolders.Contains(asset.DirectoryPath)) return true;
		if (string.Equals(asset.DirectoryPath, casAssetsDirectory, StringComparison.OrdinalIgnoreCase)) return true;
		if (asset.FilePath.StartsWith(globalArchive, StringComparison.OrdinalIgnoreCase)) return true;
		if (asset.FilePath.StartsWith(p2pArchive, StringComparison.OrdinalIgnoreCase)) return true;
		if (!string.IsNullOrEmpty(asset.MapName)) return true;

		return false;
	}

	private void RemoveOrphanedPackages()
	{
		var orphanedPackages = _mapPackageCollection.FindAll()
			.Where(p => string.IsNullOrWhiteSpace(p.ManifestPath) || !File.Exists(p.ManifestPath))
			.Select(p => (BsonValue)p.Id)
			.ToArray();

		if (orphanedPackages.Length > 0)
		{
			_mapPackageCollection.DeleteMany(Query.In("_id", orphanedPackages));
		}
	}

	public void ScanAllCasManifests(IProgress<AssetIndexProgressUpdate>? progress = null)
	{
		var referencedCasPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		ScanGlobalManifests(referencedCasPaths, progress);
		CleanupOrphanedCasData(referencedCasPaths);
		ScanP2PManifests(progress);
	}

	private void ScanGlobalManifests(HashSet<string> referencedCasPaths, IProgress<AssetIndexProgressUpdate>? progress)
	{
		if (!Directory.Exists(MapAssetManager.GlobalArchiveDirectory)) return;

		var manifestFiles = Directory.GetFiles(MapAssetManager.GlobalArchiveDirectory, "manifest.json", SearchOption.AllDirectories);
		for (int i = 0; i < manifestFiles.Length; i++)
		{
			ProcessGlobalManifestFile(manifestFiles[i], i, manifestFiles.Length, referencedCasPaths, progress);
		}
	}

	private void ProcessGlobalManifestFile(string file, int index, int total, HashSet<string> referencedCasPaths, IProgress<AssetIndexProgressUpdate>? progress)
	{
		try
		{
			var manifest = MapManifest.LoadFromFile(file);
			if (manifest?.Files == null || manifest.Files.Count == 0) return;

			string mapName = !string.IsNullOrWhiteSpace(manifest.MapName) ? manifest.MapName : Path.GetFileName(Path.GetDirectoryName(file)) ?? "Map";
			double pct = 0.20 + 0.65 * ((double)index / Math.Max(1, total));
			progress?.Report(new AssetIndexProgressUpdate(pct, string.Format(TranslationServer.Translate("Indexing manifest {0}/{1}: {2}..."), index + 1, total, mapName)));

			var paths = RegisterManifestInternal(manifest, file, isP2P: false);
			foreach (var p in paths)
			{
				referencedCasPaths.Add(p);
			}
		}
		catch
		{
		}
	}

	private void CleanupOrphanedCasData(HashSet<string> referencedCasPaths)
	{
		lock (_syncLock)
		{
			var orphanedCasAssets = _assetCollection.Find(Query.EQ("DirectoryPath", GlobalCasAssetsDirectory))
				.Where(a => !referencedCasPaths.Contains(a.FilePath) || !File.Exists(a.FilePath))
				.Select(a => (BsonValue)a.Id)
				.ToArray();

			if (orphanedCasAssets.Length > 0)
			{
				_assetCollection.DeleteMany(Query.In("_id", orphanedCasAssets));
			}

			var orphanedPackages = _mapPackageCollection.FindAll()
				.Where(p => !p.IsP2P && (string.IsNullOrWhiteSpace(p.ManifestPath) || !File.Exists(p.ManifestPath)))
				.Select(p => (BsonValue)p.Id)
				.ToArray();

			if (orphanedPackages.Length > 0)
			{
				_mapPackageCollection.DeleteMany(Query.In("_id", orphanedPackages));
			}

			_database.Checkpoint();
		}
	}

	private void ScanP2PManifests(IProgress<AssetIndexProgressUpdate>? progress)
	{
		if (!Directory.Exists(MapAssetManager.P2PArchiveDirectory)) return;

		var p2pManifestFiles = Directory.GetFiles(MapAssetManager.P2PArchiveDirectory, "manifest.json", SearchOption.AllDirectories);
		for (int j = 0; j < p2pManifestFiles.Length; j++)
		{
			ProcessP2PManifestFile(p2pManifestFiles[j], j, p2pManifestFiles.Length, progress);
		}
	}

	private void ProcessP2PManifestFile(string file, int index, int total, IProgress<AssetIndexProgressUpdate>? progress)
	{
		try
		{
			var manifest = MapManifest.LoadFromFile(file);
			if (manifest?.Files == null || manifest.Files.Count == 0) return;

			double pct = 0.85 + 0.12 * ((double)index / Math.Max(1, total));
			progress?.Report(new AssetIndexProgressUpdate(pct, string.Format(TranslationServer.Translate("Indexing P2P archive {0}/{1}..."), index + 1, total)));
			RegisterManifest(manifest, file, isP2P: true);
		}
		catch
		{
		}
	}


	public void RegisterManifest(MapManifest manifest, string manifestPath, bool isP2P = false)
	{
		if (manifest == null) return;
		lock (isP2P ? _p2pSyncLock : _syncLock)
		{
			RegisterManifestInternal(manifest, manifestPath, isP2P);
		}
	}

	private List<string> RegisterManifestInternal(MapManifest manifest, string manifestPath, bool isP2P)
	{
		var indexedPaths = new List<string>();
		if (manifest == null) return indexedPaths;

		string mapName = !string.IsNullOrWhiteSpace(manifest.MapName) ? manifest.MapName.Trim() : Path.GetFileNameWithoutExtension(manifestPath).Replace("_manifest", "");
		string mapVersion = !string.IsNullOrWhiteSpace(manifest.Version) ? manifest.Version.Trim() : "1.0.0";

		var database = isP2P ? GetP2PDatabase() : _database;
		var mapPackageCollection = isP2P ? database.GetCollection<IndexedMapPackage>("map_packages") : _mapPackageCollection;
		var assetCollection = isP2P ? database.GetCollection<IndexedAsset>("assets") : _assetCollection;
		var storage = isP2P ? MapAssetManager.P2PStorage : MapAssetManager.Storage;
		string storageDirectory = isP2P ? MapAssetManager.P2PArchiveDirectory : GlobalCasAssetsDirectory;

		UpsertManifestPackage(manifest, manifestPath, isP2P, mapName, mapVersion, mapPackageCollection);

		if (manifest.Files != null)
		{
			ProcessManifestFiles(manifest, mapName, mapVersion, storage, storageDirectory, assetCollection, indexedPaths);
		}

		database.Checkpoint();
		return indexedPaths;
	}

	private void UpsertManifestPackage(MapManifest manifest, string manifestPath, bool isP2P, string mapName, string mapVersion, ILiteCollection<IndexedMapPackage> mapPackageCollection)
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

	private void ProcessManifestFiles(MapManifest manifest, string mapName, string mapVersion, ContentAddressableStorage storage, string storageDirectory, ILiteCollection<IndexedAsset> assetCollection, List<string> indexedPaths)
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

	private void ProcessSingleManifestFile(MapManifest manifest, string virtualPath, string hash, string mapName, string mapVersion, ContentAddressableStorage storage, string storageDirectory, Dictionary<string, IndexedAsset> existingAssetMap, HashSet<string> processedPaths, List<IndexedAsset> assetsToUpdate, List<IndexedAsset> assetsToInsert, List<string> indexedPaths)
	{
		string norm = ContentAddressableStorage.NormalizeBlake3Hash(hash);
		string? resolvedPath = storage.FindAssetFilePath(norm);
		if (resolvedPath == null || !File.Exists(resolvedPath)) return;

		string normPath = NormalizePath(resolvedPath);
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

	private void UpdateExistingManifestAsset(IndexedAsset existingAsset, string virtualPath, string norm, string normPath, string mapName, string mapVersion, ContentAddressableStorage storage, string storageDirectory, List<IndexedAsset> assetsToUpdate)
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

	private bool TryUpdateAssetType(IndexedAsset existingAsset, string virtualPath, AssetType? parsedType, string? existingMetaJson)
	{
		AssetType? existingAssetType = ResolveAssetTypeFromParsed(parsedType, virtualPath);
		if (existingAsset.AssetType == existingAssetType) return false;

		existingAsset.AssetType = existingAssetType;
		existingAsset.HasRealmMetadata = !string.IsNullOrEmpty(existingMetaJson) || existingAsset.HasRealmMetadata || existingAssetType.HasValue;
		return true;
	}

	private bool TryUpdateAssetProperties(IndexedAsset existingAsset, bool? parsedSupportsTeamColor, string? parsedChromaKey, string? existingMetaJson, string normPath)
	{
		bool existingHasMask = parsedSupportsTeamColor ?? AssetRecordFactory.DetermineHasPlayerColorMask(existingMetaJson, normPath);
		string resolvedChroma = parsedChromaKey ?? string.Empty;

		if (existingAsset.HasPlayerColorMask == existingHasMask && existingAsset.ChromaKey == resolvedChroma) return false;

		existingAsset.HasPlayerColorMask = existingHasMask;
		existingAsset.ChromaKey = resolvedChroma;
		return true;
	}

	private bool TryUpdateAssetMapInfo(IndexedAsset existingAsset, string mapName, string mapVersion, string storageDirectory)
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

	private AssetType? ResolveAssetTypeFromParsed(AssetType? parsedType, string virtualPath)
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

	private void InsertNewManifestAsset(MapManifest manifest, string virtualPath, string norm, string normPath, string mapName, string mapVersion, ContentAddressableStorage storage, string storageDirectory, Dictionary<string, IndexedAsset> existingAssetMap, List<IndexedAsset> assetsToInsert)
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

	private IndexedAsset CreateBaseManifestAsset(string virtualPath, string norm, string normPath, string mapName, string mapVersion, string storageDirectory, FileInfo fi)
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

	private AssetType? ResolveFallbackAssetType(AssetType? parsedType, string virtualPath, List<string> tags)
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

	private List<string> BuildManifestAssetTags(MapManifest manifest, string virtualPath, string normPath, string storageDirectory, string fileName, List<string> parsedTags)
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

	private static bool IsForbiddenPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path)) return true;
		string norm = path.Replace('\\', '/').ToLowerInvariant();
		return norm.Contains("maptemplate");
	}

	public bool IsDirectoryIndexing(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath)) return false;
		lock (_syncLock)
		{
			return _indexingDirectories.Contains(NormalizePath(directoryPath));
		}
	}

	public IReadOnlyList<string> GetIndexedDirectories()
	{
		lock (_syncLock)
		{
			return _folderCollection.FindAll()
				.Select(f => f.DirectoryPath)
				.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}
	}

	public int GetAssetCount()
	{
		lock (_syncLock)
		{
			return _assetCollection.Count();
		}
	}

	public void AddDirectory(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath)) return;

		string normalizedPath = NormalizePath(directoryPath);
		if (!IsValidDirectoryForIndexing(normalizedPath)) return;

		if (!TryRegisterDirectory(normalizedPath)) return;

		DirectoryIndexingStateChanged?.Invoke(normalizedPath, true);
		StartDirectoryScanTask(normalizedPath);
	}

	private bool IsValidDirectoryForIndexing(string normalizedPath)
	{
		if (IsForbiddenPath(normalizedPath) || !Directory.Exists(normalizedPath)) return false;

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
		lock (_syncLock)
		{
			var existingFolder = _folderCollection.FindOne(x => x.DirectoryPath == normalizedPath);
			if (existingFolder == null)
			{
				existingFolder = new IndexedFolder
				{
					DirectoryPath = normalizedPath,
					LastScannedUtc = DateTime.MinValue
				};
				_folderCollection.Insert(existingFolder);
				_database.Checkpoint();
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

				DirectoryIndexingStateChanged?.Invoke(normalizedPath, false);
				DirectoryScanCompleted?.Invoke(normalizedPath);
			}
		});
	}


	public void RemoveDirectory(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath))
		{
			return;
		}

		string normalizedPath = NormalizePath(directoryPath);
		if (string.Equals(normalizedPath, GlobalCasAssetsDirectory, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		lock (_syncLock)
		{
			_indexingDirectories.Remove(normalizedPath);

			_folderCollection.DeleteMany(f => f.DirectoryPath == normalizedPath);
			var leftoverFolders = _folderCollection.FindAll()
				.Where(f => string.Equals(f.DirectoryPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
				.Select(f => (BsonValue)f.Id)
				.ToArray();
			if (leftoverFolders.Length > 0)
			{
				_folderCollection.DeleteMany(Query.In("_id", leftoverFolders));
			}

			_assetCollection.DeleteMany(Query.EQ("DirectoryPath", normalizedPath));
			_assetCollection.DeleteMany(Query.StartsWith("FilePath", normalizedPath + "/"));
			_assetCollection.DeleteMany(Query.StartsWith("FilePath", normalizedPath + "\\"));

			_database.Checkpoint();
		}

		DirectoryIndexingStateChanged?.Invoke(normalizedPath, false);
	}

	public void RescanDirectory(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath))
		{
			return;
		}

		string normalizedPath = NormalizePath(directoryPath);
		if (IsForbiddenPath(normalizedPath) || !Directory.Exists(normalizedPath))
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

		DirectoryIndexingStateChanged?.Invoke(normalizedPath, true);

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

				DirectoryIndexingStateChanged?.Invoke(normalizedPath, false);
				DirectoryScanCompleted?.Invoke(normalizedPath);
			}
		});
	}

	public void RescanAllDirectories()
	{
		List<string> dirsToScan = PrepareDirectoriesForRescan();

		foreach (var dir in dirsToScan)
		{
			DirectoryIndexingStateChanged?.Invoke(dir, true);
		}

		Task.Run(() => ExecuteRescanTask(dirsToScan));
	}

	private List<string> PrepareDirectoriesForRescan()
	{
		List<string> dirsToScan;
		lock (_syncLock)
		{
			CleanOrphanedAssetsFromIndex();

			dirsToScan = _folderCollection.FindAll()
				.Select(f => f.DirectoryPath)
				.Where(p => Directory.Exists(p) && !_indexingDirectories.Contains(p) && !string.Equals(p, GlobalCasAssetsDirectory, StringComparison.OrdinalIgnoreCase))
				.ToList();

			foreach (var dir in dirsToScan)
			{
				_indexingDirectories.Add(dir);
			}

			_database.Checkpoint();
		}

		return dirsToScan;
	}

	private void CleanOrphanedAssetsFromIndex()
	{
		var validFolders = _folderCollection.FindAll()
			.Select(f => f.DirectoryPath)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		string globalArchive = NormalizePath(MapAssetManager.GlobalArchiveDirectory);
		string p2pArchive = NormalizePath(MapAssetManager.P2PArchiveDirectory);

		var orphanedAssets = _assetCollection.FindAll()
			.Where(a => (!validFolders.Contains(a.DirectoryPath) &&
			             !string.Equals(a.DirectoryPath, GlobalCasAssetsDirectory, StringComparison.OrdinalIgnoreCase) &&
			             !a.FilePath.StartsWith(globalArchive, StringComparison.OrdinalIgnoreCase) &&
			             !a.FilePath.StartsWith(p2pArchive, StringComparison.OrdinalIgnoreCase) &&
			             string.IsNullOrEmpty(a.MapName)) ||
			            IsForbiddenPath(a.DirectoryPath))
			.Select(a => (BsonValue)a.Id)
			.ToArray();

		if (orphanedAssets.Length > 0)
		{
			_assetCollection.DeleteMany(Query.In("_id", orphanedAssets));
		}
	}

	private void ExecuteRescanTask(List<string> dirsToScan)
	{
		try
		{
			ScanAllCasManifests();
			DirectoryScanCompleted?.Invoke(GlobalCasAssetsDirectory);
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

			DirectoryIndexingStateChanged?.Invoke(dir, false);
			DirectoryScanCompleted?.Invoke(dir);
		}
	}

	private void ScanDirectoryInternal(string normalizedDirectoryPath)
	{
		if (!Directory.Exists(normalizedDirectoryPath)) return;

		if (string.Equals(normalizedDirectoryPath, GlobalCasAssetsDirectory, StringComparison.OrdinalIgnoreCase))
		{
			ScanAllCasManifests();
			DirectoryScanCompleted?.Invoke(GlobalCasAssetsDirectory);
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
				_assetCollection.Upsert(batchToUpsert);
			}

			batchToUpsert.Clear();
		}

		CleanupStaleAssetsAndRecordScan(normalizedDirectoryPath, existingAssets, discoveredFiles);
	}

	private Dictionary<string, IndexedAsset> GetExistingAssetsInDirectory(string normalizedDirectoryPath)
	{
		lock (_syncLock)
		{
			return _assetCollection.Find(Query.EQ("DirectoryPath", normalizedDirectoryPath))
				.ToDictionary(x => x.FilePath, StringComparer.OrdinalIgnoreCase);
		}
	}

	private void ProcessScannedFile(string filePath, string normalizedDirectoryPath, HashSet<string> discoveredFiles, Dictionary<string, IndexedAsset> existingAssets, List<IndexedAsset> batchToUpsert)
	{
		string normalizedFilePath = NormalizePath(filePath);
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
					_assetCollection.Upsert(batchToUpsert);
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
		lock (_syncLock)
		{
			var idsToDelete = existingAssets.Values
				.Where(x => !discoveredFiles.Contains(x.FilePath))
				.Select(x => (BsonValue)x.Id)
				.ToArray();

			if (idsToDelete.Length > 0)
			{
				_assetCollection.DeleteMany(Query.In("_id", idsToDelete));
			}

			var folderRecord = _folderCollection.FindOne(x => x.DirectoryPath == normalizedDirectoryPath);
			if (folderRecord != null)
			{
				folderRecord.LastScannedUtc = DateTime.UtcNow;
				_folderCollection.Update(folderRecord);
			}

			_database.Checkpoint();
		}
	}




	public void UpdateAssetTags(string filePath, List<string> newTags)
	{
		if (string.IsNullOrWhiteSpace(filePath))
		{
			return;
		}

		string normalizedFilePath = NormalizePath(filePath);
		lock (_syncLock)
		{
			var asset = _assetCollection.FindOne(x => x.FilePath == normalizedFilePath);
			if (asset == null)
			{
				return;
			}

			asset.Tags = newTags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim().ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			asset.MetadataJson = JsonSerializer.Serialize(new AssetMetadataModel { Tags = asset.Tags });
			_assetCollection.Update(asset);

			RealmMetadataHelper.SetTags(normalizedFilePath, asset.Tags);

			bool isCasFile = normalizedFilePath.StartsWith(GlobalCasAssetsDirectory + "/", StringComparison.OrdinalIgnoreCase);
			if (isCasFile)
			{
				string hash = ContentAddressableStorage.NormalizeBlake3Hash(Path.GetFileName(normalizedFilePath));
				string? updatedMeta = RealmMetadataHelper.ExtractMetadata(normalizedFilePath);
				if (!string.IsNullOrWhiteSpace(updatedMeta))
				{
					MapAssetManager.Storage.UpdateSidecarCache(hash, updatedMeta);
				}
			}
			else
			{
				try
				{
					string sidecarPath = normalizedFilePath + ".json";
					var root = new JsonObject
					{
						["tags"] = new JsonArray(asset.Tags.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray())
					};
					File.WriteAllText(sidecarPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
				}
				catch (Exception ex)
				{
					GameLog.LogError($"[AssetIndexService] UpdateAssetTags error writing sidecar: {ex.Message}");
				}
			}

			_database.Checkpoint();
		}
	}

	public void UpdateAssetType(string filePath, AssetType newAssetType)
	{
		if (string.IsNullOrWhiteSpace(filePath))
		{
			return;
		}

		string normalizedFilePath = NormalizePath(filePath);
		lock (_syncLock)
		{
			var asset = _assetCollection.FindOne(x => x.FilePath == normalizedFilePath);
			if (asset == null)
			{
				return;
			}

			asset.AssetType = newAssetType;
			asset.HasRealmMetadata = true;
			_assetCollection.Update(asset);

			RealmMetadataHelper.SetAssetType(normalizedFilePath, newAssetType.ToCanonicalString());

			bool isCasFile = normalizedFilePath.StartsWith(GlobalCasAssetsDirectory + "/", StringComparison.OrdinalIgnoreCase);
			if (isCasFile)
			{
				string hash = ContentAddressableStorage.NormalizeBlake3Hash(Path.GetFileName(normalizedFilePath));
				string? updatedMeta = RealmMetadataHelper.ExtractMetadata(normalizedFilePath);
				if (!string.IsNullOrWhiteSpace(updatedMeta))
				{
					MapAssetManager.Storage.UpdateSidecarCache(hash, updatedMeta);
				}
			}

			_database.Checkpoint();
		}
	}

	public void UpdateAssetType(string filePath, string newAssetType)
	{
		var norm = RealmMetadataHelper.NormalizeAssetType(newAssetType);
		if (norm.HasValue)
		{
			UpdateAssetType(filePath, norm.Value);
		}
	}

	public static string GetCanonicalBlake3(IndexedAsset asset)
	{
		if (!string.IsNullOrEmpty(asset.Blake3))
		{
			return asset.Blake3;
		}

		if (asset.FilePath.StartsWith(GlobalCasAssetsDirectory + "/", StringComparison.OrdinalIgnoreCase) ||
		    asset.FilePath.StartsWith(GlobalCasAssetsDirectory + "\\", StringComparison.OrdinalIgnoreCase))
		{
			return ContentAddressableStorage.NormalizeBlake3Hash(Path.GetFileName(asset.FilePath));
		}

		return ContentAddressableStorage.NormalizeBlake3Hash(asset.FileName);
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
		try
		{
			lock (_syncLock)
			{
				var query = _assetCollection.Query();

				query = ApplyMapAndDirectoryFilters(query, mapNameFilter, mapVersionFilter, directoryFilter);

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

	private ILiteQueryable<IndexedAsset> ApplyMapAndDirectoryFilters(ILiteQueryable<IndexedAsset> query, string? mapNameFilter, string? mapVersionFilter, string? directoryFilter)
	{
		if (!string.IsNullOrWhiteSpace(mapNameFilter))
		{
			string targetVersion = mapVersionFilter ?? "latest";
			if (string.Equals(targetVersion, "latest", StringComparison.OrdinalIgnoreCase))
			{
				targetVersion = GetLatestVersionForMap(mapNameFilter) ?? "";
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

	private ILiteQueryable<IndexedAsset> ApplyExtensionFilters(ILiteQueryable<IndexedAsset> query, IReadOnlyCollection<string>? allowedExtensions, out HashSet<string>? extensionFilterSet)
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

	private ILiteQueryable<IndexedAsset> ApplyMetadataFilters(ILiteQueryable<IndexedAsset> query, bool requireRealmMetadata, AssetType? requiredAssetType, bool requirePlayerColorMask)
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

	private ILiteQueryable<IndexedAsset> ApplySearchTermFilter(ILiteQueryable<IndexedAsset> query, string? searchTerm)
	{
		string[] terms = !string.IsNullOrWhiteSpace(searchTerm) ? searchTerm.Split((char[])null, StringSplitOptions.RemoveEmptyEntries) : [];

		foreach (var queryTerm in terms)
		{
			query = query.Where(x => x.FileName.Contains(queryTerm, StringComparison.OrdinalIgnoreCase) ||
			                          x.FilePath.Contains(queryTerm, StringComparison.OrdinalIgnoreCase) ||
			                          (x.Tags != null && x.Tags.Any(t => t.Contains(queryTerm, StringComparison.OrdinalIgnoreCase)))
			                    );
		}

		return query;
	}

	private List<IndexedAsset> DeduplicateSearchResults(List<IndexedAsset> filtered)
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