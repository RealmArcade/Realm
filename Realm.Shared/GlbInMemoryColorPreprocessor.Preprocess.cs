using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>the in-memory entry point and the chroma key it normalises.</summary>
public static partial class GlbInMemoryColorPreprocessor
	{
	public static byte[] PreprocessGlbInMemory(byte[] glbBytes, string? chromaKeyHex = null)
	{
		if (glbBytes == null || glbBytes.Length == 0)
		{
			return glbBytes ?? Array.Empty<byte>();
		}

		string effectiveChromaKey = NormalizeChromaKey(chromaKeyHex);

		ulong cacheKey = ComputeFnv1a64(glbBytes, effectiveChromaKey);
		if (DespilledGlbCache.TryGetValue(cacheKey, out var cachedBytes))
		{
			return cachedBytes;
		}

		try
		{
			return TryProcessGlbCore(glbBytes, effectiveChromaKey, cacheKey);
		}
		catch
		{
			StoreInCache(cacheKey, glbBytes);
			return glbBytes;
		}
	}

	private static string NormalizeChromaKey(string? chromaKeyHex)
	{
		string key = chromaKeyHex ?? string.Empty;
		if (string.IsNullOrWhiteSpace(key) || string.Equals(key, "auto", StringComparison.OrdinalIgnoreCase))
			return "#FF00FF";

		key = key.Trim();
		return key.StartsWith('#') ? key : "#" + key;
	}
}
