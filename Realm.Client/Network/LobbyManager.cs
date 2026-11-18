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
using Steamworks;
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

	/// <summary>
	///     The class this lobby was listed with: reachable without a punch, reachable only after one, or not shown to have
	///     been reachable at all. It is what the lobby room, the diagnostics block and the advice a host reads follow from.
	/// </summary>
	public string ReachabilityClass { get; private set; } = ReachabilityClasses.Unverified;

	/// <summary>
	///     Whether this lobby's router reused one outside port for the destinations that were measured, which is what decides
	///     whether forwarding a port is advice worth giving or an impossibility to state.
	/// </summary>
	public string MappingBehaviour { get; private set; } = MappingBehaviours.Unknown;

	/// <summary>
	///     The last thing a player was told when a join into this lobby failed, as the lobby's host was told it. It is kept
	///     for the diagnostics a host copies: a failure that reached this host's chat is the one piece of evidence about the
	///     lobby's network that its own measurements cannot produce. It describes the lobby this screen is hosting now, so it
	///     is cleared when a lobby is prepared rather than carried into the next one.
	/// </summary>
	public string? LastPlayerJoinFailure { get; private set; }

	public bool IsHost { get; set; }

	/// <summary>
	///     Whether this screen's lobby is kept out of the lobby browser, either because its host asked for that or because no
	///     registry could be made to list it. An unlisted lobby is opened and administered exactly as a listed one is: it
	///     keeps its seat, its host channel and the players it invites, and only stops being offered to players browsing.
	/// </summary>
	public bool IsUnlisted { get; private set; }

	/// <summary>
	///     The address a player on this network joins this screen's lobby with, in the `address:port` form every player
	///     pastes into the direct connect dialog. It is the address a host sends to the people they want to play with, and
	///     it is this network's IPv4 address whenever this machine has one, because that is the address the players in the
	///     same room as the host dial; a machine that holds only IPv6 states its IPv6 address, which is the only one it has.
	/// </summary>
	public string DirectConnectAddress => LobbyAddresses.Describe(BestLocalConnectHost(), LobbyPort > 0 ? LobbyPort : ENetPort);

	/// <summary>
	///     The address players whose network gives them IPv6 reach this lobby at, or nothing when this machine holds no
	///     advertisable IPv6 address. It is offered beside the address above rather than instead of it: a dual-stack host
	///     has one player to invite on their own network and another anywhere on IPv6, and handing either of them the other
	///     player's address is how an invite stops working. It needs no router forward, which is why it is worth showing
	///     even when the IPv4 route needs one that a host cannot make.
	/// </summary>
	public string? Ipv6ConnectAddress
	{
		get
		{
			IPAddress? best = LocalEndpointDiscovery.BestIPv6Address();
			return best == null ? null : LobbyAddresses.Describe(best.ToString(), LobbyPort > 0 ? LobbyPort : ENetPort);
		}
	}

	/// <summary>
	///     The best address this machine holds for another player to join this lobby at, or the IPv4 address this machine
	///     has always advertised when it holds nothing else.
	/// </summary>
	/// <returns>The address to state, without a port.</returns>
	private static string BestLocalConnectHost()
	{
		LobbyEndpointCandidate? best = LocalEndpointDiscovery.Best(LobbyAddressFamily.IPv4) ?? LocalEndpointDiscovery.Best(LobbyAddressFamily.IPv6);
		return best?.Address.ToString() ?? GetLocalIPAddress();
	}

	/// <summary>
	///     The endpoints a registry node verified for the lobby this screen last published, one per family it is reachable
	///     in. It is empty until a listed lobby is registered, and it is what the lobby room reads to say which families
	///     the lobby is offered over.
	/// </summary>
	public IReadOnlyList<LobbyEndpointInfo> PublishedEndpoints => _publishedEndpoints;

	private List<LobbyEndpointInfo> _publishedEndpoints = new();

	/// <summary>
	///     The family this screen connected to the lobby it is in over, which is stated in the lobby room and in the log so
	///     a dual-stack path can be told apart from an IPv4 one without reading a packet capture.
	/// </summary>
	public LobbyAddressFamily JoinedLobbyFamily { get; private set; } = LobbyAddressFamily.Unknown;

	/// <summary>
	///     Whether this screen accepts an endpoint inside its own network as publishable, which is set only by a development
	///     harness that puts the lobby server on the machine it is hosting from. A node that serves players states the same
	///     thing about itself in its capabilities (<see cref="Realm.Shared.Reachability.RegistryCapabilities.AllowsLocalCallers" />),
	///     which is the path a real host takes; this one exists because a node built before that statement existed cannot
	///     make it, and the old-node rig has to publish through exactly such a node.
	/// </summary>
	/// </summary>
	public bool AllowNonPublicEndpointForDevelopment { get; set; }

	/// <summary>
	///     Whether this machine holds a global IPv6 address, which is the only thing that lets it reach a lobby published
	///     over IPv6. It is read when a lobby is joined and never on a match tick.
	/// </summary>
	public static bool LocalIpv6Available => LocalEndpointDiscovery.HasGlobalIpv6();

	/// <summary>
	///     The address this lobby was confirmed to be reachable at from outside this network, or nothing while nothing
	///     confirmed it. What confirms it is something outside acting on it: a registry node reached the game port through it,
	///     or this network's router accepted a forward and serves the same address the measurement saw. A public address this
	///     machine merely measured for itself is a guess, because a router reports the same mapping whether or not its outside
	///     face forwards anything, and it is offered separately as <see cref="MeasuredPublicConnectAddress" /> so a host can
	///     read it without being told that it works.
	/// </summary>
	public string? PublicConnectAddress => RouterReachability is RouterReachability.Confirmed or RouterReachability.ConfirmedFromOutside ? MeasuredPublicConnectAddress : null;

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

			// The address is stated with the port whether or not the port is the one a direct connect would assume: a message
			// a player can act on is one that says where to go, and the rest of it is what to do about the one thing that
			// can still stop them getting in. The class decides that sentence, because a lobby a router was measured to
			// forward for needs no advice and one that only opens on a punch does.
			string fallback = ReachabilityClass == ReachabilityClasses.Open
				? TranslationServer.Translate("A lobby server reached this address from outside without anything sent first, so a join should need nothing else.")
				: string.Format(TranslationServer.Translate("If you cannot join, the host should try enabling UPnP on their router or forwarding UDP port {0} to their machine."), LobbyPort > 0 ? LobbyPort : DefaultGamePort);

			return string.Join(
				"\n",
				string.Format(TranslationServer.Translate("Join my Realm lobby: {0}"), mapName),
				string.Format(TranslationServer.Translate("Address: {0}"), address),
				TranslationServer.Translate("In Realm, choose PLAY, then Join Private Lobby via IP Address, and paste the address."),
				fallback);
		}
	}
	public string? ActiveLobbyId { get; private set; }

	/// <summary>
	///     The Steam lobby that stands for this screen's lobby, or zero when it is not in one. It is the directory entry, the
	///     membership list and the invite target, and the Realm lobby id a screen joins is this number as a string.
	/// </summary>
	public ulong SteamLobbyId { get; private set; }

	/// <summary>
	///     The optional code a host set so a community event can be gated without a password service. It is never written to
	///     Steam metadata, it is not a credential, and the authority is what compares it before seating anybody.
	/// </summary>
	public string JoinCode { get; set; } = string.Empty;

	/// <summary>Who may see and enter the lobby the host opens: public, friends-only, private or invisible.</summary>
	public ELobbyType HostedVisibility { get; set; } = ELobbyType.k_ELobbyTypePublic;
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

	/// <summary>
	///     Whether this screen is holding a lobby: seated in one, whether it administers it or joined it. A screen that gave
	///     its lobby up holds nothing, and everything the lobby measured is then only what was copied out of it beforehand.
	/// </summary>
	public bool HoldsLobby => _lobbySession != null;

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
	private int _hostPublicPortMeasuredForLocalPort;

	/// <summary>
	///     Puts one line in front of the host without pretending a player said it. It is the only status surface the lobby
	///     room already has, and the room is the one place a host is looking while a lobby is being published in the
	///     background, so a listing that fails is reported where the address is shown rather than in a dialog that is gone.
	/// </summary>
	/// <param name="message">The already-translated sentence to show.</param>
	private void NotifyHostLine(string message)
	{
		if (string.IsNullOrWhiteSpace(message)) return;

		ChatReceived?.Invoke(TranslationServer.Translate("System"), message, false, DateTime.UtcNow.Ticks);
	}
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
	///     Raised when a lobby this screen hosts could not be shown to be reachable, so it stays open to the players it is
	///     given the address to instead of being listed. It is the moment the room's connectivity summary has something to
	///     state: the host is already in the lobby room, the lobby is still joinable by address, and the thing to change is on
	///     this machine or its router.
	/// </summary>
	public event Action? LobbyPublicationRefused;

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
	///     A forward exists and the router stated an outside address, but this machine measured no outside address of its
	///     own, so there is nothing to compare the router's with. It is not a second network in front of the router as far as
	///     anything here can tell: the missing half is the measurement, not a forward.
	/// </summary>
	PublicAddressUnmeasured,

	/// <summary>
	///     A forward exists, but the router's own outside address is not the one this network measured, so another network
	///     sits in front of it and that forward reaches nobody outside. Opening the port on this router cannot help.
	/// </summary>
	BehindAnotherNetwork,

	/// <summary>A forward serves the address this network measured, which is the address players outside join at.</summary>
	Confirmed,

	/// <summary>
	///     A lobby server reached this game port from outside, which is what makes the lobby joinable. It is not a statement
	///     about a forward: a lobby can be reached through a punch with no forward on this network's router at all, so it is
	///     stated apart from <see cref="Confirmed" /> and a page read by a person never reports a forward nobody made.
	/// </summary>
	ConfirmedFromOutside
}