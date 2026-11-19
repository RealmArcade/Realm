using System.Globalization;
using System.Text;
using MemoryPack;

namespace Realm.Shared.Network;

public enum LobbyCommandKind : byte
{
	SetReady = 1,

	UpdateSeat = 2,

	AddComputer = 3,

	Kick = 4,

	StartCountdown = 5,

	CancelCountdown = 6,

	SetSpectatorDelay = 7,

	ReportMapReady = 8,

	Chat = 9,

	Leave = 10,

	ReportHostStability = 11,

	RequestChatHistory = 13,

	ReportLoadReady = 14,

	ReportMapProgress = 15,

	SetComputerDifficulty = 16,

	/// <summary>Sets one match setting the lobby's map declared, named by <see cref="LobbyCommand.SettingId" />.</summary>
	SetLobbySetting = 17,

	/// <summary>Sets whether the match groups each team's players into consecutive player slots, in <see cref="LobbyCommand.Flag" />.</summary>
	SetTeamsTogether = 18,

	/// <summary>Opens or closes one player slot, named by <see cref="LobbyCommand.Slot" /> and opened when <see cref="LobbyCommand.Flag" /> is set.</summary>
	SetSlotOpen = 19,

	/// <summary>Chooses the shroud mode the match plays, in <see cref="LobbyCommand.Progress" />.</summary>
	SetShroudMode = 20,


	/// <summary>
	///     Presents, in <see cref="LobbyCommand.Text" />, the join code the player typed. A lobby that set one seats nobody
	///     until it matches, and a member who has not presented it stands in the room without a seat. It is compared on the
	///     authority and never leaves it, and a wrong code is answered rather than punished.
	/// </summary>
	PresentJoinCode = 23,

	/// <summary>
	///     Sets the join code the lobby gates its seats with, in <see cref="LobbyCommand.Text" />, or clears it when the text is
	///     empty. Sent by the lobby's administrator only. Clearing it releases every member who was waiting for it, which is
	///     why the change is announced to the room as normal lobby state.
	/// </summary>
	SetJoinCode = 24
}

/// <summary>
///     How accurately a lobby's computer seat follows the trained bot profile it plays. The lobby's host picks one per
///     computer seat, and the authority turns it into the handicap that seat plays with.
/// </summary>
public enum LobbyBotDifficulty : byte
{
	Easy = 1,
	Medium = 2,
	Hard = 3
}

/// <summary>
///     The handicap each difficulty places on a computer seat. A mistake is a single decision the bot throws away and
///     replaces with a uniformly random choice from the actions it was offered, so <see cref="LobbyBotDifficulty.Hard" />
///     plays the trained profile exactly as it was trained.
/// </summary>
public static class LobbyBotDifficultyRules
{
	public const LobbyBotDifficulty Default = LobbyBotDifficulty.Hard;

	public const float EasyMistakeChance = 0.25f;
	public const float MediumMistakeChance = 0.10f;
	public const float HardMistakeChance = 0.0f;

	/// <summary>
	///     Resolves a difficulty as it arrived over the wire into the one of the three the game plays.
	/// </summary>
	/// <param name="value">The difficulty the sender stated, which may be outside the three known values.</param>
	/// <returns>The nearest known difficulty.</returns>
	public static LobbyBotDifficulty Clamp(int value)
	{
		if (value <= (int)LobbyBotDifficulty.Easy) return LobbyBotDifficulty.Easy;
		if (value >= (int)LobbyBotDifficulty.Hard) return LobbyBotDifficulty.Hard;
		return (LobbyBotDifficulty)value;
	}

	/// <summary>
	///     Gets the fraction of a seat's decisions that difficulty replaces with a random choice.
	/// </summary>
	/// <param name="difficulty">The difficulty the seat plays on.</param>
	/// <returns>A value between zero and one; zero is the profile played exactly as trained.</returns>
	public static float MistakeChance(LobbyBotDifficulty difficulty)
	{
		return difficulty switch
		{
			LobbyBotDifficulty.Easy => EasyMistakeChance,
			LobbyBotDifficulty.Medium => MediumMistakeChance,
			_ => HardMistakeChance
		};
	}
}

public enum MapTransferRequestKind : byte
{
	Manifest = 1,
	Chunk = 2
}

public enum MapTransferReplyKind : byte
{
	Manifest = 1,
	Chunk = 2,
	Refused = 3
}

[MemoryPackable]
public partial struct LobbySeat
{
	public int PeerId;

	public int Slot;

	public string Name;
	public string Faction;

	public string TeamName;

	public int Team;

	public uint ColorRgba;

	public bool IsHost;

	public bool IsComputer;

	public bool IsReady;

	public bool IsMapReady;

	public bool IsLoadReady;

	public bool IsDisconnected;

	public int LatencyMilliseconds;

	public int JitterMilliseconds;

	public float PacketLossPercent;

	public int MapDownloadPercent;

	/// <summary>
	///     The <see cref="LobbyBotDifficulty" /> this computer seat plays on, or zero for a seat no one seated as a computer.
	/// </summary>
	public int Difficulty;

	/// <summary>
	///     The SteamID64 the seat's own client plays as, or 0 for a computer seat, for a screen running without Steam, or for
	///     a seat whose player has not sent a hello this build can read. It is stated by that client, exactly like the name it
	///     plays under, and it is only used to point Steam's add-friend dialog at the right person.
	/// </summary>
	public ulong SteamId;
}

[MemoryPackable]
public partial struct LobbyStateMessage
{
	public LobbySeat[] Seats;
	public string MapName;
	public string MapVersion;

	/// <summary>How many player slots the lobby carries, which is every row its screens draw.</summary>
	public int SlotCount;

	/// <summary>
	///     Which player slots the host left open, indexed by slot. A closed slot holds no player and accepts none, and a
	///     map that stated no slot closed leaves every slot open.
	/// </summary>
	public bool[] OpenSlots;

	public string[] Factions;
	public int TeamCount;
	public bool SpectatorDelay;
	public string HostStability;
	public bool CountdownActive;
	public int CountdownRemainingSeconds;

	/// <summary>
	///     The match settings this lobby's map declared, which every seat reads to show them and only the host may change.
	///     A map that declared none leaves the list empty, and the lobby then offers the core settings alone.
	/// </summary>
	public LobbyMatchSettingMessage[] Settings;

	public long Revision;

	/// <summary>
	///     Whether the match groups each team's players into consecutive player slots, which every seat reads to show the
	///     setting and only the host may change. It is last so the seats a peer already reads keep their wire position.
	/// </summary>
	public bool TeamsTogether;

	/// <summary>
	///     The <see cref="Realm.Ecs.MatchShroudMode" /> this lobby will play: the host's own choice when it made one, the
	///     mode the map's lobby script states otherwise, and <c>FromMap</c> when neither says anything. It is last so the
	///     fields a peer already reads keep their wire position.
	/// </summary>
	public int ShroudMode;

	/// <summary>
	///     Whether this lobby gates its seats with a join code, which every member reads to show the field the players who
	///     arrived without it have to fill in. The code itself is never published: the authority holds it for the lobby's
	///     lifetime and compares what a screen presents.
	/// </summary>
	public bool JoinCodeRequired;

	/// <summary>
	///     The colour each player index's units are drawn in, indexed by player index, packed as 0xRRGGBBAA, where zero
	///     means the index keeps the colour its seat states. It carries the colours a map keeps for the player indexes
	///     nobody holds - the players such a map drives itself - which a seat list has nowhere to name.
	/// </summary>
	public uint[] ColorOverlays;

	/// <summary>
	///     The colour each player index's units are drawn in on a minimap that paints every player in its own colour,
	///     indexed by player index and packed as 0xRRGGBBAA, zero while the map stated none.
	/// </summary>
	public uint[] MinimapPlayerColors;

	/// <summary>
	///     The colour each player index's units are drawn in on a minimap that paints players by their relation to the
	///     screen looking at it, indexed by player index and packed as 0xRRGGBBAA, zero while the map stated none.
	/// </summary>
	public uint[] MinimapTeamColors;

	/// <summary>
	///     The player index each player index stands in for, indexed by player index, zero for an index that stands in for
	///     nobody. A map uses it to say that a player it drives itself carries a real player's army, which the minimap that
	///     paints players by their relation to the screen reads as that player.
	/// </summary>
	public int[] ProxyOwners;
}

/// <summary>
///     One match setting a lobby's map declared, as the lobby publishes it to the screens sitting in it.
/// </summary>
[MemoryPackable]
public partial struct LobbyMatchSettingMessage
{
	/// <summary>The identifier the map declared the setting under.</summary>
	public int SettingId;

	/// <summary>The label the host reads.</summary>
	public string Label;

	/// <summary>The sentence that explains the setting, empty when the map offered none.</summary>
	public string Tooltip;

	/// <summary>The <see cref="Realm.Ecs.LobbySettingControl" /> the setting is edited with.</summary>
	public int Control;

	/// <summary>The value the host chose, or the value the map stated while nobody changed it.</summary>
	public int Value;

	/// <summary>The lowest value a count may hold.</summary>
	public int Minimum;

	/// <summary>The highest value a count may hold.</summary>
	public int Maximum;

	/// <summary>The options a choice offers, in the order their indices name them.</summary>
	public string[] Options;
}

[MemoryPackable]
public partial struct LobbyCommand
{
	public LobbyCommandKind Kind;

	public int TargetPeerId;

	public string Name;
	public string Faction;
	public string TeamName;
	public uint ColorRgba;
	public string Text;
	public bool Flag;

	/// <summary>
	///     Whether the host has already read and accepted the warning its map asks to show before the match starts. A
	///     start the host has not acknowledged is answered with that warning instead of a countdown.
	/// </summary>
	public bool AcknowledgeStartWarning;

	public int Progress;

	/// <summary>The identifier a <see cref="LobbyCommandKind.SetLobbySetting" /> sets, as the map declared it.</summary>
	public int SettingId;

	/// <summary>The value the setting is set to.</summary>
	public int SettingValue;

	/// <summary>The player slot a <see cref="LobbyCommandKind.SetSlotOpen" /> opens or closes.</summary>
	public int Slot;
}

/// <summary>
///     The answer to a presented join code. A refusal is a refused seat, not a disconnect: the screen stays in the room and
///     the player may type the code again.
/// </summary>
[MemoryPackable]
public partial struct LobbyJoinCodeAnswerMessage
{
	/// <summary>Whether the presented code matched the one the lobby gates its seats with.</summary>
	public bool Accepted;
}

[MemoryPackable]
public partial struct LobbyChatMessage
{
	public int SenderPeerId;

	/// <summary>
	///     The SteamID64 whose seat spoke, filled by the authority from the seat rather than from anything the sender typed.
	/// </summary>
	public ulong SenderSteamId;

	public string SenderName;
	public string Message;
	public long TimestampUtcTicks;
}

[MemoryPackable]
public partial struct LobbyMatchStartedMessage
{
	public string MapName;
	public int Seed;
}

[MemoryPackable]
public partial struct LobbyKickedMessage
{
	public string Reason;
}

/// <summary>
///     The warning a lobby's map asks its host to read before the match starts. It is sent only to the host, and the
///     match does not begin until that host starts again with the warning acknowledged.
/// </summary>
[MemoryPackable]
public partial struct LobbyStartWarningMessage
{
	public string Message;
}

[MemoryPackable]
public partial struct MapTransferRequest
{
	public MapTransferRequestKind Kind;
	public string TransferId;
	public string MapName;

	public string[] MissingHashes;

	public int ChunkIndex;
}

[MemoryPackable]
public partial struct MapTransferReply
{
	public MapTransferReplyKind Kind;
	public string TransferId;
	public string MapName;

	/// <summary>
	///     The host's own <c>manifest.json</c>, byte for byte, because that file is the package's identity: a joiner
	///     writes these bytes rather than a re-serialization of them, so the copy it ends up holding is the package the
	///     lobby advertises and not a look-alike that hashes differently.
	/// </summary>
	public byte[] ManifestBytes;

	public string Refusal;
	public int ChunkIndex;
	public int TotalChunks;
	public long TotalBytes;

	public byte[] Bundle;
}

public static class LobbyLimits
{
	/// <summary>
	///     The seat's name ceiling in UTF-16 units. It is deliberately the name byte ceiling: a persona that already reads cleanly
	///     never exceeds <see cref="LobbyPlayerNameRules.MaximumUtf8Bytes" /> UTF-8 bytes, and UTF-8 never uses fewer bytes than the
	///     UTF-16 units it encodes, so such a name is never truncated on its way into a seat.
	/// </summary>
	public const int MaximumPlayerNameLength = LobbyPlayerNameRules.MaximumUtf8Bytes;
	public const int MaximumFactionLength = 48;
	public const int MaximumTeamNameLength = 48;
	public const int MaximumChatMessageLength = 400;

	/// <summary>The ceiling on the warning a map may ask its lobby's host to read before the match starts.</summary>
	public const int MaximumStartWarningLength = 300;

	/// <summary>
	///     The ceiling on the join code a lobby gates its seats with. It is short because it is read out loud and typed by
	///     hand, and because it is a room's shared phrase rather than a credential.
	/// </summary>
	public const int MaximumJoinCodeLength = 32;

	/// <summary>
	///     How many player slots a lobby carries. It is the shared roster's own ceiling minus the neutral slot, so the
	///     default lobby shows one row per player a match could ever seat.
	/// </summary>
	public const int MaximumSeats = Realm.Ecs.EcsSharedMemory.MAX_PLAYERS - 1;

	public const int CountdownSeconds = 5;

	/// <summary>
	///     The number of teams a lobby whose map states none offers. Every seat sits on its own team, which is what the
	///     default free-for-all board offers.
	/// </summary>
	public const int DefaultTeamCount = MaximumSeats;

	/// <summary>
	///     The team name a seat carries while it watches the match instead of playing it. Such a seat holds no player seat,
	///     so it is not counted against the players a map seats.
	/// </summary>
	public const string SpectatorTeamName = "Spectator";

	/// <summary>
	///     The faction a seat holds while it leaves the choice to the match. The lobby offers it as Random, and the authority
	///     resolves it to one of the factions the map offers when it seats the match, so nothing that plays the match ever
	///     reads it as a faction.
	/// </summary>
	public const string RandomFactionName = "@random";

	/// <summary>
	///     Answers whether a seat's faction is the lobby's random choice rather than a faction the map offers.
	/// </summary>
	/// <param name="factionName">The faction a seat holds.</param>
	/// <returns>Whether the seat left its faction to the match.</returns>
	public static bool IsRandomFaction(string? factionName) => string.Equals(factionName, RandomFactionName, StringComparison.Ordinal);

	public const int MaximumMapBundleBytes = 2 * 1024 * 1024;

	/// <summary>
	///     The refusal a peer is answered with when it asks to be seated in a lobby that removed it.
	/// </summary>
	public const string KickedRejection = "server_rejected_kicked";

	public static string Sanitize(string? value, int maximumLength)
	{
		if (string.IsNullOrEmpty(value)) return string.Empty;

		var builder = new System.Text.StringBuilder(value.Length);
		foreach (Rune rune in value.EnumerateRunes())
		{
			UnicodeCategory category = Rune.GetUnicodeCategory(rune);
			if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate) continue;
			builder.Append(rune.ToString());
		}

		return LobbyPlayerNameRules.TruncateAtGraphemeBoundary(builder.ToString().Trim(), maximumLength);
	}

	/// <summary>
	///     The name a lobby carries for a peer. A persona that already reads cleanly is passed through untouched; anything else
	///     is reduced to a control-free, grapheme-safe rendering, so a seat can never hold a name that is unreadable or a string
	///     cut in the middle of a surrogate pair. Steam owns the name itself, so this only makes it safe to draw.
	/// </summary>
	public static string SanitizeDisplayName(string? value)
	{
		return LobbyPlayerNameRules.AdmitForLobby(value, MaximumPlayerNameLength);
	}

	public static bool IsAcceptableTeamName(string? teamName)
	{
		if (string.IsNullOrWhiteSpace(teamName)) return false;
		if (string.Equals(teamName, SpectatorTeamName, StringComparison.Ordinal)) return true;
		if (!teamName.StartsWith("Team ", StringComparison.Ordinal)) return false;
		return int.TryParse(teamName.AsSpan(5), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int team) && team > 0 && team <= MaximumSeats;
	}
}
