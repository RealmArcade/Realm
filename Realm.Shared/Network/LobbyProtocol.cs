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

	ReportMapProgress = 15
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

	public long Revision;
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

	public string Address;
	public int Port;

	public int Progress;
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

	public const int MaximumSeats = 16;
	public const int CountdownSeconds = 5;

	/// <summary>The number of teams a lobby whose map states none offers, which is the two teams lobbies have always had.</summary>
	public const int DefaultTeamCount = 2;

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
	///     cut in the middle of a surrogate pair.
	/// </summary>
	public static string SanitizeDisplayName(string? value)
	{
		return DisplayNameRules.AdmitForLobby(value, MaximumPlayerNameLength);
	}

	public static bool IsAcceptableTeamName(string? teamName)
	{
		if (string.IsNullOrWhiteSpace(teamName)) return false;
		if (string.Equals(teamName, "Spectator", StringComparison.Ordinal)) return true;
		if (!teamName.StartsWith("Team ", StringComparison.Ordinal)) return false;
		return int.TryParse(teamName.AsSpan(5), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int team) && team > 0 && team <= MaximumSeats;
	}
}
