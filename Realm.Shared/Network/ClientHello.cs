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
	///     The locale the client reads in, so text the authority produces for that player is written in the language that
	///     player's own screens are in rather than in the authority's.
	/// </summary>
	public string Language;
}
