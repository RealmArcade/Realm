using System.Numerics;
using System.Security.Cryptography;
using MemoryPack;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Players;
using Realm.Shared.Services;
using Realm.Shared.Simulation;
using Realm.Ecs;

namespace Realm.ListenServer;

public partial class ServerLobby : ILobbySeatTable
	{
	private readonly Action<int, NetworkFrameKind, byte[], FrameDelivery> _send;

	private readonly Action<int> _disconnect;

	private readonly Func<IReadOnlyList<MatchRosterSeat>, IReadOnlyList<(int PeerId, int Slot)>, IReadOnlyDictionary<string, int>, int, string?> _startMatch;

	private readonly Action _lobbyAbandoned;

	private readonly Action<string, int> _punchAddress;

	private readonly Func<int, PeerLinkStatistics?> _readPeerLink;

	/// <summary>
	///     Asks the lobby's map whether the host must read a warning before the match starts. It answers the text the host
	///     has to accept, or nothing when the map has nothing to warn about.
	/// </summary>
	private readonly Func<string?> _readStartWarning;

	private readonly ServerLog _log;
	private readonly string _mapFolder;
	private readonly string _mapName;
	private readonly string _mapVersion;
	private readonly int _mapMaximumPlayers;
	private readonly MatchOptionSet _matchOptions = new();
	private int _maximumPlayers;

	private readonly SortedDictionary<int, LobbySeat> _seats = new();
	private readonly Dictionary<int, int> _slotByPeer = new();
	private readonly List<LobbyChatMessage> _chatHistory = new();

	/// <summary>
	///     Sessions this lobby removed by name. A connection that drops because it was kicked spends no time deciding to
	///     come back, so a kicked client reconnects before it has read the reason; that reconnect is answered with the
	///     refusal instead of a fresh seat.
	/// </summary>
	private readonly HashSet<string> _refusedSessionTokens = new(StringComparer.Ordinal);

	private const int MaximumChatHistory = 200;

	/// <summary>How often the lobby republishes the link quality it measures for each seat.</summary>
	private const double LinkStatisticsIntervalSeconds = 1.0;

	private double _linkStatisticsTimer;

	private const int MaximumSessionTokenLength = 64;

	private int _adminPeerId;
	private long _revision;
	private int _nextComputerNumber = 1;
	private bool _countdownActive;
	private double _countdownRemainingSeconds;
	private int _lastCountdownSeconds;
	private bool _matchStarted;

	public ServerLobby(ServerArguments arguments, ServerLog log, Action<int, NetworkFrameKind, byte[], FrameDelivery> send, Action<int> disconnect,
		Func<IReadOnlyList<MatchRosterSeat>, IReadOnlyList<(int PeerId, int Slot)>, IReadOnlyDictionary<string, int>, int, string?> startMatch, Action lobbyAbandoned, Action<string, int> punchAddress,
		Func<int, PeerLinkStatistics?> readPeerLink, Func<string?> readStartWarning)
	{
		_log = log;
		_send = send;
		_disconnect = disconnect;
		_startMatch = startMatch;
		_lobbyAbandoned = lobbyAbandoned;
		_punchAddress = punchAddress;
		_readPeerLink = readPeerLink;
		_readStartWarning = readStartWarning;
		_mapFolder = arguments.MapPath;
		_mapName = ResolveMapName(arguments);
		_mapVersion = ResolveMapVersion(arguments);
		_mapMaximumPlayers = ResolveMaximumPlayers(arguments);
		_maximumPlayers = _mapMaximumPlayers;
	}

	public bool MatchStarted => _matchStarted;

	public int AdminPeerId => _adminPeerId;

	public string MapName => _mapName;

	public string MapVersion => _mapVersion;

	public int MaximumPlayers => _maximumPlayers;

	/// <summary>
	///     The parameters the host configured for the match this lobby starts. They are read once, when the match is put on
	///     the authority, so a change the host makes after the countdown has begun does not reach the match already built.
	/// </summary>
	public MatchOptionSet MatchOptions => _matchOptions;

	/// <summary>
	///     The shroud mode this lobby will play: the mode its host chose, the mode its map's lobby script states, or
	///     <see cref="MatchShroudMode.FromMap" /> while neither has said anything. The lobby publishes it, so a peer shows
	///     the mode the match will play rather than the mode the board's properties alone describe.
	/// </summary>
	public MatchShroudMode EffectiveShroudMode
	{
		get
		{
			MatchShroudMode chosen = _matchOptions.GetShroudMode();
			if (chosen != MatchShroudMode.FromMap) return chosen;
			return Roster?.ShroudMode ?? MatchShroudMode.FromMap;
		}
	}

	/// <summary>
	///     Sets one parameter the host configures for the match. Every value the option range refuses is dropped rather than
	///     clamped, so a host is never shown a setting the match would not play.
	/// </summary>
	/// <param name="option">The option being configured.</param>
	/// <param name="value">The value to play, where zero returns the option to the map's own default.</param>
	/// <returns>Whether the lobby now holds that setting.</returns>
	public bool TrySetMatchOption(MatchOption option, int value)
	{
		if (option == MatchOption.MaximumPlayers)
		{
			if (!_matchOptions.Set(option, value)) return false;

			int players = value == 0 ? _mapMaximumPlayers : value;
			if (!TrySetMaximumPlayers(players))
			{
				_matchOptions.Set(option, 0);
				return false;
			}

			BroadcastState();
			return true;
		}

		if (!_matchOptions.Set(option, value)) return false;

		_log.Write($"the lobby's host set {option} to {(value == 0 ? "the map's default" : value.ToString(System.Globalization.CultureInfo.InvariantCulture))}");
		BroadcastState();
		return true;
	}

	/// <summary>
	///     How many seats are playing rather than watching. A spectator holds no player seat, so the players a lobby seats
	///     is a count of these and never of the seats it holds.
	/// </summary>
	public int PlayingSeatCount
	{
		get
		{
			int count = 0;
			foreach (LobbySeat seat in _seats.Values)
			{
				if (seat.Team >= 1) count++;
			}

			return count;
		}
	}

	/// <summary>
	///     The roster rules this lobby's map states, or nothing while the lobby runs on the authority's own rules. The
	///     roster refuses a change to a field it locked, so every way a seat can change is filtered through it.
	/// </summary>
	public ServerLobbyRoster? Roster { get; set; }

	public IReadOnlyCollection<LobbySeat> Seats => _seats.Values;

	public bool TryGetSeatBySlot(int slot, out LobbySeat seat) => _seats.TryGetValue(slot, out seat);

	public string HostStability { get; private set; } = "Excellent";
}
