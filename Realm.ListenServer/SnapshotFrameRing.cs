using System;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>
///     The last descriptions one peer was sent, kept so a disappearance the acknowledgement reports can be answered with
///     the description that went missing rather than with the whole world. A ring this shallow covers the round trip a
///     repair takes and no more: a hole deeper than it is a peer that stopped reading, and only a baseline puts that
///     screen where the match is.
/// </summary>
/// <remarks>
///     One ring is created per seat and it knows the peer it belongs to, so it cannot be claimed, reused or cleared by
///     any other peer. Addressing the ring by the peer rather than by the peer's place in a publish is what keeps a seat
///     taken or given up between two publishes from answering one screen with another's repair history.
/// </remarks>
public class SnapshotFrameRing
{
	public const int Capacity = MatchTiming.SnapshotHistoryDepth;

	private readonly int[] _sequences = new int[Capacity];
	private readonly byte[][] _frames = new byte[Capacity][];

	/// <param name="peerId">The one peer this ring keeps descriptions for, for as long as it exists.</param>
	public SnapshotFrameRing(int peerId)
	{
		PeerId = peerId;
		Array.Fill(_sequences, -1);
	}

	public int PeerId { get; }

	public void Add(int sequence, byte[] frame)
	{
		if (frame == null || frame.Length == 0) return;
		if (sequence <= 0) return;

		// A sequence lives in the slot its own value names, so finding one again is an index rather than a walk of the
		// whole ring: a repair asks for up to a ring's worth of sequences at once, and each of those asks walked all of
		// them again.
		int slot = sequence % Capacity;
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
