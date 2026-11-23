using MemoryPack;

namespace Realm.Shared.Network;

[MemoryPackable]
public partial struct ClientHello
{
	public string Build;

	public string TokenProof;

	public string WireTableHash;

	public string MapHash;

	public string Language;

	public string PlayerName;

	public string AdminProof;

	/// <summary>
	///     The SteamID64 this screen plays as, or 0 when it is not running inside Steam. Steam asserts it on the socket the
	///     hello arrived on, so it is the identity the seat, its reconnect and its chat line are all keyed by.
	/// </summary>
	public ulong SteamId;
}
