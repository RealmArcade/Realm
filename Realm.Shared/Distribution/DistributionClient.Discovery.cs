using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

/// <summary>manifests, discovery maps and map maintainers.</summary>
public partial class DistributionClient
	{
	public async Task<MapManifest?> GetManifestAsync(string mapId, CancellationToken cancellationToken = default)
	{
		string url = $"{_registryServerUrl}/api/manifests/{Uri.EscapeDataString(mapId)}";
		try
		{
			var response = await _httpClient.GetAsync(url, cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				return null;
			}

			string json = await response.Content.ReadAsStringAsync(cancellationToken);
			return MapManifest.LoadFromJson(json);
		}
		catch
		{
			return null;
		}
	}

	public async Task<List<DiscoveryMapDto>> GetDiscoveryMapsAsync(CancellationToken cancellationToken = default)
	{
		string url = $"{_registryServerUrl}/api/discovery/maps";
		try
		{
			var response = await _httpClient.GetAsync(url, cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				return new List<DiscoveryMapDto>();
			}

			string json = await response.Content.ReadAsStringAsync(cancellationToken);
			var maps = JsonSerializer.Deserialize<List<DiscoveryMapDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			return maps ?? new List<DiscoveryMapDto>();
		}
		catch
		{
			return new List<DiscoveryMapDto>();
		}
	}

	public async Task<MapMaintainersResponseDto> GetMapMaintainersAsync(string mapTitle, CancellationToken cancellationToken = default)
	{
		string url = $"{_registryServerUrl}/api/maps/{Uri.EscapeDataString(mapTitle)}/maintainers";
		try
		{
			var response = await _httpClient.GetAsync(url, cancellationToken);
			string json = await response.Content.ReadAsStringAsync(cancellationToken);
			var dto = JsonSerializer.Deserialize<MapMaintainersResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			return dto ?? new MapMaintainersResponseDto { Success = false, Message = "Failed to deserialize server response." };
		}
		catch (Exception ex)
		{
			return new MapMaintainersResponseDto { Success = false, Message = ex.Message };
		}
	}

	private async Task<MapMaintainersResponseDto> SendMaintainerRequestAsync(string url, object request, CancellationToken cancellationToken)
	{
		try
		{
			var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
			var response = await _httpClient.PostAsync(url, content, cancellationToken);
			string json = await response.Content.ReadAsStringAsync(cancellationToken);
			var dto = JsonSerializer.Deserialize<MapMaintainersResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			return dto ?? new MapMaintainersResponseDto { Success = false, Message = "Failed to deserialize server response." };
		}
		catch (Exception ex)
		{
			return new MapMaintainersResponseDto { Success = false, Message = ex.Message };
		}
	}

	public async Task<MapMaintainersResponseDto> AddMapMaintainerAsync(AddMapMaintainerRequest request, CancellationToken cancellationToken = default)
	{
		string url = $"{_registryServerUrl}/api/maps/{Uri.EscapeDataString(request.MapTitle)}/maintainers/add";
		return await SendMaintainerRequestAsync(url, request, cancellationToken);
	}

	public async Task<MapMaintainersResponseDto> RemoveMapMaintainerAsync(RemoveMapMaintainerRequest request, CancellationToken cancellationToken = default)
	{
		string url = $"{_registryServerUrl}/api/maps/{Uri.EscapeDataString(request.MapTitle)}/maintainers/remove";
		return await SendMaintainerRequestAsync(url, request, cancellationToken);
	}
}
