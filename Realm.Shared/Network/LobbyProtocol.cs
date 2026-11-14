using System.Globalization;
using System.Text;
using MemoryPack;
using Realm.Shared.Accounts;

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

	PunchAddress = 12,

	RequestChatHistory = 13,

	ReportLoadReady = 14,

	ReportMapProgress = 15,

	SetComputerDifficulty = 16,

	/// <summary>Sets one of the parameters the host configures for the match, in <see cref="LobbyCommand.Option" />.</summary>
	SetMatchOption = 17,

	/// <summary>Sets whether the match groups each team's players into consecutive player slots, in <see cref="LobbyCommand.Flag" />.</summary>
	SetTeamsTogether = 18
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

	public long AccountId;

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

	public string SessionToken;

	public int LatencyMilliseconds;

	public int JitterMilliseconds;

	public float PacketLossPercent;

	public int MapDownloadPercent;

	/// <summary>
	///     The <see cref="LobbyBotDifficulty" /> this computer seat plays on, or zero for a seat no one seated as a computer.
	/// </summary>
	public int Difficulty;

	/// <summary>
	///     The SteamID64 the seat's own client plays as, or 0 for a computer seat, a guest who is not running inside Steam, or
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
	public int MaxPlayers;
	public string[] Factions;
	public int TeamCount;
	public bool SpectatorDelay;
	public string HostStability;
	public bool CountdownActive;
	public int CountdownRemainingSeconds;

	/// <summary>The parameters the host configured for the match, which every seat reads to show them.</summary>
	public Realm.Shared.Simulation.MatchOptionValue[] Options;

	public long Revision;

	/// <summary>
	///     Whether the match groups each team's players into consecutive player slots, which every seat reads to show the
	///     setting and only the host may change. It is last so the seats a peer already reads keep their wire position.
	/// </summary>
	public bool TeamsTogether;
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

	public string Address;
	public int Port;

	public int Progress;

	/// <summary>The <see cref="Realm.Ecs.MatchOption" /> a <see cref="LobbyCommandKind.SetMatchOption" /> sets.</summary>
	public int Option;

	/// <summary>
	///     The value the option is set to, where zero returns the option to the map's own default.
	/// </summary>
	public int OptionValue;
}

[MemoryPackable]
public partial struct LobbyChatMessage
{
	public int SenderPeerId;

	/// <summary>
	///     The account whose seat spoke, filled by the authority from the seat rather than from anything the sender typed.
	/// </summary>
	public long SenderAccountId;

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
	public string ManifestJson;
	public string Refusal;
	public int ChunkIndex;
	public int TotalChunks;
	public long TotalBytes;

	public byte[] Bundle;
}

public static class LobbyLimits
{
	/// <summary>
	///     The seat's name ceiling in UTF-16 units. It is deliberately the DisplayName byte ceiling: a legal DisplayName never
	///     exceeds <see cref="DisplayNameRules.MaximumUtf8Bytes" /> UTF-8 bytes, and UTF-8 never uses fewer bytes than the UTF-16
	///     units it encodes, so a registered name can never be truncated on its way into a seat.
	/// </summary>
	public const int MaximumPlayerNameLength = DisplayNameRules.MaximumUtf8Bytes;
	public const int MaximumFactionLength = 48;
	public const int MaximumTeamNameLength = 48;
	public const int MaximumChatMessageLength = 400;

	/// <summary>The ceiling on the warning a map may ask its lobby's host to read before the match starts.</summary>
	public const int MaximumStartWarningLength = 300;

	public const int MaximumSeats = 16;
	public const int CountdownSeconds = 5;

	/// <summary>The number of teams a lobby whose map states none offers, which is the two teams lobbies have always had.</summary>
	public const int DefaultTeamCount = 2;

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

		return DisplayNameRules.TruncateAtGraphemeBoundary(builder.ToString().Trim(), maximumLength);
	}

	/// <summary>
	///     The name a lobby carries for a peer. A legal DisplayName is passed through untouched; anything else is reduced to a
	///     control-free, grapheme-safe rendering so a seat can never hold a name the account authority would refuse or a string
	///     cut in the middle of a surrogate pair. The unverified mark is stripped, because whether a name is marked is what the
	///     screen knows about the seat, never something the seat declares about itself.
	/// </summary>
	public static string SanitizeDisplayName(string? value)
	{
		return UnverifiedNameMark.Remove(DisplayNameRules.AdmitForLobby(value, MaximumPlayerNameLength));
	}

	public static bool IsAcceptableTeamName(string? teamName)
	{
		if (string.IsNullOrWhiteSpace(teamName)) return false;
		if (string.Equals(teamName, SpectatorTeamName, StringComparison.Ordinal)) return true;
		if (!teamName.StartsWith("Team ", StringComparison.Ordinal)) return false;
		return int.TryParse(teamName.AsSpan(5), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int team) && team > 0 && team <= MaximumSeats;
	}
}
