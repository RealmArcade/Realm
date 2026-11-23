namespace Realm.Client.Services.Steam;

/// <summary>
///     The three Steam pages a map browser can send a player to. They are built here rather than pasted into each button so
///     the shape of a Steam Community address is stated once: a profile, the Workshop page listing everything one account
///     published for this game, and the page of one published item.
/// </summary>
public static class SteamCommunityLinks
{
	/// <summary>The Steam Community profile of one account.</summary>
	/// <param name="steamId">The account's SteamID64.</param>
	/// <returns>The profile's address, or an empty string when the id names nobody.</returns>
	public static string Profile(ulong steamId)
	{
		if (steamId == 0) return string.Empty;
		return $"https://steamcommunity.com/profiles/{steamId}";
	}

	/// <summary>
	///     The Workshop page listing one account's items for this game. The AppID is in the address because a SteamID is
	///     global: without it the page would offer the same person's items for every other game they publish for.
	/// </summary>
	/// <param name="steamId">The creator's SteamID64.</param>
	/// <param name="appId">The Steam application the page is read for.</param>
	/// <returns>The page's address, or an empty string when either id is missing.</returns>
	public static string CreatorWorkshop(ulong steamId, uint appId)
	{
		if (steamId == 0 || appId == 0) return string.Empty;
		return $"https://steamcommunity.com/profiles/{steamId}/myworkshopfiles/?appid={appId}";
	}

	/// <summary>
	///     The page of one published item, which is where the item's own description, screenshots, comments, votes and
	///     report button live. The game reads the same facts through the Steamworks API and owns none of them.
	/// </summary>
	/// <param name="publishedFileId">The Workshop item id.</param>
	/// <returns>The item page's address, or an empty string when there is no item.</returns>
	public static string MapPage(ulong publishedFileId)
	{
		if (publishedFileId == 0) return string.Empty;
		return $"https://steamcommunity.com/sharedfiles/filedetails/?id={publishedFileId}";
	}
}
