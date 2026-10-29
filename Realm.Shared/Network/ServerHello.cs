using MemoryPack;

namespace Realm.Shared.Network;

[MemoryPackable]
public partial struct ServerHello
{
	public int Protocol;
	public string Build;
	public bool Accepted;
	public string Rejection;

	/// <summary>
	///     The map this authority is running, as the hash of the authored files it loaded. A peer that holds a map of its
	///     own compares it before playing: two worlds built from different files agree on the protocol and disagree about
	///     everything a player can see.
	/// </summary>
	public string MapHash;

	/// <summary>The identity the authority gave this connection, which a lobby state or a roster names it by.</summary>
	public int PeerId;

	/// <summary>Whether this connection administers the lobby, which is settled by the ticket it proved it holds.</summary>
	public bool IsAdmin;

	/// <summary>Whether this connection was admitted to a lobby rather than seated in a running match.</summary>
	public bool InLobby;
}
