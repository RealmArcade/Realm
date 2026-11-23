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

	private readonly ulong _steamLobbyId;

	private readonly Dictionary<int, HSteamNetConnection> _connectionByPeer = new();

	private readonly Dictionary<int, ulong> _steamIdByPeer = new();

	private readonly IntPtr[] _messagePointers = new IntPtr[MaximumMessagesPerPoll];

	private readonly List<int> _pollPeerIds = new();

	private byte[] _payloadBuffer = new byte[64 * 1024];

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
	/// <param name="steamLobbyId">The Steam lobby this match belongs to, whose members are the only peers admitted.</param>
	public SteamServerTransport(ServerLog log, ulong steamLobbyId)
	{
		_log = log;
		_steamLobbyId = steamLobbyId;
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
		}			try
			{
				_connectionStatusCallback ??= Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);

				// The tuning is stated twice on purpose: as this process's Steam defaults, and on the listener whose connections
				// inherit it. A setting Steam will not take is logged and left at Steam's own value, because the tuning is an
				// optimisation and a lobby that cannot exist is not.
				ApplyConnectionDefaults();

				SteamNetworkingConfigValue_t[] socketOptions = BuildConnectionOptions();
				_listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, socketOptions.Length, socketOptions);
				if (_listenSocket == HSteamListenSocket.Invalid)
				{
					_log.Write("Steam refused the listener with this match's connection tuning stated on it, so it listens with Steam's own values");
					_listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, Array.Empty<SteamNetworkingConfigValue_t>());
				}

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
	///     The connection settings a map transfer needs, stated in the type Steam says each one holds. The type has to be
	///     stated: Steam refuses a listener whose options leave it out, which is a lobby that never exists rather than a
	///     transfer that runs slowly.
	/// </summary>
	/// <returns>The options, which a listener's connections inherit.</returns>
	private SteamNetworkingConfigValue_t[] BuildConnectionOptions()
	{
		var options = new List<SteamNetworkingConfigValue_t>(4);
		AddConnectionOption(options, ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize, LobbyLimits.MaximumMapTransferBytesInFlight);
		AddConnectionOption(options, ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin, LobbyLimits.ConnectionSendRateMinimumBytesPerSecond);
		AddConnectionOption(options, ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax, LobbyLimits.ConnectionSendRateMaximumBytesPerSecond);
		AddConnectionOption(options, ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_RecvBufferSize, LobbyLimits.ConnectionReceiveBufferBytes);
		return options.ToArray();
	}

	/// <summary>
	///     Adds one setting to a listener's options when Steam states a type this transport can express, so a value Steam
	///     describes some other way is left out rather than stated wrong.
	/// </summary>
	/// <param name="options">The options being built.</param>
	/// <param name="value">The setting to state.</param>
	/// <param name="bytes">Its value, in bytes or bytes per second.</param>
	private void AddConnectionOption(List<SteamNetworkingConfigValue_t> options, ESteamNetworkingConfigValue value, int bytes)
	{
		ESteamNetworkingConfigDataType dataType = DescribeConfigType(value);
		switch (dataType)
		{
			case ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32:
				options.Add(new SteamNetworkingConfigValue_t
				{
					m_eValue = value,
					m_eDataType = dataType,
					m_val = new SteamNetworkingConfigValue_t.OptionValue { m_int32 = bytes }
				});
				break;

			case ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Float:
				options.Add(new SteamNetworkingConfigValue_t
				{
					m_eValue = value,
					m_eDataType = dataType,
					m_val = new SteamNetworkingConfigValue_t.OptionValue { m_float = bytes }
				});
				break;
		}
	}

	/// <summary>
	///     States the same settings as this process's Steam defaults, which covers any connection whose listener did not carry
	///     them. Steam paces a peer connection by an estimate of the path and bounds that estimate with a clamp that defaults
	///     to 256 KB/s, and it buffers far less than one window of pieces on the receiving side; both defaults suit a match's
	///     own traffic rather than a map's assets, so a transfer would spend its time against them instead of the line.
	/// </summary>
	private void ApplyConnectionDefaults()
	{
		ApplyConnectionDefault(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin, LobbyLimits.ConnectionSendRateMinimumBytesPerSecond);
		ApplyConnectionDefault(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax, LobbyLimits.ConnectionSendRateMaximumBytesPerSecond);
		ApplyConnectionDefault(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_RecvBufferSize, LobbyLimits.ConnectionReceiveBufferBytes);
	}

	private void ApplyConnectionDefault(ESteamNetworkingConfigValue value, int bytes)
	{
		ESteamNetworkingConfigDataType dataType = DescribeConfigType(value);
		if (dataType is not (ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32 or ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Float)) return;

		IntPtr stated = Marshal.AllocHGlobal(sizeof(int));
		try
		{
			if (dataType == ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32) Marshal.WriteInt32(stated, 0, bytes);
			else Marshal.StructureToPtr((float)bytes, stated, false);

			bool applied = SteamNetworkingUtils.SetConfigValue(value, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero, dataType, stated);
			if (!applied) _log.Write($"Steam refused to state {value} as {bytes}, so this match's connections keep Steam's own value");
		}
		catch (Exception exception)
		{
			_log.Write($"{value} could not be stated for this match's connections: {exception.Message}");
		}
		finally
		{
			Marshal.FreeHGlobal(stated);
		}
	}

	/// <summary>
	///     The type Steam says one connection setting holds, or the invalid one when Steam cannot be asked.
	/// </summary>
	/// <param name="value">The setting to ask about.</param>
	/// <returns>The setting's data type.</returns>
	private ESteamNetworkingConfigDataType DescribeConfigType(ESteamNetworkingConfigValue value)
	{
		try
		{
			SteamNetworkingUtils.GetConfigValueInfo(value, out ESteamNetworkingConfigDataType dataType, out _);
			return dataType;
		}
		catch (Exception exception)
		{
			_log.Write($"Steam would not say what type {value} holds, so this match leaves it at Steam's own value: {exception.Message}");
			return (ESteamNetworkingConfigDataType)0;
		}
	}

	/// <summary>
	///     Delivers every message and connection change Steam has ready. It runs once per tick on the authority's main
	///     thread and never blocks: a poll that finds nothing returns immediately.
	/// </summary>
	public void Poll()
	{
		if (_disposed || _listenSocket == HSteamListenSocket.Invalid) return;

		// The peers are listed before any frame is delivered, because delivering one can refuse and close the peer it came
		// from, and the viewer being walked is one of the dictionaries that closing rewrites.
		_pollPeerIds.Clear();
		foreach (KeyValuePair<int, HSteamNetConnection> peerEntry in _connectionByPeer) _pollPeerIds.Add(peerEntry.Key);

		for (int peerIndex = 0; peerIndex < _pollPeerIds.Count; peerIndex++)
		{
			int peerId = _pollPeerIds[peerIndex];
			if (!_connectionByPeer.TryGetValue(peerId, out HSteamNetConnection connection)) continue;

			int received;
			try
			{
				received = SteamNetworkingSockets.ReceiveMessagesOnConnection(connection, _messagePointers, MaximumMessagesPerPoll);
			}
			catch (Exception exception)
			{
				_log.Write($"messages from peer {peerId} could not be read: {exception.Message}");
				continue;
			}

			for (int index = 0; index < received; index++)
			{
				IntPtr pointer = _messagePointers[index];
				try
				{
					SteamNetworkingMessage_t message = SteamNetworkingMessage_t.FromIntPtr(pointer);
					int size = message.m_cbSize;
					if (size <= 0) continue;

					if (_payloadBuffer.Length < size) _payloadBuffer = new byte[size];
					Marshal.Copy(message.m_pData, _payloadBuffer, 0, size);

					if (!SteamFrameCodec.TryRead(new ReadOnlySpan<byte>(_payloadBuffer, 0, size), out NetworkFrameKind kind, out ReadOnlySpan<byte> payload)) continue;

					_sink?.OnFrameReceived(peerId, kind, payload);
				}
				catch (Exception exception)
				{
					_log.Write($"a frame from peer {peerId} could not be read: {exception.Message}");
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
		// The identity is read before the connection is accepted, because it is the admission decision. A peer that is not a
		// member of this match's Steam lobby is not a seat this game can attribute, seat or moderate.
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

		if (!IsMemberOfMatchLobby(steamId))
		{
			_log.Write($"refused Steam peer {steamId}: not a member of lobby {_steamLobbyId}");
			try
			{
				SteamNetworkingSockets.CloseConnection(connection, 0, "this Steam identity is not in this match's lobby", false);
			}
			catch (Exception)
			{
			}

			return;
		}

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

	/// <summary>
	///     Whether Steam lists this identity among the members of the match's lobby. The authority reads the same lobby the
	///     host's screen created, so the membership test is Steam's answer rather than a list this process kept.
	/// </summary>
	/// <param name="steamId">The identity the connection presented.</param>
	/// <returns>Whether that identity may be seated in this match.</returns>
	private bool IsMemberOfMatchLobby(ulong steamId)
	{
		if (steamId == 0) return false;
		if (_steamLobbyId == 0) return false;

		try
		{
			var lobby = new CSteamID(_steamLobbyId);
			int members = SteamMatchmaking.GetNumLobbyMembers(lobby);
			for (int index = 0; index < members; index++)
			{
				if (SteamMatchmaking.GetLobbyMemberByIndex(lobby, index).m_SteamID == steamId) return true;
			}

			return false;
		}
		catch (Exception exception)
		{
			_log.Write($"the match lobby's membership could not be read, so this peer is refused: {exception.Message}");
			return false;
		}
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
				EResult result = SteamNetworkingSockets.SendMessageToConnection(connection, pinned.AddrOfPinnedObject(), (uint)written, flags, out _);
				if (result != EResult.k_EResultOK) _log.Write($"a {written}-byte {kind} frame could not be sent to Steam peer {peerId}: {result}");
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

	/// <summary>
	///     Ends one peer's connection and forgets it, which is what a refused command, a kick or a failed admission asks for.
	/// </summary>
	/// <remarks>
	///     A connection this transport closes itself is closed without a status callback, so Steam never reports the close
	///     and the peer has to be forgotten here. Leaving it in the tables would keep a dead connection listed: the poll
	///     would keep reading it, every send would keep aiming at it, and the lobby or the match would never learn its peer
	///     is gone, so its seat and peer state would stay held.
	/// </remarks>
	/// <param name="peerId">The peer to close, which is forgotten whether or not Steam still holds the connection.</param>
	public void Disconnect(int peerId)
	{
		if (!_connectionByPeer.TryGetValue(peerId, out HSteamNetConnection connection)) return;

		CloseAndForget(connection, "realm closed the connection");
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
