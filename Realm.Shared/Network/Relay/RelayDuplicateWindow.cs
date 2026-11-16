using System;

namespace Realm.Shared.Network.Relay;

/// <summary>
///     The window that keeps one connection's inner datagrams at most once, however many paths carried copies of them. A
///     copy whose sequence the window already holds is a duplicate and is dropped; a copy older than the window is dropped
///     as well, which is what makes a replayed datagram useless to whoever replays it.
/// </summary>
public sealed class RelayDuplicateWindow
{
	private const int WordBits = 64;

	/// <summary>
	///     The copies the window remembers behind the newest one it has seen. It is one machine word, which is what the
	///     window is: a distance wider than the word would be shifted past the bits that exist, so the stated capacity and the
	///     implemented one are the same number rather than two numbers that only agree by accident.
	/// </summary>
	public const int Capacity = WordBits;

	private ulong _heard = 1UL;
	private int _newestSequence;
	private bool _started;

	/// <summary>How many copies this window has refused because they were already delivered.</summary>
	public int DuplicateCount { get; private set; }

	/// <summary>How many copies this window has refused because they were older than the window.</summary>
	public int StaleCount { get; private set; }

	/// <summary>How many distinct inner datagrams this window has accepted.</summary>
	public int AcceptedCount { get; private set; }

	/// <summary>
	///     Takes one message sequence and answers whether its inner datagram is new. The window moves forward when the
	///     sequence is newer than the newest it has seen and is left alone when the sequence is old.
	/// </summary>
	/// <param name="messageSequence">The sequence the copy carried.</param>
	/// <returns>Whether the copy may be delivered.</returns>
	public bool TryAccept(int messageSequence)
	{
		if (!_started)
		{
			_started = true;
			_newestSequence = messageSequence;
			_heard = 1UL;
			AcceptedCount++;
			return true;
		}

		int distance = SequenceDistance(messageSequence, _newestSequence);
		if (distance > 0)
		{
			if (distance >= WordBits)
			{
				_heard = 1UL;
			}
			else
			{
				_heard <<= distance;
				_heard |= 1UL;
			}

			_newestSequence = messageSequence;
			AcceptedCount++;
			return true;
		}

		int behind = -distance;
		if (behind >= WordBits)
		{
			StaleCount++;
			return false;
		}

		ulong bit = 1UL << behind;
		if ((_heard & bit) != 0)
		{
			DuplicateCount++;
			return false;
		}

		_heard |= bit;
		AcceptedCount++;
		return true;
	}

	/// <summary>Forgets every copy the window holds, which is what a fresh connection starts from.</summary>
	public void Reset()
	{
		_heard = 1UL;
		_newestSequence = 0;
		_started = false;
		DuplicateCount = 0;
		StaleCount = 0;
		AcceptedCount = 0;
	}

	/// <summary>
	///     Measures how far one sequence is ahead of another, honouring the wrap the three-byte wire sequence makes. The
	///     answer is positive when the first is newer, negative when it is older, and zero when they are the same.
	/// </summary>
	/// <param name="sequence">The sequence being placed.</param>
	/// <param name="reference">The sequence it is placed against.</param>
	/// <returns>The signed distance in the sequence space.</returns>
	private static int SequenceDistance(int sequence, int reference)
	{
		const int half = (RelayEnvelope.MessageSequenceMask + 1) / 2;

		int forward = (sequence - reference) & RelayEnvelope.MessageSequenceMask;
		if (forward == 0) return 0;

		return forward < half ? forward : forward - (RelayEnvelope.MessageSequenceMask + 1);
	}

	/// <summary>The bits the window currently holds, which the diagnostics report as the replay window's occupancy.</summary>
	/// <returns>The occupancy mask behind the newest accepted sequence.</returns>
	public ulong Occupancy() => _heard;
}
