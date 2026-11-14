using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Realm.Shared.Reachability;

namespace Realm.Network.Tests;

/// <summary>
///     A router in front of a host's game port: it gives that port a different external port, and a datagram from the
///     network arrives at the game port only when it is sent to the external port the router handed out. That is the shape
///     the reachability check exists for, and the shape that a registry node on the host's own network never exercises
///     because it reaches the game port directly.
/// </summary>
public class SimulatedNat : IDisposable
{
	private readonly UdpClient _publicSocket;
	private readonly IPEndPoint _internalGamePort;
	private readonly CancellationTokenSource _stop = new();
	private readonly Task _relay;
	private IPEndPoint? _lastRemoteEndPoint;

	public SimulatedNat(int internalGamePort)
	{
		_internalGamePort = new IPEndPoint(IPAddress.Loopback, internalGamePort);
		_publicSocket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		PublicPort = ((IPEndPoint)_publicSocket.Client.LocalEndPoint!).Port;
		_relay = Task.Run(() => RelayAsync(_stop.Token));
	}

	/// <summary>The external port the router gives the game port, which is not the port the game listens on.</summary>
	public int PublicPort { get; }

	public IPEndPoint PublicEndpoint => new(IPAddress.Loopback, PublicPort);

	/// <summary>
	///     Sends a datagram out from the router's public endpoint, which is what a host's punch looks like to the network.
	///     The receiving node sees this endpoint as the host's address, exactly as it would see a real router's mapping.
	/// </summary>
	public void Punch(IPEndPoint destination) => _publicSocket.Send(ReachabilityPunch.Datagram.ToArray(), destination);

	public void Dispose()
	{
		_stop.Cancel();
		_publicSocket.Dispose();

		try
		{
			_relay.Wait(TimeSpan.FromSeconds(2));
		}
		catch (AggregateException)
		{
		}

		_stop.Dispose();
	}

	private async Task RelayAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			UdpReceiveResult received;
			try
			{
				received = await _publicSocket.ReceiveAsync(cancellationToken).ConfigureAwait(false);
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

			if (received.RemoteEndPoint.Equals(_internalGamePort))
			{
				IPEndPoint? remoteEndPoint = _lastRemoteEndPoint;
				if (remoteEndPoint == null) continue;

				_publicSocket.Send(received.Buffer, received.Buffer.Length, remoteEndPoint);
				continue;
			}

			_lastRemoteEndPoint = received.RemoteEndPoint;
			_publicSocket.Send(received.Buffer, received.Buffer.Length, _internalGamePort);
		}
	}
}
