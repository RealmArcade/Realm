using MemoryPack;

namespace Realm.Shared.Network;

[MemoryPackable]
public partial struct ClientHello
{
	public int Protocol;
	public string Build;

	/// <summary>
	///     The answer to the challenge this connection was handed (<see cref="RealmProtocol.ProveToken" />), or nothing
	///     when this peer holds no ticket. The ticket itself never crosses the wire, so reading this frame tells an
	///     observer nothing they can use on another connection.
	/// </summary>
	public string TokenProof;

	public string WireTableHash;

	/// <summary>
	///     The map this peer is playing, as the hash of the authored files it holds, or nothing when this peer cannot state
	///     one. The wire table says both sides decode the same components; this says they are describing the same world.
	/// </summary>
	public string MapHash;

	/// <summary>
	///     The locale the client reads in, so text the authority produces for that player is written in the language that
	///     player's own screens are in rather than in the authority's.
	/// </summary>
	public string Language;

	/// <summary>
	///     The name this player is shown under in the lobby, already sanitized by the client that typed it. It is only
	///     the first name a seat is given; the lobby's authority owns every change to it afterwards.
	/// </summary>
	public string PlayerName;

	/// <summary>
	///     The identity this connection presents on a later connection, so a seat it dropped out of is handed back to it
	///     rather than to whoever connects next. It is not a credential and grants nothing on its own.
	/// </summary>
	public string SessionToken;

	/// <summary>
	///     The answer to the challenge that proves possession of the ticket this lobby's authority was started with. It
	///     is what makes this peer the lobby's administrator: a process on the loopback address that does not hold that
	///     ticket is an ordinary member, because the loopback address is not a credential.
	/// </summary>
	public string AdminProof;
}
