using System;
using Realm.Shared.Network.Relay;

namespace Realm.Shared.Network;

public interface IServerTransportSink
{
	void OnPeerConnected(int peerId);

	void OnFrameReceived(int peerId, NetworkFrameKind kind, ReadOnlySpan<byte> payload);

	void OnPeerDisconnected(int peerId, string reason);

	/// <summary>
	///     States what one relayed connection's paths did: a path that stopped being answered, one that answered again, the
	///     best path moving, or the entry being given up and why. It is what a captured authority log needs to explain a move
	///     of the connection rather than only the path it ended up on, and it is called only when something changed.
	/// </summary>
	/// <param name="statement">The change, naming the loopback entry it happened on.</param>
	void OnRelayedPathStatement(string statement);
}

/// <summary>
///     What the transport itself has measured about one peer's link. ENet keeps these per connection, so they are read
///     rather than guessed: a round trip the authority observed, how far those trips land from each other, and how much
///     of what it sent that peer never acknowledged.
/// </summary>
public readonly record struct PeerLinkStatistics(int RoundTripMilliseconds, int JitterMilliseconds, float PacketLossPercent);

public interface IServerTransport : IDisposable
{
	/// <summary>
	///     The port this transport is actually listening on, which is the port every address the lobby publishes states.
	/// </summary>
	int Port { get; }

	/// <summary>
	///     The port this transport was asked to listen on, which differs from <see cref="Port" /> when something else on this
	///     machine already held it.
	/// </summary>
	int RequestedPort { get; }

	/// <summary>Whether this transport is listening somewhere other than the port it was asked for.</summary>
	bool IsListeningOnAnotherPort { get; }

	bool TryStart(int maximumPeers, bool loopbackOnly, IServerTransportSink sink, out string failure);

	void Send(int peerId, NetworkFrameKind kind, byte[] payload, FrameDelivery delivery = FrameDelivery.Reliable);

	void Disconnect(int peerId);

	bool SendToAddress(string address, int port, ReadOnlySpan<byte> payload);

	/// <summary>
	///     Reads the link quality of one connected peer, or answers false when this transport holds no measurement for it
	///     yet.
	/// </summary>
	bool TryReadPeerLink(int peerId, out PeerLinkStatistics statistics);

	/// <summary>
	///     Opens one relayed entry in front of this transport: a stable loopback address the relayed connection speaks to, so
	///     the peer behind it is seated exactly like a direct one and keeps that peer identity when the volunteer carrying it
	///     changes. It is a call the lobby makes for a join that named volunteers, and a lobby that never makes it behaves
	///     exactly as it did before relays existed.
	/// </summary>
	/// <param name="plan">The volunteers both peers were offered and the ticket their keys come from.</param>
	/// <param name="failure">Why no entry was opened.</param>
	/// <returns>Whether the connection is being carried.</returns>
	bool TryOpenRelayedEntry(RelayConnectionPlan plan, out string failure);

	/// <summary>How many relayed connections this transport is carrying.</summary>
	int RelayedEntryCount { get; }

	/// <summary>States each relayed connection and the paths behind it, for the match log and the diagnostics report.</summary>
	/// <returns>The relayed connections, or a line stating that the transport is direct only.</returns>
	string DescribeRelayedEntries();

	/// <summary>
	///     Gives up the relayed entries that are carrying nothing, so a join that went direct, or one that never arrived,
	///     stops costing a loopback socket, a lane and a volunteer a probe a second, and states every path change to the sink
	///     on the way. It is called by the clock that runs the match rather than by the next join, and a transport that
	///     carries no relayed connection answers it by doing nothing.
	/// </summary>
	/// <param name="nowMilliseconds">The clock the entries' ages are read against.</param>
	void MaintainRelayedEntries(long nowMilliseconds);

	void Stop();
}
