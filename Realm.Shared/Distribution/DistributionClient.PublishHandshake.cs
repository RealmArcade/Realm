using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

/// <summary>the initiate/finalize publish handshake and asset probing.</summary>
public partial class DistributionClient
	{
	public async Task<PublishMapInitiateResponse> InitiatePublishAsync(PublishMapInitiateRequest request, CancellationToken cancellationToken = default)
	{
		string url = $"{_registryServerUrl}/api/publish_map/initiate";
		var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

		try
		{
			var response = await _httpClient.PostAsync(url, content, cancellationToken);
			string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

			var result = JsonSerializer.Deserialize<PublishMapInitiateResponse>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			return result ?? new PublishMapInitiateResponse
			{
				Success = response.IsSuccessStatusCode,
				Message = responseJson
			};
		}
		catch (Exception ex)
		{
			return new PublishMapInitiateResponse
			{
				Success = false,
				Status = "Error",
				Message = ex.Message
			};
		}
	}

	public async Task<PublishMapFinalizeResponse> FinalizePublishAsync(PublishMapFinalizeRequest request, CancellationToken cancellationToken = default)
	{
		string url = $"{_registryServerUrl}/api/publish_map/finalize";
		var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

		try
		{
			var response = await _httpClient.PostAsync(url, content, cancellationToken);
			string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

			var result = JsonSerializer.Deserialize<PublishMapFinalizeResponse>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			return result ?? new PublishMapFinalizeResponse
			{
				Success = response.IsSuccessStatusCode,
				Message = responseJson
			};
		}
		catch (Exception ex)
		{
			return new PublishMapFinalizeResponse
			{
				Success = false,
				Status = "Error",
				Message = ex.Message
			};
		}
	}

	public async Task<bool> CheckAssetExistsAsync(string hash, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(hash)) return false;
		string normalized = ContentAddressableStorage.NormalizeBlake3Hash(hash);
		string url = $"{_registryServerUrl}/api/assets/{normalized}";
		try
		{
			using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
			using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
			using var request = new HttpRequestMessage(HttpMethod.Head, url);
			using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
			return response.IsSuccessStatusCode;
		}
		catch
		{
			return false;
		}
	}

	public async Task<bool> UploadMissingAssetAsync(
		string hash,
		byte[] fileBytes,
		string fileName,
		string currentUsername,
		string authorPublicKey,
		string signature,
		string mapTitle,
		string mapVersion,
		string? sessionId = null,
		CancellationToken cancellationToken = default)
	{
		if (fileBytes.Length == 0 || fileBytes.Length > ContentAddressableStorage.MaximumAssetSizeBytes)
		{
			return false;
		}

		if (await CheckAssetExistsAsync(hash, cancellationToken))
		{
			return true;
		}

		string url = $"{_registryServerUrl}/api/publish_map/upload_asset";
		using var form = new MultipartFormDataContent();
		form.Add(new StringContent(hash), "Hash");
		form.Add(new StringContent(signature), "Signature");
		form.Add(new StringContent(currentUsername), "AuthorUsername");
		form.Add(new StringContent(authorPublicKey), "PublicKey");
		form.Add(new StringContent(mapTitle), "MapTitle");
		form.Add(new StringContent(mapVersion), "MapVersion");
		if (!string.IsNullOrEmpty(sessionId))
		{
			form.Add(new StringContent(sessionId), "SessionId");
		}

		var fileContent = new ByteArrayContent(fileBytes);
		form.Add(fileContent, "File", fileName);

		if (_throttle != null)
		{
			await _throttle.ConsumeAsync(fileBytes.Length, cancellationToken);
		}

		try
		{
			var response = await _httpClient.PostAsync(url, form, cancellationToken);
			return response.IsSuccessStatusCode;
		}
		catch
		{
			return false;
		}
	}
}
