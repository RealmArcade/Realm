using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Realm.Shared.Network.Relay;

/// <summary>
///     The identifier a relayed connection is known by at the path layer. Both peers agree on one during the join, a
///     volunteer forwards datagrams by it without knowing which match they belong to, and it is the name every relayed
///     datagram carries so that a carrier can tell two connections it is carrying apart.
/// </summary>
public readonly record struct RelayConnectionToken(ulong Value)
{
	/// <summary>The characters the token's hexadecimal text form is made of.</summary>
	public const int TextCharacters = 16;

	/// <summary>The token that names no connection, which is what a default value of the type is.</summary>
	public static RelayConnectionToken None => default;

	/// <summary>Whether this token names a connection rather than being the absent one.</summary>
	public bool IsSome => Value != 0;

	/// <summary>Creates a token no other connection is expected to hold.</summary>
	/// <returns>A fresh random token.</returns>
	public static RelayConnectionToken Create()
	{
		Span<byte> bytes = stackalloc byte[sizeof(ulong)];
		RandomNumberGenerator.Fill(bytes);
		ulong value = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
		return new RelayConnectionToken(value == 0 ? 1UL : value);
	}

	/// <summary>Gets the token as the lowercase hexadecimal text its wire forms use.</summary>
	/// <returns>Sixteen hexadecimal characters.</returns>
	public string ToHex()
	{
		return Value.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
	}

	/// <summary>Reads a token from the hexadecimal text another process stated.</summary>
	/// <param name="text">The text to read, at most sixteen hexadecimal characters.</param>
	/// <param name="token">The token the text names, or the absent token when it names none.</param>
	/// <returns>Whether the text named a token.</returns>
	public static bool TryParse(string? text, out RelayConnectionToken token)
	{
		token = None;
		if (string.IsNullOrWhiteSpace(text) || text.Length > TextCharacters) return false;

		if (!ulong.TryParse(text, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out ulong value)) return false;
		if (value == 0) return false;

		token = new RelayConnectionToken(value);
		return true;
	}

	/// <summary>Writes the token into the bytes of a relayed datagram's header.</summary>
	/// <param name="destination">The buffer the token is written into, which must hold eight bytes.</param>
	public void WriteTo(Span<byte> destination) => BinaryPrimitives.WriteUInt64LittleEndian(destination, Value);

	/// <summary>Reads the token from the bytes of a relayed datagram's header.</summary>
	/// <param name="source">The buffer the token is read from, which must hold eight bytes.</param>
	/// <returns>The token those bytes name.</returns>
	public static RelayConnectionToken ReadFrom(ReadOnlySpan<byte> source) => new(BinaryPrimitives.ReadUInt64LittleEndian(source));

	public override string ToString() => ToHex();
}
