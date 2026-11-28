namespace Realm.MapGeneration;

public static class DeterministicHash
{
	public static uint Combine(int x, int z, int seed)
	{
		unchecked
		{
			uint hash = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)(seed * 83492791);
			hash ^= hash >> 13;
			hash *= 0x85ebca6bu;
			hash ^= hash >> 16;
			return hash;
		}
	}

	public static float Unit(int x, int z, int seed)
	{
		return (Combine(x, z, seed) & 0xFFFFFF) / 16777215f;
	}

	public static float Jitter(int x, int z, int seed, int channel)
	{
		return Unit(x * 31 + channel, z * 17 + channel * 7, seed);
	}
}
