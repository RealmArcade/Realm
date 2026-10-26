using System;
using System.Buffers.Binary;
using System.IO;

namespace Realm.Shared.Network;

public static class TransportFrameCodec
{
	public static void WriteFrame(Stream stream, NetworkFrameKind kind, ReadOnlySpan<byte> payload)
	{
		int frameLength = RealmProtocol.FrameKindBytes + payload.Length;
		if (frameLength <= RealmProtocol.FrameKindBytes || frameLength > RealmProtocol.MaximumFrameBytes)
		{
			throw new InvalidDataException($"A {kind} frame of {frameLength} bytes is outside the protocol's frame limits.");
		}

		Span<byte> header = stackalloc byte[RealmProtocol.FrameHeaderBytes];
		BinaryPrimitives.WriteInt32LittleEndian(header, frameLength);
		header[RealmProtocol.FrameLengthPrefixBytes] = (byte)kind;
		stream.Write(header);
		if (payload.Length > 0) stream.Write(payload);
		stream.Flush();
	}

	public static bool TryReadFrame(Stream stream, ref byte[]? buffer, out NetworkFrame frame)
	{
		Span<byte> lengthPrefix = stackalloc byte[RealmProtocol.FrameLengthPrefixBytes];
		if (!TryReadExactly(stream, lengthPrefix, allowCleanEndOfStream: true)) return Fail(out frame);

		int frameLength = BinaryPrimitives.ReadInt32LittleEndian(lengthPrefix);
		if (frameLength <= RealmProtocol.FrameKindBytes || frameLength > RealmProtocol.MaximumFrameBytes)
		{
			throw new InvalidDataException($"A frame declares {frameLength} bytes, outside the protocol's frame limits.");
		}

		buffer ??= new byte[frameLength];
		if (buffer.Length < frameLength) buffer = new byte[frameLength];

		Span<byte> frameBytes = new Span<byte>(buffer, 0, frameLength);
		if (!TryReadExactly(stream, frameBytes, allowCleanEndOfStream: false))
		{
			throw new InvalidDataException($"A frame of {frameLength} bytes was truncated.");
		}

		var kind = (NetworkFrameKind)frameBytes[0];
		frame = new NetworkFrame(kind, buffer, RealmProtocol.FrameKindBytes, frameLength - RealmProtocol.FrameKindBytes);
		return true;
	}

	private static bool Fail(out NetworkFrame frame)
	{
		frame = default;
		return false;
	}

	private static bool TryReadExactly(Stream stream, Span<byte> destination, bool allowCleanEndOfStream)
	{
		int read = 0;
		while (read < destination.Length)
		{
			int chunk = stream.Read(destination.Slice(read));
			if (chunk == 0)
			{
				if (read == 0 && allowCleanEndOfStream) return false;
				throw new InvalidDataException("The transport closed in the middle of a frame.");
			}

			read += chunk;
		}

		return true;
	}
}
