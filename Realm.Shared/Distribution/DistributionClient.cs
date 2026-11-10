using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

public partial class DistributionClient
	{
	private const int FailedAssetIndex = 0;
	private const int FailureMessageIndex = 1;

	private readonly HttpClient _httpClient;
	private readonly string _registryServerUrl;
	private readonly TokenBucketThrottle? _throttle;

	public DistributionClient(string? registryServerUrl = null, HttpClient? httpClient = null, TokenBucketThrottle? throttle = null)
	{
		_registryServerUrl = (string.IsNullOrWhiteSpace(registryServerUrl) ? ServersConfigHelper.GetDefaultServerUrl() : registryServerUrl).TrimEnd('/');
		_httpClient = httpClient ?? new HttpClient();
		_throttle = throttle;
	}

	public async Task<List<SeederNodeDto>> GetActiveSeedersAsync(CancellationToken cancellationToken = default)
	{
		string url = $"{_registryServerUrl}/api/seeders";
		try
		{
			var response = await _httpClient.GetAsync(url, cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				return new List<SeederNodeDto>();
			}

			string json = await response.Content.ReadAsStringAsync(cancellationToken);
			var seeders = JsonSerializer.Deserialize<List<SeederNodeDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			return seeders ?? new List<SeederNodeDto>();
		}
		catch
		{
			return new List<SeederNodeDto>();
		}
	}

}
