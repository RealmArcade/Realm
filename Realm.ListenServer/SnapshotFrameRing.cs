using System;

namespace Realm.ListenServer;

/// <summary>
///     The last descriptions one peer was sent, kept so a disappearance the acknowledgement reports can be answered with
///     the description that went missing rather than with the whole world. A ring this shallow covers the round trip a
///     repair takes and no more: a hole deeper than it is a peer that stopped reading, and only a baseline puts that
///     screen where the match is.
/// </summary>
public class SnapshotFrameRing
{
	public const int Capacity = 24;

	private readonly int[] _sequences = new int[Capacity];
	private readonly byte[][] _frames = new byte[Capacity][];

	private int _ownerPeerId = -1;
	private int _next;

	public SnapshotFrameRing() => Array.Fill(_sequences, -1);

	public int PeerId => _ownerPeerId;

	public int Count { get; private set; }

	public void Claim(int peerId)
	{
		if (_ownerPeerId == peerId) return;

		Clear();
		_ownerPeerId = peerId;
	}

	public void Clear()
	{
		Array.Clear(_frames);
		Array.Fill(_sequences, -1);
		_next = 0;
		Count = 0;
	}

	public void Add(int sequence, byte[] frame)
	{
		if (frame == null || frame.Length == 0) return;

		_sequences[_next] = sequence;
		_frames[_next] = frame;
		_next = (_next + 1) % Capacity;
		if (Count < Capacity) Count++;
	}

	public bool TryGet(int sequence, out byte[] frame)
	{
		for (int index = 0; index < Capacity; index++)
		{
			if (_sequences[index] != sequence) continue;

			frame = _frames[index];
			return frame != null;
		}

		frame = Array.Empty<byte>();
		return false;
	}
}
