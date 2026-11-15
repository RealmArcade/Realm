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

	/// <summary>
	///     How often the cold probe repeats while a check runs. It is slower than the punched attempt because it is the
	///     second question, and the answer that matters first is whether the punched route works at all.
	/// </summary>
	private const int ColdAttemptIntervalMilliseconds = 400;

	/// <summary>
	///     How long a check waits for the host's punch before it starts knocking on the advertised endpoint as well. A probe
	///     sent before the punch occupies the router mapping the punch would have used, so the wait is what lets a host
	///     behind a restricted router turn punch-only reachability into an answer.
	/// </summary>
	private const int PunchGraceMilliseconds = 750;

	private const int PendingLifetimeMilliseconds = 120_000;

	private const int ReceivedDatagramBytes = 2048;

	private readonly int _requestedPort;
	private readonly string _statedPublicIp;
	private readonly string _statedPublicIpv6;
	private readonly ConcurrentDictionary<string, PendingProbe> _pending = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<uint, string> _pendingByConnectId = new();
	private readonly ConcurrentDictionary<uint, string> _pendingByColdConnectId = new();
	private readonly Stopwatch _clock = Stopwatch.StartNew();
	private CancellationTokenSource? _stop;
	private UdpClient? _ipv4Socket;
	private UdpClient? _ipv6Socket;
	private UdpClient? _coldSocket;
	private readonly List<Task> _receiveLoops = new(3);
	private bool _disposed;

	public UdpReachabilityProbeService(int probePort = DefaultProbePort, string statedPublicIp = "", string statedPublicIpv6 = "")
	{
		_requestedPort = probePort;
		_statedPublicIp = statedPublicIp ?? string.Empty;
		_statedPublicIpv6 = statedPublicIpv6 ?? string.Empty;
	}

	/// <summary>The port this service probes from, which is the port a host is told to punch.</summary>
	public int ProbePort { get; private set; }

	/// <summary>
	///     The port the cold probe is sent from, or zero when this node could not open that socket. A cold probe leaves from
	///     a socket no host ever punches, which is the only way to tell a game port a router forwards from one only a punch
	///     opens.
	/// </summary>
	public int ColdProbePort { get; private set; }

	public bool IsRunning => _ipv4Socket != null || _ipv6Socket != null;

	/// <summary>
	///     The address families this node has a probe socket in, which is the whole of what it can measure. A node with no
	///     IPv6 socket can never verify an IPv6 endpoint, and it says so rather than measuring one against a socket of the
	///     wrong family.
	/// </summary>
	public IReadOnlyList<LobbyAddressFamily> AvailableFamilies
	{
		get
		{
			var families = new List<LobbyAddressFamily>(2);
			if (_ipv4Socket != null) families.Add(LobbyAddressFamily.IPv4);
			if (_ipv6Socket != null) families.Add(LobbyAddressFamily.IPv6);
			return families;
		}
	}

	/// <summary>Whether this node holds a probe socket in a family.</summary>
	/// <param name="family">The family to look for.</param>
	/// <returns>Whether a check can be made in that family.</returns>
	public bool HasSocketFor(LobbyAddressFamily family) => SocketFor(family) != null;

	/// <summary>
	///     Binds the probe sockets. They are bound on first use so a node that never registers a lobby pays nothing, and a
	///     node whose port is taken says so once rather than per lobby. One socket is bound per address family: a check is
	///     only ever sent from a socket of the family it is made in, because a datagram can not cross families and an
	///     answer that arrived from the wrong one would state an endpoint nobody can use.
	/// </summary>
	/// <remarks>
	///     The IPv4 socket is the one this node must have. A missing IPv6 socket is not a failure: it makes this node an
	///     IPv4-only node, which is what it was before this check could be made in IPv6 at all.
	/// </remarks>
	public bool TryStart(out string failure)
	{
		failure = string.Empty;
		if (_ipv4Socket != null) return true;
		if (_disposed)
		{
			failure = "this probe service has been disposed";
			return false;
		}

		try
		{
			UdpClient socket = Bind(AddressFamily.InterNetwork, _requestedPort);
			ProbePort = ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
			_ipv4Socket = socket;
		}
		catch (SocketException exception)
		{
			failure = $"a UDP probe socket could not be bound on port {_requestedPort}: {exception.Message}";
			return false;
		}

		_stop = new CancellationTokenSource();
		_receiveLoops.Add(Task.Run(() => ReceiveAsync(_ipv4Socket, _stop.Token, cold: false)));

		// Both sockets take the same port number: UDP names the two families separately, so port 8998 over IPv6 is a
		// different port from 8998 over IPv4 and a host can be told one number whichever family it is checked in.
		if (Socket.OSSupportsIPv6)
		{
			try
			{
				UdpClient ipv6 = Bind(AddressFamily.InterNetworkV6, ProbePort);
				_ipv6Socket = ipv6;
				_receiveLoops.Add(Task.Run(() => ReceiveAsync(ipv6, _stop.Token, cold: false)));
			}
			catch (SocketException)
			{
				// A node that cannot bind the IPv6 port stays the IPv4-only node it was; the lobby server states which
				// families it can check, so a host is told this instead of having its lobby measured in the wrong family.
				_ipv6Socket = null;
			}
		}

		TryStartColdSocket();
		return true;
	}

	/// <summary>
	///     Binds the socket a cold probe is sent from. It is a second IPv4 socket rather than the probe socket because the
	///     probe socket is the one a host punches, and a check sent from a port the host has sent toward proves only that
	///     the punch worked. A node that cannot open this socket still runs every check; it simply cannot state that a game
	///     port is reachable without a punch, and says so instead of guessing. No IPv6 cold socket is opened: an IPv6 check
	///     needs no punch at all, so every IPv6 answer is already cold.
	/// </summary>
	private void TryStartColdSocket()
	{
		if (_coldSocket != null || _disposed || _stop == null) return;

		try
		{
			UdpClient socket = Bind(AddressFamily.InterNetwork, 0);
			ColdProbePort = ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
			_coldSocket = socket;
			_receiveLoops.Add(Task.Run(() => ReceiveAsync(socket, _stop.Token, cold: true)));
		}
		catch (SocketException)
		{
			ColdProbePort = 0;
		}
	}

	private static UdpClient Bind(AddressFamily family, int port)
	{
		var socket = new UdpClient(family);
		socket.Client.ExclusiveAddressUse = true;
		if (family == AddressFamily.InterNetworkV6)
		{
			socket.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
		}

		socket.Client.Bind(new IPEndPoint(family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, port));
		return socket;
	}

	private UdpClient? SocketFor(LobbyAddressFamily family) => family switch
	{
		LobbyAddressFamily.IPv4 => _ipv4Socket,
		LobbyAddressFamily.IPv6 => _ipv6Socket,
		_ => null
	};


	/// <summary>
	///     Starts a check of the game port a host is about to advertise. The port is the host's claim and is always used,
	///     because it is the port a joining player will punch and connect to; the address is checked both as the host stated
	///     it and as this node sees the host's own requests arrive, so a host whose measurement of its own address is wrong is
	///     still measured correctly instead of being refused for it.
	/// </summary>
	public ReachabilityTicket Begin(IPAddress advertisedAddress, int advertisedPort, IPAddress requestSourceAddress, int requestSourcePort, int windowMilliseconds = DefaultWindowMilliseconds)
	{
		if (!TryStart(out string failure)) throw new InvalidOperationException(failure);

		// The check is made in the family of the endpoint the host advertised, because that is the family a joining
		// player, and therefore this node, has to reach it in. An address this node cannot read states no family, so the
		// request's own source address decides - that is the endpoint the host came from and the only one known.
		LobbyAddressFamily family = LobbyAddresses.FamilyOf(advertisedAddress);
		if (family == LobbyAddressFamily.Unknown) family = LobbyAddresses.FamilyOf(requestSourceAddress);

		if (SocketFor(family) == null)
		{
			throw new InvalidOperationException(family == LobbyAddressFamily.IPv6
				? "this node has no IPv6 socket to check an IPv6 endpoint from, so the endpoint was not checked"
				: $"this node has no {LobbyAddresses.FamilyName(family)} probe socket, so the endpoint was not checked");
		}

		long now = _clock.ElapsedMilliseconds;
		ForgetStale(now);
		if (!HasCapacityFor(requestSourceAddress, now, out string refusal)) throw new InvalidOperationException(refusal);

		int window = Math.Clamp(windowMilliseconds <= 0 ? DefaultWindowMilliseconds : windowMilliseconds, MinimumWindowMilliseconds, MaximumWindowMilliseconds);
		ProbeTarget[] targets = BuildTargets(advertisedAddress, advertisedPort, requestSourceAddress);
		string probeId = Guid.NewGuid().ToString("N");
		uint connectId = (uint)Random.Shared.NextInt64(1, uint.MaxValue);

		bool coldPossible = family == LobbyAddressFamily.IPv4 && _coldSocket != null;
		uint coldConnectId = coldPossible ? (uint)Random.Shared.NextInt64(1, uint.MaxValue) : 0;

		var probe = new PendingProbe(probeId, connectId, coldConnectId, targets, family, requestSourceAddress, requestSourcePort, now + window);
		_pending[probeId] = probe;
		_pendingByConnectId[connectId] = probeId;
		if (coldPossible) _pendingByColdConnectId[coldConnectId] = probeId;

		_ = Task.Run(() => RunProbeAsync(probe));
		if (coldPossible) _ = Task.Run(() => RunColdProbeAsync(probe));
		return new ReachabilityTicket(probeId, StatedPublicAddress(family), ProbePort, window, LobbyAddresses.FamilyName(family));
	}

	/// <summary>
	///     The address this node tells a host its checks come from, in the family a check is being made in. An address the
	///     node was configured with that belongs to the other family is not stated: a host told to punch an IPv4 address
	///     for an IPv6 check would send its datagram where no check is listening, so the host is left to resolve this
	///     node's name in the right family instead.
	/// </summary>
	/// <param name="family">The family the check is made in.</param>
	/// <returns>The stated address, or empty when this node stated none in that family.</returns>
	private string StatedPublicAddress(LobbyAddressFamily family) => family switch
	{
		LobbyAddressFamily.IPv6 => _statedPublicIpv6,
		LobbyAddressFamily.IPv4 => _statedPublicIp,
		_ => string.Empty
	};

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
			probe.Attempts,
			LobbyAddresses.FamilyName(probe.Family),
			ColdAnswered(probe),
			probe.ColdAttempts);
	}

	/// <summary>
	///     Whether this check's game port answered a socket the host never punched. An IPv6 check is always cold, because
	///     nothing in IPv6 is punched; an IPv4 check is cold only when the cold socket answered it.
	/// </summary>
	/// <param name="probe">The check being described.</param>
	/// <returns>Whether the port answered without a punch.</returns>
	private static bool ColdAnswered(PendingProbe probe) => probe.Family == LobbyAddressFamily.IPv6 ? probe.HasAnswered : probe.ColdAnswered;

	/// <summary>
	///     The endpoints a check is sent to: the one the host stated, and the one this node can derive from the host's own
	///     requests. Only the port is taken from the host either way, because a joining player punches that port at whichever
	///     of the two addresses is the host's real one.
	/// </summary>
	/// <remarks>
	///     Every target is in one family, which is the family the check is made in. A request that arrived over one family
	///     says nothing about how the host is reached in the other, and a target of the wrong family could only be probed
	///     from a socket that cannot reach it.
	/// </remarks>
	public static ProbeTarget[] BuildTargets(IPAddress advertisedAddress, int advertisedPort, IPAddress requestSourceAddress)
	{
		IPAddress statedTarget = ChooseDestinationAddress(advertisedAddress, requestSourceAddress);
		LobbyAddressFamily family = LobbyAddresses.FamilyOf(statedTarget);
		bool sourceSharesFamily = LobbyAddresses.FamilyOf(requestSourceAddress) == family && family != LobbyAddressFamily.Unknown;

		if (!sourceSharesFamily || statedTarget.Equals(requestSourceAddress)) return new[] { new ProbeTarget(statedTarget, advertisedPort) };
		return new[] { new ProbeTarget(statedTarget, advertisedPort), new ProbeTarget(requestSourceAddress, advertisedPort) };
	}

	/// <summary>
	///     The address a probe is sent to. Loopback, an unspecified address and an address the caller could not state all
	///     mean the same thing here: use the address the host's request came from.
	/// </summary>
	public static IPAddress ChooseDestinationAddress(IPAddress advertisedAddress, IPAddress requestSourceAddress)
	{
		if (advertisedAddress.Equals(IPAddress.Any) || advertisedAddress.Equals(IPAddress.None)) return requestSourceAddress;
		if (advertisedAddress.Equals(IPAddress.IPv6Any) || advertisedAddress.Equals(IPAddress.IPv6None)) return requestSourceAddress;
		if (IPAddress.IsLoopback(advertisedAddress) && !IPAddress.IsLoopback(requestSourceAddress)) return requestSourceAddress;
		if (advertisedAddress.IsIPv6LinkLocal || advertisedAddress.IsIPv6SiteLocal) return requestSourceAddress;
		return advertisedAddress;
	}

	private async Task RunProbeAsync(PendingProbe probe)
	{
		long graceUntil = _clock.ElapsedMilliseconds + PunchGraceMilliseconds;
		while (!probe.HasAnswered && !probe.IsExpired(_clock.ElapsedMilliseconds))
		{
			bool waitingForPunch = probe.Family == LobbyAddressFamily.IPv4 && !probe.HasPunched && _clock.ElapsedMilliseconds < graceUntil;
			if (!waitingForPunch) SendAttempt(probe);

			await Task.Delay(AttemptIntervalMilliseconds).ConfigureAwait(false);
		}
	}

	/// <summary>
	///     Asks the game port the question a stranger asks, from a socket the host has never sent toward and never will. An
	///     answer is the only thing that makes a lobby reachable without hole punching, so this is the check that separates a
	///     forwarded port from one that only a punch opens.
	/// </summary>
	private async Task RunColdProbeAsync(PendingProbe probe)
	{
		while (!probe.ColdAnswered && !probe.IsExpired(_clock.ElapsedMilliseconds))
		{
			SendColdAttempt(probe);
			await Task.Delay(ColdAttemptIntervalMilliseconds).ConfigureAwait(false);
		}
	}

	private void SendAttempt(PendingProbe probe)
	{
		UdpClient? socket = SocketFor(probe.Family);
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

	private void SendColdAttempt(PendingProbe probe)
	{
		UdpClient? socket = _coldSocket;
		if (socket == null) return;

		Span<byte> datagram = stackalloc byte[EnetConnectProbe.ConnectDatagramBytes];
		int written = EnetConnectProbe.WriteConnect(datagram, probe.ColdConnectId, probe.ColdConnectId ^ 0x3C3C3C3C, (ushort)(_clock.ElapsedMilliseconds & 0xFFFF));
		if (written == 0) return;

		try
		{
			ProbeTarget target = probe.NextColdTarget();
			socket.Send(datagram, new IPEndPoint(target.Address, target.Port));
		}
		catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
		{
		}
	}

	private async Task ReceiveAsync(UdpClient socket, CancellationToken cancellationToken, bool cold)
	{
		var buffer = new byte[ReceivedDatagramBytes];
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				UdpReceiveResult received = await socket.ReceiveAsync(cancellationToken).ConfigureAwait(false);
				RecordDatagram(received.Buffer, received.RemoteEndPoint, cold);
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

	private void RecordDatagram(ReadOnlySpan<byte> datagram, IPEndPoint remoteEndPoint, bool cold)
	{
		if (EnetConnectProbe.TryReadVerifyConnect(datagram, out EnetConnectReply reply))
		{
			RecordAnswer(reply, remoteEndPoint, cold);
			return;
		}

		// Only the punched socket is ever punched, so a punch read from the cold socket is a datagram that was never
		// addressed to it and says nothing about this check.
		if (cold) return;

		RecordPunch(datagram, remoteEndPoint);
	}

	private void RecordAnswer(EnetConnectReply reply, IPEndPoint remoteEndPoint, bool cold)
	{
		ConcurrentDictionary<uint, string> pending = cold ? _pendingByColdConnectId : _pendingByConnectId;
		if (!pending.TryGetValue(reply.ConnectId, out string? probeId)) return;
		if (!_pending.TryGetValue(probeId, out PendingProbe? probe)) return;

		if (cold) probe.RecordColdAnswer(remoteEndPoint.Address, remoteEndPoint.Port);
		else probe.RecordAnswer(remoteEndPoint.Address, remoteEndPoint.Port);
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
		_ipv4Socket?.Dispose();
		_ipv4Socket = null;
		_ipv6Socket?.Dispose();
		_ipv6Socket = null;
		_coldSocket?.Dispose();
		_coldSocket = null;

		for (int index = 0; index < _receiveLoops.Count; index++)
		{
			try
			{
				_receiveLoops[index].Wait(TimeSpan.FromSeconds(2));
			}
			catch (AggregateException)
			{
			}
		}

		_receiveLoops.Clear();
		_stop?.Dispose();
		_stop = null;
		_pending.Clear();
		_pendingByConnectId.Clear();
		_pendingByColdConnectId.Clear();
	}

	private class PendingProbe
	{
		private readonly ProbeTarget[] _targets;
		private volatile bool _answered;
		private volatile bool _coldAnswered;
		private IPAddress? _observedAddress;
		private int _observedPort;
		private int _attempts;
		private int _coldAttempts;
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

		/// <summary>Whether the host has punched this check, which is what says the punched route is the one to probe.</summary>
		public bool HasPunched => Volatile.Read(ref _punchedTarget) != null;

		public PendingProbe(string probeId, uint connectId, uint coldConnectId, ProbeTarget[] targets, LobbyAddressFamily family, IPAddress requestSourceAddress, int requestSourcePort, long deadlineMilliseconds)
		{
			ProbeId = probeId;
			ConnectId = connectId;
			ColdConnectId = coldConnectId;
			_targets = targets;
			Family = family;
			RequestSourceAddress = requestSourceAddress;
			RequestSourcePort = requestSourcePort;
			DeadlineMilliseconds = deadlineMilliseconds;
		}

		public string ProbeId { get; }

		public uint ConnectId { get; }

		/// <summary>The identifier the cold probe carries, or zero when this check has no cold probe.</summary>
		public uint ColdConnectId { get; }

		/// <summary>The family this check is made in, which is the family of every endpoint it probes and of the socket it probes from.</summary>
		public LobbyAddressFamily Family { get; }

		public IPAddress RequestSourceAddress { get; }

		public int RequestSourcePort { get; }

		public long DeadlineMilliseconds { get; }

		public int Attempts => Volatile.Read(ref _attempts);

		public int ColdAttempts => Volatile.Read(ref _coldAttempts);

		/// <summary>Whether the game port answered, by either route.</summary>
		public bool HasAnswered => _answered || _coldAnswered;

		/// <summary>Whether the game port answered the socket the host never punched.</summary>
		public bool ColdAnswered => _coldAnswered;

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

		/// <summary>The endpoint the next cold attempt is sent to, which is always one of the endpoints the host stated.</summary>
		public ProbeTarget NextColdTarget()
		{
			int attempt = Interlocked.Increment(ref _coldAttempts) - 1;
			return _targets[attempt % _targets.Length];
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

		/// <summary>
		///     Records that the cold probe answered. The endpoint it names is only adopted when the punched route has not
		///     already named one, because the punched endpoint is the one a joining player will use.
		/// </summary>
		public void RecordColdAnswer(IPAddress address, int port)
		{
			if (_coldAnswered) return;

			if (!_answered)
			{
				_observedAddress = address;
				_observedPort = port;
			}

			_coldAnswered = true;
		}
	}
}
