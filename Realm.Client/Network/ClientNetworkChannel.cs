using System;
using Godot;
using Realm.Shared.Network;

namespace Realm.Client.Network;

/// <summary>
///     The client's end of the wire: one RPC that carries a frame from the authority. It is autoloaded at the same path
///     the authority gives its own channel, so both processes address a method they already agree on rather than each
///     other's scene tree.
/// </summary>
[GlobalClass]
public partial class ClientNetworkChannel : Node
{
	/// <summary>The one peer a client has: the authority it is playing against.</summary>
	private const int AuthorityPeerId = 1;

	/// <summary>A frame the authority sent. The payload is owned by the match that reads it.</summary>
	public event Action<NetworkFrameKind, byte[]>? FrameArrived;

	/// <summary>Whether the connection to the authority has finished being established.</summary>
	public bool IsConnected => Multiplayer.MultiplayerPeer?.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;

	/// <summary>
	///     Receives one frame from the authority. Only the authority may call this: a client is told what the world is,
	///     never told it by another client.
	/// </summary>
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void Frame(int kind, byte[] payload)
	{
		FrameArrived?.Invoke((NetworkFrameKind)kind, payload ?? Array.Empty<byte>());
	}

	/// <summary>Reaches the authority on the given loopback port, or reports why it could not.</summary>
	public bool TryConnect(string address, int port, out string failure)
	{
		failure = string.Empty;

		var peer = new ENetMultiplayerPeer();
		Error error = peer.CreateClient(address, port);
		if (error != Error.Ok)
		{
			peer.Dispose();
			failure = $"ENet could not reach {address}:{port}: {error}";
			return false;
		}

		Multiplayer.MultiplayerPeer = peer;
		return true;
	}

	/// <summary>Sends one frame to the authority.</summary>
	public void Send(NetworkFrameKind kind, byte[] payload)
	{
		if (Multiplayer.MultiplayerPeer == null) return;

		RpcId(AuthorityPeerId, nameof(Frame), (int)kind, payload);
	}

	/// <summary>Ends the connection to the authority.</summary>
	public void Disconnect()
	{
		Multiplayer.MultiplayerPeer = null;
	}
}
