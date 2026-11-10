using System.Globalization;
using System.Text.Json.Nodes;
using Imazen.WebP;
using Realm.Shared.Metadata;
using SkiaSharp;

namespace Realm.Shared.Textures;

/// <summary>normalising luminance and deriving the terrain pbr layers from it.</summary>
public static partial class TextureConverter
	{
	public static SKBitmap NormalizeLuminance(SKBitmap sourceImage, float scaleFactor)
	{
		int width = sourceImage.Width;
		int height = sourceImage.Height;
		var result = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

		byte[] scaledLut = new byte[256];
		for (int i = 0; i < 256; i++)
		{
			float lin = SrgbToLinearLut[i] * scaleFactor;
			scaledLut[i] = LinearToSrgbByte(lin);
		}

		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				SKColor pixel = sourceImage.GetPixel(x, y);
				if (pixel.Alpha < 13)
				{
					result.SetPixel(x, y, pixel);
					continue;
				}

				byte r = scaledLut[pixel.Red];
				byte g = scaledLut[pixel.Green];
				byte b = scaledLut[pixel.Blue];

				result.SetPixel(x, y, new SKColor(r, g, b, pixel.Alpha));
			}
		}

		return result;
	}

	public static void ProcessTerrainPbr(
		SKBitmap sourceImage,
		bool isDecal,
		out SKBitmap layer0,
		out SKBitmap layer1)
	{
		int width = sourceImage.Width;
		int height = sourceImage.Height;

		float[,] luminance = ComputeLuminance(sourceImage, width, height);

		float[,] fineMean = ComputeSeparableBoxBlur(luminance, width, height, 3, isDecal);
		float[,] coarseMean = ComputeSeparableBoxBlur(luminance, width, height, 14, isDecal);

		float[] flatHeights = new float[width * height];
		float[,] rawHeight = ComputeRawHeight(luminance, fineMean, coarseMean, width, height, isDecal, flatHeights);

		float[,] normalizedHeight = NormalizeHeight(rawHeight, flatHeights, width, height);

		GenerateTerrainLayers(sourceImage, luminance, fineMean, normalizedHeight, isDecal, width, height, out layer0, out layer1);
	}

	private static float[,] ComputeLuminance(SKBitmap sourceImage, int width, int height)
	{
		float[,] luminance = new float[width, height];
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				SKColor p = sourceImage.GetPixel(x, y);
				luminance[x, y] = (0.299f * p.Red + 0.587f * p.Green + 0.114f * p.Blue) / 255.0f;
			}
		}

		return luminance;
	}

	private static int GetPrevCoord(int coord, bool isDecal, int maxVal)
	{
		if (coord > 0)
		{
			return coord - 1;
		}

		return isDecal ? coord : maxVal - 1;
	}

	private static int GetNextCoord(int coord, bool isDecal, int maxVal)
	{
		if (coord < maxVal - 1)
		{
			return coord + 1;
		}

		return isDecal ? coord : 0;
	}

	private static float[,] ComputeRawHeight(float[,] luminance, float[,] fineMean, float[,] coarseMean, int width, int height, bool isDecal, float[] flatHeights)
	{
		float[,] rawHeight = new float[width, height];
		int idx = 0;
		for (int y = 0; y < height; y++)
		{
			int py = GetPrevCoord(y, isDecal, height);
			int ny = GetNextCoord(y, isDecal, height);

			for (int x = 0; x < width; x++)
			{
				int px = GetPrevCoord(x, isDecal, width);
				int nx = GetNextCoord(x, isDecal, width);

				float lum = luminance[x, y];
				float highFreq = lum - fineMean[x, y];
				float midFreq = fineMean[x, y] - coarseMean[x, y];

				float l00 = luminance[px, py];
				float l10 = luminance[x, py];
				float l20 = luminance[nx, py];
				float l01 = luminance[px, y];
				float l21 = luminance[nx, y];
				float l02 = luminance[px, ny];
				float l12 = luminance[x, ny];
				float l22 = luminance[nx, ny];

				float dx = ((3.0f * l20 + 10.0f * l21 + 3.0f * l22) - (3.0f * l00 + 10.0f * l01 + 3.0f * l02)) / 32.0f;
				float dy = ((3.0f * l02 + 10.0f * l12 + 3.0f * l22) - (3.0f * l00 + 10.0f * l10 + 3.0f * l20)) / 32.0f;
				float gradMag = MathF.Sqrt(dx * dx + dy * dy);
				float laplacian = l21 + l01 + l12 + l10 - 4.0f * lum;

				float structuralValue = 0.5f + (highFreq * 2.2f) + (midFreq * 1.4f) + (laplacian * 0.5f) - (gradMag * 0.25f);
				rawHeight[x, y] = structuralValue;
				flatHeights[idx++] = structuralValue;
			}
		}

		return rawHeight;
	}

	private static float[,] NormalizeHeight(float[,] rawHeight, float[] flatHeights, int width, int height)
	{
		Array.Sort(flatHeights);
		int totalPixels = flatHeights.Length;
		int p1Index = Math.Clamp((int)(totalPixels * 0.01f), 0, totalPixels - 1);
		int p99Index = Math.Clamp((int)(totalPixels * 0.99f), 0, totalPixels - 1);
		float lowPercentile = flatHeights[p1Index];
		float highPercentile = flatHeights[p99Index];

		float normRange = highPercentile - lowPercentile;
		float invNormRange = normRange > 1e-5f ? 1.0f / normRange : 0.0f;
		float[,] normalizedHeight = new float[width, height];

		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				float normH = (rawHeight[x, y] - lowPercentile) * invNormRange;
				normalizedHeight[x, y] = Math.Clamp(normH, 0.0f, 1.0f);
			}
		}

		return normalizedHeight;
	}

	private static void GenerateTerrainLayers(SKBitmap sourceImage, float[,] luminance, float[,] fineMean, float[,] normalizedHeight, bool isDecal, int width, int height, out SKBitmap layer0, out SKBitmap layer1)
	{
		layer0 = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
		layer1 = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

		float normalStrength = 2.5f;

		for (int y = 0; y < height; y++)
		{
			int py = GetPrevCoord(y, isDecal, height);
			int ny = GetNextCoord(y, isDecal, height);

			for (int x = 0; x < width; x++)
			{
				int px = GetPrevCoord(x, isDecal, width);
				int nx = GetNextCoord(x, isDecal, width);

				SKColor albedoCol = sourceImage.GetPixel(x, y);
				float heightVal = normalizedHeight[x, y];
				byte heightByte = (byte)Math.Clamp((int)Math.Round(heightVal * 255.0f), 0, 255);
				byte alphaVal = isDecal ? albedoCol.Alpha : (byte)255;

				layer0.SetPixel(x, y, new SKColor(albedoCol.Red, albedoCol.Green, albedoCol.Blue, alphaVal));

				float h00 = normalizedHeight[px, py];
				float h10 = normalizedHeight[x, py];
				float h20 = normalizedHeight[nx, py];
				float h01 = normalizedHeight[px, y];
				float h21 = normalizedHeight[nx, y];
				float h02 = normalizedHeight[px, ny];
				float h12 = normalizedHeight[x, ny];
				float h22 = normalizedHeight[nx, ny];

				float scharrX = ((3.0f * h20 + 10.0f * h21 + 3.0f * h22) - (3.0f * h00 + 10.0f * h01 + 3.0f * h02)) / 32.0f;
				float scharrY = ((3.0f * h02 + 10.0f * h12 + 3.0f * h22) - (3.0f * h00 + 10.0f * h10 + 3.0f * h20)) / 32.0f;

				float dX = scharrX * normalStrength;
				float dY = scharrY * normalStrength;

				float len = MathF.Sqrt(dX * dX + dY * dY + 1.0f);
				float invLen = 1.0f / len;
				float normX = -dX * invLen;
				float normY = -dY * invLen;

				byte normR = (byte)Math.Clamp((int)Math.Round((normX * 0.5f + 0.5f) * 255.0f), 0, 255);
				byte normG = (byte)Math.Clamp((int)Math.Round((normY * 0.5f + 0.5f) * 255.0f), 0, 255);
				byte normB = heightByte;

				float contrastHeight = Math.Clamp((heightVal - 0.5f) * 1.4f + 0.5f, 0.0f, 1.0f);
				float highDetail = Math.Abs(luminance[x, y] - fineMean[x, y]);
				float lerpVal = 0.85f + (0.45f - 0.85f) * contrastHeight;
				float roughness = Math.Clamp(lerpVal + highDetail * 0.8f, 0.15f, 0.95f);
				byte normA = (byte)Math.Clamp((int)Math.Round(roughness * 255.0f), 0, 255);

				layer1.SetPixel(x, y, new SKColor(normR, normG, normB, normA));
			}
		}
	}
}
