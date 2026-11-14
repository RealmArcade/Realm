using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Realm.Shared.Reachability;

namespace Realm.Network.Tests;

/// <summary>
///     A game port with the behaviours a joining player meets in the field. It speaks just enough ENet to answer the opening
///     packet of a connection attempt, so the reachability check can be exercised without a match server, and it can be told
///     to behave like the networks that make hosting fail in one house and work in the next.
/// </summary>
public class FakeGamePort : IDisposable
{
	public enum Behaviour
	{
		/// <summary>Answers anything that arrives: an open port, a forwarded port, or a port a firewall rule allows.</summary>
		AnswerEverything,

		/// <summary>
		///     Answers only traffic it has already sent to, which is a router that drops unsolicited inbound packets together
		///     with a local firewall that lets the replies to its own traffic through. This is the machine that can host only
		///     because the lobby server punches for it.
		/// </summary>
		AnswerOnlySolicited,

		/// <summary>Never answers: a closed port, a refused firewall rule, or a game port with nothing listening on it.</summary>
		Silent
	}

	private static readonly TimeSpan SolicitedLifetime = TimeSpan.FromSeconds(30);

	private readonly UdpClient _socket;
	private readonly CancellationTokenSource _stop = new();
	private readonly Behaviour _behaviour;
	private readonly ConcurrentDictionary<string, DateTime> _solicited = new(StringComparer.Ordinal);
	private Task? _loop;
	private int _receivedCount;

	public FakeGamePort(Behaviour behaviour)
	{
		_behaviour = behaviour;
		_socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		Port = ((IPEndPoint)_socket.Client.LocalEndPoint!).Port;
		_loop = Task.Run(() => ReceiveAsync(_stop.Token));
	}

	public int Port { get; }

	public IPAddress Address => IPAddress.Loopback;

	public int ReceivedCount => Volatile.Read(ref _receivedCount);

	/// <summary>
	///     Sends a datagram to an endpoint, which is what a host's authority does when it punches a joining peer. It is the
	///     only thing that makes <see cref="Behaviour.AnswerOnlySolicited" /> answer.
	/// </summary>
	public void Punch(IPEndPoint endpoint)
	{
		_solicited[endpoint.ToString()] = DateTime.UtcNow;
		_socket.Send(new byte[] { 0x50, 0x55, 0x4E, 0x43, 0x48 }, endpoint);
	}

	public void Dispose()
	{
		_stop.Cancel();
		_socket.Dispose();

		try
		{
			_loop?.Wait(TimeSpan.FromSeconds(2));
		}
		catch (AggregateException)
		{
		}

		_stop.Dispose();
		_loop = null;
	}

	private async Task ReceiveAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			UdpReceiveResult received;
			try
			{
				received = await _socket.ReceiveAsync(cancellationToken).ConfigureAwait(false);
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
				continue;
			}

			Interlocked.Increment(ref _receivedCount);

			if (_behaviour == Behaviour.Silent) continue;
			if (_behaviour == Behaviour.AnswerOnlySolicited && !HasSolicited(received.RemoteEndPoint)) continue;
			if (!EnetConnectProbe.TryReadConnect(received.Buffer, out uint connectId)) continue;

			byte[] reply = BuildVerifyConnect(connectId);
			_socket.Send(reply, reply.Length, received.RemoteEndPoint);
		}
	}

	private bool HasSolicited(IPEndPoint endpoint)
	{
		if (!_solicited.TryGetValue(endpoint.ToString(), out DateTime sentAt)) return false;
		return DateTime.UtcNow - sentAt < SolicitedLifetime;
	}

	/// <summary>The answer a real ENet server sends to an opening packet, laid out as the authority this game runs sends it.</summary>
	private static byte[] BuildVerifyConnect(uint connectId)
	{
		var reply = new byte[EnetConnectProbe.MinimumReplyBytes];
		BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(0, 2), 0x8000);
		BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(2, 2), 0x2E08);
		reply[4] = 0x83;
		reply[5] = 0xFF;
		BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(6, 2), 0x0001);
		BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(8, 2), 0x0000);
		reply[10] = 0x00;
		reply[11] = 0x01;
		BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(12, 4), 1392);
		BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(16, 4), 65536);
		BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(20, 4), 0);
		BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(24, 4), 0);
		BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(28, 4), 0);
		BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(32, 4), 5000);
		BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(36, 4), 2);
		BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(40, 4), 2);
		BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(44, 4), connectId);
		return reply;
	}
}
