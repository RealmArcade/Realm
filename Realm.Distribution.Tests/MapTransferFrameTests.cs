using System.Collections.Generic;
using MemoryPack;
using NUnit.Framework;
using Realm.Shared.Distribution;
using Realm.Shared.Network;

namespace Realm.Distribution.Tests;

/// <summary>
///     A map transfer is the one frame kind whose payload is sized by a map rather than by the match, and a transport that
///     refuses a frame answers with nothing at all: the joiner waits out its timeout and reports that the lobby never
///     answered. A bundle therefore travels as pieces that each fit one message, and these hold that contract.
/// </summary>
[TestFixture]
public class MapTransferFrameTests
{
	[Test]
	public void EveryPieceOfABundleFitsOneSteamMessage()
	{
		byte[] bundle = ZstdAssetBundleHelper.CreateBundleBytes(new[]
		{
			("a.rtex", IncompressibleBytes(2 * 1024 * 1024, 1), (string?)null),
			("b.rmesh", IncompressibleBytes(2 * 1024 * 1024, 2), (string?)null),
			("c.raud", IncompressibleBytes(1024 * 1024, 3), (string?)null)
		});

		int pieceCount = MapTransferPackets.CountFor(bundle.Length);
		Assert.That(pieceCount, Is.GreaterThan(1), "a bundle this size has to travel as several pieces");

		var pieces = new List<byte[]>(pieceCount);
		for (int pieceIndex = 0; pieceIndex < pieceCount; pieceIndex++)
		{
			byte[] piece = MapTransferPackets.Slice(bundle, pieceIndex);
			pieces.Add(piece);

			var reply = new MapTransferReply
			{
				Kind = MapTransferReplyKind.Chunk,
				TransferId = "transfer",
				MapName = "map",
				ChunkIndex = 0,
				TotalChunks = 1,
				PacketIndex = pieceIndex,
				PacketCount = pieceCount,
				TotalBytes = bundle.Length,
				Bundle = piece
			};

			int frameBytes = SteamFrameCodec.HeaderBytes + MemoryPackSerializer.Serialize(reply).Length;
			Assert.That(frameBytes, Is.LessThanOrEqualTo(LobbyLimits.MaximumMapTransferFrameBytes), $"piece {pieceIndex} of {pieceCount} would travel as {frameBytes} bytes");
		}

		Assert.That(MapTransferPackets.Join(pieces), Is.EqualTo(bundle), "the pieces rejoin into the bundle they were cut from");
	}

	[Test]
	public void APieceSizeLeavesRoomForTheFrameAroundIt()
	{
		Assert.That(MapTransferPackets.PieceBytes + 1024, Is.LessThanOrEqualTo(LobbyLimits.MaximumMapTransferFrameBytes), "a piece plus its frame has to stay inside one message");
	}

	[Test]
	public void PiecesOutsideTheBundleAnswerNothing()
	{
		byte[] bundle = IncompressibleBytes(MapTransferPackets.PieceBytes + 64, 4);

		Assert.That(MapTransferPackets.CountFor(0), Is.Zero);
		Assert.That(MapTransferPackets.Slice(bundle, -1), Is.Empty);
		Assert.That(MapTransferPackets.Slice(bundle, MapTransferPackets.CountFor(bundle.Length)), Is.Empty);
		Assert.That(MapTransferPackets.Join(new List<byte[]>()), Is.Empty);
	}

	private static byte[] IncompressibleBytes(int length, int seed)
	{
		byte[] bytes = new byte[length];
		new System.Random(seed).NextBytes(bytes);
		return bytes;
	}
}
