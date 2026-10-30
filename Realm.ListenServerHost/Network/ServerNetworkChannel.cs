using System;
using Godot;
using Realm.Shared.Network;

namespace Realm.ListenServerHost.Network;

[GlobalClass]
public partial class ServerNetworkChannel : Node
{
	public event Action<int, NetworkFrameKind, byte[]>? FrameArrived;

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void Frame(int kind, byte[] payload)
	{
		FrameArrived?.Invoke(Multiplayer.GetRemoteSenderId(), (NetworkFrameKind)kind, payload ?? Array.Empty<byte>());
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
	public void FrameUnreliable(int kind, byte[] payload)
	{
		FrameArrived?.Invoke(Multiplayer.GetRemoteSenderId(), (NetworkFrameKind)kind, payload ?? Array.Empty<byte>());
	}

	public void Send(int peerId, NetworkFrameKind kind, byte[] payload, FrameDelivery delivery)
	{
		if (Multiplayer.MultiplayerPeer == null) return;

		RpcId(peerId, delivery == FrameDelivery.UnreliableOrdered ? nameof(FrameUnreliable) : nameof(Frame), (int)kind, payload);
	}
}
