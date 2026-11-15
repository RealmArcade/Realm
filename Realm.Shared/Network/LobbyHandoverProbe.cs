using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Realm.Shared.Network;

/// <summary>Which datagram of the handover probe handshake a packet is.</summary>
public enum LobbyHandoverProbeKind : byte
{
	/// <summary>A screen states that it is listening and asks the lobby's relay to record the endpoint it arrived from.</summary>
	Register = 1,

	/// <summary>The relay states that the screen is registered, and which endpoint it was seen at.</summary>
	Registered = 2,

	/// <summary>A screen asks the relay to introduce it to another seated screen.</summary>
	RequestPunch = 3,

	/// <summary>The relay states where a screen was seen, so the two screens can open a path to each other.</summary>
	PeerEndpoint = 4,

	/// <summary>A screen sends one datagram to open its path, and asks nothing of the peer beyond an answer.</summary>
	Punch = 5,

	/// <summary>A timed probe datagram, answered by a <see cref="Pong" /> that echoes its nonce.</summary>
	Ping = 6,

	/// <summary>The answer to a probe or a punch, echoing the nonce it was asked with.</summary>
	Pong = 7
}

/// <summary>
///     The fixed datagram the handover probe handshake is spoken in. It is deliberately small and fixed: a probe is not
///     allowed to be a way to make the lobby's authority move bytes on somebody else's behalf, so every answer is the same
///     size as the question and carries nothing but the tokens, the nonce and the endpoint it is about.
/// </summary>
public static class LobbyHandoverProbeWire
{
	/// <summary>The longest token a datagram carries, which is the length of a session token the lobby already uses.</summary>
	public const int MaximumTokenBytes = 64;

	/// <summary>Every datagram of this handshake is exactly this many bytes.</summary>
	public const int DatagramBytes = 176;

	private const uint Magic = 0x31504852;

	private const int KindOffset = 4;
	private const int TokenOffset = 8;
	private const int TargetTokenOffset = 72;
	private const int NonceOffset = 136;
	private const int FamilyOffset = 144;
	private const int AddressOffset = 148;
	private const int PortOffset = 164;

	/// <summary>Writes one datagram of the handshake.</summary>
	/// <param name="datagram">The buffer to write, which must be <see cref="DatagramBytes" /> long.</param>
	/// <param name="kind">Which datagram it is.</param>
	/// <param name="token">The token of the screen sending it.</param>
	/// <param name="targetToken">The token the datagram is about, or empty for one that concerns the sender alone.</param>
	/// <param name="nonce">The nonce a probe or an answer echoes, or zero.</param>
	/// <param name="endpoint">The endpoint the datagram is about, or null for none.</param>
	/// <returns>Whether the datagram was written.</returns>
	public static bool TryWrite(Span<byte> datagram, LobbyHandoverProbeKind kind, string token, string targetToken, long nonce, IPEndPoint? endpoint)
	{
		if (datagram.Length < DatagramBytes) return false;

		datagram.Clear();
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(datagram, Magic);
		datagram[KindOffset] = (byte)kind;
		WriteToken(datagram.Slice(TokenOffset, MaximumTokenBytes), token);
		WriteToken(datagram.Slice(TargetTokenOffset, MaximumTokenBytes), targetToken);
		System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(datagram.Slice(NonceOffset), nonce);

		if (endpoint == null) return true;

		IPAddress address = endpoint.Address;
		if (address.AddressFamily == AddressFamily.InterNetwork)
		{
			datagram[FamilyOffset] = 4;
			address.TryWriteBytes(datagram.Slice(AddressOffset, 4), out _);
		}
		else
		{
			if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
			if (address.AddressFamily == AddressFamily.InterNetwork)
			{
				datagram[FamilyOffset] = 4;
				address.TryWriteBytes(datagram.Slice(AddressOffset, 4), out _);
			}
			else
			{
				datagram[FamilyOffset] = 6;
				address.TryWriteBytes(datagram.Slice(AddressOffset, 16), out _);
			}
		}

		System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(datagram.Slice(PortOffset), (ushort)endpoint.Port);
		return true;
	}

	/// <summary>Reads one datagram of the handshake.</summary>
	/// <param name="datagram">The bytes that arrived.</param>
	/// <param name="kind">Which datagram it is.</param>
	/// <param name="token">The token of the screen that sent it.</param>
	/// <param name="targetToken">The token the datagram is about, or empty for none.</param>
	/// <param name="nonce">The nonce a probe or an answer echoes, or zero.</param>
	/// <param name="endpoint">The endpoint the datagram is about, or null for none.</param>
	/// <returns>Whether the datagram is one of ours.</returns>
	public static bool TryRead(ReadOnlySpan<byte> datagram, out LobbyHandoverProbeKind kind, out string token, out string targetToken, out long nonce, out IPEndPoint? endpoint)
	{
		kind = default;
		token = string.Empty;
		targetToken = string.Empty;
		nonce = 0;
		endpoint = null;

		if (datagram.Length < DatagramBytes) return false;
		if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(datagram) != Magic) return false;

		kind = (LobbyHandoverProbeKind)datagram[KindOffset];
		token = ReadToken(datagram.Slice(TokenOffset, MaximumTokenBytes));
		targetToken = ReadToken(datagram.Slice(TargetTokenOffset, MaximumTokenBytes));
		nonce = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(datagram.Slice(NonceOffset));

		byte family = datagram[FamilyOffset];
		if (family == 4)
		{
			var address = new IPAddress(datagram.Slice(AddressOffset, 4));
			endpoint = new IPEndPoint(address, System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(PortOffset)));
		}
		else if (family == 6)
		{
			var address = new IPAddress(datagram.Slice(AddressOffset, 16));
			endpoint = new IPEndPoint(address, System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(PortOffset)));
		}

		return true;
	}

	private static void WriteToken(Span<byte> destination, string token)
	{
		destination.Clear();
		if (string.IsNullOrEmpty(token)) return;

		int bytes = Encoding.UTF8.GetBytes(token, destination);
		_ = bytes;
	}

	private static string ReadToken(ReadOnlySpan<byte> source)
	{
		int length = source.IndexOf((byte)0);
		if (length < 0) length = source.Length;
		return length == 0 ? string.Empty : Encoding.UTF8.GetString(source.Slice(0, length));
	}

	/// <summary>
	///     States an endpoint the way a log line wants it, or a dash when the datagram carried none.
	/// </summary>
	/// <param name="endpoint">The endpoint, or null.</param>
	/// <returns>The endpoint as text.</returns>
	public static string Describe(IPEndPoint? endpoint) => endpoint == null ? "-" : $"{endpoint.Address}:{endpoint.Port}";
}

/// <summary>
///     One screen's half of the handover probe handshake: it registers with the lobby's relay, introduces itself to a
///     named seat, and times a bounded exchange with it. Nothing here reserves a seat, joins a lobby or changes a lobby's
///     state, so a probe that fails costs the lobby nothing.
/// </summary>
public sealed class LobbyHandoverProbeService : IDisposable
{
	private const int PingIntervalMilliseconds = 20;
	private const int RegistrationRetryMilliseconds = 400;
	private const int PunchTimeoutMilliseconds = 6000;

	private readonly Socket _rawSocket;

	private readonly IPEndPoint _relayEndpoint;

	private readonly string _ownToken;

	private readonly Thread _receiveThread;

	private readonly ConcurrentDictionary<long, long> _sentProbes = new();

	private readonly ConcurrentDictionary<string, TaskCompletionSource<IPEndPoint>> _waitingPeers = new(StringComparer.Ordinal);

	private volatile bool _disposed;

	private volatile bool _registered;

	private volatile string _observedEndpoint = string.Empty;

	private long _nextNonce;

	private LobbyHandoverProbeService(Socket socket, IPEndPoint relayEndpoint, string ownToken)
	{
		_rawSocket = socket;
		_relayEndpoint = relayEndpoint;
		_ownToken = ownToken;
		_receiveThread = new Thread(ReceiveLoop)
		{
			IsBackground = true,
			Name = "RealmHandoverProbe"
		};
		_receiveThread.Start();
	}

	/// <summary>Whether the lobby's relay has recorded this screen's endpoint.</summary>
	public bool IsRegistered => _registered;

	/// <summary>The endpoint the relay saw this screen at, or empty while it has not registered.</summary>
	public string ObservedEndpoint => _observedEndpoint;

	/// <summary>
	///     Opens a probe socket and starts registering it with a lobby's relay. A screen that cannot open a socket states
	///     why, so a lobby that cannot be measured says so rather than showing an empty measurement as a good one.
	/// </summary>
	/// <param name="relayAddress">The address of the lobby's probe relay.</param>
	/// <param name="relayPort">The port of the lobby's probe relay.</param>
	/// <param name="ownToken">The token this screen proves itself with, which is its own lobby session token.</param>
	/// <param name="failure">Why no probe socket could be opened.</param>
	/// <returns>The probe service, or null when the socket could not be opened.</returns>
	public static LobbyHandoverProbeService? TryStart(IPAddress relayAddress, int relayPort, string ownToken, out string failure)
	{
		failure = string.Empty;
		if (relayPort <= 0 || relayPort > 65535)
		{
			failure = "The lobby stated no handover probe relay port.";
			return null;
		}

		if (string.IsNullOrEmpty(ownToken))
		{
			failure = "This seat holds no session token, so the probe relay cannot tell which seat is measuring.";
			return null;
		}

		var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
		try
		{
			socket.DualMode = true;
			socket.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
		}
		catch (SocketException exception)
		{
			socket.Dispose();
			failure = $"A handover probe socket could not be opened: {exception.Message}";
			return null;
		}

		IPAddress mapped = relayAddress.AddressFamily == AddressFamily.InterNetwork ? relayAddress.MapToIPv6() : relayAddress;
		return new LobbyHandoverProbeService(socket, new IPEndPoint(mapped, relayPort), ownToken);
	}

	/// <summary>
	///     Measures one bounded exchange with a named seat. The measurement proves the path in both directions: this screen
	///     reaches the other, and the answers prove the other reaches this one.
	/// </summary>
	/// <param name="targetToken">The token of the seat being measured.</param>
	/// <param name="targetSlot">The slot of the seat being measured, stated in the answer.</param>
	/// <param name="budget">How long the exchange runs before it is summarised.</param>
	/// <returns>What the exchange measured, stated as measured or not measured.</returns>
	public async Task<LobbyHandoverLinkSample> MeasureAsync(string targetToken, int targetSlot, TimeSpan budget)
	{
		var sample = new LobbyHandoverLinkSample { TargetSlot = targetSlot };
		if (string.IsNullOrEmpty(targetToken) || _disposed) return sample;

		if (!await WaitForRegistrationAsync(budget).ConfigureAwait(false)) return sample;

		IPEndPoint? peerEndpoint = await AskForPeerEndpointAsync(targetToken, budget).ConfigureAwait(false);
		if (peerEndpoint == null) return sample;

		byte[] datagram = new byte[LobbyHandoverProbeWire.DatagramBytes];
		LobbyHandoverProbeWire.TryWrite(datagram, LobbyHandoverProbeKind.Punch, _ownToken, targetToken, 0, null);
		Send(datagram, peerEndpoint);

		var roundTrips = new List<int>(64);
		int sent = 0;
		long deadline = Environment.TickCount64 + (long)budget.TotalMilliseconds;
		while (Environment.TickCount64 < deadline && !_disposed)
		{
			long nonce = Interlocked.Increment(ref _nextNonce);
			_sentProbes[nonce] = Stopwatch.GetTimestamp();
			LobbyHandoverProbeWire.TryWrite(datagram, LobbyHandoverProbeKind.Ping, _ownToken, targetToken, nonce, null);
			if (Send(datagram, peerEndpoint)) sent++;

			await Task.Delay(PingIntervalMilliseconds).ConfigureAwait(false);

			// Answers are taken in the order they arrived, which is what the jitter of a link is measured from.
			while (_answers.TryDequeue(out ProbeAnswer answer))
			{
				roundTrips.Add(answer.RoundTripMilliseconds);
			}
		}

		_answers.Clear();
		_sentProbes.Clear();

		if (roundTrips.Count == 0) return sample;

		long total = 0;
		long jitterTotal = 0;
		for (int index = 0; index < roundTrips.Count; index++)
		{
			total += roundTrips[index];
			if (index == 0) continue;
			jitterTotal += Math.Abs(roundTrips[index] - roundTrips[index - 1]);
		}

		sample.Measured = true;
		sample.Samples = roundTrips.Count;
		sample.RoundTripMilliseconds = (int)Math.Round((double)total / roundTrips.Count);
		sample.JitterMilliseconds = roundTrips.Count > 1 ? (int)Math.Round((double)jitterTotal / (roundTrips.Count - 1)) : 0;
		sample.PacketLossPercent = sent == 0 ? 100f : (float)Math.Round(100.0 * (sent - roundTrips.Count) / sent, 1);
		if (sample.PacketLossPercent < 0f) sample.PacketLossPercent = 0f;
		return sample;
	}

	private readonly ConcurrentQueue<ProbeAnswer> _answers = new();

	private readonly record struct ProbeAnswer(int RoundTripMilliseconds);

	private async Task<bool> WaitForRegistrationAsync(TimeSpan budget)
	{
		long deadline = Environment.TickCount64 + (long)budget.TotalMilliseconds;
		byte[] datagram = new byte[LobbyHandoverProbeWire.DatagramBytes];
		while (!_registered && Environment.TickCount64 < deadline && !_disposed)
		{
			LobbyHandoverProbeWire.TryWrite(datagram, LobbyHandoverProbeKind.Register, _ownToken, string.Empty, 0, null);
			Send(datagram, _relayEndpoint);
			await Task.Delay(RegistrationRetryMilliseconds).ConfigureAwait(false);
		}

		return _registered;
	}

	private async Task<IPEndPoint?> AskForPeerEndpointAsync(string targetToken, TimeSpan budget)
	{
		var waiting = new TaskCompletionSource<IPEndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
		_waitingPeers[targetToken] = waiting;

		byte[] datagram = new byte[LobbyHandoverProbeWire.DatagramBytes];
		LobbyHandoverProbeWire.TryWrite(datagram, LobbyHandoverProbeKind.RequestPunch, _ownToken, targetToken, 0, null);
		Send(datagram, _relayEndpoint);

		Task completed = await Task.WhenAny(waiting.Task, Task.Delay(Math.Min(PunchTimeoutMilliseconds, (int)budget.TotalMilliseconds))).ConfigureAwait(false);
		_waitingPeers.TryRemove(targetToken, out _);
		return completed == waiting.Task ? waiting.Task.Result : null;
	}

	private void ReceiveLoop()
	{
		var buffer = new byte[LobbyHandoverProbeWire.DatagramBytes * 2];
		EndPoint remote = new IPEndPoint(IPAddress.IPv6Any, 0);

		while (!_disposed)
		{
			try
			{
				_rawSocket.ReceiveTimeout = 250;
				int received = _rawSocket.ReceiveFrom(buffer, ref remote);
				if (received <= 0) continue;
				if (!LobbyHandoverProbeWire.TryRead(buffer.AsSpan(0, received), out LobbyHandoverProbeKind kind, out string token, out string targetToken, out long nonce, out IPEndPoint? endpoint)) continue;
				if (remote is not IPEndPoint remoteEndpoint) continue;

				switch (kind)
				{
					case LobbyHandoverProbeKind.Registered:
						_observedEndpoint = LobbyHandoverProbeWire.Describe(endpoint);
						_registered = true;
						break;
					case LobbyHandoverProbeKind.PeerEndpoint:
						if (endpoint != null && _waitingPeers.TryGetValue(targetToken, out TaskCompletionSource<IPEndPoint>? waiting)) waiting.TrySetResult(endpoint);
						break;
					case LobbyHandoverProbeKind.Punch:
					case LobbyHandoverProbeKind.Ping:
						Answer(remoteEndpoint, token, nonce);
						break;
					case LobbyHandoverProbeKind.Pong:
						if (_sentProbes.TryRemove(nonce, out long sentTimestamp))
						{
							long elapsed = Stopwatch.GetTimestamp() - sentTimestamp;
							int roundTrip = (int)Math.Round(elapsed * 1000.0 / Stopwatch.Frequency);
							_answers.Enqueue(new ProbeAnswer(roundTrip));
						}

						break;
				}
			}
			catch (SocketException)
			{
			}
			catch (ObjectDisposedException)
			{
				return;
			}
		}
	}

	private void Answer(IPEndPoint remote, string peerToken, long nonce)
	{
		Span<byte> datagram = stackalloc byte[LobbyHandoverProbeWire.DatagramBytes];
		LobbyHandoverProbeWire.TryWrite(datagram, LobbyHandoverProbeKind.Pong, _ownToken, peerToken, nonce, null);
		Send(datagram, remote);
	}

	private bool Send(ReadOnlySpan<byte> datagram, IPEndPoint endpoint)
	{
		try
		{
			IPAddress address = endpoint.Address;
			if (address.AddressFamily == AddressFamily.InterNetwork) address = address.MapToIPv6();
			return _rawSocket.SendTo(datagram, new IPEndPoint(address, endpoint.Port)) == datagram.Length;
		}
		catch (SocketException)
		{
			return false;
		}
		catch (ObjectDisposedException)
		{
			return false;
		}
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		foreach (KeyValuePair<string, TaskCompletionSource<IPEndPoint>> pair in _waitingPeers) pair.Value.TrySetCanceled();
		try
		{
			_rawSocket.Dispose();
		}
		catch (ObjectDisposedException)
		{
		}

		if (_receiveThread.IsAlive) _receiveThread.Join(500);
	}
}
