using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

/// <summary>circuit breakers and the priority order servers are tried in.</summary>
public partial class DistributionClient
	{
	private int _roundRobinCounter = 0;
	private static readonly ConcurrentDictionary<string, SeederCircuitState> SeederCircuitBreakers = new(StringComparer.OrdinalIgnoreCase);

	public static SeederCircuitState GetCircuitState(string baseUrl)
	{
		return SeederCircuitBreakers.GetOrAdd(baseUrl.TrimEnd('/'), _ => new SeederCircuitState());
	}

	public static void ResetAllCircuitBreakers()
	{
		foreach (var pair in SeederCircuitBreakers)
		{
			pair.Value.Reset();
		}
	}

	private List<string> GetPrioritizedServerUrls(
		string normalizedHash,
		List<SeederNodeDto> seeders,
		string? fallbackHostUrl)
	{
		var candidateSeeders = seeders
			.Where(s => DistributionSharding.SeederAcceptsHash(s.SeederId, s.CapacityPercentage, normalizedHash))
			.ToList();

		var prioritizedUrls = new List<string>();

		if (candidateSeeders.Count > 0)
		{
			int startIndex = Interlocked.Increment(ref _roundRobinCounter) % candidateSeeders.Count;
			for (int i = 0; i < candidateSeeders.Count; i++)
			{
				int index = (startIndex + i) % candidateSeeders.Count;
				var seeder = candidateSeeders[index];
				prioritizedUrls.Add($"http://{seeder.IP}:{seeder.Port}");
			}
		}

		foreach (var seeder in seeders)
		{
			string url = $"http://{seeder.IP}:{seeder.Port}";
			if (!prioritizedUrls.Contains(url))
			{
				prioritizedUrls.Add(url);
			}
		}

		if (!string.IsNullOrEmpty(fallbackHostUrl) && !prioritizedUrls.Contains(fallbackHostUrl))
		{
			prioritizedUrls.Add(fallbackHostUrl.TrimEnd('/'));
		}

		if (!prioritizedUrls.Contains(_registryServerUrl))
		{
			prioritizedUrls.Add(_registryServerUrl);
		}

		return prioritizedUrls;
	}
}
