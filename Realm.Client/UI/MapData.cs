using System;
using System.Collections.Generic;
using System.IO;
using Realm.Client.Network;
using Realm.Shared.Distribution;

namespace Realm.Client.UI;

public class MapData
{
	public string MapId { get; set; }
	public string Title { get; set; }
	public string Version { get; set; } = "1.0.0";
	public bool IsTestVersion { get; set; }
	public string ManifestHash { get; set; } = string.Empty;
	public List<MapData> AvailableVersions { get; set; } = new();
	public string Creator { get; set; }
	public string ThumbnailPath { get; set; }
	public string Description { get; set; }
	public string[] Screenshots { get; set; }
	public string[] Features { get; set; }


	public float RatingStars { get; set; }
	public string Votes5Star { get; set; }
	public string Votes3Star { get; set; }
	public string Votes1Star { get; set; }
	public string AvgRating { get; set; }


	public string AvgPlaytime { get; set; }
	public string PlayerCount { get; set; }
	public string CompletionRate { get; set; }


	public string FileSize { get; set; }
	public string EngineVersion { get; set; }
	public string MaxPlayers { get; set; }
	public string Genre { get; set; }


	public string[] Awards { get; set; }
	public List<string> Attribution_SourceMap { get; set; } = new();


	public static MapData FromDto(DiscoveryMapDto dto, string? serverBaseUrl = null)
	{
		float rating = GetCalculatedRating(dto);
		var (v5, v3, v1) = CalculateVotes(dto.TotalReviews, rating);
		string version = GetSafeVersion(dto);

		var mapData = new MapData
		{
			MapId = GetMapId(dto, version),
			Title = dto.Title,
			Version = version,
			IsTestVersion = dto.IsTestVersion,
			Creator = dto.Creator,
			ThumbnailPath = GetThumbnailPath(dto),
			Description = GetDescription(dto),
			Screenshots = GetScreenshots(dto),
			Features = GetFeatures(dto),
			RatingStars = rating,
			Votes5Star = $"{v5:N0} Votes",
			Votes3Star = $"{v3:N0} Votes",
			Votes1Star = $"{v1:N0} Votes",
			AvgRating = $"{rating:F1} / 5.0",
			AvgPlaytime = GetAvgPlaytime(dto),
			PlayerCount = GetPlayerCount(dto),
			CompletionRate = GetCompletionRate(dto),
			FileSize = GetFileSize(dto),
			EngineVersion = GetEngineVersion(dto),
			MaxPlayers = GetMaxPlayersSafe(dto),
			Genre = GetGenre(dto),
			Awards = GetAwardsFallback(dto),
			Attribution_SourceMap = dto.Attribution_SourceMap != null ? new List<string>(dto.Attribution_SourceMap) : new List<string>()
		};

		mapData.AvailableVersions = new List<MapData> { mapData };
		return mapData;
	}

	private static float GetCalculatedRating(DiscoveryMapDto dto)
	{
		if (dto.RatingStars > 0) return dto.RatingStars;
		if (dto.AverageRating > 0) return (float)dto.AverageRating;
		return 5.0f;
	}

	private static string GetSafeVersion(DiscoveryMapDto dto)
	{
		if (!string.IsNullOrWhiteSpace(dto.Version)) return dto.Version.Trim();
		return "1.0.0";
	}

	private static string GetMapId(DiscoveryMapDto dto, string version)
	{
		if (!string.IsNullOrEmpty(dto.MapId)) return dto.MapId;
		return $"{dto.Title}_{version}";
	}

	private static string GetDescription(DiscoveryMapDto dto)
	{
		if (!string.IsNullOrEmpty(dto.Description)) return dto.Description;
		return "A custom map package published to the Realm network.";
	}

	private static string GetPlayerCount(DiscoveryMapDto dto)
	{
		if (dto.GamesPlayed > 0) return $"{dto.GamesPlayed:N0} Played";
		return "New Release";
	}

	private static string GetCompletionRate(DiscoveryMapDto dto)
	{
		if (dto.GamesPlayed > 0) return "100%";
		return "N/A";
	}

	private static string GetEngineVersion(DiscoveryMapDto dto)
	{
		if (!string.IsNullOrWhiteSpace(dto.EngineVersion)) return dto.EngineVersion;
		return "Godot Realm Engine v1.0";
	}

	private static string GetMaxPlayersSafe(DiscoveryMapDto dto)
	{
		if (!string.IsNullOrWhiteSpace(dto.MaxPlayers)) return dto.MaxPlayers;
		return "8 Players";
	}

	private static string[] GetAwardsFallback(DiscoveryMapDto dto)
	{
		if (dto.Awards != null)
		{
			if (dto.Awards.Count > 0) return dto.Awards.ToArray();
		}

		return new string[]
		{
			"res://Assets/UI/gold_coin.webp",
			"res://Assets/UI/battle_shield.webp"
		};
	}

	private static string GetThumbnailPath(DiscoveryMapDto dto)
	{
		if (!string.IsNullOrEmpty(dto.ThumbnailHash))
		{
			string? localCasPath = MapAssetManager.Storage.FindAssetFilePath(dto.ThumbnailHash);
			if (localCasPath != null && File.Exists(localCasPath))
			{
				return localCasPath;
			}
		}

		return !string.IsNullOrEmpty(dto.ThumbnailUrl) ? dto.ThumbnailUrl : "";
	}

	private static string[] GetScreenshots(DiscoveryMapDto dto)
	{
		var screenshots = new List<string>();

		if (dto.Screenshots != null && dto.Screenshots.Count > 0)
		{
			foreach (var s in dto.Screenshots)
			{
				string? localCas = MapAssetManager.Storage.FindAssetFilePath(s);
				if (localCas == null || !File.Exists(localCas))
					continue;

				screenshots.Add(localCas);
			}
		}

		if (screenshots.Count == 0)
		{
			screenshots.Add("res://Assets/UI/moonlit_castle.webp");
			screenshots.Add("res://Assets/UI/moonlit_forest.webp");
			screenshots.Add("res://Assets/UI/forest_path.webp");
		}

		return screenshots.ToArray();
	}

	private static string[] GetFeatures(DiscoveryMapDto dto)
	{
		if (dto.Features != null && dto.Features.Count > 0)
			return dto.Features.ToArray();

		if (dto.Tags != null && dto.Tags.Count > 0)
			return dto.Tags.ToArray();

		return new string[] { "Custom Assets", "Verified Map", "Community Rated" };
	}

	private static (int v5, int v3, int v1) CalculateVotes(int totalVotes, float rating)
	{
		if (totalVotes <= 0)
			return (0, 0, 0);

		int v5 = Math.Clamp((int)Math.Round(totalVotes * Math.Max(0.0, (rating - 3.0) / 2.0)), 0, totalVotes);
		int v1 = Math.Clamp((int)Math.Round(totalVotes * Math.Max(0.0, (3.0 - rating) / 2.0)), 0, totalVotes - v5);
		int v3 = totalVotes - v5 - v1;

		return (v5, v3, v1);
	}

	private static string GetAvgPlaytime(DiscoveryMapDto dto)
	{
		if (dto.PlaytimeMinutes <= 0)
			return "N/A";

		if (dto.GamesPlayed > 0)
		{
			double avg = (double)dto.PlaytimeMinutes / dto.GamesPlayed;
			return $"{(int)Math.Max(1, Math.Round(avg))} min";
		}

		return $"{dto.PlaytimeMinutes} min";
	}

	private static string GetFileSize(DiscoveryMapDto dto)
	{
		if (!string.IsNullOrWhiteSpace(dto.FileSizeFormatted))
			return dto.FileSizeFormatted;

		if (dto.TotalSizeBytes > 0)
			return $"{dto.TotalSizeBytes / (1024.0 * 1024.0):F1} MB";

		return "0 MB";
	}

	private static string GetGenre(DiscoveryMapDto dto)
	{
		if (!string.IsNullOrWhiteSpace(dto.Genre))
			return dto.Genre;

		if (dto.Tags != null && dto.Tags.Count > 0)
			return dto.Tags[0];

		return "Custom Map";
	}
}