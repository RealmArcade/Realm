using System;
using SkiaSharp;

namespace Realm.Shared.Textures;

public enum TextureDataType
{
	Albedo,
	NormalMap,
	Data
}

public static class TextureResizer
{
	public static bool IsPowerOfTwo(int n)
	{
		return n > 0 && (n & (n - 1)) == 0;
	}

	public static int CalculateTargetPowerOfTwo(int size, int maxResolution = 4096)
	{
		if (size <= 0) return 1;
		if (size >= maxResolution) return maxResolution;

		if (IsPowerOfTwo(size))
		{
			return size;
		}

		int p = 1;
		while (p < size && p < maxResolution)
		{
			p <<= 1;
		}

		int lower = p >> 1;
		if (lower > 0 && (size - lower) < (p - size))
		{
			return lower;
		}

		return Math.Min(maxResolution, p);
	}

	public static SKBitmap ResizeImage(
		SKBitmap image,
		int targetWidth,
		int targetHeight,
		TextureDataType dataType = TextureDataType.Albedo)
	{
		if (image.Width == targetWidth && image.Height == targetHeight)
		{
			if (dataType == TextureDataType.NormalMap)
			{
				RenormalizeNormalMap(image);
			}
			return image;
		}

		SKSamplingOptions samplingOptions = dataType switch
		{
			TextureDataType.NormalMap => new SKSamplingOptions(SKCubicResampler.Mitchell),
			TextureDataType.Data => new SKSamplingOptions(SKFilterMode.Linear),
			_ => new SKSamplingOptions(SKCubicResampler.Mitchell)
		};

		SKBitmap resized = image.Resize(new SKImageInfo(targetWidth, targetHeight), samplingOptions);
		if (dataType == TextureDataType.NormalMap && resized != null)
		{
			RenormalizeNormalMap(resized);
		}

		return resized ?? image;
	}

	public static void RenormalizeNormalMap(SKBitmap image)
	{
		int width = image.Width;
		int height = image.Height;

		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				SKColor pixel = image.GetPixel(x, y);
				float nx = (pixel.Red / 255.0f) * 2.0f - 1.0f;
				float ny = (pixel.Green / 255.0f) * 2.0f - 1.0f;
				float nz = (pixel.Blue / 255.0f) * 2.0f - 1.0f;

				float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
				if (len > 1e-5f)
				{
					float invLen = 1.0f / len;
					nx *= invLen;
					ny *= invLen;
					nz *= invLen;
				}
				else
				{
					nx = 0.0f;
					ny = 0.0f;
					nz = 1.0f;
				}

				byte r = (byte)Math.Clamp((int)Math.Round((nx * 0.5f + 0.5f) * 255.0f), 0, 255);
				byte g = (byte)Math.Clamp((int)Math.Round((ny * 0.5f + 0.5f) * 255.0f), 0, 255);
				byte b = (byte)Math.Clamp((int)Math.Round((nz * 0.5f + 0.5f) * 255.0f), 0, 255);

				image.SetPixel(x, y, new SKColor(r, g, b, pixel.Alpha));
			}
		}
	}
}
