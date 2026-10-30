using System;
using System.Text;

namespace Realm.Client.Services.MapGeneration;

public static class DeterministicSeed
{
	public static int HashText(string? seedText)
	{
		if (string.IsNullOrEmpty(seedText))
		{
			return unchecked((int)2166136261u);
		}

		ReadOnlySpan<byte> bytes = Encoding.UTF8.GetBytes(seedText);
		uint hash = 2166136261u;
		for (int byteIndex = 0; byteIndex < bytes.Length; byteIndex++)
		{
			hash ^= bytes[byteIndex];
			hash *= 16777619u;
		}

		hash ^= hash >> 16;
		hash *= 0x7feb352du;
		hash ^= hash >> 15;
		return unchecked((int)hash);
	}
}

public struct SeededRandom
{
	private ulong _state;

	public SeededRandom(int seed)
	{
		_state = (uint)seed;
		if (_state == 0)
		{
			_state = 0x9e3779b97f4a7c15UL;
		}

		NextUInt();
	}

	public uint NextUInt()
	{
		ulong state = _state;
		state ^= state << 13;
		state ^= state >> 7;
		state ^= state << 17;
		_state = state;
		return (uint)(state >> 32);
	}

	public float NextFloat()
	{
		return (NextUInt() & 0xFFFFFF) / 16777216.0f;
	}

	public float NextFloat(float minimum, float maximum)
	{
		return minimum + NextFloat() * (maximum - minimum);
	}

	public int NextInt(int maximumExclusive)
	{
		if (maximumExclusive <= 0)
		{
			return 0;
		}

		return (int)(NextUInt() % (uint)maximumExclusive);
	}
}
