using System;
using System.Threading.Tasks;
using Realm.Client.Services.Steam;

namespace Realm.Client.Services;

/// <summary>
///     The editor's shared art assets, which are published as their own Workshop item so a map author downloads them
///     once instead of carrying them in every map.
/// </summary>
public static class AssetPackService
{
	public const string AssetPackMapName = "ASSET_PACK";

	public static bool IsInstalled()
	{
		return MapProvisioningService.IsInstalled(AssetPackMapName);
	}

	public static Task<WorkshopMapEntry?> FindLatestPublishedAsync()
	{
		return MapProvisioningService.FindLatestPublishedAsync(AssetPackMapName);
	}

	public static Task<bool> DownloadLatestAsync(Action<float>? progressCallback = null)
	{
		return MapProvisioningService.DownloadLatestAsync(AssetPackMapName, progressCallback);
	}
}
