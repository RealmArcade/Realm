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

	PunchAddress = 12,

	RequestChatHistory = 13,

	ReportLoadReady = 14
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

	public string SessionToken;
}

[MemoryPackable]
public partial struct LobbyStateMessage
{
	public LobbySeat[] Seats;
	public string MapName;
	public string MapVersion;
	public int MaxPlayers;
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
}

[MemoryPackable]
public partial struct LobbyChatMessage
{
	public int SenderPeerId;
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
	public const int MaximumPlayerNameLength = 32;
	public const int MaximumFactionLength = 48;
	public const int MaximumTeamNameLength = 48;
	public const int MaximumChatMessageLength = 400;

	public const int MaximumSeats = 16;
	public const int CountdownSeconds = 5;

	public const int MaximumMapBundleBytes = 2 * 1024 * 1024;

	public static string Sanitize(string? value, int maximumLength)
	{
		if (string.IsNullOrEmpty(value)) return string.Empty;

		int length = Math.Min(value.Length, maximumLength);
		var builder = new System.Text.StringBuilder(length);
		for (int index = 0; index < length; index++)
		{
			char character = value[index];
			if (char.IsControl(character)) continue;
			builder.Append(character);
		}

		return builder.ToString().Trim();
	}

	public static bool IsAcceptableTeamName(string? teamName)
	{
		if (string.IsNullOrWhiteSpace(teamName)) return false;
		if (string.Equals(teamName, "Spectator", StringComparison.Ordinal)) return true;
		if (!teamName.StartsWith("Team ", StringComparison.Ordinal)) return false;
		return int.TryParse(teamName.AsSpan(5), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int team) && team > 0 && team <= MaximumSeats;
	}
}
