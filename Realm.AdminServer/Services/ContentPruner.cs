using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;

namespace Realm.AdminServer.Services;

/// <summary>
///     Reaches into the content-addressed store and deletes what nothing references: published manifests, the signature
///     sidecars keyed by hash, and the sidecar cache. It only reads the database and the store, so it keeps no state of its
///     own besides the queue flag that coalesces concurrent prune requests.
/// </summary>
internal sealed class ContentPruner
{
	private readonly SemaphoreSlim _pruneLock = new(1, 1);
	private int _prunePending;

	public void QueueCasPrune(ContentAddressableStorage cas, DataStoreService db)
	{
		Interlocked.Exchange(ref _prunePending, 1);
		_ = Task.Run(async () =>
		{
			await _pruneLock.WaitAsync();
			try
			{
				if (Interlocked.Exchange(ref _prunePending, 0) == 1)
				{
					PruneCas(cas, db);
				}
			}
			finally
			{
				_pruneLock.Release();
			}
		});
	}

	public CasPruneResponseDto PruneCas(ContentAddressableStorage cas, DataStoreService db)
	{
		int totalScanned = 0;
		int orphansPruned = 0;
		int corruptPruned = 0;
		long bytesFreed = 0;

		var referencedHashes = GetReferencedHashes(cas, db);
		var signaturesToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		PruneDirectories(cas, referencedHashes, signaturesToDelete, ref totalScanned, ref orphansPruned, ref corruptPruned, ref bytesFreed);
		PruneSignatures(db, cas, referencedHashes, signaturesToDelete);
		PruneSidecarCache(cas, referencedHashes);

		CleanEmptySubdirectories(cas.AssetsDirectory);
		CleanEmptySubdirectories(cas.SidecarCacheDirectory);

		return new CasPruneResponseDto
		{
			Success = true,
			TotalScanned = totalScanned,
			OrphansPruned = orphansPruned,
			CorruptPruned = corruptPruned,
			BytesFreed = bytesFreed,
			Message = $"Prune completed: {totalScanned} scanned, {orphansPruned} orphans deleted, {corruptPruned} corrupt files deleted, {bytesFreed} bytes freed."
		};
	}

	private HashSet<string> GetReferencedHashes(ContentAddressableStorage cas, DataStoreService db)
	{
		var referencedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var doc in db.GetAll<JsonDocument>("published_maps"))
		{
			try
			{
				var mf = MapManifest.LoadFromJson(doc.RootElement.GetRawText());
				AddManifestHashes(mf, referencedHashes);
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"[ClusterEvents] could not read the manifest hashes of a published map, so its assets may be treated as unreferenced: {ex.GetType().Name}: {ex.Message}");
			}
		}

		var manifestDir = Path.Combine(cas.RootDirectory, "manifests");
		if (Directory.Exists(manifestDir))
		{
			foreach (var file in Directory.EnumerateFiles(manifestDir, "*.json"))
			{
				try
				{
					string json = File.ReadAllText(file);
					var mf = MapManifest.LoadFromJson(json);
					AddManifestHashes(mf, referencedHashes);
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine($"[ClusterEvents] could not read a manifest while collecting referenced hashes, so its assets may be treated as unreferenced: {ex.GetType().Name}: {ex.Message}");
				}
			}
		}

		return referencedHashes;
	}

	private void AddManifestHashes(MapManifest? mf, HashSet<string> referencedHashes)
	{
		if (mf == null) return;
		foreach (var hashVal in mf.Files.Values)
		{
			string h = ContentAddressableStorage.NormalizeBlake3Hash(hashVal);
			if (!string.IsNullOrEmpty(h)) referencedHashes.Add(h);
		}
	}

	private void PruneDirectories(ContentAddressableStorage cas, HashSet<string> referencedHashes, HashSet<string> signaturesToDelete, ref int totalScanned, ref int orphansPruned, ref int corruptPruned, ref long bytesFreed)
	{
		var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var searchLocations = new List<(string DirectoryPath, SearchOption Option)>();

		if (Directory.Exists(cas.AssetsDirectory))
			searchLocations.Add((cas.AssetsDirectory, SearchOption.AllDirectories));
		if (Directory.Exists(cas.RootDirectory))
			searchLocations.Add((cas.RootDirectory, SearchOption.TopDirectoryOnly));

		foreach (var (directoryPath, option) in searchLocations)
		{
			if (!Directory.Exists(directoryPath)) continue;

			foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*", option))
			{
				string fullPath = Path.GetFullPath(filePath);
				if (!scannedPaths.Add(fullPath)) continue;

				string fileName = Path.GetFileName(filePath);
				if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;

				string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(fileName);
				if (string.IsNullOrEmpty(normalizedHash) || normalizedHash.Length < 32) continue;

				totalScanned++;
				PruneFile(cas, filePath, fileName, normalizedHash, referencedHashes, signaturesToDelete, ref orphansPruned, ref corruptPruned, ref bytesFreed);
			}
		}
	}

	private void PruneFile(ContentAddressableStorage cas, string filePath, string fileName, string normalizedHash, HashSet<string> referencedHashes, HashSet<string> signaturesToDelete, ref int orphansPruned, ref int corruptPruned, ref long bytesFreed)
	{
		try
		{
			var fileInfo = new FileInfo(filePath);
			long fileSize = fileInfo.Length;

			if (!referencedHashes.Contains(normalizedHash))
			{
				DeleteAssetFile(cas, filePath, normalizedHash, signaturesToDelete);
				orphansPruned++;
				bytesFreed += fileSize;
				return;
			}

			byte[] bytes = File.ReadAllBytes(filePath);
			string ext = Path.GetExtension(fileName).ToLowerInvariant();
			string computedHash = ContentAddressableStorage.NormalizeBlake3Hash(RealmMetadataHelper.ComputeBlake3(bytes, ext));

			if (!string.Equals(computedHash, normalizedHash, StringComparison.OrdinalIgnoreCase))
			{
				DeleteAssetFile(cas, filePath, normalizedHash, signaturesToDelete);
				corruptPruned++;
				bytesFreed += fileSize;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ClusterEvents] could not verify a content-addressed asset while pruning the store: {ex.GetType().Name}: {ex.Message}");
		}
	}

	private void DeleteAssetFile(ContentAddressableStorage cas, string filePath, string normalizedHash, HashSet<string> signaturesToDelete)
	{
		File.Delete(filePath);
		cas.RemoveSidecarCache(normalizedHash);
		signaturesToDelete.Add(normalizedHash);
	}

	private void PruneSignatures(DataStoreService db, ContentAddressableStorage cas, HashSet<string> referencedHashes, HashSet<string> signaturesToDelete)
	{
		var allSignatures = db.GetAllWithKeys<JsonDocument>("asset_signatures");
		foreach (var pair in allSignatures)
		{
			string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(pair.Key);
			if (!referencedHashes.Contains(normalizedHash) || !cas.HasAsset(normalizedHash))
			{
				signaturesToDelete.Add(pair.Key);
			}
		}

		if (signaturesToDelete.Count > 0)
		{
			db.DeleteMany("asset_signatures", signaturesToDelete);
		}
	}

	private void PruneSidecarCache(ContentAddressableStorage cas, HashSet<string> referencedHashes)
	{
		if (!Directory.Exists(cas.SidecarCacheDirectory)) return;

		foreach (var sidecarFile in Directory.EnumerateFiles(cas.SidecarCacheDirectory, "*.json", SearchOption.AllDirectories))
		{
			try
			{
				string sidecarHash = ContentAddressableStorage.NormalizeBlake3Hash(Path.GetFileName(sidecarFile));
				if (!referencedHashes.Contains(sidecarHash) || !cas.HasAsset(sidecarHash))
				{
					File.Delete(sidecarFile);
				}
			}
			catch
			{
			}
		}
	}

	private static void CleanEmptySubdirectories(string rootDirectory)
	{
		if (!Directory.Exists(rootDirectory)) return;

		try
		{
			foreach (var subDirectory in Directory.EnumerateDirectories(rootDirectory))
			{
				if (!Directory.EnumerateFileSystemEntries(subDirectory).Any())
				{
					try
					{
						Directory.Delete(subDirectory);
					}
					catch
					{
					}
				}
			}
		}
		catch
		{
		}
	}
}
