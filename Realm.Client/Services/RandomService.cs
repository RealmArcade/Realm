using System;
using Realm.MapAPI;

namespace Realm.Client.Services
{
	public class RandomProxy
	{
		private System.Random _rng;

		private static RandomProxy? _shared;
		public static RandomProxy Shared
		{
			get
			{
				if (_shared == null)
				{
					int seed = EcsSharedMemory.RandomSeed;
					_shared = seed != 0 ? new RandomProxy(seed) : new RandomProxy();
				}
				return _shared;
			}
			internal set => _shared = value;
		}

		public RandomProxy()
		{
			int seed = EcsSharedMemory.RandomSeed;
			_rng = seed != 0 ? new System.Random(seed) : new System.Random();
		}

		public RandomProxy(int seed)
		{
			_rng = new System.Random(seed);
		}

		public int Next() => GetRng().Next();
		public int Next(int maxValue) => GetRng().Next(maxValue);
		public int Next(int minValue, int maxValue) => GetRng().Next(minValue, maxValue);
		public double NextDouble() => GetRng().NextDouble();
		public float NextSingle() => GetRng().NextSingle();
		public void NextBytes(byte[] buffer) => GetRng().NextBytes(buffer);
		public void NextBytes(Span<byte> buffer) => GetRng().NextBytes(buffer);
		public long NextInt64() => GetRng().NextInt64();
		public long NextInt64(long maxValue) => GetRng().NextInt64(maxValue);
		public long NextInt64(long minValue, long maxValue) => GetRng().NextInt64(minValue, maxValue);
		public void Shuffle<T>(T[] array) => GetRng().Shuffle(array);
		public void Shuffle<T>(Span<T> values) => GetRng().Shuffle(values);
		public T[] GetItems<T>(T[] choices, int length) => GetRng().GetItems(choices, length);
		public T[] GetItems<T>(ReadOnlySpan<T> choices, int length) => GetRng().GetItems(choices, length);
		public void GetItems<T>(ReadOnlySpan<T> choices, Span<T> destination) => GetRng().GetItems(choices, destination);

		private System.Random GetRng()
		{
			if (_rng == null)
			{
				int seed = EcsSharedMemory.RandomSeed;
				_rng = seed != 0 ? new System.Random(seed) : new System.Random();
			}
			return _rng;
		}
	}

	public class RandomService
	{
		public RandomProxy Rng { get; private set; } = new();

		public void SetSeed(int? seed)
		{
			if (seed.HasValue && seed.Value != 0)
			{
				EcsSharedMemory.RandomSeed = seed.Value;
				Rng = new RandomProxy(seed.Value);
				RandomProxy.Shared = new RandomProxy(seed.Value);
			}
			else
			{
				EcsSharedMemory.RandomSeed = 0;
				Rng = new RandomProxy();
				RandomProxy.Shared = new RandomProxy();
			}
		}
	}
}