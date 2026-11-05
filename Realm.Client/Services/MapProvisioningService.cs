using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

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

	public static async Task<DiscoveryMapDto?> FindLatestPublishedAsync(string mapName)
	{
		string normalizedMapName = NormalizeMapName(mapName);
		var distributionClient = new DistributionClient(ResolveRegistryServerUrl());
		List<DiscoveryMapDto> publishedMaps = await distributionClient.GetDiscoveryMapsAsync();
		return publishedMaps
			.Where(map => MatchesMapName(normalizedMapName, map.Title))
			.OrderByDescending(map => map.Version, Comparer<string>.Create(AssetIndexService.CompareVersions))
			.FirstOrDefault();
	}

	public static async Task<bool> DownloadLatestAsync(string mapName, Action<float>? progressCallback = null)
	{
		if (!MapAssetManager.Storage.CheckFreeDiskSpaceAcceptingDownloads())
		{
			return false;
		}

		DiscoveryMapDto? latestPublishedMap = await FindLatestPublishedAsync(mapName);
		if (latestPublishedMap == null)
		{
			return false;
		}

		var mapDistributionClient = new MapDistributionClient();
		bool downloaded = await mapDistributionClient.DownloadMapPackageFromRegistryAsync(latestPublishedMap.MapId, ResolveRegistryServerUrl(), progressCallback);
		if (!downloaded)
		{
			return false;
		}

		GameContext.AssetIndexService?.RescanAllDirectories();
		return true;
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

	private static string ResolveRegistryServerUrl()
	{
		return GodotObject.IsInstanceValid(GameContext.Lobby)
			? GameContext.Lobby.RegistryServerUrl
			: ServersConfigHelper.GetDefaultServerUrl();
	}
}
