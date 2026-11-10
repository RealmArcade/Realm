using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

/// <summary>uploading every asset the registry is missing.</summary>
public partial class DistributionClient
	{
	public async Task<(bool Success, string? FailedAsset, string? ErrorMessage)> UploadMissingAssetsMultiThreadedAsync(
		string workspace,
		IReadOnlyList<string> missingHashes,
		IReadOnlyDictionary<string, string> hashToRelativePath,
		string currentUsername,
		string authorPublicKey,
		Key authorshipKey,
		string mapTitle,
		string mapVersion,
		string? sessionId = null,
		Action<int, int, string>? progressCallback = null,
		int maximumConcurrency = 4,
		CancellationToken cancellationToken = default)
	{
		int totalMissing = missingHashes.Count;
		int[] completedCountWrapper = new int[1];
		string?[] errorWrapper = new string?[2];
		var errorLock = new object();

		using var semaphore = new SemaphoreSlim(Math.Max(1, maximumConcurrency));

		var tasks = missingHashes.Select(async missingHash =>
		{
			await ProcessMissingAssetUploadAsync(
				missingHash,
				workspace,
				hashToRelativePath,
				currentUsername,
				authorPublicKey,
				authorshipKey,
				mapTitle,
				mapVersion,
				sessionId,
				progressCallback,
				totalMissing,
				completedCountWrapper,
				errorLock,
				errorWrapper,
				semaphore,
				cancellationToken);
		});

		await Task.WhenAll(tasks);

		if (errorWrapper[FailedAssetIndex] != null)
		{
			return (false, errorWrapper[FailedAssetIndex], errorWrapper[FailureMessageIndex]);
		}

		return (true, null, null);
	}

	private async Task ProcessMissingAssetUploadAsync(
		string missingHash,
		string workspace,
		IReadOnlyDictionary<string, string> hashToRelativePath,
		string currentUsername,
		string authorPublicKey,
		Key authorshipKey,
		string mapTitle,
		string mapVersion,
		string? sessionId,
		Action<int, int, string>? progressCallback,
		int totalMissing,
		int[] completedCountWrapper,
		object errorLock,
		string?[] errorWrapper,
		SemaphoreSlim semaphore,
		CancellationToken cancellationToken)
	{
		if (errorWrapper[FailedAssetIndex] != null || cancellationToken.IsCancellationRequested) return;

		if (!hashToRelativePath.TryGetValue(missingHash, out var relPath) &&
		    !hashToRelativePath.TryGetValue(ContentAddressableStorage.NormalizeBlake3Hash(missingHash), out relPath))
		{
			SetUploadError(errorLock, errorWrapper, missingHash, $"Missing hash '{missingHash}' is not present in manifest file mapping.");
			return;
		}

		string fullFilePath = Path.Combine(workspace, relPath);
		if (!File.Exists(fullFilePath))
		{
			SetUploadError(errorLock, errorWrapper, relPath, $"File '{relPath}' was not found on disk at '{fullFilePath}'.");
			return;
		}

		await semaphore.WaitAsync(cancellationToken);
		try
		{
			if (errorWrapper[FailedAssetIndex] != null || cancellationToken.IsCancellationRequested) return;

			byte[] fileBytes = await File.ReadAllBytesAsync(fullFilePath, cancellationToken);
			if (!ValidateAssetBytes(fileBytes, relPath, missingHash, fullFilePath, errorLock, errorWrapper))
			{
				return;
			}

			if (await CheckAssetExistsAsync(missingHash, cancellationToken))
			{
				NotifyAssetUploaded(fullFilePath, progressCallback, totalMissing, completedCountWrapper);
				return;
			}

			await PerformAssetUploadWithRetriesAsync(
				missingHash,
				fileBytes,
				relPath,
				fullFilePath,
				currentUsername,
				authorPublicKey,
				authorshipKey,
				mapTitle,
				mapVersion,
				sessionId,
				progressCallback,
				totalMissing,
				completedCountWrapper,
				errorLock,
				errorWrapper,
				cancellationToken);
		}
		finally
		{
			semaphore.Release();
		}
	}
}
