using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Realm.Shared.Reachability;

/// <summary>
///     Asks a game port whether anything is listening on it, over one address family. It is the same opening packet a
///     registry node's check sends, so a port that answers this is a port a player could connect to, and it exists so a
///     host can find out what its own machine answers before it tells other players where to go.
/// </summary>
/// <remarks>
///     This is a diagnostic and never a gate: a host that fails it is still hosted, because the only thing that decides
///     whether a lobby is published is the check a registry node makes from outside its own network. What it buys is a
///     sentence in the log naming the half that did not answer, which is the difference between a Godot upgrade quietly
///     dropping one family and a bug report that says so.
/// </remarks>
public static class LobbyPortCheck
{
	/// <summary>How long a self-check waits for an answer, which is short enough to run while a lobby is opening.</summary>
	public const int DefaultTimeoutMilliseconds = 750;

	/// <summary>
	///     Whether the port answers over the address family of the address given.
	/// </summary>
	/// <param name="address">The address to ask, which decides the family the question is made in.</param>
	/// <param name="port">The game port to ask.</param>
	/// <param name="timeoutMilliseconds">How long to wait for the answer.</param>
	/// <param name="cancellationToken">Cancels the question when the lobby is abandoned.</param>
	/// <returns>Whether anything answered, or false when the request could not even be sent.</returns>
	public static async Task<bool> AnswersAsync(IPAddress address, int port, int timeoutMilliseconds = DefaultTimeoutMilliseconds, CancellationToken cancellationToken = default)
	{
		if (port <= 0) return false;

		uint connectId = NextConnectId();
		uint challengeData = connectId ^ 0x5A5A5A5A;

		try
		{
			using var socket = new UdpClient(address.AddressFamily);
			socket.Connect(new IPEndPoint(LobbyAddresses.Normalize(address) ?? address, port));

			var datagram = new byte[EnetConnectProbe.ConnectDatagramBytes];
			int written = EnetConnectProbe.WriteConnect(datagram.AsSpan(), connectId, challengeData, (ushort)(connectId & 0xFFFF));
			if (written == 0) return false;

			socket.Send(datagram, written);

			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(Math.Max(timeoutMilliseconds, 1));

			var buffer = new byte[1024];
			while (!timeout.IsCancellationRequested)
			{
				UdpReceiveResult received = await socket.ReceiveAsync(timeout.Token).ConfigureAwait(false);
				if (EnetConnectProbe.TryReadVerifyConnect(received.Buffer, out EnetConnectReply reply) && reply.ConnectId == connectId) return true;
			}

			return false;
		}
		catch (Exception exception) when (exception is SocketException or OperationCanceledException or ObjectDisposedException or ArgumentException)
		{
			return false;
		}
	}

	/// <summary>
	///     A ticket for this check. It is never zero, because a zero is what a datagram that was not a connection attempt
	///     reads as.
	/// </summary>
	/// <returns>The ticket to make the attempt with.</returns>
	private static uint NextConnectId()
	{
		uint candidate = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
		return candidate == 0 ? 1u : candidate;
	}
}
