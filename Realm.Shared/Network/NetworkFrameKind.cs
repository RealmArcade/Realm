namespace Realm.Shared.Network;

/// <summary>
///     Every kind of frame two processes exchange about one match. The numbers are part of the wire protocol, so a kind
///     keeps its value once a build ships it.
/// </summary>
public enum NetworkFrameKind : byte
{
	ClientHello = 1,
	ServerHello = 2,
	Shutdown = 3,

	/// <summary>A player's order, as the client authored it.</summary>
	Command = 4,

	/// <summary>Whether the authority took one command, and why not when it did not.</summary>
	CommandOutcome = 5,

	/// <summary>The world as the peer's player can see it.</summary>
	Snapshot = 6,

	/// <summary>The player slot the authority gave the peer that received it.</summary>
	Roster = 7,

	/// <summary>The newest snapshot a peer has applied, so the authority knows what it no longer has to repeat.</summary>
	SnapshotAck = 8,

	/// <summary>A peer asking to be described from scratch, which is what a client that lost its place does.</summary>
	BaselineRequest = 9,

	/// <summary>Something the authority decided a screen should show: an effect, a line of text, a sound, a camera move.</summary>
	Presentation = 10,

	/// <summary>A client asking to be answered at once, so it can measure the round trip to the authority.</summary>
	LatencyProbe = 11,

	/// <summary>That probe, answered unchanged.</summary>
	LatencyProbeEcho = 12,

	/// <summary>
	///     The obstacle volumes the authority baked its navigation mesh with, so a peer bakes the same one rather than a
	///     mesh that is missing what the map's script spawned.
	/// </summary>
	NavMeshObstacles = 13,

	/// <summary>A peer asking the match to pause or resume. The authority decides and answers with <see cref="MatchPaused" />.</summary>
	PauseRequest = 14,

	/// <summary>The pause state the authority holds, which is what every screen shows.</summary>
	MatchPaused = 15,

	/// <summary>A line a player typed into the map script's console, which only the authority can interpret.</summary>
	ConsoleCommand = 16,

	/// <summary>
	///     A line of chat, either on its way to the authority or relayed by it. The match is what every screen is
	///     connected to, so a line a map's script has to see - and every line other players have to read - travels here
	///     rather than between peers.
	/// </summary>
	ChatLine = 17,

	/// <summary>
	///     The challenge a peer answers before it says who it is, so the ticket a match was started with never travels.
	///     It is the first frame an authority sends and the only one a peer may receive before it has spoken.
	/// </summary>
	HandshakeChallenge = 18
}
