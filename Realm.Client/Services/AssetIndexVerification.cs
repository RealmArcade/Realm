using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using LiteDB;
using Realm.Client.Network;
using Realm.Client.Utils;
using Realm.EditorAPI;
using Realm.Shared;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

/// <summary>
///     Checking the asset index against the files it claims to describe, and healing the entries that no
///     longer match. A verification pass picks a deterministic sample or walks every entry and reports what
///     it found; a repair pass restores the content-addressed file from a map archive when it can, reindexes
///     it from disk when it cannot, and drops the entry when the bytes are gone. The collection, the lock,
///     the database and the set of paths that failed last time are supplied by the caller.
/// </summary>
internal static class AssetIndexVerification
{
	internal const int DefaultSampleSize = 64;

	internal static AssetIndexVerificationResult VerifySample(
		ILiteCollection<IndexedAsset> collection, object syncLock, ref HashSet<string> lastFailurePaths, int sampleSize)
	{
		if (sampleSize <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(sampleSize));
		}

		lock (syncLock)
		{
			var assets = collection.FindAll()
				.OrderBy(a => a.FilePath, StringComparer.Ordinal)
				.ToList();

			var sampledAssets = SelectDeterministicSample(assets, sampleSize, lastFailurePaths);
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

			int previousFailureCount = lastFailurePaths.Count;
			lastFailurePaths = failures.Select(f => f.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

			int stride = CalculateSampleStride(assets.Count, sampleSize);
			string strategy = $"Deterministic stride sample of up to {sampleSize} of {assets.Count} indexed file(s) ordered by path (stride {stride}), always including the {previousFailureCount} file(s) that failed the previous verification.";
			LogVerificationOutcome("sample", strategy, checkedPaths.Count, failures);

			return new AssetIndexVerificationResult(checkedPaths.Count, assets.Count, strategy, checkedPaths, failures);
		}
	}

	internal static AssetIndexVerificationResult VerifyAll(
		ILiteCollection<IndexedAsset> collection, object syncLock, ref HashSet<string> lastFailurePaths, bool verifyContentHash, IProgress<AssetIndexProgressUpdate>? progress, double progressStart, double progressEnd)
	{
		lock (syncLock)
		{
			var assets = collection.FindAll()
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

			lastFailurePaths = failures.Select(f => f.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
			string strategy = verifyContentHash
				? "Full verification of every indexed file, including its content hash."
				: "Full verification of every indexed file.";
			LogVerificationOutcome("full", strategy, checkedPaths.Count, failures);

			return new AssetIndexVerificationResult(checkedPaths.Count, assets.Count, strategy, checkedPaths, failures);
		}
	}

	internal static int RepairAssets(
		ILiteCollection<IndexedAsset> collection, LiteDatabase database, object syncLock, IReadOnlyList<AssetIndexVerificationFailure> failures, IProgress<AssetIndexProgressUpdate>? progress, double progressStart, double progressEnd)
	{
		if (failures == null || failures.Count == 0) return 0;

		int repairedCount = 0;
		lock (syncLock)
		{
			for (int i = 0; i < failures.Count; i++)
			{
				double fraction = (double)(i + 1) / failures.Count;
				progress?.Report(new AssetIndexProgressUpdate(progressStart + (progressEnd - progressStart) * fraction, string.Format(TranslationServer.Translate("Repairing indexed file {0}/{1}..."), i + 1, failures.Count)));
				if (RepairIndexedAsset(collection, failures[i].FilePath))
				{
					repairedCount++;
				}
			}

			if (repairedCount > 0)
			{
				database.Checkpoint();
			}
		}

		return repairedCount;
	}

	private static List<IndexedAsset> SelectDeterministicSample(List<IndexedAsset> assets, int sampleSize, HashSet<string> lastFailurePaths)
	{
		var sampledAssets = new List<IndexedAsset>();
		var sampledPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var asset in assets)
		{
			if (sampledAssets.Count >= sampleSize) break;
			if (lastFailurePaths.Contains(asset.FilePath) && sampledPaths.Add(asset.FilePath))
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

	private static bool RepairIndexedAsset(ILiteCollection<IndexedAsset> collection, string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath)) return false;

		string normalizedFilePath = AssetIndexPaths.Normalize(filePath);
		var asset = collection.FindOne(x => x.FilePath == normalizedFilePath);
		if (asset == null) return false;

		if (!RestoreCasAssetFileIfNeeded(asset))
		{
			collection.Delete(asset.Id);
			return true;
		}

		ReindexAssetFromDisk(asset, normalizedFilePath);
		collection.Update(asset);
		return true;
	}

	private static bool RestoreCasAssetFileIfNeeded(IndexedAsset asset)
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

	internal static bool ContentMatchesHash(string filePath, string hash)
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

	private static void ReindexAssetFromDisk(IndexedAsset asset, string normalizedFilePath)
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

	internal static int CountRemaining(
		IReadOnlyList<AssetIndexVerificationFailure> failures, Func<string, IndexedAsset?> locateAsset)
	{
		int remainingCount = 0;
		foreach (var failure in failures)
		{
			var asset = locateAsset(failure.FilePath);
			if (asset == null) continue;
			if (AssetIndexInspector.Inspect(asset, verifyContentHash: true) != null) remainingCount++;
		}

		return remainingCount;
	}
}
