using System;

namespace Realm.Shared.Network;

public interface IServerTransportSink
{
	void OnPeerConnected(int peerId);

	void OnFrameReceived(int peerId, NetworkFrameKind kind, ReadOnlySpan<byte> payload);

	void OnPeerDisconnected(int peerId, string reason);
}

/// <summary>
///     What the transport itself has measured about one peer's link. ENet keeps these per connection, so they are read
///     rather than guessed: a round trip the authority observed, how far those trips land from each other, and how much
///     of what it sent that peer never acknowledged. A transport that reports connection quality instead of a packet count
///     states that quality as the percentage, which is why the field is described as what the transport measured rather
///     than as a figure this game computed.
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

	/// <summary>
	///     Delivers the traffic and connection events that arrived since the last call. A transport whose library only
	///     progresses while it is polled — Steam's peer-to-peer sockets are one — receives nothing at all without it, so the
	///     authority calls this once per tick, before it reads any frame.
	/// </summary>
	void Poll()
	{
	}

	/// <summary>
	///     The SteamID64 the transport authenticated this peer as, when the transport is Steam's. A peer whose identity the
	///     transport did not assert answers nothing, which is what keeps a seat from existing without a verified identity.
	/// </summary>
	/// <param name="peerId">The internal peer id the simulation uses.</param>
	/// <param name="steamId">The peer's SteamID64, or <c>0</c>.</param>
	/// <returns>Whether this transport knows a Steam identity for that peer.</returns>
	bool TryGetPeerSteamId(int peerId, out ulong steamId)
	{
		steamId = 0;
		return false;
	}

	/// <summary>
	///     A short description of the route Steam chose for this peer, for the authority log only. It is a diagnostic rather
	///     than a feature: nothing in the game's flow reads it, and no screen shows a player a relay toggle or a plan.
	/// </summary>
	/// <param name="peerId">The internal peer id the simulation uses.</param>
	/// <param name="pathType">The route Steam selected, or an empty string.</param>
	/// <returns>Whether this transport knows a route for that peer.</returns>
	bool TryGetPeerPathType(int peerId, out string pathType)
	{
		pathType = string.Empty;
		return false;
	}

	bool TryStart(int maximumPeers, bool loopbackOnly, IServerTransportSink sink, out string failure);

	void Send(int peerId, NetworkFrameKind kind, byte[] payload, FrameDelivery delivery = FrameDelivery.Reliable);

	void Disconnect(int peerId);

	bool SendToAddress(string address, int port, ReadOnlySpan<byte> payload);

	/// <summary>
	///     Reads the link quality of one connected peer, or answers false when this transport holds no measurement for it
	///     yet.
	/// </summary>
	bool TryReadPeerLink(int peerId, out PeerLinkStatistics statistics);

	void Stop();
}
