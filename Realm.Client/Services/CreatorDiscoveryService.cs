using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Realm.Client.UI;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

public class CreatorPortfolioResult
{
	public bool RequestSucceeded { get; init; }
	public List<MapData> Maps { get; init; } = new();
	public CreatorAssetPackDto? AssetPack { get; init; }
}

public class CreatorCatalogLoadResult
{
	public bool Reachable { get; init; }
	public List<CreatorInfo> Creators { get; init; } = new();
	public List<DiscoveryMapDto> DiscoveryMaps { get; init; } = new();
}

public static class CreatorDiscoveryService
{
	private const string CreatorsPath = "/api/creators";

	private static readonly HttpClient SharedHttpClient = new()
	{
		Timeout = TimeSpan.FromSeconds(10)
	};

	private static readonly JsonSerializerOptions CaseInsensitiveJson = new() { PropertyNameCaseInsensitive = true };

	public static Dictionary<string, int> LoadPlaytimeCreators(string statsPath)
	{
		var localCreators = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		if (!File.Exists(statsPath))
		{
			return localCreators;
		}

		try
		{
			string statsJson = File.ReadAllText(statsPath);
			var parsed = JsonSerializer.Deserialize<Dictionary<string, int>>(statsJson);
			if (parsed == null)
			{
				return localCreators;
			}

			foreach (var kvp in parsed)
			{
				if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value > 0)
				{
					string key = kvp.Key.Trim();
					localCreators[key] = localCreators.TryGetValue(key, out int existing) ? existing + kvp.Value : kvp.Value;
				}
			}

			return localCreators;
		}
		catch
		{
			return localCreators;
		}
	}

	public static async Task<CreatorCatalogLoadResult> LoadCatalogAsync(string registryServerUrl)
	{
		string baseUrl = registryServerUrl.TrimEnd('/');
		var creatorsTask = FetchCreatorsRawAsync(baseUrl);
		var mapsTask = new DistributionClient(baseUrl, SharedHttpClient).GetDiscoveryMapsAsync();

		await Task.WhenAll(creatorsTask, mapsTask);

		var (reachable, rawCreators) = await creatorsTask;
		List<DiscoveryMapDto> discoveryMaps = DeduplicateLatestMaps(await mapsTask);

		var merged = MergeCreatorsWithDiscoveryMaps(rawCreators, discoveryMaps);
		return new CreatorCatalogLoadResult
		{
			Reachable = reachable || discoveryMaps.Count > 0,
			Creators = merged,
			DiscoveryMaps = discoveryMaps
		};
	}

	public static async Task<List<CreatorInfo>> LoadCreatorsAsync(string registryServerUrl)
	{
		var (_, creators) = await FetchCreatorsRawAsync(registryServerUrl.TrimEnd('/'));
		return creators;
	}

	public static async Task<CreatorPortfolioResult> LoadPortfolioAsync(
		string registryServerUrl,
		CreatorInfo creator,
		IReadOnlyList<DiscoveryMapDto>? catalogMaps = null)
	{
		var mapDtos = new List<DiscoveryMapDto>();
		CreatorAssetPackDto? assetPack = null;
		bool requestSucceeded = string.IsNullOrWhiteSpace(creator.PublicKey);

		if (!string.IsNullOrWhiteSpace(creator.PublicKey))
		{
			try
			{
				string url = $"{registryServerUrl.TrimEnd('/')}{CreatorsPath}/{Uri.EscapeDataString(creator.PublicKey)}/portfolio";
				using HttpResponseMessage res = await SharedHttpClient.GetAsync(url);
				if (res.IsSuccessStatusCode)
				{
					requestSucceeded = true;
					string json = await res.Content.ReadAsStringAsync();
					ParsePortfolioJson(json, creator, mapDtos, out assetPack);
				}
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[CreatorDiscovery] Failed to load portfolio: {ex.Message}");
			}
		}

		MergeCatalogMapsForCreator(mapDtos, creator.Username, catalogMaps);

		var maps = DeduplicateLatestMaps(mapDtos)
			.OrderByDescending(m => m.GamesPlayed)
			.ThenBy(m => m.Title, StringComparer.OrdinalIgnoreCase)
			.Select(m => MapData.FromDto(m, registryServerUrl))
			.ToList();

		if (assetPack != null && assetPack.AssetCount <= 0)
		{
			assetPack = null;
		}

		return new CreatorPortfolioResult
		{
			RequestSucceeded = requestSucceeded || maps.Count > 0,
			Maps = maps,
			AssetPack = assetPack
		};
	}

	private static async Task<(bool Reachable, List<CreatorInfo> Creators)> FetchCreatorsRawAsync(string baseUrl)
	{
		try
		{
			using HttpResponseMessage res = await SharedHttpClient.GetAsync(baseUrl + CreatorsPath);
			if (!res.IsSuccessStatusCode)
			{
				return (false, new List<CreatorInfo>());
			}

			string json = await res.Content.ReadAsStringAsync();
			var summaries = JsonSerializer.Deserialize<CreatorSummaryDto[]>(json, CaseInsensitiveJson);
			if (summaries == null)
			{
				return (true, new List<CreatorInfo>());
			}

			var creators = summaries
				.Where(c => !string.IsNullOrWhiteSpace(c.PublicKey) && !string.IsNullOrWhiteSpace(c.Username))
				.GroupBy(c => c.PublicKey, StringComparer.OrdinalIgnoreCase)
				.Select(g => g.First())
				.Select(c => new CreatorInfo
				{
					PublicKey = c.PublicKey.Trim(),
					Username = c.Username.Trim(),
					DonationLink = (c.DonationLink ?? string.Empty).Trim(),
					ContactInfo = (c.ContactInfo ?? string.Empty).Trim(),
					PublishedMapCount = c.PublishedMapCount,
					AssetCount = c.AssetCount,
					IsRegistered = true
				})
				.OrderBy(c => c.Username, StringComparer.OrdinalIgnoreCase)
				.ToList();

			return (true, creators);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[CreatorDiscovery] Failed to load creators: {ex.Message}");
			return (false, new List<CreatorInfo>());
		}
	}

	private static List<CreatorInfo> MergeCreatorsWithDiscoveryMaps(
		List<CreatorInfo> registeredCreators,
		List<DiscoveryMapDto> discoveryMaps)
	{
		var mapsByCreator = discoveryMaps
			.Where(m => !string.IsNullOrWhiteSpace(m.Creator) &&
			            !string.Equals(m.Creator.Trim(), "Unknown", StringComparison.OrdinalIgnoreCase) &&
			            !string.Equals(m.Creator.Trim(), "Realm Builder", StringComparison.OrdinalIgnoreCase))
			.GroupBy(m => m.Creator.Trim(), StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

		var result = new List<CreatorInfo>(registeredCreators.Count);
		var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var creator in registeredCreators)
		{
			var updated = creator;
			seenNames.Add(creator.Username);
			if (mapsByCreator.TryGetValue(creator.Username, out var creatorMaps))
			{
				if (updated.PublishedMapCount == 0)
				{
					updated.PublishedMapCount = creatorMaps.Count;
				}

				updated.TotalGamesPlayed = creatorMaps.Sum(m => m.GamesPlayed);
			}

			result.Add(updated);
		}

		foreach (var kvp in mapsByCreator)
		{
			if (seenNames.Contains(kvp.Key))
			{
				continue;
			}

			result.Add(new CreatorInfo
			{
				PublicKey = string.Empty,
				Username = kvp.Key,
				DonationLink = string.Empty,
				ContactInfo = string.Empty,
				PublishedMapCount = kvp.Value.Count,
				AssetCount = 0,
				TotalGamesPlayed = kvp.Value.Sum(m => m.GamesPlayed),
				IsRegistered = false
			});
		}

		return result
			.OrderBy(c => c.Username, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static void ParsePortfolioJson(
		string json,
		CreatorInfo creator,
		List<DiscoveryMapDto> mapDtos,
		out CreatorAssetPackDto? assetPack)
	{
		assetPack = null;
		if (string.IsNullOrWhiteSpace(json))
		{
			return;
		}

		using var doc = JsonDocument.Parse(json);
		if (doc.RootElement.ValueKind == JsonValueKind.Object)
		{
			var portfolio = JsonSerializer.Deserialize<CreatorPortfolioDto>(json, CaseInsensitiveJson);
			if (portfolio == null)
			{
				return;
			}

			if (portfolio.Maps != null)
			{
				mapDtos.AddRange(portfolio.Maps.Where(m => !m.IsTestVersion && !string.IsNullOrWhiteSpace(m.Title)));
			}

			assetPack = portfolio.AssetPack;
			return;
		}

		if (doc.RootElement.ValueKind == JsonValueKind.Array)
		{
			var uniqueHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			string representativeHash = string.Empty;

			foreach (var item in doc.RootElement.EnumerateArray())
			{
				string rawHash = item.TryGetProperty("hash", out var hLower)
					? hLower.GetString() ?? string.Empty
					: (item.TryGetProperty("Hash", out var hUpper) ? hUpper.GetString() ?? string.Empty : string.Empty);
				if (string.IsNullOrWhiteSpace(rawHash))
				{
					continue;
				}

				string norm = ContentAddressableStorage.NormalizeBlake3Hash(rawHash);
				if (uniqueHashes.Add(norm) && string.IsNullOrEmpty(representativeHash))
				{
					representativeHash = norm;
				}
			}

			if (uniqueHashes.Count > 0)
			{
				assetPack = new CreatorAssetPackDto
				{
					Title = $"{creator.Username} Asset Pack",
					AuthorUsername = creator.Username,
					AuthorPublicKey = creator.PublicKey,
					RepresentativeHash = representativeHash,
					AssetCount = uniqueHashes.Count,
					TotalSizeBytes = 0
				};
			}
		}
	}

	private static void MergeCatalogMapsForCreator(
		List<DiscoveryMapDto> target,
		string creatorName,
		IReadOnlyList<DiscoveryMapDto>? catalogMaps)
	{
		if (catalogMaps == null || string.IsNullOrWhiteSpace(creatorName))
		{
			return;
		}

		var existingTitles = new HashSet<string>(
			target.Select(m => m.Title.Trim()),
			StringComparer.OrdinalIgnoreCase);

		var matchingLatest = DeduplicateLatestMaps(catalogMaps.Where(dto =>
			!dto.IsTestVersion &&
			!string.IsNullOrWhiteSpace(dto.Title) &&
			string.Equals(dto.Creator?.Trim(), creatorName.Trim(), StringComparison.OrdinalIgnoreCase)));

		foreach (var dto in matchingLatest)
		{
			if (existingTitles.Add(dto.Title.Trim()))
			{
				target.Add(dto);
			}
		}
	}

	private static List<DiscoveryMapDto> DeduplicateLatestMaps(IEnumerable<DiscoveryMapDto> maps)
	{
		return maps
			.Where(m => !m.IsTestVersion && !string.IsNullOrWhiteSpace(m.Title))
			.GroupBy(m => m.Title.Trim(), StringComparer.OrdinalIgnoreCase)
			.Select(g => g
				.OrderByDescending(m => m.Version, Comparer<string>.Create(CompareVersions))
				.First())
			.ToList();
	}

	private static int CompareVersions(string? a, string? b)
	{
		string[] partsA = (a ?? string.Empty).Split('.', '-', '+');
		string[] partsB = (b ?? string.Empty).Split('.', '-', '+');
		int max = Math.Max(partsA.Length, partsB.Length);
		for (int i = 0; i < max; i++)
		{
			string segA = i < partsA.Length ? partsA[i] : "0";
			string segB = i < partsB.Length ? partsB[i] : "0";
			bool isNumA = int.TryParse(segA, out int numA);
			bool isNumB = int.TryParse(segB, out int numB);
			int cmp = isNumA && isNumB
				? numA.CompareTo(numB)
				: string.Compare(segA, segB, StringComparison.OrdinalIgnoreCase);
			if (cmp != 0)
			{
				return cmp;
			}
		}

		return 0;
	}
}
