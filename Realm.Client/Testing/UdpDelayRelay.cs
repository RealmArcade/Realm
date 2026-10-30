using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Realm.Client.Testing;

public sealed class UdpDelayRelay : IDisposable
{
	private const int MaximumQueuedDatagramCount = 4096;
	private const int SocketBufferSize = 4 * 1024 * 1024;

	private readonly UdpClient _clientFacingSocket;
	private readonly UdpClient _hostFacingSocket;
	private readonly IPEndPoint _hostEndpoint;
	private readonly Queue<DelayedDatagram> _clientToHostQueue;
	private readonly Queue<DelayedDatagram> _hostToClientQueue;
	private readonly object _queueLock;
	private readonly Thread _clientReaderThread;
	private readonly Thread _hostReaderThread;
	private readonly Thread _forwarderThread;
	private readonly int _oneWayDelayMilliseconds;
	private readonly int _jitterMilliseconds;
	private readonly int _packetLossPercent;
	private readonly Random _random = new(20261023);
	private IPEndPoint? _clientEndpoint;
	private volatile bool _stopped;
	private long _forwardedDatagramCount;
	private long _droppedDatagramCount;

	public UdpDelayRelay(int hostPort, int roundTripDelayMilliseconds)
		: this(hostPort, roundTripDelayMilliseconds / 2, 0, 0)
	{
	}

	public UdpDelayRelay(int hostPort, int oneWayDelayMilliseconds, int jitterMilliseconds, int packetLossPercent)
	{
		if (hostPort <= 0 || hostPort > 65535) throw new ArgumentOutOfRangeException(nameof(hostPort));
		if (oneWayDelayMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(oneWayDelayMilliseconds));
		if (jitterMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(jitterMilliseconds));
		if (packetLossPercent < 0 || packetLossPercent > 100) throw new ArgumentOutOfRangeException(nameof(packetLossPercent));

		_oneWayDelayMilliseconds = Math.Max(1, oneWayDelayMilliseconds);
		_jitterMilliseconds = jitterMilliseconds;
		_packetLossPercent = packetLossPercent;
		_hostEndpoint = new IPEndPoint(IPAddress.Loopback, hostPort);
		_clientFacingSocket = CreateSocket();
		_hostFacingSocket = CreateSocket();
		ListenPort = ((IPEndPoint)_clientFacingSocket.Client.LocalEndPoint!).Port;
		_clientToHostQueue = new Queue<DelayedDatagram>();
		_hostToClientQueue = new Queue<DelayedDatagram>();
		_queueLock = new object();

		_clientReaderThread = new Thread(ReadFromClient) { IsBackground = true, Name = "UdpDelayRelayClientReader" };
		_hostReaderThread = new Thread(ReadFromHost) { IsBackground = true, Name = "UdpDelayRelayHostReader" };
		_forwarderThread = new Thread(ForwardDueDatagrams) { IsBackground = true, Name = "UdpDelayRelayForwarder" };
		_clientReaderThread.Start();
		_hostReaderThread.Start();
		_forwarderThread.Start();
	}

	public int ListenPort { get; }
	public long ForwardedDatagramCount => Interlocked.Read(ref _forwardedDatagramCount);
	public long DroppedDatagramCount => Interlocked.Read(ref _droppedDatagramCount);

	public void Dispose()
	{
		if (_stopped) return;
		_stopped = true;
		_clientFacingSocket.Close();
		_hostFacingSocket.Close();

		foreach (Thread thread in new[] { _clientReaderThread, _hostReaderThread, _forwarderThread })
		{
			if (thread.IsAlive) thread.Join(1000);
		}

		_clientFacingSocket.Dispose();
		_hostFacingSocket.Dispose();
	}

	private static UdpClient CreateSocket()
	{
		UdpClient socket = new(new IPEndPoint(IPAddress.Loopback, 0));
		socket.Client.ReceiveBufferSize = SocketBufferSize;
		socket.Client.SendBufferSize = SocketBufferSize;
		return socket;
	}

	private void ReadFromClient()
	{
		IPEndPoint remote = new(IPAddress.Any, 0);
		while (!_stopped)
		{
			try
			{
				byte[] payload = _clientFacingSocket.Receive(ref remote);
				_clientEndpoint = new IPEndPoint(remote.Address, remote.Port);
				Enqueue(_clientToHostQueue, payload, _hostEndpoint);
			}
			catch (SocketException)
			{
				if (_stopped) return;
			}
			catch (ObjectDisposedException)
			{
				return;
			}
		}
	}

	private void ReadFromHost()
	{
		IPEndPoint remote = new(IPAddress.Any, 0);
		while (!_stopped)
		{
			try
			{
				byte[] payload = _hostFacingSocket.Receive(ref remote);
				Enqueue(_hostToClientQueue, payload, null);
			}
			catch (SocketException)
			{
				if (_stopped) return;
			}
			catch (ObjectDisposedException)
			{
				return;
			}
		}
	}

	private void Enqueue(Queue<DelayedDatagram> queue, byte[] payload, IPEndPoint? destination)
	{
		if (_packetLossPercent > 0 && NextRandom(100) < _packetLossPercent)
		{
			Interlocked.Increment(ref _droppedDatagramCount);
			return;
		}

		int delayMilliseconds = _oneWayDelayMilliseconds;
		if (_jitterMilliseconds > 0) delayMilliseconds += NextRandom(_jitterMilliseconds + 1);

		long dueTimestamp = Stopwatch.GetTimestamp() + (delayMilliseconds * Stopwatch.Frequency / 1000);
		lock (_queueLock)
		{
			if (queue.Count >= MaximumQueuedDatagramCount)
			{
				Interlocked.Increment(ref _droppedDatagramCount);
				return;
			}

			queue.Enqueue(new DelayedDatagram(payload, destination, dueTimestamp));
		}
	}

	private int NextRandom(int exclusiveMaximum)
	{
		lock (_random) return _random.Next(exclusiveMaximum);
	}

	private void ForwardDueDatagrams()
	{
		while (!_stopped)
		{
			bool forwardedAny = false;
			forwardedAny |= ForwardDueDatagrams(_clientToHostQueue, _hostFacingSocket);
			forwardedAny |= ForwardDueDatagrams(_hostToClientQueue, _clientFacingSocket);
			if (!forwardedAny) Thread.Sleep(1);
		}
	}

	private bool ForwardDueDatagrams(Queue<DelayedDatagram> queue, UdpClient sendSocket)
	{
		bool forwardedAny = false;
		while (true)
		{
			DelayedDatagram datagram;
			IPEndPoint? destination;
			lock (_queueLock)
			{
				if (queue.Count == 0) return forwardedAny;
				datagram = queue.Peek();
				if (datagram.DueTimestamp > Stopwatch.GetTimestamp()) return forwardedAny;
				queue.Dequeue();
			}

			destination = datagram.Destination ?? _clientEndpoint;
			if (destination == null) continue;

			try
			{
				sendSocket.Send(datagram.Payload, datagram.Payload.Length, destination);
				Interlocked.Increment(ref _forwardedDatagramCount);
				forwardedAny = true;
			}
			catch (SocketException)
			{
				if (_stopped) return forwardedAny;
			}
			catch (ObjectDisposedException)
			{
				return forwardedAny;
			}
		}
	}

	private readonly struct DelayedDatagram
	{
		public readonly byte[] Payload;
		public readonly IPEndPoint? Destination;
		public readonly long DueTimestamp;

		public DelayedDatagram(byte[] payload, IPEndPoint? destination, long dueTimestamp)
		{
			Payload = payload;
			Destination = destination;
			DueTimestamp = dueTimestamp;
		}
	}
}
