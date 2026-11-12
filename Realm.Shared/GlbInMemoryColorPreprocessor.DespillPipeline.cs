using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>the key vector, the bitmap preparation and the despill entry.</summary>
public static partial class GlbInMemoryColorPreprocessor
	{
	private static bool TryCalculateKeyUnitVector(string chromaKeyHex, out Vector2 keyUnitVector)
	{
		keyUnitVector = default;
		(float targetR, float targetG, float targetB) = GlbChromaKeyMatcher.HexToRgb(chromaKeyHex);
		Vector3 targetLinear = new(
			SrgbToLinearTable[(byte)Math.Clamp((int)(targetR * 255.0f + 0.5f), 0, 255)],
			SrgbToLinearTable[(byte)Math.Clamp((int)(targetG * 255.0f + 0.5f), 0, 255)],
			SrgbToLinearTable[(byte)Math.Clamp((int)(targetB * 255.0f + 0.5f), 0, 255)]);

		Vector3 targetOklab = ConvertLinearRgbToOklab(targetLinear);
		Vector2 keyVector = new(targetOklab.Y, targetOklab.Z);
		float keyChroma = keyVector.Length();
		if (keyChroma < 1e-5f) return false;

		keyUnitVector = keyVector / keyChroma;
		return true;
	}

	private static (SKBitmap workBitmap, bool disposeWork) PrepareBitmap(SKBitmap original)
	{
		if (original.ColorType == SKColorType.Rgba8888 || original.ColorType == SKColorType.Bgra8888)
		{
			return (original, false);
		}

		SKBitmap workBitmap = original.Copy(SKColorType.Rgba8888) ?? original;
		bool disposeWork = workBitmap != original;
		return (workBitmap, disposeWork);
	}

	private static void ExecuteDespill(SKBitmap originalAlbedo, SKBitmap workAlbedo, SKBitmap workOrm, Vector2 keyUnitVector, bool sameDimensions, bool copyBack)
	{
		IntPtr albedoPtr = workAlbedo.GetPixels();
		IntPtr ormPtr = workOrm.GetPixels();

		if (albedoPtr != IntPtr.Zero && ormPtr != IntPtr.Zero)
		{
			ApplyAnalyticalChromaDespillUnsafe(workAlbedo, workOrm, keyUnitVector, sameDimensions);
		}
		else
		{
			ApplyAnalyticalChromaDespillSafe(workAlbedo, workOrm, keyUnitVector, sameDimensions);
		}

		if (copyBack)
		{
			using var skImg = SKImage.FromBitmap(workAlbedo);
			using var canvas = new SKCanvas(originalAlbedo);
			canvas.Clear();
			canvas.DrawImage(skImg, 0, 0);
		}
	}

	private static unsafe void ApplyAnalyticalChromaDespillUnsafe(SKBitmap workAlbedo, SKBitmap workOrm, Vector2 keyUnitVector, bool sameDimensions)
	{
		byte* albBase = (byte*)workAlbedo.GetPixels();
		byte* ormBase = (byte*)workOrm.GetPixels();
		int albRowBytes = workAlbedo.RowBytes;
		int ormRowBytes = workOrm.RowBytes;

		int albROff = workAlbedo.ColorType == SKColorType.Bgra8888 ? 2 : 0;
		int albGOff = 1;
		int albBOff = workAlbedo.ColorType == SKColorType.Bgra8888 ? 0 : 2;

		int ormROff = workOrm.ColorType == SKColorType.Bgra8888 ? 2 : 0;

		int width = workAlbedo.Width;
		int height = workAlbedo.Height;
		int ormWidth = workOrm.Width;
		int ormHeight = workOrm.Height;

		Parallel.For(0, height, y =>
		{
			int ormY = sameDimensions ? y : Math.Clamp((int)(((y + 0.5f) / height) * ormHeight), 0, ormHeight - 1);
			byte* albRow = albBase + y * albRowBytes;
			byte* ormRow = ormBase + ormY * ormRowBytes;

			ProcessDespillRowUnsafe(albRow, ormRow, width, ormWidth, albROff, albGOff, albBOff, ormROff, sameDimensions, keyUnitVector);
		});
	}
}
