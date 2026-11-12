using System.Globalization;
using System.Text.Json.Nodes;
using Imazen.WebP;
using Realm.Shared.Metadata;
using SkiaSharp;

namespace Realm.Shared.Textures;

/// <summary>copying pixels out of a skia bitmap.</summary>
public static partial class TextureConverter
	{
	private static unsafe void CopyPixelsRgba(byte* srcBase, byte* dstPtr, int width, int height, int rowBytes)
	{
		if (rowBytes == width * 4)
		{
			Buffer.MemoryCopy(srcBase, dstPtr, width * height * 4, width * height * 4);
		}
		else
		{
			for (int y = 0; y < height; y++)
			{
				Buffer.MemoryCopy(srcBase + y * rowBytes, dstPtr + y * width * 4, width * 4, width * 4);
			}
		}
	}

	private static unsafe void CopyPixelsBgra(byte* srcBase, byte* dstPtr, int width, int height, int rowBytes)
	{
		for (int y = 0; y < height; y++)
		{
			byte* srcRow = srcBase + y * rowBytes;
			byte* dstRow = dstPtr + y * width * 4;
			for (int x = 0; x < width; x++)
			{
				int idx = x * 4;
				dstRow[idx] = srcRow[idx + 2];
				dstRow[idx + 1] = srcRow[idx + 1];
				dstRow[idx + 2] = srcRow[idx];
				dstRow[idx + 3] = srcRow[idx + 3];
			}
		}
	}

	private static void CopyPixelsFallback(SKBitmap bitmap, byte[] dstBytes)
	{
		int width = bitmap.Width;
		int height = bitmap.Height;
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				SKColor color = bitmap.GetPixel(x, y);
				int idx = (y * width + x) * 4;
				dstBytes[idx] = color.Red;
				dstBytes[idx + 1] = color.Green;
				dstBytes[idx + 2] = color.Blue;
				dstBytes[idx + 3] = color.Alpha;
			}
		}
	}

	private static unsafe void ExtractPixelBytes(SKBitmap workBitmap, byte[] pixelBytes)
	{
		IntPtr pixelsPtr = workBitmap.GetPixels();
		if (pixelsPtr == IntPtr.Zero)
		{
			CopyPixelsFallback(workBitmap, pixelBytes);
			return;
		}

		byte* srcBase = (byte*)pixelsPtr;
		int rowBytes = workBitmap.RowBytes;
		bool isRgba = workBitmap.ColorType == SKColorType.Rgba8888;
		bool isBgra = workBitmap.ColorType == SKColorType.Bgra8888;

		fixed (byte* dstPtr = pixelBytes)
		{
			if (isRgba)
			{
				CopyPixelsRgba(srcBase, dstPtr, workBitmap.Width, workBitmap.Height, rowBytes);
			}
			else if (isBgra)
			{
				CopyPixelsBgra(srcBase, dstPtr, workBitmap.Width, workBitmap.Height, rowBytes);
			}
			else
			{
				CopyPixelsFallback(workBitmap, pixelBytes);
			}
		}
	}
}
