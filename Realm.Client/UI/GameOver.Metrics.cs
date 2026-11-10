// Post-match metrics: report playtime and stars, add local playtime, and resolve the manifest info
// (version, author key, contributors) that the report needs before it can be sent.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Shared.Distribution;
using HttpClient = System.Net.Http.HttpClient;
using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Utils;


namespace Realm.Client.UI;

public partial class GameOver
{
	private async Task ReportMetricsAsync(string mapName, double playtimeMinutes, int stars, bool isComplete)
	{
		string mapTitle = CleanMapName(mapName);
		string mapVersion = GameContext.Lobby?.ActiveMapVersion ?? "1.0.0";

		ExtractManifestInfo(mapName, mapTitle, ref mapVersion, out string authorPubKey);

		var payload = new
		{
			MapTitle = mapTitle,
			MapVersion = mapVersion,
			AuthorPublicKey = authorPubKey,
			PlaytimeMinutes = playtimeMinutes,
			Stars = stars,
			IsCompleteGame = isComplete,
			SessionToken = GameContext.Account.SessionToken
		};

		string seedServerUrl = GodotObject.IsInstanceValid(GameContext.Lobby) ? GameContext.Lobby.RegistryServerUrl : ServersConfigHelper.GetDefaultServerUrl();
		try
		{
			using var httpClient = new HttpClient();
			var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
			await httpClient.PostAsync(seedServerUrl + "/api/maps/report_metrics", content);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[GameOver] Failed to report map metrics: {ex.Message}");
		}
	}

	private void AddLocalPlaytime(string mapName, double playtimeMinutes)
	{
		try
		{
			string rawName = CleanMapName(mapName);
			var contributors = GetContributorsFromMetadata(rawName);

			if (contributors.Count == 0)
			{
				contributors.Add(UnknownAuthorContributorName);
			}

			UpdatePlaytimeStats(contributors, playtimeMinutes);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[GameOver] Failed to update local playtime stats: {ex.Message}");
		}
	}

	private static string CleanMapName(string mapName)
	{
		string mapTitle = mapName;
		if (mapTitle.StartsWith("[Beta-Testing] "))
		{
			mapTitle = mapTitle.Substring("[Beta-Testing] ".Length);
		}

		int dashIdx = mapTitle.LastIndexOf(" - ");
		if (dashIdx != -1)
		{
			mapTitle = mapTitle.Substring(0, dashIdx);
		}

		return mapTitle.Trim();
	}

	private static void ExtractManifestInfo(string mapName, string mapTitle, ref string mapVersion, out string authorPubKey)
	{
		authorPubKey = "";
		try
		{
			if (TryExtractFromManifest(mapName, mapTitle, ref mapVersion, out string pubKey))
			{
				authorPubKey = pubKey;
				return;
			}

			authorPubKey = ExtractAuthorPubKeyFromMetadata(mapTitle);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"could not read the author key of map {mapTitle}, so its attribution will be missing: {ex.GetType().Name}: {ex.Message}");
		}
	}

	private static bool TryExtractFromManifest(string mapName, string mapTitle, ref string mapVersion, out string authorPubKey)
	{
		authorPubKey = "";
		string? manifestPath = MapAssetManager.FindManifestPath(mapName, mapVersion)
		                       ?? MapAssetManager.FindManifestPath(mapTitle, mapVersion);

		if (string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath))
			return false;

		string json = File.ReadAllText(manifestPath);
		using var mapDoc = JsonDocument.Parse(json);
		var root = mapDoc.RootElement;

		if (root.TryGetProperty("author_key", out var keyProp))
			authorPubKey = keyProp.GetString() ?? "";

		if (root.TryGetProperty("Version", out var vProp) && vProp.ValueKind == JsonValueKind.String)
			mapVersion = vProp.GetString() ?? mapVersion;

		return true;
	}

	private static string ExtractAuthorPubKeyFromMetadata(string mapTitle)
	{
		string[] possiblePaths =
		{
			ProjectSettings.GlobalizePath($"user://maps/{mapTitle}/metadata.json"),
			ProjectSettings.GlobalizePath($"res://Maps/{mapTitle}/metadata.json"),
			Path.Combine(GameContext.EditorService._editingDirectory, "metadata.json")
		};

		foreach (var p in possiblePaths)
		{
			if (!File.Exists(p))
				continue;

			string json = File.ReadAllText(p);
			using var mapDoc = JsonDocument.Parse(json);
			var root = mapDoc.RootElement;
			if (root.TryGetProperty("author_key", out var keyProp))
				return keyProp.GetString() ?? "";
			break;
		}

		return "";
	}

	private static List<string> GetContributorsFromMetadata(string rawName)
	{
		var contributors = new List<string>();
		string[] paths =
		{
			ProjectSettings.GlobalizePath($"res://Maps/{rawName}/metadata.json"),
			ProjectSettings.GlobalizePath($"user://maps/{rawName}/metadata.json"),
			Path.Combine(GameContext.EditorService._editingDirectory, "metadata.json")
		};

		foreach (var path in paths)
		{
			if (!File.Exists(path)) continue;

			string json = File.ReadAllText(path);
			using var doc = JsonDocument.Parse(json);
			if (!doc.RootElement.TryGetProperty("Contributors", out var conts) || conts.ValueKind != JsonValueKind.Array) break;

			foreach (var el in conts.EnumerateArray())
			{
				var s = el.GetString();
				if (!string.IsNullOrEmpty(s)) contributors.Add(s);
			}

			break;
		}

		return contributors;
	}

	private static void UpdatePlaytimeStats(List<string> contributors, double playtimeMinutes)
	{
		string statsPath = ProjectSettings.GlobalizePath("user://appdata/playtime_stats.json");
		var stats = new Dictionary<string, int>();
		if (File.Exists(statsPath))
		{
			string statsJson = File.ReadAllText(statsPath);
			stats = JsonSerializer.Deserialize<Dictionary<string, int>>(statsJson) ?? stats;
		}

		foreach (var contributor in contributors)
		{
			if (!stats.ContainsKey(contributor)) stats[contributor] = 0;
			stats[contributor] += (int)playtimeMinutes;
		}

		Directory.CreateDirectory(Path.GetDirectoryName(statsPath)!);
		File.WriteAllText(statsPath, JsonSerializer.Serialize(stats));
	}
}