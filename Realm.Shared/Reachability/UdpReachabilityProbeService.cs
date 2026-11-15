using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Realm.Shared.Reachability;

/// <summary>One endpoint a check is sent to: an address with the game port the host stated.</summary>
public readonly record struct ProbeTarget(IPAddress Address, int Port);

/// <summary>
///     Runs a registry node's half of a join rehearsal: it sends the opening packet of an ENet connection attempt to the
///     endpoint a host advertised, from a socket of its own, and records whether the host's game port answered it and from
///     which public endpoint. A host behind a router that drops unsolicited traffic answers once it has punched this
///     socket first, which is exactly the path a real peer takes, so a lobby is never refused for relying on hole
///     punching and is refused when neither route works.
/// </summary>
/// <remarks>
///     The endpoint a host is reachable at is the one its punch arrived from, not the port it listens on: a router that
///     translates the game port hands out an external port the host cannot measure and its own STUN query cannot see, so a
///     check that only probed the advertised port would be refused on every such router while passing on a node that sits
///     on the host's own network and reaches it directly.
/// </remarks>
public class UdpReachabilityProbeService : IDisposable
{
	public const int DefaultProbePort = 8998;

	/// <summary>
	///     How long a check keeps probing unless the caller asks for something else. It is long enough for a host behind a
	///     router that drops unsolicited packets to punch and for the next probe to arrive afterwards, which is the whole
	///     point of checking with a punch rather than by a single unsolicited datagram.
	/// </summary>
	public const int DefaultWindowMilliseconds = 3000;

	/// <summary>A window shorter than this leaves no room for a punch to land before the last probe, so it is never used.</summary>
	public const int MinimumWindowMilliseconds = 1500;

	public const int MaximumWindowMilliseconds = 15000;

	/// <summary>
	///     How many checks this node runs at once. The route that starts one takes no credentials, so the work one caller
	///     can make the node do is bounded here rather than by who is calling.
	/// </summary>
	public const int MaximumLiveProbes = 512;

	/// <summary>How many checks one source may have running at once, which is more than a host retrying registration needs.</summary>
	public const int MaximumLiveProbesPerSource = 8;

	private const int AttemptIntervalMilliseconds = 250;

	private const int PendingLifetimeMilliseconds = 120_000;

	private const int ReceivedDatagramBytes = 2048;

	private readonly int _requestedPort;
	private readonly string _statedPublicIp;
	private readonly ConcurrentDictionary<string, PendingProbe> _pending = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<uint, string> _pendingByConnectId = new();
	private readonly Stopwatch _clock = Stopwatch.StartNew();
	private CancellationTokenSource? _stop;
	private UdpClient? _socket;
	private Task? _receiveLoop;
	private bool _disposed;

	public UdpReachabilityProbeService(int probePort = DefaultProbePort, string statedPublicIp = "")
	{
		_requestedPort = probePort;
		_statedPublicIp = statedPublicIp ?? string.Empty;
	}

	/// <summary>The port this service probes from, which is the port a host is told to punch.</summary>
	public int ProbePort { get; private set; }

	public bool IsRunning => _socket != null;

	/// <summary>
	///     Binds the probe socket. It is bound on first use so a node that never registers a lobby pays nothing, and a node
	///     whose port is taken says so once rather than per lobby.
	/// </summary>
	public bool TryStart(out string failure)
	{
		failure = string.Empty;
		if (_socket != null) return true;
		if (_disposed)
		{
			failure = "this probe service has been disposed";
			return false;
		}

		try
		{
			var socket = new UdpClient(AddressFamily.InterNetwork);
			socket.Client.ExclusiveAddressUse = true;
			socket.Client.Bind(new IPEndPoint(IPAddress.Any, _requestedPort));
			ProbePort = ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
			_socket = socket;
			_stop = new CancellationTokenSource();
			_receiveLoop = Task.Run(() => ReceiveAsync(socket, _stop.Token));
			return true;
		}
		catch (SocketException exception)
		{
			failure = $"a UDP probe socket could not be bound on port {_requestedPort}: {exception.Message}";
			return false;
		}
	}

	/// <summary>
	///     Starts a check of the game port a host is about to advertise. The port is the host's claim and is always used,
	///     because it is the port a joining player will punch and connect to; the address is checked both as the host stated
	///     it and as this node sees the host's own requests arrive, so a host whose measurement of its own address is wrong is
	///     still measured correctly instead of being refused for it.
	/// </summary>
	public ReachabilityTicket Begin(IPAddress advertisedAddress, int advertisedPort, IPAddress requestSourceAddress, int requestSourcePort, int windowMilliseconds = DefaultWindowMilliseconds)
	{
		if (!TryStart(out string failure)) throw new InvalidOperationException(failure);

		long now = _clock.ElapsedMilliseconds;
		ForgetStale(now);
		if (!HasCapacityFor(requestSourceAddress, now, out string refusal)) throw new InvalidOperationException(refusal);

		int window = Math.Clamp(windowMilliseconds <= 0 ? DefaultWindowMilliseconds : windowMilliseconds, MinimumWindowMilliseconds, MaximumWindowMilliseconds);
		ProbeTarget[] targets = BuildTargets(advertisedAddress, advertisedPort, requestSourceAddress);
		string probeId = Guid.NewGuid().ToString("N");
		uint connectId = (uint)Random.Shared.NextInt64(1, uint.MaxValue);

		var probe = new PendingProbe(probeId, connectId, targets, requestSourceAddress, requestSourcePort, now + window);
		_pending[probeId] = probe;
		_pendingByConnectId[connectId] = probeId;

		_ = Task.Run(() => RunProbeAsync(probe));
		return new ReachabilityTicket(probeId, _statedPublicIp, ProbePort, window);
	}

	/// <summary>
	///     Reports what a check has seen. A check that is still inside its window answers <see cref="ReachabilityStates.Waiting" />;
	///     one that is not, and has been asked for its result, is finished and forgotten so a node does not accumulate them.
	/// </summary>
	public ReachabilityReport? Read(string probeId)
	{
		ForgetStale();
		if (string.IsNullOrEmpty(probeId) || !_pending.TryGetValue(probeId, out PendingProbe? probe)) return null;

		long now = _clock.ElapsedMilliseconds;
		bool expired = probe.IsExpired(now);
		string state = probe.HasAnswered ? ReachabilityStates.Answered : expired ? ReachabilityStates.Silent : ReachabilityStates.Waiting;

		ReachabilityReport report = Describe(probe, state);

		// A check that answered is kept until the lobby it was run for is registered or the record ages out, because the
		// host has to read the answer and then register with it, and the registration is what takes the record.
		if (state == ReachabilityStates.Silent) Forget(probe);
		return report;
	}

	/// <summary>
	///     Reports the check a lobby names as one this node answered, and the endpoint it measured. A lobby that names a
	///     check this node did not run, or one that never answered, is refused with this method's false. The record is taken
	///     by the check that reads it, so one answered check publishes one lobby.
	/// </summary>
	public bool TryReadVerified(string probeId, out ReachabilityReport report)
	{
		report = null!;
		if (string.IsNullOrEmpty(probeId) || !_pending.TryGetValue(probeId, out PendingProbe? probe)) return false;
		if (!probe.HasAnswered) return false;

		report = Describe(probe, ReachabilityStates.Answered);
		Forget(probe);
		return true;
	}

	private bool HasCapacityFor(IPAddress requestSourceAddress, long now, out string refusal)
	{
		refusal = string.Empty;
		int live = 0;
		int liveFromSource = 0;

		foreach (KeyValuePair<string, PendingProbe> entry in _pending)
		{
			PendingProbe probe = entry.Value;
			if (probe.IsExpired(now)) continue;

			live++;
			if (probe.RequestSourceAddress.Equals(requestSourceAddress)) liveFromSource++;
		}

		if (liveFromSource >= MaximumLiveProbesPerSource)
		{
			refusal = $"this node is already checking {liveFromSource} lobbies for {requestSourceAddress}; try again when one has finished";
			return false;
		}

		if (live >= MaximumLiveProbes)
		{
			refusal = $"this node is already checking {live} lobbies, which is as many as it checks at once; try again shortly";
			return false;
		}

		return true;
	}

	private static ReachabilityReport Describe(PendingProbe probe, string state)
	{
		ProbeTarget target = probe.AnsweringTarget;
		return new ReachabilityReport(
			probe.ProbeId,
			state,
			probe.HasAnswered,
			probe.ObservedAddress?.ToString() ?? string.Empty,
			probe.ObservedPort,
			target.Address.ToString(),
			target.Port,
			probe.RequestSourceAddress.ToString(),
			probe.Attempts);
	}

	/// <summary>
	///     The endpoints a check is sent to: the one the host stated, and the one this node can derive from the host's own
	///     requests. Only the port is taken from the host either way, because a joining player punches that port at whichever
	///     of the two addresses is the host's real one.
	/// </summary>
	public static ProbeTarget[] BuildTargets(IPAddress advertisedAddress, int advertisedPort, IPAddress requestSourceAddress)
	{
		IPAddress statedTarget = ChooseDestinationAddress(advertisedAddress, requestSourceAddress);
		if (statedTarget.Equals(requestSourceAddress)) return new[] { new ProbeTarget(statedTarget, advertisedPort) };
		return new[] { new ProbeTarget(statedTarget, advertisedPort), new ProbeTarget(requestSourceAddress, advertisedPort) };
	}

	/// <summary>
	///     The address a probe is sent to. Loopback, an unspecified address and an address the caller could not state all
	///     mean the same thing here: use the address the host's request came from.
	/// </summary>
	public static IPAddress ChooseDestinationAddress(IPAddress advertisedAddress, IPAddress requestSourceAddress)
	{
		if (advertisedAddress.Equals(IPAddress.Any) || advertisedAddress.Equals(IPAddress.None)) return requestSourceAddress;
		if (IPAddress.IsLoopback(advertisedAddress) && !IPAddress.IsLoopback(requestSourceAddress)) return requestSourceAddress;
		if (advertisedAddress.IsIPv6LinkLocal || advertisedAddress.IsIPv6SiteLocal) return requestSourceAddress;
		return advertisedAddress;
	}

	private async Task RunProbeAsync(PendingProbe probe)
	{
		while (!probe.HasAnswered && !probe.IsExpired(_clock.ElapsedMilliseconds))
		{
			SendAttempt(probe);
			await Task.Delay(AttemptIntervalMilliseconds).ConfigureAwait(false);
		}
	}

	private void SendAttempt(PendingProbe probe)
	{
		UdpClient? socket = _socket;
		if (socket == null) return;

		Span<byte> datagram = stackalloc byte[EnetConnectProbe.ConnectDatagramBytes];
		int written = EnetConnectProbe.WriteConnect(datagram, probe.ConnectId, probe.ConnectId ^ 0x5A5A5A5A, (ushort)(_clock.ElapsedMilliseconds & 0xFFFF));
		if (written == 0) return;

		try
		{
			ProbeTarget target = probe.NextTarget();
			socket.Send(datagram, new IPEndPoint(target.Address, target.Port));
		}
		catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
		{
		}
	}

	private async Task ReceiveAsync(UdpClient socket, CancellationToken cancellationToken)
	{
		var buffer = new byte[ReceivedDatagramBytes];
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				UdpReceiveResult received = await socket.ReceiveAsync(cancellationToken).ConfigureAwait(false);
				RecordDatagram(received.Buffer, received.RemoteEndPoint);
				ForgetStale();
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (ObjectDisposedException)
			{
				return;
			}
			catch (SocketException)
			{
			}
		}
	}

	private void RecordDatagram(ReadOnlySpan<byte> datagram, IPEndPoint remoteEndPoint)
	{
		if (EnetConnectProbe.TryReadVerifyConnect(datagram, out EnetConnectReply reply))
		{
			RecordAnswer(reply, remoteEndPoint);
			return;
		}

		RecordPunch(datagram, remoteEndPoint);
	}

	private void RecordAnswer(EnetConnectReply reply, IPEndPoint remoteEndPoint)
	{
		if (!_pendingByConnectId.TryGetValue(reply.ConnectId, out string? probeId)) return;
		if (!_pending.TryGetValue(probeId, out PendingProbe? probe)) return;

		probe.RecordAnswer(remoteEndPoint.Address, remoteEndPoint.Port);
	}

	/// <summary>
	///     Reads a punch and makes the endpoint it came from the one the check it names probes. The punch is what opened the
	///     path through the host's router, so the endpoint it arrived from is both where that host is reachable and the one
	///     endpoint the router will let a probe in at, and the attempt is sent at once rather than at the next tick.
	/// </summary>
	/// <remarks>
	///     The punch is matched by the check it names, never by the address it arrived from: two hosts behind one router, an
	///     office, or two screens on one machine reach this socket from the same public address, so an address match would
	///     hand one host's punch to another host's check and point that check at a game port that belongs to somebody else.
	/// </remarks>
	private void RecordPunch(ReadOnlySpan<byte> datagram, IPEndPoint remoteEndPoint)
	{
		if (!ReachabilityPunch.TryReadProbeId(datagram, out string probeId)) return;
		if (!_pending.TryGetValue(probeId, out PendingProbe? probe)) return;
		if (probe.IsExpired(_clock.ElapsedMilliseconds)) return;
		if (!probe.LearnPunchedEndpoint(remoteEndPoint.Address, remoteEndPoint.Port)) return;

		SendAttempt(probe);
	}

	private void ForgetStale() => ForgetStale(_clock.ElapsedMilliseconds);

	private void ForgetStale(long now)
	{
		foreach (KeyValuePair<string, PendingProbe> entry in _pending)
		{
			if (now - entry.Value.DeadlineMilliseconds < PendingLifetimeMilliseconds) continue;
			Forget(entry.Value);
		}
	}

	private void Forget(PendingProbe probe)
	{
		_pending.TryRemove(probe.ProbeId, out _);
		_pendingByConnectId.TryRemove(probe.ConnectId, out _);
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		_stop?.Cancel();
		_socket?.Dispose();
		_socket = null;

		try
		{
			_receiveLoop?.Wait(TimeSpan.FromSeconds(2));
		}
		catch (AggregateException)
		{
		}

		_stop?.Dispose();
		_stop = null;
		_receiveLoop = null;
		_pending.Clear();
		_pendingByConnectId.Clear();
	}

	private class PendingProbe
	{
		private readonly ProbeTarget[] _targets;
		private volatile bool _answered;
		private IPAddress? _observedAddress;
		private int _observedPort;
		private int _attempts;
		private PunchedTarget? _punchedTarget;

		/// <summary>
		///     The endpoint one punch arrived from, held as one value so a check can only ever be given an address and a port
		///     that came from the same datagram. Publishing the two halves separately let a punch that lost the race write its
		///     port over the port of the punch that won, which pointed the check at an endpoint no punch ever came from.
		/// </summary>
		private class PunchedTarget
		{
			public PunchedTarget(ProbeTarget target)
			{
				Target = target;
			}

			public ProbeTarget Target { get; }
		}

		private PunchedTarget? Punched => Volatile.Read(ref _punchedTarget);

		public PendingProbe(string probeId, uint connectId, ProbeTarget[] targets, IPAddress requestSourceAddress, int requestSourcePort, long deadlineMilliseconds)
		{
			ProbeId = probeId;
			ConnectId = connectId;
			_targets = targets;
			RequestSourceAddress = requestSourceAddress;
			RequestSourcePort = requestSourcePort;
			DeadlineMilliseconds = deadlineMilliseconds;
		}

		public string ProbeId { get; }

		public uint ConnectId { get; }

		public IPAddress RequestSourceAddress { get; }

		public int RequestSourcePort { get; }

		public long DeadlineMilliseconds { get; }

		public int Attempts => Volatile.Read(ref _attempts);

		public bool HasAnswered => _answered;

		public IPAddress? ObservedAddress => _observedAddress;

		public int ObservedPort => _observedPort;

		public bool IsExpired(long nowMilliseconds) => nowMilliseconds >= DeadlineMilliseconds;

		/// <summary>
		///     The endpoint the next attempt is sent to. Once the host has punched, that is the endpoint the punch came from,
		///     because it is the only one its router will let a probe in at; the stated endpoints are kept every fourth attempt,
		///     so a host that is reachable there as well is still measured at the address players would use.
		/// </summary>
		public ProbeTarget NextTarget()
		{
			int attempt = Interlocked.Increment(ref _attempts) - 1;
			PunchedTarget? punched = Punched;
			if (punched == null) return _targets[attempt % _targets.Length];

			if ((attempt & 3) == 3) return _targets[attempt / 4 % _targets.Length];
			return punched.Target;
		}

		/// <summary>
		///     Records the endpoint a punch arrived from, which is the endpoint this host's router made it reachable at. Only
		///     the first punch is taken: it is the one that opened the path, and taking a later one would let a stray datagram
		///     move a check that is already running. The punch named this check before it reached here, so a punch meant for a
		///     check running beside this one cannot arrive.
		/// </summary>
		/// <returns>Whether this punch was the one this check learned from.</returns>
		public bool LearnPunchedEndpoint(IPAddress address, int port)
		{
			var candidate = new PunchedTarget(new ProbeTarget(address, port));
			return Interlocked.CompareExchange(ref _punchedTarget, candidate, null) == null;
		}

		/// <summary>
		///     The candidate the answer came from, which is the endpoint the host's real address matched. A host that answered
		///     from an address no candidate named is reported against the endpoint it stated.
		/// </summary>
		public ProbeTarget AnsweringTarget
		{
			get
			{
				IPAddress? observed = _observedAddress;
				if (observed != null)
				{
					PunchedTarget? punched = Punched;
					if (punched != null && punched.Target.Address.Equals(observed) && punched.Target.Port == _observedPort) return punched.Target;

					foreach (ProbeTarget target in _targets)
					{
						if (target.Address.Equals(observed) && target.Port == _observedPort) return target;
					}

					foreach (ProbeTarget target in _targets)
					{
						if (target.Address.Equals(observed)) return target;
					}
				}

				return _targets[0];
			}
		}

		public void RecordAnswer(IPAddress address, int port)
		{
			if (_answered) return;

			_observedAddress = address;
			_observedPort = port;
			_answered = true;
		}
	}
}
