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

		Error error = CreateClientPeer(address, port, localPort, out ENetMultiplayerPeer? peer);
		if (error != Error.Ok && localPort > 0) error = CreateClientPeer(address, port, 0, out peer);

		if (error != Error.Ok || peer == null)
		{
			failure = $"ENet could not reach {address}:{port}: {error}";
			return false;
		}

		Multiplayer.MultiplayerPeer = peer;
		return true;
	}

	private static Error CreateClientPeer(string address, int port, int localPort, out ENetMultiplayerPeer? peer)
	{
		peer = null;

		var candidate = new ENetMultiplayerPeer();
		Error error = localPort > 0 ? candidate.CreateClient(address, port, localPort: localPort) : candidate.CreateClient(address, port);
		if (error != Error.Ok)
		{
			candidate.Dispose();
			return error;
		}

		peer = candidate;
		return Error.Ok;
	}

	public void Send(NetworkFrameKind kind, byte[] payload)
	{
		if (Multiplayer.MultiplayerPeer == null) return;

		RpcId(AuthorityPeerId, nameof(Frame), (int)kind, payload);
	}

	/// <summary>
	///     Sends a frame the next one of its kind supersedes, over the stream that would rather be current than complete.
	///     An answer that names where this screen has got to and a probe that times the path are both worth nothing once
	///     they are late, so neither belongs behind a retransmission.
	/// </summary>
	public void SendUnreliable(NetworkFrameKind kind, byte[] payload)
	{
		if (Multiplayer.MultiplayerPeer == null) return;

		RpcId(AuthorityPeerId, nameof(FrameUnreliable), (int)kind, payload);
	}

	public void Disconnect()
	{
		MultiplayerPeer? peer = Multiplayer.MultiplayerPeer;
		if (peer != null) peer.Close();
		Multiplayer.MultiplayerPeer = null;
	}
}
