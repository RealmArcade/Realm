using System;
using System.Collections.Generic;
using System.IO;
using Realm.Client.Network;
using Realm.Client.Services.Steam;
using Steamworks;

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
	public string ThumbnailPath { get; set; } = null!;
	public string Description { get; set; } = null!;
	public string[] Screenshots { get; set; } = null!;
	public string[] Features { get; set; } = null!;


	public float RatingStars { get; set; }
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


	/// <summary>
	///     Builds the entry the map library shows from what Steam says about a Workshop item and what the item states
	///     about itself. Ratings are Steam's own up and down votes, and the creator is the Steam account that owns it.
	/// </summary>
	/// <param name="entry">The Workshop item.</param>
	/// <returns>The map the library, the details screen and the lobby list all read.</returns>
	public static MapData FromWorkshop(WorkshopMapEntry entry)
	{
		string version = string.IsNullOrWhiteSpace(entry.Identity.Version) ? "1.0.0" : entry.Identity.Version.Trim();
		string title = string.IsNullOrWhiteSpace(entry.Identity.MapName) ? entry.Title : entry.Identity.MapName;
		float rating = GetWorkshopRating(entry);
		int totalVotes = (int)(entry.VotesUp + entry.VotesDown);
		var (v5, v3, v1) = CalculateVotes(totalVotes, rating);

		var mapData = new MapData
		{
			MapId = entry.PublishedFileId.ToString(),
			Title = title,
			Version = version,
			IsTestVersion = false,
			Creator = SteamFriends.GetFriendPersonaName(new CSteamID(entry.AuthorSteamId)),
			ThumbnailPath = entry.PreviewUrl ?? string.Empty,
			Description = string.IsNullOrWhiteSpace(entry.Description) ? "A map published to the Steam Workshop." : entry.Description,
			Screenshots = GetScreenshots(),
			Features = GetFeatures(),
			RatingStars = rating,
			Votes5Star = $"{v5:N0} Votes",
			Votes3Star = $"{v3:N0} Votes",
			Votes1Star = $"{v1:N0} Votes",
			AvgRating = $"{rating:F1} / 5.0",
			AvgPlaytime = "N/A",
			PlayerCount = entry.VotesUp > 0 ? $"{entry.VotesUp:N0} Upvotes" : "New Release",
			CompletionRate = "N/A",
			FileSize = $"{entry.FileSizeBytes / (1024.0 * 1024.0):F1} MB",
			EngineVersion = entry.Identity.GameBuildNumber,
			MaxPlayers = "8 Players",
			Genre = "Custom Map",
			Awards = GetAwardsFallback(),
			Attribution_SourceMap = new List<string>()
		};

		mapData.AvailableVersions = new List<MapData> { mapData };
		return mapData;
	}

	private static float GetWorkshopRating(WorkshopMapEntry entry)
	{
		int totalVotes = (int)(entry.VotesUp + entry.VotesDown);
		if (totalVotes <= 0) return 5.0f;
		return Math.Clamp(5.0f * entry.VotesUp / totalVotes, 1.0f, 5.0f);
	}

	private static string[] GetAwardsFallback()
	{
		return new string[]
		{
			"res://Assets/UI/gold_coin.webp",
			"res://Assets/UI/battle_shield.webp"
		};
	}

	private static string[] GetScreenshots()
	{
		return new string[]
		{
			"res://Assets/UI/moonlit_castle.webp",
			"res://Assets/UI/moonlit_forest.webp",
			"res://Assets/UI/forest_path.webp"
		};
	}

	private static string[] GetFeatures()
	{
		return new string[] { "Steam Workshop", "Community Rated", "Custom Assets" };
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
}