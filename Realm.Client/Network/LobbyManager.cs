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
using Realm.Ecs;
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
using Realm.Shared.Reachability;
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

	public void ReportHostError(string message)
	{
		LastHostError = message;
		GD.PushError($"[LobbyManager] {message}");
	}

	public string LastAuthorityLogPath { get; private set; } = string.Empty;

	public string LastAuthorityConsoleLogPath { get; private set; } = string.Empty;

	public bool IsRestrictedHost { get; private set; }

	/// <summary>
	///     Why the last attempt to host was refused before the lobby was published, when it was refused because no other
	///     player could reach it. The screen states this with the steps that fix it rather than as a bare failure.
	/// </summary>
	public HostReachabilityResult? LastHostFailure { get; private set; }

	/// <summary>What the local firewall said when this lobby asked it for an inbound allowance.</summary>
	public WindowsFirewallGrantResult LastFirewallGrant { get; private set; }

	/// <summary>The UDP port this lobby listens on, which is the port a router and the local firewall must let through.</summary>
	public int LobbyPort { get; private set; }

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
	/// <summary>The port a lobby listens on when nothing asked for another one, and the port a direct connect assumes.</summary>
	public const int DefaultGamePort = 8999;
	private const int StunPort = 19302;

	public int ENetPort { get; private set; } = DefaultGamePort;

	/// <summary>
	///     The player slots the lobby's map states in its metadata, which is the shape a match started from this screen is
	///     seated with. The lobby itself is sized by the slots it leaves open, which its own rows show.
	/// </summary>
	public int MaxPlayers { get; private set; } = MapInfoMetadata.DefaultPlayablePlayerSlots;

	/// <summary>Every player slot the lobby carries, which is the number of rows its screen draws.</summary>
	public int SlotCount { get; private set; } = LobbyLimits.MaximumSeats;

	/// <summary>How many player slots the lobby leaves open to a player.</summary>
	public int OpenSlotCount { get; private set; } = LobbyLimits.MaximumSeats;

	/// <summary>Whether one player slot accepts a player, as the lobby last published it.</summary>
	/// <param name="slot">The player slot.</param>
	/// <returns>Whether the slot is open.</returns>
	public bool IsSlotOpen(int slot) => slot > 0 && slot < _openSlots.Length && _openSlots[slot];

	/// <summary>
	///     Answers whether the slot holds nobody and accepts a player, which is where a joining peer or a new computer
	///     seat is placed.
	/// </summary>
	/// <param name="slot">The player slot.</param>
	/// <returns>Whether the slot is open and free.</returns>
	public bool IsSlotFree(int slot) => IsSlotOpen(slot) && !PlayerList.Exists(player => player.Slot == slot);

	/// <summary>How many player slots are open and hold nobody.</summary>
	public int FreeSlotCount
	{
		get
		{
			int count = 0;
			for (int slot = 1; slot < _openSlots.Length; slot++)
			{
				if (IsSlotFree(slot)) count++;
			}

			return count;
		}
	}

	private readonly bool[] _openSlots = new bool[Realm.Ecs.EcsSharedMemory.MAX_PLAYERS];

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

	/// <summary>
	///     Whether this screen's lobby is reachable only by the players it gives its address to, rather than being listed on
	///     a registry. A direct lobby is opened and administered exactly as a listed one is; the difference is that no lobby
	///     server knows it exists.
	/// </summary>
	public bool IsDirectLobby { get; private set; }

	/// <summary>
	///     The address a player on this network joins this screen's lobby with, in the `address:port` form every player
	///     pastes into the direct connect dialog. It is the address a host sends to the people they want to play with.
	/// </summary>
	public string DirectConnectAddress => $"{GetLocalIPAddress()}:{(LobbyPort > 0 ? LobbyPort : ENetPort)}";

	/// <summary>
	///     The address this lobby was confirmed to be reachable at from outside this network, or nothing while nothing
	///     confirmed it. What confirms it is something outside acting on it: a registry node reached the game port through it,
	///     or this network's router accepted a forward and serves the same address the measurement saw. A public address this
	///     machine merely measured for itself is a guess, because a router reports the same mapping whether or not its outside
	///     face forwards anything, and it is offered separately as <see cref="MeasuredPublicConnectAddress" /> so a host can
	///     read it without being told that it works.
	/// </summary>
	public string? PublicConnectAddress => RouterReachability == RouterReachability.Confirmed ? MeasuredPublicConnectAddress : null;

	/// <summary>
	///     Why the address outside this network is, or is not, offered to the players this lobby is shared with. The lobby room
	///     tells its host, so a failed check names the thing to change rather than only the address to read.
	/// </summary>
	public RouterReachability RouterReachability => _routerReachability;

	/// <summary>
	///     The address this machine measured for its own lobby with a STUN query, or nothing while the measurement failed or
	///     named the address this machine already holds. It is where the router maps the lobby's port when a forward exists at
	///     all, and nothing more than that: nothing outside has reached the port through it yet.
	/// </summary>
	public string? MeasuredPublicConnectAddress
	{
		get
		{
			if (string.IsNullOrEmpty(_hostPublicIp)) return null;
			if (_hostPublicIp is "127.0.0.1" or "0.0.0.0") return null;
			if (string.Equals(_hostPublicIp, GetLocalIPAddress(), StringComparison.Ordinal)) return null;

			return DirectConnectHistory.DescribeAddress(_hostPublicIp, PublicPort);
		}
	}

	/// <summary>
	///     The address this screen joined the lobby at, or nothing while it hosts. A player who reached a lobby this way can
	///     pass the address on, which is what a lobby that is not listed depends on.
	/// </summary>
	public string? JoinedLobbyAddress => !IsHost && _connectedHostIp is { Length: > 0 } host ? DirectConnectHistory.DescribeAddress(host, BoundPort) : null;

	/// <summary>
	///     The message a host pastes into a chat to bring players into the lobby they opened, or nothing while this screen
	///     is not hosting. It names the map being played and the address those players join at, and says where that address
	///     goes in the game, because an address sent on its own leaves a friend guessing what to do with it. The address is
	///     the one confirmed to be reachable from outside this network, because a friend in a chat is usually not on this
	///     one, and it falls back to this network's own address when nothing confirmed anything, which is the only address
	///     that is certainly right.
	/// </summary>
	public string DirectConnectInviteMessage
	{
		get
		{
			if (!IsHost) return string.Empty;

			string mapName = !string.IsNullOrEmpty(HostedMapDisplayName) ? HostedMapDisplayName : ActiveMapName;
			string address = PublicConnectAddress ?? DirectConnectAddress;

			return string.Join(
				"\n",
				string.Format(TranslationServer.Translate("Join my Realm lobby: {0}"), mapName),
				string.Format(TranslationServer.Translate("Address: {0}"), address),
				TranslationServer.Translate("In Realm, choose PLAY, then Join Private Lobby via IP Address, and paste the address."));
		}
	}
	public string? ActiveLobbyId { get; private set; }
	public bool IsGameStarted { get; set; }
	public DateTime? GameSessionStartTime { get; private set; }
	public int MatchSeed { get; private set; }
	public string ActiveMapName { get; set; } = null!;
	public string ActiveMapVersion { get; set; } = "1.0.0";
	public bool SpectatorDelay { get; set; } = false;

	/// <summary>
	///     Whether the match groups each team's players into consecutive player slots. It starts at the value the lobby's
	///     map states and follows the host from the moment the host changes it; every seat reads it to show the setting.
	/// </summary>
	public bool TeamsTogether { get; set; } = false;

	/// <summary>
	///     The shroud this lobby will play, as the lobby published it: the host's own choice, or the mode the map's lobby
	///     script states, or <see cref="MatchShroudMode.FromMap" /> while neither has said anything.
	/// </summary>
	public MatchShroudMode ShroudMode { get; private set; } = MatchShroudMode.FromMap;

	/// <summary>
	///     The shroud the next match will actually play. A lobby that states no mode falls back to the mode the board's
	///     properties state, which is what a map that never runs a lobby script plays.
	/// </summary>
	/// <summary>
	///     The display name the lobby waiting to be published was opened with, which is what the registry lists it as.
	/// </summary>
	public string HostedMapDisplayName { get; private set; } = string.Empty;

	/// <summary>The map version the lobby waiting to be published plays.</summary>
	public string HostedMapVersion { get; private set; } = string.Empty;

	public MatchShroudMode EffectiveShroudMode =>
		ShroudMode != MatchShroudMode.FromMap
			? ShroudMode
			: MapPropertiesLoader.ReadMapShroudMode(ActiveMapName);

	/// <summary>
	///     The match settings this lobby's map declared, as the lobby last published them. Every seat shows the same list;
	///     only the host may change one, and the map that declared them reads the values.
	/// </summary>
	public List<LobbyMatchSetting> MatchSettings { get; } = new();

	public event Action? MatchSettingsChanged;
	public string? LobbyJoinError { get; set; }
	public string HostStability { get; set; } = "Excellent";
	public event Action<string>? HostStabilityUpdated;

	public List<PlayerInfo> PlayerList { get; } = new();

	/// <summary>
	///     The colour the lobby's map keeps for each player index, indexed by player index and packed as 0xRRGGBBAA, zero
	///     for an index that keeps the colour its seat states. It names the colours of the player indexes nobody holds -
	///     the players a map drives itself - which a seat list has nowhere to carry.
	/// </summary>
	public uint[] ColorOverlays { get; private set; } = new uint[LobbyLimits.MaximumSeats + 1];

	/// <summary>
	///     The colour each player index's units are drawn in on a minimap that paints every player in its own colour, as the
	///     lobby's map stated it, zero for an index it stated none for.
	/// </summary>
	public uint[] MinimapPlayerColors { get; private set; } = new uint[LobbyLimits.MaximumSeats + 1];

	/// <summary>
	///     The colour each player index's units are drawn in on a minimap that paints players by their relation to the screen
	///     looking at it, as the lobby's map stated it, zero for an index it stated none for.
	/// </summary>
	public uint[] MinimapTeamColors { get; private set; } = new uint[LobbyLimits.MaximumSeats + 1];

	/// <summary>
	///     The player index each player index stands in for, as the lobby's map stated it, zero for an index that stands in
	///     for nobody. The minimap that paints players by their relation to the screen looking at it draws a standing-in
	///     index as the player it stands in for.
	/// </summary>
	public int[] ProxyOwners { get; private set; } = new int[LobbyLimits.MaximumSeats + 1];

	/// <summary>Reads one entry of a published colour array, answering zero for an index the array does not carry.</summary>
	/// <param name="colors">The published array.</param>
	/// <param name="playerIndex">The player index.</param>
	/// <returns>The colour, packed as 0xRRGGBBAA, or zero.</returns>
	public static uint ColorAt(uint[]? colors, int playerIndex)
	{
		if (colors == null || playerIndex < 0 || playerIndex >= colors.Length) return 0u;
		return colors[playerIndex];
	}

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
	private RouterReachability _routerReachability = RouterReachability.NoRouterForward;
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
	public event Action<bool>? TeamsTogetherChanged;
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

/// <summary>
///     Why the address players outside this network would join at is, or is not, offered to them. Each state names a thing
///     the host can do something about, rather than the check that produced it, because this is what the lobby room tells
///     the host when an address it expected is not there to send.
/// </summary>
public enum RouterReachability
{
	/// <summary>No forward exists on this network's router, so nothing outside has reached the port and the port is what is missing.</summary>
	NoRouterForward,

	/// <summary>A forward exists, but the router stated no outside address of its own, so nothing showed that the port is reachable from outside.</summary>
	RouterAddressUnreadable,

	/// <summary>
	///     A forward exists, but the router's own outside address is not the one this network measured, so another network
	///     sits in front of it and that forward reaches nobody outside. Opening the port on this router cannot help.
	/// </summary>
	BehindAnotherNetwork,

	/// <summary>A forward serves the address this network measured, which is the address players outside join at.</summary>
	Confirmed
}