using System.Collections.Generic;

public class MapData
{
	public string MapId { get; set; }
	public string Title { get; set; }
	public string Version { get; set; } = "1.0.0";
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


	public static MapData FromDto(Realm.Shared.Distribution.DiscoveryMapDto dto, string? serverBaseUrl = null)
	{
		float rating = dto.RatingStars > 0 ? dto.RatingStars : (dto.AverageRating > 0 ? (float)dto.AverageRating : 5.0f);
		var (v5, v3, v1) = CalculateVotes(dto.TotalReviews, rating);
		string version = !string.IsNullOrWhiteSpace(dto.Version) ? dto.Version.Trim() : "1.0.0";

		var mapData = new MapData
		{
			MapId = !string.IsNullOrEmpty(dto.MapId) ? dto.MapId : $"{dto.Title}_{version}",
			Title = dto.Title,
			Version = version,
			Creator = dto.Creator,
			ThumbnailPath = GetThumbnailPath(dto),
			Description = !string.IsNullOrEmpty(dto.Description) ? dto.Description : "A custom map package published to the Realm network.",
			Screenshots = GetScreenshots(dto),
			Features = GetFeatures(dto),
			RatingStars = rating,
			Votes5Star = $"{v5:N0} Votes",
			Votes3Star = $"{v3:N0} Votes",
			Votes1Star = $"{v1:N0} Votes",
			AvgRating = $"{rating:F1} / 5.0",
			AvgPlaytime = GetAvgPlaytime(dto),
			PlayerCount = dto.GamesPlayed > 0 ? $"{dto.GamesPlayed:N0} Played" : "New Release",
			CompletionRate = dto.GamesPlayed > 0 ? "100%" : "N/A",
			FileSize = GetFileSize(dto),
			EngineVersion = !string.IsNullOrWhiteSpace(dto.EngineVersion) ? dto.EngineVersion : "Godot Realm Engine v1.0",
			MaxPlayers = !string.IsNullOrWhiteSpace(dto.MaxPlayers) ? dto.MaxPlayers : "8 Players",
			Genre = GetGenre(dto),
			Awards = dto.Awards != null && dto.Awards.Count > 0 ? dto.Awards.ToArray() : new string[]
			{
				"res://Assets/UI/gold_coin.png",
				"res://Assets/UI/battle_shield.png"
			}
		};

		mapData.AvailableVersions = new List<MapData> { mapData };
		return mapData;
	}

	private static string GetThumbnailPath(Realm.Shared.Distribution.DiscoveryMapDto dto)
	{
		if (!string.IsNullOrEmpty(dto.ThumbnailHash))
		{
			string? localCasPath = MapAssetManager.Storage.FindAssetFilePath(dto.ThumbnailHash);
			if (localCasPath != null && System.IO.File.Exists(localCasPath))
			{
				return localCasPath;
			}
		}
		
		return !string.IsNullOrEmpty(dto.ThumbnailUrl) ? dto.ThumbnailUrl : "";
	}

	private static string[] GetScreenshots(Realm.Shared.Distribution.DiscoveryMapDto dto)
	{
		var screenshots = new List<string>();
		
		if (dto.Screenshots != null && dto.Screenshots.Count > 0)
		{
			foreach (var s in dto.Screenshots)
			{
				string? localCas = MapAssetManager.Storage.FindAssetFilePath(s);
				if (localCas == null || !System.IO.File.Exists(localCas))
					continue;
					
				screenshots.Add(localCas);
			}
		}
		
		if (screenshots.Count == 0)
		{
			screenshots.Add("res://Assets/UI/moonlit_castle.png");
			screenshots.Add("res://Assets/UI/moonlit_forest.png");
			screenshots.Add("res://Assets/UI/forest_path.png");
		}
		
		return screenshots.ToArray();
	}

	private static string[] GetFeatures(Realm.Shared.Distribution.DiscoveryMapDto dto)
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

		int v5 = System.Math.Clamp((int)System.Math.Round(totalVotes * System.Math.Max(0.0, (rating - 3.0) / 2.0)), 0, totalVotes);
		int v1 = System.Math.Clamp((int)System.Math.Round(totalVotes * System.Math.Max(0.0, (3.0 - rating) / 2.0)), 0, totalVotes - v5);
		int v3 = totalVotes - v5 - v1;
		
		return (v5, v3, v1);
	}

	private static string GetAvgPlaytime(Realm.Shared.Distribution.DiscoveryMapDto dto)
	{
		if (dto.PlaytimeMinutes <= 0)
			return "N/A";
			
		if (dto.GamesPlayed > 0)
		{
			double avg = (double)dto.PlaytimeMinutes / dto.GamesPlayed;
			return $"{(int)System.Math.Max(1, System.Math.Round(avg))} min";
		}
		
		return $"{dto.PlaytimeMinutes} min";
	}

	private static string GetFileSize(Realm.Shared.Distribution.DiscoveryMapDto dto)
	{
		if (!string.IsNullOrWhiteSpace(dto.FileSizeFormatted))
			return dto.FileSizeFormatted;
			
		if (dto.TotalSizeBytes > 0)
			return $"{dto.TotalSizeBytes / (1024.0 * 1024.0):F1} MB";
			
		return "0 MB";
	}

	private static string GetGenre(Realm.Shared.Distribution.DiscoveryMapDto dto)
	{
		if (!string.IsNullOrWhiteSpace(dto.Genre))
			return dto.Genre;
			
		if (dto.Tags != null && dto.Tags.Count > 0)
			return dto.Tags[0];
			
		return "Custom Map";
	}
}
