using System;
using System.IO;
using System.Text.Json.Nodes;
using Godot;
using Realm.Client.Network;
using Realm.Client.Utils;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

/// <summary>
///     Making a map archive and the content-addressed store agree, without the index database: every
///     manifest entry whose file is missing from the store is restored from the map folder (or from a hard
///     link of a file that already matches), every file the store has and the map does not is linked back
///     into the map, and every restored file gets a sidecar cache next to it that carries the asset type
///     from its metadata headers.
/// </summary>
public static class AssetIndexCasSynchronization
{
	public static void Synchronize(IProgress<AssetIndexProgressUpdate>? progress = null)
	{
		SynchronizeArchiveDirectory(MapAssetManager.GlobalArchiveDirectory, MapAssetManager.Storage, isP2P: false, progress, 0.02, 0.08);
		if (Directory.Exists(MapAssetManager.P2PArchiveDirectory))
		{
			SynchronizeArchiveDirectory(MapAssetManager.P2PArchiveDirectory, MapAssetManager.P2PStorage, isP2P: true, progress, 0.08, 0.12);
		}
	}

	private static void SynchronizeArchiveDirectory(string archiveDirectory, ContentAddressableStorage cas, bool isP2P, IProgress<AssetIndexProgressUpdate>? progress, double startPercentage, double endPercentage)
	{
		if (string.IsNullOrEmpty(archiveDirectory) || !Directory.Exists(archiveDirectory) || cas == null) return;

		var manifestFiles = Directory.GetFiles(archiveDirectory, "manifest.json", SearchOption.AllDirectories);
		for (int i = 0; i < manifestFiles.Length; i++)
		{
			ProcessSynchronizeManifest(manifestFiles[i], cas, progress, startPercentage, endPercentage, i, manifestFiles.Length);
		}
	}

	private static void ProcessSynchronizeManifest(string manifestFile, ContentAddressableStorage cas, IProgress<AssetIndexProgressUpdate>? progress, double startPercentage, double endPercentage, int index, int totalFiles)
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

	private static void ProcessSynchronizeManifestEntry(string virtualPath, string hash, string mapDir, ContentAddressableStorage cas)
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

	private static string? EnsureCasFileExists(string? casFilePath, string blake3, string virtualPath, string mapDir, string mapFilePath, ContentAddressableStorage cas)
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

		if (!AssetIndexVerification.ContentMatchesHash(sourceFile, blake3))
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

	private static string? FindSourceFileForCas(string mapDir, string virtualPath, string mapFilePath)
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

	private static void EnsureSidecarCacheExists(string blake3, string mapDir, string? casFilePath, string mapFilePath, ContentAddressableStorage cas)
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

	public static void InvalidatePathCaches()
	{
		MapAssetManager.Storage.InvalidateAllAssetPaths();
		if (Directory.Exists(MapAssetManager.P2PArchiveDirectory))
		{
			MapAssetManager.P2PStorage.InvalidateAllAssetPaths();
		}
	}
}
