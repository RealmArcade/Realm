using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

/// <summary>cluster events and removing manifest versions.</summary>
public partial class DistributionClient
	{
	public async Task<List<ClusterEventDto>> GetClusterEventsAsync(DateTime? sinceUtc = null, int limit = 100, CancellationToken cancellationToken = default)
	{
		string url = $"{_registryServerUrl}/api/cluster/events?limit={limit}";
		if (sinceUtc.HasValue)
		{
			url += $"&sinceUtc={Uri.EscapeDataString(sinceUtc.Value.ToString("o"))}";
		}

		try
		{
			var response = await _httpClient.GetAsync(url, cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				return new List<ClusterEventDto>();
			}

			string json = await response.Content.ReadAsStringAsync(cancellationToken);
			var list = JsonSerializer.Deserialize<List<ClusterEventDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			return list ?? new List<ClusterEventDto>();
		}
		catch
		{
			return new List<ClusterEventDto>();
		}
	}

	public async Task<bool> PostClusterEventAsync(ClusterEventDto evt, CancellationToken cancellationToken = default)
	{
		string url = $"{_registryServerUrl}/api/cluster/event";
		try
		{
			var content = new StringContent(JsonSerializer.Serialize(evt), Encoding.UTF8, "application/json");
			var response = await _httpClient.PostAsync(url, content, cancellationToken);
			return response.IsSuccessStatusCode;
		}
		catch
		{
			return false;
		}
	}

	public async Task<RemoveManifestResponseDto> RemoveManifestVersionAsync(string mapTitle, string mapVersion, string adminPrivateKeyBase64, CancellationToken cancellationToken = default)
	{
		string url = $"{_registryServerUrl}/api/admin/remove_manifest";
		string adminPublicKey = AuthorSignatureHelper.GetPublicKey(adminPrivateKeyBase64);
		string payload = $"remove_manifest:{mapTitle.Trim().ToLowerInvariant()}:{mapVersion.Trim().ToLowerInvariant()}";
		string signature = AuthorSignatureHelper.SignMessage(adminPrivateKeyBase64, payload);
		string bypassToken = AdminBypassAuth.CreateBypassToken(adminPrivateKeyBase64, mapTitle.Trim(), mapVersion.Trim());

		using var request = new HttpRequestMessage(HttpMethod.Post, url);
		request.Headers.Add("X-Admin-Bypass", bypassToken);
		request.Headers.Add("X-Admin-PublicKey", adminPublicKey);
		request.Headers.Add("X-Cluster-Signature", signature);

		var body = new AdminRemoveManifestRequest
		{
			MapTitle = mapTitle.Trim(),
			MapVersion = mapVersion.Trim(),
			AdminPublicKey = adminPublicKey,
			Signature = signature
		};
		request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

		try
		{
			var response = await _httpClient.SendAsync(request, cancellationToken);
			string json = await response.Content.ReadAsStringAsync(cancellationToken);
			var result = JsonSerializer.Deserialize<RemoveManifestResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			return result ?? new RemoveManifestResponseDto
			{
				Success = response.IsSuccessStatusCode,
				MapTitle = mapTitle,
				MapVersion = mapVersion,
				Message = json
			};
		}
		catch (Exception ex)
		{
			return new RemoveManifestResponseDto
			{
				Success = false,
				MapTitle = mapTitle,
				MapVersion = mapVersion,
				Message = ex.Message
			};
		}
	}

	public async Task<RemoveManifestResponseDto> RemoveAllManifestVersionsAsync(string mapTitle, string adminPrivateKeyBase64, CancellationToken cancellationToken = default)
	{
		string url = $"{_registryServerUrl}/api/admin/remove_manifest";
		string adminPublicKey = AuthorSignatureHelper.GetPublicKey(adminPrivateKeyBase64);
		string payload = $"remove_manifest:{mapTitle.Trim().ToLowerInvariant()}:all";
		string signature = AuthorSignatureHelper.SignMessage(adminPrivateKeyBase64, payload);
		string bypassToken = AdminBypassAuth.CreateBypassToken(adminPrivateKeyBase64, mapTitle.Trim(), "all");

		using var request = new HttpRequestMessage(HttpMethod.Post, url);
		request.Headers.Add("X-Admin-Bypass", bypassToken);
		request.Headers.Add("X-Admin-PublicKey", adminPublicKey);
		request.Headers.Add("X-Cluster-Signature", signature);

		var body = new AdminRemoveManifestRequest
		{
			MapTitle = mapTitle.Trim(),
			MapVersion = null,
			AdminPublicKey = adminPublicKey,
			Signature = signature
		};
		request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

		try
		{
			var response = await _httpClient.SendAsync(request, cancellationToken);
			string json = await response.Content.ReadAsStringAsync(cancellationToken);
			var result = JsonSerializer.Deserialize<RemoveManifestResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			return result ?? new RemoveManifestResponseDto
			{
				Success = response.IsSuccessStatusCode,
				MapTitle = mapTitle,
				AllVersionsRemoved = true,
				Message = json
			};
		}
		catch (Exception ex)
		{
			return new RemoveManifestResponseDto
			{
				Success = false,
				MapTitle = mapTitle,
				AllVersionsRemoved = true,
				Message = ex.Message
			};
		}
	}

	public async Task<RemoveManifestResponseDto> RemoveManifestAsync(string mapTitle, string? mapVersion, string adminPrivateKeyBase64, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(mapVersion))
		{
			return await RemoveAllManifestVersionsAsync(mapTitle, adminPrivateKeyBase64, cancellationToken);
		}

		return await RemoveManifestVersionAsync(mapTitle, mapVersion, adminPrivateKeyBase64, cancellationToken);
	}
}
