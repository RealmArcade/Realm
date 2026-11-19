using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using Realm.Client.Services;
using Realm.Shared.Network;
using Steamworks;

namespace Realm.Client.Network;

/// <summary>
///     One screen's Steam peer-to-peer connection to a host. There is no address, port, punch ticket or relay plan in it:
///     the host is a SteamID, Steam authenticates both ends and picks a direct or relayed route by itself, and the game's
///     frames ride on top unchanged. It is driven by <see cref="Poll" /> from the channel's frame, because a Steam message
///     is only delivered while the connection is being drained.
/// </summary>
public class SteamClientLink : IDisposable
{
	private const int MaximumMessagesPerPoll = 64;

	private const int LinkQualitySamples = 16;

	private readonly Queue<int> _recentRoundTrips = new(LinkQualitySamples);

	private readonly Callback<SteamNetConnectionStatusChangedCallback_t> _connectionStatusCallback;

	private byte[] _payloadBuffer = new byte[64 * 1024];

	private readonly IntPtr[] _messagePointers = new IntPtr[MaximumMessagesPerPoll];

	private HSteamNetConnection _connection = HSteamNetConnection.Invalid;

	private ESteamNetworkingConnectionState _state = ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_None;

	private bool _disposed;

	private bool _reportedConnected;

	private ulong _hostSteamId;

	/// <summary>Raised once the connection Steam reports as established delivers its first frame or state.</summary>
	public event Action? Connected;

	/// <summary>Raised when <see cref="Poll" /> finds a frame the host sent.</summary>
	public event Action<NetworkFrameKind, byte[]>? FrameArrived;

	/// <summary>Raised once with a player-readable reason when the connection ends or never forms.</summary>
	public event Action<string>? Closed;

	public SteamClientLink()
	{
		_connectionStatusCallback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);
	}

	public bool IsConnected => _state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected;

	public bool IsConnecting => _state is ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting
		or ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_FindingRoute;

	/// <summary>
	///     Opens a Steam connection to the host this screen was told to join. It fails immediately and with a stateable
	///     reason when Steamworks is not running, because a session Steam cannot identify is a session this game cannot seat.
	/// </summary>
	/// <param name="hostSteamId">The SteamID64 of the host's authority.</param>
	/// <param name="failure">Why no connection could be started.</param>
	/// <returns>Whether Steam accepted the attempt.</returns>
	public bool Connect(ulong hostSteamId, out string failure)
	{
		failure = string.Empty;

		if (_disposed)
		{
			failure = "the Steam connection has already been closed";
			return false;
		}

		if (!GameContext.Steam.IsAvailable)
		{
			failure = GameContext.Steam.UnavailableExplanation;
			return false;
		}

		if (hostSteamId == 0)
		{
			failure = "this lobby names no host Steam identity";
			return false;
		}

		if (GameContext.Steam.SteamId != 0 && GameContext.Steam.SteamId == hostSteamId)
		{
			// The host's own seat travels over loopback, never over Steam to itself: a self-addressed connection is not
			// refused, it is relayed over the network to the same machine.
			failure = "this screen is the host, so its own seat uses the loopback connection";
			return false;
		}

		try
		{
			var identity = new SteamNetworkingIdentity();
			identity.SetSteamID64(hostSteamId);

			// The joiner is the side a map's pieces arrive on, so its receive buffer is stated above one window of them, and its
			// own sends are left for Steam to pace rather than clamped to a rate a map transfer would have to fight.
			var connectionOptions = new[]
			{
				new SteamNetworkingConfigValue_t
				{
					m_eValue = ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_RecvBufferSize,
					m_val = new SteamNetworkingConfigValue_t.OptionValue { m_int32 = LobbyLimits.ConnectionReceiveBufferBytes }
				},
				new SteamNetworkingConfigValue_t
				{
					m_eValue = ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin,
					m_val = new SteamNetworkingConfigValue_t.OptionValue { m_int32 = LobbyLimits.ConnectionSendRateMinimumBytesPerSecond }
				},
				new SteamNetworkingConfigValue_t
				{
					m_eValue = ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax,
					m_val = new SteamNetworkingConfigValue_t.OptionValue { m_int32 = LobbyLimits.ConnectionSendRateMaximumBytesPerSecond }
				}
			};

			_connection = SteamNetworkingSockets.ConnectP2P(ref identity, 0, connectionOptions.Length, connectionOptions);
			if (_connection == HSteamNetConnection.Invalid)
			{
				failure = "Steam could not start a connection to the host";
				return false;
			}
		}
		catch (Exception exception)
		{
			failure = $"Steam could not reach the host: {exception.Message}";
			return false;
		}

		_hostSteamId = hostSteamId;
		_state = ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting;
		GameLog.LogToConsole($"[SteamNetwork] connecting to host {hostSteamId}");
		return true;
	}

	/// <summary>
	///     Delivers everything Steam has ready for this connection: state changes it raised, and the frames the host sent. It
	///     runs once per rendered frame on the main thread and never blocks.
	/// </summary>
	public void Poll()
	{
		if (_disposed || _connection == HSteamNetConnection.Invalid) return;

		if (!IsConnected && !IsConnecting) return;

		DrainConnectionState();

		if (IsConnected && !_reportedConnected)
		{
			_reportedConnected = true;
			Connected?.Invoke();
		}

		if (!IsConnected) return;

		int received;

		try
		{
			received = SteamNetworkingSockets.ReceiveMessagesOnConnection(_connection, _messagePointers, MaximumMessagesPerPoll);
		}
		catch (Exception exception)
		{
			Close($"Steam could not read the host's messages: {exception.Message}");
			return;
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

				FrameArrived?.Invoke(kind, payload.ToArray());
			}
			catch (Exception exception)
			{
				GameLog.LogError($"[SteamNetwork] a frame from the host could not be read: {exception.Message}");
			}
			finally
			{
				SteamNetworkingMessage_t.Release(pointer);
			}
		}
	}

	private void DrainConnectionState()
	{
		try
		{
			if (SteamNetworkingSockets.GetConnectionInfo(_connection, out SteamNetConnectionInfo_t info)) _state = info.m_eState;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[SteamNetwork] the connection state could not be read: {exception.Message}");
		}
	}

	private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t status)
	{
		if (_connection == HSteamNetConnection.Invalid) return;
		if (status.m_hConn.m_HSteamNetConnection != _connection.m_HSteamNetConnection) return;

		_state = status.m_info.m_eState;

		switch (_state)
		{
			case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
				if (_reportedConnected) return;
				_reportedConnected = true;
				GameLog.LogToConsole($"[SteamNetwork] connected to host {_hostSteamId} over {(status.m_info.m_idPOPRelay.m_SteamNetworkingPOPID != 0 ? "Steam relay" : "Steam direct")}");
				Connected?.Invoke();
				break;

			case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
			case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
				string reason = string.IsNullOrWhiteSpace(status.m_info.m_szEndDebug)
					? (_state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
						? "The host closed the lobby."
						: "Steam could not keep the connection to the host.")
					: status.m_info.m_szEndDebug;
				Close(reason);
				break;
		}
	}

	public void Send(NetworkFrameKind kind, byte[] payload, bool unreliable)
	{
		if (!IsConnected) return;

		byte[] frame = payload ?? Array.Empty<byte>();
		byte[] buffer = new byte[SteamFrameCodec.HeaderBytes + frame.Length];
		int written = SteamFrameCodec.TryWrite(buffer, kind, frame);
		if (written <= 0) return;

		int flags = unreliable
			? Constants.k_nSteamNetworkingSend_UnreliableNoDelay
			: Constants.k_nSteamNetworkingSend_Reliable;

		try
		{
			GCHandle pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
			try
			{
				EResult result = SteamNetworkingSockets.SendMessageToConnection(_connection, pinned.AddrOfPinnedObject(), (uint)written, flags, out _);
				if (result != EResult.k_EResultOK) GameLog.LogError($"[SteamNetwork] a {written}-byte {kind} frame could not be sent to the host: {result}");
			}
			finally
			{
				pinned.Free();
			}
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[SteamNetwork] a frame could not be sent to the host: {exception.Message}");
		}
	}

	/// <summary>
	///     The round trip Steam measured for this connection, with jitter derived from the variance of recent samples. Steam
	///     does not report a packet-loss count, so the percentage is its own connection-quality figure.
	/// </summary>
	/// <param name="statistics">What Steam reported for this connection.</param>
	/// <returns>Whether Steam had a reading yet.</returns>
	public bool TryReadLink(out PeerLinkStatistics statistics)
	{
		statistics = default;
		if (!IsConnected) return false;			try
			{
				var status = new SteamNetConnectionRealTimeStatus_t();
				var lanes = new SteamNetConnectionRealTimeLaneStatus_t();
				EResult result = SteamNetworkingSockets.GetConnectionRealTimeStatus(_connection, ref status, 0, ref lanes);
				if (result != EResult.k_EResultOK) return false;

			int roundTrip = Math.Clamp(status.m_nPing, 0, int.MaxValue);
			if (_recentRoundTrips.Count == LinkQualitySamples) _recentRoundTrips.Dequeue();
			_recentRoundTrips.Enqueue(roundTrip);

			double mean = 0;
			foreach (int sample in _recentRoundTrips) mean += sample;
			mean /= _recentRoundTrips.Count;

			double variance = 0;
			foreach (int sample in _recentRoundTrips) variance += (sample - mean) * (sample - mean);
			variance /= _recentRoundTrips.Count;

			float qualityPercent = Math.Clamp((1f - status.m_flConnectionQualityRemote) * 100f, 0f, 100f);
			statistics = new PeerLinkStatistics(roundTrip, (int)Math.Round(Math.Sqrt(variance)), qualityPercent);
			return true;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[SteamNetwork] the connection's quality could not be read: {exception.Message}");
			return false;
		}
	}

	/// <summary>Ends the connection and states the reason once, for whatever the screen is showing.</summary>
	/// <param name="reason">A player-readable reason the connection is over.</param>
	public void Close(string reason)
	{
		if (_disposed) return;

		HSteamNetConnection connection = _connection;
		_connection = HSteamNetConnection.Invalid;
		_state = ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_None;

		if (connection != HSteamNetConnection.Invalid)
		{
			try
			{
				SteamNetworkingSockets.CloseConnection(connection, 0, "realm closed the connection", false);
			}
			catch (Exception exception)
			{
				GameLog.LogError($"[SteamNetwork] the connection could not be closed: {exception.Message}");
			}
		}

		Closed?.Invoke(reason);
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		Close("the connection was given up");
		_connectionStatusCallback.Dispose();
	}
}
