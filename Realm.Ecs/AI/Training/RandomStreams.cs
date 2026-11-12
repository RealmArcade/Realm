namespace Realm.Ecs.AI.Training;

public static class RandomStreams
{
	public const int DefaultSeed = 1;

	private const ulong GOLDEN_GAMMA = 0x9E3779B97F4A7C15UL;
	private const ulong MULTIPLIER_ONE = 0xBF58476D1CE4E5B9UL;
	private const ulong MULTIPLIER_TWO = 0x94D049BB133111EBUL;

	public static int CreateBaseSeed(int seed)
	{
		return seed != 0 ? seed : DefaultSeed;
	}

	public static SeededRandom Derive(int baseSeed, int salt)
	{
		ulong value = (ulong)(uint)baseSeed + GOLDEN_GAMMA * (ulong)(uint)(salt + 1);
		value = (value ^ (value >> 30)) * MULTIPLIER_ONE;
		value = (value ^ (value >> 27)) * MULTIPLIER_TWO;
		value ^= value >> 31;
		return new SeededRandom(unchecked((int)value));
	}
}
