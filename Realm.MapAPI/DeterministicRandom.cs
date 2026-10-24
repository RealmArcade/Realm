using System;

namespace Realm.MapAPI;

/// <summary>
/// Provides a deterministic pseudo random stream whose sequence is identical on the host simulation and in WebAssembly map scripts for the same seed.
/// </summary>
/// <remarks>
/// The generator is a fixed SplitMix64 implementation rather than <see cref="System.Random"/>, so the produced sequence does not depend on the runtime version,
/// the platform, or whether the code executes inside WebAssembly. Host code and map scripts must draw from <see cref="Shared"/> so that both sides advance the
/// same logical stream and client-side movement prediction cannot diverge because of a random draw.
/// </remarks>
public class DeterministicRandom
{
	private const ulong GOLDEN_GAMMA = 0x9E3779B97F4A7C15UL;
	private const ulong MULTIPLIER_ONE = 0xBF58476D1CE4E5B9UL;
	private const ulong MULTIPLIER_TWO = 0x94D049BB133111EBUL;
	private const double DOUBLE_SCALE = 1.0 / 9007199254740992.0;
	private const float SINGLE_SCALE = 1.0f / 16777216.0f;

	private static DeterministicRandom? _shared;

	private ulong _state;
	private int _seed;
	private long _drawCount;

	/// <summary>
	/// Initializes a new deterministic stream with the given seed.
	/// </summary>
	/// <param name="seed">The seed that determines the sequence. The same seed always produces the same sequence.</param>
	public DeterministicRandom(int seed)
	{
		Reset(seed);
	}

	/// <summary>
	/// Gets the single stream shared by the host simulation and the WebAssembly map script.
	/// </summary>
	/// <remarks>
	/// The stream seeds itself from <see cref="EcsSharedMemory.RandomSeed"/> on first access, so the seed published by the host is the one the map script uses.
	/// </remarks>
	public static DeterministicRandom Shared
	{
		get
		{
			DeterministicRandom? shared = _shared;
			if (shared == null)
			{
				shared = new DeterministicRandom(EcsSharedMemory.RandomSeed);
				_shared = shared;
			}
			return shared;
		}
	}

	/// <summary>
	/// Gets the seed currently driving this stream.
	/// </summary>
	/// <value>The seed supplied to <see cref="Reset(int)"/> or to the constructor.</value>
	public int Seed => _seed;

	/// <summary>
	/// Gets the number of values drawn from this stream since it was last reset.
	/// </summary>
	/// <value>A monotonically increasing draw counter that is useful when debugging sequence divergence.</value>
	public long DrawCount => _drawCount;

	/// <summary>
	/// Restarts the stream at the given seed, discarding any previously drawn values.
	/// </summary>
	/// <param name="seed">The seed that determines the sequence. Two streams reset with the same seed produce identical values.</param>
	public void Reset(int seed)
	{
		_seed = seed;
		_state = (ulong)(uint)seed;
		_drawCount = 0L;
	}

	/// <summary>
	/// Returns the next raw 64-bit value from the stream and advances the stream exactly once.
	/// </summary>
	/// <returns>A pseudo random 64-bit value.</returns>
	public ulong NextUInt64()
	{
		_state += GOLDEN_GAMMA;
		ulong value = _state;
		value = (value ^ (value >> 30)) * MULTIPLIER_ONE;
		value = (value ^ (value >> 27)) * MULTIPLIER_TWO;
		_drawCount++;
		return value ^ (value >> 31);
	}

	/// <summary>
	/// Returns the next raw 32-bit value from the stream and advances the stream exactly once.
	/// </summary>
	/// <returns>A pseudo random 32-bit value.</returns>
	public uint NextUInt32() => (uint)(NextUInt64() >> 32);

	/// <summary>
	/// Returns a non-negative random integer below <see cref="int.MaxValue"/>.
	/// </summary>
	/// <returns>A pseudo random integer in the range [0, <see cref="int.MaxValue"/>).</returns>
	public int Next() => Next(0, int.MaxValue);

	/// <summary>
	/// Returns a non-negative random integer below the exclusive upper bound.
	/// </summary>
	/// <param name="maxValueExclusive">The exclusive upper bound. Values of zero or less return zero.</param>
	/// <returns>A pseudo random integer in the range [0, <paramref name="maxValueExclusive"/>).</returns>
	public int Next(int maxValueExclusive) => maxValueExclusive <= 0 ? 0 : Next(0, maxValueExclusive);

	/// <summary>
	/// Returns a random integer in the half-open range [<paramref name="minValueInclusive"/>, <paramref name="maxValueExclusive"/>).
	/// </summary>
	/// <param name="minValueInclusive">The inclusive lower bound.</param>
	/// <param name="maxValueExclusive">The exclusive upper bound.</param>
	/// <returns>A pseudo random integer in the requested range; the lower bound when the range is empty.</returns>
	public int Next(int minValueInclusive, int maxValueExclusive)
	{
		if (maxValueExclusive <= minValueInclusive) return minValueInclusive;
		long range = (long)maxValueExclusive - minValueInclusive;
		return (int)(minValueInclusive + (long)NextBounded((ulong)range));
	}

	/// <summary>
	/// Returns a random integer in the inclusive range [<paramref name="minValueInclusive"/>, <paramref name="maxValueInclusive"/>].
	/// </summary>
	/// <param name="minValueInclusive">The inclusive lower bound.</param>
	/// <param name="maxValueInclusive">The inclusive upper bound.</param>
	/// <returns>A pseudo random integer in the requested range; the lower bound when the range is empty.</returns>
	public int NextInt(int minValueInclusive, int maxValueInclusive)
	{
		if (maxValueInclusive < minValueInclusive) return minValueInclusive;
		long range = (long)maxValueInclusive - minValueInclusive + 1L;
		return (int)(minValueInclusive + (long)NextBounded((ulong)range));
	}

	/// <summary>
	/// Returns a random double in the range [0, 1).
	/// </summary>
	/// <returns>A pseudo random double with 53 bits of precision.</returns>
	public double NextDouble() => (NextUInt64() >> 11) * DOUBLE_SCALE;

	/// <summary>
	/// Returns a random single-precision value in the range [0, 1).
	/// </summary>
	/// <returns>A pseudo random float with 24 bits of precision.</returns>
	public float NextSingle() => (NextUInt64() >> 40) * SINGLE_SCALE;

	/// <summary>
	/// Returns a non-negative random 64-bit integer below <see cref="long.MaxValue"/>.
	/// </summary>
	/// <returns>A pseudo random 64-bit integer in the range [0, <see cref="long.MaxValue"/>).</returns>
	public long NextInt64() => NextInt64(0L, long.MaxValue);

	/// <summary>
	/// Returns a non-negative random 64-bit integer below the exclusive upper bound.
	/// </summary>
	/// <param name="maxValueExclusive">The exclusive upper bound. Values of zero or less return zero.</param>
	/// <returns>A pseudo random 64-bit integer in the range [0, <paramref name="maxValueExclusive"/>).</returns>
	public long NextInt64(long maxValueExclusive) => maxValueExclusive <= 0L ? 0L : NextInt64(0L, maxValueExclusive);

	/// <summary>
	/// Returns a random 64-bit integer in the half-open range [<paramref name="minValueInclusive"/>, <paramref name="maxValueExclusive"/>).
	/// </summary>
	/// <param name="minValueInclusive">The inclusive lower bound.</param>
	/// <param name="maxValueExclusive">The exclusive upper bound.</param>
	/// <returns>A pseudo random 64-bit integer in the requested range; the lower bound when the range is empty.</returns>
	public long NextInt64(long minValueInclusive, long maxValueExclusive)
	{
		if (maxValueExclusive <= minValueInclusive) return minValueInclusive;
		ulong range = unchecked((ulong)(maxValueExclusive - minValueInclusive));
		if (range == 0UL) return unchecked((long)NextUInt64());
		return unchecked((long)((ulong)minValueInclusive + NextBounded(range)));
	}

	/// <summary>
	/// Fills the destination buffer with random bytes and advances the stream once per filled 64-bit chunk.
	/// </summary>
	/// <param name="buffer">The buffer to fill.</param>
	public void NextBytes(Span<byte> buffer)
	{
		int index = 0;
		while (buffer.Length - index >= sizeof(ulong))
		{
			ulong value = NextUInt64();
			for (int byteIndex = 0; byteIndex < sizeof(ulong); byteIndex++)
			{
				buffer[index + byteIndex] = (byte)(value >> (byteIndex * 8));
			}
			index += sizeof(ulong);
		}

		if (index < buffer.Length)
		{
			ulong value = NextUInt64();
			for (int byteIndex = 0; index < buffer.Length; index++, byteIndex++)
			{
				buffer[index] = (byte)(value >> (byteIndex * 8));
			}
		}
	}

	/// <summary>
	/// Fills the destination buffer with random bytes.
	/// </summary>
	/// <param name="buffer">The buffer to fill.</param>
	public void NextBytes(byte[] buffer) => NextBytes(buffer.AsSpan());

	/// <summary>
	/// Shuffles the given span in place using a Fisher-Yates shuffle driven by this stream.
	/// </summary>
	/// <typeparam name="T">The element type.</typeparam>
	/// <param name="values">The values to shuffle in place.</param>
	public void Shuffle<T>(Span<T> values)
	{
		for (int index = values.Length - 1; index > 0; index--)
		{
			int swapIndex = Next(0, index + 1);
			(values[index], values[swapIndex]) = (values[swapIndex], values[index]);
		}
	}

	/// <summary>
	/// Shuffles the given array in place using a Fisher-Yates shuffle driven by this stream.
	/// </summary>
	/// <typeparam name="T">The element type.</typeparam>
	/// <param name="values">The values to shuffle in place.</param>
	public void Shuffle<T>(T[] values) => Shuffle(values.AsSpan());

	/// <summary>
	/// Copies a random selection of values into a new array.
	/// </summary>
	/// <typeparam name="T">The element type.</typeparam>
	/// <param name="choices">The values to select from.</param>
	/// <param name="length">The number of values to select. Selection is with replacement, matching <see cref="System.Random.GetItems{T}(ReadOnlySpan{T}, int)"/>.</param>
	/// <returns>A new array containing the selected values.</returns>
	public T[] GetItems<T>(ReadOnlySpan<T> choices, int length)
	{
		T[] result = new T[length];
		GetItems(choices, result);
		return result;
	}

	/// <summary>
	/// Copies a random selection of values into a new array.
	/// </summary>
	/// <typeparam name="T">The element type.</typeparam>
	/// <param name="choices">The values to select from.</param>
	/// <param name="length">The number of values to select. Selection is with replacement.</param>
	/// <returns>A new array containing the selected values.</returns>
	public T[] GetItems<T>(T[] choices, int length) => GetItems((ReadOnlySpan<T>)choices, length);

	/// <summary>
	/// Fills the destination with a random selection of values.
	/// </summary>
	/// <typeparam name="T">The element type.</typeparam>
	/// <param name="choices">The values to select from. Nothing is written when this span is empty.</param>
	/// <param name="destination">The destination to fill. Selection is with replacement.</param>
	public void GetItems<T>(ReadOnlySpan<T> choices, Span<T> destination)
	{
		if (choices.IsEmpty) return;
		for (int index = 0; index < destination.Length; index++)
		{
			destination[index] = choices[Next(0, choices.Length)];
		}
	}

	/// <summary>
	/// Verifies that two streams built from the same seed produce the same sequence of values.
	/// </summary>
	/// <param name="seed">The seed to verify.</param>
	/// <param name="drawCount">The number of values to compare. Values of zero or less are treated as one.</param>
	/// <returns>A report describing whether the sequence is deterministic and where the first divergence occurred.</returns>
	public static RandomDeterminismReport Verify(int seed, int drawCount = 256)
	{
		int comparisons = drawCount <= 0 ? 1 : drawCount;
		var first = new DeterministicRandom(seed);
		var second = new DeterministicRandom(seed);

		for (int index = 0; index < comparisons; index++)
		{
			ulong expected = first.NextUInt64();
			ulong actual = second.NextUInt64();
			if (expected != actual)
			{
				return new RandomDeterminismReport(seed, comparisons, index, expected, actual);
			}
		}

		return new RandomDeterminismReport(seed, comparisons, -1, 0UL, 0UL);
	}

	private ulong NextBounded(ulong exclusiveBound)
	{
		if (exclusiveBound == 0UL) return 0UL;
		return Math.BigMul(NextUInt64(), exclusiveBound, out _);
	}
}

/// <summary>
/// Describes the outcome of a deterministic random sequence verification.
/// </summary>
public readonly struct RandomDeterminismReport
{
	/// <summary>
	/// Initializes a new report for the given verification run.
	/// </summary>
	/// <param name="seed">The verified seed.</param>
	/// <param name="drawCount">The number of compared values.</param>
	/// <param name="divergenceIndex">The zero-based index of the first divergence, or -1 when the sequences matched.</param>
	/// <param name="firstSequenceValue">The value produced by the first sequence at the divergence point.</param>
	/// <param name="secondSequenceValue">The value produced by the second sequence at the divergence point.</param>
	public RandomDeterminismReport(int seed, int drawCount, int divergenceIndex, ulong firstSequenceValue, ulong secondSequenceValue)
	{
		Seed = seed;
		DrawCount = drawCount;
		DivergenceIndex = divergenceIndex;
		FirstSequenceValue = firstSequenceValue;
		SecondSequenceValue = secondSequenceValue;
	}

	/// <summary>
	/// Gets the verified seed.
	/// </summary>
	/// <value>The seed both sequences were started with.</value>
	public int Seed { get; }

	/// <summary>
	/// Gets the number of compared values.
	/// </summary>
	/// <value>The requested comparison count.</value>
	public int DrawCount { get; }

	/// <summary>
	/// Gets the zero-based index of the first divergence.
	/// </summary>
	/// <value>The divergence index, or -1 when both sequences matched for the whole comparison.</value>
	public int DivergenceIndex { get; }

	/// <summary>
	/// Gets the value produced by the first sequence at the divergence point.
	/// </summary>
	/// <value>The first sequence value, or zero when no divergence occurred.</value>
	public ulong FirstSequenceValue { get; }

	/// <summary>
	/// Gets the value produced by the second sequence at the divergence point.
	/// </summary>
	/// <value>The second sequence value, or zero when no divergence occurred.</value>
	public ulong SecondSequenceValue { get; }

	/// <summary>
	/// Gets a value indicating whether both sequences were identical for the whole comparison.
	/// </summary>
	/// <value><see langword="true"/> when no divergence occurred; otherwise <see langword="false"/>.</value>
	public bool IsDeterministic => DivergenceIndex < 0;

	/// <summary>
	/// Produces a human readable description of the verification outcome.
	/// </summary>
	/// <returns>A message stating whether the sequence is deterministic and, on divergence, where it diverged.</returns>
	public string Describe()
	{
		return IsDeterministic
			? $"Deterministic random sequence verified for seed {Seed} across {DrawCount} draws."
			: $"Deterministic random sequence diverged for seed {Seed} at draw {DivergenceIndex}: {FirstSequenceValue} != {SecondSequenceValue}.";
	}
}
