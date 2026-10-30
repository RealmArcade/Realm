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

	SnapshotRepairRequest = 26
}
