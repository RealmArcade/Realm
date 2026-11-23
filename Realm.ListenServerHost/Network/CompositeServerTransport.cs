using System;
using System.Collections.Generic;
using Realm.Shared.Network;

namespace Realm.ListenServerHost.Network;

/// <summary>
///     The one transport a hosted match sees. Two links stand behind it: the loopback socket the host's own screen uses, and
///     Steam's peer-to-peer listener every remote player arrives on. Both are presented as one peer set with disjoint
///     internal ids, so the simulation keeps its small-integer peer contract and knows nothing about which link a seat
///     travelled over.
///     <para>
///         Internal ids are handed out from a counter that never repeats inside a match. ENet would otherwise recycle an id
///         after a peer leaves, and a recycled id inherits the repair ring and queued frames of the peer it replaces, which
///         delivers one player's world to another.
///     </para>
/// </summary>
public class CompositeServerTransport : IServerTransport
{
	private const int FirstLoopbackPeerId = 1;

	private const int FirstSteamPeerId = 1000;

	private readonly EnetServerTransport _loopback;

	private readonly SteamServerTransport? _steam;

	private readonly LinkSink _loopbackSink;

	private readonly LinkSink? _steamSink;

	private readonly Dictionary<int, LinkSink> _linkByPeer = new();

	private readonly Dictionary<int, int> _nativeIdByPeer = new();

	private readonly Dictionary<LinkSink, Dictionary<int, int>> _peerByNativeId = new();

	private int _nextLoopbackPeerId = FirstLoopbackPeerId;

	private int _nextSteamPeerId = FirstSteamPeerId;

	private IServerTransportSink? _sink;

	/// <summary>
	///     Builds the transport a hosted match uses.
	/// </summary>
	/// <param name="loopback">The socket the host's own screen connects to, bound to the loopback address.</param>
	/// <param name="steam">The Steam listener remote peers arrive on, or <c>null</c> for a match nobody else can join.</param>
	public CompositeServerTransport(EnetServerTransport loopback, SteamServerTransport? steam)
	{
		_loopback = loopback;
		_steam = steam;

		_loopbackSink = new LinkSink(this, loopback, FirstLoopbackPeerId);
		_steamSink = steam == null ? null : new LinkSink(this, steam, FirstSteamPeerId);

		_peerByNativeId[_loopbackSink] = new Dictionary<int, int>();
		if (_steamSink != null) _peerByNativeId[_steamSink] = new Dictionary<int, int>();
	}

	public int Port => _loopback.Port;

	public int RequestedPort => _loopback.RequestedPort;

	public bool IsListeningOnAnotherPort => _loopback.IsListeningOnAnotherPort;

	public bool TryStart(int maximumPeers, bool loopbackOnly, IServerTransportSink sink, out string failure)
	{
		_sink = sink;
		failure = string.Empty;

		// The loopback socket is always bound to the loopback address: a machine with a permissive firewall must not be
		// listening for a player this game no longer admits, and no screen offers an address for one to arrive at.
		if (!_loopback.TryStart(maximumPeers, loopbackOnly: true, _loopbackSink, out failure)) return false;

		if (_steam == null || _steamSink == null) return true;

		if (!_steam.TryStart(maximumPeers, loopbackOnly: false, _steamSink, out failure))
		{
			_loopback.Stop();
			return false;
		}

		return true;
	}

	public void Poll()
	{
		((IServerTransport)_loopback).Poll();
		_steam?.Poll();
	}

	public void Send(int peerId, NetworkFrameKind kind, byte[] payload, FrameDelivery delivery = FrameDelivery.Reliable)
	{
		if (!_linkByPeer.TryGetValue(peerId, out LinkSink? link)) return;
		if (!_nativeIdByPeer.TryGetValue(peerId, out int nativeId)) return;

		link.Transport.Send(nativeId, kind, payload, delivery);
	}

	public void Disconnect(int peerId)
	{
		if (!_linkByPeer.TryGetValue(peerId, out LinkSink? link)) return;
		if (!_nativeIdByPeer.TryGetValue(peerId, out int nativeId)) return;

		link.Transport.Disconnect(nativeId);
	}

	public bool TryReadPeerLink(int peerId, out PeerLinkStatistics statistics)
	{
		statistics = default;
		if (!_linkByPeer.TryGetValue(peerId, out LinkSink? link)) return false;
		if (!_nativeIdByPeer.TryGetValue(peerId, out int nativeId)) return false;

		return link.Transport.TryReadPeerLink(nativeId, out statistics);
	}

	public bool TryGetPeerSteamId(int peerId, out ulong steamId)
	{
		steamId = 0;
		if (!_linkByPeer.TryGetValue(peerId, out LinkSink? link)) return false;
		if (!_nativeIdByPeer.TryGetValue(peerId, out int nativeId)) return false;

		return link.Transport.TryGetPeerSteamId(nativeId, out steamId);
	}

	public bool TryGetPeerPathType(int peerId, out string pathType)
	{
		pathType = string.Empty;
		if (!_linkByPeer.TryGetValue(peerId, out LinkSink? link)) return false;
		if (!_nativeIdByPeer.TryGetValue(peerId, out int nativeId)) return false;

		return link.Transport.TryGetPeerPathType(nativeId, out pathType);
	}

	public void Stop()
	{
		_steam?.Stop();
		_loopback.Stop();

		_linkByPeer.Clear();
		_nativeIdByPeer.Clear();
		foreach (Dictionary<int, int> map in _peerByNativeId.Values) map.Clear();
	}

	public void Dispose()
	{
		_steam?.Dispose();
		_loopback.Dispose();
	}

	private void OnLinkPeerConnected(LinkSink link, int nativePeerId)
	{
		if (_peerByNativeId[link].ContainsKey(nativePeerId)) return;

		int peerId = link.IsSteamLink ? _nextSteamPeerId++ : _nextLoopbackPeerId++;

		_linkByPeer[peerId] = link;
		_nativeIdByPeer[peerId] = nativePeerId;
		_peerByNativeId[link][nativePeerId] = peerId;

		_sink?.OnPeerConnected(peerId);
	}

	private void OnLinkFrameReceived(LinkSink link, int nativePeerId, NetworkFrameKind kind, ReadOnlySpan<byte> payload)
	{
		if (!_peerByNativeId[link].TryGetValue(nativePeerId, out int peerId)) return;

		_sink?.OnFrameReceived(peerId, kind, payload);
	}

	private void OnLinkPeerDisconnected(LinkSink link, int nativePeerId, string reason)
	{
		if (!_peerByNativeId[link].TryGetValue(nativePeerId, out int peerId)) return;

		// The peer's id is retired for the rest of the match rather than handed back out, so nothing it left behind can be
		// read as the next peer's.
		_peerByNativeId[link].Remove(nativePeerId);
		_linkByPeer.Remove(peerId);
		_nativeIdByPeer.Remove(peerId);

		_sink?.OnPeerDisconnected(peerId, reason);
	}

	/// <summary>
	///     One link's own view of the sink. It exists so a callback arriving from a sub-transport states which link it came
	///     from, which is what keeps an internal peer id bound to one transport for the life of the match.
	/// </summary>
	private sealed class LinkSink : IServerTransportSink
	{
		private readonly CompositeServerTransport _owner;

		public LinkSink(CompositeServerTransport owner, IServerTransport transport, int firstPeerId)
		{
			_owner = owner;
			Transport = transport;
			IsSteamLink = firstPeerId != FirstLoopbackPeerId;
		}

		public IServerTransport Transport { get; }

		public bool IsSteamLink { get; }

		public void OnPeerConnected(int peerId) => _owner.OnLinkPeerConnected(this, peerId);

		public void OnFrameReceived(int peerId, NetworkFrameKind kind, ReadOnlySpan<byte> payload) => _owner.OnLinkFrameReceived(this, peerId, kind, payload);

		public void OnPeerDisconnected(int peerId, string reason) => _owner.OnLinkPeerDisconnected(this, peerId, reason);
	}
}
