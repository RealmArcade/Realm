using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>the despill over raw pixel rows.</summary>
public static partial class GlbInMemoryColorPreprocessor
	{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static unsafe void ProcessDespillRowUnsafe(
		byte* albRow, byte* ormRow,
		int width, int ormWidth,
		int albROff, int albGOff, int albBOff, int ormROff,
		bool sameDimensions, Vector2 keyUnitVector)
	{
		for (int x = 0; x < width; x++)
		{
			int ormX = sameDimensions ? x : Math.Clamp((int)(((x + 0.5f) / width) * ormWidth), 0, ormWidth - 1);
			float mask = ormRow[ormX * 4 + ormROff] / 255.0f;
			if (mask >= 0.999f) continue;

			int albIdx = x * 4;
			byte r = albRow[albIdx + albROff];
			byte g = albRow[albIdx + albGOff];
			byte b = albRow[albIdx + albBOff];

			if (TryDespillPixel(r, g, b, mask, keyUnitVector, out byte newR, out byte newG, out byte newB))
			{
				albRow[albIdx + albROff] = newR;
				albRow[albIdx + albGOff] = newG;
				albRow[albIdx + albBOff] = newB;
			}
		}
	}
}
