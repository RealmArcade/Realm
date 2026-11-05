using System;
using System.Threading.Tasks;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

public static class AssetPackService
{
	public const string AssetPackMapName = "ASSET_PACK";

	public static bool IsInstalled()
	{
		return MapProvisioningService.IsInstalled(AssetPackMapName);
	}

	public static Task<DiscoveryMapDto?> FindLatestPublishedAsync()
	{
		return MapProvisioningService.FindLatestPublishedAsync(AssetPackMapName);
	}

	public static Task<bool> DownloadLatestAsync(Action<float>? progressCallback = null)
	{
		return MapProvisioningService.DownloadLatestAsync(AssetPackMapName, progressCallback);
	}
}
