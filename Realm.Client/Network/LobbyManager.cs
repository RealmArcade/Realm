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
using Realm.Shared.Accounts;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Metadata;
using Realm.Shared.Network;
using Realm.Shared.Simulation;
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

	/// <summary>The factions the lobby's map offers, in the order its roster numbers them.</summary>
	public IReadOnlyList<string> LobbyFactions { get; private set; } = Array.Empty<string>();

	/// <summary>The number of teams the lobby offers, which is the highest team number a seat may hold.</summary>
	public int LobbyTeamCount { get; private set; } = LobbyLimits.DefaultTeamCount;

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

	/// <summary>
	///     How many seats in this lobby are playing rather than watching. A spectator holds no player seat, so it is not
	///     counted against the players this map seats.
	/// </summary>
	public int PlayingPlayerCount
	{
		get
		{
			int count = 0;
			for (int index = 0; index < PlayerList.Count; index++)
			{
				if (PlayerList[index].TeamIndex >= 1) count++;
			}

			return count;
		}
	}

	public string? LastLobbyWarning { get; private set; }

	public bool IsSignedIn => GameContext.Account.IsSignedIn;

	public long AccountId => GameContext.Account.AccountId;

	public string AuthenticatedUsername => GameContext.Account.DisplayName;

	/// <summary>
	///     The name a lobby seat carries, resolved by the same rule the profile screen shows, so the name on the card and the
	///     name in the lobby can never describe the player differently. The lobby appends the seat number when two peers turn out
	///     to carry the same name.
	/// </summary>
	public string PlayerDisplayName => GameContext.Account.EffectiveUserName;

	private readonly string _guestLobbySessionToken = Guid.NewGuid().ToString();

	/// <summary>
	///     The secret a lobby holds this screen's seat by. A signed-in player's secret is minted on this machine when the
	///     session is adopted and kept beside it, so it outlives a restart and is replaced only by signing out; a player
	///     playing without an account carries one made for this run. Neither is derived from anything the lobby shows, so
	///     no other peer can name a seat it does not hold.
	/// </summary>
	public string LobbySessionToken
	{
		get
		{
			if (!GameContext.Account.IsSignedIn) return _guestLobbySessionToken;

			string seatToken = GameContext.Account.LobbySeatToken;
			return seatToken.Length > 0 ? seatToken : _guestLobbySessionToken;
		}
	}

	public NatType LocalNatType { get; private set; } = NatType.Open;
	public bool IsHost { get; set; }
	public string? ActiveLobbyId { get; private set; }
	public bool IsGameStarted { get; set; }
	public DateTime? GameSessionStartTime { get; private set; }
	public int MatchSeed { get; private set; }
	public string ActiveMapName { get; set; } = null!;
	public string ActiveMapVersion { get; set; } = "1.0.0";
	public bool SpectatorDelay { get; set; } = false;

	/// <summary>
	///     The parameters the lobby's host configured for the match, as the lobby last published them. Every seat shows the
	///     same list; only the host may change it.
	/// </summary>
	public MatchOptionSet MatchOptions { get; } = new();

	public event Action? MatchOptionsChanged;
	public string? LobbyJoinError { get; set; }
	public string HostStability { get; set; } = "Excellent";
	public event Action<string>? HostStabilityUpdated;

	public List<PlayerInfo> PlayerList { get; } = new();

	public int LocalPeerId => _lobbySession?.LocalPeerId ?? -1;

	public int BoundPort => _lobbySession?.Port ?? ENetPort;

	public bool IsCountdownActive => _countdownActive;

	/// <summary>
	///     Whether the lobby's authority has described this lobby to this screen yet. A screen that has one is connected: it
	///     has its seat, it can chat, and whatever else it is waiting for (a map package, a warmed world) is work already
	///     running behind a lobby it is free to use.
	/// </summary>
	public bool HasLobbyState => _lobbyRevision > 0;

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
			GameLog.LogToConsole($"[LobbyManager] Switched to next bootstrap server: {RegistryServerUrl}");
		}
	}

	public void RandomizeServerIndex()
	{
		if (RegistryServers.Count > 1)
		{
			_currentServerIndex = Random.Shared.Next(RegistryServers.Count);
			GameLog.LogToConsole($"[LobbyManager] Randomized bootstrap server to: {RegistryServerUrl}");
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
				GameLog.LogError($"[LobbyManager] Failed to fetch lobbies from {RegistryServerUrl}: {ex.Message}");
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
	public event Action<HostLobbyStage>? HostLobbyStageChanged;
	public event Action<float>? MapDownloadProgressChanged;
	public event Action? MapDownloadCompleted;
	public event Action? MapDownloadFailed;
	public event Action<bool>? SpectatorDelayChanged;
	public event Action<string, int>? CountdownStarted;
	public event Action<int>? CountdownTick;
	public event Action? CountdownCancelled;
	public event Action? CountdownFinished;
	public event Action<string>? ActiveMapChanged;

	/// <summary>
	///     Raised when the lobby's map asks its host to read a warning before the match starts, with the text that host has
	///     to accept. The match stays at tick zero until it is accepted or the start is abandoned.
	/// </summary>
	public event Action<string>? StartWarningReceived;

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
		GameContext.MapStorageService.ImportEmbeddedMaps();
		AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

		StartPeerSeederLoop();
		WarmUpLocalAuthority();

		GameContext.Account.Changed += OnAccountChanged;
	}

	/// <summary>
	///     Keeps the local seat on the account it belongs to. A DisplayName change is announced to the lobby so every other
	///     screen sees the new name without the player leaving and rejoining.
	/// </summary>
	private void OnAccountChanged()
	{
		if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return;

		Callable.From(ApplyAccountChange).CallDeferred();
	}

	private void ApplyAccountChange()
	{
		PlayerInfo? me = PlayerList.Find(IsLocalPlayer);
		if (me == null) return;

		me.AccountId = AccountId;
		string displayName = PlayerDisplayName;
		if (string.Equals(me.Name, displayName, StringComparison.Ordinal)) return;

		me.Name = displayName;
		SendSeatUpdate(me);
	}

	/// <summary>
	///     Brings this screen's own authority up while the screen is still in its menus. It is started once here and stays
	///     up for the whole session: matches are put on it and taken off it, so hosting never pays for a process start and
	///     nothing is killed between one session and the next.
	/// </summary>
	private void WarmUpLocalAuthority()
	{
		// A harness screen that joins somebody else's authority is a player on a machine that already has one: warming a
		// second authority up here would give a room of twelve screens twelve authority processes to share one box, which
		// is not a room of twelve players and measures the box rather than the match.
		if (HarnessJoinsExistingAuthority) return;

		if (!ListenServerTarget.TryResolve(out ServerLaunchTarget? target, out string failure) || target == null)
		{
			GD.PushWarning($"[LobbyManager] This screen cannot host a match: {failure}");
			return;
		}

		GameContext.LocalAuthority.Configure(target, Path.Combine(OS.GetUserDataDir(), "logs"), OS.GetUserDataDir());
		_ = GameContext.LocalAuthority.EnsureStartedAsync();
	}

	/// <summary>
	///     Whether this screen was started as a member of a harness room rather than as a player's own session. No normal
	///     launch sets it; a harness sets it on the screens that join an authority another process already runs.
	/// </summary>
	private static bool HarnessJoinsExistingAuthority =>
		string.Equals(System.Environment.GetEnvironmentVariable("REALM_HARNESS_JOIN_ONLY"), "1", StringComparison.Ordinal);

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
				catch (Exception ex)
				{
					GameLog.LogError($"the peer-seeder status loop stopped after an error: {ex.GetType().Name}: {ex.Message}");
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

}