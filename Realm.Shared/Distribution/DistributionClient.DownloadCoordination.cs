using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

/// <summary>working out which assets are missing and downloading them.</summary>
public partial class DistributionClient
	{
	public async Task<bool> DownloadMissingAssetsMultiThreadedAsync(
		MapManifest manifest,
		ContentAddressableStorage targetStorage,
		List<SeederNodeDto>? availableSeeders = null,
		string? fallbackHostUrl = null,
		Action<float>? progressCallback = null,
		int maximumConcurrency = 4,
		CancellationToken cancellationToken = default,
		Action<string, string, string>? onAssetReady = null)
	{
		var (missingHashes, existingHashes) = CategorizeManifestAssets(manifest, targetStorage);

		NotifyExistingAssetsReady(existingHashes, onAssetReady);

		if (missingHashes.Count == 0)
		{
			progressCallback?.Invoke(1.0f);
			return true;
		}

		var seeders = availableSeeders ?? await GetActiveSeedersAsync(cancellationToken);
		int totalMissing = missingHashes.Count;
		int[] completedCountWrapper = new int[1];

		var (remainingMissingMap, hashToExtMap) = BuildMissingAssetMaps(missingHashes);

		string firstHash = missingHashes.Count > 0 ? missingHashes[0].NormalizedHash : "";
		var prioritizedUrls = GetPrioritizedServerUrls(firstHash, seeders, fallbackHostUrl);

		await DownloadBatchedAssetsFromSeedersAsync(
			prioritizedUrls,
			remainingMissingMap,
			hashToExtMap,
			targetStorage,
			totalMissing,
			progressCallback,
			onAssetReady,
			completedCountWrapper,
			cancellationToken);

		if (!remainingMissingMap.IsEmpty && !cancellationToken.IsCancellationRequested)
		{
			await DownloadRemainingAssetsConcurrentlyAsync(
				remainingMissingMap,
				hashToExtMap,
				targetStorage,
				seeders,
				fallbackHostUrl,
				maximumConcurrency,
				totalMissing,
				progressCallback,
				onAssetReady,
				completedCountWrapper,
				cancellationToken);
		}

		if (completedCountWrapper[0] >= totalMissing)
		{
			progressCallback?.Invoke(1.0f);
			return true;
		}

		return remainingMissingMap.IsEmpty;
	}

	private static (List<(string VirtualPath, string AssetKey, string NormalizedHash)> Missing, List<(string VirtualPath, string AssetKey, string NormalizedHash)> Existing) CategorizeManifestAssets(
		MapManifest manifest,
		ContentAddressableStorage targetStorage)
	{
		var missingHashes = new List<(string VirtualPath, string AssetKey, string NormalizedHash)>();
		var existingHashes = new List<(string VirtualPath, string AssetKey, string NormalizedHash)>();

		foreach (var filePair in manifest.Files)
		{
			string assetKey = filePair.Value;
			string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(assetKey);

			if (targetStorage.HasAsset(normalizedHash))
			{
				existingHashes.Add((filePair.Key, assetKey, normalizedHash));
			}
			else
			{
				missingHashes.Add((filePair.Key, assetKey, normalizedHash));
			}
		}

		return (missingHashes, existingHashes);
	}

	private static void NotifyExistingAssetsReady(
		List<(string VirtualPath, string AssetKey, string NormalizedHash)> existingHashes,
		Action<string, string, string>? onAssetReady)
	{
		if (existingHashes.Count == 0 || onAssetReady == null)
		{
			return;
		}

		Parallel.ForEach(existingHashes, item => { onAssetReady(item.VirtualPath, item.AssetKey, item.NormalizedHash); });
	}

	private static (ConcurrentDictionary<string, List<(string VirtualPath, string AssetKey, string NormalizedHash)>> RemainingMap, Dictionary<string, string> ExtMap) BuildMissingAssetMaps(
		List<(string VirtualPath, string AssetKey, string NormalizedHash)> missingHashes)
	{
		var remainingMissingMap = new ConcurrentDictionary<string, List<(string VirtualPath, string AssetKey, string NormalizedHash)>>(StringComparer.OrdinalIgnoreCase);
		var hashToExtMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		foreach (var item in missingHashes)
		{
			string ext = Path.GetExtension(item.AssetKey).ToLowerInvariant();
			if (string.IsNullOrEmpty(ext))
			{
				ext = Path.GetExtension(item.VirtualPath).ToLowerInvariant();
			}

			hashToExtMap[item.NormalizedHash] = ext;

			if (!remainingMissingMap.TryGetValue(item.NormalizedHash, out var list))
			{
				list = new List<(string VirtualPath, string AssetKey, string NormalizedHash)>();
				remainingMissingMap[item.NormalizedHash] = list;
			}

			list.Add(item);
		}

		return (remainingMissingMap, hashToExtMap);
	}
}
