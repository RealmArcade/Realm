using Godot;
using Realm.Client.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Realm.Client.Network;

public partial class PeerSeederManager : Node
{
	private static PeerSeederManager? _instance;
	public static PeerSeederManager Instance => _instance ??= new PeerSeederManager();

	private ENetMultiplayerPeer? _enetPeer;
	private CancellationTokenSource? _cts;
	private bool _isSeeding;
	private string _seederId = "";

	public int CapacityPercentage { get; set; } = 100;
	public bool AcceptingUploads { get; set; } = true;
	public bool IsSeeding => _isSeeding;

	private PeerSeederManager()
	{
		_seederId = Guid.NewGuid().ToString("N");
	}

	public void Start()
	{
		if (_isSeeding) return;

		if (LobbyManager.Instance == null || LobbyManager.Instance.LocalNatType == NatType.Symmetric)
		{
			GD.Print("[PeerSeeder] Machine cannot host (Symmetric NAT). Seeding disabled.");
			return;
		}

		if (GetParent() == null)
		{
			LobbyManager.Instance.AddChild(this);
		}

		_isSeeding = true;
		_seederId = Guid.NewGuid().ToString("N");
		_cts = new CancellationTokenSource();

		int localPort = LobbyManager.Instance.ENetPort + 15;
		try
		{
			_enetPeer = new ENetMultiplayerPeer();
			var err = _enetPeer.CreateServer(localPort, 1);
			if (err != Error.Ok)
			{
				GD.PrintErr($"[PeerSeeder] Failed to create ENet Seeder server on port {localPort}: {err}");
				_isSeeding = false;
				return;
			}

			LobbyManager.Instance.Multiplayer.MultiplayerPeer = _enetPeer;
			LobbyManager.Instance.IsHost = true;

			GD.Print($"[PeerSeeder] Bound ENet seeder listener on port {localPort}");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[PeerSeeder] Failed to bind ENet listener on port {localPort}: {ex.Message}");
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

		try
		{
			if (LobbyManager.Instance != null && LobbyManager.Instance.Multiplayer.MultiplayerPeer == _enetPeer)
			{
				_enetPeer?.Close();
				LobbyManager.Instance.Multiplayer.MultiplayerPeer = null;
				LobbyManager.Instance.IsHost = false;
			}
			else
			{
				_enetPeer?.Close();
			}
			_enetPeer = null;
		}
		catch { }

		SendUnregisterRequest();
		GD.Print("[PeerSeeder] Stopped seeding.");
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
			GD.PrintErr($"[PeerSeeder] Error scanning local maps: {ex.Message}");
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
		catch { }
	}

	private async Task MaintainRegistrationAsync(CancellationToken token)
	{
		using var httpClient = new System.Net.Http.HttpClient();
		while (!token.IsCancellationRequested && _isSeeding)
		{
			try
			{
				var mapIds = GetFullyAvailableMapIds();
				int publicPort = LobbyManager.Instance.PublicPort + 15;
				string publicIp = LobbyManager.Instance.PublicIP;

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
					var regUrl = $"{LobbyManager.Instance.RegistryServerUrl}/seeders/register";
					var resp = await httpClient.PostAsync(regUrl, jsonContent, token);
					if (resp.IsSuccessStatusCode)
					{
						GD.Print($"[PeerSeeder] Registered {mapIds.Count} fully available map identifiers with orchestrator.");
					}
				}

				await Task.Delay(10000, token);
			}
			catch (OperationCanceledException) { break; }
			catch (Exception ex)
			{
				GD.PrintErr($"[PeerSeeder] Seeder registration loop exception: {ex.Message}");
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
				using var client = new System.Net.Http.HttpClient();
				var payload = new { SeederId = _seederId };
				var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
				await client.PostAsync($"{LobbyManager.Instance.RegistryServerUrl}/seeders/unregister", jsonContent);
				GD.Print("[PeerSeeder] Sent unregister request to orchestrator.");
			}
			catch { }
		});
	}

	private bool IsSeederConditionMet()
	{
		if (!GameSettings.SeedMapFiles) return false;
		if (LobbyManager.Instance == null) return false;

		bool loggedIn = !string.IsNullOrEmpty(LobbyManager.Instance.AuthenticatedUsername);
		if (!loggedIn) return false;

		bool idle = string.IsNullOrEmpty(LobbyManager.Instance.ActiveLobbyId) &&
		            (LobbyManager.Instance.Multiplayer.MultiplayerPeer == null ||
		             LobbyManager.Instance.Multiplayer.MultiplayerPeer is OfflineMultiplayerPeer);
		if (!idle) return false;

		bool canHost = LobbyManager.Instance.LocalNatType != NatType.Symmetric;
		return canHost;
	}

	public void CheckIdleAndSeedStatus()
	{
		bool shouldSeed = IsSeederConditionMet();

		if (shouldSeed && !_isSeeding)
		{
			GD.Print("[PeerSeeder] Seeder condition met. Starting background ENet seeding service...");
			Start();
		}
		else if (!shouldSeed && _isSeeding)
		{
			GD.Print("[PeerSeeder] Seeder condition no longer met. Stopping seeding service...");
			Stop();
		}
	}
}