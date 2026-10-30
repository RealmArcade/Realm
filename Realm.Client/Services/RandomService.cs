using System;
using Realm.Ecs;

namespace Realm.Client.Services
{
	public class RandomProxy
	{
		private static RandomProxy? _shared;
		private readonly SeededRandom _stream;

		public static RandomProxy Shared => _shared ??= new RandomProxy(SeededRandom.Shared);

		public RandomProxy() : this(new SeededRandom(CreateIndependentSeed()))
		{
		}

		private static int CreateIndependentSeed() => System.Random.Shared.Next(int.MinValue, int.MaxValue);

		public RandomProxy(int seed) : this(new SeededRandom(seed))
		{
		}

		private RandomProxy(SeededRandom stream)
		{
			_stream = stream;
		}

		public int Next() => _stream.Next();
		public int Next(int maxValue) => _stream.Next(maxValue);
		public int Next(int minValue, int maxValue) => _stream.Next(minValue, maxValue);
		public int NextInt(int minValueInclusive, int maxValueInclusive) => _stream.NextInt(minValueInclusive, maxValueInclusive);
		public double NextDouble() => _stream.NextDouble();
		public float NextSingle() => _stream.NextSingle();
		public void NextBytes(byte[] buffer) => _stream.NextBytes(buffer);
		public void NextBytes(Span<byte> buffer) => _stream.NextBytes(buffer);
		public long NextInt64() => _stream.NextInt64();
		public long NextInt64(long maxValue) => _stream.NextInt64(maxValue);
		public long NextInt64(long minValue, long maxValue) => _stream.NextInt64(minValue, maxValue);
		public void Shuffle<T>(T[] array) => _stream.Shuffle(array);
		public void Shuffle<T>(Span<T> values) => _stream.Shuffle(values);
		public T[] GetItems<T>(T[] choices, int length) => _stream.GetItems(choices, length);
		public T[] GetItems<T>(ReadOnlySpan<T> choices, int length) => _stream.GetItems(choices, length);
		public void GetItems<T>(ReadOnlySpan<T> choices, Span<T> destination) => _stream.GetItems(choices, destination);
	}

	public class RandomService
	{
		public RandomProxy Rng { get; private set; } = RandomProxy.Shared;

		public int Seed => SeededRandom.Shared.Seed;

		public long DrawCount => SeededRandom.Shared.DrawCount;

		public void SetSeed(int? seed)
		{
			int resolvedSeed = seed.HasValue && seed.Value != 0 ? seed.Value : System.Random.Shared.Next(1, int.MaxValue);
			EcsSharedMemory.RandomSeed = resolvedSeed;
			SeededRandom.Shared.Reset(resolvedSeed);
			Rng = RandomProxy.Shared;
			Godot.GD.Print($"[RandomService] Deterministic random seed {resolvedSeed}.");
		}
	}
}
