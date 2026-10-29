using System;
using Godot;
using Realm.Shared.Network;

namespace Realm.Client.Network;

[GlobalClass]
public partial class ClientNetworkChannel : Node
{
	private const int AuthorityPeerId = 1;

	public event Action<NetworkFrameKind, byte[]>? FrameArrived;

	public bool IsConnected => Multiplayer.MultiplayerPeer?.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void Frame(int kind, byte[] payload)
	{
		FrameArrived?.Invoke((NetworkFrameKind)kind, payload ?? Array.Empty<byte>());
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
	public void FrameUnreliable(int kind, byte[] payload)
	{
		FrameArrived?.Invoke((NetworkFrameKind)kind, payload ?? Array.Empty<byte>());
	}

	public bool TryConnect(string address, int port, out string failure) => TryConnect(address, port, 0, out failure);

	public bool TryConnect(string address, int port, int localPort, out string failure)
	{
		failure = string.Empty;

		var peer = new ENetMultiplayerPeer();
		Error error = localPort > 0 ? peer.CreateClient(address, port, localPort: localPort) : peer.CreateClient(address, port);
		if (error != Error.Ok && localPort > 0)
		{
			error = peer.CreateClient(address, port);
		}

		if (error != Error.Ok)
		{
			peer.Dispose();
			failure = $"ENet could not reach {address}:{port}: {error}";
			return false;
		}

		Multiplayer.MultiplayerPeer = peer;
		return true;
	}

	public void Send(NetworkFrameKind kind, byte[] payload)
	{
		if (Multiplayer.MultiplayerPeer == null) return;

		RpcId(AuthorityPeerId, nameof(Frame), (int)kind, payload);
	}

	public void Disconnect()
	{
		Multiplayer.MultiplayerPeer = null;
	}
}
