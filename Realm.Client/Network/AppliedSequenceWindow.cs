namespace Realm.Client.Network;

public struct AppliedSequenceWindow
{
	public const int WindowSize = 128;

	private int _highestPlusOne;
	private ulong _recent;
	private ulong _older;

	public int Highest => _highestPlusOne - 1;

	public void Reset()
	{
		_highestPlusOne = 0;
		_recent = 0;
		_older = 0;
	}

	public void Advance(int sequence)
	{
		if (sequence < 0) return;

		if (_highestPlusOne == 0)
		{
			_highestPlusOne = sequence + 1;
			_recent = 0;
			_older = 0;
			return;
		}

		int shift = sequence - Highest;
		if (shift <= 0) return;

		if (shift >= WindowSize)
		{
			_recent = 0;
			_older = 0;
		}
		else if (shift >= 64)
		{
			_older = _recent << (shift - 64);
			_recent = 0;
		}
		else
		{
			_older = (_older << shift) | (_recent >> (64 - shift));
			_recent <<= shift;
		}

		_highestPlusOne = sequence + 1;
	}

	public void MarkApplied(int sequence) => Merge(sequence, 1UL);

	public void Merge(int sequence, ulong appliedMask)
	{
		if (sequence < 0) return;

		Advance(sequence);
		if (_highestPlusOne == 0) return;

		int offset = Highest - sequence;
		if (offset < 0 || offset >= WindowSize) return;

		if (offset == 0)
		{
			_recent |= appliedMask;
			return;
		}

		if (offset < 64)
		{
			_recent |= appliedMask << offset;
			_older |= appliedMask >> (64 - offset);
			return;
		}

		_older |= appliedMask << (offset - 64);
	}

	public bool Contains(int sequence)
	{
		if (_highestPlusOne == 0) return false;

		int offset = Highest - sequence;
		if (offset < 0 || offset >= WindowSize) return false;

		return offset < 64
			? ((_recent >> offset) & 1UL) != 0
			: ((_older >> (offset - 64)) & 1UL) != 0;
	}

	public ulong BuildMask() => _recent;
}
