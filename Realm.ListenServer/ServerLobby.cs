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

	/// <summary>
	///     Sends one datagram from the lobby's game port to an address. The third argument names the reachability check the
	///     punch is for, or is empty when the punch only opens a path for a joining player.
	/// </summary>
	private readonly Action<string, int, string> _punchAddress;

	private readonly Action<string> _openRelayedPeer;

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

	/// <summary>Every player slot the lobby carries, where true means the slot accepts a player.</summary>
	private readonly bool[] _openSlots = new bool[EcsSharedMemory.MAX_PLAYERS];

	/// <summary>
	///     The colours the lobby's map keeps for player indexes, indexed by player index. A slot holding a seat takes the
	///     colour on its seat; an index nobody holds - a player the map drives itself - keeps its colour here so the match
	///     still draws it. Zero means the index keeps the colour its seat states.
	/// </summary>
	private readonly uint[] _colorOverrides = new uint[EcsSharedMemory.MAX_PLAYERS];

	/// <summary>The colours the map stated for the minimap's player-colour scheme, indexed by player index, zero for none.</summary>
	private readonly uint[] _minimapPlayerColors = new uint[EcsSharedMemory.MAX_PLAYERS];

	/// <summary>The colours the map stated for the minimap's team-colour scheme, indexed by player index, zero for none.</summary>
	private readonly uint[] _minimapTeamColors = new uint[EcsSharedMemory.MAX_PLAYERS];

	/// <summary>
	///     The player index each player index stands in for, indexed by player index, zero for an index that stands in for
	///     nobody. A player the map drives itself carries a real player's army, and the minimap that paints players by
	///     their relation to the screen reads it as the player it stands in for.
	/// </summary>
	private readonly int[] _proxyOwners = new int[EcsSharedMemory.MAX_PLAYERS];

	private readonly SortedDictionary<int, LobbyMatchSetting> _matchSettings = new();
	private int _openSlotCount;
	private MatchShroudMode _hostShroudMode = MatchShroudMode.FromMap;
	private bool _hostChoseSpectatorDelay;

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
		Func<IReadOnlyList<MatchRosterSeat>, IReadOnlyList<(int PeerId, int Slot)>, IReadOnlyDictionary<string, int>, int, string?> startMatch, Action lobbyAbandoned, Action<string, int, string> punchAddress,
		Func<int, PeerLinkStatistics?> readPeerLink, Func<string?> readStartWarning, Action<string> openRelayedPeer)
	{
		_log = log;
		_send = send;
		_disconnect = disconnect;
		_startMatch = startMatch;
		_lobbyAbandoned = lobbyAbandoned;
		_punchAddress = punchAddress;
		_openRelayedPeer = openRelayedPeer;
		_readPeerLink = readPeerLink;
		_readStartWarning = readStartWarning;
		_mapFolder = arguments.MapPath;
		_mapName = ResolveMapName(arguments);
		_mapVersion = ResolveMapVersion(arguments);

		// The core game starts every lobby with every slot it has open. A map that its board seats fewer players closes the
		// rest, and the host may close and reopen any of them from the lobby, which is the whole of how a lobby's size is
		// decided - there is no player count to set and no count to disagree with the rows on screen.
		for (int slot = 1; slot < _openSlots.Length; slot++) _openSlots[slot] = true;
		_openSlotCount = LobbyLimits.MaximumSeats;
	}

	public bool MatchStarted => _matchStarted;

	/// <summary>The highest player index a person may be seated in, which is every slot while the map stated no limit.</summary>
	public int SeatLimit { get; private set; } = EcsSharedMemory.MAX_PLAYERS - 1;

	/// <summary>
	///     Keeps every player index above a count for the lobby's map, which is how a map that drives players of its own
	///     stops a person being seated in one of them. The slots above the count are closed as well, so no admission, no
	///     computer seat and no host order can place anyone there.
	/// </summary>
	/// <param name="maximumPlayers">The highest player index a person may be seated in.</param>
	/// <returns>Whether the lobby holds that limit afterwards.</returns>
	public bool TrySetPlayerSlotLimit(int maximumPlayers)
	{
		if (maximumPlayers <= 0 || maximumPlayers >= EcsSharedMemory.MAX_PLAYERS) return false;
		if (maximumPlayers == SeatLimit) return true;

		SeatLimit = maximumPlayers;

		bool changed = false;
		for (int slot = 1; slot < _openSlots.Length; slot++)
		{
			if (slot <= maximumPlayers || !_openSlots[slot]) continue;

			_openSlots[slot] = false;
			changed = true;
		}

		_openSlotCount = CountOpenSlots();
		_log.Write($"the lobby's map reserves every player slot after {maximumPlayers} for itself");
		if (changed) BroadcastState();
		return true;
	}

	public int AdminPeerId => _adminPeerId;

	public string MapName => _mapName;

	public string MapVersion => _mapVersion;

	public int OpenSlotCount => _openSlotCount;

	/// <summary>
	///     Every player slot the lobby carries, which is the number of rows its screens draw. A slot the lobby's map keeps
	///     for its own players is not one of them: no person can be seated there, so a row offering it would be a row the
	///     lobby's own limits refuse.
	/// </summary>
	public int SlotCount => Math.Min(_openSlots.Length - 1, SeatLimit);

	/// <summary>Whether a player slot accepts a player.</summary>
	/// <param name="slot">The player slot.</param>
	/// <returns>Whether the slot is open.</returns>
	public bool IsSlotOpen(int slot) => slot > 0 && slot < _openSlots.Length && _openSlots[slot];

	/// <summary>
	///     Opens or closes one player slot, which is how the lobby's host decides which seats the board offers. A slot the
	///     host closes keeps its row on screen and stops accepting a player. A slot the lobby's map reserved for itself is
	///     refused, because the player indexes above the map's limit belong to the map's own rules.
	/// </summary>
	/// <param name="slot">The player slot.</param>
	/// <param name="open">Whether the slot accepts a player.</param>
	/// <returns>Whether the lobby offers that slot afterwards.</returns>
	public bool TrySetSlotOpen(int slot, bool open)
	{
		if (slot <= 0 || slot >= _openSlots.Length) return false;
		if (open && slot > SeatLimit)
		{
			_log.Write($"player slot {slot} was opened, but the lobby's map keeps every slot after {SeatLimit} for its own players; the order is refused");
			return false;
		}

		if (_openSlots[slot] == open) return true;

		_openSlots[slot] = open;
		_openSlotCount = CountOpenSlots();
		_log.Write($"the lobby's host {(open ? "opened" : "closed")} player slot {slot}");
		BroadcastState();
		return true;
	}

	/// <summary>
	///     Opens the first slots up to a count and closes the rest, which is how a map states the number of players its
	///     board seats. A slot somebody already holds is left as it is.
	/// </summary>
	/// <param name="count">How many slots, counted from the first, the lobby should offer.</param>
	/// <returns>Whether the lobby offers those slots afterwards.</returns>
	public bool TrySetOpenSlotCount(int count)
	{
		if (count <= 0 || count >= _openSlots.Length) return false;

		count = Math.Min(count, SeatLimit);
		bool changed = false;
		for (int slot = 1; slot < _openSlots.Length; slot++)
		{
			bool open = slot <= count && !_seats.ContainsKey(slot);
			if (_openSlots[slot] == open) continue;

			_openSlots[slot] = open;
			changed = true;
		}

		if (!changed) return true;

		_openSlotCount = CountOpenSlots();
		_log.Write($"the lobby's map offers the first {count} player slot(s)");
		BroadcastState();
		return true;
	}

	/// <summary>
	///     The match settings the lobby's map declared, keyed by the identifier the map named them under. They are read once,
	///     when the match is put on the authority, so a change the host makes after the countdown has begun does not reach
	///     the match already built.
	/// </summary>
	public IReadOnlyDictionary<int, LobbyMatchSetting> MatchSettings => _matchSettings;

	/// <summary>
	///     The values the host chose for the settings the map declared, which is what the match reads back through its own
	///     API and what the roster file carries to a match started without a lobby.
	/// </summary>
	public LobbySettingSet MatchSettingValues
	{
		get
		{
			var values = new LobbySettingSet();
			foreach (KeyValuePair<int, LobbyMatchSetting> pair in _matchSettings) values.Set(pair.Key, pair.Value.Value);
			return values;
		}
	}

	/// <summary>
	///     Sets one of the settings the lobby's map declared. A setting the map never declared, and a value outside the range
	///     the map stated, are both refused, so a host is never shown a setting the match would not play.
	/// </summary>
	/// <param name="settingId">The identifier the map declared the setting under.</param>
	/// <param name="value">The value to play.</param>
	/// <returns>Whether the lobby now holds that value.</returns>
	public bool TrySetLobbySetting(int settingId, int value)
	{
		if (!_matchSettings.TryGetValue(settingId, out LobbyMatchSetting? setting))
		{
			_log.Write($"the lobby's host set match setting {settingId}, which this lobby's map does not declare; the order is refused");
			return false;
		}

		int clamped = Math.Clamp(value, setting.Minimum, setting.Maximum);
		if (clamped != value)
		{
			_log.Write($"match setting {settingId} was set to {value}, outside the {setting.Minimum} to {setting.Maximum} its map states; the order is refused");
			return false;
		}

		if (setting.Value == value) return true;

		setting.Value = value;
		_log.Write($"the lobby's host set '{setting.Label}' to {value}");
		BroadcastState();
		return true;
	}

	/// <summary>Declares one match setting of the map's own, or restates one the lobby already holds.</summary>
	/// <param name="settingId">The identifier the map declares the setting under.</param>
	/// <param name="control">How the host edits the setting.</param>
	/// <param name="label">The label the host reads.</param>
	/// <param name="tooltip">The sentence that explains the setting, or empty for none.</param>
	/// <param name="defaultValue">The value the setting holds until the host changes it.</param>
	public void DeclareMatchSetting(int settingId, LobbySettingControl control, string label, string tooltip, int defaultValue)
	{
		if (settingId <= 0) return;

		if (_matchSettings.TryGetValue(settingId, out LobbyMatchSetting? existing))
		{
			existing.Label = label;
			existing.Tooltip = tooltip;
			existing.Control = control;
			return;
		}

		var setting = new LobbyMatchSetting
		{
			SettingId = settingId,
			Label = label,
			Tooltip = tooltip,
			Control = control,
			Value = control == LobbySettingControl.Toggle ? Math.Clamp(defaultValue, 0, 1) : defaultValue,
			Minimum = 0,
			Maximum = control == LobbySettingControl.Toggle ? 1 : defaultValue
		};

		_matchSettings[settingId] = setting;
		_log.Write($"the lobby's map declared the '{label}' match setting ({control}, default {setting.Value})");
		BroadcastState();
	}

	/// <summary>Adds one option to a setting the map declared as a choice, or shows a setting it already holds.</summary>
	/// <param name="settingId">The identifier the map declared the setting under.</param>
	/// <param name="optionLabel">The text the host reads in the dropdown.</param>
	public void DeclareMatchSettingOption(int settingId, string optionLabel)
	{
		if (!_matchSettings.TryGetValue(settingId, out LobbyMatchSetting? setting)) return;

		var options = new List<string>(setting.Options.Length + 1);
		options.AddRange(setting.Options);
		options.Add(optionLabel);

		setting.Options = options.ToArray();
		setting.Maximum = setting.Options.Length - 1;
		if (setting.Value > setting.Maximum) setting.Value = setting.Maximum;
		BroadcastState();
	}

	/// <summary>States the values a count setting the map declared may hold.</summary>
	/// <param name="settingId">The identifier the map declared the setting under.</param>
	/// <param name="minimum">The lowest value the host may choose.</param>
	/// <param name="maximum">The highest value the host may choose.</param>
	public void DeclareMatchSettingRange(int settingId, int minimum, int maximum)
	{
		if (!_matchSettings.TryGetValue(settingId, out LobbyMatchSetting? setting)) return;
		if (minimum < 0 || maximum < minimum) return;

		setting.Minimum = minimum;
		setting.Maximum = maximum;
		setting.Value = Math.Clamp(setting.Value, minimum, maximum);
		BroadcastState();
	}

	/// <summary>Reads the value one of the settings the lobby's map declared currently holds.</summary>
	/// <param name="settingId">The identifier the map declared the setting under.</param>
	/// <returns>The value the setting holds, or zero when the map declared no such setting.</returns>
	public int GetMatchSettingValue(int settingId)
	{
		return _matchSettings.TryGetValue(settingId, out LobbyMatchSetting? setting) ? setting.Value : 0;
	}

	/// <summary>
	///     The shroud mode this lobby will play: the mode its host chose, the mode its map's lobby script states, or
	///     <see cref="MatchShroudMode.FromMap" /> while neither has said anything. The lobby publishes it, so a peer shows
	///     the mode the match will play rather than the mode the board's properties alone describe.
	/// </summary>
	public MatchShroudMode EffectiveShroudMode
	{
		get
		{
			if (_hostShroudMode != MatchShroudMode.FromMap) return _hostShroudMode;
			return Roster?.ShroudMode ?? MatchShroudMode.FromMap;
		}
	}

	/// <summary>
	///     Takes the host's own choice of shroud mode, which overrules the default its map stated and holds for the rest of
	///     the lobby. Clearing it returns the lobby to the mode the map states.
	/// </summary>
	/// <param name="shroudMode">The mode the match should play.</param>
	/// <returns>Whether the lobby now holds that mode.</returns>
	public bool TrySetShroudMode(MatchShroudMode shroudMode)
	{
		if (!Enum.IsDefined(shroudMode)) return false;
		if (_hostShroudMode == shroudMode) return true;

		_hostShroudMode = shroudMode;
		_log.Write($"the lobby's host set the shroud mode to {shroudMode}");
		BroadcastState();
		return true;
	}

	private int CountOpenSlots()
	{
		int count = 0;
		for (int slot = 1; slot < _openSlots.Length; slot++)
		{
			if (_openSlots[slot]) count++;
		}

		return count;
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
