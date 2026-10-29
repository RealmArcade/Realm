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
}
