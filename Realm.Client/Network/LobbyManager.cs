using System;
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
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.Utils;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Metadata;
using Realm.Shared.Network;
using Realm.Shared.Services;
using Environment = System.Environment;
using FileAccess = System.IO.FileAccess;
using HttpClient = System.Net.Http.HttpClient;

namespace Realm.Client.Network;

[GlobalClass]
public partial class LobbyManager : BaseMapTransferNode
{
	public bool IsSinglePlayer { get; set; } = false;
	public bool AllowsCheats => IsSinglePlayer;
	public string? LastHostError { get; private set; }

	public string LastAuthorityLogPath { get; private set; } = string.Empty;

	public string LastAuthorityConsoleLogPath { get; private set; } = string.Empty;

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
		PlayerInfo? me = PlayerList.Find(IsLocalPlayer);
		if (me != null)
		{
			me.Name = AuthenticatedUsername;
			SendSeatUpdate(me);
		}

		return true;
	}

	private readonly string _persistentSessionToken = Guid.NewGuid().ToString();

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

	public int LocalPeerId => _lobbySession?.LocalPeerId ?? -1;

	public int BoundPort => _lobbySession?.Port ?? ENetPort;

	public bool IsCountdownActive => _countdownActive;

	public int CountdownRemainingSeconds => _countdownRemaining;

	private readonly LobbyMatchPreparation _preparation = new();

	private bool _worldPrepared;

	private string? _worldPreparedMapName;

	private bool _matchPresentationOpen;

	private bool _loadReadyReported;

	private readonly HttpClient _httpClient = new();
	private ClientServerSession? _lobbySession;
	private string? _connectedHostIp;
	private bool _isConnectedToHost;
	private ClientWebSocket? _hostWebSocket;
	private CancellationTokenSource? _wsCts;
	private string? _hostPublicIp;
	private int _hostPublicPort;
	public string PublicIP => _hostPublicIp ?? "127.0.0.1";
	public int PublicPort => _hostPublicPort > 0 ? _hostPublicPort : BoundPort;
	private string? _hostToken;
	private long _lobbyRevision;
	private bool _countdownActive;
	private int _countdownRemaining;
	private string? _countdownMapName;
	private TaskCompletionSource<string?>? _admissionTcs;
	private TaskCompletionSource<MapTransferReply>? _transferReplyTcs;
	private string? _downloadingMapName;
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

	public override void _Process(double delta)
	{
		ClientServerSession? session = _lobbySession;
		if (session == null) return;

		session.Pump();
		TickMatchPreparation();
	}

	private void TickMatchPreparation()
	{
		if (_preparation.HasWork)
		{
			if (!_preparation.Tick()) return;

			GD.Print($"MATCH_LOAD preparation models={_preparation.TotalModels} warm_ms={_preparation.WarmMilliseconds.ToString("F1", CultureInfo.InvariantCulture)}");
			BeginMatchWorldBuild();
			return;
		}

		AdvanceMatchWorldBuild();
	}

	private void BeginMatchWorldBuild()
	{
		if (_worldPrepared && string.Equals(_worldPreparedMapName, ActiveMapName, StringComparison.Ordinal)) return;
		_loadReadyReported = false;
		if (string.IsNullOrWhiteSpace(ActiveMapName)) return;

		GameHost? host = GameContext.Host;
		if (host == null) return;

		if (host.IsBuildingMatchWorld) return;

		if (_worldPrepared) host.DiscardPreparedMatchWorld();
		_worldPrepared = false;
		_worldPreparedMapName = null;

		host.BeginMatchWorldPreparation(ActiveMapName);
		MatchLoadTrace.Mark("world_build_begun");
	}

	private void AdvanceMatchWorldBuild()
	{
		GameHost? host = GameContext.Host;
		if (host == null) return;
		if (!host.IsBuildingMatchWorld)
		{
			ReportWorldPreparedIfReady();
			return;
		}

		if (!host.AdvanceMatchWorldPreparation()) return;

		_worldPrepared = true;
		_worldPreparedMapName = ActiveMapName;
		MatchLoadTrace.Mark("lobby_ready");
		MatchLoadTrace.WriteSummary("lobby_ready");
		ReportWorldPreparedIfReady();
	}

	private void ReportWorldPreparedIfReady()
	{
		if (!_worldPrepared || _loadReadyReported) return;

		PlayerInfo? me = PlayerList.Find(IsLocalPlayer);
		if (me == null || !me.IsMapReady) return;

		_loadReadyReported = true;
		ReportLocalLoadReadyState(true);
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
			GameContext.Upnp.ClosePort(BoundPort);
			_lobbySession?.Dispose();
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
		LocalNatType = await NatTypeTester.DetermineNatTypeAsync(0);
		GD.Print($"[LobbyManager] NAT Type Classified: {LocalNatType}");

		await ResolvePublicEndpointAsync(ENetPort);
		CallDeferred(nameof(EmitNatTestCompleted));
	}

	private async Task ResolvePublicEndpointAsync(int localPort)
	{
		_hostPublicIp = null;
		_hostPublicPort = 0;

		try
		{
			var dnsAddresses = await Dns.GetHostAddressesAsync("stun.l.google.com");
			if (dnsAddresses.Length == 0) return;

			StunResult? result = null;
			try
			{
				result = await NatTypeTester.QueryStunOnPortAsync(dnsAddresses[0], StunPort, localPort);
			}
			catch (Exception ex)
			{
				GD.Print($"[LobbyManager] Direct STUN on port {localPort} failed: {ex.Message}");
			}

			if (result == null || !result.Success || result.MappedEndPoint == null)
			{
				using var udp = new UdpClient();
				udp.ExclusiveAddressUse = false;
				udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
				udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

				var serverEp = new IPEndPoint(dnsAddresses[0], StunPort);
				byte[] req = new byte[20];
				req[0] = 0x00;
				req[1] = 0x01;
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

			if (result == null || !result.Success || result.MappedEndPoint == null) return;

			bool mappedFromBoundPort = result.LocalEndPoint != null && result.LocalEndPoint.Port == localPort;
			_hostPublicIp = result.MappedEndPoint.Address.ToString();
			_hostPublicPort = mappedFromBoundPort ? result.MappedEndPoint.Port : localPort;
			GD.Print($"[STUN] Local {localPort} -> Public {_hostPublicIp}:{_hostPublicPort}");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[LobbyManager] Failed to get public IP: {ex.Message}");
		}
	}

	public void RunNatTypeTest()
	{
		Task.Run(RunNatTypeTestAsync);
	}

	private void EmitNatTestCompleted()
	{
		NatTestCompleted?.Invoke();
	}

	public async Task<bool> HostSinglePlayerGame(string mapPathName, string mapDisplayName, string? mapVersion = null, bool debugMapScript = false)
	{
		IsSinglePlayer = true;
		IsHost = true;
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

		if (!await TryStartAuthority(mapDirectory, mapPathName, wasmDebug: debugMapScript)) return false;

		IsGameStarted = true;
		CallDeferred(nameof(EnterMatchScene), mapPathName, MatchSeed);
		return true;
	}

	private async Task<bool> TryStartAuthority(string? mapDirectory, string mapPathName, int authorityPort = 0, bool openToOthers = false, bool wasmDebug = false)
	{
		LastAuthorityLogPath = string.Empty;
		LastAuthorityConsoleLogPath = string.Empty;

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

		MatchStartResult started = await session.TryStartAsync(start, authorityPort);
		LastAuthorityLogPath = started.LogPath;
		LastAuthorityConsoleLogPath = started.ConsoleLogPath;
		if (!started.Started)
		{
			LastHostError = started.Failure;
			GD.PushError($"[LobbyManager] {started.Failure}");
			IsGameStarted = false;
			session.Dispose();
			return false;
		}

		GameContext.AttachServerSession(session);
		return true;
	}

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

		string? mapDirectory = GameHost.ResolveMapDirectory(mapPathName, ActiveMapVersion);
		if (string.IsNullOrEmpty(mapDirectory))
		{
			LastHostError = $"The map folder for '{mapPathName}' could not be found.";
			return false;
		}

		var channel = GetNodeOrNull<ClientNetworkChannel>("/root/RealmNetwork");
		if (channel == null)
		{
			LastHostError = "This build has no channel to an authority.";
			return false;
		}

		GameContext.ServerSession?.Dispose();
		GameContext.DetachServerSession();

		var session = new ClientServerSession(channel);
		session.ConfigureLobbyIdentity(AuthenticatedUsername, _persistentSessionToken);
		AttachLobbySession(session);

		var start = new ServerLobbyStart(
			ProjectSettings.GlobalizePath(mapDirectory),
			mapPathName,
			MaxPlayers,
			Path.Combine(OS.GetUserDataDir(), "logs"),
			OS.GetUserDataDir(),
			LocalizationManager.GetCurrentLanguageCode());

		MatchStartResult lobbyStarted = await session.TryStartLobbyAsync(start, ENetPort);
		LastAuthorityLogPath = lobbyStarted.LogPath;
		LastAuthorityConsoleLogPath = lobbyStarted.ConsoleLogPath;
		if (!lobbyStarted.Started)
		{
			LastHostError = lobbyStarted.Failure;
			GD.PushError($"[LobbyManager] {lobbyStarted.Failure}");
			TeardownHostedLobby(session);
			return false;
		}

		string? admissionFailure = await WaitForLobbyAdmissionAsync(session);
		if (admissionFailure != null)
		{
			LastHostError = admissionFailure;
			GD.PushError($"[LobbyManager] {admissionFailure}");
			TeardownHostedLobby(session);
			return false;
		}

		if (!session.IsAdmin)
		{
			LastHostError = "The lobby's authority did not grant this client the capability that administers it.";
			GD.PushError($"[LobbyManager] {LastHostError}");
			TeardownHostedLobby(session);
			return false;
		}

		IsHost = true;

		await ResolvePublicEndpointAsync(session.Port);

		bool upnpSuccess = await GameContext.Upnp.OpenPortAsync(session.Port);
		bool isRestrictedNat = LocalNatType is NatType.RestrictedCone or NatType.PortRestrictedCone or NatType.Symmetric;
		IsRestrictedHost = !upnpSuccess && isRestrictedNat;
		if (IsRestrictedHost)
		{
			GD.Print("[LobbyManager] Warning: UPnP port mapping not available on restricted NAT. External players may require Direct Connect.");
		}

		Diagnostics.StartHostListener(session.Port + 1);

		bool registered = await RegisterLobbyWithRegistryAsync(mapPathName, mapDisplayName, explicitVersion);
		if (!registered)
		{
			TeardownHostedLobby(session);
			return false;
		}

		PlayerListUpdated?.Invoke();
		return true;
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
		_downloadingMapName = null;
		PlayerList.Clear();
		ActiveMapName = mapPathName;
		ActiveMapVersion = !string.IsNullOrWhiteSpace(explicitVersion) ? explicitVersion : "1.0.0";
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
			return await EstablishLobbyConnectionAsync(connectIp, connectPort, IsPrivateIp(connectIp));
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
		return await EstablishLobbyConnectionAsync(targetIp, targetPort, IsPrivateIp(targetIp));
	}

	private void InitializeClientState(string lobbyId)
	{
		IsHost = false;
		IsGameStarted = false;
		ActiveLobbyId = lobbyId;
		LastLobbyWarning = null;
		LastHostError = null;
		_downloadingMapName = null;
		PlayerList.Clear();
	}

	private async Task<bool> EstablishLobbyConnectionAsync(string connectIp, int connectPort, bool isLocalConnection)
	{
		var channel = GetNodeOrNull<ClientNetworkChannel>("/root/RealmNetwork");
		if (channel == null)
		{
			LastHostError = "This build has no channel to an authority.";
			ConnectionFailed?.Invoke(LastHostError);
			return false;
		}

		if (!isLocalConnection)
		{
			await UdpHolePuncher.PunchHoleAsync(connectIp, connectPort, ENetPort);
		}

		GameContext.ServerSession?.Dispose();
		GameContext.DetachServerSession();

		var session = new ClientServerSession(channel);
		session.ConfigureLobbyIdentity(AuthenticatedUsername, _persistentSessionToken);
		AttachLobbySession(session);

		if (!session.TryJoinLobby(connectIp, connectPort, LocalizationManager.GetCurrentLanguageCode(), isLocalConnection ? 0 : ENetPort, out string failure))
		{
			LastHostError = failure;
			GD.PrintErr($"[LobbyManager] {failure}");
			Disconnect();
			ConnectionFailed?.Invoke(failure);
			return false;
		}

		_isConnectedToHost = true;

		string? admissionFailure = await WaitForLobbyAdmissionAsync(session);
		if (admissionFailure != null)
		{
			LobbyJoinError = admissionFailure;
			GD.PrintErr($"[LobbyManager] {admissionFailure}");
			Disconnect();
			ConnectionFailed?.Invoke("Direct connection handshake failed.");
			return false;
		}

		IsHost = session.IsAdmin;
		return true;
	}

	private async Task<string?> WaitForLobbyAdmissionAsync(ClientServerSession session)
	{
		var decided = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
		_admissionTcs = decided;

		void OnHello(ServerHello hello) => decided.TrySetResult(hello.Accepted ? null : hello.Rejection);
		void OnLost(string reason) => decided.TrySetResult(reason);

		session.HelloAnswered += OnHello;
		session.ServerLost += OnLost;

		try
		{
			var completed = await Task.WhenAny(decided.Task, Task.Delay(TimeSpan.FromSeconds(15)));
			return completed == decided.Task ? decided.Task.Result : "The lobby's authority did not answer in time.";
		}
		finally
		{
			session.HelloAnswered -= OnHello;
			session.ServerLost -= OnLost;
			_admissionTcs = null;
		}
	}

	private void AttachLobbySession(ClientServerSession session)
	{
		_lobbySession = session;
		session.HelloAnswered += OnLobbyHelloAnswered;
		session.LobbyStateReceived += OnLobbyState;
		session.LobbyChatReceived += OnLobbyChat;
		session.LobbyMatchStartedEvent += OnLobbyMatchStarted;
		session.LobbyKickedEvent += OnLobbyKicked;
		session.MapTransferReplied += OnMapTransferReplied;
		session.ServerLost += OnLobbyAuthorityLost;
	}

	private void DetachLobbySession(ClientServerSession session)
	{
		session.HelloAnswered -= OnLobbyHelloAnswered;
		session.LobbyStateReceived -= OnLobbyState;
		session.LobbyChatReceived -= OnLobbyChat;
		session.LobbyMatchStartedEvent -= OnLobbyMatchStarted;
		session.LobbyKickedEvent -= OnLobbyKicked;
		session.MapTransferReplied -= OnMapTransferReplied;
		session.ServerLost -= OnLobbyAuthorityLost;
	}

	private void OnLobbyHelloAnswered(ServerHello hello)
	{
		if (!hello.Accepted) return;

		IsHost = hello.IsAdmin;
		if (IsHost)
		{
			ReportLocalMapReadyState(true);
			_lobbySession?.SendLobbyCommand(new LobbyCommand
			{
				Kind = LobbyCommandKind.ReportHostStability,
				Text = HostStability
			});
		}

		PlayerListUpdated?.Invoke();
	}

	private void OnLobbyState(LobbyStateMessage state)
	{
		if (state.Revision < _lobbyRevision) return;
		_lobbyRevision = state.Revision;

		MaxPlayers = state.MaxPlayers > 0 ? state.MaxPlayers : MaxPlayers;
		SpectatorDelay = state.SpectatorDelay;
		if (!string.IsNullOrEmpty(state.HostStability) && !string.Equals(state.HostStability, HostStability, StringComparison.Ordinal))
		{
			HostStability = state.HostStability;
			HostStabilityUpdated?.Invoke(HostStability);
		}

		if (!string.IsNullOrEmpty(state.MapName) && !string.Equals(state.MapName, ActiveMapName, StringComparison.Ordinal))
		{
			ActiveMapName = state.MapName;
			ActiveMapVersion = string.IsNullOrEmpty(state.MapVersion) ? "1.0.0" : state.MapVersion;
			ActiveMapChanged?.Invoke(ActiveMapName);
			if (!IsHost) _ = EnsureMapDownloadedAsync(ActiveMapName, ActiveMapVersion);
		}

		ApplySeats(state.Seats);
		ApplyCountdown(state);
		MakeSureThisSeatReportsItsMap();
		StartMatchPreparationForCurrentMap();
		PlayerListUpdated?.Invoke();
	}

	private void StartMatchPreparationForCurrentMap()
	{
		if (string.IsNullOrWhiteSpace(ActiveMapName)) return;

		bool holdsMap = IsHost
			? !string.IsNullOrEmpty(GameHost.ResolveMapDirectory(ActiveMapName, ActiveMapVersion))
			: MapAssetManager.IsMapDownloaded(ActiveMapName, ActiveMapVersion);
		if (!holdsMap) return;

		bool preparedForThisMap = _preparation.IsPrepared && string.Equals(_preparation.MapName, ActiveMapName, StringComparison.Ordinal);
		if (preparedForThisMap)
		{
			if (!_worldPrepared || !string.Equals(_worldPreparedMapName, ActiveMapName, StringComparison.Ordinal)) BeginMatchWorldBuild();
			ReportWorldPreparedIfReady();
			return;
		}

		if (_preparation.IsPreparing(ActiveMapName)) return;

		if (_worldPrepared) GameContext.Host?.DiscardPreparedMatchWorld();
		_worldPrepared = false;
		_worldPreparedMapName = null;

		ReportLocalLoadReadyState(false);
		MatchLoadTrace.Begin("lobby_preparation");
		_preparation.Prepare(ActiveMapName, ActiveMapVersion);
		MatchLoadTrace.Mark("preparation_started");
		MatchLoadTrace.WriteSummary("preparation_started");

		if (_preparation.HasWork) return;
		BeginMatchWorldBuild();
	}

	private void MakeSureThisSeatReportsItsMap()
	{
		if (IsHost) return;

		PlayerInfo? me = PlayerList.Find(IsLocalPlayer);
		if (me == null || me.IsMapReady) return;
		if (string.IsNullOrWhiteSpace(ActiveMapName)) return;

		if (MapAssetManager.IsMapDownloaded(ActiveMapName, ActiveMapVersion))
		{
			ReportLocalMapReadyState(true);
			return;
		}

		if (_downloadingMapName != ActiveMapName) _ = EnsureMapDownloadedAsync(ActiveMapName, ActiveMapVersion);
	}

	private void ApplySeats(LobbySeat[] seats)
	{
		PlayerList.Clear();
		if (seats == null) return;

		for (int index = 0; index < seats.Length; index++)
		{
			LobbySeat seat = seats[index];
			PlayerList.Add(new PlayerInfo
			{
				PeerId = seat.PeerId,
				Slot = seat.Slot,
				Name = seat.Name ?? string.Empty,
				Faction = seat.Faction ?? "HUMAN",
				Team = seat.TeamName ?? string.Empty,
				Color = UnpackColor(seat.ColorRgba),
				IsHost = seat.IsHost,
				IsReady = seat.IsReady,
				IsMapReady = seat.IsMapReady,
				IsLoadReady = seat.IsLoadReady,
				IsDisconnected = seat.IsDisconnected,
				Latency = "0 ms",
				Jitter = "0 ms",
				PacketLoss = "0%",
				BinaryVersion = RealmVersion.GameBinaryVersion,
				SessionToken = seat.SessionToken ?? string.Empty
			});
		}
	}

	private void ApplyCountdown(LobbyStateMessage state)
	{
		if (state.CountdownActive)
		{
			bool started = !_countdownActive;
			_countdownActive = true;
			_countdownMapName = state.MapName;

			if (started)
			{
				_countdownRemaining = state.CountdownRemainingSeconds;
				CountdownStarted?.Invoke(state.MapName, state.CountdownRemainingSeconds);

				BeginMatchPresentation(state.MapName);
			}
			else if (state.CountdownRemainingSeconds != _countdownRemaining)
			{
				_countdownRemaining = state.CountdownRemainingSeconds;
				CountdownTick?.Invoke(_countdownRemaining);
			}

			return;
		}

		if (!_countdownActive) return;

		_countdownActive = false;
		_countdownMapName = null;
		_countdownRemaining = 0;
		CountdownCancelled?.Invoke();
		EndMatchPresentation();
	}

	private void BeginMatchPresentation(string mapName)
	{
		if (_matchPresentationOpen || _matchStarted) return;

		ActiveMapName = mapName;
		GameContext.ReplayPlayback.StopReplay();

		GameHost? host = GameContext.Host;
		while (host != null && host.IsBuildingMatchWorld) host.AdvanceMatchWorldPreparation();
		if (host != null && host.HasPreparedMatchWorld)
		{
			_worldPrepared = true;
			_worldPreparedMapName = mapName;
		}

		if (!_worldPrepared) return;

		_matchPresentationOpen = true;
		MatchLoadTrace.Begin("start_presentation");
		MatchLoadTrace.Mark("countdown_received");
		GD.Print($"[LobbyManager] Showing the match world for '{mapName}' under the countdown");

		host?.ShowPreparedMatchWorld();
		GameContext.UserInterface?.TransitionTo(GameScreen.InGameHUD);
	}

	private void EndMatchPresentation()
	{
		if (!_matchPresentationOpen) return;

		_matchPresentationOpen = false;
		GD.Print("[LobbyManager] Countdown cancelled; returning to the lobby");
		GameContext.UserInterface?.TransitionTo(GameScreen.LobbyRoom);
	}

	public void ReportLocalLoadReadyState(bool isLoadReady)
	{
		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.ReportLoadReady,
			Flag = isLoadReady
		});
	}

	private void OnLobbyChat(LobbyChatMessage line)
	{
		string sender = string.IsNullOrEmpty(line.SenderName) ? "System" : line.SenderName;
		int senderSlot = PlayerList.Find(p => p.PeerId == line.SenderPeerId)?.Slot ?? 0;
		if (line.SenderPeerId > 0) ServerChatCommandReceived?.Invoke(senderSlot, line.Message);
		ChatReceived?.Invoke(sender, line.Message, false, line.TimestampUtcTicks);
	}

	private void OnLobbyKicked(LobbyKickedMessage kicked)
	{
		GD.Print($"[LobbyManager] Removed from the lobby: {kicked.Reason}");
		KickReceived?.Invoke(string.IsNullOrEmpty(kicked.Reason) ? "Kicked by Host" : kicked.Reason);

		bool wasBuilding = _matchPresentationOpen;
		var session = _lobbySession;
		if (session != null)
		{
			DetachLobbySession(session);
			session.Dispose();
			_lobbySession = null;
		}

		ResetLobbyState();
		if (wasBuilding) GameContext.UserInterface?.TransitionTo(GameScreen.MainMenu);
	}

	private void OnLobbyAuthorityLost(string reason)
	{
		if (_matchStarted) return;

		bool wasBuilding = _matchPresentationOpen;
		bool administeredHere = IsHost;
		if (administeredHere)
		{
			LastHostError = reason;
			GD.PrintErr($"[LobbyManager] The lobby's authority is gone: {reason}");
		}

		var session = _lobbySession;
		int hostedPort = session?.Port ?? ENetPort;
		if (session != null)
		{
			DetachLobbySession(session);
			session.Dispose();
			_lobbySession = null;
		}

		ReleaseHostedEndpoint(hostedPort);
		ResetLobbyState();
		ConnectionFailed?.Invoke(administeredHere ? reason : "The host's lobby is no longer running.");
		if (wasBuilding) GameContext.UserInterface?.TransitionTo(GameScreen.MainMenu);
	}

	private void OnLobbyMatchStarted(LobbyMatchStartedMessage started)
	{
		if (_matchStarted) return;
		_matchStarted = true;
		IsGameStarted = true;
		_countdownActive = false;
		CountdownFinished?.Invoke();

		if (IsHost) UnregisterActiveLobbyFromRegistry();

		ClientServerSession? session = _lobbySession;
		if (session == null) return;

		if (_matchPresentationOpen)
		{
			_matchPresentationOpen = false;
			DetachLobbySession(session);
			_lobbySession = null;
			MatchSeed = started.Seed;
			GameContext.RandomService.SetSeed(started.Seed);
			GameSessionStartTime = DateTime.UtcNow;
			GameContext.AttachServerSession(session);
			GameContext.Host?.AttachMatchSession();
			GameContext.Host?.StartMatchRecording();
			MatchLoadTrace.Mark("match_started");
			return;
		}

		DetachLobbySession(session);
		_lobbySession = null;
		GameContext.AttachServerSession(session);
		EnterMatchScene(started.MapName, started.Seed);
	}

	private void EnterMatchScene(string mapName, int matchSeed)
	{
		GD.Print($"[LobbyManager] Match started for '{mapName}' with deterministic random seed {matchSeed}");
		GameContext.ReplayPlayback.StopReplay();
		ActiveMapName = mapName;
		MatchSeed = matchSeed;
		GameContext.RandomService.SetSeed(matchSeed);

		IsGameStarted = true;
		GameSessionStartTime = DateTime.UtcNow;

		GetTree().ChangeSceneToFile("res://GameHost.tscn");
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

		_wsCts?.Cancel();
		_hostWebSocket?.Dispose();
		_hostWebSocket = null;
		ActiveLobbyId = null;
	}

	private void ReleaseHostedEndpoint(int port)
	{
		if (!IsHost) return;

		Diagnostics.StopHostListener();
		GameContext.Upnp.ClosePort(port > 0 ? port : ENetPort);
	}

	public void Disconnect()
	{
		GD.Print("[LobbyManager] Disconnecting...");

		ClientServerSession? session = _lobbySession;
		int hostedPort = session?.Port ?? ENetPort;
		bool hostedHere = IsHost;
		_lobbySession = null;

		UnregisterActiveLobbyFromRegistry();

		if (session != null)
		{
			DetachLobbySession(session);
			if (ReferenceEquals(GameContext.ServerSession, session)) GameContext.DetachServerSession();

			if (!hostedHere) session.SendLobbyCommand(new LobbyCommand { Kind = LobbyCommandKind.Leave });
			if (hostedHere) session.Dispose();
			else Callable.From(session.Dispose).CallDeferred();
		}

		ReleaseHostedEndpoint(hostedPort);
		ResetLobbyState();
	}

	private void ResetLobbyState()
	{
		_preparation.Cancel();

		bool hadPreparedWorld = _worldPrepared || GameContext.Host?.IsBuildingMatchWorld == true;
		_worldPrepared = false;
		_worldPreparedMapName = null;
		_loadReadyReported = false;
		_matchPresentationOpen = false;
		_isConnectedToHost = false;
		IsHost = false;
		IsGameStarted = false;
		_countdownActive = false;
		_countdownRemaining = 0;
		_countdownMapName = null;
		_matchStarted = false;
		_lobbyRevision = 0;
		_downloadingMapName = null;
		PlayerList.Clear();
		PlayerListUpdated?.Invoke();

		GameContext.Host?.DiscardPreparedMatchWorld();
		if (hadPreparedWorld) GameContext.Host?.EndMatchAndClearWorld();
	}

	private bool _matchStarted;

	private void TeardownHostedLobby(ClientServerSession? launchedSession = null)
	{
		ClientServerSession? session = _lobbySession ?? launchedSession;
		int hostedPort = session?.Port ?? ENetPort;
		_lobbySession = null;
		if (session != null)
		{
			DetachLobbySession(session);
			session.Dispose();
		}

		UnregisterActiveLobbyFromRegistry();
		ReleaseHostedEndpoint(hostedPort);
		ResetLobbyState();
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
				Callable.From(() => SendPunchRequest(clientIp, clientPort)).CallDeferred();
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[LobbyManager] Error handling WebSocket message: {ex.Message}");
		}
	}

	private void SendPunchRequest(string clientIp, int clientPort)
	{
		if (string.IsNullOrEmpty(clientIp) || clientPort <= 0) return;

		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.PunchAddress,
			Address = clientIp,
			Port = clientPort
		});
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
				}

				await Task.Delay(1000, token);
			}
		}, token);
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
				HostPort = _hostPublicPort > 0 ? _hostPublicPort : BoundPort,
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
			LastHostError = ex.Message;
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
		return false;
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

	public bool IsLocalPlayer(PlayerInfo player) => player.PeerId == LocalPeerId && player.PeerId > 0;

	public void UpdateReadyState(int peerId, bool isReady)
	{
		PlayerInfo? player = PlayerList.Find(p => p.PeerId == peerId);
		if (player != null) player.IsReady = isReady;

		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.SetReady,
			TargetPeerId = peerId,
			Flag = isReady
		});
	}

	private void SendSeatUpdate(PlayerInfo player)
	{
		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.UpdateSeat,
			TargetPeerId = player.PeerId,
			Name = player.Name,
			Faction = player.Faction,
			TeamName = player.Team,
			ColorRgba = PackColor(player.Color)
		});
	}

	public void UpdatePlayerSlot(int peerId, string faction, string team, Color color, string name)
	{
		PlayerInfo? player = PlayerList.Find(p => p.PeerId == peerId);
		if (player != null)
		{
			player.Faction = faction;
			player.Team = team;
			player.Color = color;
			player.Name = name;
		}

		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.UpdateSeat,
			TargetPeerId = peerId,
			Name = name,
			Faction = faction,
			TeamName = team,
			ColorRgba = PackColor(color)
		});
	}

	public void ReportLocalMapReadyState(bool isMapReady)
	{
		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.ReportMapReady,
			Flag = isMapReady
		});
	}

	public void UpdateSpectatorDelay(bool enabled)
	{
		if (!IsHost) return;

		SpectatorDelay = enabled;
		SpectatorDelayChanged?.Invoke(enabled);
		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.SetSpectatorDelay,
			Flag = enabled
		});
	}

	public void StartGame(string mapName, bool ignoreReadiness = false)
	{
		if (!IsHost) return;

		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.StartCountdown,
			Text = mapName,
			Flag = ignoreReadiness
		});
	}

	public void RequestCancelCountdown()
	{
		_lobbySession?.SendLobbyCommand(new LobbyCommand { Kind = LobbyCommandKind.CancelCountdown });
	}

	public bool AddAIBot()
	{
		if (!IsHost) return false;
		if (PlayerList.Count >= MaxPlayers)
		{
			LastLobbyWarning = string.Format(Tr("Cannot add an AI player: all {0} playable slots are taken."), MaxPlayers);
			return false;
		}

		_lobbySession?.SendLobbyCommand(new LobbyCommand { Kind = LobbyCommandKind.AddComputer });
		return true;
	}

	public void BootPlayer(int peerId)
	{
		if (!IsHost || peerId == LocalPeerId) return;

		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.Kick,
			TargetPeerId = peerId
		});
	}

	public void RequestChatHistory()
	{
		_lobbySession?.SendLobbyCommand(new LobbyCommand { Kind = LobbyCommandKind.RequestChatHistory });
	}

	public void ReceiveChatFromAuthority(string senderName, string message, bool alliesOnly, long timestampUtcTicks)
	{
		ChatReceived?.Invoke(LobbyLimits.Sanitize(senderName, 32), message, alliesOnly, timestampUtcTicks);
	}

	public void SendChatMessage(string senderName, string message, bool alliesOnly = false, long timestampUtcTicks = 0)
	{
		long sentAtUtcTicks = timestampUtcTicks > 0 ? timestampUtcTicks : DateTime.UtcNow.Ticks;

		ClientServerSession? matchSession = GameContext.ServerSession;
		if (matchSession != null && matchSession.IsRunning && IsGameStarted && senderName != "System")
		{
			if (matchSession.SendChat(message, alliesOnly)) return;
		}

		if (senderName == "System") return;

		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.Chat,
			Text = message
		});
	}

	public bool HasRegisteredSessionToken(int peerId)
	{
		PlayerInfo? player = PlayerList.Find(p => p.PeerId == peerId);
		return player != null && !string.IsNullOrEmpty(player.SessionToken);
	}

	public bool TryGetReconnectOwnerForHarness(string sessionToken, out int slot)
	{
		slot = -1;
		if (string.IsNullOrEmpty(sessionToken)) return false;

		PlayerInfo? owner = PlayerList.Find(p => !string.IsNullOrEmpty(p.SessionToken) && string.Equals(p.SessionToken, sessionToken, StringComparison.Ordinal));
		if (owner == null) return false;

		slot = owner.Slot;
		return true;
	}

	public async Task<bool> EnsureMapDownloadedAsync(string? mapName = null, string? version = null)
	{
		string targetMap = !string.IsNullOrWhiteSpace(mapName) ? mapName : ActiveMapName;
		string targetVersion = !string.IsNullOrWhiteSpace(version) ? version : ActiveMapVersion;
		if (string.IsNullOrWhiteSpace(targetMap) || IsHost)
		{
			ReportLocalMapReadyState(true);
			return true;
		}

		if (MapAssetManager.IsMapDownloaded(targetMap, targetVersion))
		{
		_downloadingMapName = null;
		CallDeferred(nameof(EmitDownloadCompleted));
		StartMatchPreparationForCurrentMap();
		return true;
	}

		if (_downloadingMapName == targetMap) return false;

		if (_lobbySession == null && _isConnectedToHost) return await TryDownloadMapFromHostAsync(targetMap);
		if (_lobbySession == null) return false;

		_downloadingMapName = targetMap;

		await _mapDownloadLock.WaitAsync();
		try
		{
			ReportLocalMapReadyState(false);
			CallDeferred(nameof(EmitDownloadProgress), 0.0f);

			MapTransferReply? manifestReply = await RequestMapTransferAsync(new MapTransferRequest
			{
				Kind = MapTransferRequestKind.Manifest,
				TransferId = Guid.NewGuid().ToString("N"),
				MapName = targetMap,
				MissingHashes = Array.Empty<string>(),
				ChunkIndex = 0
			});

			if (manifestReply is not { Kind: MapTransferReplyKind.Manifest } servedManifest || string.IsNullOrEmpty(servedManifest.ManifestJson))
			{
				GD.PrintErr($"[LobbyManager] The lobby did not serve a manifest for '{targetMap}': {manifestReply?.Refusal}");
				CallDeferred(nameof(EmitDownloadFailed));
				return false;
			}

			MapManifest? manifest = MapManifest.LoadFromJson(servedManifest.ManifestJson);
			if (manifest == null)
			{
				CallDeferred(nameof(EmitDownloadFailed));
				return false;
			}

			string transferId = Guid.NewGuid().ToString("N");
			List<string> allHashes = manifest.Files.Values.ToList();
			int totalAssets = allHashes.Count;
			List<string> missing = await Task.Run(() => MapAssetManager.GetMissingHashes(allHashes));

			int chunkIndex = 0;
			int totalChunks = 1;
			while (chunkIndex < totalChunks)
			{
				MapTransferReply? chunkReply = await RequestMapTransferAsync(new MapTransferRequest
				{
					Kind = MapTransferRequestKind.Chunk,
					TransferId = transferId,
					MapName = targetMap,
					MissingHashes = missing.ToArray(),
					ChunkIndex = chunkIndex
				}, 60000);

				if (chunkReply is not { Kind: MapTransferReplyKind.Chunk } servedChunk || servedChunk.Bundle == null)
				{
					GD.PrintErr($"[LobbyManager] The lobby stopped serving '{targetMap}' at bundle {chunkIndex}: {chunkReply?.Refusal}");
					CallDeferred(nameof(EmitDownloadFailed));
					return false;
				}

				totalChunks = Math.Max(1, servedChunk.TotalChunks);
				List<(string AssetKey, byte[] Data, string? Metadata)> assets = await Task.Run(() => ZstdAssetBundleHelper.ExtractBundleBytes(servedChunk.Bundle));
				await Task.Run(() =>
				{
					foreach (var (assetKey, data, metadata) in assets)
					{
						MapAssetManager.Storage.StoreAsset(data, Path.GetExtension(assetKey), metadata, precomputedBlake3: assetKey);
					}
				});

				chunkIndex++;
				float progress = totalChunks > 0 ? Math.Clamp((float)chunkIndex / totalChunks, 0.0f, 1.0f) : 0.0f;
				CallDeferred(nameof(EmitDownloadProgress), progress);
				if (totalAssets > 0 && assets.Count == 0) break;
			}

			string targetDirectory = MapAssetManager.GetMapDirectory(targetMap, targetVersion);
			await Task.Run(() =>
			{
				MapAssetManager.ExtractManifestFiles(manifest, targetDirectory);
				GameContext.AssetIndexService.RegisterManifest(manifest, Path.Combine(targetDirectory, "manifest.json"));
			});

		_downloadingMapName = null;
		CallDeferred(nameof(EmitDownloadCompleted));
		StartMatchPreparationForCurrentMap();
		return true;
	}
		catch (Exception ex)
		{
			GD.PrintErr($"[LobbyManager] Map download failed for '{targetMap}': {ex.Message}");
			_downloadingMapName = null;
			CallDeferred(nameof(EmitDownloadFailed));
			return false;
		}
		finally
		{
			_mapDownloadLock.Release();
		}
	}

	private void OnMapTransferReplied(MapTransferReply reply) => _transferReplyTcs?.TrySetResult(reply);

	private async Task<MapTransferReply?> RequestMapTransferAsync(MapTransferRequest request, int timeoutMilliseconds = 20000)
	{
		ClientServerSession? session = _lobbySession;
		if (session == null) return null;

		var reply = new TaskCompletionSource<MapTransferReply>(TaskCreationOptions.RunContinuationsAsynchronously);
		_transferReplyTcs = reply;
		try
		{
			if (!session.RequestMapTransfer(request)) return null;

			var completed = await Task.WhenAny(reply.Task, Task.Delay(timeoutMilliseconds));
			return completed == reply.Task ? reply.Task.Result : null;
		}
		finally
		{
			if (ReferenceEquals(_transferReplyTcs, reply)) _transferReplyTcs = null;
		}
	}

	private void EmitDownloadProgress(float progress)
	{
		MapDownloadProgressChanged?.Invoke(progress);
	}

	private void EmitDownloadCompleted()
	{
		ReportLocalMapReadyState(true);
		MapDownloadProgressChanged?.Invoke(1.0f);
		MapDownloadCompleted?.Invoke();
	}

	private void EmitDownloadFailed()
	{
		MapDownloadFailed?.Invoke();
	}

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
		PlayerInfo? me = PlayerList.Find(IsLocalPlayer);
		if (me != null)
		{
			me.Name = username;
			SendSeatUpdate(me);
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
		GameContext.UnregisterLobby(this);
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
		_manifestTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

		Callable.From(() => RpcId(1, nameof(RequestMapManifestRpc), targetMap)).CallDeferred();

		var manifestTask = await Task.WhenAny(_manifestTcs.Task, Task.Delay(10000));
		string? manifestJson = manifestTask == _manifestTcs.Task ? _manifestTcs.Task.Result : null;

		if (string.IsNullOrWhiteSpace(manifestJson)) return null;

		try
		{
			return MapManifest.LoadFromJson(manifestJson);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[LobbyManager] Failed to deserialize manifest from host: {ex.Message}");
			return null;
		}
	}

	private async Task RegisterDownloadedMapAsync(string targetMap, MapManifest manifest)
	{
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
				GD.PrintErr("[LobbyManager] Disconnected from the peer serving the map.");
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

	public async Task<bool> DownloadMapEphemerallyAsync(string targetIp, int targetPort, string mapName, Action<float>? progressCallback = null, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(mapName) || string.IsNullOrWhiteSpace(targetIp) || targetPort <= 0) return false;
		if (_lobbySession != null) return false;

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

	private static uint PackColor(Color color)
	{
		uint red = (uint)Math.Clamp(Math.Round(color.R * 255.0), 0.0, 255.0);
		uint green = (uint)Math.Clamp(Math.Round(color.G * 255.0), 0.0, 255.0);
		uint blue = (uint)Math.Clamp(Math.Round(color.B * 255.0), 0.0, 255.0);
		uint alpha = (uint)Math.Clamp(Math.Round(color.A * 255.0), 0.0, 255.0);
		return (red << 24) | (green << 16) | (blue << 8) | alpha;
	}

	private static Color UnpackColor(uint rgba)
	{
		if (rgba == 0) return PlayerColorConfig.GetColor(1);

		float red = ((rgba >> 24) & 0xFF) / 255.0f;
		float green = ((rgba >> 16) & 0xFF) / 255.0f;
		float blue = ((rgba >> 8) & 0xFF) / 255.0f;
		float alpha = (rgba & 0xFF) / 255.0f;
		return new Color(red, green, blue, alpha);
	}

	private static string Tr(string text) => TranslationServer.Translate(text);
}
