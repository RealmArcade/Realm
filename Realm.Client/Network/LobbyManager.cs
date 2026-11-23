using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
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
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Metadata;
using Realm.Shared.Network;
using Realm.Shared.Simulation;
using Realm.Shared.Services;
using Environment = System.Environment;
using FileAccess = System.IO.FileAccess;

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

	/// <summary>
	///     The loopback port the host's own screen reaches its authority on. Nothing outside this machine can reach it, and no
	///     screen publishes it: it exists so the host's seat has a socket to travel over like any other seat.
	/// </summary>
	public int LobbyPort { get; private set; }

	/// <summary>The factions the lobby's map offers, in the order its roster numbers them.</summary>
	public IReadOnlyList<string> LobbyFactions { get; private set; } = Array.Empty<string>();

	/// <summary>The number of teams the lobby offers, which is the highest team number a seat may hold.</summary>
	public int LobbyTeamCount { get; private set; } = LobbyLimits.DefaultTeamCount;

	/// <summary>The port a lobby's loopback socket asks for by name before the system is allowed to choose one.</summary>
	public const int DefaultGamePort = 8999;

	/// <summary>
	///     The port a hosted lobby asks for by name, or null once this session has learned that the named port is taken.
	///     A port held by another instance stays held for as long as that instance lives, so remembering the refusal is what
	///     keeps every later lobby from spending a whole start on a bind that cannot succeed.
	/// </summary>
	private int? _preferredLobbyPort = DefaultGamePort;

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

	public bool IsSignedIn => GameContext.Steam.IsAvailable;

	public ulong SteamId => GameContext.Steam.SteamId;

	public string AuthenticatedUsername => GameContext.Steam.UserName;

	/// <summary>
	///     The name a lobby seat carries, which is the player's own Steam persona name. The lobby appends the seat number
	///     when two peers turn out to carry the same name. A screen with no Steam has no persona to state, so it names
	///     itself the way a single-player match has always named the person at the keyboard rather than stating nothing.
	/// </summary>
	public string PlayerDisplayName
	{
		get
		{
			string personaName = GameContext.Steam.UserName;
			return string.IsNullOrEmpty(personaName) ? TranslationServer.Translate("Player") : personaName;
		}
	}

	public bool IsHost { get; set; }

	/// <summary>
	///     Whether this screen's lobby is kept out of the lobby browser because its host published it to friends or by
	///     invitation only. An unlisted lobby is opened and administered exactly as a listed one is: it keeps its seat, its
	///     host channel and the players it invites, and only stops being offered to players browsing.
	/// </summary>
	public bool IsUnlisted { get; private set; }

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

	/// <summary>
	///     Whether the lobby this screen is in gates its seats with a join code, which the room shows as a field for the
	///     players who arrived without it.
	/// </summary>
	public bool JoinCodeRequired { get; private set; }

	/// <summary>Whether this screen presented the lobby's join code and was given its seat.</summary>
	public bool JoinCodeSeatHeld { get; private set; }

	/// <summary>Raised when the lobby starts or stops asking for a join code, and with the value it now asks for.</summary>
	public event Action<bool>? JoinCodeRequirementChanged;

	/// <summary>
	///     Raised with what the player should be told after presenting a code: the refusal when it did not match, or an empty
	///     string when the seat was taken.
	/// </summary>
	public event Action<string>? JoinCodeRefused;

	/// <summary>Who may see and enter the lobby the host opens: public, friends-only, private or invisible.</summary>
	public ELobbyType HostedVisibility { get; set; } = ELobbyType.k_ELobbyTypePublic;

	/// <summary>The map name the current advertisement states, so a state that changes nothing does not rewrite it.</summary>
	private string _publishedAdvertisementMap = string.Empty;

	/// <summary>The seat count the current advertisement states.</summary>
	private int _publishedAdvertisementSeats = -1;

	/// <summary>The trust mark this lobby advertises for its map, resolved once when the lobby is published.</summary>
	private MapTrustState _publishedMapTrust = MapTrustState.Trusted;

	/// <summary>
	///     The Workshop item the lobby's map is published as, as the lobby found it. It is what the advertisement names only
	///     while the package Steam installed for it is the package this lobby plays; a joiner given an item is told to
	///     subscribe to it, and an absence is what selects the host-transfer path.
	/// </summary>
	private ulong _publishedMapWorkshopId;

	/// <summary>
	///     The private copy of the active map this session plays from, which is the installed package migrated to this build.
	///     Empty until a lobby is opened or joined, and left empty for a joiner whose map came over the lobby itself.
	/// </summary>
	public string ActiveMapSessionDirectory { get; set; } = string.Empty;

	/// <summary>
	///     Whether this screen holds the package the lobby plays, which is what ends the wait before a seat can say it is
	///     ready. A map taken from a Workshop item is prepared into a private copy and never enters the download archive, so
	///     a room that asked the archive alone would keep a joiner waiting for a download nobody was going to send.
	/// </summary>
	public bool HoldsActiveMapPackage
	{
		get
		{
			if (string.IsNullOrEmpty(ActiveMapName)) return false;

			string sessionDirectory = ActiveMapSessionDirectory;
			if (sessionDirectory.Length > 0 && Directory.Exists(sessionDirectory)) return true;

			return MapAssetManager.IsMapDownloaded(ActiveMapName);
		}
	}
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
	///     The display name the lobby waiting to be published was opened with, which is what Steam shows the lobby as.
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

	private ClientServerSession? _lobbySession;
	private bool _isConnectedToHost;

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
	private long _lobbyRevision;
	private bool _countdownActive;
	private int _countdownRemaining;
	private string? _countdownMapName;
	private TaskCompletionSource<string?>? _admissionTcs;
	private TaskCompletionSource<MapTransferReply>? _transferReplyTcs;
	private readonly Queue<MapTransferReply> _unreadTransferReplies = new();
	private string? _downloadingMapName;
	private readonly SemaphoreSlim _mapDownloadLock = new(1, 1);

	public event Action? PlayerListUpdated;
	public event Action<string, string, bool, long>? ChatReceived;
	public event Action<int, string>? ServerChatCommandReceived;
	public event Action<string>? ConnectionFailed;
	public event Action<string>? KickReceived;
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

	protected override bool IsMapTransferAllowed() => IsHost;

	protected override void OnDownloadProgressChanged(float progress) => CallDeferred(nameof(EmitDownloadProgress), progress);

	public override void _Ready()
	{
		ProcessPerformanceOptimizer.EnforceOptimalPerformance();
		GameContext.RegisterLobby(this);
		ProcessMode = ProcessModeEnum.Always;

		MapAssetManager.PruneGlobalArchive();
		GameContext.MapStorageService.ImportEmbeddedMaps();
		AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

		WarmUpLocalAuthority();


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

		// The authority is handed the Steam application this screen is running as, so Steam has to have answered before
		// that value is read. This runs while the screen is still starting and the Steam autoload has not said anything
		// yet, and the authority it would launch without an id is the one the whole session keeps: every host request
		// would then be refused for want of an identity. Asking here makes the order explicit rather than a property of
		// the autoload list, and Start states it is a no-op in a process Steam already answered in.
		GameContext.Steam.Start();

		GameContext.LocalAuthority.Configure(target, Path.Combine(OS.GetUserDataDir(), "logs"), OS.GetUserDataDir());
		_ = GameContext.LocalAuthority.EnsureStartedAsync();
	}

	/// <summary>
	///     Whether this screen was started as a member of a harness room rather than as a player's own session. No normal
	///     launch sets it; a harness sets it on the screens that join an authority another process already runs.
	/// </summary>
	private static bool HarnessJoinsExistingAuthority =>
		string.Equals(System.Environment.GetEnvironmentVariable("REALM_HARNESS_JOIN_ONLY"), "1", StringComparison.Ordinal);


}
