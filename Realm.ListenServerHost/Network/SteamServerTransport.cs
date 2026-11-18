using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Realm.ListenServer;
using Realm.Shared.Network;
using Steamworks;

namespace Realm.ListenServerHost.Network;

/// <summary>
///     The authority's Steam peer-to-peer listener. It accepts remote peers over Steam's own sockets, which carry the
///     connection, authentication, encryption and Steam Datagram Relay, so nothing here punches a hole, asks a router for a
///     mapping or publishes an address. Every peer arrives with an identity Steam asserted, and the transport remembers it.
///     <para>
///         Steam reports no jitter figure and no packet-loss count. Round trip is Steam's own measurement. Jitter is derived
///         from the variance of the round trips this transport has seen, which is why the diagnostic line labels it as
///         derived. The percentage carried in <see cref="PeerLinkStatistics" /> is Steam's own connection-quality figure
///         expressed as a percentage rather than a count of lost packets.
///     </para>
/// </summary>
public class SteamServerTransport : IServerTransport
{
	private const int MaximumMessagesPerPoll = 64;

	private const int LinkQualitySamples = 16;

	private readonly ServerLog _log;

	private readonly Dictionary<int, HSteamNetConnection> _connectionByPeer = new();

	private readonly Dictionary<int, ulong> _steamIdByPeer = new();

	private readonly Dictionary<int, Queue<int>> _recentRoundTripsByPeer = new();

	private int _nextPeerId = 1;

	private Callback<SteamNetConnectionStatusChangedCallback_t>? _connectionStatusCallback;

	private HSteamListenSocket _listenSocket = HSteamListenSocket.Invalid;

	private IServerTransportSink? _sink;

	private bool _disposed;

	/// <summary>
	///     Creates the listener for one hosted match. The log is where a peer's SteamID and the route Steam chose appear; no
	///     screen and no frame reads either of them.
	/// </summary>
	/// <param name="log">The authority log this match writes to.</param>
	public SteamServerTransport(ServerLog log)
	{
		_log = log;
	}

	/// <summary>
	///     Steam's peer-to-peer listener has no port of its own: a peer addresses the host's SteamID and Steam chooses the
	///     path. The property is part of the transport contract and answers zero rather than inventing a number a lobby could
	///     publish.
	/// </summary>
	public int Port => 0;

	public int RequestedPort => 0;

	public bool IsListeningOnAnotherPort => false;

	public bool TryStart(int maximumPeers, bool loopbackOnly, IServerTransportSink sink, out string failure)
	{
		failure = string.Empty;
		_sink = sink;

		if (!SteamAPI.IsSteamRunning())
		{
			failure = "Steamworks is not running in this process, so it cannot listen for Steam peers";
			return false;
		}

		try
		{
			_connectionStatusCallback ??= Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);

			_listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, Array.Empty<SteamNetworkingConfigValue_t>());
			if (_listenSocket == HSteamListenSocket.Invalid)
			{
				failure = "Steam refused to create a peer-to-peer listen socket for this host";
				return false;
			}
		}
		catch (Exception exception)
		{
			failure = $"Steam's peer-to-peer listener could not be created: {exception.Message}";
			return false;
		}

		_log.Write($"listening for Steam peers as {SteamUser.GetSteamID().m_SteamID}");
		return true;
	}

	/// <summary>
	///     Delivers every message and connection change Steam has ready. It runs once per tick on the authority's main
	///     thread and never blocks: a poll that finds nothing returns immediately.
	/// </summary>
	public void Poll()
	{
		if (_disposed || _listenSocket == HSteamListenSocket.Invalid) return;

		var messagePointers = new IntPtr[MaximumMessagesPerPoll];
		var payloadBuffer = new byte[64 * 1024];

		foreach (KeyValuePair<int, HSteamNetConnection> entry in _connectionByPeer)
		{
			int received;
			try
			{
				received = SteamNetworkingSockets.ReceiveMessagesOnConnection(entry.Value, messagePointers, MaximumMessagesPerPoll);
			}
			catch (Exception exception)
			{
				_log.Write($"messages from peer {entry.Key} could not be read: {exception.Message}");
				continue;
			}

			for (int index = 0; index < received; index++)
			{
				IntPtr pointer = messagePointers[index];
				try
				{
					SteamNetworkingMessage_t message = SteamNetworkingMessage_t.FromIntPtr(pointer);
					int size = message.m_cbSize;
					if (size <= 0) continue;

					if (payloadBuffer.Length < size) payloadBuffer = new byte[size];
					Marshal.Copy(message.m_pData, payloadBuffer, 0, size);

					if (!SteamFrameCodec.TryRead(new ReadOnlySpan<byte>(payloadBuffer, 0, size), out NetworkFrameKind kind, out ReadOnlySpan<byte> payload)) continue;

					_sink?.OnFrameReceived(entry.Key, kind, payload);
				}
				catch (Exception exception)
				{
					_log.Write($"a frame from peer {entry.Key} could not be read: {exception.Message}");
				}
				finally
				{
					SteamNetworkingMessage_t.Release(pointer);
				}
			}
		}
	}

	private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t status)
	{
		if (status.m_hConn == HSteamNetConnection.Invalid) return;

		switch (status.m_info.m_eState)
		{
			case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
				Accept(status.m_hConn);
				break;

			case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
				break;

			case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
			case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
				CloseAndForget(status.m_hConn, DescribeEnd(status.m_info));
				break;
		}
	}

	private void Accept(HSteamNetConnection connection)
	{
		try
		{
			if (SteamNetworkingSockets.AcceptConnection(connection) != EResult.k_EResultOK)
			{
				_log.Write("a Steam peer's connection was offered and could not be accepted");
				SteamNetworkingSockets.CloseConnection(connection, 0, "realm refused the connection", false);
				return;
			}
		}
		catch (Exception exception)
		{
			_log.Write($"a Steam peer's connection could not be accepted: {exception.Message}");
			return;
		}

		ulong steamId = 0;
		string route = "steam";
		try
		{
			if (SteamNetworkingSockets.GetConnectionInfo(connection, out SteamNetConnectionInfo_t info))
			{
				steamId = info.m_identityRemote.GetSteamID().m_SteamID;
				route = DescribeRoute(info);
			}
		}
		catch (Exception exception)
		{
			_log.Write($"a Steam peer's connection details could not be read: {exception.Message}");
		}

		// An internal id is handed out from a counter that never repeats inside one match, so a departed peer's repair ring
		// and queued frames can never be read as the new peer's.
		int peerId = _nextPeerId++;

		_connectionByPeer[peerId] = connection;
		_steamIdByPeer[peerId] = steamId;

		_log.Write($"Steam peer {peerId} connected as {steamId} over {route}");
		_sink?.OnPeerConnected(peerId);
	}

	private void CloseAndForget(HSteamNetConnection connection, string reason)
	{
		int peerId = 0;
		foreach (KeyValuePair<int, HSteamNetConnection> entry in _connectionByPeer)
		{
			if (entry.Value.m_HSteamNetConnection != connection.m_HSteamNetConnection) continue;
			peerId = entry.Key;
			break;
		}

		try
		{
			SteamNetworkingSockets.CloseConnection(connection, 0, "realm closed the connection", false);
		}
		catch (Exception exception)
		{
			_log.Write($"a Steam peer's connection could not be closed: {exception.Message}");
		}

		if (peerId == 0) return;

		_connectionByPeer.Remove(peerId);
		_steamIdByPeer.Remove(peerId);
		_recentRoundTripsByPeer.Remove(peerId);

		_log.Write($"Steam peer {peerId} disconnected: {reason}");
		_sink?.OnPeerDisconnected(peerId, reason);
	}

	/// <summary>
	///     Names the route Steam chose for one connection. Steam picks direct or relayed on its own and the game never
	///     overrides it, so this exists for the authority log and for nothing else.
	/// </summary>
	/// <param name="info">The connection Steam described.</param>
	/// <returns>Whether the traffic is being relayed, or whether it is going direct.</returns>
	private static string DescribeRoute(SteamNetConnectionInfo_t info)
	{
		return info.m_idPOPRelay.m_SteamNetworkingPOPID != 0 ? "Steam relay" : "Steam direct";
	}

	private static string DescribeEnd(SteamNetConnectionInfo_t info)
	{
		string debug = info.m_szEndDebug;
		if (!string.IsNullOrWhiteSpace(debug)) return debug;

		return info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
			? "the peer closed the connection"
			: "Steam lost the connection";
	}

	public void Send(int peerId, NetworkFrameKind kind, byte[] payload, FrameDelivery delivery = FrameDelivery.Reliable)
	{
		if (!_connectionByPeer.TryGetValue(peerId, out HSteamNetConnection connection)) return;

		byte[] frame = payload ?? Array.Empty<byte>();
		byte[] buffer = new byte[SteamFrameCodec.HeaderBytes + frame.Length];
		int written = SteamFrameCodec.TryWrite(buffer, kind, frame);
		if (written <= 0) return;

		// Unreliable frames leave immediately rather than waiting to be coalesced with the next one, because a description
		// the next description supersedes is worth nothing once it is late.
		int flags = delivery == FrameDelivery.Reliable
			? Constants.k_nSteamNetworkingSend_Reliable
			: Constants.k_nSteamNetworkingSend_UnreliableNoDelay;

		try
		{
			GCHandle pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
			try
			{
				SteamNetworkingSockets.SendMessageToConnection(connection, pinned.AddrOfPinnedObject(), (uint)written, flags, out _);
			}
			finally
			{
				pinned.Free();
			}
		}
		catch (Exception exception)
		{
			_log.Write($"a frame could not be sent to Steam peer {peerId}: {exception.Message}");
		}
	}

	public void Disconnect(int peerId)
	{
		if (!_connectionByPeer.TryGetValue(peerId, out HSteamNetConnection connection)) return;

		try
		{
			SteamNetworkingSockets.CloseConnection(connection, 0, "realm closed the connection", false);
		}
		catch (Exception exception)
		{
			_log.Write($"Steam peer {peerId} could not be disconnected: {exception.Message}");
		}
	}

	/// <summary>
	///     The Steam transport has no socket to send a bare datagram from, which is the only thing this call is for. A punch
	///     is not part of a Steam connection at all, so it answers false rather than pretending to have sent one.
	/// </summary>
	public bool SendToAddress(string address, int port, ReadOnlySpan<byte> payload) => false;

	public bool TryReadPeerLink(int peerId, out PeerLinkStatistics statistics)
	{
		statistics = default;
		if (!_connectionByPeer.TryGetValue(peerId, out HSteamNetConnection connection)) return false;			try
			{
				var status = new SteamNetConnectionRealTimeStatus_t();
				var lanes = new SteamNetConnectionRealTimeLaneStatus_t();
				EResult result = SteamNetworkingSockets.GetConnectionRealTimeStatus(connection, ref status, 0, ref lanes);
				if (result != EResult.k_EResultOK) return false;

			int roundTrip = Math.Clamp(status.m_nPing, 0, int.MaxValue);
			int jitter = RecordAndDeriveJitter(peerId, roundTrip);
			float qualityPercent = Math.Clamp((1f - status.m_flConnectionQualityRemote) * 100f, 0f, 100f);

			statistics = new PeerLinkStatistics(roundTrip, jitter, qualityPercent);
			return true;
		}
		catch (Exception exception)
		{
			_log.Write($"peer {peerId}'s Steam link could not be read: {exception.Message}");
			return false;
		}
	}

	private int RecordAndDeriveJitter(int peerId, int roundTripMilliseconds)
	{
		if (!_recentRoundTripsByPeer.TryGetValue(peerId, out Queue<int>? samples))
		{
			samples = new Queue<int>(LinkQualitySamples);
			_recentRoundTripsByPeer[peerId] = samples;
		}

		if (samples.Count == LinkQualitySamples) samples.Dequeue();
		samples.Enqueue(roundTripMilliseconds);

		if (samples.Count < 2) return 0;

		double mean = 0;
		foreach (int sample in samples) mean += sample;
		mean /= samples.Count;

		double variance = 0;
		foreach (int sample in samples) variance += (sample - mean) * (sample - mean);
		variance /= samples.Count;

		return (int)Math.Round(Math.Sqrt(variance));
	}

	public bool TryGetPeerSteamId(int peerId, out ulong steamId) => _steamIdByPeer.TryGetValue(peerId, out steamId) && steamId != 0;

	public bool TryGetPeerPathType(int peerId, out string pathType)
	{
		pathType = string.Empty;
		if (!_connectionByPeer.TryGetValue(peerId, out HSteamNetConnection connection)) return false;

		try
		{
			if (!SteamNetworkingSockets.GetConnectionInfo(connection, out SteamNetConnectionInfo_t info)) return false;
			pathType = DescribeRoute(info);
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public void Stop()
	{
		foreach (HSteamNetConnection connection in _connectionByPeer.Values)
		{
			try
			{
				SteamNetworkingSockets.CloseConnection(connection, 0, "the match ended", false);
			}
			catch (Exception)
			{
			}
		}

		_connectionByPeer.Clear();
		_steamIdByPeer.Clear();
		_recentRoundTripsByPeer.Clear();

		if (_listenSocket != HSteamListenSocket.Invalid)
		{
			try
			{
				SteamNetworkingSockets.CloseListenSocket(_listenSocket);
			}
			catch (Exception exception)
			{
				_log.Write($"the Steam listener could not be closed: {exception.Message}");
			}

			_listenSocket = HSteamListenSocket.Invalid;
		}
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		Stop();

		_connectionStatusCallback?.Dispose();
		_connectionStatusCallback = null;
	}
}
