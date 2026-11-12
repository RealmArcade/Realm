using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

/// <summary>validation, progress and the retried upload of one asset.</summary>
public partial class DistributionClient
	{
	private bool ValidateAssetBytes(
		byte[] fileBytes,
		string relPath,
		string missingHash,
		string fullFilePath,
		object errorLock,
		string?[] errorWrapper)
	{
		if (fileBytes.Length == 0)
		{
			SetUploadError(errorLock, errorWrapper, relPath, $"File '{relPath}' is empty (0 bytes) and cannot be published as an asset.");
			return false;
		}

		if (fileBytes.Length > ContentAddressableStorage.MaximumAssetSizeBytes)
		{
			double sizeMb = (double)fileBytes.Length / (1024 * 1024);
			double maxMb = (double)ContentAddressableStorage.MaximumAssetSizeBytes / (1024 * 1024);
			SetUploadError(errorLock, errorWrapper, relPath, $"Asset '{relPath}' ({sizeMb:F2} MB) exceeds maximum allowed size of {maxMb:F0} MB per asset.");
			return false;
		}

		string ext = Path.GetExtension(fullFilePath);
		string computedBlake3 = RealmMetadataHelper.ComputeBlake3(fileBytes, ext);
		string computedNorm = ContentAddressableStorage.NormalizeBlake3Hash(computedBlake3);
		string expectedNorm = ContentAddressableStorage.NormalizeBlake3Hash(missingHash);

		if (!string.Equals(computedNorm, expectedNorm, StringComparison.OrdinalIgnoreCase))
		{
			SetUploadError(errorLock, errorWrapper, relPath, $"Asset file '{relPath}' hash mismatch: expected {expectedNorm}, but file on disk has hash {computedNorm}.");
			return false;
		}

		return true;
	}

	private void SetUploadError(object errorLock, string?[] errorWrapper, string asset, string message)
	{
		lock (errorLock)
		{
			errorWrapper[FailedAssetIndex] ??= asset;
			errorWrapper[FailureMessageIndex] ??= message;
		}
	}

	private void NotifyAssetUploaded(string fullFilePath, Action<int, int, string>? progressCallback, int totalMissing, int[] completedCountWrapper)
	{
		int done = Interlocked.Increment(ref completedCountWrapper[0]);
		progressCallback?.Invoke(done, totalMissing, Path.GetFileName(fullFilePath));
	}

	private async Task PerformAssetUploadWithRetriesAsync(
		string missingHash,
		byte[] fileBytes,
		string relPath,
		string fullFilePath,
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
		CancellationToken cancellationToken)
	{
		byte[] hashBytes = Encoding.UTF8.GetBytes(missingHash);
		byte[] signatureBytes = SignatureAlgorithm.Ed25519.Sign(authorshipKey, hashBytes);
		string signatureStr = Convert.ToBase64String(signatureBytes);

		bool success = false;
		string? lastError = null;

		for (int attempt = 0; attempt < 3 && !success && !cancellationToken.IsCancellationRequested; attempt++)
		{
			if (attempt > 0)
			{
				await Task.Delay(200 * attempt, cancellationToken);
			}

			var (attemptSuccess, attemptError) = await AttemptSingleAssetUploadAsync(
				missingHash, signatureStr, currentUsername, authorPublicKey, mapTitle, mapVersion, sessionId, fileBytes, fullFilePath, cancellationToken);

			success = attemptSuccess;
			lastError = attemptError;
		}

		if (!success)
		{
			SetUploadError(errorLock, errorWrapper, relPath, lastError ?? "Upload failed after retries.");
		}
		else
		{
			NotifyAssetUploaded(fullFilePath, progressCallback, totalMissing, completedCountWrapper);
		}
	}

	private async Task<(bool Success, string? Error)> AttemptSingleAssetUploadAsync(
		string missingHash,
		string signatureStr,
		string currentUsername,
		string authorPublicKey,
		string mapTitle,
		string mapVersion,
		string? sessionId,
		byte[] fileBytes,
		string fullFilePath,
		CancellationToken cancellationToken)
	{
		string url = $"{_registryServerUrl}/api/publish_map/upload_asset";
		using var form = new MultipartFormDataContent();
		form.Add(new StringContent(missingHash), "Hash");
		form.Add(new StringContent(signatureStr), "Signature");
		form.Add(new StringContent(currentUsername), "AuthorUsername");
		form.Add(new StringContent(authorPublicKey), "PublicKey");
		form.Add(new StringContent(mapTitle), "MapTitle");
		form.Add(new StringContent(mapVersion), "MapVersion");
		if (!string.IsNullOrEmpty(sessionId))
		{
			form.Add(new StringContent(sessionId), "SessionId");
		}

		var fileContent = new ByteArrayContent(fileBytes);
		form.Add(fileContent, "File", Path.GetFileName(fullFilePath));

		if (_throttle != null)
		{
			await _throttle.ConsumeAsync(fileBytes.Length, cancellationToken);
		}

		try
		{
			var response = await _httpClient.PostAsync(url, form, cancellationToken);
			if (response.IsSuccessStatusCode)
			{
				return (true, null);
			}
			else
			{
				string err = await response.Content.ReadAsStringAsync(cancellationToken);
				return (false, $"HTTP {(int)response.StatusCode}: {err}");
			}
		}
		catch (Exception ex)
		{
			return (false, ex.Message);
		}
	}
}
