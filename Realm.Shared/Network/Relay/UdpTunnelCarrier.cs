using System;
using System.Net;
using System.Net.Sockets;

namespace Realm.Shared.Network.Relay;

/// <summary>
///     Carries one connection's relayed datagrams through a volunteer over plain UDP. It is the default carrier: the same
///     registry acknowledgement and hole punch that lets a host be reached let a volunteer be reached, and a datagram it
///     takes is moved as bytes it cannot read.
/// </summary>
public sealed class UdpTunnelCarrier : IRelayCarrier
{
	private const int ProbeIntervalMilliseconds = 500;

	private readonly Socket _socket;
	private readonly EndPoint _relayEndPoint;
	private EndPoint _anySender;

	private int _sent;
	private int _received;
	private int _refused;
	private long _bytes;

	private UdpTunnelCarrier(Socket socket, EndPoint relayEndPoint, byte pathId, int maximumPayloadBytes)
	{
		_socket = socket;
		_relayEndPoint = relayEndPoint;
		_anySender = new IPEndPoint(IPAddress.Any, 0);
		PathId = pathId;
		Capabilities = new RelayCarrierCapabilities(
			RelayCarrierKind.UdpTunnel,
			maximumPayloadBytes,
			GuaranteesDelivery: false,
			ProvidesOwnEncryption: false,
			SupportsIndependentPaths: true,
			SetupCostMilliseconds: 0);
	}

	public RelayCarrierCapabilities Capabilities { get; }

	public byte PathId { get; }

	public bool IsOpen => _socket.IsBound;

	public RelayCarrierTraffic Traffic => new(_sent, _received, _refused, _bytes);

	/// <summary>How often this carrier asks the volunteer to prove the path is still there.</summary>
	public static int ProbeInterval => ProbeIntervalMilliseconds;

	/// <summary>
	///     Opens a tunnel toward one volunteer. The local socket is bound before anything is sent so the endpoint the
	///     volunteer answers back to is the endpoint this side keeps, which is what makes a carrier survive the volunteer
	///     answering from a different outside port than it was reached at.
	/// </summary>
	/// <param name="relayAddress">The address the volunteer is reached at.</param>
	/// <param name="relayPort">The port the volunteer was reached at.</param>
	/// <param name="pathId">The path identifier this carrier's datagrams are sealed under.</param>
	/// <param name="maximumPayloadBytes">The most one datagram may hold, which the volunteer stated.</param>
	/// <param name="carrier">The carrier that was opened.</param>
	/// <param name="failure">Why no carrier was opened.</param>
	/// <returns>Whether a tunnel was opened.</returns>
	public static bool TryOpen(string relayAddress, int relayPort, byte pathId, int maximumPayloadBytes, out UdpTunnelCarrier? carrier, out string failure)
	{
		carrier = null;
		failure = string.Empty;

		if (string.IsNullOrWhiteSpace(relayAddress) || relayPort <= 0)
		{
			failure = "the volunteer was named without an address to reach it at";
			return false;
		}

		if (maximumPayloadBytes < RelayEnvelope.HeaderBytes)
		{
			failure = $"the volunteer stated room for {maximumPayloadBytes} bytes, which is less than the path layer's own header";
			return false;
		}

		IPAddress[] addresses;
		try
		{
			addresses = IPAddress.TryParse(relayAddress, out IPAddress? parsed) ? [parsed] : Dns.GetHostAddresses(relayAddress);
		}
		catch (SocketException exception)
		{
			failure = $"{relayAddress} could not be resolved: {exception.Message}";
			return false;
		}

		if (addresses.Length == 0)
		{
			failure = $"{relayAddress} resolved to no address";
			return false;
		}

		var socket = new Socket(addresses[0].AddressFamily, SocketType.Dgram, ProtocolType.Udp);
		try
		{
			socket.Bind(new IPEndPoint(addresses[0].AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0));
			socket.Blocking = false;
		}
		catch (SocketException exception)
		{
			socket.Dispose();
			failure = $"a local socket for the volunteer could not be held: {exception.Message}";
			return false;
		}

		carrier = new UdpTunnelCarrier(socket, new IPEndPoint(addresses[0], relayPort), pathId, maximumPayloadBytes);
		return true;
	}

	public bool TrySend(ReadOnlySpan<byte> datagram)
	{
		if (!_socket.IsBound) return false;
		if (!Capabilities.Fits(datagram.Length))
		{
			_refused++;
			return false;
		}

		try
		{
			int sent = _socket.SendTo(datagram, SocketFlags.None, _relayEndPoint);
			if (sent != datagram.Length)
			{
				_refused++;
				return false;
			}
		}
		catch (SocketException)
		{
			_refused++;
			return false;
		}

		_sent++;
		_bytes += datagram.Length;
		return true;
	}

	public bool TryReceive(Span<byte> destination, out int length)
	{
		length = 0;
		if (!_socket.IsBound || _socket.Available <= 0) return false;

		try
		{
			length = _socket.ReceiveFrom(destination, SocketFlags.None, ref _anySender);
		}
		catch (SocketException)
		{
			length = 0;
			return false;
		}

		if (length <= 0)
		{
			length = 0;
			return false;
		}

		_received++;
		_bytes += length;
		return true;
	}

	public void Dispose()
	{
		_socket.Dispose();
	}
}
