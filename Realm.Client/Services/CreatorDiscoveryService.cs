using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Realm.Client.UI;

namespace Realm.Client.Services;

/// <summary>
///     Reads the creator catalogue the discovery screen shows: the local playtime record that seeds "For You",
///     the registry's creator list, and one creator's published portfolio. It lives here rather than in the
///     screen because it is I/O with its own fallbacks, not layout.
/// </summary>
public static class CreatorDiscoveryService
{
	private const string CreatorsPath = "/api/creators";

	private static readonly JsonSerializerOptions CaseInsensitiveJson = new() { PropertyNameCaseInsensitive = true };

	/// <summary>
	///     Reads the creator -> minutes map the screen ranks "For You" by. A missing, unreadable or malformed
	///     file yields an empty map so the caller can decide what to show instead; it is not an error.
	/// </summary>
	public static Dictionary<string, int> LoadPlaytimeCreators(string statsPath)
	{
		var localCreators = new Dictionary<string, int>();
		if (!File.Exists(statsPath)) return localCreators;

		try
		{
			string statsJson = File.ReadAllText(statsPath);
			return JsonSerializer.Deserialize<Dictionary<string, int>>(statsJson) ?? localCreators;
		}
		catch
		{
			return localCreators;
		}
	}

	public static async Task<List<CreatorInfo>> LoadCreatorsAsync(string registryServerUrl)
	{
		try
		{
			using var httpClient = new HttpClient();
			var res = await httpClient.GetAsync(registryServerUrl + CreatorsPath);
			if (!res.IsSuccessStatusCode) return new List<CreatorInfo>();

			string json = await res.Content.ReadAsStringAsync();
			var creators = JsonSerializer.Deserialize<CreatorInfo[]>(json, CaseInsensitiveJson);
			return creators == null ? new List<CreatorInfo>() : creators.ToList();
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[CreatorDiscovery] Failed to load creators: {ex.Message}");
			return new List<CreatorInfo>();
		}
	}

	public static async Task<List<PortfolioAsset>> LoadPortfolioAsync(string registryServerUrl, CreatorInfo creator)
	{
		try
		{
			using var httpClient = new HttpClient();
			var res = await httpClient.GetAsync(registryServerUrl + $"{CreatorsPath}/{Uri.EscapeDataString(creator.PublicKey)}/portfolio");
			if (!res.IsSuccessStatusCode) return new List<PortfolioAsset>();

			string json = await res.Content.ReadAsStringAsync();
			var assets = JsonSerializer.Deserialize<PortfolioAsset[]>(json, CaseInsensitiveJson);
			if (assets == null || assets.Length == 0) return new List<PortfolioAsset>();

			return assets.ToList();
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[CreatorDiscovery] Failed to load portfolio: {ex.Message}");
			return new List<PortfolioAsset>();
		}
	}
}
