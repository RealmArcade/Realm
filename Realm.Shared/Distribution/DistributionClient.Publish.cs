using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

/// <summary>publishing a manifest and uploading a single asset.</summary>
public partial class DistributionClient
	{
	public async Task<MapPublishResponseDto> PublishManifestAsync(
		MapManifest manifest,
		string? adminBypassToken = null,
		CancellationToken cancellationToken = default)
	{
		string url = $"{_registryServerUrl}/api/manifests";
		var request = new HttpRequestMessage(HttpMethod.Post, url)
		{
			Content = new StringContent(manifest.ToJson(), Encoding.UTF8, "application/json")
		};

		if (!string.IsNullOrWhiteSpace(adminBypassToken))
		{
			request.Headers.Add("X-Admin-Bypass", adminBypassToken);
		}

		try
		{
			var response = await _httpClient.SendAsync(request, cancellationToken);
			string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

			if (!response.IsSuccessStatusCode)
			{
				return new MapPublishResponseDto
				{
					Success = false,
					Status = "Failed",
					Message = $"Publish failed with HTTP {(int)response.StatusCode}: {responseJson}"
				};
			}

			var publishResponse = JsonSerializer.Deserialize<MapPublishResponseDto>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			return publishResponse ?? new MapPublishResponseDto { Success = true, Status = "Published" };
		}
		catch (Exception exception)
		{
			return new MapPublishResponseDto
			{
				Success = false,
				Status = "Error",
				Message = exception.Message
			};
		}
	}

	public async Task<AssetUploadResponseDto> UploadAssetAsync(
		string targetServerBaseUrl,
		byte[] assetBytes,
		string extensionOrPath,
		string? metadataHeadersJson = null,
		string? authorPublicKey = null,
		string? authorSignature = null,
		CancellationToken cancellationToken = default)
	{
		string extension = Path.GetExtension(extensionOrPath).ToLowerInvariant();
		string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(assetBytes, extension);
		string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(canonicalBlake3);

		if (assetBytes.Length > ContentAddressableStorage.MaximumAssetSizeBytes)
		{
			double sizeMb = (double)assetBytes.Length / (1024 * 1024);
			double maxMb = (double)ContentAddressableStorage.MaximumAssetSizeBytes / (1024 * 1024);
			return new AssetUploadResponseDto
			{
				Success = false,
				Message = $"Upload rejected: Asset size ({sizeMb:F2} MB) exceeds maximum allowed size of {maxMb:F0} MB per asset.",
				Blake3Hash = normalizedHash
			};
		}

		string url = $"{targetServerBaseUrl.TrimEnd('/')}/api/assets/{normalizedHash}";

		using var content = new MultipartFormDataContent();
		var byteContent = new ByteArrayContent(assetBytes);
		byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
		content.Add(byteContent, "file", $"{normalizedHash}{extension}");

		AddUploadMultipartContent(content, metadataHeadersJson, authorPublicKey, authorSignature);

		using var request = new HttpRequestMessage(HttpMethod.Post, url);
		request.Content = content;

		AddUploadHeaders(request, extension, metadataHeadersJson, authorPublicKey, authorSignature);

		if (_throttle != null)
		{
			await _throttle.ConsumeAsync(assetBytes.Length, cancellationToken);
		}

		var response = await _httpClient.SendAsync(request, cancellationToken);
		string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

		if (!response.IsSuccessStatusCode)
		{
			return new AssetUploadResponseDto
			{
				Success = false,
				Message = $"Upload rejected: HTTP {(int)response.StatusCode} - {responseJson}",
				Blake3Hash = normalizedHash
			};
		}

		try
		{
			var result = JsonSerializer.Deserialize<AssetUploadResponseDto>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			return result ?? new AssetUploadResponseDto { Success = true, Blake3Hash = normalizedHash };
		}
		catch
		{
			return new AssetUploadResponseDto { Success = true, Blake3Hash = normalizedHash };
		}
	}

	private static void AddUploadMultipartContent(MultipartFormDataContent content, string? metadataHeadersJson, string? authorPublicKey, string? authorSignature)
	{
		if (!string.IsNullOrWhiteSpace(metadataHeadersJson))
		{
			content.Add(new StringContent(metadataHeadersJson, Encoding.UTF8), "metadata");
		}

		if (!string.IsNullOrWhiteSpace(authorPublicKey))
		{
			content.Add(new StringContent(authorPublicKey, Encoding.UTF8), "authorPublicKey");
		}

		if (!string.IsNullOrWhiteSpace(authorSignature))
		{
			content.Add(new StringContent(authorSignature, Encoding.UTF8), "authorSignature");
		}
	}

	private static void AddUploadHeaders(HttpRequestMessage request, string extension, string? metadataHeadersJson, string? authorPublicKey, string? authorSignature)
	{
		if (!string.IsNullOrEmpty(extension))
		{
			request.Headers.TryAddWithoutValidation("X-File-Extension", extension);
		}

		if (!string.IsNullOrWhiteSpace(metadataHeadersJson))
		{
			request.Headers.TryAddWithoutValidation("X-Asset-Metadata", Convert.ToBase64String(Encoding.UTF8.GetBytes(metadataHeadersJson)));
		}

		if (!string.IsNullOrWhiteSpace(authorPublicKey))
		{
			request.Headers.TryAddWithoutValidation("X-Author-Public-Key", authorPublicKey);
		}

		if (!string.IsNullOrWhiteSpace(authorSignature))
		{
			request.Headers.TryAddWithoutValidation("X-Author-Signature", authorSignature);
		}
	}
}
