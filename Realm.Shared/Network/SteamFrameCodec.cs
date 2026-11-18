using System;
using System.Buffers.Binary;

namespace Realm.Shared.Network;

/// <summary>
///     The four-byte header that carries a frame's kind over a transport that does not encode one itself. Godot's RPC layer
///     states the frame kind for the ENet paths; a Steam message is opaque bytes, so the kind travels in front of the payload
///     and the payload is everything after it. There is one codec, used by the authority's Steam listener and by a joining
///     screen, so a frame means the same thing on both.
/// </summary>
public static class SteamFrameCodec
{
	/// <summary>The bytes in front of every payload: one little-endian 32-bit frame kind.</summary>
	public const int HeaderBytes = sizeof(int);

	/// <summary>
	///     Writes one frame into a buffer this caller owns. The buffer must be at least
	///     <see cref="HeaderBytes" /> + <paramref name="payload" />.Length bytes long.
	/// </summary>
	/// <param name="destination">The buffer the frame is written into.</param>
	/// <param name="kind">The frame kind to state.</param>
	/// <param name="payload">The frame's own bytes, which may be empty.</param>
	/// <returns>The number of bytes written, or <c>0</c> when the buffer is too small.</returns>
	public static int TryWrite(Span<byte> destination, NetworkFrameKind kind, ReadOnlySpan<byte> payload)
	{
		int required = HeaderBytes + payload.Length;
		if (destination.Length < required) return 0;

		BinaryPrimitives.WriteInt32LittleEndian(destination, (int)kind);
		payload.CopyTo(destination[HeaderBytes..]);
		return required;
	}

	/// <summary>
	///     Reads the frame kind out of a received message and hands back the payload behind it.
	/// </summary>
	/// <param name="message">The bytes a transport delivered.</param>
	/// <param name="kind">The frame kind the message states.</param>
	/// <param name="payload">The frame's own bytes, empty when the header is all there was.</param>
	/// <returns>Whether the message was long enough to hold a header.</returns>
	public static bool TryRead(ReadOnlySpan<byte> message, out NetworkFrameKind kind, out ReadOnlySpan<byte> payload)
	{
		if (message.Length < HeaderBytes)
		{
			kind = default;
			payload = default;
			return false;
		}

		kind = (NetworkFrameKind)BinaryPrimitives.ReadInt32LittleEndian(message);
		payload = message[HeaderBytes..];
		return true;
	}
}
