using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Realm.Shared.Network.Relay;

/// <summary>
///     The stable local identity a connection keeps while its paths change. ENet speaks to one loopback socket and nothing
///     else, so a datagram is carried by whichever volunteer is best at that moment without ENet ever noticing: the port it
///     connected to, the peer it admitted and the baseline it is holding all stay exactly where they were when a path
///     appears, fails or is removed. On the authority one shim stands for one joining player, which is what keeps every
///     relayed player a distinct peer there.
/// </summary>
public sealed class RelayUdpShim : IRelayPathSink, IDisposable
{
	/// <summary>The widest datagram the transport on this side may hand the shim, which is what the path layer carries.</summary>
	public const int MaximumInnerDatagramBytes = RelayEnvelope.MaximumInnerDatagramBytes;

	private const int LaneSleepMilliseconds = 1;

	private const int DescriptionIntervalMilliseconds = 1000;

	private readonly RelayPathMux _mux;
	private readonly Socket _socket;
	private readonly int _innerPort;
	private readonly byte[] _innerBuffer = new byte[MaximumInnerDatagramBytes];
	private EndPoint _innerEndpoint = new IPEndPoint(IPAddress.Any, 0);
	private EndPoint _anySender = new IPEndPoint(IPAddress.Any, 0);
	private Thread? _lane;
	private CancellationTokenSource? _cancellation;
	private string _description = string.Empty;
	private string _laneFailure = string.Empty;
	private RelayPathSummary _summary = RelayPathSummary.Empty;
	private long _lastDescriptionMilliseconds;
	private int _deliveredToTransport;
	private int _refusedBeforeTransportWasKnown;
	private bool _disposed;

	/// <summary>Creates one shim for one relayed connection.</summary>
	/// <param name="mux">The connection's path layer, which the caller fills with paths before starting the shim.</param>
	/// <param name="innerPort">
	///     The loopback port of the transport this shim speaks to. A shim on the authority states the game port its own ENet
	///     server listens on; a shim on a screen states zero and learns the port from the first datagram its own ENet peer
	///     sends, because that peer's local port is chosen by the operating system.
	/// </param>
	public RelayUdpShim(RelayPathMux mux, int innerPort)
	{
		_mux = mux;
		_innerPort = Math.Max(0, innerPort);
		_socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

		// A shim in front of the authority knows the port it forwards to, so its endpoint is built once here rather than
		// once per datagram: nothing on this lane allocates for a packet it carries.
		if (_innerPort > 0) _innerEndpoint = new IPEndPoint(IPAddress.Loopback, _innerPort);
	}

	/// <summary>The loopback port the transport speaks to, which a screen hands to its connection attempt.</summary>
	public int Port { get; private set; }

	/// <summary>Whether the shim owns a lane and is moving datagrams.</summary>
	public bool IsRunning => _lane is { IsAlive: true };

	/// <summary>
	///     Why this connection's lane stopped, or empty while it is running. A lane that died has to be stated rather than
	///     left looking like a connection that is carrying: the path it stands for is not moving anything, and its owner
	///     gives the connection up on this.
	/// </summary>
	public string LaneFailure => Volatile.Read(ref _laneFailure);

	/// <summary>How many inner datagrams the shim has handed its own transport.</summary>
	public int DeliveredToTransport => _deliveredToTransport;

	/// <summary>How many datagrams arrived from the relay before the transport's own port was known.</summary>
	public int RefusedBeforeTransportWasKnown => _refusedBeforeTransportWasKnown;

	/// <summary>
	///     Opens the loopback socket the transport speaks to. It is bound before anything is announced, because the port it
	///     takes is what a screen connects to and therefore what it must state first.
	/// </summary>
	/// <param name="failure">Why no socket was opened.</param>
	/// <returns>Whether the shim is holding a loopback port.</returns>
	public bool TryOpen(out string failure)
	{
		failure = string.Empty;
		if (Port > 0) return true;

		try
		{
			_socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
			_socket.Blocking = true;
			_socket.ReceiveTimeout = LaneSleepMilliseconds;
		}
		catch (SocketException exception)
		{
			failure = $"a loopback socket for the relayed connection could not be held: {exception.Message}";
			return false;
		}

		Port = ((IPEndPoint)_socket.LocalEndPoint!).Port;
		return true;
	}

	/// <summary>
	///     Starts the lane that carries this connection. It is one thread per shim, owned by the shim, and it is the only
	///     place the path layer and the loopback socket are touched after this returns.
	/// </summary>
	/// <param name="failure">Why no lane was started.</param>
	/// <returns>Whether the connection is being carried.</returns>
	public bool Start(out string failure)
	{
		failure = string.Empty;
		if (!TryOpen(out failure)) return false;
		if (IsRunning) return true;

		_cancellation = new CancellationTokenSource();
		var thread = new Thread(() => Run(_cancellation.Token))
		{
			IsBackground = true,
			Name = $"RelayPathShim-{Port}"
		};

		thread.Start();
		_lane = thread;
		return true;
	}

	/// <summary>States which paths this connection has and what each of them measured, read from the lane's last snapshot.</summary>
	/// <returns>One line per path and a line for the connection.</returns>
	public string Describe() => Volatile.Read(ref _description);

	/// <summary>
	///     The paths this connection held the last time its lane stated them, and what the best of them measured. The lane
	///     publishes a new value once a second and nothing else writes it, so a screen reads one immutable statement rather
	///     than fields another lane is measuring.
	/// </summary>
	public RelayPathSummary Summary => Volatile.Read(ref _summary);

	/// <summary>Hands one inner datagram from the relayed path to the transport this shim speaks to.</summary>
	/// <param name="inner">The datagram the peer sent.</param>
	/// <param name="delivery">The guarantee the peer stated for it.</param>
	public void OnRelayDatagram(ReadOnlySpan<byte> inner, FrameDelivery delivery)
	{
		if (_innerEndpoint is IPEndPoint endpoint && endpoint.Port == 0)
		{
			_refusedBeforeTransportWasKnown++;
			return;
		}

		try
		{
			if (_socket.SendTo(inner, SocketFlags.None, _innerEndpoint) != inner.Length)
			{
				_refusedBeforeTransportWasKnown++;
				return;
			}
		}
		catch (SocketException)
		{
			_refusedBeforeTransportWasKnown++;
			return;
		}

		_deliveredToTransport++;
	}

	private void Run(CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			try
			{
				DrainTransport();
				_mux.Poll(this);
				PublishDescriptionIfDue();
				if (_socket.Available == 0) Thread.Sleep(LaneSleepMilliseconds);
			}
			catch (ObjectDisposedException)
			{
				// The connection was given up between the check above and this lane's own socket, which is the ordinary end
				// of a shim whose owner closed it. An exception escaping this thread would take the process with it.
				return;
			}
			catch (Exception exception)
			{
				// An exception escaping this thread would take the process with it, and the connection this lane carries would
				// be a path nothing is moving while every report said it was there. It is recorded instead, so the owner gives
				// the connection up and the line it writes names the failure rather than only the silence.
				Volatile.Write(ref _laneFailure, $"{exception.GetType().Name}: {exception.Message}");
				return;
			}
		}
	}

	private void DrainTransport()
	{
		while (_socket.Available > 0)
		{
			EndPoint sender = _anySender;
			int length;
			try
			{
				length = _socket.ReceiveFrom(_innerBuffer, SocketFlags.None, ref sender);
			}
			catch (SocketException)
			{
				return;
			}

			if (length <= 0) continue;

			if (_innerPort == 0 && _innerEndpoint is IPEndPoint known && known.Port == 0)
			{
				_innerEndpoint = sender;
			}

			_mux.Send(_innerBuffer.AsSpan(0, length), FrameDelivery.UnreliableOrdered);
		}
	}

	private void PublishDescriptionIfDue()
	{
		long now = Environment.TickCount64;
		if (now - _lastDescriptionMilliseconds < DescriptionIntervalMilliseconds) return;

		_lastDescriptionMilliseconds = now;
		Volatile.Write(ref _description, $"relay shim port={Port} delivered={_deliveredToTransport} dropped={_refusedBeforeTransportWasKnown}\n{_mux.Describe()}");
		Volatile.Write(ref _summary, new RelayPathSummary(_mux.PathCount, _mux.BestPath, _mux.Samples()));
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		_cancellation?.Cancel();
		_lane?.Join(250);
		_cancellation?.Dispose();
		_cancellation = null;
		_lane = null;

		_socket.Dispose();
		_mux.Dispose();
	}
}
