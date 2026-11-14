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

	public long AccountId;

	public string SessionToken;

	public string AdminProof;

	/// <summary>
	///     The SteamID64 this screen plays as, or 0 when it is not running inside Steam. It is last so every field a peer
	///     already sent keeps its wire position, and it travels with the hello because the lobby publishes it to the other
	///     seats, which is what lets a player at the end of a match ask Steam to add somebody they just played with.
	/// </summary>
	public ulong SteamId;
}
