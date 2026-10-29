using MemoryPack;

namespace Realm.Shared.Network;

/// <summary>
///     Every command a client may send the authority while a lobby is open. A command is a request: the authority
///     validates it against the lobby it holds and answers by describing the lobby it decided on, never by echoing the
///     command back.
/// </summary>
public enum LobbyCommandKind : byte
{
	/// <summary>This player is ready, or no longer ready, to start the match.</summary>
	SetReady = 1,

	/// <summary>Change a seat the sender is allowed to change: its own, or any seat when the sender administers the lobby.</summary>
	UpdateSeat = 2,

	/// <summary>Add a computer player to the lobby. Only the peer that administers the lobby may do this.</summary>
	AddComputer = 3,

	/// <summary>Remove a seat the sender administers, ending that peer's connection for a person.</summary>
	Kick = 4,

	/// <summary>Begin the pre-match countdown. Only the peer that administers the lobby may do this.</summary>
	StartCountdown = 5,

	/// <summary>Cancel a running countdown. Any lobby member may do this, as any of them could before.</summary>
	CancelCountdown = 6,

	/// <summary>Whether spectators watch the match five minutes behind. Only the peer that administers the lobby may do this.</summary>
	SetSpectatorDelay = 7,

	/// <summary>Whether the sender holds the map the lobby selected, whole and verified.</summary>
	ReportMapReady = 8,

	/// <summary>A line of lobby chat, or a command typed into it.</summary>
	Chat = 9,

	/// <summary>The sender is leaving the lobby rather than being dropped from it.</summary>
	Leave = 10,

	/// <summary>What the sender measured about its own stability, as the lobby screen shows it for the host.</summary>
	ReportHostStability = 11,

	/// <summary>
	///     Ask the authority to send a NAT hole punch towards one address. It is what a host's client used to do on the
	///     socket it owned; the authority owns the socket now, so the request travels to it instead.
	/// </summary>
	PunchAddress = 12,

	/// <summary>Ask the authority to repeat the lobby's chat history to this seat, which is what a lobby screen opened late does.</summary>
	RequestChatHistory = 13
}

/// <summary>Whether a map transfer request asks for the map's manifest or for one bundle of the files it lists.</summary>
public enum MapTransferRequestKind : byte
{
	Manifest = 1,
	Chunk = 2
}

/// <summary>What the authority answered a map transfer request with.</summary>
public enum MapTransferReplyKind : byte
{
	Manifest = 1,
	Chunk = 2,
	Refused = 3
}

/// <summary>
///     One seat in a lobby, as the authority holds it. A seat is what a player is in the lobby and on the roster the
///     match starts from: a person is <see cref="IsComputer" /> false and has a live <see cref="PeerId" /> while it is
///     attached, and a computer player has a negative <see cref="PeerId" /> and no connection at all.
/// </summary>
[MemoryPackable]
public partial struct LobbySeat
{
	/// <summary>The connection playing this seat, or a negative identifier for a computer player.</summary>
	public int PeerId;

	/// <summary>The player slot the seat plays, which is the index the map's script addresses it by.</summary>
	public int Slot;

	public string Name;
	public string Faction;

	/// <summary>The team as players read it: "Team 1", "Team 2", or "Spectator".</summary>
	public string TeamName;

	/// <summary>The team number <see cref="TeamName" /> stands for, or the seat's own slot when it names no team.</summary>
	public int Team;

	/// <summary>The seat's colour, packed the way the renderer packs one, so both processes agree on what a seat looks like.</summary>
	public uint ColorRgba;

	/// <summary>Whether this seat is the peer that administers the lobby.</summary>
	public bool IsHost;

	/// <summary>Whether the match plays this seat itself.</summary>
	public bool IsComputer;

	/// <summary>Whether the player is ready to start the match.</summary>
	public bool IsReady;

	/// <summary>Whether the player holds the lobby's map, whole and verified.</summary>
	public bool IsMapReady;

	/// <summary>Whether a person's connection dropped and the seat is being held for it to come back.</summary>
	public bool IsDisconnected;

	/// <summary>The identity the seat may be handed back with after its connection dropped.</summary>
	public string SessionToken;
}

/// <summary>
///     The lobby as the authority holds it. It is sent whole after every change, so a client never has to apply a
///     command in the same order another client did, and a client that reconnects catches up from one message.
/// </summary>
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

	/// <summary>How many times the authority has described this lobby, so a client can drop an older description that arrived late.</summary>
	public long Revision;
}

/// <summary>One command from a lobby member, whatever kind it is.</summary>
[MemoryPackable]
public partial struct LobbyCommand
{
	public LobbyCommandKind Kind;

	/// <summary>The seat a command is about, or zero for a command about the sender.</summary>
	public int TargetPeerId;

	public string Name;
	public string Faction;
	public string TeamName;
	public uint ColorRgba;
	public string Text;
	public bool Flag;

	/// <summary>The address a hole punch is meant for, and the port it is meant to reach.</summary>
	public string Address;
	public int Port;
}

/// <summary>A line of lobby chat, relayed by the authority with the name of the seat that sent it.</summary>
[MemoryPackable]
public partial struct LobbyChatMessage
{
	public int SenderPeerId;
	public string SenderName;
	public string Message;
	public long TimestampUtcTicks;
}

/// <summary>The lobby decided to start a match, on this map, from this seed. Every attached seat receives it together.</summary>
[MemoryPackable]
public partial struct LobbyMatchStartedMessage
{
	public string MapName;
	public int Seed;
}

/// <summary>A seat was removed by the peer administering the lobby, and this connection is ending because of it.</summary>
[MemoryPackable]
public partial struct LobbyKickedMessage
{
	public string Reason;
}

/// <summary>A client asking the authority for the map's manifest or for one bundle of the files it lists.</summary>
[MemoryPackable]
public partial struct MapTransferRequest
{
	public MapTransferRequestKind Kind;
	public string TransferId;
	public string MapName;

	/// <summary>The file hashes the client does not hold. Only the files it is missing are served.</summary>
	public string[] MissingHashes;

	public int ChunkIndex;
}

/// <summary>What the authority answered a map transfer request with.</summary>
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

	/// <summary>A Zstandard-compressed bundle of map files, as the distribution format defines one.</summary>
	public byte[] Bundle;
}

/// <summary>The lengths a lobby accepts, so every process refuses the same text at the same place.</summary>
public static class LobbyLimits
{
	public const int MaximumPlayerNameLength = 32;
	public const int MaximumFactionLength = 48;
	public const int MaximumTeamNameLength = 48;
	public const int MaximumChatMessageLength = 400;

	public const int MaximumSeats = 16;
	public const int CountdownSeconds = 5;

	/// <summary>The largest bundle one map transfer frame carries, so a map is served in pieces a frame can hold.</summary>
	public const int MaximumMapBundleBytes = 2 * 1024 * 1024;

	/// <summary>Strips control characters and caps a line of text from a client, so no seat can make a screen render what it cannot show.</summary>
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

	/// <summary>Whether a team name is one the lobby and the roster both understand.</summary>
	public static bool IsAcceptableTeamName(string? teamName)
	{
		if (string.IsNullOrWhiteSpace(teamName)) return false;
		if (string.Equals(teamName, "Spectator", StringComparison.Ordinal)) return true;
		if (!teamName.StartsWith("Team ", StringComparison.Ordinal)) return false;
		return int.TryParse(teamName.AsSpan(5), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int team) && team > 0 && team <= MaximumSeats;
	}
}
