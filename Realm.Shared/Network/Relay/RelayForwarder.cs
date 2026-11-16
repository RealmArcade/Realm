using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Realm.Shared.Network.Relay;

/// <summary>
///     What a volunteer has forwarded, in the shape its settings page and its log state it. It learns addresses and
///     volumes, never match data: every count here is read from a header it can see and a payload it cannot.
/// </summary>
public sealed class RelayForwarderStatistics
{
	/// <summary>The datagrams this volunteer forwarded.</summary>
	public int Forwarded { get; set; }

	/// <summary>The datagrams this volunteer dropped because its budget had run out.</summary>
	public int DroppedForBudget { get; set; }

	/// <summary>The datagrams this volunteer dropped because they were not whole relay datagrams.</summary>
	public int DroppedInvalid { get; set; }

	/// <summary>The datagrams this volunteer refused because the connection they named is not one it carries.</summary>
	public int RefusedUnknownConnection { get; set; }

	/// <summary>The datagrams this volunteer dropped because the connection they name has only one peer so far.</summary>
	public int DroppedUnpaired { get; set; }

	/// <summary>
	///     The datagrams this volunteer took in and could not hand to the other peer's socket. They are counted apart from the
	///     budget because a volunteer reading its own usage must be able to tell reaching its cap from its operating system
	///     refusing a send, and only one of those is something it chose.
	/// </summary>
	public int DroppedOnSend { get; set; }

	/// <summary>The bytes this volunteer forwarded.</summary>
	public long BytesForwarded { get; set; }

	/// <summary>How many connections this volunteer is carrying.</summary>
	public int Connections { get; set; }

	/// <summary>How many datagrams this volunteer ended up not carrying, for every reason together.</summary>
	public int Dropped => DroppedForBudget + DroppedInvalid + RefusedUnknownConnection + DroppedUnpaired + DroppedOnSend;
}

/// <summary>
///     The forwarding half of a volunteer: one UDP socket that moves opaque datagrams between the two peers of a connection
///     and cannot do anything else with them. It pairs the peers from the traffic it observes, forwards each datagram to the
///     other one out of one reused buffer, and enforces the volunteer's ceilings by dropping rather than queueing: nothing
///     it carries waits behind anything else. It is owned by one lane, and it stops when its player starts a match, so
///     nothing it does can extend the match the volunteer itself is playing.
/// </summary>
public sealed class RelayForwarder : IDisposable
{
	/// <summary>How many connections one volunteer carries unless its owner states fewer.</summary>
	public const int DefaultMaximumConnections = 8;

	/// <summary>The most connections a volunteer may state that it carries.</summary>
	public const int MaximumConnectionsCeiling = 32;

	/// <summary>The bytes per second one volunteer forwards unless its owner states another budget.</summary>
	public const int DefaultBudgetBytesPerSecond = 512 * 1024;

	/// <summary>The widest datagram this forwarder carries, which is one relayed datagram.</summary>
	public const int MaximumDatagramBytes = RelayEnvelope.MaximumEncapsulatedDatagramBytes;

	/// <summary>How long a connection stays paired with no traffic before the volunteer forgets it.</summary>
	public const int PairIdleExpiryMilliseconds = 30_000;

	private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(2);

	private readonly Socket _socket;
	private readonly RelayPair[] _pairs;
	private readonly int _maximumConnections;
	private readonly int _budgetBytesPerSecond;
	private readonly byte[] _receiveBuffer = new byte[MaximumDatagramBytes];
	private readonly EndPoint _anySender = new IPEndPoint(IPAddress.Any, 0);

	private double _allowance;
	private long _lastBudgetMilliseconds;
	private bool _disposed;

	private RelayForwarder(Socket socket, int maximumConnections, int budgetBytesPerSecond)
	{
		_socket = socket;
		_maximumConnections = Math.Clamp(maximumConnections, 0, MaximumConnectionsCeiling);
		_budgetBytesPerSecond = Math.Max(0, budgetBytesPerSecond);
		_pairs = new RelayPair[_maximumConnections];
		_allowance = _budgetBytesPerSecond;
		_lastBudgetMilliseconds = Environment.TickCount64;
		Port = ((IPEndPoint)socket.LocalEndPoint!).Port;
	}

	/// <summary>The port this volunteer is reachable at, which is the port it registers.</summary>
	public int Port { get; }

	/// <summary>What this volunteer has forwarded, which its settings page shows and its log states.</summary>
	public RelayForwarderStatistics Statistics { get; } = new();

	/// <summary>Whether the forwarding socket is open.</summary>
	public bool IsRunning => !_disposed && _socket.IsBound;

	/// <summary>The address family this volunteer's socket is bound in.</summary>
	public bool IsIpv4Only => _socket.AddressFamily == AddressFamily.InterNetwork;

	/// <summary>
	///     Opens the forwarding socket a volunteer offers its bandwidth on. The socket is bound before it is registered, so
	///     the endpoint a lobby hands out is one players can already reach.
	/// </summary>
	/// <param name="port">The port to offer, or zero for one the system chooses.</param>
	/// <param name="maximumConnections">How many connections this volunteer carries at once.</param>
	/// <param name="budgetBytesPerSecond">The most this volunteer forwards per second.</param>
	/// <param name="forwarder">The forwarder that was opened.</param>
	/// <param name="failure">Why no forwarder was opened.</param>
	/// <returns>Whether the socket was opened.</returns>
	public static bool TryOpen(int port, int maximumConnections, int budgetBytesPerSecond, out RelayForwarder? forwarder, out string failure)
	{
		forwarder = null;
		failure = string.Empty;

		var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
		try
		{
			socket.ExclusiveAddressUse = false;
			socket.Bind(new IPEndPoint(IPAddress.Any, Math.Max(0, port)));
		}
		catch (SocketException exception)
		{
			socket.Dispose();
			failure = $"the volunteer's forwarding socket could not be held on port {port}: {exception.Message}";
			return false;
		}

		forwarder = new RelayForwarder(socket, maximumConnections, budgetBytesPerSecond);
		return true;
	}

	/// <summary>
	///     Sends one datagram from this volunteer's own forwarding socket to an address it was given. It exists for the one
	///     thing that needs it: being measured. A node cannot reach a port nothing opens, so the volunteer knocks first from
	///     the very socket it is offering, and the answer it gets is what its own reachability is measured at.
	/// </summary>
	/// <param name="address">The address to send to.</param>
	/// <param name="port">The port to send to.</param>
	/// <param name="datagram">The datagram to send.</param>
	/// <returns>Whether the socket took it.</returns>
	public bool TrySendFromOfferedPort(string address, int port, ReadOnlySpan<byte> datagram)
	{
		if (_disposed || string.IsNullOrWhiteSpace(address) || port <= 0) return false;

		try
		{
			var destination = new IPEndPoint(IPAddress.Parse(address), port);
			return _socket.SendTo(datagram, SocketFlags.None, destination) == datagram.Length;
		}
		catch (SocketException)
		{
			return false;
		}
		catch (FormatException)
		{
			return false;
		}
	}

	/// <summary>
	///     Forwards everything that has arrived since the last poll. Each datagram is moved as it is read or dropped, so there
	///     is no queue to grow: a volunteer over its budget loses the datagram rather than delaying the one behind it.
	/// </summary>
	/// <param name="nowMilliseconds">The clock the budget is refilled from.</param>
	public void Poll(long nowMilliseconds)
	{
		if (_disposed) return;

		RefillBudget(nowMilliseconds);
		ExpireIdlePairs(nowMilliseconds);

		while (_socket.Available > 0)
		{
			EndPoint sender = _anySender;
			int length;
			try
			{
				length = _socket.ReceiveFrom(_receiveBuffer, SocketFlags.None, ref sender);
			}
			catch (SocketException exception) when (exception.SocketErrorCode == SocketError.MessageSize)
			{
				// A datagram wider than one relayed datagram is not one this volunteer carries. The operating system refuses
				// to hand it over whole, so it is counted here rather than lost between the socket and the counters: a
				// volunteer's line states everything it dropped, not only what it managed to read.
				Statistics.DroppedInvalid++;
				continue;
			}
			catch (SocketException)
			{
				break;
			}

			if (length <= 0) continue;
			Forward(_receiveBuffer.AsSpan(0, length), sender);
		}

		Statistics.Connections = CountActivePairs();
	}

	/// <summary>
	///     Runs the forwarding loop until the token is cancelled. It is what the volunteer's own lane calls, so no match
	///     thread ever carries another player's traffic.
	/// </summary>
	/// <param name="token">What stops the loop.</param>
	public void Run(CancellationToken token)
	{
		while (!token.IsCancellationRequested && !_disposed)
		{
			try
			{
				Poll(Environment.TickCount64);
			}
			catch (ObjectDisposedException)
			{
				// The owner stopped volunteering between the check above and the socket this lane was about to read. That
				// is this lane's whole job ending, not a failure to carry: an exception raised here would end the thread.
				return;
			}

			token.WaitHandle.WaitOne(PollInterval);
		}
	}

	/// <summary>
	///     States what this volunteer is carrying and what it has refused, in the shape a diagnostics page and a log compare
	///     line by line. It names endpoints and volumes only.
	/// </summary>
	/// <returns>The volunteer's own line and one line per connection it carries.</returns>
	public string Describe()
	{
		var builder = new StringBuilder();
		builder.Append("relay volunteer port=").Append(Port)
			.Append(" carry=").Append(CountActivePairs()).Append('/').Append(_maximumConnections)
			.Append(" budget=").Append(_budgetBytesPerSecond).Append("B/s")
			.Append(" forwarded=").Append(Statistics.Forwarded)
			.Append(" dropped=").Append(Statistics.Dropped)
			.Append(" bytes=").Append(Statistics.BytesForwarded)
			.Append('\n');

		return builder.ToString();
	}

	private void Forward(ReadOnlySpan<byte> datagram, EndPoint sender)
	{
		if (datagram.Length > MaximumDatagramBytes || !RelayEnvelope.TryReadHeader(datagram, out RelayPacketHeader header))
		{
			Statistics.DroppedInvalid++;
			return;
		}

		RelayPair? pair = FindPair(header.Token);
		if (pair == null)
		{
			if (!TryAddPair(header.Token, sender, out pair))
			{
				Statistics.RefusedUnknownConnection++;
				return;
			}
		}

		if (!pair.TryDescribeRoute(sender, out EndPoint? destination))
		{
			if (destination == null && pair.IsEither(sender)) Statistics.DroppedUnpaired++;
			else Statistics.RefusedUnknownConnection++;
			return;
		}

		if (_allowance < datagram.Length)
		{
			Statistics.DroppedForBudget++;
			return;
		}

		try
		{
			int sent = _socket.SendTo(datagram, SocketFlags.None, destination!);
			if (sent != datagram.Length)
			{
				Statistics.DroppedOnSend++;
				return;
			}
		}
		catch (SocketException)
		{
			Statistics.DroppedOnSend++;
			return;
		}

		_allowance -= datagram.Length;
		pair.LastSeenMilliseconds = Environment.TickCount64;
		Statistics.Forwarded++;
		Statistics.BytesForwarded += datagram.Length;
	}

	private RelayPair? FindPair(RelayConnectionToken token)
	{
		for (int index = 0; index < _pairs.Length; index++)
		{
			RelayPair? pair = _pairs[index];
			if (pair == null || pair.Token != token) continue;
			return pair;
		}

		return null;
	}

	private bool TryAddPair(RelayConnectionToken token, EndPoint first, out RelayPair? pair)
	{
		pair = null;
		for (int index = 0; index < _pairs.Length; index++)
		{
			if (_pairs[index] != null) continue;

			var created = new RelayPair(token, first);
			_pairs[index] = created;
			pair = created;
			return true;
		}

		return false;
	}

	private void ExpireIdlePairs(long nowMilliseconds)
	{
		for (int index = 0; index < _pairs.Length; index++)
		{
			RelayPair? pair = _pairs[index];
			if (pair == null || nowMilliseconds - pair.LastSeenMilliseconds < PairIdleExpiryMilliseconds) continue;

			_pairs[index] = null;
		}
	}

	private int CountActivePairs()
	{
		int active = 0;
		for (int index = 0; index < _pairs.Length; index++)
		{
			if (_pairs[index] != null) active++;
		}

		return active;
	}

	private void RefillBudget(long nowMilliseconds)
	{
		long elapsed = nowMilliseconds - _lastBudgetMilliseconds;
		if (elapsed <= 0) return;

		_lastBudgetMilliseconds = nowMilliseconds;
		_allowance = Math.Min(_budgetBytesPerSecond, _allowance + _budgetBytesPerSecond * elapsed / 1000.0);
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_socket.Dispose();
		for (int index = 0; index < _pairs.Length; index++) _pairs[index] = null;
	}

	private sealed class RelayPair
	{
		public RelayPair(RelayConnectionToken token, EndPoint first)
		{
			Token = token;
			First = first;
			LastSeenMilliseconds = Environment.TickCount64;
		}

		public RelayConnectionToken Token { get; }

		public EndPoint First { get; }

		public EndPoint? Second { get; private set; }

		public long LastSeenMilliseconds { get; set; }

		/// <summary>Whether this sender is one of the two peers this connection is carried between.</summary>
		/// <param name="sender">The peer to place.</param>
		/// <returns>Whether the peer is the first or the second of the pair.</returns>
		public bool IsEither(EndPoint sender) => First.Equals(sender) || (Second != null && Second.Equals(sender));

		/// <summary>
		///     Reads where one datagram is forwarded to. The first peer a connection is heard from opens the pair, the second
		///     distinct one closes it, and a third address is refused: a stranger that learned a token still cannot make the
		///     volunteer carry its bytes into somebody else's connection.
		/// </summary>
		/// <param name="sender">The peer this datagram arrived from.</param>
		/// <param name="destination">The other peer, or null when the sender is not one of the pair.</param>
		/// <returns>Whether this sender is one of the pair the volunteer carries.</returns>
		public bool TryDescribeRoute(EndPoint sender, out EndPoint? destination)
		{
			destination = null;

			if (First.Equals(sender))
			{
				LastSeenMilliseconds = Environment.TickCount64;
				destination = Second;
				return Second != null;
			}

			if (Second == null)
			{
				if (Token.IsSome && !First.Equals(sender))
				{
					Second = sender;
					LastSeenMilliseconds = Environment.TickCount64;
					destination = First;
					return true;
				}

				return false;
			}

			if (Second.Equals(sender))
			{
				LastSeenMilliseconds = Environment.TickCount64;
				destination = First;
				return true;
			}

			return false;
		}
	}
}
