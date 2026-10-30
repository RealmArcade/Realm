using System;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>
///     The buffers one peer's description is split across. They belong to the peer rather than to the call that fills them
///     because a busy match splits a description every tick, and a fresh set of datagrams per peer per tick is the whole
///     cost this avoids.
/// </summary>
public class SnapshotFragmentBuffer
{
	private byte[][] _datagrams = Array.Empty<byte[]>();

	public int Count { get; private set; }

	public ReadOnlySpan<byte[]> Datagrams => _datagrams.AsSpan(0, Count);

	public int Split(byte[] frame, int sequence)
	{
		int count = SnapshotFragment.CountFor(frame.Length);
		if (_datagrams.Length < count) Array.Resize(ref _datagrams, count);
		for (int index = 0; index < count; index++)
		{
			int length = SnapshotFragment.HeaderBytes + SnapshotFragment.PayloadLengthFor(frame.Length, index);
			byte[] datagram = _datagrams[index];
			if (datagram == null || datagram.Length != length) datagram = _datagrams[index] = new byte[length];
			SnapshotFragment.Write(frame, sequence, index, count, datagram);
		}

		Count = count;
		return count;
	}
}
