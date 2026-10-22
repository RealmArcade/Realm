using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

public class MapCatalogService
{
	private static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromMinutes(5);
	private const string BetaTestingPrefix = "[Beta-Testing]";

	private readonly object _gate = new();
	private readonly HashSet<string> _downloadableMapNames = new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _refreshLock = new(1, 1);

	private DateTime _lastRefreshUtc = DateTime.MinValue;
	private bool _hasCatalog;

	public bool HasCatalog
	{
		get
		{
			lock (_gate) return _hasCatalog;
		}
	}

	public async Task RefreshAsync(string? registryServerUrl, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(registryServerUrl)) return;
		if (!ShouldRefresh()) return;

		await _refreshLock.WaitAsync(cancellationToken);
		try
		{
			if (!ShouldRefresh()) return;

			var distributionClient = new DistributionClient(registryServerUrl);
			List<DiscoveryMapDto> discoveryMaps = await distributionClient.GetDiscoveryMapsAsync(cancellationToken);
			HashSet<string> downloadableNames = CollectDownloadableNames(discoveryMaps);

			lock (_gate)
			{
				_downloadableMapNames.Clear();
				_downloadableMapNames.UnionWith(downloadableNames);
				_hasCatalog = discoveryMaps.Count > 0;
				_lastRefreshUtc = DateTime.UtcNow;
			}
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[MapCatalogService] Failed to refresh the discovery catalog: {exception.Message}");
		}
		finally
		{
			_refreshLock.Release();
		}
	}

	public bool IsAvailableForDownload(string? mapName)
	{
		string normalizedName = NormalizeMapName(mapName);
		if (normalizedName.Length == 0) return false;

		lock (_gate)
		{
			if (!_hasCatalog) return true;
			return _downloadableMapNames.Contains(normalizedName);
		}
	}

	public static string NormalizeMapName(string? mapName)
	{
		if (string.IsNullOrWhiteSpace(mapName)) return string.Empty;

		string normalized = mapName.Trim();
		if (normalized.StartsWith(BetaTestingPrefix, StringComparison.OrdinalIgnoreCase))
		{
			normalized = normalized[BetaTestingPrefix.Length..].Trim();
		}

		return normalized.ToLowerInvariant();
	}

	private static HashSet<string> CollectDownloadableNames(List<DiscoveryMapDto> discoveryMaps)
	{
		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var map in discoveryMaps)
		{
			if (!map.IsGreenlit) continue;
			if (map.IsTestVersion) continue;

			AddName(names, map.Title);
			AddName(names, map.MapId);
		}

		return names;
	}

	private static void AddName(HashSet<string> names, string? name)
	{
		string normalized = NormalizeMapName(name);
		if (normalized.Length > 0) names.Add(normalized);
	}

	private bool ShouldRefresh()
	{
		lock (_gate)
		{
			return !_hasCatalog || DateTime.UtcNow - _lastRefreshUtc >= MinimumRefreshInterval;
		}
	}
}
