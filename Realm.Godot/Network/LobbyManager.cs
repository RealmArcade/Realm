using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SharpToken;
using Realm.Godot.Services;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using ZstdSharp;

public partial class LobbyManager : Node
{
    public static LobbyManager Instance { get; private set; }
    public bool IsSinglePlayer { get; set; } = false;
    public string? LastHostError { get; private set; }

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static string GetBaseGameDirectory()
    {
        string exePath = OS.GetExecutablePath();
        string baseDir = System.IO.Path.GetDirectoryName(exePath) ?? "";
        
        int versionsIndex = baseDir.IndexOf($"{System.IO.Path.DirectorySeparatorChar}versions{System.IO.Path.DirectorySeparatorChar}");
        if (versionsIndex == -1) versionsIndex = baseDir.IndexOf("/versions/");
        if (versionsIndex == -1 && baseDir.EndsWith($"{System.IO.Path.DirectorySeparatorChar}versions")) versionsIndex = baseDir.Length - 9;
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
        string fileName = System.IO.Path.GetFileName(exePath);

        return System.IO.Path.Combine(baseDir, "versions", targetVersion, fileName);
    }

    public class PlayerInfo
    {
        public int PeerId { get; set; }
        public int Slot { get; set; }
        public string Name { get; set; } = "";
        public string Faction { get; set; } = "HUMAN";
        public string Team { get; set; } = "Team 1";
        public Color Color { get; set; } = PlayerColorConfig.GetColor(1);
        public bool IsHost { get; set; }
        public string Latency { get; set; } = "--";
        public string Jitter { get; set; } = "--";
        public string PacketLoss { get; set; } = "--";
        public bool IsReady { get; set; }
        public string BinaryVersion { get; set; } = "";
        public bool IsMapReady { get; set; } = true;
        public string SessionToken { get; set; } = Guid.NewGuid().ToString();
        public bool IsDisconnected { get; set; } = false;
    }

    public List<string> OfficialServers { get; private set; } = new();
    public List<string> RegistryServers => OfficialServers;
    private int _currentServerIndex = 0;
    public string RegistryServerUrl => OfficialServers.Count > 0 && _currentServerIndex < OfficialServers.Count && !string.IsNullOrWhiteSpace(OfficialServers[_currentServerIndex]) ? OfficialServers[_currentServerIndex] : ServersConfigHelper.GetDefaultServerUrl();
    public int ENetPort { get; private set; } = 8999;
    public int MaxPlayers { get; set; } = 8;
    
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
        if (LocalPlayer != null)
        {
            LocalPlayer.Name = AuthenticatedUsername;
        }
        return true;
    }
    private readonly string _persistentSessionToken = Guid.NewGuid().ToString();
    private bool _isReconnecting = false;
    public bool IsReconnecting => _isReconnecting;

    public NatType LocalNatType { get; private set; } = NatType.Open;
    public bool IsHost { get; set; }
    public string? ActiveLobbyId { get; private set; }
    public bool IsGameStarted { get; set; }
    public DateTime? GameSessionStartTime { get; private set; }
    public string ActiveMapName { get; set; }
    public string ActiveMapVersion { get; set; } = "1.0.0";
    public bool SpectatorDelay { get; set; } = false;
    public string? LobbyJoinError { get; set; }
    public string HostStability { get; set; } = "Excellent";
    public event System.Action<string> HostStabilityUpdated;


    public List<PlayerInfo> PlayerList { get; } = new();
    public PlayerInfo LocalPlayer { get; private set; } = new();


    private readonly System.Net.Http.HttpClient _httpClient = new();
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
    private readonly List<(string Sender, string Message, bool IsMuted)> _chatHistory = new();
    private static InferenceSession? _onnxSession;
    private static Dictionary<string, int>? _vocab;
    private static readonly object _sessionLock = new();
    private readonly SemaphoreSlim _mapDownloadLock = new(1, 1);
    private struct HostAssetTransferItem
    {
        public string AssetKey;
        public string SourceFilePath;
        public string? Metadata;
        public long Size;
    }

    private class HostTransferSession
    {
        public string TransferId { get; set; } = string.Empty;
        public int PeerId { get; set; }
        public string MapName { get; set; } = string.Empty;
        public string MapVersion { get; set; } = string.Empty;
        public List<List<HostAssetTransferItem>> ChunkItems { get; set; } = new();
        public int CurrentChunkIndex { get; set; } = 0;
        public int TotalChunks => ChunkItems.Count;
        public long TotalRawBytes { get; set; }
        public CancellationTokenSource Cts { get; set; } = new();
    }

    private class ClientTransferSession
    {
        public string TransferId { get; set; } = string.Empty;
        public string MapName { get; set; } = string.Empty;
        public string MapVersion { get; set; } = string.Empty;
        public MapManifest Manifest { get; set; } = new();
        public long ExpectedTotalBytes { get; set; }
        public int ExpectedTotalChunks { get; set; }
        public int CurrentChunkIndex { get; set; }
        public MemoryStream CurrentChunkStream { get; set; } = new();
        public int ReceivedPacketsInCurrentChunk { get; set; }
        public long TotalReceivedBytes { get; set; }
        public int TotalAssetsInManifest { get; set; }
        public int AlreadyPresentAssets { get; set; }
        public int SessionMissingAssets { get; set; }
        public DateTime LastActivityTimeUtc { get; set; } = DateTime.UtcNow;
        public float LastEmittedProgress { get; set; } = -1.0f;
        public long LastProgressEmitTicks { get; set; } = 0;
    }

    private readonly ConcurrentDictionary<string, HostTransferSession> _hostTransfers = new();
    private TaskCompletionSource<string>? _manifestTcs;
    private TaskCompletionSource<bool>? _transferCompleteTcs;
    private ClientTransferSession? _currentClientTransfer;
    private int _activeEphemeralTransferPeerId = 0;

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
    public event Action<string, string, bool>? ChatReceived;
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

    public override void _Ready()
    {
        Instance = this;
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

        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await Task.Delay(5000);
                    if (!GodotObject.IsInstanceValid(this) || IsQueuedForDeletion()) break;
                    if (PeerSeederManager.Instance != null)
                    {
                        Callable.From(() =>
                        {
                            if (GodotObject.IsInstanceValid(this) && !IsQueuedForDeletion() && PeerSeederManager.Instance != null)
                            {
                                PeerSeederManager.Instance.CheckIdleAndSeedStatus();
                            }
                        }).CallDeferred();
                    }
                }
                catch { break; }
            }
        });
    }

    public override void _Notification(int what)
    {
        if (what == (int)NotificationWMCloseRequest || what == (int)NotificationPredelete)
        {
            CleanUpLobbySynchronously();
        }
        base._Notification(what);
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
            PeerSeederManager.Instance.Stop();
        }
        catch { }

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
                
                var task = Task.Run(async () =>
                {
                    await _httpClient.PostAsync($"{RegistryServerUrl}/lobbies/close", jsonContent);
                });
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
        

        try
        {
            var dnsAddresses = await Dns.GetHostAddressesAsync("stun.l.google.com");
            if (dnsAddresses.Length > 0)
            {
                using var udp = new System.Net.Sockets.UdpClient();
                udp.ExclusiveAddressUse = false;
                udp.Client.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket, System.Net.Sockets.SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0));
                
                var serverEp = new System.Net.IPEndPoint(dnsAddresses[0], 19302);
                byte[] req = new byte[20];
                req[0] = 0x00; req[1] = 0x01; // Binding Request
                new Random().NextBytes(new Span<byte>(req, 4, 16));
                
                await udp.SendAsync(req, req.Length, serverEp);
                var receiveTask = udp.ReceiveAsync();
                var timeoutTask = Task.Delay(1000);
                if (await Task.WhenAny(receiveTask, timeoutTask) == receiveTask)
                {
                    var response = await receiveTask;
                    var result = new NatTypeTester.StunResult();
                    NatTypeTester.ParseStunResponse(response.Buffer, result);
                    if (result.Success && result.MappedEndPoint != null)
                    {
                        _hostPublicIp = result.MappedEndPoint.Address.ToString();
                        _hostPublicPort = ENetPort;
                        GD.Print($"[LobbyManager] Public Endpoint Mapped: {_hostPublicIp}:{_hostPublicPort}");
                    }
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



    public void HostSinglePlayerGame(string mapPathName, string mapDisplayName, string? mapVersion = null)
    {
        IsSinglePlayer = true;
        IsHost = true;
        IsGameStarted = true;
        ActiveMapName = mapPathName;
        ActiveMapVersion = !string.IsNullOrWhiteSpace(mapVersion) ? mapVersion : "1.0.0";
        PlayerList.Clear();
        
        LocalPlayer = new PlayerInfo
        {
            PeerId = 1,
            Slot = 0,
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
        PlayerList.Add(LocalPlayer);
        
        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        CallDeferred(nameof(LoadMap), mapPathName);
    }

    public async Task<bool> HostLobbyAsync(string mapPathName, string mapDisplayName, string? explicitVersion = null)
    {
        IsHost = true;
        HostStability = HostStabilityTracker.GetOverallStability();
        IsGameStarted = false;
        SpectatorDelay = false;
        LastHostError = null;
        PlayerList.Clear();
        ActiveMapName = mapPathName;
        ActiveMapVersion = !string.IsNullOrWhiteSpace(explicitVersion) ? explicitVersion : "1.0.0";


        LocalPlayer = new PlayerInfo
        {
            PeerId = 1,
            Slot = 0,
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
        PlayerList.Add(LocalPlayer);


        await RunNatTypeTestAsync();
        if (LocalNatType == NatType.Symmetric)
        {
            LastHostError = "Lobby creation rejected: Symmetric NAT is not supported.";
            GD.PrintErr("[LobbyManager] Lobby creation rejected: Symmetric NAT is not supported.");
            return false;
        }

        if (Multiplayer.MultiplayerPeer != null)
        {
            try
            {
                Multiplayer.MultiplayerPeer.Close();
            }
            catch { }
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


        Diagnostics.StartHostListener(ENetPort + 1);
        StartHostDiagnosticsTimer();





        try
        {
            int hostPingBaseline = await MeasurePingToRegistryAsync();
            string localIpAddress = GetLocalIPAddress();
            
            string mapVersion = !string.IsNullOrWhiteSpace(explicitVersion) ? explicitVersion.Trim() : "1.0.0";
            string signature = "";
            string publicKey = "";
            string mapHash = "";

            try
            {
                string? manifestPath = MapAssetManager.FindManifestPath(mapPathName, mapVersion)
                    ?? MapAssetManager.FindManifestPath(mapDisplayName, mapVersion);

                if (!string.IsNullOrEmpty(manifestPath) && System.IO.File.Exists(manifestPath))
                {
                    string json = System.IO.File.ReadAllText(manifestPath);
                    using var mapDoc = JsonDocument.Parse(json);
                    var root = mapDoc.RootElement;
                    if (root.TryGetProperty("Version", out var vProp) && vProp.ValueKind == JsonValueKind.String)
                    {
                        mapVersion = vProp.GetString() ?? mapVersion;
                    }
                    else if (root.TryGetProperty("MapProperties", out var props) && props.TryGetProperty("MapVersion", out var mv))
                    {
                        mapVersion = mv.GetString() ?? mapVersion;
                    }
                    if (root.TryGetProperty("signature", out var sigProp))
                    {
                        signature = sigProp.GetString() ?? "";
                    }
                    if (root.TryGetProperty("author_key", out var keyProp))
                    {
                        publicKey = keyProp.GetString() ?? "";
                    }
                    if (!string.IsNullOrEmpty(signature) && !string.IsNullOrEmpty(publicKey))
                    {
                        byte[] mapBytes = System.IO.File.ReadAllBytes(manifestPath);
                        string mapBlake3 = RealmMetadataHelper.ComputeBlake3(mapBytes, ".json");
                        mapHash = $"{mapBlake3}.json";
                    }
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
                LocalIP = localIpAddress,
                MapVersion = mapVersion,
                Signature = signature,
                PublicKey = publicKey,
                MapHash = mapHash,
                MapSizeBytes = mapSizeBytes
            };

            HttpResponseMessage? response = null;
            for (int i = 0; i < RegistryServers.Count; i++)
            {
                try
                {
                    var jsonContent = new StringContent(JsonSerializer.Serialize(registerPayload), Encoding.UTF8, "application/json");
                    response = await _httpClient.PostAsync($"{RegistryServerUrl}/lobbies/register", jsonContent);
                    if (response.IsSuccessStatusCode)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[LobbyManager] Register failed on {RegistryServerUrl}: {ex.Message}");
                }
                SwitchToNextServer();
            }

            if (response == null || !response.IsSuccessStatusCode)
            {
                string errMsg = "Registry server registration failed.";
                if (response != null)
                {
                    try
                    {
                        string body = await response.Content.ReadAsStringAsync();
                        using var errDoc = JsonDocument.Parse(body);
                        if (errDoc.RootElement.TryGetProperty("message", out var msgProp) ||
                            errDoc.RootElement.TryGetProperty("Message", out msgProp) ||
                            errDoc.RootElement.TryGetProperty("title", out msgProp) ||
                            errDoc.RootElement.TryGetProperty("detail", out msgProp))
                        {
                            errMsg = msgProp.GetString() ?? errMsg;
                        }
                        else if (!string.IsNullOrWhiteSpace(body))
                        {
                            errMsg = body;
                        }
                    }
                    catch {}
                }
                LastHostError = errMsg;
                GD.PrintErr($"[LobbyManager] {errMsg}");
                if (response != null && response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    Multiplayer.MultiplayerPeer = null;
                    IsHost = false;
                    return false;
                }
                return true; // proceed locally if registry is offline
            }

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
        catch (Exception ex)
        {
            GD.PrintErr($"[LobbyManager] Registry registration error: {ex.Message}");
        }

        PlayerListUpdated?.Invoke();
        return true;
    }

    public async Task<bool> JoinLobbyAsync(string lobbyId)
    {
        IsHost = false;
        IsGameStarted = false;
        ActiveLobbyId = lobbyId;
        PlayerList.Clear();


        LocalPlayer = new PlayerInfo
        {
            PeerId = 0, // Assigned by server
            Slot = -1,
            Name = AuthenticatedUsername,
            Faction = "HUMAN",
            Team = "Team 1",
            Color = PlayerColorConfig.GetColor(2),
            IsHost = false,
            BinaryVersion = RealmVersion.GameBinaryVersion,
            SessionToken = _persistentSessionToken
        };


        await RunNatTypeTestAsync();

        string clientPublicIp = _hostPublicIp ?? "127.0.0.1";
        int clientPublicPort = _hostPublicPort > 0 ? _hostPublicPort : ENetPort; // Use same local port

        try
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
                    if (response.IsSuccessStatusCode)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[LobbyManager] Join failed on {RegistryServerUrl}: {ex.Message}");
                }
                SwitchToNextServer();
            }

            if (response == null || !response.IsSuccessStatusCode)
            {
                var errorText = response != null ? await response.Content.ReadAsStringAsync() : "All registry nodes offline";
                GD.PrintErr($"[LobbyManager] Failed to join lobby: {errorText}");
                ConnectionFailed?.Invoke("Failed to coordinate join.");
                return false;
            }

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
            _connectedHostIp = connectIp;
            _connectedHostPort = connectPort;

            bool isLocalConnection = IsPrivateIp(connectIp);
            if (!isLocalConnection)
            {
                await UdpHolePuncher.PunchHoleAsync(connectIp, connectPort, ENetPort);
            }

            var peer = new ENetMultiplayerPeer();
            var err = peer.CreateClient(connectIp, connectPort, localPort: isLocalConnection ? 0 : ENetPort);
            if (err != Error.Ok)
            {
                GD.PrintErr($"[LobbyManager] Failed to create ENet Client: {err}");
                ConnectionFailed?.Invoke("Failed to bind network socket.");
                return false;
            }
            var packetPeer = peer.GetPeer(1);
            if (packetPeer != null)
            {
                packetPeer.SetTimeout(32, 5000, 15000);
            }
            Multiplayer.MultiplayerPeer = peer;
            if (Multiplayer is SceneMultiplayer sceneMultiplayer)
            {
                sceneMultiplayer.ServerRelay = false;
            }
            GD.Print($"[LobbyManager] ENet Client initialized. Connecting to {connectIp}:{connectPort}...");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LobbyManager] Join error: {ex.Message}");
            ConnectionFailed?.Invoke(ex.Message);
            return false;
        }

        return true;
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
            catch { }
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

                byte[] buffer = new byte[1024 * 4];
                while (_hostWebSocket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    var result = await _hostWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        if (root.GetProperty("Action").GetString() == "Punch")
                        {
                            string clientIp = root.GetProperty("ClientIP").GetString() ?? "";
                            int clientPort = root.GetProperty("ClientPort").GetInt32();
                            
                            GD.Print($"[LobbyManager] WebSocket signal: incoming client. Punching to {clientIp}:{clientPort}...");
                            

                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await UdpHolePuncher.PunchHoleAsync(clientIp, clientPort, ENetPort);
                                }
                                catch (Exception ex)
                                {
                                    GD.PrintErr($"[LobbyManager] Async punch to client failed: {ex.Message}");
                                }
                            });
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException) { }
            catch (Exception ex)
            {
                GD.PrintErr($"[LobbyManager] Host WebSocket error: {ex.Message}");
            }
        }, token);
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
                catch { /* Ignore heartbeat errors */ }
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
                GD.Print($"[LobbyManager] Rejecting peer {id}: Lobby is full.");
                RpcId(id, nameof(RejectConnection), "Lobby is full");
                

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


            var newPlayer = new PlayerInfo
            {
                PeerId = id,
                Slot = PlayerList.Count,
                Name = $"Player_{id}",
                Faction = "HUMAN",
                Team = "Team 1",
                Color = GetNextColor(),
                IsHost = false,
                BinaryVersion = RealmVersion.GameBinaryVersion,
                IsMapReady = false,
                SessionToken = Guid.NewGuid().ToString()
            };
            PlayerList.Add(newPlayer);
            SendChatMessage("System", string.Format(Tr("{0} joined the lobby."), newPlayer.Name));


            UpdateAllPeerDiagnostics();
            RpcId(id, nameof(SyncSpectatorDelay), SpectatorDelay);
            RpcId(id, nameof(SyncHostStability), HostStability);
            RpcId(id, nameof(SyncActiveMap), ActiveMapName);
        }
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
                    PlayerList[i].Slot = i;
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
        LocalPlayer.PeerId = myId;
        _isConnectedToHost = true;
    }

    public async Task<bool> EnsureMapDownloadedAsync(string? mapName = null)
    {
        string targetMap = !string.IsNullOrWhiteSpace(mapName) ? mapName : ActiveMapName;
        if (string.IsNullOrWhiteSpace(targetMap) || IsHost)
        {
            return true;
        }

        await _mapDownloadLock.WaitAsync();
        try
        {
            if (MapAssetManager.IsMapDownloaded(targetMap))
            {
                CallDeferred(nameof(EmitDownloadCompleted));
                return true;
            }

            bool isConnectedToHost = !IsHost && _isConnectedToHost;

            if (isConnectedToHost)
            {
                GD.Print($"[LobbyManager] Requesting map manifest for '{targetMap}' from host via reliable ENet RPC...");
                _manifestTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

                Callable.From(() => RpcId(1, nameof(RequestMapManifestRpc), targetMap)).CallDeferred();

                var manifestTask = await Task.WhenAny(_manifestTcs.Task, Task.Delay(10000));
                string? manifestJson = manifestTask == _manifestTcs.Task ? _manifestTcs.Task.Result : null;

                if (!string.IsNullOrWhiteSpace(manifestJson))
                {
                    MapManifest? manifest = null;
                    try
                    {
                        manifest = MapManifest.LoadFromJson(manifestJson) ?? JsonSerializer.Deserialize<MapManifest>(manifestJson);
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[LobbyManager] Failed to deserialize manifest from host: {ex.Message}");
                    }

                    if (manifest != null)
                    {
                        var allHashes = manifest.Files != null ? manifest.Files.Values.ToList() : new List<string>();
                        int totalManifestFiles = allHashes.Count;
                        int maxRetries = 10;
                        int retryCount = 0;

                        while (retryCount < maxRetries && _isConnectedToHost)
                        {
                            var missingHashes = await Task.Run(() => MapAssetManager.GetMissingHashes(allHashes));

                            if (missingHashes.Count == 0)
                            {
                                GD.Print($"[LobbyManager] All assets for '{targetMap}' now exist locally in CAS.");
                                string targetMapDir = MapAssetManager.GetMapDirectory(targetMap, manifest.Version ?? "1.0.0");
                                await Task.Run(() =>
                                {
                                    MapAssetManager.ExtractManifestFiles(manifest, targetMapDir);
                                    string localManifestPath = Path.Combine(targetMapDir, "manifest.json");
                                    AssetIndexService.Instance.RegisterManifest(manifest, localManifestPath);
                                });

                                CallDeferred(nameof(EmitDownloadProgress), 1.0f);
                                CallDeferred(nameof(EmitDownloadCompleted));
                                return true;
                            }

                            float initialProgress = totalManifestFiles > 0
                                ? Math.Clamp((float)(totalManifestFiles - missingHashes.Count) / totalManifestFiles, 0.0f, 1.0f)
                                : 0.0f;
                            CallDeferred(nameof(EmitDownloadProgress), initialProgress);

                            GD.Print($"[LobbyManager] Missing {missingHashes.Count}/{totalManifestFiles} assets for '{targetMap}' (attempt {retryCount + 1}/{maxRetries}). Requesting compressed zstd bundle from host...");
                            string transferId = Guid.NewGuid().ToString("N");

                            var transferSession = new ClientTransferSession
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

                            _currentClientTransfer = transferSession;
                            _transferCompleteTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                            Callable.From(() => RpcId(1, nameof(RequestMapAssetTransferRpc), transferId, targetMap, manifest.Version ?? "1.0.0", missingHashes.ToArray())).CallDeferred();

                            bool transferSuccess = false;
                            while (!_transferCompleteTcs.Task.IsCompleted)
                            {
                                if (!_isConnectedToHost || Multiplayer.MultiplayerPeer == null)
                                {
                                    GD.PrintErr("[LobbyManager] Disconnected from host during map download.");
                                    break;
                                }

                                var completedTask = await Task.WhenAny(_transferCompleteTcs.Task, Task.Delay(2000));
                                if (completedTask == _transferCompleteTcs.Task)
                                {
                                    transferSuccess = _transferCompleteTcs.Task.Result;
                                    break;
                                }

                                if (DateTime.UtcNow - transferSession.LastActivityTimeUtc > TimeSpan.FromSeconds(45))
                                {
                                    GD.PrintErr("[LobbyManager] ENet transfer inactivity timeout for chunk. Retrying remaining missing assets...");
                                    break;
                                }
                            }

                            if (transferSuccess)
                            {
                                _currentClientTransfer = null;
                                GD.Print($"[LobbyManager] Map '{targetMap}' successfully extracted and registered.");
                                CallDeferred(nameof(EmitDownloadProgress), 1.0f);
                                CallDeferred(nameof(EmitDownloadCompleted));
                                return true;
                            }

                            if (_currentClientTransfer != null)
                            {
                                try
                                {
                                    _currentClientTransfer.CurrentChunkStream.Dispose();
                                }
                                catch { }
                                _currentClientTransfer = null;
                            }

                            if (!_isConnectedToHost)
                            {
                                break;
                            }

                            retryCount++;
                            await Task.Delay(1000);
                        }
                    }
                }
            }

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

    public async Task<bool> DownloadMapEphemerallyAsync(
        string targetIp,
        int targetPort,
        string mapName,
        Action<float>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mapName) || string.IsNullOrWhiteSpace(targetIp) || targetPort <= 0)
        {
            return false;
        }

        if (MapAssetManager.IsMapDownloaded(mapName))
        {
            progressCallback?.Invoke(1.0f);
            MapDownloadProgressChanged?.Invoke(1.0f);
            MapDownloadCompleted?.Invoke();
            return true;
        }

        bool wasSeeding = PeerSeederManager.Instance != null && PeerSeederManager.Instance.IsSeeding;
        if (wasSeeding)
        {
            PeerSeederManager.Instance.Stop();
        }

        Action<float>? progressHandler = null;
        if (progressCallback != null)
        {
            progressHandler = p => progressCallback.Invoke(p);
            MapDownloadProgressChanged += progressHandler;
        }

        try
        {
            bool isLocal = targetIp == "127.0.0.1" || targetIp == "localhost" || targetIp == PublicIP;
            int clientPort = ENetPort + 16;
            if (!isLocal)
            {
                await UdpHolePuncher.PunchHoleAsync(targetIp, targetPort, clientPort);
            }

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
                return false;
            }

            var packetPeer = peer.GetPeer(1);
            if (packetPeer != null)
            {
                packetPeer.SetTimeout(32, 5000, 15000);
            }

            _isConnectedToHost = false;
            Multiplayer.MultiplayerPeer = peer;

            var connectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnConnected() => connectedTcs.TrySetResult(true);
            void OnFailed() => connectedTcs.TrySetResult(false);

            Multiplayer.ConnectedToServer += OnConnected;
            Multiplayer.ConnectionFailed += OnFailed;

            var connectTask = await Task.WhenAny(connectedTcs.Task, Task.Delay(8000, cancellationToken));
            Multiplayer.ConnectedToServer -= OnConnected;
            Multiplayer.ConnectionFailed -= OnFailed;

            if (connectTask != connectedTcs.Task || !connectedTcs.Task.Result)
            {
                GD.PrintErr($"[LobbyManager] Ephemeral connection to {targetIp}:{targetPort} timed out or failed.");
                try { peer.Close(); } catch { }
                Multiplayer.MultiplayerPeer = null;
                _isConnectedToHost = false;
                return false;
            }

            _isConnectedToHost = true;
            bool downloadResult = await EnsureMapDownloadedAsync(mapName);

            try { peer.Close(); } catch { }
            Multiplayer.MultiplayerPeer = null;
            _isConnectedToHost = false;

            return downloadResult;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LobbyManager] Ephemeral map download error: {ex.Message}");
            return false;
        }
        finally
        {
            if (progressHandler != null)
            {
                MapDownloadProgressChanged -= progressHandler;
            }

            if (Multiplayer.MultiplayerPeer != null)
            {
                try { Multiplayer.MultiplayerPeer.Close(); } catch { }
                Multiplayer.MultiplayerPeer = null;
            }
            _isConnectedToHost = false;

            if (wasSeeding)
            {
                PeerSeederManager.Instance?.CheckIdleAndSeedStatus();
            }
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestMapManifestRpc(string targetMap)
    {
        if (!IsHost && (PeerSeederManager.Instance == null || !PeerSeederManager.Instance.IsSeeding)) return;
        int senderId = Multiplayer.GetRemoteSenderId();
        var manifest = MapAssetManager.FindHostManifest(targetMap);
        if (manifest != null)
        {
            string json = JsonSerializer.Serialize(manifest);
            Callable.From(() => RpcId(senderId, nameof(ReceiveMapManifestRpc), targetMap, manifest.Version ?? "1.0.0", json)).CallDeferred();
        }
        else
        {
            Callable.From(() => RpcId(senderId, nameof(ReceiveMapManifestRpc), targetMap, "", "")).CallDeferred();
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveMapManifestRpc(string mapName, string mapVersion, string manifestJson)
    {
        _manifestTcs?.TrySetResult(manifestJson);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestMapAssetTransferRpc(string transferId, string mapName, string mapVersion, string[] missingHashes)
    {
        if (!IsHost && (PeerSeederManager.Instance == null || !PeerSeederManager.Instance.IsSeeding)) return;
        int senderId = Multiplayer.GetRemoteSenderId();

        if (PeerSeederManager.Instance != null && PeerSeederManager.Instance.IsSeeding)
        {
            if (_activeEphemeralTransferPeerId != 0 && _activeEphemeralTransferPeerId != senderId)
            {
                GD.Print($"[LobbyManager] Rejecting map transfer {transferId} from peer {senderId}: Seeder is currently serving peer {_activeEphemeralTransferPeerId}.");
                return;
            }
            _activeEphemeralTransferPeerId = senderId;
        }

        foreach (var kvp in _hostTransfers)
        {
            if (kvp.Value.PeerId == senderId)
            {
                kvp.Value.Cts.Cancel();
                _hostTransfers.TryRemove(kvp.Key, out _);
            }
        }

        _ = HandleHostAssetTransferAsync(senderId, transferId, mapName, mapVersion, missingHashes);
    }

    private async Task HandleHostAssetTransferAsync(int peerId, string transferId, string mapName, string mapVersion, string[] missingHashes)
    {
        var cts = new CancellationTokenSource();
        var session = new HostTransferSession
        {
            TransferId = transferId,
            PeerId = peerId,
            MapName = mapName,
            MapVersion = mapVersion,
            Cts = cts
        };
        _hostTransfers[transferId] = session;

        try
        {
            await Task.Run(() =>
            {
                var manifest = MapAssetManager.FindHostManifest(mapName, mapVersion);
                string? mapDir = null;
                string? manifestPath = MapAssetManager.FindManifestPath(mapName, mapVersion);
                if (!string.IsNullOrEmpty(manifestPath) && File.Exists(manifestPath))
                {
                    mapDir = Path.GetDirectoryName(manifestPath);
                }

                var localFileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (manifest != null && manifest.Files != null && !string.IsNullOrEmpty(mapDir))
                {
                    foreach (var kvp in manifest.Files)
                    {
                        string normKvp = ContentAddressableStorage.NormalizeBlake3Hash(kvp.Value);
                        string relPath = kvp.Key.Replace("res://", "").TrimStart('/', '\\');
                        string localFilePath = Path.Combine(mapDir, relPath);
                        if (File.Exists(localFilePath))
                        {
                            localFileMap[normKvp] = localFilePath;
                        }
                    }
                }

                var itemsToPack = new List<HostAssetTransferItem>(missingHashes.Length);
                foreach (var hash in missingHashes)
                {
                    string norm = ContentAddressableStorage.NormalizeBlake3Hash(hash);
                    string? filePath = MapAssetManager.Storage.FindAssetFilePath(norm) ?? MapAssetManager.P2PStorage.FindAssetFilePath(norm);
                    string? meta = null;

                    if (filePath != null && File.Exists(filePath))
                    {
                        meta = MapAssetManager.Storage.GetAssetMetadata(norm);
                    }
                    else if (localFileMap.TryGetValue(norm, out var localPath) && File.Exists(localPath))
                    {
                        filePath = localPath;
                    }

                    if (filePath != null && File.Exists(filePath))
                    {
                        long size = new FileInfo(filePath).Length;
                        itemsToPack.Add(new HostAssetTransferItem
                        {
                            AssetKey = hash,
                            SourceFilePath = filePath,
                            Metadata = meta,
                            Size = size
                        });
                    }
                    else
                    {
                        GD.PrintErr($"[LobbyManager] Host could not find asset payload for hash: {hash}");
                    }
                }

                var chunkList = new List<List<HostAssetTransferItem>>();
                var currentChunk = new List<HostAssetTransferItem>();
                long currentChunkBytes = 0;

                foreach (var item in itemsToPack)
                {
                    long estimated = item.Size + (item.AssetKey?.Length ?? 0) * 2 + (item.Metadata?.Length ?? 0) * 2 + 16;
                    if (currentChunk.Count > 0 && currentChunkBytes + estimated > ZstdAssetBundleHelper.MaxBundleChunkSize)
                    {
                        chunkList.Add(currentChunk);
                        currentChunk = new List<HostAssetTransferItem>();
                        currentChunkBytes = 0;
                    }
                    currentChunk.Add(item);
                    currentChunkBytes += estimated;
                }

                if (currentChunk.Count > 0)
                {
                    chunkList.Add(currentChunk);
                }

                session.ChunkItems = chunkList;
                session.TotalRawBytes = itemsToPack.Sum(a => a.Size);
            }, cts.Token);

            int totalChunks = session.TotalChunks;
            long totalBytes = session.TotalRawBytes;

            Callable.From(() => RpcId(peerId, nameof(BeginMapTransferRpc), transferId, mapName, mapVersion, totalBytes, totalChunks)).CallDeferred();

            if (totalChunks > 0)
            {
                await SendHostChunkAsync(session, 0);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LobbyManager] Error during host asset transfer {transferId} to peer {peerId}: {ex.Message}");
        }
    }

    private async Task SendHostChunkAsync(HostTransferSession session, int chunkIndex)
    {
        if (chunkIndex < 0 || chunkIndex >= session.ChunkItems.Count || session.Cts.IsCancellationRequested)
        {
            return;
        }

        try
        {
            byte[] compressedChunkBytes = await Task.Run(() =>
            {
                var chunkItems = session.ChunkItems[chunkIndex];
                var chunkAssets = new List<(string AssetKey, byte[] Data, string? Metadata)>(chunkItems.Count);
                foreach (var item in chunkItems)
                {
                    if (session.Cts.IsCancellationRequested) break;
                    if (File.Exists(item.SourceFilePath))
                    {
                        byte[] data = File.ReadAllBytes(item.SourceFilePath);
                        chunkAssets.Add((item.AssetKey, data, item.Metadata));
                    }
                }
                return ZstdAssetBundleHelper.CreateBundleBytes(chunkAssets, 1);
            }, session.Cts.Token);

            int totalPacketsInChunk = (int)Math.Ceiling((double)compressedChunkBytes.Length / ZstdAssetBundleHelper.PacketChunkSize);
            if (totalPacketsInChunk <= 0) totalPacketsInChunk = 1;

            int packetIndex = 0;
            int offset = 0;

            while (offset < compressedChunkBytes.Length)
            {
                if (session.Cts.IsCancellationRequested) break;

                int packetSize = Math.Min(ZstdAssetBundleHelper.PacketChunkSize, compressedChunkBytes.Length - offset);
                byte[] packetData = new byte[packetSize];
                Buffer.BlockCopy(compressedChunkBytes, offset, packetData, 0, packetSize);

                int currentPacketIndex = packetIndex;
                int currentChunkIndex = chunkIndex;
                int totalChunks = session.TotalChunks;
                int totalPackets = totalPacketsInChunk;
                long totalBytes = session.TotalRawBytes;

                Callable.From(() => RpcId(
                    session.PeerId,
                    nameof(SendMapTransferChunkRpc),
                    session.TransferId,
                    currentChunkIndex,
                    totalChunks,
                    currentPacketIndex,
                    totalPackets,
                    totalBytes,
                    packetData)).CallDeferred();

                offset += packetSize;
                packetIndex++;

                if (packetIndex % 2 == 0)
                {
                    await Task.Delay(1, session.Cts.Token);
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LobbyManager] Error sending chunk {chunkIndex} for transfer {session.TransferId}: {ex.Message}");
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestNextMapTransferChunkRpc(string transferId, int nextChunkIndex)
    {
        if (!IsHost && (PeerSeederManager.Instance == null || !PeerSeederManager.Instance.IsSeeding)) return;
        if (_hostTransfers.TryGetValue(transferId, out var session) && !session.Cts.IsCancellationRequested)
        {
            session.CurrentChunkIndex = nextChunkIndex;
            _ = SendHostChunkAsync(session, nextChunkIndex);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void BeginMapTransferRpc(string transferId, string mapName, string mapVersion, long totalBytes, int totalChunks)
    {
        if (_currentClientTransfer != null && _currentClientTransfer.TransferId == transferId)
        {
            _currentClientTransfer.LastActivityTimeUtc = DateTime.UtcNow;
            _currentClientTransfer.ExpectedTotalBytes = totalBytes;
            _currentClientTransfer.ExpectedTotalChunks = totalChunks;
            _currentClientTransfer.CurrentChunkIndex = 0;
            _currentClientTransfer.ReceivedPacketsInCurrentChunk = 0;
            _currentClientTransfer.CurrentChunkStream.SetLength(0);
            _currentClientTransfer.CurrentChunkStream.Position = 0;

            if (totalChunks == 0)
            {
                string targetMapDir = MapAssetManager.GetMapDirectory(mapName, mapVersion);
                MapAssetManager.ExtractManifestFiles(_currentClientTransfer.Manifest, targetMapDir);
                string localManifestPath = Path.Combine(targetMapDir, "manifest.json");
                AssetIndexService.Instance.RegisterManifest(_currentClientTransfer.Manifest, localManifestPath);

                Callable.From(() => RpcId(1, nameof(AcknowledgeMapTransferCompleteRpc), transferId)).CallDeferred();
                _transferCompleteTcs?.TrySetResult(true);
            }
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SendMapTransferChunkRpc(string transferId, int chunkIndex, int totalChunks, int packetIndex, int totalPacketsInChunk, long totalBytes, byte[] packetData)
    {
        if (_currentClientTransfer == null || _currentClientTransfer.TransferId != transferId)
        {
            return;
        }

        try
        {
            _currentClientTransfer.LastActivityTimeUtc = DateTime.UtcNow;
            _currentClientTransfer.ExpectedTotalChunks = totalChunks;
            _currentClientTransfer.ExpectedTotalBytes = totalBytes;
            _currentClientTransfer.CurrentChunkIndex = chunkIndex;

            _currentClientTransfer.CurrentChunkStream.Write(packetData, 0, packetData.Length);
            _currentClientTransfer.ReceivedPacketsInCurrentChunk++;
            _currentClientTransfer.TotalReceivedBytes += packetData.Length;

            float chunkFraction = totalPacketsInChunk > 0 ? (float)(packetIndex + 1) / totalPacketsInChunk : 1.0f;
            float sessionFraction = totalChunks > 0
                ? Math.Clamp(((float)chunkIndex + chunkFraction) / totalChunks, 0.0f, 1.0f)
                : 1.0f;

            float overallProgress;
            if (_currentClientTransfer.TotalAssetsInManifest > 0)
            {
                float downloadedAssetsInSession = sessionFraction * _currentClientTransfer.SessionMissingAssets;
                overallProgress = Math.Clamp((_currentClientTransfer.AlreadyPresentAssets + downloadedAssetsInSession) / _currentClientTransfer.TotalAssetsInManifest, 0.0f, 1.0f);
            }
            else
            {
                overallProgress = sessionFraction;
            }

            bool isChunkEnd = packetIndex + 1 >= totalPacketsInChunk;
            long nowTicks = System.Environment.TickCount64;
            if (isChunkEnd || overallProgress >= 1.0f || Math.Abs(overallProgress - _currentClientTransfer.LastEmittedProgress) >= 0.005f || (nowTicks - _currentClientTransfer.LastProgressEmitTicks) >= 100)
            {
                _currentClientTransfer.LastEmittedProgress = overallProgress;
                _currentClientTransfer.LastProgressEmitTicks = nowTicks;
                CallDeferred(nameof(EmitDownloadProgress), overallProgress);
            }

            if (isChunkEnd)
            {
                byte[] chunkBytes = _currentClientTransfer.CurrentChunkStream.ToArray();
                _currentClientTransfer.CurrentChunkStream.SetLength(0);
                _currentClientTransfer.CurrentChunkStream.Position = 0;
                _currentClientTransfer.ReceivedPacketsInCurrentChunk = 0;

                _ = ProcessReceivedChunkAsync(_currentClientTransfer, chunkIndex, totalChunks, chunkBytes);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LobbyManager] Error writing transfer packet: {ex.Message}");
            _transferCompleteTcs?.TrySetException(ex);
        }
    }

    private async Task ProcessReceivedChunkAsync(ClientTransferSession transferSession, int chunkIndex, int totalChunks, byte[] chunkBytes)
    {
        try
        {
            var extractedAssets = await Task.Run(() => ZstdAssetBundleHelper.ExtractBundleBytes(chunkBytes));
            GD.Print($"[LobbyManager] Decompressed chunk {chunkIndex + 1}/{totalChunks} ({extractedAssets.Count} assets) in memory.");

            transferSession.LastActivityTimeUtc = DateTime.UtcNow;

            await Task.Run(() =>
            {
                foreach (var (assetKey, data, metadata) in extractedAssets)
                {
                    string ext = Path.GetExtension(assetKey);
                    MapAssetManager.Storage.StoreAsset(data, ext, metadata, precomputedBlake3: assetKey);
                }
            });

            transferSession.LastActivityTimeUtc = DateTime.UtcNow;

            if (chunkIndex + 1 < totalChunks)
            {
                Callable.From(() => RpcId(1, nameof(RequestNextMapTransferChunkRpc), transferSession.TransferId, chunkIndex + 1)).CallDeferred();
            }
            else
            {
                string targetMapDir = MapAssetManager.GetMapDirectory(transferSession.MapName, transferSession.MapVersion);
                await Task.Run(() =>
                {
                    MapAssetManager.ExtractManifestFiles(transferSession.Manifest, targetMapDir);
                    string localManifestPath = Path.Combine(targetMapDir, "manifest.json");
                    AssetIndexService.Instance.RegisterManifest(transferSession.Manifest, localManifestPath);
                });

                Callable.From(() => RpcId(1, nameof(AcknowledgeMapTransferCompleteRpc), transferSession.TransferId)).CallDeferred();
                _transferCompleteTcs?.TrySetResult(true);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LobbyManager] Error extracting chunk {chunkIndex}: {ex.Message}");
            _transferCompleteTcs?.TrySetException(ex);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void AcknowledgeMapTransferCompleteRpc(string transferId)
    {
        if (_hostTransfers.TryRemove(transferId, out var session))
        {
            session.Cts.Cancel();
            if (_activeEphemeralTransferPeerId == session.PeerId)
            {
                _activeEphemeralTransferPeerId = 0;
            }
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
        if (IsGameStarted)
        {
            GD.Print("[LobbyManager] Allowing local play after host disconnect. Starting reconnect attempt...");
            TriggerReconnect();
            return;
        }
        KickReceived?.Invoke("Host closed the server.");
        Disconnect();
    }

    public void TriggerReconnect()
    {
        if (_isReconnecting || !IsGameStarted || IsHost)
        {
            return;
        }
        _ = AttemptReconnectLoopAsync();
    }

    private async Task AttemptReconnectLoopAsync()
    {
        if (_isReconnecting || !IsGameStarted || IsHost)
        {
            return;
        }

        _isReconnecting = true;
        _isConnectedToHost = false;
        string hostIp = _connectedHostIp;
        int hostPort = _connectedHostPort;

        GD.Print($"[LobbyManager] Starting match reconnect loop to {hostIp}:{hostPort}...");

        DateTime startTime = DateTime.UtcNow;
        TimeSpan maxTimeout = TimeSpan.FromSeconds(60);

        while (_isReconnecting && IsGameStarted && DateTime.UtcNow - startTime < maxTimeout)
        {
            try
            {
                if (Multiplayer.MultiplayerPeer != null)
                {
                    try { Multiplayer.MultiplayerPeer.Close(); } catch { }
                    Multiplayer.MultiplayerPeer = null;
                }

                bool isLocal = IsPrivateIp(hostIp);
                if (!isLocal)
                {
                    await UdpHolePuncher.PunchHoleAsync(hostIp, hostPort, ENetPort);
                }

                var peer = new ENetMultiplayerPeer();
                var err = peer.CreateClient(hostIp, hostPort, localPort: isLocal ? 0 : ENetPort);
                if (err == Error.Ok)
                {
                    Multiplayer.MultiplayerPeer = peer;
                    DateTime connectTimeout = DateTime.UtcNow.AddSeconds(4);
                    while (DateTime.UtcNow < connectTimeout && peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connecting)
                    {
                        await Task.Delay(100);
                    }

                    if (peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected)
                    {
                        GD.Print("[LobbyManager] Connected socket during reconnect. Sending RequestReconnect RPC...");
                        RpcId(1, nameof(RequestReconnect), LocalPlayer.SessionToken, LocalPlayer.Name, RealmVersion.GameBinaryVersion);

                        DateTime ackTimeout = DateTime.UtcNow.AddSeconds(5);
                        while (DateTime.UtcNow < ackTimeout && _isReconnecting)
                        {
                            await Task.Delay(150);
                        }

                        if (!_isReconnecting)
                        {
                            GD.Print("[LobbyManager] Match reconnect handshake completed.");
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[LobbyManager] Reconnect attempt failed: {ex.Message}");
            }

            await Task.Delay(2000);
        }

        if (_isReconnecting)
        {
            _isReconnecting = false;
            GD.PrintErr("[LobbyManager] Match reconnection timed out.");
            Disconnect();
            CallDeferred(nameof(OnReconnectFailed));
        }
    }

    private void OnReconnectFailed()
    {
        KickReceived?.Invoke(Tr("Connection to host lost permanently."));
        UIManager.Instance?.TransitionTo(GameScreen.LobbyBrowser);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void RequestReconnect(string sessionToken, string playerName, string binaryVersion)
    {
        if (!IsHost) return;
        int senderId = Multiplayer.GetRemoteSenderId();
        GD.Print($"[LobbyManager] RequestReconnect received from Peer {senderId}: Token={sessionToken}, Name={playerName}");

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

        PlayerInfo? targetPlayer = null;
        if (!string.IsNullOrEmpty(sessionToken))
        {
            targetPlayer = PlayerList.Find(p => p.SessionToken == sessionToken);
        }
        if (targetPlayer == null && !string.IsNullOrEmpty(playerName))
        {
            targetPlayer = PlayerList.Find(p => p.Name == playerName);
        }

        if (targetPlayer == null)
        {
            RpcId(senderId, nameof(RejectConnection), Tr("Player session not found in active match."));
            return;
        }

        int oldPeerId = targetPlayer.PeerId;
        targetPlayer.PeerId = senderId;
        targetPlayer.IsDisconnected = false;

        GD.Print($"[LobbyManager] Reconnected player {targetPlayer.Name} (Slot {targetPlayer.Slot}). Remapped old peer {oldPeerId} -> {senderId}");
        SendChatMessage("System", string.Format(Tr("{0} reconnected to the match."), targetPlayer.Name));

        string serializedPlayers = JsonSerializer.Serialize(PlayerList);
        RpcId(senderId, nameof(AcceptReconnect), targetPlayer.Slot, ActiveMapName, serializedPlayers);

        GameHost.Instance?.HandlePeerReconnected(oldPeerId, senderId, targetPlayer.Slot);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void AcceptReconnect(int slot, string activeMapName, string serializedPlayers)
    {
        GD.Print($"[LobbyManager] AcceptReconnect received! Slot={slot}, Map={activeMapName}");
        _isReconnecting = false;
        _isConnectedToHost = true;
        ActiveMapName = activeMapName;
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
        LocalPlayer.PeerId = myId;
        LocalPlayer.Slot = slot;

        var me = PlayerList.Find(p => p.Slot == slot || p.SessionToken == LocalPlayer.SessionToken);
        if (me != null)
        {
            me.PeerId = myId;
            me.IsDisconnected = false;
            LocalPlayer = me;
        }

        PlayerListUpdated?.Invoke();

        if (GameHost.Instance != null && GodotObject.IsInstanceValid(GameHost.Instance))
        {
            GameHost.Instance.OnClientReconnected(slot);
        }
        else
        {
            GetTree().ChangeSceneToFile("res://Main.tscn");
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
                

                int myId = Multiplayer.GetUniqueId();
                var me = PlayerList.Find(p => p.PeerId == myId);
                if (me != null)
                {
                    LocalPlayer = me;
                }

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
        UIManager.Instance.TransitionTo(GameScreen.LobbyBrowser);
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

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void UpdatePlayerSlot(int peerId, string faction, string team, Color color, string name)
    {
        if (IsHost)
        {
            var p = PlayerList.Find(x => x.PeerId == peerId);
            if (p != null)
            {
                p.Faction = faction;
                p.Team = team;
                p.Color = color;
                p.Name = name;
                BroadcastPlayerList();
            }
        }
        else
        {

            RpcId(1, nameof(UpdatePlayerSlot), peerId, faction, team, color, name);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void UpdateDiagnostics(int peerId, float minRtt, float maxRtt, float avgRtt, float jitter, float lossRate, int consecutiveLoss)
    {
        if (IsHost)
        {
            var p = PlayerList.Find(x => x.PeerId == peerId);
            if (p != null)
            {
                p.Latency = $"{Math.Round(avgRtt)} ms";
                p.Jitter = $"{Math.Round(jitter)} ms";
                p.PacketLoss = $"{Math.Round(lossRate)}% (Burst: {consecutiveLoss})";
                BroadcastPlayerList();
            }
        }
        else
        {
            RpcId(1, nameof(UpdateDiagnostics), peerId, minRtt, maxRtt, avgRtt, jitter, lossRate, consecutiveLoss);
        }
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
            if (LocalPlayer != null && LocalPlayer.PeerId == peerId)
            {
                LocalPlayer.IsReady = isReady;
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
        if (!IsHost && LocalPlayer != null)
        {
            LocalPlayer.IsMapReady = isMapReady;
            if (_isConnectedToHost && Multiplayer.MultiplayerPeer != null)
            {
                try
                {
                    RpcId(1, nameof(ReportMapReadyOnHost), LocalPlayer.PeerId, isMapReady);
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

    public void SendChatMessage(string senderName, string message, bool alliesOnly = false)
    {
        if (IsHost)
        {
            if (senderName == "System")
            {
                _chatHistory.Add((senderName, message, false));
                Rpc(nameof(ReceiveChatMessage), senderName, message, alliesOnly);
                ChatReceived?.Invoke(senderName, message, alliesOnly);
            }
            else
            {
                _ = ProcessAndSendChatMessageAsync(senderName, message, alliesOnly);
            }
        }
        else
        {
            RpcId(1, nameof(ReceiveChatMessage), senderName, message, alliesOnly);
        }
    }

    private static bool _isDownloadingModels = false;
    private static async Task InitializeToxicityModelAsync()
    {
        if (_onnxSession != null)
        {
            return;
        }

        if (_isDownloadingModels)
        {
            return;
        }
        _isDownloadingModels = true;

        try
        {

            string modelPath = "";
            string vocabPath = "";

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string path1 = System.IO.Path.Combine(baseDir, "Assets", "MLModels", "toxic-xlm-roberta", "model_quantized.onnx");
            string vPath1 = System.IO.Path.Combine(baseDir, "Assets", "MLModels", "toxic-xlm-roberta", "vocab.bin");

            string path2 = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Realm.Godot", "Assets", "MLModels", "toxic-xlm-roberta", "model_quantized.onnx");
            string vPath2 = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Realm.Godot", "Assets", "MLModels", "toxic-xlm-roberta", "vocab.bin");

            string path3 = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Assets", "MLModels", "toxic-xlm-roberta", "model_quantized.onnx");
            string vPath3 = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Assets", "MLModels", "toxic-xlm-roberta", "vocab.bin");

            if (System.IO.File.Exists(path1))
            {
                modelPath = path1;
                vocabPath = vPath1;
            }
            else if (System.IO.File.Exists(path2))
            {
                modelPath = path2;
                vocabPath = vPath2;
            }
            else if (System.IO.File.Exists(path3))
            {
                modelPath = path3;
                vocabPath = vPath3;
            }
            else
            {
                try
                {
                    string globalized = PathUtils.GlobalizePath("res://Assets/MLModels/toxic-xlm-roberta/model_quantized.onnx");
                    string globalizedVocab = PathUtils.GlobalizePath("res://Assets/MLModels/toxic-xlm-roberta/vocab.bin");
                    if (!string.IsNullOrEmpty(globalized) && System.IO.File.Exists(globalized))
                    {
                        modelPath = globalized;
                        vocabPath = globalizedVocab;
                    }
                }
                catch
                {
                }
            }

            if (string.IsNullOrEmpty(modelPath) || !System.IO.File.Exists(modelPath))
            {
                try
                {
                    string globalizedUserDir = ProjectSettings.GlobalizePath("user://Assets/MLModels/toxic-xlm-roberta");
                    if (!System.IO.Directory.Exists(globalizedUserDir))
                    {
                        System.IO.Directory.CreateDirectory(globalizedUserDir);
                    }

                    modelPath = System.IO.Path.Combine(globalizedUserDir, "model_quantized.onnx");
                    vocabPath = System.IO.Path.Combine(globalizedUserDir, "vocab.bin");

                    if (!System.IO.File.Exists(modelPath) || !System.IO.File.Exists(vocabPath))
                    {
                        GD.Print("[LobbyManager] Downloading toxicity models...");
                        var httpClient = new System.Net.Http.HttpClient();
                        
                        if (!System.IO.File.Exists(modelPath))
                        {
                            var modelBytes = await httpClient.GetByteArrayAsync("https://huggingface.co/hoan/multilingual-toxic-xlm-roberta-dynamic-quantized/resolve/main/model_quantized.onnx");
                            System.IO.File.WriteAllBytes(modelPath, modelBytes);
                        }

                        if (!System.IO.File.Exists(vocabPath))
                        {
                            var vocabBytes = await httpClient.GetByteArrayAsync("https://huggingface.co/hoan/multilingual-toxic-xlm-roberta-dynamic-quantized/resolve/main/vocab.bin");
                            System.IO.File.WriteAllBytes(vocabPath, vocabBytes);
                        }
                        GD.Print("[LobbyManager] Successfully downloaded toxicity models.");
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[LobbyManager] Error downloading toxicity models: {ex}");
                }
            }

            if (string.IsNullOrEmpty(modelPath) || !System.IO.File.Exists(modelPath))
            {
                throw new System.IO.FileNotFoundException("Model file not found.");
            }

            lock (_sessionLock)
            {
                _onnxSession = new InferenceSession(modelPath);

                using (var fs = new System.IO.FileStream(vocabPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                using (var reader = new System.IO.BinaryReader(fs, Encoding.UTF8))
                {
                    int count = reader.ReadInt32();
                    _vocab = new Dictionary<string, int>(count);
                    for (int i = 0; i < count; i++)
                    {
                        string token = reader.ReadString();
                        _vocab[token] = i;
                    }
                }
            }
        }
        finally
        {
            _isDownloadingModels = false;
        }
    }

    private async Task<bool> IsMessageToxicAsync(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        try
        {
            if (_onnxSession == null || _vocab == null)
            {
                await InitializeToxicityModelAsync();
            }

            return await Task.Run(() =>
            {
                var encoding = GptEncoding.GetEncoding("cl100k_base");
                var gptTokens = encoding.Encode(message);
                if (gptTokens == null || gptTokens.Count == 0)
                {
                    return false;
                }

                if (_onnxSession == null || _vocab == null)
                {
                    return false;
                }

                var tokens = new List<long>();
                tokens.Add(0);

                string normalized = message.Replace(" ", " ");
                if (!normalized.StartsWith(" "))
                {
                    normalized = " " + normalized;
                }

                int i = 0;
                while (i < normalized.Length)
                {
                    int longestMatchLength = 0;
                    int longestMatchId = -1;

                    int maxLen = Math.Min(normalized.Length - i, 50);
                    for (int len = 1; len <= maxLen; len++)
                    {
                        string sub = normalized.Substring(i, len);
                        if (_vocab!.TryGetValue(sub, out int id))
                        {
                            longestMatchLength = len;
                            longestMatchId = id;
                        }
                    }

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

                long[] inputIds = tokens.ToArray();
                long[] attentionMask = inputIds.Select(_ => 1L).ToArray();

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
                        if (prob > 0.5)
                        {
                            return true;
                        }
                    }
                }

                return false;
            });
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LobbyManager] Local toxicity check failed: {ex.Message}");
        }

        return false;
    }

    private async Task ProcessAndSendChatMessageAsync(string senderName, string message, bool alliesOnly)
    {
        bool isToxic = await IsMessageToxicAsync(message);
        _chatHistory.Add((senderName, message, isToxic));
        
        if (!isToxic)
        {
            if (alliesOnly)
            {
                string senderTeam = GetPlayerTeamByName(senderName);
				foreach (var player in PlayerList)
				{
					if (player.Team == senderTeam)
					{
						if (player.PeerId == LocalPlayer.PeerId)
						{
							ChatReceived?.Invoke(senderName, message, true);
						}
						else
						{
							RpcId(player.PeerId, nameof(ReceiveChatMessage), senderName, message, true);
						}
					}
				}
            }
            else
            {
                Rpc(nameof(ReceiveChatMessage), senderName, message, false);
                ChatReceived?.Invoke(senderName, message, false);
            }
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveChatMessage(string senderName, string message, bool alliesOnly)
    {
        if (IsHost)
        {
            _ = ProcessAndSendChatMessageAsync(senderName, message, alliesOnly);
        }
        else
        {
            ChatReceived?.Invoke(senderName, message, alliesOnly);
        }
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
                    RpcId(senderId, nameof(ReceiveChatMessage), chat.Sender, chat.Message, false);
                }
            }
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void LoadMap(string mapName)
    {
        GD.Print($"[LobbyManager] LoadMap RPC received for: {mapName}");
        Realm.Godot.ReplaySystem.ReplayPlaybackManager.Instance.StopReplay();
        ActiveMapName = mapName;

        IsGameStarted = true;
        GameSessionStartTime = DateTime.UtcNow;
        
        GetTree().ChangeSceneToFile("res://Main.tscn");
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
                Rpc(nameof(LoadMap), _countdownMapName);
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

    public void AddAIBot()
    {
        if (IsHost && PlayerList.Count < MaxPlayers)
        {
            int botId = -100;
            while (PlayerList.Exists(x => x.PeerId == botId))
            {
                botId--;
            }
            var botPlayer = new PlayerInfo
            {
                PeerId = botId,
                Slot = PlayerList.Count,
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
        }
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
                        PlayerList[i].Slot = i;
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
                    catch { }
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
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var response = await _httpClient.GetAsync($"{RegistryServerUrl}/lobbies");
            stopwatch.Stop();
            if (response.IsSuccessStatusCode)
            {
                return (int)stopwatch.ElapsedMilliseconds;
            }
        }
        catch { }
        return 100;
    }

    private HttpListener? _authHttpListener;

    public async Task<bool> StartOAuthFlowAsync(string provider)
    {
        _authHttpListener?.Stop();
        int port = 8089;
        _authHttpListener = new HttpListener();
        _authHttpListener.Prefixes.Add($"http://localhost:{port}/auth/callback/");
        try
        {
            _authHttpListener.Start();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LobbyManager] Failed to start local OAuth listener on port {port}: {ex.Message}");
            return false;
        }

        string loginUrl = $"{RegistryServerUrl}/auth/login?provider={provider}&port={port}";
        OS.ShellOpen(loginUrl);

        try
        {
            var context = await _authHttpListener.GetContextAsync();
            var request = context.Request;
            var response = context.Response;

            string? username = request.QueryString["username"];
            string? token = request.QueryString["token"];
            string? returnedProvider = request.QueryString["provider"];

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(token))
            {
                AuthenticatedUsername = username;
                AuthToken = token;
                AuthProvider = returnedProvider;
                if (LocalPlayer != null)
                {
                    LocalPlayer.Name = username;
                }

                GD.Print($"[LobbyManager] OAuth Login Success! Provider: {returnedProvider}, User: {username}");

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

                byte[] buffer = Encoding.UTF8.GetBytes(successHtml);
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();

                _authHttpListener.Stop();
                return true;
            }
            else
            {
                var errorHtml = "<h1>Login Failed</h1><p>Invalid parameters received.</p>";
                byte[] buffer = Encoding.UTF8.GetBytes(errorHtml);
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LobbyManager] Error handling OAuth callback: {ex.Message}");
        }
        finally
        {
            _authHttpListener?.Stop();
        }

        return false;
    }

    public override void _ExitTree()
    {
        _authHttpListener?.Stop();
        StopHostDiagnosticsTimer();
        base._ExitTree();
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
        if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enetMultiplayer)
        {
            bool changed = false;
            float totalRtt = 0f;
            float totalJitter = 0f;
            float totalLoss = 0f;
            int clientCount = 0;

            foreach (var p in PlayerList)
            {
                if (p.PeerId == 1 || p.PeerId < 0) continue;

                try
                {
                    var peer = enetMultiplayer.GetPeer(p.PeerId);
                    if (peer != null)
                    {
                        float rtt = (float)peer.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime);
                        float jitter = (float)peer.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTimeVariance);
                        float loss = (float)peer.GetStatistic(ENetPacketPeer.PeerStatistic.PacketLoss) / 65536.0f * 100.0f;

                        totalRtt += rtt;
                        totalJitter += jitter;
                        totalLoss += loss;
                        clientCount++;

                        string newLatency = $"{Math.Round(rtt)} ms";
                        string newJitter = $"{Math.Round(jitter)} ms";
                        string newLoss = $"{Math.Round(loss)}%";

                        if (p.Latency != newLatency || p.Jitter != newJitter || p.PacketLoss != newLoss)
                        {
                            p.Latency = newLatency;
                            p.Jitter = newJitter;
                            p.PacketLoss = newLoss;
                            changed = true;
                        }
                    }
                }
                catch { }
            }

            var hostInfo = PlayerList.Find(x => x.PeerId == 1);
            if (hostInfo != null)
            {
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
                    changed = true;
                }
            }

            if (changed)
            {
                BroadcastPlayerList();
            }
        }
        else
        {
            BroadcastPlayerList();
        }
    }

    private static string GetLocalIPAddress()
    {
        try
        {
            using (var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, 0))
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
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
            }
            catch { }
        }
        return "127.0.0.1";
    }

    private static bool IsPrivateIp(string ip)
    {
        if (ip == "127.0.0.1" || ip == "localhost") return true;
        if (IPAddress.TryParse(ip, out var address))
        {
            byte[] bytes = address.GetAddressBytes();
            if (bytes.Length == 4)
            {
                if (bytes[0] == 10) return true;
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                if (bytes[0] == 192 && bytes[1] == 168) return true;
            }
        }
        return false;
    }
}
