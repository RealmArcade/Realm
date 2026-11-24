namespace Realm.Shared.Network;

public enum NetworkFrameKind : byte
{
	ClientHello = 1,
	ServerHello = 2,
	Shutdown = 3,

	Command = 4,

	CommandOutcome = 5,

	Snapshot = 6,

	Roster = 7,

	SnapshotAck = 8,

	BaselineRequest = 9,

	Presentation = 10,

	LatencyProbe = 11,

	LatencyProbeEcho = 12,

	NavMeshObstacles = 13,

	PauseRequest = 14,

	MatchPaused = 15,

	ConsoleCommand = 16,

	ChatLine = 17,

	HandshakeChallenge = 18,

	LobbyCommand = 19,

	LobbyState = 20,

	LobbyChat = 21,

	LobbyMatchStarted = 22,

	LobbyKicked = 23,

	MapTransferRequest = 24,

	MapTransferReply = 25,

	SnapshotRepairRequest = 26,

	PauseAllowance = 27,

	SnapshotDescriptionRepeat = 28,

	LobbyStartWarning = 29,

	/// <summary>
	///     The cloud files one player's screen holds for this map, sent in answer to
	///     <see cref="PresentationKind.SteamCloudFilesRequested" />. The seat is the one the connection was seated in, so the
	///     frame states no player of its own.
	/// </summary>
	SteamCloudUpload = 30,

	/// <summary>
	///     The authority's answer to a join code a screen presented: accepted, which seats it, or refused, which leaves it in
	///     the room free to try again.
	/// </summary>
	LobbyJoinCodeAnswer = 31,

	/// <summary>
	///     A player's playtest vote about the map they are playing, sent while the match runs. The identity it is recorded
	///     against is the one the handshake seated the connection as, never an id the client states.
	/// </summary>
	PlaytestVote = 32,

	/// <summary>The authority's answer to a playtest vote, carrying the code the tester hands to the map's author.</summary>
	PlaytestVoteAnswer = 33,

	/// <summary>One accepted playtest vote, handed to the screens in the match so each keeps it in its own ledger.</summary>
	PlaytestVoteRecord = 34
}
