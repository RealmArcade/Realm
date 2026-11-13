using System;
using System.Collections.Generic;
using System.IO;
using Realm.Client.Network;
using Realm.Shared.Distribution;

namespace Realm.Client.UI;

public class MapData
{
	public string MapId { get; set; } = null!;
	public string Title { get; set; } = null!;
	public string Version { get; set; } = "1.0.0";
	public bool IsTestVersion { get; set; }
	public string ManifestHash { get; set; } = string.Empty;
	public List<MapData> AvailableVersions { get; set; } = new();
	public string Creator { get; set; } = null!;
	public bool IsGreenlit { get; set; }
	public string ThumbnailHash { get; set; } = string.Empty;
	public string ThumbnailPath { get; set; } = null!;
	public string Description { get; set; } = null!;
	public string[] ScreenshotHashes { get; set; } = Array.Empty<string>();
	public string[] Screenshots { get; set; } = null!;
	public string[] Features { get; set; } = null!;

	public float RatingStars { get; set; }
	public int TotalReviews { get; set; }
	public int VerifiedGoodReviews { get; set; }
	public int GamesPlayed { get; set; }
	public int PlaytimeMinutes { get; set; }
	public long TotalSizeBytes { get; set; }
	public string Votes5Star { get; set; } = null!;
	public string Votes3Star { get; set; } = null!;
	public string Votes1Star { get; set; } = null!;
	public string AvgRating { get; set; } = null!;

	public string AvgPlaytime { get; set; } = null!;
	public string PlayerCount { get; set; } = null!;
	public string CompletionRate { get; set; } = null!;

	public string FileSize { get; set; } = null!;
	public string EngineVersion { get; set; } = null!;
	public string MaxPlayers { get; set; } = null!;
	public string Genre { get; set; } = null!;

	public string[] Awards { get; set; } = null!;
	public List<string> Attribution_SourceMap { get; set; } = new();

	public static MapData FromDto(DiscoveryMapDto dto, string? serverBaseUrl = null)
	{
		float rating = GetCalculatedRating(dto);
		string version = GetSafeVersion(dto);

		var mapData = new MapData
		{
			MapId = GetMapId(dto, version),
			Title = dto.Title ?? string.Empty,
			Version = version,
			IsTestVersion = dto.IsTestVersion,
			IsGreenlit = dto.IsGreenlit,
			Creator = !string.IsNullOrWhiteSpace(dto.Creator) ? dto.Creator.Trim() : "Unknown",
			ThumbnailHash = dto.ThumbnailHash ?? string.Empty,
			ThumbnailPath = GetThumbnailPath(dto),
			Description = dto.Description?.Trim() ?? string.Empty,
			ScreenshotHashes = dto.Screenshots != null ? dto.Screenshots.ToArray() : Array.Empty<string>(),
			Screenshots = GetScreenshots(dto),
			Features = GetFeatures(dto),
			RatingStars = rating,
			TotalReviews = dto.TotalReviews,
			VerifiedGoodReviews = dto.VerifiedGoodReviews,
			GamesPlayed = dto.GamesPlayed,
			PlaytimeMinutes = dto.PlaytimeMinutes,
			TotalSizeBytes = dto.TotalSizeBytes,
			Votes5Star = dto.TotalReviews > 0 ? $"{dto.TotalReviews:N0}" : "0",
			Votes3Star = dto.VerifiedGoodReviews > 0 ? $"{dto.VerifiedGoodReviews:N0}" : "0",
			Votes1Star = "0",
			AvgRating = dto.TotalReviews > 0 ? $"{rating:F1} / 5.0" : string.Empty,
			AvgPlaytime = GetAvgPlaytime(dto),
			PlayerCount = GetGamesPlayedLabel(dto),
			CompletionRate = dto.TotalReviews > 0 ? $"{dto.VerifiedGoodReviews:N0} / {dto.TotalReviews:N0}" : string.Empty,
			FileSize = GetFileSize(dto),
			EngineVersion = dto.EngineVersion?.Trim() ?? string.Empty,
			MaxPlayers = dto.MaxPlayers?.Trim() ?? string.Empty,
			Genre = GetGenre(dto),
			Awards = dto.Awards != null ? dto.Awards.ToArray() : Array.Empty<string>(),
			Attribution_SourceMap = dto.Attribution_SourceMap != null ? new List<string>(dto.Attribution_SourceMap) : new List<string>()
		};

		mapData.AvailableVersions = new List<MapData> { mapData };
		return mapData;
	}

	private static float GetCalculatedRating(DiscoveryMapDto dto)
	{
		if (dto.TotalReviews <= 0)
		{
			return 0f;
		}

		if (dto.RatingStars > 0) return dto.RatingStars;
		if (dto.AverageRating > 0) return (float)dto.AverageRating;
		return 0f;
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

	private static string GetGamesPlayedLabel(DiscoveryMapDto dto)
	{
		return dto.GamesPlayed > 0 ? $"{dto.GamesPlayed:N0}" : "0";
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

		return !string.IsNullOrEmpty(dto.ThumbnailUrl) ? dto.ThumbnailUrl : string.Empty;
	}

	private static string[] GetScreenshots(DiscoveryMapDto dto)
	{
		var screenshots = new List<string>();
		if (dto.Screenshots != null && dto.Screenshots.Count > 0)
		{
			foreach (var s in dto.Screenshots)
			{
				string? localCas = MapAssetManager.Storage.FindAssetFilePath(s);
				if (localCas != null && File.Exists(localCas))
				{
					screenshots.Add(localCas);
				}
			}
		}

		return screenshots.ToArray();
	}

	private static string[] GetFeatures(DiscoveryMapDto dto)
	{
		if (dto.Features != null && dto.Features.Count > 0)
			return dto.Features.ToArray();

		if (dto.Tags != null && dto.Tags.Count > 0)
			return dto.Tags.ToArray();

		return Array.Empty<string>();
	}

	private static string GetAvgPlaytime(DiscoveryMapDto dto)
	{
		if (dto.PlaytimeMinutes <= 0)
		{
			return string.Empty;
		}

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
