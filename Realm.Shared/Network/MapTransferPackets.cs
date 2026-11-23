using System;
using System.Collections.Generic;

namespace Realm.Shared.Network;

/// <summary>
///     Splits one map bundle into pieces that each fit a single transport message, and joins those pieces back into the
///     bundle they were cut from. A bundle is bounded by what a map chunk holds, which is far larger than one message a
///     Steam peer connection will carry, so the pieces are the units that actually travel and the bundle is only ever the
///     thing reassembled on the arriving side.
/// </summary>
public static class MapTransferPackets
{
	/// <summary>
	///     The bytes one piece carries. It is stated well under the largest message Steam will send, which leaves room for
	///     the frame header and the rest of the reply that carries it.
	/// </summary>
	public const int PieceBytes = 256 * 1024;

	/// <summary>
	///     How many pieces a bundle of the given size travels as, which is also the count a bundle of zero bytes answers,
	///     because a reply that carries no bundle is answered as a refusal rather than as an empty transfer.
	/// </summary>
	/// <param name="bundleLength">The bundle's size in bytes.</param>
	/// <returns>The number of pieces.</returns>
	public static int CountFor(int bundleLength) => bundleLength <= 0 ? 0 : (bundleLength + PieceBytes - 1) / PieceBytes;

	/// <summary>
	///     Reads the bytes of one piece out of the bundle it belongs to.
	/// </summary>
	/// <param name="bundle">The whole bundle.</param>
	/// <param name="pieceIndex">The piece to read, counted from zero.</param>
	/// <returns>The piece's bytes, or an empty array when the index is not a piece of this bundle.</returns>
	public static byte[] Slice(byte[] bundle, int pieceIndex)
	{
		if (bundle == null || pieceIndex < 0) return Array.Empty<byte>();

		long offset = (long)pieceIndex * PieceBytes;
		if (offset >= bundle.Length) return Array.Empty<byte>();

		int length = (int)Math.Min(PieceBytes, bundle.Length - offset);
		var piece = new byte[length];
		Array.Copy(bundle, offset, piece, 0, length);
		return piece;
	}

	/// <summary>
	///     Joins the pieces of one bundle back into it, in the order they were counted.
	/// </summary>
	/// <param name="pieces">Every piece of one bundle, starting with piece zero.</param>
	/// <returns>The bundle, or an empty array when no piece was given.</returns>
	public static byte[] Join(IReadOnlyList<byte[]> pieces)
	{
		if (pieces == null || pieces.Count == 0) return Array.Empty<byte>();

		long total = 0;
		foreach (byte[] piece in pieces) total += piece?.Length ?? 0;
		if (total <= 0 || total > int.MaxValue) return Array.Empty<byte>();

		var bundle = new byte[total];
		int offset = 0;
		foreach (byte[] piece in pieces)
		{
			if (piece == null || piece.Length == 0) continue;
			Array.Copy(piece, 0, bundle, offset, piece.Length);
			offset += piece.Length;
		}

		return bundle;
	}
}
