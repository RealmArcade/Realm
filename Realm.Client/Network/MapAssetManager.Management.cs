using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Distribution;
using Environment = System.Environment;

namespace Realm.Client.Network;

public static partial class MapAssetManager
{
	public static MapManifest? FindHostManifest(string mapName, string? version = null)
	{
		string? manifestPath = FindManifestPath(mapName, version);
		if (!string.IsNullOrEmpty(manifestPath) && File.Exists(manifestPath))
		{
			var manifest = MapManifest.LoadFromFile(manifestPath);
			if (manifest != null) return manifest;
		}

		return IngestHostMap(mapName);
	}

	public static bool IsMapDownloaded(string mapName, string? version = null, string? manifestHash = null)
	{
		if (string.IsNullOrWhiteSpace(mapName))
			return false;

		string? manifestPath = FindManifestPath(mapName, version, manifestHash);
		if (manifestPath == null || !File.Exists(manifestPath))
			return false;

		return VerifyMapManifest(manifestPath);
	}

	private static bool VerifyMapManifest(string manifestPath)
	{
		try
		{
			var manifest = ParseManifest(manifestPath);
			if (manifest == null) return false;

			if (manifest.Files == null || manifest.Files.Count == 0)
				return true;

			var missingHashes = GetMissingHashes(manifest.Files.Values);
			if (missingHashes.Count > 0)
				return false;

			string mapDir = Path.GetDirectoryName(manifestPath) ?? string.Empty;
			if (HasMissingDiskFile(manifest, mapDir))
			{
				bool isP2P = manifestPath.Contains("p2p_cache", StringComparison.OrdinalIgnoreCase);
				ExtractManifestFiles(manifest, mapDir, isP2P: isP2P);
			}

			return true;
		}
		catch
		{
			return false;
		}
	}

	private static MapManifest? ParseManifest(string manifestPath)
	{
		string manifestJson = File.ReadAllText(manifestPath);
		return MapManifest.LoadFromJson(manifestJson) ?? JsonSerializer.Deserialize<MapManifest>(manifestJson);
	}

	private static bool HasMissingDiskFile(MapManifest manifest, string mapDir)
	{
		foreach (var kvp in manifest.Files)
		{
			string rel = kvp.Key.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ? kvp.Key.Substring(6) : kvp.Key;
			rel = rel.TrimStart('/', '\\');
			if (!File.Exists(Path.Combine(mapDir, rel)))
				return true;
		}

		return false;
	}

	public static void PruneGlobalArchive()
	{
		string archiveDir = GlobalArchiveDirectory;
		Task.Run(() => PruneGlobalArchiveInternal(archiveDir));
	}

	public static void PruneGlobalArchiveSync()
	{
		string archiveDir = GlobalArchiveDirectory;
		PruneGlobalArchiveInternal(archiveDir);
	}

	private static void PruneGlobalArchiveInternal(string archiveDir)
	{
		lock (ArchiveLock)
		{
			try
			{
				Log("[MapAssetManager] Starting background pruning process...");

				if (!Directory.Exists(archiveDir)) return;

				CleanLegacyArchiveFiles(archiveDir);

				var referencedHashes = GetReferencedManifestHashes(archiveDir);
				Log($"[MapAssetManager] Total referenced BLAKE3 hashes found in manifests: {referencedHashes.Count}");

				PruneUnreferencedCasFiles(referencedHashes);

				Log("[MapAssetManager] Pruning complete.");
			}
			catch (Exception ex)
			{
				LogErr($"[MapAssetManager] Pruning failed: {ex.Message}");
			}
		}
	}

	private static void CleanLegacyArchiveFiles(string archiveDir)
	{
		string legacyGlobal7z = Path.Combine(archiveDir, "global_assets.7z");
		if (File.Exists(legacyGlobal7z))
		{
			try
			{
				File.Delete(legacyGlobal7z);
			}
			catch
			{
			}
		}

		string legacyPckCache = Path.Combine(archiveDir, "pck_cache.json");
		if (File.Exists(legacyPckCache))
		{
			try
			{
				File.Delete(legacyPckCache);
			}
			catch
			{
			}
		}

		try
		{
			var legacyPcks = Directory.GetFiles(archiveDir, "*.pck", SearchOption.AllDirectories);
			foreach (var pck in legacyPcks)
			{
				try
				{
					File.Delete(pck);
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
	}

	private static HashSet<string> GetReferencedManifestHashes(string archiveDir)
	{
		var manifestFiles = Directory.GetFiles(archiveDir, "manifest.json", SearchOption.AllDirectories);
		var referencedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var file in manifestFiles)
		{
			try
			{
				string content = File.ReadAllText(file);
				var manifest = MapManifest.LoadFromJson(content);
				if (manifest == null || manifest.Files == null) continue;

				foreach (var hash in manifest.Files.Values)
				{
					referencedHashes.Add(hash);
					referencedHashes.Add(ContentAddressableStorage.NormalizeBlake3Hash(hash));
				}
			}
			catch (Exception ex)
			{
				LogErr($"[MapAssetManager] Error reading manifest {file}: {ex.Message}");
			}
		}

		return referencedHashes;
	}

	private static void PruneUnreferencedCasFiles(HashSet<string> referencedHashes)
	{
		if (!Directory.Exists(Storage.AssetsDirectory)) return;

		var casFiles = Directory.GetFiles(Storage.AssetsDirectory, "*.*", SearchOption.AllDirectories);
		foreach (var casFile in casFiles)
		{
			string fileHash = ContentAddressableStorage.NormalizeBlake3Hash(Path.GetFileName(casFile));
			if (referencedHashes.Contains(fileHash)) continue;

			try
			{
				File.Delete(casFile);
				string sidecarShard = Path.Combine(Storage.SidecarCacheDirectory, fileHash.Substring(0, 2));
				string sidecarFile = Path.Combine(sidecarShard, $"{fileHash}.json");
				if (File.Exists(sidecarFile))
				{
					File.Delete(sidecarFile);
				}

				Log($"[MapAssetManager] Pruned CAS asset: {casFile}");
			}
			catch
			{
			}
		}
	}

}
