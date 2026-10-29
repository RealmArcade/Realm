using MemoryPack;

namespace Realm.Shared.Network;

/// <summary>
///     The authority's half of the handshake: the one value a peer has to answer before it may say who it is. It is
///     sent the moment a peer connects, so a peer proves it holds the match's ticket without ever sending the ticket.
/// </summary>
[MemoryPackable]
public partial struct HandshakeChallenge
{
	/// <summary>The challenge this connection was handed, which is answered with <see cref="ClientHello.TokenProof" />.</summary>
	public string Nonce;
}
