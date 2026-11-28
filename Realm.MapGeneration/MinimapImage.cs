using System;
using System.IO;
using SkiaSharp;

namespace Realm.MapGeneration;

public class MinimapImage
{
	public int Width { get; }

	public int Height { get; }

	public float[] Red { get; }

	public float[] Green { get; }

	public float[] Blue { get; }

	public MinimapImage(int width, int height)
	{
		Width = width;
		Height = height;
		Red = new float[width * height];
		Green = new float[width * height];
		Blue = new float[width * height];
	}

	public static MinimapImage FromFile(string path)
	{
		using SKBitmap? bitmap = SKBitmap.Decode(path);
		if (bitmap == null)
		{
			throw new InvalidDataException($"The image at '{path}' could not be decoded.");
		}

		return FromBitmap(bitmap);
	}

	public static MinimapImage FromBytes(ReadOnlySpan<byte> encoded)
	{
		using SKBitmap? bitmap = SKBitmap.Decode(encoded.ToArray());
		if (bitmap == null)
		{
			throw new InvalidDataException("The encoded image could not be decoded.");
		}

		return FromBitmap(bitmap);
	}

	public static MinimapImage FromBitmap(SKBitmap bitmap)
	{
		using SKBitmap converted = bitmap.ColorType == SKColorType.Rgba8888
			? bitmap.Copy()
			: bitmap.Copy(SKColorType.Rgba8888);

		var image = new MinimapImage(converted.Width, converted.Height);
		ReadOnlySpan<byte> pixels = converted.GetPixelSpan();
		int stride = converted.RowBytes;
		for (int y = 0; y < converted.Height; y++)
		{
			int rowStart = y * stride;
			int target = y * converted.Width;
			for (int x = 0; x < converted.Width; x++)
			{
				int source = rowStart + x * 4;
				image.Red[target + x] = pixels[source] / 255f;
				image.Green[target + x] = pixels[source + 1] / 255f;
				image.Blue[target + x] = pixels[source + 2] / 255f;
			}
		}

		return image;
	}

	public void SampleBilinear(float normalisedX, float normalisedY, out float red, out float green, out float blue)
	{
		float pixelX = Math.Clamp(normalisedX * (Width - 1), 0f, Math.Max(0, Width - 1));
		float pixelY = Math.Clamp(normalisedY * (Height - 1), 0f, Math.Max(0, Height - 1));
		int x0 = (int)pixelX;
		int y0 = (int)pixelY;
		int x1 = Math.Min(x0 + 1, Width - 1);
		int y1 = Math.Min(y0 + 1, Height - 1);
		float tx = pixelX - x0;
		float ty = pixelY - y0;

		int topLeft = y0 * Width + x0;
		int topRight = y0 * Width + x1;
		int bottomLeft = y1 * Width + x0;
		int bottomRight = y1 * Width + x1;

		red = Blend(Red, topLeft, topRight, bottomLeft, bottomRight, tx, ty);
		green = Blend(Green, topLeft, topRight, bottomLeft, bottomRight, tx, ty);
		blue = Blend(Blue, topLeft, topRight, bottomLeft, bottomRight, tx, ty);
	}

	public void SampleBox(float minimumX, float minimumY, float maximumX, float maximumY, out float red, out float green, out float blue)
	{
		float clampedMinX = Math.Clamp(minimumX, 0f, 1f);
		float clampedMinY = Math.Clamp(minimumY, 0f, 1f);
		float clampedMaxX = Math.Clamp(maximumX, 0f, 1f);
		float clampedMaxY = Math.Clamp(maximumY, 0f, 1f);

		int startX = Math.Clamp((int)(clampedMinX * Width), 0, Width - 1);
		int endX = Math.Clamp((int)MathF.Ceiling(clampedMaxX * Width), startX + 1, Width);
		int startY = Math.Clamp((int)(clampedMinY * Height), 0, Height - 1);
		int endY = Math.Clamp((int)MathF.Ceiling(clampedMaxY * Height), startY + 1, Height);

		double sumRed = 0;
		double sumGreen = 0;
		double sumBlue = 0;
		int count = 0;
		for (int y = startY; y < endY; y++)
		{
			int rowStart = y * Width;
			for (int x = startX; x < endX; x++)
			{
				sumRed += Red[rowStart + x];
				sumGreen += Green[rowStart + x];
				sumBlue += Blue[rowStart + x];
				count++;
			}
		}

		if (count == 0)
		{
			SampleBilinear((minimumX + maximumX) * 0.5f, (minimumY + maximumY) * 0.5f, out red, out green, out blue);
			return;
		}

		red = (float)(sumRed / count);
		green = (float)(sumGreen / count);
		blue = (float)(sumBlue / count);
	}

	private static float Blend(float[] channel, int topLeft, int topRight, int bottomLeft, int bottomRight, float tx, float ty)
	{
		float top = channel[topLeft] + (channel[topRight] - channel[topLeft]) * tx;
		float bottom = channel[bottomLeft] + (channel[bottomRight] - channel[bottomLeft]) * tx;
		return top + (bottom - top) * ty;
	}

	public float[] BuildLuminance()
	{
		var luminance = new float[Width * Height];
		for (int index = 0; index < luminance.Length; index++)
		{
			luminance[index] = Red[index] * 0.2126f + Green[index] * 0.7152f + Blue[index] * 0.0722f;
		}

		return luminance;
	}

	public float[] BuildLocalVariance(int radius)
	{
		float[] luminance = BuildLuminance();
		var variance = new float[Width * Height];
		for (int y = 0; y < Height; y++)
		{
			for (int x = 0; x < Width; x++)
			{
				int minX = Math.Max(0, x - radius);
				int maxX = Math.Min(Width - 1, x + radius);
				int minY = Math.Max(0, y - radius);
				int maxY = Math.Min(Height - 1, y + radius);
				double sum = 0;
				int count = 0;
				for (int sampleY = minY; sampleY <= maxY; sampleY++)
				{
					for (int sampleX = minX; sampleX <= maxX; sampleX++)
					{
						sum += luminance[sampleY * Width + sampleX];
						count++;
					}
				}

				float mean = (float)(sum / Math.Max(1, count));
				double squaredSum = 0;
				for (int sampleY = minY; sampleY <= maxY; sampleY++)
				{
					for (int sampleX = minX; sampleX <= maxX; sampleX++)
					{
						float delta = luminance[sampleY * Width + sampleX] - mean;
						squaredSum += delta * delta;
					}
				}

				variance[y * Width + x] = (float)Math.Sqrt(squaredSum / Math.Max(1, count));
			}
		}

		return variance;
	}

	public void SavePng(string path)
	{
		using var bitmap = new SKBitmap(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
		Span<byte> pixels = bitmap.GetPixelSpan();
		int stride = bitmap.RowBytes;
		for (int y = 0; y < Height; y++)
		{
			int rowStart = y * stride;
			for (int x = 0; x < Width; x++)
			{
				int target = rowStart + x * 4;
				pixels[target] = (byte)Math.Clamp((int)(Red[y * Width + x] * 255f), 0, 255);
				pixels[target + 1] = (byte)Math.Clamp((int)(Green[y * Width + x] * 255f), 0, 255);
				pixels[target + 2] = (byte)Math.Clamp((int)(Blue[y * Width + x] * 255f), 0, 255);
				pixels[target + 3] = 255;
			}
		}

		using SKImage image = SKImage.FromBitmap(bitmap);
		using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
		using FileStream stream = File.Create(path);
		data.SaveTo(stream);
	}
}
