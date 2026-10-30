using System;
using Realm.Shared.Network;

namespace Realm.Client.Network;

/// <summary>
///     Puts a description that travelled as several datagrams back together. The unordered stream delivers them in the
///     order they were sent, so a group completes as its last piece arrives; a group a piece short is dropped when the
///     next one needs the space, and the description it belonged to is reported missing by the acknowledgement like any
///     other that never arrived.
/// </summary>
public class SnapshotReassembler
{
	private const int MaximumPendingGroups = 4;

	private const int MaximumFragmentsPerGroup = 128;

	private const long StaleGroupMilliseconds = 2000;

	private readonly FragmentGroup[] _groups = new FragmentGroup[MaximumPendingGroups];

	private int _newestCompletedSequence;

	public SnapshotReassembler()
	{
		for (int index = 0; index < _groups.Length; index++) _groups[index] = new FragmentGroup();
	}

	/// <summary>
	///     Hands back the description this payload completes, or the payload itself when it was never split.
	/// </summary>
	public bool TryTake(byte[] payload, out byte[] frame)
	{
		frame = null;
		if (payload == null || payload.Length == 0) return false;

		if (!SnapshotFragment.TryReadHeader(payload, out int sequence, out int fragmentIndex, out int fragmentCount))
		{
			frame = payload;
			return true;
		}

		if (fragmentCount > MaximumFragmentsPerGroup || sequence <= _newestCompletedSequence) return false;

		FragmentGroup group = FindOrCreate(sequence, fragmentCount);
		if (group == null) return false;

		group.LastTouchedMilliseconds = Environment.TickCount64;
		if (group.Parts[fragmentIndex] == null)
		{
			group.Parts[fragmentIndex] = payload;
			group.Received++;
		}

		if (group.Received < group.Count) return false;

		frame = Assemble(group);
		_newestCompletedSequence = group.Sequence;
		group.Forget();
		return true;
	}

	private static byte[] Assemble(FragmentGroup group)
	{
		int totalBytes = 0;
		for (int index = 0; index < group.Parts.Length; index++)
		{
			totalBytes += group.Parts[index].Length - SnapshotFragment.HeaderBytes;
		}

		byte[] frame = new byte[totalBytes];
		int offset = 0;
		for (int index = 0; index < group.Parts.Length; index++)
		{
			ReadOnlySpan<byte> part = SnapshotFragment.ReadPayload(group.Parts[index]);
			part.CopyTo(frame.AsSpan(offset));
			offset += part.Length;
		}

		return frame;
	}

	private FragmentGroup? FindOrCreate(int sequence, int fragmentCount)
	{
		FragmentGroup? free = null;
		FragmentGroup? oldest = null;
		long now = Environment.TickCount64;

		for (int index = 0; index < _groups.Length; index++)
		{
			FragmentGroup group = _groups[index];

			if (group.Count == fragmentCount && group.Sequence == sequence && group.Received < group.Count) return group;

			if (group.Received == 0)
			{
				free ??= group;
				continue;
			}

			if (now - group.LastTouchedMilliseconds > StaleGroupMilliseconds)
			{
				group.Forget();
				free ??= group;
				continue;
			}

			if (oldest == null || group.LastTouchedMilliseconds < oldest.LastTouchedMilliseconds) oldest = group;
		}

		FragmentGroup? target = free ?? oldest;
		if (target == null) return null;

		target.Begin(sequence, fragmentCount);
		return target;
	}

	private class FragmentGroup
	{
		public int Sequence;

		public int Count;

		public int Received;

		public byte[][] Parts = Array.Empty<byte[]>();

		public long LastTouchedMilliseconds;

		public void Begin(int sequence, int fragmentCount)
		{
			Sequence = sequence;
			Count = fragmentCount;
			Received = 0;
			if (Parts.Length != fragmentCount) Parts = new byte[fragmentCount][];
			else Array.Clear(Parts);
		}

		public void Forget()
		{
			Sequence = 0;
			Count = 0;
			Received = 0;
			Array.Clear(Parts);
		}
	}
}
