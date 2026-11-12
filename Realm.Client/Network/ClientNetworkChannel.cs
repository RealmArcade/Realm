using System;
using Godot;
using Realm.Shared.Network;

namespace Realm.Client.Network;

[GlobalClass]
public partial class ClientNetworkChannel : Node
{
	private const int AuthorityPeerId = 1;

	private const int LongSilenceTimeoutLimit = 32;

	private const int LongSilenceTimeoutMilliseconds = 86_400_000;

	public event Action<NetworkFrameKind, byte[]>? FrameArrived;

	private readonly SimulatedLink _simulatedLink = SimulatedLink.FromEnvironment();

	public SimulatedLink SimulatedLink => _simulatedLink;

	public new bool IsConnected => Multiplayer.MultiplayerPeer?.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;

	/// <summary>
	///     Hands on the frames a simulated link has finished with. Every frame travels through the queue even when the link
	///     is off, which costs one branch and keeps the two paths identical: what a harness measures is then the same code
	///     a screen runs, with a delay and a drop rate in front of it.
	/// </summary>
	public override void _Process(double delta) => ReleaseDueFrames();

	public override void _PhysicsProcess(double delta) => ReleaseDueFrames();

	private void ReleaseDueFrames()
	{
		if (!_simulatedLink.IsEnabled) return;

		while (_simulatedLink.TryDequeue(out bool outbound, out NetworkFrameKind kind, out byte[] payload, out bool unreliable))
		{
			if (outbound) SendNow(kind, payload, unreliable);
			else FrameArrived?.Invoke(kind, payload);
		}

		_simulatedLink.ObserveCounters();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void Frame(int kind, byte[] payload)
	{
		byte[] frame = payload ?? Array.Empty<byte>();
		if (_simulatedLink.TryQueueInbound((NetworkFrameKind)kind, frame, false)) return;
		FrameArrived?.Invoke((NetworkFrameKind)kind, frame);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
	public void FrameUnreliable(int kind, byte[] payload)
	{
		byte[] frame = payload ?? Array.Empty<byte>();
		if (_simulatedLink.TryQueueInbound((NetworkFrameKind)kind, frame, true)) return;
		FrameArrived?.Invoke((NetworkFrameKind)kind, frame);
	}

	public bool TryConnect(string address, int port, out string failure) => TryConnect(address, port, 0, false, out failure);

	public bool TryConnect(string address, int port, bool allowLongSilence, out string failure) => TryConnect(address, port, 0, allowLongSilence, out failure);

	public bool TryConnect(string address, int port, int localPort, out string failure) => TryConnect(address, port, localPort, false, out failure);

	public bool TryConnect(string address, int port, int localPort, bool allowLongSilence, out string failure)
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
		if (allowLongSilence) AllowLongAuthoritySilence();
		return true;
	}

	/// <summary>
	///     Lets the authority of a debug match stay silent for as long as a person stands at a breakpoint. The timeout is
	///     ENet's own, so it has to be widened before the silence starts: the authority is stopped when the breakpoint is
	///     hit and cannot answer, and a peer that times out there is a match that can never be resumed.
	/// </summary>
	public bool AllowLongAuthoritySilence()
	{
		ENetMultiplayerPeer? peer = Multiplayer.MultiplayerPeer as ENetMultiplayerPeer;
		ENetPacketPeer? authorityPeer = peer?.GetPeer(AuthorityPeerId);
		if (authorityPeer == null) return false;

		authorityPeer.SetTimeout(LongSilenceTimeoutLimit, LongSilenceTimeoutMilliseconds, LongSilenceTimeoutMilliseconds);
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
		if (!IsConnected) return;
		if (_simulatedLink.TryQueueOutbound(kind, payload, false)) return;

		SendNow(kind, payload, false);
	}

	private void SendNow(NetworkFrameKind kind, byte[] payload, bool unreliable)
	{
		if (!IsConnected) return;

		RpcId(AuthorityPeerId, unreliable ? nameof(FrameUnreliable) : nameof(Frame), (int)kind, payload);
	}

	/// <summary>
	///     Sends a frame the next one of its kind supersedes, over the stream that would rather be current than complete.
	///     An answer that names where this screen has got to and a probe that times the path are both worth nothing once
	///     they are late, so neither belongs behind a retransmission.
	/// </summary>
	public void SendUnreliable(NetworkFrameKind kind, byte[] payload)
	{
		if (!IsConnected) return;
		if (_simulatedLink.TryQueueOutbound(kind, payload, true)) return;

		SendNow(kind, payload, true);
	}

	public void Disconnect()
	{
		MultiplayerPeer? peer = Multiplayer.MultiplayerPeer;
		if (peer != null) peer.Close();
		Multiplayer.MultiplayerPeer = null;
	}
}
