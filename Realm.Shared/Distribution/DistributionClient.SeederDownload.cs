using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

/// <summary>downloading batches of assets from the seeder nodes.</summary>
public partial class DistributionClient
	{
	private async Task DownloadBatchedAssetsFromSeedersAsync(
		List<string> prioritizedUrls,
		ConcurrentDictionary<string, List<(string VirtualPath, string AssetKey, string NormalizedHash)>> remainingMissingMap,
		Dictionary<string, string> hashToExtMap,
		ContentAddressableStorage targetStorage,
		int totalMissing,
		Action<float>? progressCallback,
		Action<string, string, string>? onAssetReady,
		int[] completedCountWrapper,
		CancellationToken cancellationToken)
	{
		foreach (string baseUrl in prioritizedUrls)
		{
			if (remainingMissingMap.IsEmpty || cancellationToken.IsCancellationRequested)
			{
				break;
			}

			var circuit = GetCircuitState(baseUrl);
			if (circuit.IsOpen(DateTime.UtcNow))
			{
				continue;
			}

			await ProcessUrlBatchesAsync(
				baseUrl,
				remainingMissingMap,
				hashToExtMap,
				targetStorage,
				totalMissing,
				progressCallback,
				onAssetReady,
				completedCountWrapper,
				cancellationToken,
				circuit);
		}
	}

	private async Task ProcessUrlBatchesAsync(
		string baseUrl,
		ConcurrentDictionary<string, List<(string VirtualPath, string AssetKey, string NormalizedHash)>> remainingMissingMap,
		Dictionary<string, string> hashToExtMap,
		ContentAddressableStorage targetStorage,
		int totalMissing,
		Action<float>? progressCallback,
		Action<string, string, string>? onAssetReady,
		int[] completedCountWrapper,
		CancellationToken cancellationToken,
		SeederCircuitState circuit)
	{
		var currentMissingHashes = remainingMissingMap.Keys.ToList();
		if (currentMissingHashes.Count == 0) return;

		const int maxBatchCount = 100;
		for (int i = 0; i < currentMissingHashes.Count; i += maxBatchCount)
		{
			if (cancellationToken.IsCancellationRequested) break;

			var batch = currentMissingHashes.Skip(i).Take(maxBatchCount).ToList();
			var (bundleSuccess, downloadedHashes) = await DownloadAssetBundleStreamAsync(
				baseUrl,
				batch,
				hashToExtMap,
				targetStorage,
				cancellationToken,
				onAssetDownloaded: normHash => HandleAssetDownloaded(normHash, remainingMissingMap, onAssetReady, totalMissing, progressCallback, completedCountWrapper));

			if (bundleSuccess)
			{
				circuit.RecordSuccess();
			}
			else
			{
				break;
			}
		}
	}


	private void HandleAssetDownloaded(
		string normHash,
		ConcurrentDictionary<string, List<(string VirtualPath, string AssetKey, string NormalizedHash)>> remainingMissingMap,
		Action<string, string, string>? onAssetReady,
		int totalMissing,
		Action<float>? progressCallback,
		int[] completedCountWrapper)
	{
		if (remainingMissingMap.TryRemove(normHash, out var readyItems))
		{
			foreach (var item in readyItems)
			{
				onAssetReady?.Invoke(item.VirtualPath, item.AssetKey, item.NormalizedHash);
			}

			int currentCompleted = Interlocked.Add(ref completedCountWrapper[0], readyItems.Count);
			float progress = (float)currentCompleted / totalMissing;
			progressCallback?.Invoke(progress);
		}
	}

	private async Task DownloadRemainingAssetsConcurrentlyAsync(
		ConcurrentDictionary<string, List<(string VirtualPath, string AssetKey, string NormalizedHash)>> remainingMissingMap,
		Dictionary<string, string> hashToExtMap,
		ContentAddressableStorage targetStorage,
		List<SeederNodeDto> seeders,
		string? fallbackHostUrl,
		int maximumConcurrency,
		int totalMissing,
		Action<float>? progressCallback,
		Action<string, string, string>? onAssetReady,
		int[] completedCountWrapper,
		CancellationToken cancellationToken)
	{
		var remainingMissingItems = remainingMissingMap.Values.SelectMany(v => v).Distinct().ToList();

		using var semaphore = new SemaphoreSlim(maximumConcurrency);
		var downloadTasks = remainingMissingItems.Select(async item =>
		{
			await semaphore.WaitAsync(cancellationToken);
			try
			{
				return await DownloadSingleRemainingAssetAsync(
					item,
					remainingMissingMap,
					hashToExtMap,
					targetStorage,
					seeders,
					fallbackHostUrl,
					totalMissing,
					progressCallback,
					onAssetReady,
					completedCountWrapper,
					cancellationToken);
			}
			finally
			{
				semaphore.Release();
			}
		}).ToList();

		await Task.WhenAll(downloadTasks);
	}

	private async Task<bool> DownloadSingleRemainingAssetAsync(
		(string VirtualPath, string AssetKey, string NormalizedHash) item,
		ConcurrentDictionary<string, List<(string VirtualPath, string AssetKey, string NormalizedHash)>> remainingMissingMap,
		Dictionary<string, string> hashToExtMap,
		ContentAddressableStorage targetStorage,
		List<SeederNodeDto> seeders,
		string? fallbackHostUrl,
		int totalMissing,
		Action<float>? progressCallback,
		Action<string, string, string>? onAssetReady,
		int[] completedCountWrapper,
		CancellationToken cancellationToken)
	{
		if (targetStorage.HasAsset(item.NormalizedHash))
		{
			HandleAssetDownloaded(item.NormalizedHash, remainingMissingMap, onAssetReady, totalMissing, progressCallback, completedCountWrapper);
			return true;
		}

		string effectiveAssetKey = item.AssetKey;
		if (string.IsNullOrEmpty(Path.GetExtension(effectiveAssetKey)) && hashToExtMap.TryGetValue(item.NormalizedHash, out var mappedExt) && !string.IsNullOrEmpty(mappedExt))
		{
			effectiveAssetKey = $"{item.AssetKey}{mappedExt}";
		}

		bool downloaded = await DownloadSingleAssetWithRetriesAsync(
			item.NormalizedHash,
			effectiveAssetKey,
			targetStorage,
			seeders,
			fallbackHostUrl,
			cancellationToken);

		if (downloaded)
		{
			HandleAssetDownloaded(item.NormalizedHash, remainingMissingMap, onAssetReady, totalMissing, progressCallback, completedCountWrapper);
		}

		return downloaded;
	}
}
