using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

/// <summary>downloading an asset bundle as a stream and extracting it.</summary>
public partial class DistributionClient
	{
	public async Task<(bool Success, List<string> DownloadedHashes)> DownloadAssetBundleStreamAsync(
		string targetServerBaseUrl,
		List<string> requestedHashes,
		IReadOnlyDictionary<string, string> hashToExtensionMap,
		ContentAddressableStorage targetStorage,
		CancellationToken cancellationToken = default,
		Action<string>? onAssetDownloaded = null)
	{
		var downloadedHashes = new List<string>();
		if (requestedHashes.Count == 0)
		{
			return (true, downloadedHashes);
		}

		string url = $"{targetServerBaseUrl.TrimEnd('/')}/api/assets/bundle";
		var payload = new AssetBundleRequestDto { Hashes = requestedHashes };
		string jsonPayload = JsonSerializer.Serialize(payload);

		using var request = new HttpRequestMessage(HttpMethod.Post, url)
		{
			Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
		};

		try
		{
			using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				return (false, downloadedHashes);
			}

			using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);

			await ZstdAssetBundleHelper.ExtractBundleFromStreamAsync(
				responseStream,
				async (assetKey, metadata, data) => await ProcessExtractedBundleAssetAsync(assetKey, metadata, data, hashToExtensionMap, targetStorage, downloadedHashes, onAssetDownloaded, cancellationToken),
				cancellationToken);

			return (true, downloadedHashes);
		}
		catch (Exception)
		{
			return (false, downloadedHashes);
		}
	}

	private async Task ProcessExtractedBundleAssetAsync(
		string assetKey,
		string? metadata,
		byte[] data,
		IReadOnlyDictionary<string, string> hashToExtensionMap,
		ContentAddressableStorage targetStorage,
		List<string> downloadedHashes,
		Action<string>? onAssetDownloaded,
		CancellationToken cancellationToken)
	{
		string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(assetKey);
		if (!hashToExtensionMap.TryGetValue(normalizedHash, out string? extension) || string.IsNullOrEmpty(extension))
		{
			extension = ".bin";
		}

		string computedBlake3 = RealmMetadataHelper.ComputeBlake3(data, extension);
		string computedNormalized = ContentAddressableStorage.NormalizeBlake3Hash(computedBlake3);

		if (!string.Equals(computedNormalized, normalizedHash, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		if (_throttle != null)
		{
			await _throttle.ConsumeAsync(data.Length, cancellationToken);
		}

		var storeResult = targetStorage.StoreAsset(data, extension, metadata, precomputedBlake3: computedBlake3);
		if (!storeResult.Success)
		{
			return;
		}

		lock (downloadedHashes)
		{
			downloadedHashes.Add(normalizedHash);
		}

		onAssetDownloaded?.Invoke(normalizedHash);
	}
}
