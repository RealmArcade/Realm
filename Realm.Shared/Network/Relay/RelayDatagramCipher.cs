using System;
using System.Security.Cryptography;

namespace Realm.Shared.Network.Relay;

/// <summary>
///     The end-to-end keys of one relayed connection. They are derived from the secret the join agreed over the lobby's
///     authenticated channel, so a volunteer is never given them and cannot open, alter or fabricate a datagram: all it can
///     do is move bytes it cannot read.
/// </summary>
public static class RelayKeys
{
	/// <summary>The bytes one direction's key occupies.</summary>
	public const int KeyBytes = 32;

	private static ReadOnlySpan<byte> HostToClientLabel => "realm-relay-host-to-client-v1"u8;

	private static ReadOnlySpan<byte> ClientToHostLabel => "realm-relay-client-to-host-v1"u8;

	/// <summary>
	///     Derives the two direction keys of a connection. Each direction has its own key so that the nonce space of one
	///     sender can never collide with the other's, which is what lets the nonce stay a function of the header rather than
	///     travelling beside it.
	/// </summary>
	/// <param name="joinSecret">The secret both peers agreed during the join.</param>
	/// <param name="token">The connection token, which separates two joins that share a secret.</param>
	/// <param name="hostToClientKey">The key the authority seals with and the screen opens with.</param>
	/// <param name="clientToHostKey">The key the screen seals with and the authority opens with.</param>
	public static void Derive(ReadOnlySpan<byte> joinSecret, RelayConnectionToken token, Span<byte> hostToClientKey, Span<byte> clientToHostKey)
	{
		Span<byte> salt = stackalloc byte[sizeof(ulong)];
		token.WriteTo(salt);

		HKDF.DeriveKey(HashAlgorithmName.SHA256, joinSecret, hostToClientKey, salt, HostToClientLabel);
		HKDF.DeriveKey(HashAlgorithmName.SHA256, joinSecret, clientToHostKey, salt, ClientToHostLabel);
	}
}

/// <summary>
///     The sealer and opener of one relayed connection's official direction. It owns both keys, so one instance seals what
///     this side sends and opens what the other side sent, and it is never touched from two lanes at once.
/// </summary>
public sealed class RelayDatagramCipher : IDisposable
{
	/// <summary>The bytes an authentication tag occupies.</summary>
	public const int TagBytes = 16;

	private const int NonceBytes = 12;

	private readonly AesGcm _send;
	private readonly AesGcm _receive;
	private bool _disposed;

	/// <summary>Creates the sealer and opener of one direction of a connection.</summary>
	/// <param name="joinSecret">The secret both peers agreed during the join.</param>
	/// <param name="token">The connection token the connection is named by.</param>
	/// <param name="isAuthority">Whether this side is the authority, which decides which direction key is which.</param>
	public RelayDatagramCipher(ReadOnlySpan<byte> joinSecret, RelayConnectionToken token, bool isAuthority)
	{
		Span<byte> hostToClient = stackalloc byte[RelayKeys.KeyBytes];
		Span<byte> clientToHost = stackalloc byte[RelayKeys.KeyBytes];
		RelayKeys.Derive(joinSecret, token, hostToClient, clientToHost);

		_send = new AesGcm(isAuthority ? hostToClient : clientToHost, TagBytes);
		_receive = new AesGcm(isAuthority ? clientToHost : hostToClient, TagBytes);
	}

	/// <summary>
	///     Seals one inner datagram into a relayed one. The path identifier and the per-path sequence travel beside the tag
	///     rather than inside the sealed payload, because they are what a copy is routed and measured by; the tag proves both
	///     of them along with the token and the inner delivery, so a volunteer that altered a path or a sequence is refused.
	/// </summary>
	/// <param name="destination">The datagram to build, which must hold the header, the tag and the inner datagram.</param>
	/// <param name="token">The connection the datagram belongs to.</param>
	/// <param name="pathId">The path the copy travels.</param>
	/// <param name="deliveryFlag">The guarantee the inner datagram needs, and whether this datagram is a probe.</param>
	/// <param name="messageSequence">The sequence the duplicate window reads.</param>
	/// <param name="pathSequence">The per-path sequence.</param>
	/// <param name="inner">The datagram for the match, or the probe body.</param>
	/// <returns>The bytes the relayed datagram occupies.</returns>
	public int Seal(Span<byte> destination, RelayConnectionToken token, byte pathId, byte deliveryFlag, int messageSequence, int pathSequence, ReadOnlySpan<byte> inner)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		int length = RelayEnvelope.HeaderBytes + inner.Length;
		RelayEnvelope.WriteHeader(destination, token, pathId, deliveryFlag, messageSequence, pathSequence);

		Span<byte> nonce = stackalloc byte[NonceBytes];
		WriteNonce(nonce, pathId, messageSequence);

		Span<byte> associatedData = stackalloc byte[15];
		WriteAssociatedData(associatedData, token, pathId, deliveryFlag, messageSequence, pathSequence);

		_send.Encrypt(nonce, inner, destination.Slice(RelayEnvelope.HeaderBytes, inner.Length), destination.Slice(RelayEnvelope.TagOffset, TagBytes), associatedData);
		return length;
	}

	/// <summary>
	///     Opens one relayed datagram. A datagram whose tag does not prove it, which names another connection, or which is too
	///     large for an inner datagram is refused rather than handed on, so the match above never sees a byte a volunteer
	///     could have written.
	/// </summary>
	/// <param name="datagram">The datagram as it arrived.</param>
	/// <param name="inner">The buffer the inner datagram is written into.</param>
	/// <param name="header">The header the datagram stated and the tag proved.</param>
	/// <param name="innerLength">The bytes the inner datagram occupies.</param>
	/// <returns>Whether the datagram was genuine and fitted the buffer.</returns>
	public bool TryOpen(ReadOnlySpan<byte> datagram, Span<byte> inner, out RelayPacketHeader header, out int innerLength)
	{
		innerLength = 0;
		header = default;
		if (_disposed) return false;
		if (!RelayEnvelope.TryReadHeader(datagram, out header)) return false;
		if (datagram.Length - RelayEnvelope.HeaderBytes > inner.Length) return false;

		Span<byte> nonce = stackalloc byte[NonceBytes];
		WriteNonce(nonce, header.PathId, header.MessageSequence);

		Span<byte> associatedData = stackalloc byte[15];
		WriteAssociatedData(associatedData, header.Token, header.PathId, header.DeliveryFlag, header.MessageSequence, header.PathSequence);

		try
		{
			_receive.Decrypt(
				nonce,
				RelayEnvelope.CiphertextOf(datagram),
				RelayEnvelope.TagOf(datagram),
				inner.Slice(0, datagram.Length - RelayEnvelope.HeaderBytes),
				associatedData);
		}
		catch (CryptographicException)
		{
			return false;
		}

		innerLength = datagram.Length - RelayEnvelope.HeaderBytes;
		return true;
	}

	/// <summary>
	///     Builds the nonce a datagram is sealed with. It is a function of the path and the message sequence, so it never
	///     repeats under one direction's key: the same sequence travels on several paths, and each of those copies is sealed
	///     under its own path's nonce.
	/// </summary>
	/// <param name="nonce">The twelve bytes to write.</param>
	/// <param name="pathId">The path the copy travels.</param>
	/// <param name="messageSequence">The sequence the duplicate window reads.</param>
	private static void WriteNonce(Span<byte> nonce, byte pathId, int messageSequence)
	{
		nonce[0] = pathId;
		nonce[1] = (byte)messageSequence;
		nonce[2] = (byte)(messageSequence >> 8);
		nonce[3] = (byte)(messageSequence >> 16);
		nonce[4] = 0;
		nonce[5] = 0;
		nonce[6] = 0;
		nonce[7] = 0;
		nonce[8] = 0;
		nonce[9] = 0;
		nonce[10] = 0;
		nonce[11] = 0;
	}

	/// <summary>Builds the bytes the tag proves, which are the header fields a volunteer could otherwise rewrite.</summary>
	/// <param name="associatedData">The fifteen bytes to write.</param>
	/// <param name="token">The connection the datagram belongs to.</param>
	/// <param name="pathId">The path the copy travels.</param>
	/// <param name="deliveryFlag">The guarantee the inner datagram needs, and whether this datagram is a probe.</param>
	/// <param name="messageSequence">The sequence the duplicate window reads.</param>
	/// <param name="pathSequence">The per-path sequence.</param>
	private static void WriteAssociatedData(Span<byte> associatedData, RelayConnectionToken token, byte pathId, byte deliveryFlag, int messageSequence, int pathSequence)
	{
		token.WriteTo(associatedData.Slice(0, 8));
		associatedData[8] = pathId;
		associatedData[9] = deliveryFlag;
		associatedData[10] = (byte)messageSequence;
		associatedData[11] = (byte)(messageSequence >> 8);
		associatedData[12] = (byte)(messageSequence >> 16);
		associatedData[13] = (byte)pathSequence;
		associatedData[14] = (byte)(pathSequence >> 8);
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_send.Dispose();
		_receive.Dispose();
	}
}
