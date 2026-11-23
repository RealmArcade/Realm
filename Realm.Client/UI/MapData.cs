using System;
using System.Collections.Generic;
using Realm.Client.Services;
using Realm.Client.Services.Steam;
using Realm.Shared;

namespace Realm.Client.UI;

/// <summary>
///     One map as the library, the browser and the details screen show it. Everything here comes from what Steam states
///     about the Workshop item that carries the map, or from the map's own published metadata. There is no Realm portal
///     behind it: no star rating, no written reviews, no awards and no engagement metrics, because Steam owns the votes and
///     the comments and the game only reads them.
/// </summary>
public class MapData
{
	/// <summary>The Workshop item id as text, which is what a lobby and the library key a published map by.</summary>
	public string MapId { get; set; } = null!;

	/// <summary>The Workshop item id, or zero for a map that was never published for the running AppID.</summary>
	public ulong WorkshopId { get; set; }

	/// <summary>The map's title, which is a label rather than an identity.</summary>
	public string Title { get; set; } = null!;

	/// <summary>The version the author published.</summary>
	public string Version { get; set; } = "1.0.0";

	/// <summary>Whether this entry is a version the author is still testing rather than the published one.</summary>
	public bool IsTestVersion { get; set; }

	/// <summary>The hash of the packaged map content, which is the build of the map a match actually plays.</summary>
	public string ManifestHash { get; set; } = string.Empty;

	/// <summary>Other versions of the same map this screen knows about.</summary>
	public List<MapData> AvailableVersions { get; set; } = new();

	/// <summary>The Steam persona name of the account that owns the item, which is the author of record.</summary>
	public string Creator { get; set; } = null!;

	/// <summary>The SteamID64 of the author of record, or zero when the item states no owner.</summary>
	public ulong CreatorSteamId { get; set; }

	/// <summary>The Workshop preview image Steam hosts for the item.</summary>
	public string ThumbnailPath { get; set; } = string.Empty;

	/// <summary>The item's description, as its author wrote it on Steam.</summary>
	public string Description { get; set; } = null!;

	/// <summary>The one preview image the details screen shows, which is Steam's thumbnail.</summary>
	public string[] PreviewImages { get; set; } = Array.Empty<string>();

	/// <summary>Up-votes Steam has recorded for the item.</summary>
	public uint VotesUp { get; set; }

	/// <summary>Down-votes Steam has recorded for the item.</summary>
	public uint VotesDown { get; set; }

	/// <summary>The share of votes that are positive, or zero when the item has no votes.</summary>
	public int PositivePercent { get; set; }

	/// <summary>When Steam last saw the item change, or null when it states no time.</summary>
	public DateTimeOffset? UpdatedOn { get; set; }

	/// <summary>How much Steam vouches for this map, which decides the badge and the join confirmation.</summary>
	public MapTrustState Trust { get; set; } = MapTrustState.Trusted;

	/// <summary>Whether this map exists only on this machine, because it was never published.</summary>
	public bool IsLocalMap { get; set; }

	/// <summary>The item's size, formatted for the details screen.</summary>
	public string FileSize { get; set; } = null!;

	/// <summary>The game build the map was authored against.</summary>
	public string EngineVersion { get; set; } = null!;

	/// <summary>The author's stated maximum player count.</summary>
	public string MaxPlayers { get; set; } = null!;

	/// <summary>The category the map is listed under.</summary>
	public string Genre { get; set; } = null!;

	/// <summary>The maps whose content this map was built from, for the attribution panel.</summary>
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
		long totalVotes = entry.VotesUp + entry.VotesDown;

		var mapData = new MapData
		{
			MapId = entry.PublishedFileId.ToString(),
			WorkshopId = entry.PublishedFileId,
			Title = title,
			Version = version,
			IsTestVersion = false,
			ManifestHash = entry.Identity.ContentHash,
			Creator = GameContext.Steam.GetPersonaName(entry.AuthorSteamId),
			CreatorSteamId = entry.AuthorSteamId,
			ThumbnailPath = entry.PreviewUrl ?? string.Empty,
			Description = string.IsNullOrWhiteSpace(entry.Description) ? string.Empty : entry.Description,
			PreviewImages = string.IsNullOrEmpty(entry.PreviewUrl) ? Array.Empty<string>() : new[] { entry.PreviewUrl },
			VotesUp = entry.VotesUp,
			VotesDown = entry.VotesDown,
			PositivePercent = totalVotes > 0 ? (int)(entry.VotesUp * 100 / totalVotes) : 0,
			UpdatedOn = entry.TimeUpdated > 0 ? DateTimeOffset.FromUnixTimeSeconds(entry.TimeUpdated) : null,
			Trust = MapTrustPolicy.Evaluate(entry.PublishedFileId, entry.AcceptedForUse, entry.VotesUp, entry.VotesDown, entry.TimeUpdated),
			IsLocalMap = false,
			FileSize = $"{entry.FileSizeBytes / (1024.0 * 1024.0):F1} MB",
			EngineVersion = entry.Identity.GameBuildNumber,
			MaxPlayers = string.Empty,
			Genre = "Custom Map",
			Attribution_SourceMap = new List<string>()
		};

		mapData.AvailableVersions = new List<MapData> { mapData };
		return mapData;
	}

	/// <summary>
	///     Builds the entry for a map this machine has on disk but Steam has no item for. Nothing on the running AppID
	///     vouches for it, so it carries the local badge and the untrusted mark, and a lobby that hosts it says so.
	/// </summary>
	/// <param name="title">The map's title as its own metadata states it.</param>
	/// <param name="version">The version the author published.</param>
	/// <param name="authorSteamId">The SteamID64 the map credits, or zero when it credits nobody.</param>
	/// <returns>The map the library and the lobby list read.</returns>
	public static MapData FromLocal(string title, string version, ulong authorSteamId)
	{
		return new MapData
		{
			MapId = title,
			WorkshopId = 0,
			Title = title,
			Version = string.IsNullOrWhiteSpace(version) ? "1.0.0" : version,
			IsTestVersion = false,
			Creator = authorSteamId == 0 ? string.Empty : GameContext.Steam.GetPersonaName(authorSteamId),
			CreatorSteamId = authorSteamId,
			ThumbnailPath = string.Empty,
			Description = string.Empty,
			PreviewImages = Array.Empty<string>(),
			Trust = MapTrustPolicy.EvaluateUnpublished(),
			IsLocalMap = true,
			FileSize = string.Empty,
			EngineVersion = RealmVersion.GameBuildNumber,
			MaxPlayers = string.Empty,
			Genre = "Local Map",
			Attribution_SourceMap = new List<string>()
		};
	}
}
