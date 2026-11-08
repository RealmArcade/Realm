using System;
using System.Collections.Generic;
using Realm.Client.Network;
using Realm.Client.UI;
using Realm.Shared;

namespace Realm.Client.Services;

/// <summary>
///     Chooses which installed map a host lobby opens with, and whether that map was built for this game build.
///     The rules are here rather than in the lobby screen because they are about maps, not about controls.
/// </summary>
public static class MapSelectionService
{
	public static int ResolvePreferredMapIndex(IReadOnlyList<MapBriefingDetails> maps, string? requestedMapName)
	{
		int requestedIndex = IndexOfMap(maps, requestedMapName);
		if (requestedIndex >= 0) return requestedIndex;

		if (BuildFlags.IS_AUTOBATTLE_FESTIVAL_ACTIVE)
		{
			int festivalIndex = IndexOfMap(maps, BuildFlags.FESTIVAL_FEATURED_MAP_NAME);
			if (festivalIndex >= 0) return festivalIndex;
		}

		int lastHostedIndex = IndexOfMap(maps, GameSettings.LastHostedMapName);
		if (lastHostedIndex >= 0) return lastHostedIndex;

		for (int i = 0; i < maps.Count; i++)
		{
			if (!IsAssetPackMap(maps[i])) return i;
		}

		return 0;
	}

	public static int IndexOfMap(IReadOnlyList<MapBriefingDetails> maps, string? mapName)
	{
		if (string.IsNullOrWhiteSpace(mapName)) return -1;

		string normalizedMapName = NormalizeMapName(mapName);

		for (int i = 0; i < maps.Count; i++)
		{
			var map = maps[i];

			if (string.Equals(map.PathName, mapName, StringComparison.OrdinalIgnoreCase)) return i;
			if (string.Equals(map.DisplayName, mapName, StringComparison.OrdinalIgnoreCase)) return i;
			if (string.Equals(NormalizeMapName(map.PathName), normalizedMapName, StringComparison.Ordinal)) return i;
			if (string.Equals(NormalizeMapName(map.DisplayName), normalizedMapName, StringComparison.Ordinal)) return i;
		}

		return -1;
	}

	public static bool IsAssetPackMap(MapBriefingDetails map)
	{
		if (string.Equals((map.MapType ?? string.Empty).Trim(), MapTagCatalog.AssetPackTag, StringComparison.OrdinalIgnoreCase)) return true;
		if (map.Tags == null) return false;

		foreach (string mapTag in map.Tags)
		{
			if (string.IsNullOrWhiteSpace(mapTag)) continue;
			if (string.Equals(mapTag.Trim(), MapTagCatalog.AssetPackTag, StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
	}

	public static bool IsBuildMismatch(string mapBuildNumber)
	{
		return !string.IsNullOrEmpty(mapBuildNumber) &&
		       !string.Equals(mapBuildNumber, RealmVersion.GameBuildNumber, StringComparison.OrdinalIgnoreCase);
	}

	private static string NormalizeMapName(string mapName)
	{
		return mapName.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).Trim().ToLowerInvariant();
	}
}
