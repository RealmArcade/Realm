using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Realm.AdminServer.Models;
using Realm.Shared.Reachability;

namespace Realm.AdminServer.Services;

/// <summary>
///     Reads the endpoint a joining player's router exposes and hands it to the lobby's host, so the host can send toward
///     that player before the player's connection attempt reaches it.
/// </summary>
/// <remarks>
///     A host behind a router that drops unsolicited traffic only admits a player it has sent toward first, and it cannot
///     learn where a player is from the player's own connection attempt: that attempt is exactly what the router drops. The
///     joining screen therefore reports itself from the socket its connection attempt holds, this node reads the endpoint
///     the report arrived from, and the host punches that endpoint. Nothing about the join is trusted from the caller: the
///     address and port are the ones this node observed, and the token only says which join is being reported.
/// </remarks>
public class JoinerPunchRelay : IDisposable
{
	/// <summary>How long a join's report is accepted after the join was answered, which is longer than a join takes.</summary>
	private const int PendingLifetimeMilliseconds = 60_000;

	/// <summary>How many joins may be waiting for their report at once, so an unauthenticated route cannot grow unbounded.</summary>
	private const int MaximumPendingJoins = 4096;

	private const int ReceivedDatagramBytes = 1024;

	/// <summary>
	///     How long another node is given to hand a punch to its lobby's host. It bounds one delivery rather than one join:
	///     the reception loop no longer waits on this answer, and this is only how long a node that stopped answering holds
	///     up the punch it was asked for.
	/// </summary>
	private const int DeliveryTimeoutMilliseconds = 5_000;

	private static readonly HttpClient OriginClient = new();

	private readonly ConcurrentDictionary<string, PendingJoin> _pending = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, WebSocket> _hostConnections;
	private readonly LobbyRegistry _lobbies;
	private readonly Action<string> _log;
	private readonly Stopwatch _clock = Stopwatch.StartNew();
	private UdpClient? _socket;
	private CancellationTokenSource? _stop;
	private Task? _receiveLoop;
	private bool _disposed;

	public JoinerPunchRelay(ConcurrentDictionary<string, WebSocket> hostConnections, LobbyRegistry lobbies, Action<string> log)
	{
		_hostConnections = hostConnections;
		_lobbies = lobbies;
		_log = log;
	}

	/// <summary>The UDP port joining screens report themselves on, or zero when the socket is not open yet.</summary>
	public int Port { get; private set; }

	/// <summary>
	///     Opens the socket joining screens report themselves on. It is opened on the port the system chooses, because the
	///     port is handed to a screen in the join answer and nothing about it needs to be stable.
	/// </summary>
	public bool TryStart(out string failure)
	{
		failure = string.Empty;
		if (_socket != null) return true;
		if (_disposed)
		{
			failure = "this joiner relay has been disposed";
			return false;
		}

		try
		{
			var socket = new UdpClient(AddressFamily.InterNetwork);
			socket.Client.ExclusiveAddressUse = true;
			socket.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
			Port = ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
			_socket = socket;
			_stop = new CancellationTokenSource();
			_receiveLoop = Task.Run(() => ReceiveAsync(socket, _stop.Token));
			_log($"joining players report themselves on UDP port {Port}, and the host of their lobby is told where they arrived from");
			return true;
		}
		catch (SocketException exception)
		{
			failure = $"a UDP socket for joining players could not be bound: {exception.Message}";
			_log(failure);
			return false;
		}
	}

	/// <summary>
	///     States the token and port a joining screen reports itself with. The token names this join and nothing else, so two
	///     players joining the same lobby cannot be told apart by their address alone.
	/// </summary>
	/// <param name="lobbyId">The lobby being joined.</param>
	/// <returns>The ticket to hand back in the join answer.</returns>
	public JoinerPunchTicket Register(string lobbyId)
	{
		if (!TryStart(out _)) return default;

		PruneExpired();

		if (_pending.Count >= MaximumPendingJoins) return default;

		string token = Guid.NewGuid().ToString("N");
		_pending[token] = new PendingJoin(lobbyId, _clock.ElapsedMilliseconds + PendingLifetimeMilliseconds);
		return new JoinerPunchTicket(string.Empty, Port, token);
	}

	/// <summary>
	///     Records that the lobby's host has stated it sent the punch a join's report asked for, which is what a failed join is
	///     read against: a screen whose lobby never answered is told whether the host was ever told to send, so the sentence it
	///     reads names the step that failed rather than the whole handshake.
	/// </summary>
	/// <param name="token">The join the punch was for.</param>
	/// <returns>Whether a join of that name was waiting for a punch.</returns>
	public bool AcknowledgePunch(string token)
	{
		if (string.IsNullOrEmpty(token) || !_pending.TryGetValue(token, out PendingJoin? join)) return false;

		join.PunchAcknowledged = true;
		_log($"join {token} of lobby {join.LobbyId} was punched by its host");
		return true;
	}

	/// <summary>Forgets a join's token once its lobby is closed, so a token cannot outlive the join it belongs to.</summary>
	/// <param name="lobbyId">The lobby that is no longer open.</param>
	public void Forget(string lobbyId)
	{
		foreach (var pair in _pending)
		{
			if (string.Equals(pair.Value.LobbyId, lobbyId, StringComparison.Ordinal)) _pending.TryRemove(pair.Key, out _);
		}
	}

	public void Dispose()
	{
		_disposed = true;
		_stop?.Cancel();
		_socket?.Dispose();

		try
		{
			_receiveLoop?.Wait(TimeSpan.FromSeconds(2));
		}
		catch (AggregateException)
		{
		}

		_stop?.Dispose();
	}

	private void PruneExpired()
	{
		long now = _clock.ElapsedMilliseconds;
		foreach (var pair in _pending)
		{
			if (pair.Value.ExpiresAtMilliseconds < now) _pending.TryRemove(pair.Key, out _);
		}
	}

	private async Task ReceiveAsync(UdpClient socket, CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			UdpReceiveResult received;
			try
			{
				received = await socket.ReceiveAsync(cancellationToken).ConfigureAwait(false);
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

			if (received.Buffer.Length > ReceivedDatagramBytes) continue;
			if (!JoinerPunch.TryReadToken(received.Buffer, out string token)) continue;
			if (!_pending.TryGetValue(token, out PendingJoin join)) continue;

			IPEndPoint observed = received.RemoteEndPoint;
			string address = observed.Address.IsIPv4MappedToIPv6 ? observed.Address.MapToIPv4().ToString() : observed.Address.ToString();

			// One report asks for one punch. A datagram that arrives again is the same report arriving again, and telling the
			// host to punch the same endpoint a second time would only repeat work it has already done.
			if (!join.TryMarkReported()) continue;

			_log($"join {token} of lobby {join.LobbyId} reported itself from {address}:{observed.Port}");

			// The punch is delivered off this loop. This socket is the only one every joining player reports to, so waiting
			// here on another node's answer would hold up every join that arrived behind this one.
			_ = Task.Run(() => DeliverPunchAsync(join, token, address, observed.Port));
		}
	}

	/// <summary>
	///     Tells the lobby's host where the joining player is. The host is reached over its own signalling channel when this
	///     node holds it, and through the node that does when this node is only relaying the join for another one.
	/// </summary>
	private async Task DeliverPunchAsync(PendingJoin join, string token, string address, int port)
	{
		string lobbyId = join.LobbyId;
		if (_hostConnections.TryGetValue(lobbyId, out WebSocket? hostSocket) && hostSocket.State == WebSocketState.Open)
		{
			bool written = await HostSignaling.SendAsync(hostSocket, HostSignaling.Encode(new { Action = "Punch", Token = token, ClientIP = address, ClientPort = port })).ConfigureAwait(false);
			if (written) return;

			_log($"the punch for lobby {lobbyId} could not be handed to the host's signalling channel: its channel refused the write");
		}

		string originUri = _lobbies.TryGet(lobbyId, out LobbyInfo? lobby) && lobby != null ? lobby.OriginServerUri ?? string.Empty : string.Empty;
		if (string.IsNullOrWhiteSpace(originUri))
		{
			_log($"the punch for lobby {lobbyId} at {address}:{port} has nowhere to go: this node holds no signalling channel for it and knows no node that does");
			return;
		}

		try
		{
			using var delivery = new CancellationTokenSource(DeliveryTimeoutMilliseconds);
			var content = new StringContent(JsonSerializer.Serialize(new { LobbyId = lobbyId, ClientIP = address, ClientPort = port }), Encoding.UTF8, "application/json");
			using HttpResponseMessage response = await OriginClient.PostAsync($"{originUri.TrimEnd('/')}/lobbies/punch", content, delivery.Token).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				_log($"the punch for lobby {lobbyId} was refused by {originUri}: {response.StatusCode}");
				return;
			}

			// The node holding the host answered only once it had handed the punch to that host's signalling channel. The
			// host's own statement travels back to the node it is connected to, not to this one, so this answer is what a
			// join relayed through here has to go on: it is enough to tell a player whose join failed that the host was told
			// where to send, rather than that it was never told at all.
			join.PunchAcknowledged = true;
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException or ObjectDisposedException)
		{
			_log($"the punch for lobby {lobbyId} could not be delivered to {originUri}: {exception.Message}");
		}
	}

	private class PendingJoin
	{
		public PendingJoin(string lobbyId, long expiresAtMilliseconds)
		{
			LobbyId = lobbyId;
			ExpiresAtMilliseconds = expiresAtMilliseconds;
		}

		public string LobbyId { get; }

		public long ExpiresAtMilliseconds { get; }

		public volatile bool PunchAcknowledged;

		private int _reported;

		/// <summary>Whether this join's report has been read, which is what makes one report ask for one punch.</summary>
		public bool Reported => Volatile.Read(ref _reported) != 0;

		/// <summary>Claims the one punch this join's report asks for.</summary>
		/// <returns>Whether this arrival is the report that asks for it.</returns>
		public bool TryMarkReported() => Interlocked.Exchange(ref _reported, 1) == 0;
	}

	/// <summary>What a joining screen is told about the report it sent: whether it arrived, and whether the host has punched it.</summary>
	/// <param name="Reported">Whether this node saw the screen's report arrive.</param>
	/// <param name="Punched">Whether the lobby's host has stated that it sent the punch the report asked for.</param>
	public readonly record struct JoinPunchState(bool Reported, bool Punched);

	/// <summary>Reads a join's punch state, which is what a joining screen waits on before it sends anything toward the lobby.</summary>
	/// <param name="token">The join the screen reported itself for.</param>
	/// <returns>The state of the report and of the punch, or an unknown join's empty state.</returns>
	public JoinPunchState ReadState(string token)
	{
		if (string.IsNullOrEmpty(token) || !_pending.TryGetValue(token, out PendingJoin? join)) return default;
		return new JoinPunchState(join.Reported, join.PunchAcknowledged);
	}
}
