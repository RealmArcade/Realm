using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>the managed despill, its mask row and the per pixel decision.</summary>
public static partial class GlbInMemoryColorPreprocessor
	{
	private static void ApplyAnalyticalChromaDespillSafe(SKBitmap workAlbedo, SKBitmap workOrm, Vector2 keyUnitVector, bool sameDimensions)
	{
		int width = workAlbedo.Width;
		int height = workAlbedo.Height;
		int ormWidth = workOrm.Width;
		int ormHeight = workOrm.Height;

		float[] maskValues = new float[width * height];
		for (int y = 0; y < height; y++)
		{
			PopulateMaskRowSafe(workOrm, maskValues, y, width, height, ormWidth, ormHeight, sameDimensions);
		}

		for (int y = 0; y < height; y++)
		{
			ProcessDespillRowSafe(workAlbedo, maskValues, y, width, keyUnitVector);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void PopulateMaskRowSafe(
		SKBitmap workOrm, float[] maskValues,
		int y, int width, int height, int ormWidth, int ormHeight,
		bool sameDimensions)
	{
		int ormY = sameDimensions ? y : Math.Clamp((int)(((y + 0.5f) / height) * ormHeight), 0, ormHeight - 1);
		int rowOffset = y * width;

		for (int x = 0; x < width; x++)
		{
			int ormX = sameDimensions ? x : Math.Clamp((int)(((x + 0.5f) / width) * ormWidth), 0, ormWidth - 1);
			maskValues[rowOffset + x] = workOrm.GetPixel(ormX, ormY).Red / 255.0f;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void ProcessDespillRowSafe(
		SKBitmap workAlbedo, float[] maskValues,
		int y, int width, Vector2 keyUnitVector)
	{
		int rowOffset = y * width;

		for (int x = 0; x < width; x++)
		{
			float mask = maskValues[rowOffset + x];
			if (mask >= 0.999f) continue;

			var pixel = workAlbedo.GetPixel(x, y);
			if (TryDespillPixel(pixel.Red, pixel.Green, pixel.Blue, mask, keyUnitVector, out byte newR, out byte newG, out byte newB))
			{
				workAlbedo.SetPixel(x, y, new SKColor(newR, newG, newB, pixel.Alpha));
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool TryDespillPixel(
		byte rByte, byte gByte, byte bByte,
		float maskFactor,
		Vector2 keyUnitVector,
		out byte outR, out byte outG, out byte outB)
	{
		outR = rByte;
		outG = gByte;
		outB = bByte;

		if (maskFactor >= 0.999f || (rByte == gByte && gByte == bByte))
		{
			return false;
		}

		float nonMaskWeight = 1.0f - maskFactor;

		Vector3 linearRgb = new(
			SrgbToLinearTable[rByte],
			SrgbToLinearTable[gByte],
			SrgbToLinearTable[bByte]);

		Vector3 oklab = ConvertLinearRgbToOklab(linearRgb);
		Vector2 chrominance = new(oklab.Y, oklab.Z);

		float parallelComponent = Vector2.Dot(chrominance, keyUnitVector);
		if (parallelComponent <= 0.0f)
		{
			return false;
		}

		Vector2 perpendicular = chrominance - parallelComponent * keyUnitVector;
		float orthogonalChroma = perpendicular.Length();

		float maxAllowedKeyChroma = 0.35f * orthogonalChroma;
		float excessChrominance = MathF.Max(0.0f, parallelComponent - maxAllowedKeyChroma) * nonMaskWeight;

		if (excessChrominance <= 1e-6f)
		{
			return false;
		}

		Vector2 despilledChrominance = chrominance - excessChrominance * keyUnitVector;
		Vector3 despilledOklab = new(oklab.X, despilledChrominance.X, despilledChrominance.Y);

		Vector3 despilledLinearRgb = ConvertOklabToLinearRgb(despilledOklab);
		outR = LinearToSrgbByte(despilledLinearRgb.X);
		outG = LinearToSrgbByte(despilledLinearRgb.Y);
		outB = LinearToSrgbByte(despilledLinearRgb.Z);
		return true;
	}
}
