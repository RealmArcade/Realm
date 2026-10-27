using System;

namespace Realm.Shared.Network;

/// <summary>
///     What a transport reports to the match as peers arrive, speak and leave. Frames are handed over on the thread that
///     drives the match, which is the thread its world is stepped on, so a command cannot land halfway through a tick.
/// </summary>
public interface IServerTransportSink
{
	/// <summary>A peer finished connecting. It has not been seated on the roster yet and owns nothing.</summary>
	void OnPeerConnected(int peerId);

	/// <summary>A peer spoke. The frame is only valid for the duration of the call, so what outlives it is copied.</summary>
	void OnFrameReceived(int peerId, NetworkFrameKind kind, ReadOnlySpan<byte> payload);

	/// <summary>A peer is gone, whether it left, was dropped, or stopped answering.</summary>
	void OnPeerDisconnected(int peerId, string reason);
}

/// <summary>
///     The match's end of the network. A transport owns its peers and addresses them by the identity it assigned them,
///     so the match never holds a socket, a peer object, or a thread: it is told what arrived and answers by peer id.
/// </summary>
public interface IServerTransport : IDisposable
{
	/// <summary>The port peers reach this match on, known once it is started.</summary>
	int Port { get; }

	/// <summary>
	///     Starts listening for up to <paramref name="maximumPeers" /> peers. A refusal reports why rather than throwing,
	///     because the caller is starting a match and has to report the failure to the player.
	/// </summary>
	bool TryStart(int maximumPeers, IServerTransportSink sink, out string failure);

	/// <summary>Sends one frame to one peer. A peer that is gone is not an error: the match is told it left.</summary>
	void Send(int peerId, NetworkFrameKind kind, byte[] payload);

	/// <summary>Ends one peer's connection, after which the sink is told it disconnected.</summary>
	void Disconnect(int peerId);

	/// <summary>Ends every peer's connection and stops listening.</summary>
	void Stop();
}
