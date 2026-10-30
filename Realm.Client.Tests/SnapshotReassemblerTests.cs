using System;
using System.Collections.Generic;
using GdUnit4;
using Realm.Client.Network;
using Realm.Shared.Network;

namespace Realm.Client.Tests;

[TestSuite]
public partial class SnapshotReassemblerTests
{
	private static byte[] Frame(int seed, int length)
	{
		var frame = new byte[length];
		for (int index = 0; index < length; index++) frame[index] = (byte)((seed * 31 + index) % 251);
		return frame;
	}

	private static List<byte[]> Pieces(byte[] frame, int sequence)
	{
		int count = SnapshotFragment.CountFor(frame.Length);
		var pieces = new List<byte[]>(count);
		for (int index = 0; index < count; index++)
		{
			int length = SnapshotFragment.HeaderBytes + SnapshotFragment.PayloadLengthFor(frame.Length, index);
			var datagram = new byte[length];
			SnapshotFragment.Write(frame, sequence, index, count, datagram);
			pieces.Add(datagram);
		}

		return pieces;
	}

	private static bool SameBytes(byte[] actual, byte[] expected) => actual != null && expected != null && actual.AsSpan().SequenceEqual(expected);

	[TestCase]
	public void ADescriptionWholeEnoughForOneDatagramTravelsAsItself()
	{
		var reassembler = new SnapshotReassembler();
		byte[] frame = Frame(1, SnapshotFragment.MaximumDatagramBytes);

		Assertions.AssertThat(reassembler.TryTake(frame, out byte[] handed)).IsTrue();
		Assertions.AssertThat(SameBytes(handed, frame)).OverrideFailureMessage("the whole description was not handed back unchanged").IsTrue();
	}

	[TestCase]
	public void ADescriptionSplitIntoPiecesIsPutBackTogetherExactly()
	{
		var reassembler = new SnapshotReassembler();
		byte[] frame = Frame(7, 5000);
		List<byte[]> pieces = Pieces(frame, 100);

		Assertions.AssertThat(pieces.Count > 1).OverrideFailureMessage("the frame was not split, so nothing was reassembled").IsTrue();

		byte[] assembled = null;
		bool completed = false;
		for (int index = 0; index < pieces.Count; index++) completed = reassembler.TryTake(pieces[index], out assembled);

		Assertions.AssertThat(completed).OverrideFailureMessage("the pieces never completed a description").IsTrue();
		Assertions.AssertThat(SameBytes(assembled, frame)).OverrideFailureMessage("the reassembled description does not match the one that was split").IsTrue();
	}

	[TestCase]
	public void ADuplicatePieceDoesNotFinishAGroupBeforeItsLastPieceArrives()
	{
		var reassembler = new SnapshotReassembler();
		byte[] frame = Frame(9, 4000);
		List<byte[]> pieces = Pieces(frame, 50);

		Assertions.AssertThat(reassembler.TryTake(pieces[0], out _)).IsFalse();
		Assertions.AssertThat(reassembler.TryTake(pieces[0], out _)).OverrideFailureMessage("a duplicate piece completed a group that was still missing pieces").IsFalse();

		for (int index = 1; index < pieces.Count; index++)
		{
			bool completed = reassembler.TryTake(pieces[index], out byte[] assembled);
			if (!completed) continue;

			Assertions.AssertThat(index == pieces.Count - 1).OverrideFailureMessage("the group completed before its last piece arrived").IsTrue();
			Assertions.AssertThat(SameBytes(assembled, frame)).IsTrue();
			return;
		}

		Assertions.AssertThat(false).OverrideFailureMessage("the run never completed once every piece had arrived").IsTrue();
	}

	[TestCase]
	public void ARepairOfAnOlderDescriptionIsStillPutBackTogether()
	{
		var reassembler = new SnapshotReassembler();
		byte[] newer = Frame(3, 6000);
		byte[] older = Frame(4, 7000);

		foreach (byte[] piece in Pieces(newer, 200)) reassembler.TryTake(piece, out _);

		byte[] repaired = null;
		bool completed = false;
		foreach (byte[] piece in Pieces(older, 197)) completed = reassembler.TryTake(piece, out repaired);

		Assertions.AssertThat(completed)
			.OverrideFailureMessage("a repair of an older description was dropped because a newer one had already been put back together, so the hole it was asked to fill could never heal")
			.IsTrue();
		Assertions.AssertThat(SameBytes(repaired, older)).IsTrue();
	}

	[TestCase]
	public void ADescriptionWiderThanTheScreenCanAssembleIsRefused()
	{
		var reassembler = new SnapshotReassembler();
		var datagram = new byte[SnapshotFragment.HeaderBytes + SnapshotFragment.FragmentPayloadBytes];
		SnapshotFragment.Write(
			new byte[SnapshotFragment.FragmentPayloadBytes],
			sequence: 300,
			fragmentIndex: 0,
			fragmentCount: SnapshotFragment.MaximumFragmentsPerDescription + 1,
			datagram);

		Assertions.AssertThat(reassembler.TryTake(datagram, out _))
			.OverrideFailureMessage("a description split wider than the screen is built to reassemble was accepted as a piece")
			.IsFalse();
	}

	[TestCase]
	public void TheCeilingBothEndsStateIsTheOneTheMatchSplitsWith()
	{
		Assertions.AssertThat(SnapshotFragment.CountFor(SnapshotFragment.MaximumReassembledBytes))
			.OverrideFailureMessage("the widest description a screen can reassemble needs more pieces than the screen accepts")
			.IsEqual(SnapshotFragment.MaximumFragmentsPerDescription);

		Assertions.AssertThat(SnapshotFragment.MaximumReassembledBytes <= SnapshotWireFormat.MaximumSerializedSnapshotBytes)
			.OverrideFailureMessage("a description the reassembler accepts can be wider than a snapshot is ever allowed to be")
			.IsTrue();
	}
}
