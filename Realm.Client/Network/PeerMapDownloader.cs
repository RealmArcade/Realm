using System;
using System.Threading.Tasks;
using Realm.Client.Services;

namespace Realm.Client.Network;

public class PeerMapDownloader
{
	private readonly MapDistributionClient _distClient = new();

	public event Action<float>? DownloadProgressChanged;

	public PeerMapDownloader()
	{
		_distClient.DownloadProgressChanged += p => DownloadProgressChanged?.Invoke(p);
	}

	public async Task<bool> DownloadMapAsync(string mapId)
	{
		string registryUrl = GameContext.Lobby != null ? GameContext.Lobby.RegistryServerUrl : "http://localhost:5000";
		return await _distClient.DownloadMapPackageFromRegistryAsync(mapId, registryUrl, p => DownloadProgressChanged?.Invoke(p));
	}
}