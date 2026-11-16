using System;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Realm.AdminServer.Services;

/// <summary>
///     Hands one message at a time to a lobby host's signalling channel.
/// </summary>
/// <remarks>
///     A websocket carries one send at a time, and one host's channel is written by three parties: the punch a joining
///     player's report asks for, the line a failed join states, and a punch another node relays. Two of them overlapping
///     throws instead of queueing, and the message is lost, so every write goes through here and waits for the one before it.
///     The gate belongs to the socket, and it is held across nothing but the send itself.
/// </remarks>
public static class HostSignaling
{
	private static readonly ConditionalWeakTable<WebSocket, SemaphoreSlim>.CreateValueCallback CreateGate = _ => new SemaphoreSlim(1, 1);

	private static readonly ConditionalWeakTable<WebSocket, SemaphoreSlim> Gates = new();

	/// <summary>
	///     Writes one text message to a host's signalling channel, waiting for any send already in flight.
	/// </summary>
	/// <param name="socket">The host's signalling channel.</param>
	/// <param name="payload">The encoded message.</param>
	/// <returns>Whether the message was written, which is false when the channel is gone.</returns>
	public static async Task<bool> SendAsync(WebSocket socket, byte[] payload)
	{
		SemaphoreSlim gate = Gates.GetValue(socket, CreateGate);
		await gate.WaitAsync().ConfigureAwait(false);
		try
		{
			if (socket.State != WebSocketState.Open) return false;
			await socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
			return true;
		}
		catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException or InvalidOperationException)
		{
			return false;
		}
		finally
		{
			gate.Release();
		}
	}

	/// <summary>
	///     Encodes a message the host reads. Callers build the same anonymous shapes they always did; only the write is shared.
	/// </summary>
	/// <param name="message">The message to encode.</param>
	/// <returns>The bytes to hand to <see cref="SendAsync" />.</returns>
	public static byte[] Encode(object message) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
}
