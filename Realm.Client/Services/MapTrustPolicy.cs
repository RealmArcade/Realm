using System;
using System.Collections.Generic;
using System.IO;
using GodotFileAccess = Godot.FileAccess;
using System.Text.Json;
using Godot;
using Realm.Client.Services.Steam;

namespace Realm.Client.Services;

/// <summary>How much Steam vouches for a map, and therefore what a player is told before they join it.</summary>
public enum MapTrustLevel
{
	/// <summary>The developer features this map, so it is trusted whatever its vote count is.</summary>
	Verified,

	/// <summary>Steam's own votes and update time vouch for the map.</summary>
	Trusted,

	/// <summary>Nothing vouches for the map yet, or the map is not published at all.</summary>
	Untrusted
}

/// <summary>Why a map is not trusted, which is the sentence a joining player reads.</summary>
public enum MapTrustReason
{
	None,
	NotPublished,
	TooNew,
	PoorlyRated,
	Unmaintained
}

/// <summary>One map's trust mark: the level, and the reason when it is not trusted.</summary>
/// <param name="Level">How much Steam vouches for the map.</param>
/// <param name="Reason">Why it is untrusted, or <see cref="MapTrustReason.None" /> when it is.</param>
public readonly record struct MapTrustState(MapTrustLevel Level, MapTrustReason Reason)
{
	/// <summary>A map this build may show without a warning.</summary>
	public static MapTrustState Trusted => new(MapTrustLevel.Trusted, MapTrustReason.None);

	/// <summary>A map nothing on Steam vouches for.</summary>
	/// <param name="reason">Why nothing vouches for it.</param>
	/// <returns>The mark the browser and the join show.</returns>
	public static MapTrustState Untrusted(MapTrustReason reason) => new(MapTrustLevel.Untrusted, reason);

	public bool IsTrusted => Level != MapTrustLevel.Untrusted;
}

/// <summary>
///     The one rule that decides whether a map carries a trust mark in this build. It reads only what Steam states about
///     an item - its votes and when it was last updated - plus the developer's own featured list, which is a curation
///     choice rather than a voting threshold. Nothing is hidden by default: a map that fails this rule is still listed and
///     still playable, and what the rule buys is one confirmation that names the reason before a player enters its lobby.
/// </summary>
public static class MapTrustPolicy
{
	/// <summary>How many votes a map needs before its score is read as a judgement rather than as a first impression.</summary>
	public const int MinimumVotes = 5;

	/// <summary>The positive share a map needs to be trusted once it has enough votes.</summary>
	public const int MinimumPositivePercent = 70;

	/// <summary>How long a map may go without an update before it is called unmaintained.</summary>
	public static readonly TimeSpan UnmaintainedAfter = TimeSpan.FromDays(730);

	/// <summary>
	///     Where the developer's featured items are listed. It is the fallback for a Workshop collection: a collection is
	///     still the preferred way to curate, but a build can always ship a list, and a map on it is trusted regardless of
	///     how recently it was published or how many votes it has collected. The file is read once; a build without it
	///     features nothing.
	/// </summary>
	public const string FeaturedItemListPath = "res://config/featured_workshop_items.json";

	private static HashSet<ulong>? _featuredItemIds;

	/// <summary>Whether the developer features a Workshop item, which overrides the vote and update checks.</summary>
	/// <param name="publishedFileId">The Workshop item id.</param>
	/// <returns>Whether the item is on the featured list.</returns>
	public static bool IsFeatured(ulong publishedFileId)
	{
		return publishedFileId != 0 && FeaturedItemIds.Contains(publishedFileId);
	}

	/// <summary>
	///     Judges one map from what Steam states about its item and whether this screen even has an item for it.
	/// </summary>
	/// <param name="publishedFileId">The map's Workshop item for the running AppID, or zero when it has none.</param>
	/// <param name="votesUp">Up-votes Steam has recorded for that item.</param>
	/// <param name="votesDown">Down-votes Steam has recorded for that item.</param>
	/// <param name="timeUpdatedUnixSeconds">When Steam last saw the item change, in Unix seconds.</param>
	/// <returns>The mark the browser, the details screen and the join all read.</returns>
	public static MapTrustState Evaluate(ulong publishedFileId, uint votesUp, uint votesDown, uint timeUpdatedUnixSeconds)
	{
		if (publishedFileId == 0) return MapTrustState.Untrusted(MapTrustReason.NotPublished);

		MapTrustState state = EvaluateVotesAndAge(votesUp, votesDown, timeUpdatedUnixSeconds);

		if (state.Level == MapTrustLevel.Untrusted && IsFeatured(publishedFileId))
		{
			return new MapTrustState(MapTrustLevel.Verified, MapTrustReason.None);
		}

		return state;
	}

	/// <summary>
	///     Judges a map this screen has no Workshop item for, which is every local map and every map whose item belongs to
	///     another AppID. Nothing on the current AppID vouches for it, so it is untrusted and says why.
	/// </summary>
	/// <returns>The mark of a map that is not published for this AppID.</returns>
	public static MapTrustState EvaluateUnpublished() => MapTrustState.Untrusted(MapTrustReason.NotPublished);

	private static MapTrustState EvaluateVotesAndAge(uint votesUp, uint votesDown, uint timeUpdatedUnixSeconds)
	{
		long totalVotes = votesUp + votesDown;
		if (totalVotes < MinimumVotes) return MapTrustState.Untrusted(MapTrustReason.TooNew);

		int positivePercent = (int)(votesUp * 100 / Math.Max(1, totalVotes));
		if (positivePercent < MinimumPositivePercent) return MapTrustState.Untrusted(MapTrustReason.PoorlyRated);

		if (timeUpdatedUnixSeconds > 0)
		{
			var updated = DateTimeOffset.FromUnixTimeSeconds(timeUpdatedUnixSeconds);
			if (DateTimeOffset.UtcNow - updated > UnmaintainedAfter) return MapTrustState.Untrusted(MapTrustReason.Unmaintained);
		}

		return MapTrustState.Trusted;
	}

	private static HashSet<ulong> FeaturedItemIds => _featuredItemIds ??= LoadFeaturedItemIds();

	private static HashSet<ulong> LoadFeaturedItemIds()
	{
		var ids = new HashSet<ulong>();

		try
		{
			if (!GodotFileAccess.FileExists(FeaturedItemListPath)) return ids;

			using GodotFileAccess? file = GodotFileAccess.Open(FeaturedItemListPath, GodotFileAccess.ModeFlags.Read);
			if (file == null) return ids;

			string json = file.GetAsText();
			ulong[]? parsed = JsonSerializer.Deserialize<ulong[]>(json);
			if (parsed == null) return ids;

			foreach (ulong id in parsed)
			{
				if (id != 0) ids.Add(id);
			}
		}
		catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
		{
			GameLog.LogToConsole($"[Workshop] the featured item list could not be read, so no map is featured by it: {exception.Message}");
		}

		return ids;
	}

	/// <summary>The sentence a player reads for one untrusted map, naming the reason rather than calling the map unsafe.</summary>
	/// <param name="reason">Why the map is not trusted.</param>
	/// <returns>The translated explanation, or an empty string when there is nothing to explain.</returns>
	public static string DescribeReason(MapTrustReason reason)
	{
		return reason switch
		{
			MapTrustReason.NotPublished => TranslationServer.Translate("This map is not published on the Steam Workshop, so other players cannot download it."),
			MapTrustReason.TooNew => TranslationServer.Translate("This map is too new to have collected votes yet."),
			MapTrustReason.PoorlyRated => TranslationServer.Translate("This map is rated below the trusted threshold by Steam Workshop votes."),
			MapTrustReason.Unmaintained => TranslationServer.Translate("This map has not been updated in a long time."),
			_ => string.Empty
		};
	}

	/// <summary>The short label a card or a lobby row draws beside the map's name.</summary>
	/// <param name="trust">The map's mark.</param>
	/// <returns>The translated badge text, or an empty string for a trusted map.</returns>
	public static string DescribeBadge(MapTrustState trust)
	{
		return trust.Level switch
		{
			MapTrustLevel.Verified => TranslationServer.Translate("Verified"),
			MapTrustLevel.Untrusted => TranslationServer.Translate("Untrusted"),
			_ => string.Empty
		};
	}
}
