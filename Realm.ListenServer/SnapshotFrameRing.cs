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
		Count = 0;
	}

	public void Add(int sequence, byte[] frame)
	{
		if (frame == null || frame.Length == 0) return;
		if (sequence <= 0) return;

		// A sequence lives in the slot its own value names, so finding one again is an index rather than a walk of the
		// whole ring: a repair asks for up to a ring's worth of sequences at once, and each of those asks walked all of
		// them again.
		int slot = sequence % Capacity;
		if (_sequences[slot] < 0) Count++;
		_sequences[slot] = sequence;
		_frames[slot] = frame;
	}

	public bool TryGet(int sequence, out byte[] frame)
	{
		frame = Array.Empty<byte>();
		if (sequence <= 0) return false;

		int slot = sequence % Capacity;
		if (_sequences[slot] != sequence) return false;

		byte[]? held = _frames[slot];
		if (held == null) return false;

		frame = held;
		return true;
	}
}
