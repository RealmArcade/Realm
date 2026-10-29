using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Realm.Client.Core;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.Utils;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Metadata;
using Realm.Shared.Services;
using SharpToken;
using Environment = System.Environment;
using FileAccess = System.IO.FileAccess;
using HttpClient = System.Net.Http.HttpClient;

namespace Realm.Client.Network;

[GlobalClass]
public partial class LobbyManager : BaseMapTransferNode
{
	public bool IsSinglePlayer { get; set; } = false;

	/// <summary>
	///     Whether the match being started may run with cheats. Only a match played against the computer may: a single
	///     player match, or a map being tested from the editor, both of which run by themselves on this machine. The
	///     authority acts on it by listening on the loopback address and seating one peer, so a match started with
	///     cheats is one nobody else can reach.
	/// </summary>
	public bool AllowsCheats => IsSinglePlayer;
	public string? LastHostError { get; private set; }
	public bool IsRestrictedHost { get; private set; }

	private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

	public static string GetBaseGameDirectory()
	{
		string exePath = OS.GetExecutablePath();
		string baseDir = Path.GetDirectoryName(exePath) ?? "";

		int versionsIndex = baseDir.IndexOf($"{Path.DirectorySeparatorChar}versions{Path.DirectorySeparatorChar}");
		if (versionsIndex == -1) versionsIndex = baseDir.IndexOf("/versions/");
		if (versionsIndex == -1 && baseDir.EndsWith($"{Path.DirectorySeparatorChar}versions")) versionsIndex = baseDir.Length - 9;
		if (versionsIndex == -1 && baseDir.EndsWith("/versions")) versionsIndex = baseDir.Length - 9;

		if (versionsIndex != -1)
		{
			baseDir = baseDir.Substring(0, versionsIndex);
		}

		return baseDir;
	}

	public static string GetVersionExecutablePath(string targetVersion)
	{
		string baseDir = GetBaseGameDirectory();
		string exePath = OS.GetExecutablePath();
		string fileName = Path.GetFileName(exePath);

		return Path.Combine(baseDir, "versions", targetVersion, fileName);
	}

	public List<string> OfficialServers { get; private set; } = new();
	public List<string> RegistryServers => OfficialServers;
	private int _currentServerIndex = 0;
	public string RegistryServerUrl => OfficialServers.Count > 0 && _currentServerIndex < OfficialServers.Count && !string.IsNullOrWhiteSpace(OfficialServers[_currentServerIndex]) ? OfficialServers[_currentServerIndex] : ServersConfigHelper.GetDefaultServerUrl();
	private const int DefaultGamePort = 8999;
	private const int StunPort = 19302;

	public int ENetPort { get; private set; } = DefaultGamePort;
	public int MaxPlayers { get; private set; } = MapInfoMetadata.DefaultPlayablePlayerSlots;
	public string? LastLobbyWarning { get; private set; }

	public string AuthenticatedUsername { get; set; } = "Horaid_Topa";
	public string? AuthToken { get; set; }
	public string? AuthProvider { get; set; }

	public bool SetInGameUsername(string newUsername, out string? errorMessage)
	{
		if (!NameNormalizationHelper.ValidateUsername(newUsername, out errorMessage))
		{
			return false;
		}

		AuthenticatedUsername = newUsername.Trim();
		int myId = Multiplayer?.MultiplayerPeer != null ? Multiplayer.GetUniqueId() : 1;
		var me = PlayerList.Find(p => p.PeerId == myId || (IsHost && p.PeerId == 1));
		if (me != null)
		{
			me.Name = AuthenticatedUsername;
		}

		return true;
	}

	private readonly string _persistentSessionToken = Guid.NewGuid().ToString();
	private readonly Dictionary<int, string> _pendingSessionTokens = new();
	private readonly HashSet<int> _peersWithRegisteredSessionTokens = new();

	public NatType LocalNatType { get; private set; } = NatType.Open;
	public bool IsHost { get; set; }
	public string? ActiveLobbyId { get; private set; }
	public bool IsGameStarted { get; set; }
	public DateTime? GameSessionStartTime { get; private set; }
	public int MatchSeed { get; private set; }
	public string ActiveMapName { get; set; }
	public string ActiveMapVersion { get; set; } = "1.0.0";
	public bool SpectatorDelay { get; set; } = false;
	public string? LobbyJoinError { get; set; }
	public string HostStability { get; set; } = "Excellent";
	public event Action<string> HostStabilityUpdated;


	public List<PlayerInfo> PlayerList { get; } = new();


	private readonly HttpClient _httpClient = new();
	private string? _connectedHostIp;
	private int _connectedHostPort;
	private bool _isConnectedToHost;
	private ClientWebSocket? _hostWebSocket;
	private CancellationTokenSource? _wsCts;
	private string? _hostPublicIp;
	private int _hostPublicPort;
	public string PublicIP => _hostPublicIp ?? "127.0.0.1";
	public int PublicPort => _hostPublicPort > 0 ? _hostPublicPort : ENetPort;
	private string? _hostToken;
	private string? _countdownMapName;
	private int _countdownRemaining;
	private SceneTreeTimer? _countdownTimer;
	private SceneTreeTimer? _diagnosticsTimer;
	private readonly List<(string Sender, string Message, bool IsMuted, long TimestampUtcTicks)> _chatHistory = new();
	private static InferenceSession? _onnxSession;
	private static Dictionary<string, int>? _vocab;
	private static readonly object _sessionLock = new();
	private readonly SemaphoreSlim _mapDownloadLock = new(1, 1);



	public void SwitchToNextServer()
	{
		if (RegistryServers.Count > 1)
		{
			_currentServerIndex = (_currentServerIndex + 1) % RegistryServers.Count;
			GD.Print($"[LobbyManager] Switched to next bootstrap server: {RegistryServerUrl}");
		}
	}

	public void RandomizeServerIndex()
	{
		if (RegistryServers.Count > 1)
		{
			_currentServerIndex = Random.Shared.Next(RegistryServers.Count);
			GD.Print($"[LobbyManager] Randomized bootstrap server to: {RegistryServerUrl}");
		}
	}

	public async Task<string?> FetchLobbiesRawAsync()
	{
		for (int i = 0; i < RegistryServers.Count; i++)
		{
			try
			{
				var response = await _httpClient.GetAsync($"{RegistryServerUrl}/lobbies");
				if (response.IsSuccessStatusCode)
				{
					return await response.Content.ReadAsStringAsync();
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[LobbyManager] Failed to fetch lobbies from {RegistryServerUrl}: {ex.Message}");
			}

			SwitchToNextServer();
		}

		return null;
	}

	public string? ConnectedHostIp => _connectedHostIp;


	public event Action? PlayerListUpdated;
	public event Action<string, string, bool, long>? ChatReceived;
	public event Action<int, string>? ServerChatCommandReceived;
	public event Action<string>? ConnectionFailed;
	public event Action<string>? KickReceived;
	public event Action? NatTestCompleted;
	public event Action<float>? MapDownloadProgressChanged;
	public event Action? MapDownloadCompleted;
	public event Action? MapDownloadFailed;
	public event Action<bool>? SpectatorDelayChanged;
	public event Action<string, int>? CountdownStarted;
	public event Action<int>? CountdownTick;
	public event Action? CountdownCancelled;
	public event Action? CountdownFinished;
	public event Action<string>? ActiveMapChanged;

	protected override string TransferLogPrefix => "LobbyManager";

	protected override bool IsMapTransferAllowed() => IsHost || (GameContext.PeerSeeder != null && GameContext.PeerSeeder.IsSeeding);

	protected override void OnDownloadProgressChanged(float progress) => CallDeferred(nameof(EmitDownloadProgress), progress);

	public override void _Ready()
	{
		ProcessPerformanceOptimizer.EnforceOptimalPerformance();
		GameContext.RegisterLobby(this);
		ProcessMode = ProcessModeEnum.Always;

		LoadServersConfig();

		Multiplayer.PeerConnected += OnPeerConnected;
		Multiplayer.PeerDisconnected += OnPeerDisconnected;
		Multiplayer.ConnectedToServer += OnConnectedToServer;
		Multiplayer.ConnectionFailed += OnConnectionFailedGodot;
		Multiplayer.ServerDisconnected += OnServerDisconnectedGodot;

		RunNatTypeTest();

		MapAssetManager.PruneGlobalArchive();
		AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

		StartPeerSeederLoop();
	}

	private void StartPeerSeederLoop()
	{
		Task.Run(async () =>
		{
			while (true)
			{
				try
				{
					await Task.Delay(5000);
					if (!GodotObject.IsInstanceValid(this) || IsQueuedForDeletion()) break;
					CheckPeerSeederStatus();
				}
				catch
				{
					break;
				}
			}
		});
	}

	private void CheckPeerSeederStatus()
	{
		if (GameContext.PeerSeeder == null) return;

		Callable.From(() =>
		{
			if (GodotObject.IsInstanceValid(this) && !IsQueuedForDeletion() && GameContext.PeerSeeder != null)
			{
				GameContext.PeerSeeder.CheckIdleAndSeedStatus();
			}
		}).CallDeferred();
	}

	public override void _Notification(int what)
	{
		if (what == (int)NotificationWMCloseRequest || what == (int)NotificationPredelete)
		{
			CleanUpLobbySynchronously();
		}
	}

	private void OnProcessExit(object? sender, EventArgs e)
	{
		CleanUpLobbySynchronously();
	}

	private readonly object _cleanupLock = new object();
	private bool _cleanedUp;

	private void CleanUpLobbySynchronously()
	{
		lock (_cleanupLock)
		{
			if (_cleanedUp) return;
			_cleanedUp = true;
		}

		try
		{
			GameContext.PeerSeeder.Stop();
			GameContext.Upnp.ClosePort(ENetPort);
		}
		catch
		{
		}

		if (IsHost && !string.IsNullOrEmpty(ActiveLobbyId) && !string.IsNullOrEmpty(_hostToken))
		{
			string lobbyIdToClose = ActiveLobbyId;
			string tokenToClose = _hostToken;
			_hostToken = null;

			try
			{
				GD.Print($"[LobbyManager] Closing lobby {lobbyIdToClose} synchronously before exit...");
				var payload = new { LobbyId = lobbyIdToClose, HostToken = tokenToClose };
				var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

				var task = Task.Run(async () => { await _httpClient.PostAsync($"{RegistryServerUrl}/lobbies/close", jsonContent); });
				task.Wait(2000);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[LobbyManager] Failed synchronous lobby close on exit: {ex.Message}");
			}
		}
	}

	private void LoadServersConfig()
	{
		var config = ServersConfigHelper.Load();
		OfficialServers = config.Servers;
		_currentServerIndex = 0;
		GD.Print($"[LobbyManager] Loaded servers: {string.Join(", ", RegistryServers)}");
	}

	public async Task RunNatTypeTestAsync()
	{
		GD.Print("[LobbyManager] Starting STUN NAT Type Test...");
		_hostPublicIp = null;
		_hostPublicPort = 0;
		LocalNatType = await NatTypeTester.DetermineNatTypeAsync(0);
		GD.Print($"[LobbyManager] NAT Type Classified: {LocalNatType}");

		try
		{
			var dnsAddresses = await Dns.GetHostAddressesAsync("stun.l.google.com");
			if (dnsAddresses.Length > 0)
			{
				StunResult? result = null;

				try
				{
					result = await NatTypeTester.QueryStunOnPortAsync(dnsAddresses[0], StunPort, ENetPort);
				}
				catch (Exception ex)
				{
					GD.Print($"[LobbyManager] Direct STUN on port {ENetPort} failed: {ex.Message}");
				}

				if (result == null || !result.Success || result.MappedEndPoint == null)
				{
					using var udp = new UdpClient();
					udp.ExclusiveAddressUse = false;
					udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
					udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

					var serverEp = new IPEndPoint(dnsAddresses[0], 19302);
					byte[] req = new byte[20];
					req[0] = 0x00;
					req[1] = 0x01; // Binding Request
					new Random().NextBytes(new Span<byte>(req, 4, 16));

					await udp.SendAsync(req, req.Length, serverEp);
					var receiveTask = udp.ReceiveAsync();
					var timeoutTask = Task.Delay(1000);
					if (await Task.WhenAny(receiveTask, timeoutTask) == receiveTask)
					{
						var response = await receiveTask;
						result = new StunResult();
						NatTypeTester.ParseStunResponse(response.Buffer, result);
					}
				}

				if (result != null && result.Success && result.MappedEndPoint != null)
				{
					bool mappedFromEnetPort = result.LocalEndPoint != null && result.LocalEndPoint.Port == ENetPort;
					_hostPublicIp = result.MappedEndPoint.Address.ToString();
					_hostPublicPort = mappedFromEnetPort ? result.MappedEndPoint.Port : ENetPort;
					GD.Print($"[STUN] Local {ENetPort} -> Public {_hostPublicIp}:{_hostPublicPort}");
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[LobbyManager] Failed to get public IP: {ex.Message}");
		}

		CallDeferred(nameof(EmitNatTestCompleted));
	}

	public void RunNatTypeTest()
	{
		Task.Run(RunNatTypeTestAsync);
	}

	private void EmitNatTestCompleted()
	{
		NatTestCompleted?.Invoke();
	}


	/// <summary>
	///     Starts a match this machine plays by itself.
	/// </summary>
	/// <param name="mapPathName">The map to play.</param>
	/// <param name="mapDisplayName">The name the map is shown under.</param>
	/// <param name="mapVersion">The version of the map to play, or nothing for the newest one.</param>
	/// <param name="debugMapScript">
	///     Whether the authority keeps the map script's debug information, which a map tested from the editor asks for so a
	///     breakpoint in the script reaches the process running it.
	/// </param>
	public void HostSinglePlayerGame(string mapPathName, string mapDisplayName, string? mapVersion = null, bool debugMapScript = false)
	{
		IsSinglePlayer = true;
		IsHost = true;
		IsGameStarted = true;
		ActiveMapName = mapPathName;
		ActiveMapVersion = !string.IsNullOrWhiteSpace(mapVersion) ? mapVersion : "1.0.0";
		PlayerList.Clear();
		LastLobbyWarning = null;

		string? mapDirectory = GameHost.ResolveMapDirectory(mapPathName, ActiveMapVersion);
		var metadata = MapFileService.LoadMetadata(mapDirectory ?? string.Empty);
		MaxPlayers = metadata.MapProperties?.GetPlayablePlayerSlots() ?? MapInfoMetadata.DefaultPlayablePlayerSlots;
		int requestedAiCount = Math.Max(0, metadata.MapProperties?.TestAiPlayerCount ?? 1);
		int aiCount = Math.Min(requestedAiCount, Math.Max(0, MaxPlayers - 1));
		if (aiCount < requestedAiCount)
		{
			LastLobbyWarning = string.Format(Tr("Only {0} of {1} requested AI players fit into the {2} playable slots of this map."), aiCount, requestedAiCount, MaxPlayers);
			GD.PushWarning($"[LobbyManager] {LastLobbyWarning}");
		}

		bool randomize = metadata.MapProperties?.TestRandomizeSlots ?? false;
		int? testSeed = metadata.MapProperties?.TestRandomSeed;

		GameContext.RandomService.SetSeed(testSeed);
		MatchSeed = GameContext.RandomService.Seed;

		int humanSlot = 1;
		var aiSlots = new List<int>();

		if (!randomize)
		{
			humanSlot = 1;
			for (int i = 0; i < aiCount; i++)
			{
				aiSlots.Add(2 + i);
			}
		}
		else
		{
			var available = Enumerable.Range(1, MaxPlayers).ToList();
			var rng = (testSeed.HasValue && testSeed.Value != 0) ? new Random(testSeed.Value) : new Random();
			int n = available.Count;
			while (n > 1)
			{
				n--;
				int k = rng.Next(n + 1);
				(available[k], available[n]) = (available[n], available[k]);
			}

			humanSlot = available[0];
			for (int i = 0; i < aiCount; i++)
			{
				aiSlots.Add(available[1 + i]);
			}
		}

		var hostPlayer = new PlayerInfo
		{
			PeerId = 1,
			Slot = humanSlot,
			Name = AuthenticatedUsername,
			Faction = "HUMAN",
			Team = "Team 1",
			Color = PlayerColorConfig.GetColor(humanSlot),
			IsHost = true,
			Latency = "0 ms",
			Jitter = "0 ms",
			PacketLoss = "0%",
			BinaryVersion = RealmVersion.GameBinaryVersion,
			SessionToken = _persistentSessionToken
		};
		PlayerList.Add(hostPlayer);

		for (int i = 0; i < aiSlots.Count; i++)
		{
			int slot = aiSlots[i];
			var botPlayer = new PlayerInfo
			{
				PeerId = -(slot + 100),
				Slot = slot,
				Name = $"Bot {slot}",
				Faction = "AI",
				Team = $"Team {slot}",
				Color = PlayerColorConfig.GetColor(slot),
				IsHost = false,
				Latency = "0 ms",
				Jitter = "0 ms",
				PacketLoss = "0%",
				BinaryVersion = RealmVersion.GameBinaryVersion,
				SessionToken = string.Empty
			};
			PlayerList.Add(botPlayer);
		}

		if (!TryStartAuthority(mapDirectory, mapPathName, wasmDebug: debugMapScript)) return;

		CallDeferred(nameof(EnterMatchScene), mapPathName, MatchSeed);
	}

	/// <summary>
	///     Starts the process that runs this match and joins it. A single-player match is run by an authority exactly as a
	///     remote one is, so the client never simulates the world it is playing in and a bug cannot behave differently in
	///     one mode than the other.
	/// </summary>
	private bool TryStartAuthority(string? mapDirectory, string mapPathName, int authorityPort = 0, bool openToOthers = false, bool wasmDebug = false)
	{
		if (string.IsNullOrEmpty(mapDirectory))
		{
			LastHostError = $"The map folder for '{mapPathName}' could not be found.";
			GD.PushError($"[LobbyManager] {LastHostError}");
			IsGameStarted = false;
			return false;
		}

		var channel = GetNodeOrNull<ClientNetworkChannel>("/root/RealmNetwork");
		if (channel == null)
		{
			LastHostError = "This build has no channel to an authority.";
			GD.PushError($"[LobbyManager] {LastHostError}");
			IsGameStarted = false;
			return false;
		}

		GameContext.ServerSession?.Dispose();

		var session = new ClientServerSession(channel);
		var start = new ServerMatchStart(
			ProjectSettings.GlobalizePath(mapDirectory),
			mapPathName,
			MatchSeed,
			Path.Combine(OS.GetUserDataDir(), "logs"),
			OS.GetUserDataDir(),
			LocalizationManager.GetCurrentLanguageCode(),
			AllowsCheats,
			openToOthers,
			wasmDebug,
			BuildMatchRoster());

		if (!session.TryStart(start, out string failure, authorityPort))
		{
			LastHostError = failure;
			GD.PushError($"[LobbyManager] {failure}");
			IsGameStarted = false;
			session.Dispose();
			return false;
		}

		GameContext.AttachServerSession(session);
		return true;
	}

	/// <summary>
	///     Hands a hosted match over to the process that runs it. Every player already in the lobby is told where to
	///     connect first, so the announcement survives the connection it was sent over: the authority takes the port the
	///     lobby was advertised on, which is the port every player already reached this machine on. Only then does the
	///     host leave the lobby, which is what frees that port for the authority.
	/// </summary>
	/// <param name="mapName">The map the match plays.</param>
	/// <param name="matchSeed">The seed the match's scripts draw from.</param>
	private async void StartHostedMatch(string mapName, int matchSeed)
	{
		await ToSignal(GetTree().CreateTimer(HandoffDelaySeconds), SceneTreeTimer.SignalName.Timeout);
		if (!IsHost || IsSinglePlayer) return;

		string? mapDirectory = GameHost.ResolveMapDirectory(mapName, ActiveMapVersion);
		Multiplayer.MultiplayerPeer?.Close();
		Multiplayer.MultiplayerPeer = null;

		if (!TryStartAuthority(mapDirectory, mapName, ENetPort, openToOthers: true))
		{
			ConnectionFailed?.Invoke(LastHostError ?? "The match could not be started.");
			return;
		}

		EnterMatchScene(mapName, matchSeed);
	}

	/// <summary>How long every player is given to be told where the match is before the lobby it was announced over is closed.</summary>
	private const double HandoffDelaySeconds = 0.75;

	public async Task<bool> HostLobbyAsync(string mapPathName, string mapDisplayName, string? explicitVersion = null)
	{
		InitializeHostState(mapPathName, explicitVersion);

		await RunNatTypeTestAsync();
		if (LocalNatType == NatType.Symmetric)
		{
			LastHostError = "Lobby creation rejected: Symmetric NAT is not supported.";
			GD.PrintErr("[LobbyManager] Lobby creation rejected: Symmetric NAT is not supported.");
			return false;
		}

		if (!await TryCreateENetServerAsync()) return false;

		bool upnpSuccess = await GameContext.Upnp.OpenPortAsync(ENetPort);
		bool isRestrictedNat = LocalNatType is NatType.RestrictedCone or NatType.PortRestrictedCone or NatType.Symmetric;
		IsRestrictedHost = !upnpSuccess && isRestrictedNat;
		if (IsRestrictedHost)
		{
			GD.Print("[LobbyManager] Warning: UPnP port mapping not available on restricted NAT. External players may require Direct Connect.");
		}

		Diagnostics.StartHostListener(ENetPort + 1);
		StartHostDiagnosticsTimer();

		bool registered = await RegisterLobbyWithRegistryAsync(mapPathName, mapDisplayName, explicitVersion);
		if (!registered) return false;

		PlayerListUpdated?.Invoke();
		return true;
	}

	private static int ResolvePlayablePlayerSlots(string mapPathName, string? version)
	{
		try
		{
			string? manifestPath = MapAssetManager.FindManifestPath(mapPathName, version);
			string? mapDirectory = string.IsNullOrEmpty(manifestPath) ? null : Path.GetDirectoryName(manifestPath);
			if (string.IsNullOrEmpty(mapDirectory)) return MapInfoMetadata.DefaultPlayablePlayerSlots;

			var metadata = MapFileService.LoadMetadata(mapDirectory);
			return metadata.MapProperties?.GetPlayablePlayerSlots() ?? MapInfoMetadata.DefaultPlayablePlayerSlots;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[LobbyManager] Failed to resolve playable player slots for '{mapPathName}': {ex.Message}");
			return MapInfoMetadata.DefaultPlayablePlayerSlots;
		}
	}

	private int FindNextFreePlayableSlot()
	{
		for (int slot = 1; slot <= MaxPlayers; slot++)
		{
			if (!PlayerList.Any(p => p.Slot == slot)) return slot;
		}

		return -1;
	}

	private void InitializeHostState(string mapPathName, string? explicitVersion)
	{
		IsHost = true;
		MaxPlayers = ResolvePlayablePlayerSlots(mapPathName, explicitVersion);
		LastLobbyWarning = null;
		HostStability = HostStabilityTracker.GetOverallStability();
		IsGameStarted = false;
		SpectatorDelay = false;
		LastHostError = null;
		PlayerList.Clear();
		ActiveMapName = mapPathName;
		ActiveMapVersion = !string.IsNullOrWhiteSpace(explicitVersion) ? explicitVersion : "1.0.0";

		var hostPlayer = new PlayerInfo
		{
			PeerId = 1,
			Slot = 1,
			Name = AuthenticatedUsername,
			Faction = "HUMAN",
			Team = "Team 1",
			Color = PlayerColorConfig.GetColor(1),
			IsHost = true,
			Latency = "0 ms",
			Jitter = "0 ms",
			PacketLoss = "0%",
			BinaryVersion = RealmVersion.GameBinaryVersion,
			SessionToken = _persistentSessionToken
		};
		PlayerList.Add(hostPlayer);
	}

	private async Task<bool> TryCreateENetServerAsync()
	{
		if (Multiplayer.MultiplayerPeer != null)
		{
			try
			{
				Multiplayer.MultiplayerPeer.Close();
			}
			catch
			{
			}

			Multiplayer.MultiplayerPeer = null;
		}

		var peer = new ENetMultiplayerPeer();
		var err = peer.CreateServer(ENetPort, MaxPlayers);
		if (err != Error.Ok)
		{
			await Task.Delay(100);
			peer = new ENetMultiplayerPeer();
			err = peer.CreateServer(ENetPort, MaxPlayers);
			if (err != Error.Ok)
			{
				LastHostError = $"Failed to create ENet Server on port {ENetPort}: {err}";
				GD.PrintErr($"[LobbyManager] Failed to create ENet Server on port {ENetPort}: {err}");
				return false;
			}
		}

		Multiplayer.MultiplayerPeer = peer;
		if (Multiplayer is SceneMultiplayer sceneMultiplayer)
		{
			sceneMultiplayer.ServerRelay = false;
		}

		GD.Print($"[LobbyManager] ENet Server initialized on port {ENetPort}");
		return true;
	}

	private async Task<bool> RegisterLobbyWithRegistryAsync(string mapPathName, string mapDisplayName, string? explicitVersion)
	{
		try
		{
			int hostPingBaseline = await MeasurePingToRegistryAsync();
			var mapInfo = GetMapMetadataInfo(mapPathName, mapDisplayName, explicitVersion);

			var registerPayload = new
			{
				Map = mapDisplayName,
				HostPort = _hostPublicPort > 0 ? _hostPublicPort : ENetPort,
				NatType = LocalNatType.ToString(),
				ReportedHostIP = _hostPublicIp ?? "127.0.0.1",
				PasswordHash = "",
				MaxPlayers = MaxPlayers,
				SlotsUsed = PlayerList.Count,
				HostPingBaseline = hostPingBaseline,
				GameVersion = RealmVersion.GameBinaryVersion,
				LocalIP = GetLocalIPAddress(),
				MapVersion = mapInfo.Version,
				Signature = mapInfo.Signature,
				PublicKey = mapInfo.PublicKey,
				MapHash = mapInfo.MapHash,
				MapSizeBytes = mapInfo.SizeBytes
			};

			var response = await SendRegisterRequestAsync(registerPayload);
			if (response == null || !response.IsSuccessStatusCode)
			{
				return await HandleRegisterFailureAsync(response);
			}

			await ProcessSuccessfulRegistrationAsync(response);
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[LobbyManager] Registry registration error: {ex.Message}");
			return false;
		}
	}

	private (string Version, string Signature, string PublicKey, string MapHash, long SizeBytes) GetMapMetadataInfo(string mapPathName, string mapDisplayName, string? explicitVersion)
	{
		string mapVersion = "1.0.0";
		if (!string.IsNullOrWhiteSpace(explicitVersion))
		{
			mapVersion = explicitVersion.Trim();
		}

		string signature = "";
		string publicKey = "";
		string mapHash = "";

		try
		{
			string? manifestPath = MapAssetManager.FindManifestPath(mapPathName, mapVersion);
			if (manifestPath == null)
			{
				manifestPath = MapAssetManager.FindManifestPath(mapDisplayName, mapVersion);
			}

			if (manifestPath != null)
			{
				TryReadManifestMetadata(manifestPath, ref mapVersion, out signature, out publicKey, out mapHash);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[LobbyManager] Failed to read map signature metadata: {ex.Message}");
		}

		long mapSizeBytes = MapAssetManager.GetMapTotalSizeBytes(mapPathName, mapVersion);
		if (mapSizeBytes <= 0)
		{
			mapSizeBytes = MapAssetManager.GetMapTotalSizeBytes(mapDisplayName, mapVersion);
		}

		return (mapVersion, signature, publicKey, mapHash, mapSizeBytes);
	}

	private void TryReadManifestMetadata(string manifestPath, ref string mapVersion, out string signature, out string publicKey, out string mapHash)
	{
		signature = "";
		publicKey = "";
		mapHash = "";

		if (!File.Exists(manifestPath))
		{
			return;
		}

		string json = File.ReadAllText(manifestPath);
		using var mapDoc = JsonDocument.Parse(json);
		var root = mapDoc.RootElement;

		mapVersion = UpdateMapVersion(root, mapVersion);

		signature = GetStringProperty(root, "signature");
		publicKey = GetStringProperty(root, "author_key");

		if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(publicKey))
		{
			return;
		}

		byte[] mapBytes = File.ReadAllBytes(manifestPath);
		string mapBlake3 = RealmMetadataHelper.ComputeBlake3(mapBytes, ".json");
		mapHash = $"{mapBlake3}.json";
	}

	private string UpdateMapVersion(JsonElement root, string defaultVersion)
	{
		if (root.TryGetProperty("Version", out var vProp) && vProp.ValueKind == JsonValueKind.String)
		{
			string? ver = vProp.GetString();
			if (ver != null) return ver;
		}

		if (root.TryGetProperty("MapProperties", out var props) && props.TryGetProperty("MapVersion", out var mv))
		{
			string? mver = mv.GetString();
			if (mver != null) return mver;
		}

		return defaultVersion;
	}

	private string GetStringProperty(JsonElement root, string propName)
	{
		if (root.TryGetProperty(propName, out var prop))
		{
			string? val = prop.GetString();
			if (val != null)
			{
				return val;
			}
		}

		return "";
	}

	private async Task<HttpResponseMessage?> SendRegisterRequestAsync(object registerPayload)
	{
		HttpResponseMessage? response = null;
		for (int i = 0; i < RegistryServers.Count; i++)
		{
			try
			{
				var jsonContent = new StringContent(JsonSerializer.Serialize(registerPayload), Encoding.UTF8, "application/json");
				response = await _httpClient.PostAsync($"{RegistryServerUrl}/lobbies/register", jsonContent);
				if (response.IsSuccessStatusCode) break;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[LobbyManager] Register failed on {RegistryServerUrl}: {ex.Message}");
			}

			SwitchToNextServer();
		}

		return response;
	}

	private async Task<bool> HandleRegisterFailureAsync(HttpResponseMessage? response)
	{
		string errMsg = "Registry server registration failed.";

		if (response != null)
		{
			errMsg = await ExtractErrorMessageAsync(response, errMsg);
		}

		LastHostError = errMsg;
		GD.PrintErr($"[LobbyManager] {errMsg}");

		if (response != null)
		{
			if (response.StatusCode == HttpStatusCode.BadRequest)
			{
				Multiplayer.MultiplayerPeer = null;
				IsHost = false;
				return false;
			}
		}

		if (string.IsNullOrEmpty(ActiveLobbyId))
		{
			ActiveLobbyId = $"local-{Guid.NewGuid():N}";
			GD.Print($"[LobbyManager] Assigned fallback local lobby ID: {ActiveLobbyId}");
		}

		return true; // proceed locally if registry is offline
	}

	private async Task<string> ExtractErrorMessageAsync(HttpResponseMessage response, string defaultError)
	{
		try
		{
			string body = await response.Content.ReadAsStringAsync();
			using var errDoc = JsonDocument.Parse(body);

			string parsedError = TryGetErrorMessage(errDoc.RootElement);
			if (!string.IsNullOrEmpty(parsedError))
			{
				return parsedError;
			}

			if (!string.IsNullOrWhiteSpace(body))
			{
				return body;
			}
		}
		catch
		{
			// Ignore parse errors, fallback to default error
		}

		return defaultError;
	}

	private string TryGetErrorMessage(JsonElement root)
	{
		string[] errorProperties = { "message", "Message", "title", "detail" };

		foreach (string propName in errorProperties)
		{
			if (root.TryGetProperty(propName, out var msgProp))
			{
				string? msg = msgProp.GetString();
				if (msg != null) return msg;
			}
		}

		return string.Empty;
	}

	private async Task ProcessSuccessfulRegistrationAsync(HttpResponseMessage response)
	{
		var respText = await response.Content.ReadAsStringAsync();
		using var doc = JsonDocument.Parse(respText);
		ActiveLobbyId = doc.RootElement.GetProperty("lobbyId").GetString();

		if (doc.RootElement.TryGetProperty("hostToken", out var hostTokenProp))
		{
			_hostToken = hostTokenProp.GetString();
		}

		GD.Print($"[LobbyManager] Lobby registered on server. LobbyId: {ActiveLobbyId}");

		if (!string.IsNullOrEmpty(ActiveLobbyId))
		{
			StartHostWebSocketSignaling(ActiveLobbyId);
			StartHeartbeatLoop(ActiveLobbyId);
		}
	}

	public async Task<bool> JoinLobbyAsync(string lobbyId)
	{
		InitializeClientState(lobbyId);
		await RunNatTypeTestAsync();

		string clientPublicIp = _hostPublicIp ?? "127.0.0.1";
		int clientPublicPort = _hostPublicPort > 0 ? _hostPublicPort : ENetPort;

		try
		{
			var response = await SendJoinRequestAsync(lobbyId, clientPublicIp, clientPublicPort);
			if (response == null || !response.IsSuccessStatusCode)
			{
				var errorText = response != null ? await response.Content.ReadAsStringAsync() : "All registry nodes offline";
				GD.PrintErr($"[LobbyManager] Failed to join lobby: {errorText}");
				ConnectionFailed?.Invoke("Failed to coordinate join.");
				return false;
			}

			var (connectIp, connectPort) = await ResolveHostConnectionInfoAsync(response, clientPublicIp);
			_connectedHostIp = connectIp;
			_connectedHostPort = connectPort;

			return await EstablishClientConnectionAsync(connectIp, connectPort);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[LobbyManager] Join error: {ex.Message}");
			ConnectionFailed?.Invoke(ex.Message);
			return false;
		}
	}

	public async Task<bool> JoinByIpAsync(string targetIp, int targetPort = DefaultGamePort)
	{
		string directLobbyId = $"direct-{Guid.NewGuid():N}";
		InitializeClientState(directLobbyId);
		_connectedHostIp = targetIp;
		_connectedHostPort = targetPort;
		return await EstablishClientConnectionAsync(targetIp, targetPort);
	}

	private void InitializeClientState(string lobbyId)
	{
		IsHost = false;
		IsGameStarted = false;
		ActiveLobbyId = lobbyId;
		PlayerList.Clear();
	}

	private async Task<HttpResponseMessage?> SendJoinRequestAsync(string lobbyId, string clientPublicIp, int clientPublicPort)
	{
		var joinPayload = new
		{
			LobbyId = lobbyId,
			ClientPublicIP = clientPublicIp,
			ClientPublicPort = clientPublicPort
		};

		GD.Print($"[LobbyManager] Joining Lobby {lobbyId} via registry server...");
		HttpResponseMessage? response = null;
		for (int i = 0; i < RegistryServers.Count; i++)
		{
			try
			{
				var jsonContent = new StringContent(JsonSerializer.Serialize(joinPayload), Encoding.UTF8, "application/json");
				response = await _httpClient.PostAsync($"{RegistryServerUrl}/lobbies/join", jsonContent);
				if (response.IsSuccessStatusCode) break;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[LobbyManager] Join failed on {RegistryServerUrl}: {ex.Message}");
			}

			SwitchToNextServer();
		}

		return response;
	}

	private async Task<(string Ip, int Port)> ResolveHostConnectionInfoAsync(HttpResponseMessage response, string clientPublicIp)
	{
		var respText = await response.Content.ReadAsStringAsync();
		using var doc = JsonDocument.Parse(respText);

		string hostIp = doc.RootElement.GetProperty("hostIP").GetString() ?? "";
		int hostPort = doc.RootElement.GetProperty("hostPort").GetInt32();

		string? localIp = null;
		if (doc.RootElement.TryGetProperty("localIP", out var localIpProp))
		{
			localIp = localIpProp.GetString();
		}

		string connectIp = hostIp;
		int connectPort = hostPort;
		if (!string.IsNullOrEmpty(localIp) && hostIp == clientPublicIp)
		{
			GD.Print($"[LobbyManager] Host is on the same LAN (Public IP: {hostIp}). Connecting to local IP: {localIp}:{ENetPort}");
			connectIp = localIp;
			connectPort = ENetPort;
		}

		return (connectIp, connectPort);
	}

	private async Task<bool> EstablishClientConnectionAsync(string connectIp, int connectPort)
	{
		bool isLocalConnection = IsPrivateIp(connectIp);
		int maxAttempts = isLocalConnection ? 1 : 2;

		for (int attempt = 1; attempt <= maxAttempts; attempt++)
		{
			if (attempt > 1)
			{
				GD.Print($"[LobbyManager] Retrying connection to {connectIp}:{connectPort} (Attempt {attempt}/{maxAttempts})...");
				if (Multiplayer.MultiplayerPeer != null)
				{
					try
					{
						Multiplayer.MultiplayerPeer.Close();
					}
					catch
					{
					}
					Multiplayer.MultiplayerPeer = null;
				}
				await Task.Delay(500);
			}

			if (!isLocalConnection)
			{
				await UdpHolePuncher.PunchHoleAsync(connectIp, connectPort, ENetPort);
			}

			var peer = new ENetMultiplayerPeer();
			var err = peer.CreateClient(connectIp, connectPort, localPort: isLocalConnection ? 0 : ENetPort);
			if (err != Error.Ok && !isLocalConnection)
			{
				GD.Print($"[LobbyManager] CreateClient on port {ENetPort} failed ({err}), falling back to port 0...");
				err = peer.CreateClient(connectIp, connectPort, localPort: 0);
			}

			if (err != Error.Ok)
			{
				GD.PrintErr($"[LobbyManager] Failed to create ENet Client: {err}");
				ConnectionFailed?.Invoke("Failed to bind network socket.");
				return false;
			}

		var packetPeer = peer.GetPeer(1);
		if (packetPeer != null)
		{
			packetPeer.SetTimeout(32, 30000, 60000);
		}

			Multiplayer.MultiplayerPeer = peer;
			if (Multiplayer is SceneMultiplayer sceneMultiplayer)
			{
				sceneMultiplayer.ServerRelay = false;
			}

			GD.Print($"[LobbyManager] ENet Client initialized. Awaiting handshake with {connectIp}:{connectPort} (Timeout 10s)...");

			using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
			bool connected = await WaitForClientHandshakeAsync(cts.Token);
			if (connected)
			{
				GD.Print($"[LobbyManager] Successfully established handshake with {connectIp}:{connectPort}");
				return true;
			}

			GD.PrintErr($"[LobbyManager] Connection handshake to {connectIp}:{connectPort} timed out on attempt {attempt}.");
		}

		if (Multiplayer.MultiplayerPeer != null)
		{
			try
			{
				Multiplayer.MultiplayerPeer.Close();
			}
			catch
			{
			}
			Multiplayer.MultiplayerPeer = null;
		}

		LobbyJoinError = "Direct connection handshake failed. The host could not be reached.";
		ConnectionFailed?.Invoke("Direct connection handshake failed.");
		return false;
	}

	private async Task<bool> WaitForClientHandshakeAsync(CancellationToken cancellationToken)
	{
		var connectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		void OnConnected() => connectedTcs.TrySetResult(true);
		void OnFailed() => connectedTcs.TrySetResult(false);

		Multiplayer.ConnectedToServer += OnConnected;
		Multiplayer.ConnectionFailed += OnFailed;

		try
		{
			var completedTask = await Task.WhenAny(connectedTcs.Task, Task.Delay(-1, cancellationToken));
			return completedTask == connectedTcs.Task && connectedTcs.Task.Result;
		}
		catch
		{
			return false;
		}
		finally
		{
			Multiplayer.ConnectedToServer -= OnConnected;
			Multiplayer.ConnectionFailed -= OnFailed;
		}
	}

	public void UnregisterActiveLobbyFromRegistry()
	{
		if (IsHost && !string.IsNullOrEmpty(ActiveLobbyId) && !string.IsNullOrEmpty(_hostToken))
		{
			string lobbyIdToClose = ActiveLobbyId;
			string tokenToClose = _hostToken;
			Task.Run(async () =>
			{
				try
				{
					var payload = new { LobbyId = lobbyIdToClose, HostToken = tokenToClose };
					var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
					await _httpClient.PostAsync($"{RegistryServerUrl}/lobbies/close", jsonContent);
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[LobbyManager] Failed to close lobby {lobbyIdToClose} on server: {ex.Message}");
				}
			});
			_hostToken = null;
		}

		Diagnostics.StopHostListener();
		GameContext.Upnp.ClosePort(ENetPort);
		_wsCts?.Cancel();
		_hostWebSocket?.Dispose();
		_hostWebSocket = null;
		ActiveLobbyId = null;
	}

	public void Disconnect()
	{
		GD.Print("[LobbyManager] Disconnecting...");
		_countdownRemaining = 0;
		_countdownMapName = null;
		StopHostDiagnosticsTimer();
		_chatHistory.Clear();

		UnregisterActiveLobbyFromRegistry();


		foreach (var kvp in _hostTransfers)
		{
			kvp.Value.Cts.Cancel();
		}

		_hostTransfers.Clear();
		_activeEphemeralTransferPeerId = 0;

		if (_currentClientTransfer != null)
		{
			try
			{
				_currentClientTransfer.CurrentChunkStream.Dispose();
			}
			catch
			{
			}

			_currentClientTransfer = null;
		}

		if (Multiplayer.MultiplayerPeer != null)
		{
			Multiplayer.MultiplayerPeer.Close();
			Multiplayer.MultiplayerPeer = null;
		}

		_isConnectedToHost = false;
		IsHost = false;
		IsGameStarted = false;
		PlayerList.Clear();
		PlayerListUpdated?.Invoke();
	}


	private void StartHostWebSocketSignaling(string lobbyId)
	{
		_wsCts = new CancellationTokenSource();
		var token = _wsCts.Token;

		Task.Run(async () =>
		{
			_hostWebSocket = new ClientWebSocket();
			var wsUrl = RegistryServerUrl.Replace("http://", "ws://").Replace("https://", "wss://");
			var uri = new Uri($"{wsUrl}/lobbies/ws?lobbyId={lobbyId}");

			try
			{
				await _hostWebSocket.ConnectAsync(uri, token);
				GD.Print("[LobbyManager] Host WebSocket signaling connected.");
				await ProcessWebSocketMessagesAsync(token);
			}
			catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
			{
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[LobbyManager] Host WebSocket error: {ex.Message}");
			}
		}, token);
	}

	private async Task ProcessWebSocketMessagesAsync(CancellationToken token)
	{
		byte[] buffer = new byte[1024 * 4];
		while (_hostWebSocket!.State == WebSocketState.Open && !token.IsCancellationRequested)
		{
			var result = await _hostWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
			if (result.MessageType == WebSocketMessageType.Close) break;

			if (result.MessageType == WebSocketMessageType.Text)
			{
				var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
				HandleWebSocketMessage(json);
			}
		}
		GD.Print("[LobbyManager] Host WebSocket message processing loop ended.");
	}

	private void HandleWebSocketMessage(string json)
	{
		try
		{
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;

			if (root.GetProperty("Action").GetString() == "Punch")
			{
				string clientIp = root.GetProperty("ClientIP").GetString() ?? "";
				int clientPort = root.GetProperty("ClientPort").GetInt32();

				GD.Print($"[LobbyManager] WebSocket signal: incoming client. Scheduling punch to {clientIp}:{clientPort} on main thread...");
				Callable.From(() => PunchHostToClient(clientIp, clientPort)).CallDeferred();
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[LobbyManager] Error handling WebSocket message: {ex.Message}");
		}
	}

	private async void PunchHostToClient(string clientIp, int clientPort)
	{
		try
		{
			byte[] punchPacket = Encoding.UTF8.GetBytes("PUNCH");
			int initialPeerCount = PlayerList.Count;

			for (int burst = 0; burst < 20; burst++)
			{
				if (!IsHost || Multiplayer.MultiplayerPeer is not ENetMultiplayerPeer enetPeer)
				{
					break;
				}

				var host = enetPeer.GetHost();
				if (host == null)
				{
					break;
				}

				for (int i = 0; i < 4; i++)
				{
					host.SocketSend(clientIp, clientPort, punchPacket);
				}

				if (burst == 0)
				{
					GD.Print($"[LobbyManager] Sent initial native ENet socket punch pulse to {clientIp}:{clientPort} from port {ENetPort}");
				}

				if (PlayerList.Count > initialPeerCount)
				{
					GD.Print($"[LobbyManager] Client connected. Stopping punch pulse to {clientIp}:{clientPort}.");
					break;
				}

				var tree = GetTree();
				if (tree == null)
				{
					break;
				}

				await ToSignal(tree.CreateTimer(0.25f), SceneTreeTimer.SignalName.Timeout);
			}

			GD.Print($"[LobbyManager] Completed punch pulsing to {clientIp}:{clientPort}");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[LobbyManager] Native ENet socket punch exception: {ex.Message}");
		}
	}

	private void StartHeartbeatLoop(string lobbyId)
	{
		var token = _wsCts!.Token;
		Task.Run(async () =>
		{
			while (!token.IsCancellationRequested)
			{
				try
				{
					var heartbeat = new { LobbyId = lobbyId, SlotsUsed = PlayerList.Count, IsGameInProgress = IsGameStarted };
					var jsonContent = new StringContent(JsonSerializer.Serialize(heartbeat), Encoding.UTF8, "application/json");
					await _httpClient.PostAsync($"{RegistryServerUrl}/lobbies/heartbeat", jsonContent, token);
				}
				catch
				{
					/* Ignore heartbeat errors */
				}

				await Task.Delay(1000, token); // every 1s
			}
		}, token);
	}


	private void OnPeerConnected(long peerId)
	{
		int id = (int)peerId;
		GD.Print($"[LobbyManager] Peer connected ENet ID: {id}");

		if (IsHost)
		{
			if (string.IsNullOrEmpty(ActiveLobbyId))
			{
				return;
			}

			if (IsGameStarted)
			{
				GD.Print($"[LobbyManager] Peer {id} connected during active match. Awaiting reconnect handshake.");
				return;
			}

			if (PlayerList.Count >= MaxPlayers)
			{
				string rejectionReason = string.Format(Tr("Lobby is full ({0} of {0} playable slots in use)."), MaxPlayers);
				LastLobbyWarning = rejectionReason;
				GD.Print($"[LobbyManager] Rejecting peer {id}: {rejectionReason}");
				RpcId(id, nameof(RejectConnection), rejectionReason);


				var timer = GetTree().CreateTimer(0.1f);
				timer.Timeout += () =>
				{
					if (Multiplayer.MultiplayerPeer != null)
					{
						Multiplayer.MultiplayerPeer.DisconnectPeer(id);
					}
				};
				return;
			}


			int nextSlot = FindNextFreePlayableSlot();
			if (nextSlot < 0)
			{
				LastLobbyWarning = Tr("No free playable player slot is available.");
				RpcId(id, nameof(RejectConnection), LastLobbyWarning);

				var slotTimer = GetTree().CreateTimer(0.1f);
				slotTimer.Timeout += () =>
				{
					if (Multiplayer.MultiplayerPeer != null)
					{
						Multiplayer.MultiplayerPeer.DisconnectPeer(id);
					}
				};
				return;
			}

			var newPlayer = new PlayerInfo
			{
				PeerId = id,
				Slot = nextSlot,
				Name = $"Player_{id}",
				Faction = "HUMAN",
				Team = ResolveJoinTeamName(),
				Color = GetNextColor(),
				IsHost = false,
				BinaryVersion = RealmVersion.GameBinaryVersion,
				IsMapReady = false,
				SessionToken = _pendingSessionTokens.Remove(id, out string? joinedToken) ? joinedToken : Guid.NewGuid().ToString()
			};
			PlayerList.Add(newPlayer);
			if (joinedToken != null) _peersWithRegisteredSessionTokens.Add(id);
			SendChatMessage("System", string.Format(Tr("{0} joined the lobby."), newPlayer.Name));


			UpdateAllPeerDiagnostics();
			BroadcastPlayerList();
			RpcId(id, nameof(RequestSessionToken));
			RpcId(id, nameof(SyncSpectatorDelay), SpectatorDelay);
			RpcId(id, nameof(SyncHostStability), HostStability);
			RpcId(id, nameof(SyncActiveMap), ActiveMapName);
		}
	}

	private string ResolveJoinTeamName()
	{
		int teamOneHumans = 0;
		int teamTwoHumans = 0;
		foreach (var player in PlayerList)
		{
			if (player.PeerId < 0) continue;
			if (player.Team == "Team 1") teamOneHumans++;
			else if (player.Team == "Team 2") teamTwoHumans++;
		}

		return teamTwoHumans < teamOneHumans ? "Team 2" : "Team 1";
	}

	private void OnPeerDisconnected(long peerId)
	{
		int id = (int)peerId;
		GD.Print($"[LobbyManager] Peer disconnected ENet ID: {id}");

		if (IsHost)
		{
			foreach (var kvp in _hostTransfers)
			{
				if (kvp.Value.PeerId == id)
				{
					kvp.Value.Cts.Cancel();
					_hostTransfers.TryRemove(kvp.Key, out _);
				}
			}

			if (_activeEphemeralTransferPeerId == id)
			{
				_activeEphemeralTransferPeerId = 0;
			}

			_pendingSessionTokens.Remove(id);
			_peersWithRegisteredSessionTokens.Remove(id);

			if (string.IsNullOrEmpty(ActiveLobbyId))
			{
				return;
			}

			if (IsGameStarted)
			{
				var player = PlayerList.Find(p => p.PeerId == id);
				if (player != null)
				{
					player.IsDisconnected = true;
					GD.Print($"[LobbyManager] In-game peer {id} ({player.Name}) marked as disconnected for reconnect. Slot {player.Slot} preserved.");
					SendChatMessage("System", string.Format(Tr("{0} disconnected. Waiting for reconnect..."), player.Name));
					BroadcastPlayerList();
				}

				return;
			}

			int removedIdx = PlayerList.FindIndex(p => p.PeerId == id);
			if (removedIdx >= 0)
			{
				var leavingPlayer = PlayerList[removedIdx];
				string name = leavingPlayer.Name;
				PlayerList.RemoveAt(removedIdx);

				for (int i = 0; i < PlayerList.Count; i++)
				{
					PlayerList[i].Slot = i + 1;
				}

				UpdateAllPeerDiagnostics();
				SendChatMessage("System", string.Format(Tr("{0} left the lobby."), name));
			}
		}
	}

	private void OnConnectedToServer()
	{
		int myId = Multiplayer.GetUniqueId();
		GD.Print($"[LobbyManager] Connected to Host. Assigned local ENet ID: {myId}");
		_isConnectedToHost = true;
		if (!IsHost) Callable.From(() => RpcId(1, nameof(RegisterSessionToken), _persistentSessionToken)).CallDeferred();
	}

	/// <summary>
	/// The host generates a slot for a connecting peer before that peer has said anything, so the session identity a
	/// later reconnect depends on has to be supplied by the peer itself. Without this the host owns a token the client
	/// never saw, and every reconnect is rejected as a session that no player in the match owns.
	/// </summary>
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RegisterSessionToken(string sessionToken)
	{
		if (!IsHost) return;

		int senderId = Multiplayer.GetRemoteSenderId();
		if (senderId <= 1) return;

		if (!Guid.TryParseExact(sessionToken, "D", out _))
		{
			GD.PrintErr($"[LobbyManager] Ignored session token from peer {senderId}: not a session identifier.");
			return;
		}

		PlayerInfo? existingOwner = PlayerList.Find(p => p.PeerId != senderId && !string.IsNullOrEmpty(p.SessionToken) && string.Equals(p.SessionToken, sessionToken, StringComparison.Ordinal));
		if (existingOwner != null)
		{
			GD.PrintErr($"[LobbyManager] Ignored session token from peer {senderId}: slot {existingOwner.Slot} already owns it.");
			return;
		}

		PlayerInfo? player = PlayerList.Find(p => p.PeerId == senderId);
		if (player == null)
		{
			_pendingSessionTokens[senderId] = sessionToken;
			return;
		}

		player.SessionToken = sessionToken;
		_peersWithRegisteredSessionTokens.Add(senderId);
	}

	/// <summary>
	/// Asks a peer that has just been given a slot to send the session identity it will present on reconnect. The peer
	/// may also volunteer it as soon as it connects; asking here covers a peer whose first message arrived before the
	/// host had a slot to attach it to.
	/// </summary>
	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestSessionToken()
	{
		if (IsHost) return;
		RpcId(1, nameof(RegisterSessionToken), _persistentSessionToken);
	}

	public bool HasRegisteredSessionToken(int peerId) => _peersWithRegisteredSessionTokens.Contains(peerId);

	/// <summary>
	/// Resolves the player a reconnect with this session token would be accepted for. The harness uses it to prove that
	/// the identity a peer registered is the identity the host compares against on reconnect.
	/// </summary>
	public bool TryGetReconnectOwnerForHarness(string sessionToken, out int slot)
	{
		slot = -1;
		PlayerInfo? owner = FindReconnectSessionOwner(sessionToken);
		if (owner == null) return false;

		slot = owner.Slot;
		return true;
	}

	public async Task<bool> EnsureMapDownloadedAsync(string? mapName = null)
	{
		string targetMap = !string.IsNullOrWhiteSpace(mapName) ? mapName : ActiveMapName;
		if (string.IsNullOrWhiteSpace(targetMap) || IsHost) return true;

		await _mapDownloadLock.WaitAsync();
		try
		{
			if (MapAssetManager.IsMapDownloaded(targetMap))
			{
				CallDeferred(nameof(EmitDownloadCompleted));
				return true;
			}

			if (await TryDownloadFromHostIfConnected(targetMap)) return true;

			if (MapAssetManager.IsMapDownloaded(targetMap))
			{
				CallDeferred(nameof(EmitDownloadProgress), 1.0f);
				CallDeferred(nameof(EmitDownloadCompleted));
				return true;
			}

			GD.PrintErr($"[LobbyManager] Map download failed for '{targetMap}'.");
			CallDeferred(nameof(EmitDownloadFailed));
			return false;
		}
		finally
		{
			_mapDownloadLock.Release();
		}
	}

	private async Task<bool> TryDownloadFromHostIfConnected(string targetMap)
	{
		if (IsHost || !_isConnectedToHost) return false;
		return await TryDownloadMapFromHostAsync(targetMap);
	}

	private async Task<bool> TryDownloadMapFromHostAsync(string targetMap)
	{
		var manifest = await RequestManifestFromHostAsync(targetMap);
		if (manifest == null) return false;

		var allHashes = manifest.Files != null ? manifest.Files.Values.ToList() : new List<string>();
		int totalManifestFiles = allHashes.Count;
		int maxRetries = 10;
		int retryCount = 0;

		while (retryCount < maxRetries && _isConnectedToHost)
		{
			var missingHashes = await Task.Run(() => MapAssetManager.GetMissingHashes(allHashes));

			if (missingHashes.Count == 0)
			{
				await RegisterDownloadedMapAsync(targetMap, manifest);
				return true;
			}

			float initialProgress = totalManifestFiles > 0 ? Math.Clamp((float)(totalManifestFiles - missingHashes.Count) / totalManifestFiles, 0.0f, 1.0f) : 0.0f;
			CallDeferred(nameof(EmitDownloadProgress), initialProgress);

			bool transferSuccess = await PerformMapAssetTransferAsync(targetMap, manifest, missingHashes, totalManifestFiles);

			if (transferSuccess)
			{
				GD.Print($"[LobbyManager] Map '{targetMap}' successfully extracted and registered.");
				CallDeferred(nameof(EmitDownloadProgress), 1.0f);
				CallDeferred(nameof(EmitDownloadCompleted));
				return true;
			}

			if (!_isConnectedToHost) break;

			retryCount++;
			await Task.Delay(1000);
		}

		return false;
	}

	private async Task<MapManifest?> RequestManifestFromHostAsync(string targetMap)
	{
		GD.Print($"[LobbyManager] Requesting map manifest for '{targetMap}' from host via reliable ENet RPC...");
		_manifestTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

		Callable.From(() => RpcId(1, nameof(RequestMapManifestRpc), targetMap)).CallDeferred();

		var manifestTask = await Task.WhenAny(_manifestTcs.Task, Task.Delay(10000));
		string? manifestJson = manifestTask == _manifestTcs.Task ? _manifestTcs.Task.Result : null;

		if (string.IsNullOrWhiteSpace(manifestJson)) return null;

		try
		{
			return MapManifest.LoadFromJson(manifestJson) ?? JsonSerializer.Deserialize<MapManifest>(manifestJson);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[LobbyManager] Failed to deserialize manifest from host: {ex.Message}");
			return null;
		}
	}

	private async Task RegisterDownloadedMapAsync(string targetMap, MapManifest manifest)
	{
		GD.Print($"[LobbyManager] All assets for '{targetMap}' now exist locally in CAS.");
		string targetMapDir = MapAssetManager.GetMapDirectory(targetMap, manifest.Version ?? "1.0.0");
		await Task.Run(() =>
		{
			MapAssetManager.ExtractManifestFiles(manifest, targetMapDir);
			string localManifestPath = Path.Combine(targetMapDir, "manifest.json");
			GameContext.AssetIndexService.RegisterManifest(manifest, localManifestPath);
		});

		CallDeferred(nameof(EmitDownloadProgress), 1.0f);
		CallDeferred(nameof(EmitDownloadCompleted));
	}

	private async Task<bool> PerformMapAssetTransferAsync(string targetMap, MapManifest manifest, List<string> missingHashes, int totalManifestFiles)
	{
		GD.Print($"[LobbyManager] Missing {missingHashes.Count}/{totalManifestFiles} assets for '{targetMap}'. Requesting compressed zstd bundle from host...");
		string transferId = Guid.NewGuid().ToString("N");

		_currentClientTransfer = new ClientTransferSession
		{
			TransferId = transferId,
			MapName = targetMap,
			MapVersion = manifest.Version ?? "1.0.0",
			Manifest = manifest,
			CurrentChunkStream = new MemoryStream(),
			TotalAssetsInManifest = totalManifestFiles,
			AlreadyPresentAssets = totalManifestFiles - missingHashes.Count,
			SessionMissingAssets = missingHashes.Count,
			LastActivityTimeUtc = DateTime.UtcNow
		};

		_transferCompleteTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		Callable.From(() => RpcId(1, nameof(RequestMapAssetTransferRpc), transferId, targetMap, manifest.Version ?? "1.0.0", missingHashes.ToArray())).CallDeferred();

		bool transferSuccess = await MonitorTransferProgressAsync(_currentClientTransfer);

		if (!transferSuccess && _currentClientTransfer != null)
		{
			try
			{
				_currentClientTransfer.CurrentChunkStream.Dispose();
			}
			catch
			{
			}

			_currentClientTransfer = null;
		}

		if (transferSuccess) _currentClientTransfer = null;

		return transferSuccess;
	}

	private async Task<bool> MonitorTransferProgressAsync(ClientTransferSession session)
	{
		while (!_transferCompleteTcs!.Task.IsCompleted)
		{
			if (!_isConnectedToHost || Multiplayer.MultiplayerPeer == null)
			{
				GD.PrintErr("[LobbyManager] Disconnected from host during map download.");
				return false;
			}

			var completedTask = await Task.WhenAny(_transferCompleteTcs.Task, Task.Delay(2000));
			if (completedTask == _transferCompleteTcs.Task) return _transferCompleteTcs.Task.Result;

			if (DateTime.UtcNow - session.LastActivityTimeUtc > TimeSpan.FromSeconds(45))
			{
				GD.PrintErr("[LobbyManager] ENet transfer inactivity timeout for chunk. Retrying remaining missing assets...");
				return false;
			}
		}

		return false;
	}

	public async Task<bool> DownloadMapEphemerallyAsync(
		string targetIp,
		int targetPort,
		string mapName,
		Action<float>? progressCallback = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(mapName) || string.IsNullOrWhiteSpace(targetIp) || targetPort <= 0) return false;

		if (MapAssetManager.IsMapDownloaded(mapName))
		{
			EmitMapDownloadComplete(progressCallback);
			return true;
		}

		bool wasSeeding = StopSeedingIfActive();
		Action<float>? progressHandler = SetupProgressHandler(progressCallback);

		try
		{
			var peer = await EstablishEphemeralConnectionAsync(targetIp, targetPort, cancellationToken);
			if (peer == null) return false;

			_isConnectedToHost = true;
			bool downloadResult = await EnsureMapDownloadedAsync(mapName);

			CleanupEphemeralConnection(peer);
			return downloadResult;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[LobbyManager] Ephemeral map download error: {ex.Message}");
			return false;
		}
		finally
		{
			TeardownEphemeralState(progressHandler, wasSeeding);
		}
	}

	private void EmitMapDownloadComplete(Action<float>? progressCallback)
	{
		progressCallback?.Invoke(1.0f);
		MapDownloadProgressChanged?.Invoke(1.0f);
		MapDownloadCompleted?.Invoke();
	}

	private bool StopSeedingIfActive()
	{
		bool wasSeeding = GameContext.PeerSeeder != null && GameContext.PeerSeeder.IsSeeding;
		if (wasSeeding)
		{
			GameContext.PeerSeeder!.Stop();
		}

		return wasSeeding;
	}

	private Action<float>? SetupProgressHandler(Action<float>? progressCallback)
	{
		if (progressCallback == null) return null;
		Action<float> handler = p => progressCallback.Invoke(p);
		MapDownloadProgressChanged += handler;
		return handler;
	}

	private async Task<ENetMultiplayerPeer?> EstablishEphemeralConnectionAsync(string targetIp, int targetPort, CancellationToken cancellationToken)
	{
		bool isLocal = targetIp == "127.0.0.1" || targetIp == "localhost" || targetIp == PublicIP;
		int clientPort = ENetPort + 16;
		if (!isLocal) await UdpHolePuncher.PunchHoleAsync(targetIp, targetPort, clientPort);

		var peer = new ENetMultiplayerPeer();
		int localPortToUse = isLocal ? 0 : clientPort;
		var err = peer.CreateClient(targetIp, targetPort, localPort: localPortToUse);
		if (err != Error.Ok && localPortToUse != 0)
		{
			err = peer.CreateClient(targetIp, targetPort, localPort: 0);
		}

		if (err != Error.Ok)
		{
			GD.PrintErr($"[LobbyManager] Failed to create ephemeral ENet client: {err}");
			return null;
		}

		var packetPeer = peer.GetPeer(1);
		if (packetPeer != null) packetPeer.SetTimeout(32, 5000, 15000);

		_isConnectedToHost = false;
		Multiplayer.MultiplayerPeer = peer;

		if (!await WaitForEphemeralConnectionAsync(cancellationToken))
		{
			GD.PrintErr($"[LobbyManager] Ephemeral connection to {targetIp}:{targetPort} timed out or failed.");
			CleanupEphemeralConnection(peer);
			return null;
		}

		return peer;
	}

	private async Task<bool> WaitForEphemeralConnectionAsync(CancellationToken cancellationToken)
	{
		var connectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		void OnConnected() => connectedTcs.TrySetResult(true);
		void OnFailed() => connectedTcs.TrySetResult(false);

		Multiplayer.ConnectedToServer += OnConnected;
		Multiplayer.ConnectionFailed += OnFailed;

		var connectTask = await Task.WhenAny(connectedTcs.Task, Task.Delay(8000, cancellationToken));

		Multiplayer.ConnectedToServer -= OnConnected;
		Multiplayer.ConnectionFailed -= OnFailed;

		return connectTask == connectedTcs.Task && connectedTcs.Task.Result;
	}

	private void CleanupEphemeralConnection(ENetMultiplayerPeer peer)
	{
		try
		{
			peer.Close();
		}
		catch
		{
		}

		Multiplayer.MultiplayerPeer = null;
		_isConnectedToHost = false;
	}

	private void TeardownEphemeralState(Action<float>? progressHandler, bool wasSeeding)
	{
		if (progressHandler != null)
		{
			MapDownloadProgressChanged -= progressHandler;
		}

		if (Multiplayer.MultiplayerPeer != null)
		{
			try
			{
				Multiplayer.MultiplayerPeer.Close();
			}
			catch
			{
			}

			Multiplayer.MultiplayerPeer = null;
		}

		_isConnectedToHost = false;

		if (wasSeeding)
		{
			GameContext.PeerSeeder?.CheckIdleAndSeedStatus();
		}
	}


	private void EmitDownloadProgress(float progress)
	{
		if (progress == 0.0f)
		{
			ReportLocalMapReadyState(false);
		}

		MapDownloadProgressChanged?.Invoke(progress);
	}

	private void EmitDownloadCompleted()
	{
		ReportLocalMapReadyState(true);
		MapDownloadCompleted?.Invoke();
	}

	private void EmitDownloadFailed()
	{
		MapDownloadFailed?.Invoke();
	}

	private void OnConnectionFailedGodot()
	{
		_isConnectedToHost = false;
		GD.PrintErr("[LobbyManager] Godot ENet connection failed.");
		ConnectionFailed?.Invoke("Direct connection handshake failed.");
	}

	private void OnServerDisconnectedGodot()
	{
		_isConnectedToHost = false;
		GD.Print("[LobbyManager] Host disconnected.");
		KickReceived?.Invoke("Host closed the server.");
		Disconnect();
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RequestReconnect(string sessionToken, string playerName, string binaryVersion)
	{
		if (!IsHost) return;
		int senderId = Multiplayer.GetRemoteSenderId();
		GD.Print($"[LobbyManager] RequestReconnect received from Peer {senderId}: TokenPresent={!string.IsNullOrEmpty(sessionToken)}, Name={playerName}");

		if (!IsGameStarted)
		{
			RpcId(senderId, nameof(RejectConnection), Tr("Game is not in progress."));
			return;
		}

		if (!string.Equals(binaryVersion, RealmVersion.GameBinaryVersion, StringComparison.Ordinal))
		{
			RpcId(senderId, nameof(RejectConnection), Tr("Game version mismatch with host."));
			return;
		}

		PlayerInfo? targetPlayer = FindReconnectSessionOwner(sessionToken);
		if (targetPlayer == null)
		{
			RpcId(senderId, nameof(RejectConnection), Tr("Reconnect rejected: no player in this match owns that session."));
			return;
		}

		int oldPeerId = targetPlayer.PeerId;
		if (oldPeerId != senderId && Multiplayer.GetPeers().Contains(oldPeerId))
		{
			RpcId(senderId, nameof(RejectConnection), Tr("Reconnect rejected: that player is already connected to this match."));
			return;
		}

		targetPlayer.PeerId = senderId;
		targetPlayer.IsDisconnected = false;
		_peersWithRegisteredSessionTokens.Remove(oldPeerId);
		_peersWithRegisteredSessionTokens.Add(senderId);

		GD.Print($"[LobbyManager] Reconnected player {targetPlayer.Name} (Slot {targetPlayer.Slot}). Remapped old peer {oldPeerId} -> {senderId}");
		SendChatMessage("System", string.Format(Tr("{0} reconnected to the match."), targetPlayer.Name));
		BroadcastPlayerList();

		string serializedPlayers = JsonSerializer.Serialize(PlayerList);
		RpcId(senderId, nameof(AcceptReconnect), targetPlayer.Slot, ActiveMapName, serializedPlayers, MatchSeed);
	}

	private PlayerInfo? FindReconnectSessionOwner(string sessionToken)
	{
		if (string.IsNullOrEmpty(sessionToken)) return null;
		return PlayerList.Find(p => !string.IsNullOrEmpty(p.SessionToken) && string.Equals(p.SessionToken, sessionToken, StringComparison.Ordinal));
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void AcceptReconnect(int slot, string activeMapName, string serializedPlayers, int matchSeed)
	{
		GD.Print($"[LobbyManager] AcceptReconnect received! Slot={slot}, Map={activeMapName}");
		_isConnectedToHost = true;
		ActiveMapName = activeMapName;
		MatchSeed = matchSeed;
		GameContext.RandomService.SetSeed(matchSeed);
		IsGameStarted = true;

		try
		{
			var list = JsonSerializer.Deserialize<List<PlayerInfo>>(serializedPlayers);
			if (list != null)
			{
				PlayerList.Clear();
				PlayerList.AddRange(list);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[LobbyManager] Failed to deserialize players on reconnect: {ex.Message}");
		}

		int myId = Multiplayer.GetUniqueId();

		var me = PlayerList.Find(p => p.Slot == slot);
		if (me != null)
		{
			me.PeerId = myId;
			me.IsDisconnected = false;
		}

		PlayerListUpdated?.Invoke();

		if (GameContext.Host != null && GodotObject.IsInstanceValid(GameContext.Host))
		{
			// The match is run by its own process, so a peer that comes back asks that process for the world again
			// rather than being handed one by whichever peer used to hold it.
			GameContext.Host.RequestSnapshotBaseline();
		}
		else
		{
			GetTree().ChangeSceneToFile("res://GameHost.tscn");
		}
	}


	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SyncLobbyData(string serializedData)
	{
		GD.Print($"[LobbyManager] SyncLobbyData received: {serializedData}");
		try
		{
			var newList = JsonSerializer.Deserialize<List<PlayerInfo>>(serializedData);
			if (newList != null)
			{
				PlayerList.Clear();
				PlayerList.AddRange(newList);

				PlayerListUpdated?.Invoke();
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[LobbyManager] Failed to deserialize sync: {ex.Message}");
			CallDeferred(nameof(HandleSyncDeserializationFailure));
		}
	}

	private void HandleSyncDeserializationFailure()
	{
		Disconnect();
		LobbyJoinError = "Error joining lobby: Game version mismatch with host";
		GameContext.UserInterface.TransitionTo(GameScreen.LobbyBrowser);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RejectConnection(string reason)
	{
		GD.Print($"[LobbyManager] Connection Rejected: {reason}");

		ConnectionFailed?.Invoke(reason);
		Disconnect();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SyncActiveMap(string mapName)
	{
		ActiveMapName = mapName;
		ActiveMapChanged?.Invoke(mapName);
		if (!IsHost)
		{
			_ = EnsureMapDownloadedAsync(mapName);
		}
	}

	public void UpdateActiveMap(string mapName)
	{
		if (IsHost)
		{
			ActiveMapName = mapName;
			Rpc(nameof(SyncActiveMap), mapName);
			ActiveMapChanged?.Invoke(mapName);
		}
	}

	private readonly TokenBucket _slotUpdateBudget = new(12, 4);
	private readonly TokenBucket _diagnosticsBudget = new(30, 10);
	private readonly TokenBucket _chatBudget = new(10, 2);
	private readonly Dictionary<int, double> _lastPlayerListBroadcastForDiagnostics = new();
	private const double DiagnosticsBroadcastIntervalSeconds = 1.0;
	private const int MaximumChatMessageLength = 400;

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void UpdatePlayerSlot(int peerId, string faction, string team, Color color, string name)
	{
		if (!IsHost)
		{
			RpcId(1, nameof(UpdatePlayerSlot), peerId, faction, team, color, name);
			return;
		}

		int senderId = Multiplayer.GetRemoteSenderId();
		int targetPeerId = senderId > 0 ? senderId : peerId;
		if (senderId > 0 && peerId > 0 && peerId != senderId)
		{
			GD.PushWarning($"[LobbyManager] Peer {senderId} tried to change slot of peer {peerId}; the sender identity wins.");
		}

		if (!_slotUpdateBudget.TryConsume(1))
		{
			GD.PushWarning($"[LobbyManager] Dropped slot update from peer {targetPeerId}: above the lobby rate limit.");
			return;
		}

		var p = PlayerList.Find(x => x.PeerId == targetPeerId);
		if (p == null) return;

		p.Faction = SanitizeLobbyText(faction, 48);
		p.Team = SanitizeLobbyText(team, 48);
		p.Color = color;
		p.Name = SanitizeLobbyText(name, 32);
		BroadcastPlayerList();
	}

	private static string SanitizeLobbyText(string? value, int maximumLength)
	{
		if (string.IsNullOrEmpty(value)) return string.Empty;

		int length = Math.Min(value.Length, maximumLength);
		var builder = new StringBuilder(length);
		for (int index = 0; index < length; index++)
		{
			char character = value[index];
			if (char.IsControl(character)) continue;
			builder.Append(character);
		}

		return builder.ToString();
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void UpdateDiagnostics(int peerId, float minRtt, float maxRtt, float avgRtt, float jitter, float lossRate, int consecutiveLoss)
	{
		if (!IsHost)
		{
			RpcId(1, nameof(UpdateDiagnostics), peerId, minRtt, maxRtt, avgRtt, jitter, lossRate, consecutiveLoss);
			return;
		}

		int senderId = Multiplayer.GetRemoteSenderId();
		int targetPeerId = senderId > 0 ? senderId : peerId;
		if (!_diagnosticsBudget.TryConsume(1))
		{
			GD.PushWarning($"[LobbyManager] Dropped diagnostics from peer {targetPeerId}: above the lobby rate limit.");
			return;
		}

		var p = PlayerList.Find(x => x.PeerId == targetPeerId);
		if (p == null) return;

		p.Latency = $"{Math.Round(Math.Clamp(avgRtt, 0f, 60000f))} ms";
		p.Jitter = $"{Math.Round(Math.Clamp(jitter, 0f, 60000f))} ms";
		p.PacketLoss = $"{Math.Round(Math.Clamp(lossRate, 0f, 100f))}% (Burst: {Math.Clamp(consecutiveLoss, 0, 100000)})";

		double now = Time.GetTicksMsec() / 1000.0;
		if (_lastPlayerListBroadcastForDiagnostics.TryGetValue(targetPeerId, out double lastBroadcast)
			&& now - lastBroadcast < DiagnosticsBroadcastIntervalSeconds)
		{
			return;
		}

		_lastPlayerListBroadcastForDiagnostics[targetPeerId] = now;
		BroadcastPlayerList();
	}

	public void UpdateReadyState(int peerId, bool isReady)
	{
		if (IsHost)
		{
			var p = PlayerList.Find(x => x.PeerId == peerId);
			if (p != null)
			{
				p.IsReady = isReady;
				BroadcastPlayerList();
			}
		}
		else
		{
			var p = PlayerList.Find(x => x.PeerId == peerId);
			if (p != null)
			{
				p.IsReady = isReady;
			}

			RpcId(1, nameof(UpdateReadyStateOnHost), peerId, isReady);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void UpdateReadyStateOnHost(int peerId, bool isReady)
	{
		if (IsHost)
		{
			var p = PlayerList.Find(x => x.PeerId == peerId);
			if (p != null)
			{
				p.IsReady = isReady;
				BroadcastPlayerList();
			}
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReportMapReadyOnHost(int peerId, bool isMapReady)
	{
		if (IsHost)
		{
			int senderId = Multiplayer.GetRemoteSenderId();
			int targetPeerId = senderId > 0 ? senderId : peerId;
			var p = PlayerList.Find(x => x.PeerId == targetPeerId || (peerId > 0 && x.PeerId == peerId));
			if (p != null)
			{
				p.IsMapReady = isMapReady;
				BroadcastPlayerList();
			}
		}
	}

	public void ReportLocalMapReadyState(bool isMapReady)
	{
		if (!IsHost)
		{
			int myPeerId = Multiplayer.GetUniqueId();
			var me = PlayerList.Find(p => p.PeerId == myPeerId);
			if (me != null)
			{
				me.IsMapReady = isMapReady;
			}
			if (_isConnectedToHost && Multiplayer.MultiplayerPeer != null)
			{
				try
				{
					RpcId(1, nameof(ReportMapReadyOnHost), myPeerId, isMapReady);
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[LobbyManager] Failed to report map ready state: {ex.Message}");
				}
			}
		}
	}

	private string GetPlayerTeamByName(string name)
	{
		foreach (var player in PlayerList)
		{
			if (player.Name == name) return player.Team;
		}

		return "Team 1";
	}

	/// <summary>
	///     The players this match is played by, as this lobby listed them. An authority seats them before its map's script
	///     runs, so the seats a map spawns starting units for are the seats the match actually has: a bot the lobby
	///     added is a computer player from the first tick rather than an empty slot nobody fills.
	/// </summary>
	private List<MatchRosterSeat> BuildMatchRoster()
	{
		var seats = new List<MatchRosterSeat>(PlayerList.Count);
		for (int index = 0; index < PlayerList.Count; index++)
		{
			PlayerInfo player = PlayerList[index];
			if (player.Slot <= 0) continue;

			bool isComputer = string.Equals(player.Faction, "AI", StringComparison.OrdinalIgnoreCase) || player.PeerId < 0;
			seats.Add(new MatchRosterSeat(player.Slot, player.Name, ResolveSeatTeam(player.Team, player.Slot), player.Team, isComputer));
		}

		seats.Sort((left, right) => left.Slot.CompareTo(right.Slot));
		return seats;
	}

	/// <summary>The team number a lobby team name stands for, falling back to the seat's own slot.</summary>
	private static int ResolveSeatTeam(string? teamName, int slot)
	{
		if (!string.IsNullOrWhiteSpace(teamName)
			&& teamName.StartsWith("Team ", StringComparison.OrdinalIgnoreCase)
			&& int.TryParse(teamName.AsSpan(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out int team)
			&& team > 0)
		{
			return team;
		}

		return slot;
	}

	/// <summary>
	///     Shows a line of chat the match relayed. A line is sent to the authority and comes back from it, so this is the
	///     one place a message reaches a screen whether it was typed here or somewhere else.
	/// </summary>
	public void ReceiveChatFromAuthority(string senderName, string message, bool alliesOnly, long timestampUtcTicks)
	{
		ChatReceived?.Invoke(SanitizeLobbyText(senderName, 32), message, alliesOnly, timestampUtcTicks);
	}

	public void SendChatMessage(string senderName, string message, bool alliesOnly = false, long timestampUtcTicks = 0)
	{
		long sentAtUtcTicks = timestampUtcTicks > 0 ? timestampUtcTicks : DateTime.UtcNow.Ticks;

		// Once a match is running, the process that runs it is the one every screen is connected to. A line goes there
		// and is relayed back to everyone it is meant for, which is also how a map's own chat commands reach the map.
		ClientServerSession? matchSession = GameContext.ServerSession;
		if (matchSession != null && matchSession.IsRunning && IsGameStarted && senderName != "System")
		{
			if (matchSession.SendChat(message, alliesOnly)) return;
		}

		if (IsHost)
		{
			if (senderName == "System")
			{
				_chatHistory.Add((senderName, message, false, sentAtUtcTicks));
				Rpc(nameof(ReceiveChatMessage), senderName, message, alliesOnly, sentAtUtcTicks);
				ChatReceived?.Invoke(senderName, message, alliesOnly, sentAtUtcTicks);
			}
			else
			{
				var senderPlayer = PlayerList.Find(p => p.Name == senderName);
				int senderSlot = senderPlayer?.Slot ?? 0;
				ServerChatCommandReceived?.Invoke(senderSlot, message);
				_ = ProcessAndSendChatMessageAsync(senderName, message, alliesOnly, sentAtUtcTicks);
			}
		}
		else
		{
			RpcId(1, nameof(ReceiveChatMessage), senderName, message, alliesOnly, sentAtUtcTicks);
		}
	}

	private static bool _isDownloadingModels = false;

	private static async Task InitializeToxicityModelAsync()
	{
		if (_onnxSession != null || _isDownloadingModels) return;
		_isDownloadingModels = true;

		try
		{
			var (modelPath, vocabPath) = GetLocalModelPaths();

			if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
			{
				(modelPath, vocabPath) = await DownloadModelsAsync();
			}

			if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
			{
				throw new FileNotFoundException("Model file not found.");
			}

			LoadModels(modelPath, vocabPath);
		}
		finally
		{
			_isDownloadingModels = false;
		}
	}

	private static (string modelPath, string vocabPath) GetLocalModelPaths()
	{
		string baseDir = AppDomain.CurrentDomain.BaseDirectory;
		string curDir = Directory.GetCurrentDirectory();

		var candidates = new[]
		{
			(Path.Combine(baseDir, "Assets", "MLModels", "toxic-xlm-roberta", "model_quantized.onnx"),
				Path.Combine(baseDir, "Assets", "MLModels", "toxic-xlm-roberta", "vocab.bin")),

			(Path.Combine(curDir, "Realm.Client", "Assets", "MLModels", "toxic-xlm-roberta", "model_quantized.onnx"),
				Path.Combine(curDir, "Realm.Client", "Assets", "MLModels", "toxic-xlm-roberta", "vocab.bin")),

			(Path.Combine(curDir, "Assets", "MLModels", "toxic-xlm-roberta", "model_quantized.onnx"),
				Path.Combine(curDir, "Assets", "MLModels", "toxic-xlm-roberta", "vocab.bin"))
		};

		foreach (var (mPath, vPath) in candidates)
		{
			if (File.Exists(mPath)) return (mPath, vPath);
		}

		try
		{
			string globalized = PathUtils.GlobalizePath("res://Assets/MLModels/toxic-xlm-roberta/model_quantized.onnx");
			string globalizedVocab = PathUtils.GlobalizePath("res://Assets/MLModels/toxic-xlm-roberta/vocab.bin");
			if (!string.IsNullOrEmpty(globalized) && File.Exists(globalized))
			{
				return (globalized, globalizedVocab);
			}
		}
		catch
		{
		}

		return ("", "");
	}

	private static async Task<(string modelPath, string vocabPath)> DownloadModelsAsync()
	{
		string globalizedUserDir = ProjectSettings.GlobalizePath("user://Assets/MLModels/toxic-xlm-roberta");
		if (!Directory.Exists(globalizedUserDir)) Directory.CreateDirectory(globalizedUserDir);

		string modelPath = Path.Combine(globalizedUserDir, "model_quantized.onnx");
		string vocabPath = Path.Combine(globalizedUserDir, "vocab.bin");

		try
		{
			if (!File.Exists(modelPath) || !File.Exists(vocabPath))
			{
				GD.Print("[LobbyManager] Downloading toxicity models...");
				using var httpClient = new HttpClient();

				if (!File.Exists(modelPath))
				{
					var modelBytes = await httpClient.GetByteArrayAsync("https://huggingface.co/hoan/multilingual-toxic-xlm-roberta-dynamic-quantized/resolve/main/model_quantized.onnx");
					File.WriteAllBytes(modelPath, modelBytes);
				}

				if (!File.Exists(vocabPath))
				{
					var vocabBytes = await httpClient.GetByteArrayAsync("https://huggingface.co/hoan/multilingual-toxic-xlm-roberta-dynamic-quantized/resolve/main/vocab.bin");
					File.WriteAllBytes(vocabPath, vocabBytes);
				}

				GD.Print("[LobbyManager] Successfully downloaded toxicity models.");
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[LobbyManager] Error downloading toxicity models: {ex}");
		}

		return (modelPath, vocabPath);
	}

	private static void LoadModels(string modelPath, string vocabPath)
	{
		lock (_sessionLock)
		{
			_onnxSession = new InferenceSession(modelPath);

			using var fs = new FileStream(vocabPath, FileMode.Open, FileAccess.Read, FileShare.Read);
			using var reader = new BinaryReader(fs, Encoding.UTF8);

			int count = reader.ReadInt32();
			_vocab = new Dictionary<string, int>(count);
			for (int i = 0; i < count; i++)
			{
				string token = reader.ReadString();
				_vocab[token] = i;
			}
		}
	}

	private async Task<bool> IsMessageToxicAsync(string message)
	{
		if (string.IsNullOrWhiteSpace(message)) return false;

		try
		{
			if (_onnxSession == null || _vocab == null)
			{
				await InitializeToxicityModelAsync();
			}

			return await Task.Run(() =>
			{
				if (!ValidateTokens(message)) return false;
				if (_onnxSession == null || _vocab == null) return false;

				long[] inputIds = TokenizeMessage(message);
				long[] attentionMask = inputIds.Select(_ => 1L).ToArray();

				return EvaluateToxicity(inputIds, attentionMask);
			});
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[LobbyManager] Local toxicity check failed: {ex.Message}");
		}

		return false;
	}

	private bool ValidateTokens(string message)
	{
		var encoding = GptEncoding.GetEncoding("cl100k_base");
		var gptTokens = encoding.Encode(message);
		return gptTokens != null && gptTokens.Count > 0;
	}

	private long[] TokenizeMessage(string message)
	{
		var tokens = new List<long> { 0 };

		string normalized = message.Replace(" ", " ");
		if (!normalized.StartsWith(" ")) normalized = " " + normalized;

		int i = 0;
		while (i < normalized.Length)
		{
			(int longestMatchLength, int longestMatchId) = FindLongestTokenMatch(normalized, i);

			if (longestMatchLength > 0)
			{
				tokens.Add(longestMatchId);
				i += longestMatchLength;
			}
			else
			{
				tokens.Add(3);
				i++;
			}
		}

		tokens.Add(2);
		return tokens.ToArray();
	}

	private (int length, int id) FindLongestTokenMatch(string normalized, int startIndex)
	{
		int longestMatchLength = 0;
		int longestMatchId = -1;
		int maxLen = Math.Min(normalized.Length - startIndex, 50);

		for (int len = 1; len <= maxLen; len++)
		{
			string sub = normalized.Substring(startIndex, len);
			if (_vocab!.TryGetValue(sub, out int id))
			{
				longestMatchLength = len;
				longestMatchId = id;
			}
		}

		return (longestMatchLength, longestMatchId);
	}

	private bool EvaluateToxicity(long[] inputIds, long[] attentionMask)
	{
		var inputIdsTensor = new DenseTensor<long>(inputIds, new int[] { 1, inputIds.Length });
		var attentionMaskTensor = new DenseTensor<long>(attentionMask, new int[] { 1, inputIds.Length });

		var inputs = new List<NamedOnnxValue>
		{
			NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
			NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor)
		};

		lock (_sessionLock)
		{
			using var results = _onnxSession!.Run(inputs);
			var logits = results.First(r => r.Name == "logits").AsTensor<float>();

			if (logits.Length > 0)
			{
				float val = logits[0, 0];
				double prob = 1.0 / (1.0 + Math.Exp(-val));
				return prob > 0.5;
			}
		}

		return false;
	}

	private async Task ProcessAndSendChatMessageAsync(string senderName, string message, bool alliesOnly, long timestampUtcTicks)
	{
		bool isToxic = await IsMessageToxicAsync(message);
		_chatHistory.Add((senderName, message, isToxic, timestampUtcTicks));

		if (!isToxic)
		{
			if (alliesOnly)
			{
				string senderTeam = GetPlayerTeamByName(senderName);
				foreach (var player in PlayerList)
				{
					if (player.Team == senderTeam)
					{
						if (player.PeerId == 1)
						{
							ChatReceived?.Invoke(senderName, message, true, timestampUtcTicks);
						}
						else
						{
							RpcId(player.PeerId, nameof(ReceiveChatMessage), senderName, message, true, timestampUtcTicks);
						}
					}
				}
			}
			else
			{
				Rpc(nameof(ReceiveChatMessage), senderName, message, false, timestampUtcTicks);
				ChatReceived?.Invoke(senderName, message, false, timestampUtcTicks);
			}
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReceiveChatMessage(string senderName, string message, bool alliesOnly, long timestampUtcTicks)
	{
		string cappedMessage = SanitizeLobbyText(message, MaximumChatMessageLength);
		if (cappedMessage.Length == 0) return;

		if (!IsHost)
		{
			ChatReceived?.Invoke(SanitizeLobbyText(senderName, 32), cappedMessage, alliesOnly, timestampUtcTicks);
			return;
		}

		int senderPeerId = Multiplayer.GetRemoteSenderId();
		if (senderPeerId <= 0) senderPeerId = 1;
		if (!_chatBudget.TryConsume(1))
		{
			GD.PushWarning($"[LobbyManager] Dropped a chat message from peer {senderPeerId}: above the chat rate limit.");
			return;
		}

		var senderPlayer = PlayerList.Find(p => p.PeerId == senderPeerId);
		string authoritativeName = senderPlayer != null ? senderPlayer.Name : SanitizeLobbyText(senderName, 32);
		int senderSlot = senderPlayer?.Slot ?? 0;
		ServerChatCommandReceived?.Invoke(senderSlot, cappedMessage);
		_ = ProcessAndSendChatMessageAsync(authoritativeName, cappedMessage, alliesOnly, timestampUtcTicks);
	}

	public void RequestChatHistory()
	{
		if (!IsHost && Multiplayer.MultiplayerPeer != null && Multiplayer.MultiplayerPeer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected)
		{
			RpcId(1, nameof(RequestChatHistoryFromHost));
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestChatHistoryFromHost()
	{
		if (IsHost)
		{
			int senderId = Multiplayer.GetRemoteSenderId();
			foreach (var chat in _chatHistory)
			{
				if (!chat.IsMuted)
				{
					RpcId(senderId, nameof(ReceiveChatMessage), chat.Sender, chat.Message, false, chat.TimestampUtcTicks);
				}
			}
		}
	}

	/// <summary>
	///     Tells a player that the match has started and where the process running it listens. The host runs this too,
	///     because it is the one that has to start that process; every other player joins it as an ordinary peer.
	/// </summary>
	/// <param name="mapName">The map the match plays.</param>
	/// <param name="matchSeed">The seed the match's scripts draw from.</param>
	/// <param name="authorityPort">The port the process running the match listens on.</param>
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void LoadMap(string mapName, int matchSeed, int authorityPort)
	{
		if (IsHost) return;

		GD.Print($"[LobbyManager] Joining the authority for {mapName} on port {authorityPort}");
		if (!TryJoinAuthority(authorityPort)) return;

		EnterMatchScene(mapName, matchSeed);
	}

	/// <summary>Joins the process running a match this client did not start, as the player the lobby seated it as.</summary>
	/// <param name="authorityPort">The port the process running the match listens on.</param>
	/// <returns>True once this client is connected to that process.</returns>
	private bool TryJoinAuthority(int authorityPort)
	{
		var channel = GetNodeOrNull<ClientNetworkChannel>("/root/RealmNetwork");
		if (channel == null)
		{
			LastHostError = "This build has no channel to an authority.";
			GD.PushError($"[LobbyManager] {LastHostError}");
			ConnectionFailed?.Invoke(LastHostError);
			return false;
		}

		GameContext.ServerSession?.Dispose();

		var session = new ClientServerSession(channel);
		// No folder is named here on purpose: the copy this peer holds is the one the lobby distributed, which is the
		// files the map's manifest lists, while the authority hashes the folder it loaded. Those two agree only when the
		// host is playing a distributed copy, and refusing a match over a difference the transfer itself creates is
		// worse than the check is worth. What a peer plays is decided by the assets both sides resolved, as it was before.
		if (!session.TryJoin(_connectedHostIp, authorityPort, LocalizationManager.GetCurrentLanguageCode(), null, out string failure))
		{
			LastHostError = failure;
			GD.PushError($"[LobbyManager] {failure}");
			ConnectionFailed?.Invoke(failure);
			session.Dispose();
			return false;
		}

		GameContext.AttachServerSession(session);
		return true;
	}

	/// <summary>Enters the screen the match is played on, having already been given its connection.</summary>
	/// <param name="mapName">The map the match plays.</param>
	/// <param name="matchSeed">The seed the match's scripts draw from.</param>
	private void EnterMatchScene(string mapName, int matchSeed)
	{
		GD.Print($"[LobbyManager] LoadMap RPC received for: {mapName} with deterministic random seed {matchSeed}");
		GameContext.ReplayPlayback.StopReplay();
		ActiveMapName = mapName;
		MatchSeed = matchSeed;
		GameContext.RandomService.SetSeed(matchSeed);

		IsGameStarted = true;
		GameSessionStartTime = DateTime.UtcNow;

		GetTree().ChangeSceneToFile("res://GameHost.tscn");
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void UpdateSpectatorDelay(bool enabled)
	{
		if (IsHost)
		{
			SpectatorDelay = enabled;
			Rpc(nameof(SyncSpectatorDelay), enabled);
			SpectatorDelayChanged?.Invoke(enabled);
		}
		else
		{
			RpcId(1, nameof(UpdateSpectatorDelay), enabled);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SyncSpectatorDelay(bool enabled)
	{
		SpectatorDelay = enabled;
		SpectatorDelayChanged?.Invoke(enabled);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SyncHostStability(string stability)
	{
		HostStability = stability;
		HostStabilityUpdated?.Invoke(stability);
	}


	public void StartGame(string mapName)
	{
		if (IsHost)
		{
			if (_countdownRemaining > 0)
			{
				return;
			}

			Rpc(nameof(BroadcastStartCountdown), mapName);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void BroadcastStartCountdown(string mapName)
	{
		if (IsHost)
		{
			SendChatMessage("System", string.Format(Tr("Game starting in 5 seconds on map: {0}."), mapName));
		}

		_countdownMapName = mapName;
		_countdownRemaining = 5;
		CountdownStarted?.Invoke(mapName, _countdownRemaining);
		TickCountdown();
	}

	private void TickCountdown()
	{
		if (_countdownRemaining <= 0)
		{
			CountdownFinished?.Invoke();
			if (IsHost && _countdownMapName != null)
			{
				MatchSeed = System.Random.Shared.Next(1, int.MaxValue);
				Rpc(nameof(LoadMap), _countdownMapName, MatchSeed, ENetPort);
				StartHostedMatch(_countdownMapName, MatchSeed);
			}

			return;
		}

		_countdownTimer = GetTree().CreateTimer(1.0f);
		_countdownTimer.Timeout += OnCountdownTimerTimeout;
	}

	private void OnCountdownTimerTimeout()
	{
		if (_countdownRemaining > 0)
		{
			_countdownRemaining--;
			CountdownTick?.Invoke(_countdownRemaining);
			TickCountdown();
		}
	}

	public void RequestCancelCountdown()
	{
		Rpc(nameof(BroadcastCancelCountdown));
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void BroadcastCancelCountdown()
	{
		if (IsHost)
		{
			int senderId = Multiplayer.GetRemoteSenderId();
			if (senderId == 0)
			{
				senderId = 1;
			}

			var player = PlayerList.Find(p => p.PeerId == senderId);
			string name = player != null ? player.Name : "Someone";
			SendChatMessage("System", string.Format(Tr("{0} cancelled the countdown."), name));
		}

		_countdownRemaining = 0;
		_countdownMapName = null;
		CountdownCancelled?.Invoke();
	}

	public bool AddAIBot()
	{
		if (!IsHost) return false;

		if (PlayerList.Count >= MaxPlayers || FindNextFreePlayableSlot() < 0)
		{
			LastLobbyWarning = string.Format(Tr("Cannot add an AI player: all {0} playable slots are taken."), MaxPlayers);
			SendChatMessage("System", LastLobbyWarning);
			return false;
		}

		int botId = -100;
		while (PlayerList.Exists(x => x.PeerId == botId))
		{
			botId--;
		}

		int nextSlot = FindNextFreePlayableSlot();

		var botPlayer = new PlayerInfo
		{
			PeerId = botId,
			Slot = nextSlot,
			Name = $"AI Bot {Math.Abs(botId) - 99}",
			Faction = "ORC",
			Team = "Team 2",
			Color = GetNextColor(),
			IsHost = false,
			Latency = "0 ms",
			Jitter = "0 ms",
			PacketLoss = "0%",
			BinaryVersion = RealmVersion.GameBinaryVersion
		};
		PlayerList.Add(botPlayer);
		BroadcastPlayerList();
		SendChatMessage("System", string.Format(Tr("{0} added to the lobby."), botPlayer.Name));

		return true;
	}

	public void BootPlayer(int peerId)
	{
		if (IsHost && peerId != 1)
		{
			if (peerId < 0)
			{
				int removedIdx = PlayerList.FindIndex(p => p.PeerId == peerId);
				if (removedIdx >= 0)
				{
					PlayerList.RemoveAt(removedIdx);
					for (int i = 0; i < PlayerList.Count; i++)
					{
						PlayerList[i].Slot = i + 1;
					}

					BroadcastPlayerList();
				}

				return;
			}

			RpcId(peerId, nameof(RejectConnection), "Kicked by Host");
			var timer = GetTree().CreateTimer(0.1f);
			timer.Timeout += () =>
			{
				if (Multiplayer.MultiplayerPeer != null)
				{
					try
					{
						Multiplayer.MultiplayerPeer.DisconnectPeer(peerId);
					}
					catch
					{
					}
				}
			};
		}
	}

	private void BroadcastPlayerList()
	{
		string serialized = JsonSerializer.Serialize(PlayerList);
		Rpc(nameof(SyncLobbyData), serialized);
		PlayerListUpdated?.Invoke();
	}

	private Color GetNextColor()
	{
		int index = (PlayerList.Count % (PlayerColorConfig.Palette.Length - 1)) + 1;
		return PlayerColorConfig.GetColor(index);
	}

	public async Task<int> MeasurePingToRegistryAsync()
	{
		var stopwatch = Stopwatch.StartNew();
		try
		{
			var response = await _httpClient.GetAsync($"{RegistryServerUrl}/lobbies");
			stopwatch.Stop();
			if (response.IsSuccessStatusCode)
			{
				return (int)stopwatch.ElapsedMilliseconds;
			}
		}
		catch
		{
		}

		return 100;
	}

	private HttpListener? _authHttpListener;

	public async Task<bool> StartOAuthFlowAsync(string provider)
	{
		int port = 8089;
		if (!TryStartAuthListener(port)) return false;

		string loginUrl = $"{RegistryServerUrl}/auth/login?provider={provider}&port={port}";
		OS.ShellOpen(loginUrl);

		try
		{
			var context = await _authHttpListener!.GetContextAsync();
			return HandleOAuthCallback(context);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[LobbyManager] Error handling OAuth callback: {ex.Message}");
			return false;
		}
		finally
		{
			_authHttpListener?.Stop();
		}
	}

	private bool TryStartAuthListener(int port)
	{
		_authHttpListener?.Stop();
		_authHttpListener = new HttpListener();
		_authHttpListener.Prefixes.Add($"http://localhost:{port}/auth/callback/");
		try
		{
			_authHttpListener.Start();
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[LobbyManager] Failed to start local OAuth listener on port {port}: {ex.Message}");
			return false;
		}
	}

	private bool HandleOAuthCallback(HttpListenerContext context)
	{
		var request = context.Request;
		var response = context.Response;

		string? username = request.QueryString["username"];
		string? token = request.QueryString["token"];
		string? returnedProvider = request.QueryString["provider"];

		if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(token))
		{
			SendAuthResponse(response, "<h1>Login Failed</h1><p>Invalid parameters received.</p>");
			return false;
		}

		AuthenticatedUsername = username;
		AuthToken = token;
		AuthProvider = returnedProvider;
		int myId = Multiplayer?.MultiplayerPeer != null ? Multiplayer.GetUniqueId() : 1;
		var me = PlayerList.Find(p => p.PeerId == myId || (IsHost && p.PeerId == 1));
		if (me != null)
		{
			me.Name = username;
		}

		GD.Print($"[LobbyManager] OAuth Login Success! Provider: {returnedProvider}, User: {username}");

		string successHtml = GetLoginSuccessHtml();
		SendAuthResponse(response, successHtml);

		_authHttpListener?.Stop();
		return true;
	}

	private string GetLoginSuccessHtml()
	{
		string successHtml = string.Empty;
		const string templatePath = "res://Templates/login_success.html";

		if (Godot.FileAccess.FileExists(templatePath))
		{
			using var file = Godot.FileAccess.Open(templatePath, Godot.FileAccess.ModeFlags.Read);
			if (file != null)
			{
				successHtml = file.GetAsText();
			}
		}
		else
		{
			string diskPath = Path.Combine(AppContext.BaseDirectory, "Templates", "login_success.html");
			if (File.Exists(diskPath))
			{
				successHtml = File.ReadAllText(diskPath);
			}
		}

		return successHtml;
	}

	private void SendAuthResponse(HttpListenerResponse response, string htmlContent)
	{
		byte[] buffer = Encoding.UTF8.GetBytes(htmlContent);
		response.ContentLength64 = buffer.Length;
		response.OutputStream.Write(buffer, 0, buffer.Length);
		response.OutputStream.Close();
	}

	public override void _ExitTree()
	{
		_authHttpListener?.Stop();
		StopHostDiagnosticsTimer();
	}

	private void StartHostDiagnosticsTimer()
	{
		StopHostDiagnosticsTimer();
		TickHostDiagnostics();
	}

	private void StopHostDiagnosticsTimer()
	{
		_diagnosticsTimer = null;
	}

	private void TickHostDiagnostics()
	{
		if (!IsHost)
		{
			return;
		}

		UpdateAllPeerDiagnostics();

		_diagnosticsTimer = GetTree().CreateTimer(2.0f);
		_diagnosticsTimer.Timeout += OnDiagnosticsTimerTimeout;
	}

	private void OnDiagnosticsTimerTimeout()
	{
		TickHostDiagnostics();
	}

	private void UpdateAllPeerDiagnostics()
	{
		if (Multiplayer.MultiplayerPeer is not ENetMultiplayerPeer enetMultiplayer)
		{
			BroadcastPlayerList();
			return;
		}

		bool changed = false;
		float totalRtt = 0f;
		float totalJitter = 0f;
		float totalLoss = 0f;
		int clientCount = 0;

		foreach (var p in PlayerList)
		{
			if (p.PeerId == 1 || p.PeerId < 0) continue;

			if (UpdateSinglePeerDiagnostics(p, enetMultiplayer, ref totalRtt, ref totalJitter, ref totalLoss))
			{
				changed = true;
			}

			clientCount++;
		}

		if (UpdateHostDiagnostics(clientCount, totalRtt, totalJitter, totalLoss))
		{
			changed = true;
		}

		if (changed)
		{
			BroadcastPlayerList();
		}
	}

	private bool UpdateSinglePeerDiagnostics(PlayerInfo p, ENetMultiplayerPeer enetMultiplayer, ref float totalRtt, ref float totalJitter, ref float totalLoss)
	{
		try
		{
			var peer = enetMultiplayer.GetPeer(p.PeerId);
			if (peer == null) return false;

			float rtt = (float)peer.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime);
			float jitter = (float)peer.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTimeVariance);
			float loss = (float)peer.GetStatistic(ENetPacketPeer.PeerStatistic.PacketLoss) / 65536.0f * 100.0f;

			totalRtt += rtt;
			totalJitter += jitter;
			totalLoss += loss;

			string newLatency = $"{Math.Round(rtt)} ms";
			string newJitter = $"{Math.Round(jitter)} ms";
			string newLoss = $"{Math.Round(loss)}%";

			if (p.Latency != newLatency || p.Jitter != newJitter || p.PacketLoss != newLoss)
			{
				p.Latency = newLatency;
				p.Jitter = newJitter;
				p.PacketLoss = newLoss;
				return true;
			}
		}
		catch
		{
		}

		return false;
	}

	private bool UpdateHostDiagnostics(int clientCount, float totalRtt, float totalJitter, float totalLoss)
	{
		var hostInfo = PlayerList.Find(x => x.PeerId == 1);
		if (hostInfo == null) return false;

		string hostLatency = "0 ms";
		string hostJitter = "0 ms";
		string hostLoss = "0%";

		if (clientCount > 0)
		{
			hostLatency = $"{Math.Round(totalRtt / clientCount)} ms";
			hostJitter = $"{Math.Round(totalJitter / clientCount)} ms";
			hostLoss = $"{Math.Round(totalLoss / clientCount)}%";
		}

		if (hostInfo.Latency != hostLatency || hostInfo.Jitter != hostJitter || hostInfo.PacketLoss != hostLoss)
		{
			hostInfo.Latency = hostLatency;
			hostInfo.Jitter = hostJitter;
			hostInfo.PacketLoss = hostLoss;
			return true;
		}

		return false;
	}

	private static string GetLocalIPAddress()
	{
		try
		{
			using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
			{
				socket.Connect("8.8.8.8", 65530);
				var endPoint = socket.LocalEndPoint as IPEndPoint;
				if (endPoint != null)
				{
					return endPoint.Address.ToString();
				}
			}
		}
		catch
		{
			try
			{
				var host = Dns.GetHostEntry(Dns.GetHostName());
				foreach (var ip in host.AddressList)
				{
					if (ip.AddressFamily == AddressFamily.InterNetwork)
					{
						return ip.ToString();
					}
				}
			}
			catch
			{
			}
		}

		return "127.0.0.1";
	}

	private static bool IsPrivateIp(string ip)
	{
		if (ip == "127.0.0.1") return true;
		if (ip == "localhost") return true;
		if (!IPAddress.TryParse(ip, out var address)) return false;

		byte[] bytes = address.GetAddressBytes();
		if (bytes.Length != 4) return false;

		if (bytes[0] == 10) return true;
		if (bytes[0] == 192 && bytes[1] == 168) return true;
		if (bytes[0] == 172 && IsInRange(bytes[1], 16, 31)) return true;
		if (bytes[0] == 100 && IsInRange(bytes[1], 64, 127)) return true;
		if (bytes[0] == 26) return true;

		return false;
	}

	private static bool IsInRange(byte value, byte min, byte max)
	{
		return value >= min && value <= max;
	}
}