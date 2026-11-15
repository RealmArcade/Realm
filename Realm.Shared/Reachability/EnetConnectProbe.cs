using System.Buffers.Binary;

namespace Realm.Shared.Reachability;

/// <summary>
///     The answer an ENet server sends to the first packet of a connection attempt. It carries the identity the server
///     picked for the peer and the ticket the attempt was made with, which is what a prober matches its own request by.
/// </summary>
public readonly record struct EnetConnectReply(uint ConnectId, ushort PeerId, byte IncomingSessionId, byte OutgoingSessionId);

/// <summary>
///     The first packet of an ENet connection attempt, written by hand so that a plain UDP socket can ask an authority
///     whether a peer can reach the port it listens on. Only the opening packet and the answer to it are needed: a server
///     that answers has proved that a datagram from outside its network arrives at the socket the match listens on, which
///     is exactly what a player joining a lobby needs, and the attempt is abandoned before the handshake is completed.
/// </summary>
public static class EnetConnectProbe
{
	/// <summary>The bytes of the opening packet, which is a protocol header, a command header and a connect command.</summary>
	public const int ConnectDatagramBytes = 52;

	/// <summary>The smallest datagram an answer can be, which is a protocol header, a command header and a verify command.</summary>
	public const int MinimumReplyBytes = 48;

	private const ushort UnassignedPeerId = 0x0FFF;
	private const ushort SentTimeFlag = 0x8000;
	private const byte ConnectCommandNumber = 0x02;
	private const byte VerifyConnectCommandNumber = 0x03;
	private const byte AcknowledgeFlag = 0x80;
	private const byte UnassignedChannelId = 0xFF;
	private const byte UnassignedSessionId = 0xFF;
	private const byte FirstSessionId = 0x00;
	private const ushort ReliableSequenceNumber = 0x0001;
	private const uint DefaultMtu = 1392;
	private const uint DefaultWindowSize = 65536;
	private const uint MaximumChannelCount = 255;
	private const uint DefaultPacketThrottleInterval = 5000;
	private const uint DefaultPacketThrottleAcceleration = 2;
	private const uint DefaultPacketThrottleDeceleration = 2;
	private const int CommandOffset = 4;
	private const int CommandPayloadOffset = 8;
	private const int ReplyConnectIdOffset = 44;

	/// <summary>
	///     Writes the opening packet of a connection attempt into <paramref name="datagram" />. The ticket and the data a
	///     server answers with are the prober's own, so the answer can be matched back to the attempt that caused it.
	/// </summary>
	/// <returns>The number of bytes written, or zero when the buffer is too small.</returns>
	public static int WriteConnect(Span<byte> datagram, uint connectId, uint challengeData, ushort sentTime)
	{
		if (datagram.Length < ConnectDatagramBytes) return 0;

		BinaryPrimitives.WriteUInt16BigEndian(datagram[..2], (ushort)(SentTimeFlag | UnassignedPeerId));
		BinaryPrimitives.WriteUInt16BigEndian(datagram[2..4], sentTime);
		datagram[CommandOffset] = (byte)(AcknowledgeFlag | ConnectCommandNumber);
		datagram[CommandOffset + 1] = UnassignedChannelId;
		BinaryPrimitives.WriteUInt16BigEndian(datagram[(CommandOffset + 2)..(CommandOffset + 4)], ReliableSequenceNumber);

		Span<byte> payload = datagram[CommandPayloadOffset..];
		BinaryPrimitives.WriteUInt16BigEndian(payload[..2], 0);
		payload[2] = UnassignedSessionId;
		payload[3] = FirstSessionId;
		BinaryPrimitives.WriteUInt32BigEndian(payload[4..8], DefaultMtu);
		BinaryPrimitives.WriteUInt32BigEndian(payload[8..12], DefaultWindowSize);
		BinaryPrimitives.WriteUInt32BigEndian(payload[12..16], MaximumChannelCount);
		BinaryPrimitives.WriteUInt32BigEndian(payload[16..20], 0);
		BinaryPrimitives.WriteUInt32BigEndian(payload[20..24], 0);
		BinaryPrimitives.WriteUInt32BigEndian(payload[24..28], DefaultPacketThrottleInterval);
		BinaryPrimitives.WriteUInt32BigEndian(payload[28..32], DefaultPacketThrottleAcceleration);
		BinaryPrimitives.WriteUInt32BigEndian(payload[32..36], DefaultPacketThrottleDeceleration);
		BinaryPrimitives.WriteUInt32BigEndian(payload[36..40], connectId);
		BinaryPrimitives.WriteUInt32BigEndian(payload[40..44], challengeData);

		return ConnectDatagramBytes;
	}

	/// <summary>Reads the opening packet of a connection attempt, when the datagram is one, and the ticket it was made with.</summary>
	public static bool TryReadConnect(ReadOnlySpan<byte> datagram, out uint connectId)
	{
		connectId = 0;
		if (datagram.Length < ConnectDatagramBytes) return false;
		if (datagram[CommandOffset] != (byte)(AcknowledgeFlag | ConnectCommandNumber)) return false;

		connectId = BinaryPrimitives.ReadUInt32BigEndian(datagram[(CommandPayloadOffset + 36)..(CommandPayloadOffset + 40)]);
		return connectId != 0;
	}

	/// <summary>Reads the answer to a connection attempt, when the datagram is one.</summary>
	public static bool TryReadVerifyConnect(ReadOnlySpan<byte> datagram, out EnetConnectReply reply)
	{
		reply = default;
		if (datagram.Length < MinimumReplyBytes) return false;

		byte command = datagram[CommandOffset];
		if ((byte)(command & ~AcknowledgeFlag) != VerifyConnectCommandNumber) return false;

		ushort peerId = BinaryPrimitives.ReadUInt16BigEndian(datagram[CommandPayloadOffset..(CommandPayloadOffset + 2)]);
		byte incomingSessionId = datagram[CommandPayloadOffset + 2];
		byte outgoingSessionId = datagram[CommandPayloadOffset + 3];
		uint connectId = BinaryPrimitives.ReadUInt32BigEndian(datagram[ReplyConnectIdOffset..(ReplyConnectIdOffset + 4)]);

		reply = new EnetConnectReply(connectId, peerId, incomingSessionId, outgoingSessionId);
		return true;
	}
}
