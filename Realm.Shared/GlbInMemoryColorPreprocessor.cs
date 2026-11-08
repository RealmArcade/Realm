using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

// CRITICAL / IN-MEMORY ONLY:
// This preprocessor performs a 1x baked CPU in-memory analytical chroma despill pass for loaded GLB/RMESH models at runtime.
// It is intended EXCLUSIVELY for runtime model loading in memory (e.g. ModelCache, thumbnail rendering) and must NEVER
// be serialized to disk, written to .glb, or packaged into .rmesh files during asset conversion or export workflows.
// On-disk asset files must strictly preserve the pristine, raw albedo textures so that spatial shaders can retain
// original albedos when 'ignore_player_color' is active.
public static partial class GlbInMemoryColorPreprocessor
	{
	private static readonly ConcurrentDictionary<ulong, byte[]> DespilledGlbCache = new();

	private static readonly float[] SrgbToLinearTable = PrecomputeSrgbToLinear();

	private static readonly byte[] LinearToSrgbLut = PrecomputeLinearToSrgbLut();

	private static readonly float[] CbrtLut = PrecomputeCbrtLut();

	private static float[] PrecomputeSrgbToLinear()
	{
		float[] table = new float[256];
		for (int i = 0; i < 256; i++)
		{
			float c = i / 255.0f;
			table[i] = c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
		}

		return table;
	}

	private static byte[] PrecomputeLinearToSrgbLut()
	{
		byte[] table = new byte[65536];
		for (int i = 0; i < 65536; i++)
		{
			float linear = i / 65535.0f;
			float srgb = linear <= 0.0031308f
				? 12.92f * linear
				: 1.055f * MathF.Pow(linear, 1.0f / 2.4f) - 0.055f;
			table[i] = (byte)Math.Clamp((int)(srgb * 255.0f + 0.5f), 0, 255);
		}

		return table;
	}

	private static float[] PrecomputeCbrtLut()
	{
		float[] table = new float[65536];
		for (int i = 0; i < 65536; i++)
		{
			table[i] = MathF.Cbrt(i / 65535.0f);
		}

		return table;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float FastCbrt(float val)
	{
		int idx = (int)(val * 65535.0f);
		if ((uint)idx >= 65536)
		{
			return val <= 0.0f ? 0.0f : MathF.Cbrt(val);
		}

		return CbrtLut[idx];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static byte LinearToSrgbByte(float linear)
	{
		int idx = (int)(linear * 65535.0f);
		if ((uint)idx >= 65536)
		{
			return idx < 0 ? (byte)0 : (byte)255;
		}

		return LinearToSrgbLut[idx];
	}

	private static void StoreInCache(ulong cacheKey, byte[] value)
	{
		DespilledGlbCache[cacheKey] = value;
	}

	private static ulong ComputeFnv1a64(byte[] bytes, string chromaKey)
	{
		ulong hash = 14695981039346656037UL;
		hash ^= (ulong)bytes.Length;
		hash *= 1099511628211UL;

		int sampleSize = Math.Min(bytes.Length, 256);
		for (int i = 0; i < sampleSize; i++)
		{
			hash ^= bytes[i];
			hash *= 1099511628211UL;
		}

		if (bytes.Length > 512)
		{
			int midStart = (bytes.Length / 2) - 128;
			int midEnd = midStart + 256;
			for (int i = midStart; i < midEnd; i++)
			{
				hash ^= bytes[i];
				hash *= 1099511628211UL;
			}

			int tailStart = bytes.Length - 256;
			for (int i = tailStart; i < bytes.Length; i++)
			{
				hash ^= bytes[i];
				hash *= 1099511628211UL;
			}
		}

		for (int i = 0; i < chromaKey.Length; i++)
		{
			hash ^= (byte)chromaKey[i];
			hash *= 1099511628211UL;
		}

		return hash;
	}
}
