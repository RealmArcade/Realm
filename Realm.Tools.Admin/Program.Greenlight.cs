using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommandLine;
using Realm.Shared;
using Realm.Shared.Accounts;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;

namespace Realm.Tools.Admin;


public static partial class Program
{
	private static int ExecuteGreenlight(AdminGreenlightOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		using var keyPair = ParseAdminKey(options.Key);
		if (keyPair == null) return 1;

		string mapTitle = options.Map.Trim();
		string creatorUsername = options.Creator.Trim();

		// The override is signed for one map and one creator name: the server resolves that name through its name locks
		// to the key allowed to publish, so a greenlit title cannot be published by whoever hears about it first.
		string payload = $"greenlight:{mapTitle.ToLowerInvariant()}:{NameNormalizationHelper.GetCreatorSlug(creatorUsername)}";
		string signature = keyPair.CreateSignature(payload);

		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Map Greenlight Override");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server:     {serverUrl}");
		Console.WriteLine($"Map Title:  {mapTitle}");
		Console.WriteLine($"Creator:    {creatorUsername}");
		Console.WriteLine($"Admin Key:  {keyPair.PublicKeyBase64}");
		Console.WriteLine();

		try
		{
			using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
			var requestBody = new
			{
				MapTitle = mapTitle,
				CreatorUsername = creatorUsername,
				AdminPublicKey = keyPair.PublicKeyBase64,
				Signature = signature
			};

			var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
			var response = httpClient.PostAsync($"{serverUrl.TrimEnd('/')}/api/admin/greenlight", content).GetAwaiter().GetResult();
			string responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

			if (response.IsSuccessStatusCode)
			{
				Console.WriteLine($"[SUCCESS] Map '{mapTitle}' has been greenlit for {creatorUsername}!");
				Console.WriteLine($"{creatorUsername} may now publish this map through the Map Editor; no other key can, until you release the title or add them as a maintainer.");
				return 0;
			}
			else
			{
				Console.Error.WriteLine($"[FAILED] HTTP {(int)response.StatusCode}: {responseText}");
				return 1;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Connection failed: {ex.Message}");
			return 1;
		}
	}

	private static int ExecuteStatus(AdminStatusOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		string mapTitle = options.Map.Trim();
		string? mapVersion = ResolveMapVersion(options.Version, serverUrl, mapTitle);

		string endpoint = string.IsNullOrWhiteSpace(mapVersion)
			? $"{serverUrl.TrimEnd('/')}/api/maps/greenlight_status/{Uri.EscapeDataString(mapTitle)}"
			: $"{serverUrl.TrimEnd('/')}/api/maps/greenlight_status/{Uri.EscapeDataString(mapTitle)}_{Uri.EscapeDataString(mapVersion)}";

		try
		{
			using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
			var response = httpClient.GetAsync(endpoint).GetAwaiter().GetResult();
			string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

			if (!response.IsSuccessStatusCode)
			{
				Console.Error.WriteLine($"[FAILED] HTTP {(int)response.StatusCode}: {json}");
				return 1;
			}

			return PrintGreenlightStatus(json, mapTitle, mapVersion);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Connection failed: {ex.Message}");
			return 1;
		}
	}

	private static string? ResolveMapVersion(string? optionsVersion, string serverUrl, string mapTitle)
	{
		if (!string.IsNullOrWhiteSpace(optionsVersion)) return optionsVersion.Trim();

		try
		{
			var client = new DistributionClient(serverUrl);
			var discoveryMaps = client.GetDiscoveryMapsAsync().GetAwaiter().GetResult();

			var matchingMap = discoveryMaps
				.Where(m => IsMatchingMap(m, mapTitle))
				.OrderByDescending(m => m.Version, Comparer<string>.Create(CompareVersions))
				.FirstOrDefault();

			if (matchingMap != null && !string.IsNullOrWhiteSpace(matchingMap.Version))
			{
				return matchingMap.Version;
			}
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine($"[WARN] Could not look up the current version of '{mapTitle}' on {serverUrl}: {exception.Message}");
		}

		return null;
	}

	private static bool IsMatchingMap(DiscoveryMapDto m, string mapTitle)
	{
		return string.Equals(m.Title, mapTitle, StringComparison.OrdinalIgnoreCase) ||
		       m.MapId.StartsWith(mapTitle + "_", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(m.MapId, mapTitle, StringComparison.OrdinalIgnoreCase);
	}

	private static int PrintGreenlightStatus(string json, string mapTitle, string? mapVersion)
	{
		var node = JsonNode.Parse(json);
		if (node == null)
		{
			Console.Error.WriteLine("Error parsing status response.");
			return 1;
		}

		bool isGreenlit = GetBool(node, "isGreenlit");
		bool adminOverride = GetBool(node, "adminOverride");
		int verifiedGoodReviews = GetInt(node, "verifiedGoodReviewsCount");
		int totalReviews = GetInt(node, "totalReviewsCount");
		double avgRating = GetDouble(node, "averageRating");

		string titleHeader = !string.IsNullOrWhiteSpace(mapVersion)
			? $"Greenlight Status for '{mapTitle}' v{mapVersion}"
			: $"Greenlight Status for '{mapTitle}'";

		Console.WriteLine("=================================================");
		Console.WriteLine(titleHeader);
		Console.WriteLine("=================================================");
		Console.WriteLine($"Overall Greenlit:             {(isGreenlit ? "YES (Approved for Discovery)" : "NO (In Beta-Testing)")}");
		Console.WriteLine($"Admin Override:               {(adminOverride ? "ACTIVE" : "None")}");
		Console.WriteLine();
		Console.WriteLine("Greenlight Criteria:");
		Console.WriteLine($"  - Total Verified Good Reviews: {verifiedGoodReviews} / 100 (Required: >= 100)");
		Console.WriteLine();
		Console.WriteLine("Public Discovery Statistics:");
		Console.WriteLine($"  - Public Total Reviews:        {totalReviews}");
		Console.WriteLine($"  - Public Average Rating:       {avgRating:F1} / 5.0");
		Console.WriteLine("=================================================");
		return 0;
	}

	private static bool GetBool(JsonNode node, string key)
	{
		return node[key]?.GetValue<bool>() ?? false;
	}

	private static int GetInt(JsonNode node, string key)
	{
		return node[key]?.GetValue<int>() ?? 0;
	}

	private static double GetDouble(JsonNode node, string key)
	{
		return node[key]?.GetValue<double>() ?? 0.0;
	}
}
