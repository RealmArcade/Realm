using System;
using Godot;
using Realm.Shared.Network;

namespace Realm.ListenServerHost.Network;

/// <summary>
///     The authority's end of the wire: one RPC that carries a frame and the peer it came from. A client addresses this
///     node by the path it is autoloaded at, which is the same path the client gives its own channel, so both sides
///     speak to a method they already agree on rather than to each other's scene tree.
/// </summary>
[GlobalClass]
public partial class ServerNetworkChannel : Node
{
	/// <summary>A frame one peer sent. The payload is owned by the listener, which is the match itself.</summary>
	public event Action<int, NetworkFrameKind, byte[]>? FrameArrived;

	/// <summary>
	///     Receives one frame from one peer. It is accepted from any peer because which peers may say what is the
	///     match's decision, made after the frame is read, not the transport's.
	/// </summary>
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void Frame(int kind, byte[] payload)
	{
		FrameArrived?.Invoke(Multiplayer.GetRemoteSenderId(), (NetworkFrameKind)kind, payload ?? Array.Empty<byte>());
	}

	/// <summary>
	///     Receives one frame a peer sent on its way. A client never sends this: what a peer says to a match is an order or
	///     an answer about the world it was given, and both of those have to arrive. It is declared here so both ends of the
	///     wire carry the same two methods, which is what lets <see cref="Send" /> name the peer's method by the name this
	///     end knows it by.
	/// </summary>
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
	public void FrameUnreliable(int kind, byte[] payload)
	{
		FrameArrived?.Invoke(Multiplayer.GetRemoteSenderId(), (NetworkFrameKind)kind, payload ?? Array.Empty<byte>());
	}

	/// <summary>Sends one frame to one peer, carried the way the caller asked for.</summary>
	public void Send(int peerId, NetworkFrameKind kind, byte[] payload, FrameDelivery delivery)
	{
		if (Multiplayer.MultiplayerPeer == null) return;

		RpcId(peerId, delivery == FrameDelivery.UnreliableOrdered ? nameof(FrameUnreliable) : nameof(Frame), (int)kind, payload);
	}
}
