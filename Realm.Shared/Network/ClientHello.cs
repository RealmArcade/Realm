using MemoryPack;

namespace Realm.Shared.Network;

[MemoryPackable]
public partial struct ClientHello
{
	public int Protocol;
	public string Build;
	public string Token;
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
}
