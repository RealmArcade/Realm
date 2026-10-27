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

	/// <summary>Sends one frame to one peer.</summary>
	public void Send(int peerId, NetworkFrameKind kind, byte[] payload)
	{
		if (Multiplayer.MultiplayerPeer == null) return;

		RpcId(peerId, nameof(Frame), (int)kind, payload);
	}
}
