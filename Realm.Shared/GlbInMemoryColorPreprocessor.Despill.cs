using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>the mask test and the despill entry that runs over decoded images.</summary>
public static partial class GlbInMemoryColorPreprocessor
	{
	private static bool TryGetGlbArrays(JsonObject root, out JsonArray textures, out JsonArray materials, out JsonArray images, out JsonArray bufferViews)
	{
		textures = (root["textures"] as JsonArray)!;
		materials = (root["materials"] as JsonArray)!;
		images = (root["images"] as JsonArray)!;
		bufferViews = (root["bufferViews"] as JsonArray)!;

		return textures != null && materials != null && images != null && bufferViews != null;
	}

	private static byte[] ProcessDecodedImages(SKBitmap albedoImg, SKBitmap ormImg, string chromaKey, JsonObject root, byte[] binChunk, int albedoImageIndex, uint glbVersion, ulong cacheKey)
	{
		ApplyAnalyticalChromaDespill(albedoImg, ormImg, chromaKey);
		byte[] newAlbedoBytes = TextureConverter.EncodeWebp(albedoImg, lossless: false, quality: 90, method: 1, sharpYuv: false);
		byte[] resultGlb = RebuildGlbWithUpdatedAlbedoTexture(root, binChunk, albedoImageIndex, newAlbedoBytes, glbVersion);
		StoreInCache(cacheKey, resultGlb);
		return resultGlb;
	}

	private static bool HasMaskInOrm(SKBitmap ormImg)
	{
		SKBitmap workBitmap = ormImg;
		bool disposeWork = false;
		if (ormImg.ColorType != SKColorType.Rgba8888 && ormImg.ColorType != SKColorType.Bgra8888)
		{
			workBitmap = ormImg.Copy(SKColorType.Rgba8888);
			disposeWork = workBitmap != null && workBitmap != ormImg;
			if (workBitmap == null) workBitmap = ormImg;
		}

		try
		{
			IntPtr pixelsPtr = workBitmap.GetPixels();
			if (pixelsPtr != IntPtr.Zero)
			{
				return HasMaskUnsafe(workBitmap, pixelsPtr);
			}

			return HasMaskSafe(workBitmap);
		}
		finally
		{
			if (disposeWork) workBitmap.Dispose();
		}
	}

	private static unsafe bool HasMaskUnsafe(SKBitmap workBitmap, IntPtr pixelsPtr)
	{
		byte* basePtr = (byte*)pixelsPtr;
		int rowBytes = workBitmap.RowBytes;
		int rOffset = workBitmap.ColorType == SKColorType.Bgra8888 ? 2 : 0;
		int width = workBitmap.Width;
		int height = workBitmap.Height;

		for (int y = 0; y < height; y++)
		{
			byte* row = basePtr + (y * rowBytes);
			if (HasMaskInRowUnsafe(row, width, rOffset))
			{
				return true;
			}
		}

		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static unsafe bool HasMaskInRowUnsafe(byte* row, int width, int rOffset)
	{
		for (int x = 0; x < width; x++)
		{
			if (row[x * 4 + rOffset] > 0)
			{
				return true;
			}
		}

		return false;
	}

	private static bool HasMaskSafe(SKBitmap workBitmap)
	{
		int h = workBitmap.Height;
		int w = workBitmap.Width;
		for (int y = 0; y < h; y++)
		{
			if (HasMaskInRowSafe(workBitmap, y, w))
			{
				return true;
			}
		}

		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool HasMaskInRowSafe(SKBitmap workBitmap, int y, int w)
	{
		for (int x = 0; x < w; x++)
		{
			if (workBitmap.GetPixel(x, y).Red > 0)
			{
				return true;
			}
		}

		return false;
	}

	public static void ApplyAnalyticalChromaDespill(
		SKBitmap albedoImg,
		SKBitmap ormImg,
		string chromaKeyHex)
	{
		if (!TryCalculateKeyUnitVector(chromaKeyHex, out Vector2 keyUnitVector))
			return;

		bool sameDimensions = (albedoImg.Width == ormImg.Width && albedoImg.Height == ormImg.Height);

		var (workAlbedo, disposeWorkAlbedo) = PrepareBitmap(albedoImg);
		var (workOrm, disposeWorkOrm) = PrepareBitmap(ormImg);

		try
		{
			ExecuteDespill(albedoImg, workAlbedo, workOrm, keyUnitVector, sameDimensions, disposeWorkAlbedo);
		}
		finally
		{
			if (disposeWorkAlbedo) workAlbedo.Dispose();
			if (disposeWorkOrm) workOrm.Dispose();
		}
	}
}
