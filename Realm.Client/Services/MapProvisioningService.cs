using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Client.Services.Steam;
using Realm.Client.UI;
using FileAccess = Godot.FileAccess;

namespace Realm.Client.Services;

/// <summary>
///     Finds a map on this machine, or on the Workshop when it is not here yet. A map is installed when a downloaded
///     package or a Workshop subscription carries it, and it is published by its Workshop item rather than by a Realm
///     server.
/// </summary>
public static class MapProvisioningService
{
	public static bool IsInstalled(string mapName)
	{
		if (!string.IsNullOrEmpty(GetInstalledVersion(mapName))) return true;

		string normalizedMapName = NormalizeMapName(mapName);
		return GameContext.MapStorageService.GetMapsAvailableForImport()
			.Any(map => MatchesMapName(normalizedMapName, map.Title));
	}

	public static string? GetInstalledVersion(string mapName)
	{
		string normalizedMapName = NormalizeMapName(mapName);
		IReadOnlyList<IndexedMapPackage> packages = GameContext.AssetIndexService?.GetDownloadedMapPackages() ?? Array.Empty<IndexedMapPackage>();
		return packages
			.Where(package => MatchesMapName(normalizedMapName, package.MapName))
			.OrderByDescending(package => package.MapVersion, Comparer<string>.Create(AssetIndexService.CompareVersions))
			.Select(package => package.MapVersion)
			.FirstOrDefault();
	}

	/// <summary>
	///     Asks the Workshop for the newest published item carrying a map, which is what a lobby resolves its map name
	///     through before it can offer a subscription.
	/// </summary>
	/// <param name="mapName">The map's folder name.</param>
	/// <returns>The newest item, or null when nothing published states that map.</returns>
	public static Task<WorkshopMapEntry?> FindLatestPublishedAsync(string mapName)
	{
		return SteamWorkshopService.FindByMapNameAsync(mapName, CancellationToken.None);
	}

	/// <summary>
	///     Subscribes to the newest published item for a map and waits for Steam to install it.
	/// </summary>
	/// <param name="mapName">The map's folder name.</param>
	/// <param name="progressCallback">How much of the download Steam has taken.</param>
	/// <returns>True when the map is on this machine afterwards.</returns>
	public static async Task<bool> DownloadLatestAsync(string mapName, Action<float>? progressCallback = null)
	{
		if (!MapAssetManager.Storage.CheckFreeDiskSpaceAcceptingDownloads())
		{
			return false;
		}

		WorkshopMapEntry? latestPublishedMap = await FindLatestPublishedAsync(mapName);
		if (latestPublishedMap == null)
		{
			return false;
		}

		bool downloaded = await SteamWorkshopService.InstallAsync(latestPublishedMap.Value.PublishedFileId, progressCallback, CancellationToken.None);
		if (!downloaded)
		{
			return false;
		}

		GameContext.AssetIndexService?.RescanAllDirectories();
		return true;
	}

	/// <summary>
	///     Whether one version of a map is already on this machine: a manifest under the map's folder in one of the
	///     layouts the details screen reads, or a package the asset manager has downloaded under either its id or title.
	/// </summary>
	public static bool IsLocalCopyPresent(MapData map)
	{
		if (map == null) return false;

		string version = !string.IsNullOrWhiteSpace(map.Version) ? map.Version.Trim() : "1.0.0";

		if (HasLocalManifestFile(map, version)) return true;
		if (IsDownloadedViaAssetManager(map, version)) return true;

		return false;
	}

	private static bool HasLocalManifestFile(MapData map, string version)
	{
		if (FileAccess.FileExists($"res://Maps/{map.Title}/{version}/manifest.json")) return true;
		if (FileAccess.FileExists($"res://Maps/{map.MapId}/manifest.json")) return true;

		return FileAccess.FileExists($"res://Maps/{map.Title}/manifest.json") && CheckManifestByTitle(map.Title, version);
	}

	private static bool IsDownloadedViaAssetManager(MapData map, string version)
	{
		if (!string.IsNullOrWhiteSpace(map.MapId) && MapAssetManager.IsMapDownloaded(map.MapId)) return true;
		return !string.IsNullOrWhiteSpace(map.Title) && MapAssetManager.IsMapDownloaded(map.Title, version);
	}

	private static bool CheckManifestByTitle(string title, string version)
	{
		try
		{
			var manifest = MapManifest.LoadFromJson(FileAccess.GetFileAsString($"res://Maps/{title}/manifest.json"));
			return manifest != null && (string.Equals(manifest.Version, version, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(manifest.Version));
		}
		catch
		{
			return false;
		}
	}

	private static bool MatchesMapName(string normalizedMapName, string? mapName)
	{
		if (string.IsNullOrWhiteSpace(mapName)) return false;

		return string.Equals(NormalizeMapName(mapName), normalizedMapName, StringComparison.OrdinalIgnoreCase);
	}

	public static string NormalizeMapName(string mapName)
	{
		return new string(mapName.Where(character => !char.IsWhiteSpace(character) && character != '_').ToArray());
	}
}
