using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

public static class AssetPackService
{
	public const string AssetPackMapName = "ASSET_PACK";

	private static readonly string NormalizedAssetPackMapName = NormalizeMapName(AssetPackMapName);

	public static bool IsInstalled()
	{
		if (!string.IsNullOrEmpty(GetInstalledVersion())) return true;

		return HasArchiveManifest();
	}

	public static string? GetInstalledVersion()
	{
		IReadOnlyList<IndexedMapPackage> packages = GameContext.AssetIndexService?.GetDownloadedMapPackages() ?? Array.Empty<IndexedMapPackage>();
		return packages
			.Where(package => MatchesAssetPackName(package.MapName))
			.OrderByDescending(package => package.MapVersion, Comparer<string>.Create(AssetIndexService.CompareVersions))
			.Select(package => package.MapVersion)
			.FirstOrDefault();
	}

	public static async Task<DiscoveryMapDto?> FindLatestPublishedAsync()
	{
		var distributionClient = new DistributionClient(ResolveRegistryServerUrl());
		List<DiscoveryMapDto> publishedMaps = await distributionClient.GetDiscoveryMapsAsync();
		return publishedMaps
			.Where(map => MatchesAssetPackName(map.Title))
			.OrderByDescending(map => map.Version, Comparer<string>.Create(AssetIndexService.CompareVersions))
			.FirstOrDefault();
	}

	public static async Task<bool> DownloadLatestAsync(Action<float>? progressCallback = null)
	{
		if (!MapAssetManager.Storage.CheckFreeDiskSpaceAcceptingDownloads())
		{
			return false;
		}

		DiscoveryMapDto? latestPublishedPack = await FindLatestPublishedAsync();
		if (latestPublishedPack == null)
		{
			return false;
		}

		string registryServerUrl = ResolveRegistryServerUrl();
		var mapDistributionClient = new MapDistributionClient();
		bool downloaded = await mapDistributionClient.DownloadMapPackageFromRegistryAsync(latestPublishedPack.MapId, registryServerUrl, progressCallback);
		if (!downloaded)
		{
			return false;
		}

		GameContext.AssetIndexService?.RescanAllDirectories();
		return true;
	}

	private static bool HasArchiveManifest()
	{
		try
		{
			string? manifestPath = MapAssetManager.FindManifestPath(AssetPackMapName);
			return !string.IsNullOrEmpty(manifestPath) && File.Exists(manifestPath);
		}
		catch
		{
			return false;
		}
	}

	private static bool MatchesAssetPackName(string? mapName)
	{
		if (string.IsNullOrWhiteSpace(mapName)) return false;

		return string.Equals(NormalizeMapName(mapName), NormalizedAssetPackMapName, StringComparison.OrdinalIgnoreCase);
	}

	private static string NormalizeMapName(string mapName)
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
