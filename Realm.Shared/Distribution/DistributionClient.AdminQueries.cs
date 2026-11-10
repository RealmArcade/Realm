using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

/// <summary>pruning, state digests, snapshots and their application.</summary>
public partial class DistributionClient
	{
	public async Task<CasPruneResponseDto> PruneServerCasAsync(string adminPrivateKeyBase64, CancellationToken cancellationToken = default)
	{
		string url = $"{_registryServerUrl}/api/admin/prune_cas";
		string bypassToken = AdminBypassAuth.CreateBypassToken(adminPrivateKeyBase64, "admin", "prune_cas");

		using var request = new HttpRequestMessage(HttpMethod.Post, url);
		request.Headers.Add("X-Admin-Bypass", bypassToken);

		try
		{
			var response = await _httpClient.SendAsync(request, cancellationToken);
			string json = await response.Content.ReadAsStringAsync(cancellationToken);

			var result = JsonSerializer.Deserialize<CasPruneResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			return result ?? new CasPruneResponseDto
			{
				Success = response.IsSuccessStatusCode,
				Message = json
			};
		}
		catch (Exception ex)
		{
			return new CasPruneResponseDto
			{
				Success = false,
				Message = ex.Message
			};
		}
	}

	public async Task<ClusterStateDigestDto?> GetStateDigestAsync(CancellationToken cancellationToken = default)
	{
		string url = $"{_registryServerUrl}/api/cluster/state_digest";
		try
		{
			var response = await _httpClient.GetAsync(url, cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				return null;
			}

			string json = await response.Content.ReadAsStringAsync(cancellationToken);
			return JsonSerializer.Deserialize<ClusterStateDigestDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
		}
		catch
		{
			return null;
		}
	}

	public async Task<ClusterSnapshotDto?> GetSnapshotAsync(CancellationToken cancellationToken = default)
	{
		string url = $"{_registryServerUrl}/api/cluster/snapshot";
		try
		{
			var response = await _httpClient.GetAsync(url, cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				return null;
			}

			string json = await response.Content.ReadAsStringAsync(cancellationToken);
			return JsonSerializer.Deserialize<ClusterSnapshotDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
		}
		catch
		{
			return null;
		}
	}

	public async Task<ApplySnapshotResponseDto> ApplySnapshotAsync(ClusterSnapshotDto snapshot, string? adminPrivateKeyBase64 = null, CancellationToken cancellationToken = default)
	{
		string url = $"{_registryServerUrl}/api/cluster/snapshot";
		using var request = new HttpRequestMessage(HttpMethod.Post, url);
		request.Content = new StringContent(JsonSerializer.Serialize(snapshot), Encoding.UTF8, "application/json");

		if (!string.IsNullOrWhiteSpace(adminPrivateKeyBase64))
		{
			string bypassToken = AdminBypassAuth.CreateBypassToken(adminPrivateKeyBase64, "cluster", "snapshot");
			request.Headers.Add("X-Admin-Bypass", bypassToken);
		}

		try
		{
			var response = await _httpClient.SendAsync(request, cancellationToken);
			string json = await response.Content.ReadAsStringAsync(cancellationToken);

			var result = JsonSerializer.Deserialize<ApplySnapshotResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			return result ?? new ApplySnapshotResponseDto
			{
				Success = response.IsSuccessStatusCode,
				Message = json
			};
		}
		catch (Exception ex)
		{
			return new ApplySnapshotResponseDto
			{
				Success = false,
				Message = ex.Message
			};
		}
	}
}
