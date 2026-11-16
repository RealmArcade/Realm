using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Realm.Shared.Reachability;

namespace Realm.Network.Tests;

/// <summary>
///     A router in front of a host's game port: a datagram from the network arrives at the game port only when it is sent
///     to the outside port the router exposed for it, and the game port's own datagrams leave through an outside port the
///     router chose. That is the shape the reachability check exists for, and the shape a registry node on the host's own
///     network never exercises because it reaches the game port directly.
/// </summary>
/// <remarks>
///     The outside port is chosen by the router, so it is not the port the game listens on. Whether it keeps one outside
///     port for every destination or makes a new one per destination is the whole of what a punch can and cannot open, so
///     both are modelled: <see cref="MappingBehaviours.Reused" /> keeps one outside port for every destination the game
///     port is seen talking to, and <see cref="MappingBehaviours.PerDestination" /> gives each destination one of its own.
/// </remarks>
public class SimulatedNat : IDisposable
{
	private readonly int _internalPort;
	private readonly bool _perDestination;
	private readonly CancellationTokenSource _stop = new();
	private readonly List<UdpClient> _outsideSockets = new(2);
	private readonly List<Task> _relays = new(2);

	/// <summary>The outside port this router exposes toward a destination, which is one port or one per destination.</summary>
	private readonly ConcurrentDictionary<IPEndPoint, int> _outsidePortForDestination = new();

	/// <summary>The destination an outside port was observed talking to, which is where its answers go back to.</summary>
	private readonly ConcurrentDictionary<int, List<IPEndPoint>> _destinationsForOutsidePort = new();

	private int _reusedOutsidePort;

	public SimulatedNat(int internalGamePort, string mappingBehaviour = MappingBehaviours.Reused)
	{
		_internalPort = internalGamePort;
		_perDestination = mappingBehaviour == MappingBehaviours.PerDestination;
	}

	/// <summary>
	///     The outside port this router exposed toward the first destination the game port talked to. Under a per-destination
	///     mapping every destination has one of its own, which is what makes the lobby unlistable; it is zero until the game
	///     port has talked to somebody.
	/// </summary>
	public int PublicPort { get; private set; }

	/// <summary>
	///     The outside endpoint the game port's own datagrams to a destination leave from, which is the endpoint that
	///     destination sees the game port at.
	/// </summary>
	/// <param name="destination">The destination the game port is talking to.</param>
	/// <returns>The outside endpoint of that destination.</returns>
	public IPEndPoint OutsideEndpointFor(IPEndPoint destination) => new(IPAddress.Loopback, OutsidePortFor(destination));

	/// <summary>
	///     Sends a datagram out toward a destination, which is what a host's punch looks like to the network. The receiving
	///     node sees the outside port this router exposed for that destination, exactly as it would see a real router's
	///     mapping, and the datagram names the check the punch is for.
	/// </summary>
	/// <param name="destination">The endpoint to punch.</param>
	/// <param name="probeId">The reachability check the punch is for.</param>
	public void Punch(IPEndPoint destination, string probeId)
	{
		var datagram = new byte[ReachabilityPunch.MaximumDatagramBytes];
		int written = ReachabilityPunch.TryWrite(datagram, probeId);
		if (written == 0) return;

		SocketFor(OutsidePortFor(destination)).SendTo(datagram, 0, written, SocketFlags.None, destination);
	}

	public void Dispose()
	{
		_stop.Cancel();
		foreach (UdpClient socket in _outsideSockets) socket.Dispose();

		foreach (Task relay in _relays)
		{
			try
			{
				relay.Wait(TimeSpan.FromSeconds(2));
			}
			catch (AggregateException)
			{
			}
		}

		_outsideSockets.Clear();
		_relays.Clear();
		_stop.Dispose();
	}

	/// <summary>
	///     The outside port this router exposes toward a destination. A reused mapping keeps the one port it already gave the
	///     game port, which is what a home router does; a per-destination mapping makes a new one for each peer, which is
	///     what no punch can predict.
	/// </summary>
	/// <param name="destination">The destination the game port is talking to.</param>
	/// <returns>The outside port that destination sees.</returns>
	private int OutsidePortFor(IPEndPoint destination)
	{
		if (!_perDestination) return ReusedOutsidePort();

		int port = _outsidePortForDestination.GetOrAdd(destination, _ => OpenOutsideSocket());
		if (PublicPort == 0) PublicPort = port;
		_destinationsForOutsidePort.GetOrAdd(port, _ => new List<IPEndPoint>(2));
		return port;
	}

	private int ReusedOutsidePort()
	{
		if (_reusedOutsidePort == 0)
		{
			_reusedOutsidePort = OpenOutsideSocket();
			PublicPort = _reusedOutsidePort;
		}

		return _reusedOutsidePort;
	}

	private int OpenOutsideSocket()
	{
		var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		int port = ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
		_outsideSockets.Add(socket);
		_relays.Add(Task.Run(() => RelayAsync(socket, port, _stop.Token)));
		return port;
	}

	private Socket SocketFor(int outsidePort)
	{
		foreach (UdpClient client in _outsideSockets)
		{
			if (((IPEndPoint)client.Client.LocalEndPoint!).Port == outsidePort) return client.Client;
		}

		return _outsideSockets[0].Client;
	}

	private async Task RelayAsync(UdpClient outsideSocket, int outsidePort, CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			UdpReceiveResult received;
			try
			{
				received = await outsideSocket.ReceiveAsync(cancellationToken).ConfigureAwait(false);
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

			if (received.RemoteEndPoint.Port == _internalPort && received.RemoteEndPoint.Address.Equals(IPAddress.Loopback))
			{
				// The game port answering: it leaves toward every destination that reached this outside port, because the
				// router remembers which flows that one mapping belongs to rather than only the last one it saw.
				if (!_destinationsForOutsidePort.TryGetValue(outsidePort, out List<IPEndPoint>? destinations)) continue;

				IPEndPoint[] answering;
				lock (destinations) answering = destinations.ToArray();

				foreach (IPEndPoint destination in answering) outsideSocket.Send(received.Buffer, received.Buffer.Length, destination);
				continue;
			}

			List<IPEndPoint> remembered = _destinationsForOutsidePort.GetOrAdd(outsidePort, _ => new List<IPEndPoint>(2));
			lock (remembered)
			{
				if (!remembered.Contains(received.RemoteEndPoint)) remembered.Add(received.RemoteEndPoint);
			}

			outsideSocket.Send(received.Buffer, received.Buffer.Length, new IPEndPoint(IPAddress.Loopback, _internalPort));
		}
	}
}
