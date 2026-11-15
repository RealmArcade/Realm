using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>
///     The small socket a lobby opens so its seated screens can prove and measure a path to each other. It records where
///     each seat's probe socket was seen and introduces two seats to each other's endpoints, which is all a handover
///     rehearsal needs; it measures nothing itself, so a path is only ever described by the two screens that use it.
/// </summary>
public class LobbyHandoverProbeRelay : IDisposable
{
	private readonly record struct ProbeRegistration(IPEndPoint Endpoint, long RegisteredTicks, long LastPunchTicks);

	private const int MaximumRegistrations = 64;

	private const long PunchRequestMinimumIntervalMilliseconds = 1000;

	private const long RegistrationLifetimeMilliseconds = 10 * 60 * 1000;

	private readonly Socket _socket;

	private readonly Thread _receiveThread;

	private readonly Func<string, bool> _isSeatedSessionToken;

	private readonly Action<string> _log;

	private readonly ConcurrentDictionary<string, ProbeRegistration> _registrations = new(StringComparer.Ordinal);

	private volatile bool _disposed;

	private LobbyHandoverProbeRelay(Socket socket, Func<string, bool> isSeatedSessionToken, Action<string> log)
	{
		_socket = socket;
		_isSeatedSessionToken = isSeatedSessionToken;
		_log = log;
		Port = ((IPEndPoint)socket.LocalEndPoint!).Port;
		_receiveThread = new Thread(ReceiveLoop)
		{
			IsBackground = true,
			Name = "RealmHandoverRelay"
		};
		_receiveThread.Start();
	}

	/// <summary>The port the relay listens on, which the lobby publishes so every seat can register with it.</summary>
	public int Port { get; }

	/// <summary>
	///     Opens the relay on a port the system chooses. A lobby that cannot open one still runs; it simply cannot rehearse
	///     a handover, and says so by publishing no relay port.
	/// </summary>
	/// <param name="isSeatedSessionToken">Answers whether a token belongs to a seat of the lobby the relay serves.</param>
	/// <param name="log">Writes a line about what the relay refused.</param>
	/// <returns>The relay, or null when no socket could be opened.</returns>
	public static LobbyHandoverProbeRelay? TryStart(Func<string, bool> isSeatedSessionToken, Action<string> log)
	{
		var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
		try
		{
			socket.DualMode = true;
			socket.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
		}
		catch (SocketException exception)
		{
			socket.Dispose();
			log($"the handover probe relay could not open a socket on this machine: {exception.Message}");
			return null;
		}

		return new LobbyHandoverProbeRelay(socket, isSeatedSessionToken, log);
	}

	private void ReceiveLoop()
	{
		var buffer = new byte[LobbyHandoverProbeWire.DatagramBytes * 2];
		var replyBuffer = new byte[LobbyHandoverProbeWire.DatagramBytes];
		EndPoint remote = new IPEndPoint(IPAddress.IPv6Any, 0);

		while (!_disposed)
		{
			try
			{
				_socket.ReceiveTimeout = 250;
				int received = _socket.ReceiveFrom(buffer, ref remote);
				if (received <= 0) continue;
				if (remote is not IPEndPoint source) continue;
				if (!LobbyHandoverProbeWire.TryRead(buffer.AsSpan(0, received), out LobbyHandoverProbeKind kind, out string token, out string targetToken, out _, out _)) continue;

				switch (kind)
				{
					case LobbyHandoverProbeKind.Register:
						Register(source, token, replyBuffer);
						break;
					case LobbyHandoverProbeKind.RequestPunch:
						Introduce(source, token, targetToken, replyBuffer);
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

	private void Register(IPEndPoint source, string token, byte[] replyBuffer)
	{
		if (!_isSeatedSessionToken(token))
		{
			_log("a handover probe registration arrived with a token no seat of this lobby holds; it was ignored");
			return;
		}

		if (_registrations.Count >= MaximumRegistrations && !_registrations.ContainsKey(token)) return;

		long now = Environment.TickCount64;
		long lastPunch = _registrations.TryGetValue(token, out ProbeRegistration existing) ? existing.LastPunchTicks : 0;
		_registrations[token] = new ProbeRegistration(source, now, lastPunch);
		PruneExpired(now);

		LobbyHandoverProbeWire.TryWrite(replyBuffer, LobbyHandoverProbeKind.Registered, token, string.Empty, 0, source);
		SendTo(replyBuffer, source);
	}

	private void Introduce(IPEndPoint source, string requesterToken, string targetToken, byte[] replyBuffer)
	{
		if (!_isSeatedSessionToken(requesterToken)) return;
		if (string.Equals(requesterToken, targetToken, StringComparison.Ordinal)) return;

		long now = Environment.TickCount64;
		if (_registrations.TryGetValue(requesterToken, out ProbeRegistration requester))
		{
			if (now - requester.LastPunchTicks < PunchRequestMinimumIntervalMilliseconds) return;
			_registrations[requesterToken] = requester with { Endpoint = source, LastPunchTicks = now };
		}
		else
		{
			if (!_isSeatedSessionToken(targetToken)) return;
			_registrations[requesterToken] = new ProbeRegistration(source, now, now);
		}

		// The target's endpoint is known only once its own probe socket has spoken to this relay. A request that arrives
		// before then is dropped rather than answered with a guess: a screen is never told an endpoint nothing measured.
		if (!_registrations.TryGetValue(targetToken, out ProbeRegistration target)) return;

		LobbyHandoverProbeWire.TryWrite(replyBuffer, LobbyHandoverProbeKind.PeerEndpoint, requesterToken, targetToken, 0, target.Endpoint);
		SendTo(replyBuffer, source);

		LobbyHandoverProbeWire.TryWrite(replyBuffer, LobbyHandoverProbeKind.PeerEndpoint, targetToken, requesterToken, 0, source);
		SendTo(replyBuffer, target.Endpoint);
	}

	private void SendTo(byte[] datagram, IPEndPoint endpoint)
	{
		try
		{
			IPAddress address = endpoint.Address;
			if (address.AddressFamily == AddressFamily.InterNetwork) address = address.MapToIPv6();
			_socket.SendTo(datagram, new IPEndPoint(address, endpoint.Port));
		}
		catch (SocketException)
		{
		}
		catch (ObjectDisposedException)
		{
		}
	}

	private void PruneExpired(long now)
	{
		if (_registrations.Count < MaximumRegistrations) return;

		foreach (KeyValuePair<string, ProbeRegistration> pair in _registrations)
		{
			if (now - pair.Value.RegisteredTicks < RegistrationLifetimeMilliseconds) continue;
			_registrations.TryRemove(pair.Key, out _);
		}
	}

	/// <summary>Answers whether a token belongs to a seat the relay serves, which is what keeps it from relaying for strangers.</summary>
	/// <param name="sessionToken">The token a probe datagram stated.</param>
	/// <returns>Whether the token is one of the lobby's seat secrets.</returns>
	public bool IsSeatedToken(string sessionToken) => _isSeatedSessionToken(sessionToken);

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		try
		{
			_socket.Dispose();
		}
		catch (ObjectDisposedException)
		{
		}

		if (_receiveThread.IsAlive) _receiveThread.Join(500);
	}
}
