using System;
using Godot;
using Realm.Client.Services;
using Realm.Shared.Network;

namespace Realm.Client.Network;

[GlobalClass]
public partial class ClientNetworkChannel : Node
{
	private const int AuthorityPeerId = 1;

	private const int LongSilenceTimeoutLimit = 32;

	private const int LongSilenceTimeoutMilliseconds = 86_400_000;

	public event Action<NetworkFrameKind, byte[]>? FrameArrived;
	public event Action? ConnectionFailed;
	public event Action? ServerDisconnected;

	private readonly SimulatedLink _simulatedLink = SimulatedLink.FromEnvironment();

	/// <summary>
	///     The Steam connection this screen uses when it joins somebody else's match. It is null for the host's own seat,
	///     which keeps the loopback socket it has always had.
	/// </summary>
	private SteamClientLink? _steamLink;

	public SimulatedLink SimulatedLink => _simulatedLink;

	/// <summary>Whether the running connection is Steam's rather than the loopback socket.</summary>
	public bool IsSteamConnection => _steamLink != null;

	public new bool IsConnected => _steamLink != null
		? _steamLink.IsConnected
		: Multiplayer.MultiplayerPeer?.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;

	public bool IsConnecting => _steamLink != null
		? _steamLink.IsConnecting
		: Multiplayer.MultiplayerPeer?.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connecting;

	public override void _Ready()
	{
		Multiplayer.ConnectionFailed += OnMultiplayerConnectionFailed;
		Multiplayer.ServerDisconnected += OnMultiplayerServerDisconnected;
	}

	public override void _ExitTree()
	{
		Multiplayer.ConnectionFailed -= OnMultiplayerConnectionFailed;
		Multiplayer.ServerDisconnected -= OnMultiplayerServerDisconnected;
		CloseSteamLink("this screen is closing");
	}

	/// <summary>
	///     Joins a host over Steam. The host is a SteamID and nothing else: there is no address, port or punch ticket in the
	///     join path, and Steam chooses whether the traffic goes direct or through its relay.
	/// </summary>
	/// <param name="hostSteamId">The SteamID64 of the host's authority, as the Steam lobby states it.</param>
	/// <param name="failure">Why the connection could not be started.</param>
	/// <returns>Whether Steam accepted the attempt.</returns>
	public bool TryConnectToSteamHost(ulong hostSteamId, out string failure)
	{
		CloseSteamLink("a new join replaced it");

		var link = new SteamClientLink();
		link.FrameArrived += (kind, payload) =>
		{
			if (_simulatedLink.TryQueueInbound(kind, payload, false)) return;
			FrameArrived?.Invoke(kind, payload);
		};
		link.Closed += reason =>
		{
			if (!ReferenceEquals(_steamLink, link)) return;
			ServerDisconnected?.Invoke();
			GameLog.LogError($"[RealmNetwork] {reason}");
		};

		if (!link.Connect(hostSteamId, out failure))
		{
			link.Dispose();
			return false;
		}

		_steamLink = link;
		return true;
	}

	/// <summary>
	///     Gives up the Steam connection this screen holds, if any, and states why once.
	/// </summary>
	/// <param name="reason">A player-readable reason the connection is over.</param>
	public void CloseSteamLink(string reason)
	{
		SteamClientLink? link = _steamLink;
		_steamLink = null;
		if (link == null) return;

		link.Dispose();
	}

	private void OnMultiplayerConnectionFailed() => ConnectionFailed?.Invoke();

	private void OnMultiplayerServerDisconnected() => ServerDisconnected?.Invoke();

	/// <summary>
	///     Hands on the frames a simulated link has finished with. Every frame travels through the queue even when the link
	///     is off, which costs one branch and keeps the two paths identical: what a harness measures is then the same code
	///     a screen runs, with a delay and a drop rate in front of it.
	/// </summary>
	public override void _Process(double delta)
	{
		_steamLink?.Poll();
		ReleaseDueFrames();
	}

	public override void _PhysicsProcess(double delta)
	{
		_steamLink?.Poll();
		ReleaseDueFrames();
	}

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
		if (error != Error.Ok && localPort > 0)
		{
			GD.Print($"[RealmNetwork] Local port {localPort} could not be held for this join ({error}), so the socket takes the port the system chooses.");
			error = CreateClientPeer(address, port, 0, out peer);
		}

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

		if (_steamLink != null)
		{
			_steamLink.Send(kind, payload, unreliable: false);
			return;
		}

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

		if (_steamLink != null)
		{
			_steamLink.Send(kind, payload, unreliable: true);
			return;
		}

		SendNow(kind, payload, true);
	}

	/// <summary>
	///     The link quality of the connection this screen is using. A Steam connection answers from Steam's own real-time
	///     status, so the lobby shows the same kind of numbers it always has.
	/// </summary>
	/// <param name="statistics">What the transport measured.</param>
	/// <returns>Whether the running transport had a reading.</returns>
	public bool TryReadLinkStatistics(out PeerLinkStatistics statistics)
	{
		statistics = default;
		return _steamLink != null && _steamLink.TryReadLink(out statistics);
	}

	public void Disconnect()
	{
		CloseSteamLink("this screen left the match");

		MultiplayerPeer? peer = Multiplayer.MultiplayerPeer;
		if (peer != null) peer.Close();
		Multiplayer.MultiplayerPeer = null;
	}
}
