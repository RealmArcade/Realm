using System.Globalization;
using System.Text.Json.Nodes;
using Imazen.WebP;
using Realm.Shared.Metadata;
using SkiaSharp;

namespace Realm.Shared.Textures;

public static partial class TextureConverter
	{
	private static readonly float[] SrgbToLinearLut = InitializeSrgbToLinearLut();

	private static float[] InitializeSrgbToLinearLut()
	{
		float[] lut = new float[256];
		for (int i = 0; i < 256; i++)
		{
			float s = i / 255.0f;
			lut[i] = s <= 0.04045f ? s / 12.92f : MathF.Pow((s + 0.055f) / 1.055f, 2.4f);
		}

		return lut;
	}

	private static byte LinearToSrgbByte(float lin)
	{
		if (lin <= 0.0f) return 0;
		if (lin >= 1.0f) return 255;
		float srgb = lin <= 0.0031308f ? lin * 12.92f : 1.055f * MathF.Pow(lin, 1.0f / 2.4f) - 0.055f;
		return (byte)Math.Clamp((int)Math.Round(srgb * 255.0f), 0, 255);
	}

	private static void AccumulatePixelLuminance(SKColor pixel, bool ignoreBlack, ref double totalLuminance, ref long validPixelCount)
	{
		if (pixel.Alpha < 13 || (ignoreBlack && pixel.Red == 0 && pixel.Green == 0 && pixel.Blue == 0))
		{
			return;
		}

		float rLinear = SrgbToLinearLut[pixel.Red];
		float gLinear = SrgbToLinearLut[pixel.Green];
		float bLinear = SrgbToLinearLut[pixel.Blue];

		float rPow = rLinear * rLinear;
		float gPow = gLinear * gLinear;
		float bPow = bLinear * bLinear;

		float lum = (0.2126f * rPow) + (0.7152f * gPow) + (0.0722f * bPow);
		totalLuminance += lum;
		validPixelCount++;
	}

	private static (double TotalLuminance, long PixelCount) AccumulateLuminance(SKBitmap sourceImage, bool ignoreBlack)
	{
		int width = sourceImage.Width;
		int height = sourceImage.Height;
		double totalReshapedLuminance = 0.0;
		long validPixelCount = 0;

		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				AccumulatePixelLuminance(sourceImage.GetPixel(x, y), ignoreBlack, ref totalReshapedLuminance, ref validPixelCount);
			}
		}

		return (totalReshapedLuminance, validPixelCount);
	}

	public static float CalculateLuminanceScaleFactor(
		SKBitmap sourceImage,
		float targetLinearLuminance = 0.1133f,
		float minScaleFactor = 0.2f,
		float maxScaleFactor = 4.0f)
	{
		var (totalLuminance, validPixelCount) = AccumulateLuminance(sourceImage, ignoreBlack: true);

		if (validPixelCount == 0)
		{
			(totalLuminance, validPixelCount) = AccumulateLuminance(sourceImage, ignoreBlack: false);
		}

		if (validPixelCount == 0)
		{
			return 1.0f;
		}

		float avgLuminance = (float)(totalLuminance / validPixelCount);
		if (avgLuminance <= 0.0001f)
		{
			return 1.0f;
		}

		float rawScaleFactor = targetLinearLuminance / avgLuminance;
		return Math.Clamp(rawScaleFactor, minScaleFactor, maxScaleFactor);
	}

	public static float CalculateLuminanceScaleFactor(
		string imagePath,
		float targetLinearLuminance = 0.1133f,
		float minScaleFactor = 0.2f,
		float maxScaleFactor = 4.0f)
	{
		if (!File.Exists(imagePath)) return 1.0f;
		try
		{
			string ext = Path.GetExtension(imagePath).ToLowerInvariant();
			if (ext == ".rtex")
			{
				using var rtexImg = ExtractImageFromRtex(imagePath, layer: 0);
				if (rtexImg != null)
				{
					return CalculateLuminanceScaleFactor(rtexImg, targetLinearLuminance, minScaleFactor, maxScaleFactor);
				}
			}

			using var img = SKBitmap.Decode(imagePath);
			if (img == null) return 1.0f;
			return CalculateLuminanceScaleFactor(img, targetLinearLuminance, minScaleFactor, maxScaleFactor);
		}
		catch
		{
			return 1.0f;
		}
	}

}
