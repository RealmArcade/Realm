using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

/// <summary>retrying one asset across the reachable servers.</summary>
public partial class DistributionClient
	{
	private async Task<bool> DownloadSingleAssetWithRetriesAsync(
		string normalizedHash,
		string assetKey,
		ContentAddressableStorage targetStorage,
		List<SeederNodeDto> seeders,
		string? fallbackHostUrl,
		CancellationToken cancellationToken)
	{
		var prioritizedUrls = GetPrioritizedServerUrls(normalizedHash, seeders, fallbackHostUrl);

		const int maxCycles = 3;
		for (int cycle = 0; cycle < maxCycles; cycle++)
		{
			if (cancellationToken.IsCancellationRequested) return false;

			if (cycle > 0)
			{
				await Task.Delay(150 * (int)Math.Pow(2, cycle - 1) + Random.Shared.Next(15, 75), cancellationToken);
			}

			var availableUrls = GetAvailableUrls(prioritizedUrls);

			if (await ProcessAvailableUrlsAsync(availableUrls, normalizedHash, assetKey, targetStorage, cancellationToken))
			{
				return true;
			}
		}

		return false;
	}

	private async Task<bool> ProcessAvailableUrlsAsync(
		List<string> availableUrls,
		string normalizedHash,
		string assetKey,
		ContentAddressableStorage targetStorage,
		CancellationToken cancellationToken)
	{
		foreach (string baseUrl in availableUrls)
		{
			if (cancellationToken.IsCancellationRequested) return false;

			if (await TryDownloadAssetFromUrlAsync(baseUrl, normalizedHash, assetKey, targetStorage, cancellationToken))
			{
				return true;
			}
		}

		return false;
	}

	private List<string> GetAvailableUrls(List<string> prioritizedUrls)
	{
		DateTime nowUtc = DateTime.UtcNow;
		var availableUrls = prioritizedUrls.Where(url => !GetCircuitState(url).IsOpen(nowUtc)).ToList();
		return availableUrls.Count == 0 ? prioritizedUrls : availableUrls;
	}

	private async Task<bool> TryDownloadAssetFromUrlAsync(
		string baseUrl,
		string normalizedHash,
		string assetKey,
		ContentAddressableStorage targetStorage,
		CancellationToken cancellationToken)
	{
		var circuit = GetCircuitState(baseUrl);
		try
		{
			string assetUrl = $"{baseUrl}/api/assets/{normalizedHash}";
			var response = await _httpClient.GetAsync(assetUrl, cancellationToken);

			if (response.IsSuccessStatusCode)
			{
				return await ProcessSuccessfulDownloadAsync(response, baseUrl, normalizedHash, assetKey, targetStorage, circuit, cancellationToken);
			}

			if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout)
			{
				circuit.RecordFailure(DateTime.UtcNow);
			}

			return false;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return false;
		}
		catch
		{
			circuit.RecordFailure(DateTime.UtcNow);
			return false;
		}
	}

	private async Task<bool> ProcessSuccessfulDownloadAsync(
		HttpResponseMessage response,
		string baseUrl,
		string normalizedHash,
		string assetKey,
		ContentAddressableStorage targetStorage,
		SeederCircuitState circuit,
		CancellationToken cancellationToken)
	{
		byte[] downloadedBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
		if (_throttle != null)
		{
			await _throttle.ConsumeAsync(downloadedBytes.Length, cancellationToken);
		}

		string extension = Path.GetExtension(assetKey).ToLowerInvariant();
		string computedBlake3 = RealmMetadataHelper.ComputeBlake3(downloadedBytes, extension);
		string computedNormalized = ContentAddressableStorage.NormalizeBlake3Hash(computedBlake3);

		if (!string.Equals(computedNormalized, normalizedHash, StringComparison.OrdinalIgnoreCase))
		{
			circuit.RecordFailure(DateTime.UtcNow);
			return false;
		}

		string? metadataHeader = ExtractMetadataHeader(response);

		var storeResult = targetStorage.StoreAsset(downloadedBytes, extension, metadataHeader, precomputedBlake3: computedBlake3);
		if (storeResult.Success)
		{
			circuit.RecordSuccess();
			return true;
		}

		return false;
	}

	private string? ExtractMetadataHeader(HttpResponseMessage response)
	{
		if (!response.Headers.TryGetValues("X-Asset-Metadata", out var metaValues))
		{
			return null;
		}

		string? rawHeader = metaValues.FirstOrDefault();
		if (string.IsNullOrWhiteSpace(rawHeader))
		{
			return null;
		}

		try
		{
			byte[] metaBytes = Convert.FromBase64String(rawHeader);
			return Encoding.UTF8.GetString(metaBytes);
		}
		catch
		{
			return rawHeader;
		}
	}
}
