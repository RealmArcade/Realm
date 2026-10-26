using System;

namespace Realm.Shared.Network;

public readonly struct NetworkFrame
{
	public NetworkFrame(NetworkFrameKind kind, byte[] buffer, int payloadOffset, int payloadLength)
	{
		Kind = kind;
		Buffer = buffer;
		PayloadOffset = payloadOffset;
		PayloadLength = payloadLength;
	}

	public NetworkFrameKind Kind { get; }
	public byte[] Buffer { get; }
	public int PayloadOffset { get; }
	public int PayloadLength { get; }

	public ReadOnlySpan<byte> Payload => new ReadOnlySpan<byte>(Buffer, PayloadOffset, PayloadLength);
}
