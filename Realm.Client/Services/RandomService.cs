using System;
using Realm.MapAPI;

namespace Realm.Client.Services
{
	public class RandomProxy
	{
		private static RandomProxy? _shared;
		private readonly DeterministicRandom _stream;

		public static RandomProxy Shared => _shared ??= new RandomProxy(DeterministicRandom.Shared);

		public RandomProxy() : this(DeterministicRandom.Shared)
		{
		}

		public RandomProxy(int seed) : this(new DeterministicRandom(seed))
		{
		}

		private RandomProxy(DeterministicRandom stream)
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

		public int Seed => DeterministicRandom.Shared.Seed;

		public long DrawCount => DeterministicRandom.Shared.DrawCount;

		public void SetSeed(int? seed)
		{
			int resolvedSeed = seed.HasValue && seed.Value != 0 ? seed.Value : System.Random.Shared.Next(1, int.MaxValue);
			EcsSharedMemory.RandomSeed = resolvedSeed;
			DeterministicRandom.Shared.Reset(resolvedSeed);
			Rng = RandomProxy.Shared;
			Godot.GD.Print($"[RandomService] Deterministic random seed {resolvedSeed}.");
		}
	}
}
