using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Client.UI;
using HttpClient = System.Net.Http.HttpClient;
using Realm.Client.Services;

namespace Realm.Client.Network;

public class PeerSeederManager
{
	private ENetMultiplayerPeer? _enetPeer;
	private MultiplayerPeer? _previousPeer;
	private CancellationTokenSource? _cts;
	private bool _isSeeding;
	private string _seederId = "";

	public int CapacityPercentage { get; set; } = 100;
	public bool AcceptingUploads { get; set; } = true;
	public bool IsSeeding => _isSeeding;

	public PeerSeederManager()
	{
		_seederId = Guid.NewGuid().ToString("N");
	}

	public void Start()
	{
		if (_isSeeding) return;

		if (GameContext.Lobby == null || GameContext.Lobby.LocalNatType == NatType.Symmetric)
		{
			GameLog.LogToConsole("[PeerSeeder] Machine cannot host (Symmetric NAT). Seeding disabled.");
			return;
		}

		_isSeeding = true;
		_seederId = Guid.NewGuid().ToString("N");
		_cts = new CancellationTokenSource();

		int localPort = GameContext.Lobby.ENetPort + 15;
		try
		{
			_enetPeer = new ENetMultiplayerPeer();
			var err = _enetPeer.CreateServer(localPort, 1);
			if (err != Error.Ok)
			{
				GameLog.LogError($"[PeerSeeder] Failed to create ENet Seeder server on port {localPort}: {err}");
				_isSeeding = false;
				return;
			}

			_previousPeer = GameContext.Lobby.Multiplayer.MultiplayerPeer;
			GameContext.Lobby.Multiplayer.MultiplayerPeer = _enetPeer;

			GameLog.LogToConsole($"[PeerSeeder] Bound ENet seeder listener on port {localPort}");
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[PeerSeeder] Failed to bind ENet listener on port {localPort}: {ex.Message}");
			_isSeeding = false;
			return;
		}

		Task.Run(() => MaintainRegistrationAsync(_cts.Token));
	}

	public void Stop()
	{
		if (!_isSeeding) return;
		_isSeeding = false;
		_cts?.Cancel();

		ENetMultiplayerPeer? seederPeer = _enetPeer;
		MultiplayerPeer? previousPeer = _previousPeer;
		_enetPeer = null;
		_previousPeer = null;

		try
		{
			LobbyManager? lobby = GameContext.Lobby;
			bool stillInstalled = lobby != null && lobby.Multiplayer.MultiplayerPeer == seederPeer;

			seederPeer?.Close();

			if (stillInstalled && lobby != null) lobby.Multiplayer.MultiplayerPeer = previousPeer;
		}
		catch
		{
		}

		SendUnregisterRequest();
		GameLog.LogToConsole("[PeerSeeder] Stopped seeding.");
	}

	private List<string> GetFullyAvailableMapIds()
	{
		var availableMaps = new List<string>();
		try
		{
			string dir = MapAssetManager.GlobalArchiveDirectory;
			if (!Directory.Exists(dir)) return availableMaps;

			var manifestFiles = Directory.GetFiles(dir, "manifest.json", SearchOption.AllDirectories);
			foreach (var file in manifestFiles)
			{
				ProcessManifestFile(file, availableMaps);
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[PeerSeeder] Error scanning local maps: {ex.Message}");
		}

		return availableMaps.Distinct().ToList();
	}

	private void ProcessManifestFile(string file, List<string> availableMaps)
	{
		try
		{
			var manifest = MapManifest.LoadFromFile(file);
			if (manifest?.Files == null || manifest.Files.Count == 0) return;

			var missing = MapAssetManager.GetMissingHashes(manifest.Files.Values);
			if (missing.Count > 0) return;

			string manifestBlake3 = MapAssetManager.ComputeManifestBlake3(manifest);
			availableMaps.Add(manifestBlake3);

			if (string.IsNullOrWhiteSpace(manifest.MapName)) return;

			availableMaps.Add(manifest.MapName);

			if (!string.IsNullOrWhiteSpace(manifest.Version))
			{
				availableMaps.Add($"{manifest.MapName}_{manifest.Version}");
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"could not read the manifest of a map this seeder holds, so it will not be advertised: {ex.GetType().Name}: {ex.Message}");
		}
	}

	private async Task MaintainRegistrationAsync(CancellationToken token)
	{
		using var httpClient = new HttpClient();
		while (!token.IsCancellationRequested && _isSeeding)
		{
			try
			{
				LobbyManager? lobby = GameContext.Lobby;
				if (lobby == null) break;

				var mapIds = GetFullyAvailableMapIds();
				int publicPort = lobby.PublicPort + 15;
				string publicIp = lobby.PublicIP;

				if (mapIds.Count > 0)
				{
					var payload = new
					{
						SeederId = _seederId,
						ReportedIP = publicIp,
						Port = publicPort,
						MapIds = mapIds,
						CapacityPercentage = CapacityPercentage,
						AcceptingUploads = AcceptingUploads
					};
					var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
					var regUrl = $"{lobby.RegistryServerUrl}/seeders/register";
					var resp = await httpClient.PostAsync(regUrl, jsonContent, token);
					if (resp.IsSuccessStatusCode)
					{
						GameLog.LogToConsole($"[PeerSeeder] Registered {mapIds.Count} fully available map identifiers with orchestrator.");
					}
				}

				await Task.Delay(10000, token);
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[PeerSeeder] Seeder registration loop exception: {ex.Message}");
				await Task.Delay(5000, token);
			}
		}
	}

	private void SendUnregisterRequest()
	{
		Task.Run(async () =>
		{
			try
			{
				using var client = new HttpClient();
				var payload = new { SeederId = _seederId };
				var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
				string? registryServerUrl = GameContext.Lobby?.RegistryServerUrl;
				if (string.IsNullOrEmpty(registryServerUrl)) return;

				await client.PostAsync($"{registryServerUrl}/seeders/unregister", jsonContent);
				GameLog.LogToConsole("[PeerSeeder] Sent unregister request to orchestrator.");
			}
			catch (Exception ex)
			{
				GameLog.LogError($"could not unregister this seeder from the orchestrator, so it may stay listed after shutdown: {ex.GetType().Name}: {ex.Message}");
			}
		});
	}

	private bool IsSeederConditionMet()
	{
		if (!GameSettings.SeedMapFiles) return false;
		if (GameContext.Lobby == null) return false;
		if (GameContext.Lobby.IsSinglePlayer || GameContext.Lobby.IsGameStarted) return false;

		MultiplayerPeer? activePeer = GameContext.Lobby.Multiplayer.MultiplayerPeer;
		bool idle = string.IsNullOrEmpty(GameContext.Lobby.ActiveLobbyId) &&
		            (activePeer == null ||
		             activePeer is OfflineMultiplayerPeer ||
		             activePeer == _enetPeer);
		if (!idle) return false;

		bool canHost = GameContext.Lobby.LocalNatType != NatType.Symmetric;
		return canHost;
	}

	public void CheckIdleAndSeedStatus()
	{
		bool shouldSeed = IsSeederConditionMet();

		if (shouldSeed && !_isSeeding)
		{
			GameLog.LogToConsole("[PeerSeeder] Seeder condition met. Starting background ENet seeding service...");
			Start();
		}
		else if (!shouldSeed && _isSeeding)
		{
			GameLog.LogToConsole("[PeerSeeder] Seeder condition no longer met. Stopping seeding service...");
			Stop();
		}
	}
}