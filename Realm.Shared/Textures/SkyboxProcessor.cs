using SkiaSharp;

namespace Realm.Shared.Textures;

public static class SkyboxProcessor
{
	public static SKColor? ParseColor(string? colorString)
	{
		if (string.IsNullOrWhiteSpace(colorString) || colorString.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		string trimmed = colorString.Trim();
		if (trimmed.StartsWith('#'))
		{
			return TryParseHexColor(trimmed);
		}

		if (trimmed.Contains(','))
		{
			return TryParseRgbColor(trimmed);
		}

		return null;
	}

	private static SKColor? TryParseHexColor(string trimmedHex)
	{
		string hex = trimmedHex.TrimStart('#');
		if (hex.Length < 6)
		{
			return null;
		}

		if (byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
			byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
			byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
		{
			return new SKColor(r, g, b, 255);
		}

		return null;
	}

	private static SKColor? TryParseRgbColor(string trimmedRgb)
	{
		string[] parts = trimmedRgb.Split(',');
		if (parts.Length < 3)
		{
			return null;
		}

		if (byte.TryParse(parts[0].Trim(), out byte r) &&
			byte.TryParse(parts[1].Trim(), out byte g) &&
			byte.TryParse(parts[2].Trim(), out byte b))
		{
			return new SKColor(r, g, b, 255);
		}

		return null;
	}

	public static SKBitmap ProcessSkybox(
		SKBitmap sourceImage,
		float horizonBlendStart = 0.5f,
		SKColor? horizonColor = null,
		float wrapBlendWidth = 0.05f,
		float zenithBlendEnd = 0.08f,
		SKColor? zenithColor = null)
	{
		ArgumentNullException.ThrowIfNull(sourceImage);

		int width = sourceImage.Width;
		int height = sourceImage.Height;

		if (width <= 0 || height <= 0)
		{
			return sourceImage.Copy();
		}

		SKBitmap workingImage = sourceImage.Copy();

		int horizonY = Math.Clamp((int)(height * horizonBlendStart), 0, height);
		var hColorTuple = DetermineHorizonColor(workingImage, horizonColor, horizonY, width, height);
		BlendHorizon(workingImage, hColorTuple.r, hColorTuple.g, hColorTuple.b, horizonY, width, height);

		int zenithYEnd = Math.Clamp((int)(height * zenithBlendEnd), 0, height);
		var zColorTuple = DetermineZenithColor(workingImage, zenithColor, width);
		BlendZenith(workingImage, zColorTuple.r, zColorTuple.g, zColorTuple.b, zenithYEnd, width);

		int blendWidth = (int)(width * wrapBlendWidth);
		if (blendWidth <= 0 || blendWidth >= width)
		{
			return workingImage;
		}

		SKBitmap blendedImage = ApplyWrapBlend(workingImage, blendWidth, width, height);

		workingImage.Dispose();

		var resizedImage = blendedImage.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
		blendedImage.Dispose();

		return resizedImage ?? blendedImage;
	}

	private static (float r, float g, float b) DetermineHorizonColor(SKBitmap workingImage, SKColor? horizonColor, int horizonY, int width, int height)
	{
		if (horizonColor != null)
		{
			return (horizonColor.Value.Red, horizonColor.Value.Green, horizonColor.Value.Blue);
		}

		int sampleY = Math.Clamp(horizonY - (int)(height * 0.05f), 0, height - 1);
		float sumR = 0f;
		float sumG = 0f;
		float sumB = 0f;
		for (int x = 0; x < width; x++)
		{
			SKColor pixel = workingImage.GetPixel(x, sampleY);
			sumR += pixel.Red;
			sumG += pixel.Green;
			sumB += pixel.Blue;
		}
		return (sumR / width, sumG / width, sumB / width);
	}

	private static void BlendHorizon(SKBitmap workingImage, float horizonColorR, float horizonColorG, float horizonColorB, int horizonY, int width, int height)
	{
		int horizonSpan = height - 1 - horizonY;
		for (int y = horizonY; y < height; y++)
		{
			float t = horizonSpan > 0 ? (y - horizonY) / (float)horizonSpan : 1.0f;
			float tSmooth = 0.5f - 0.5f * MathF.Cos(MathF.PI * t);
			float oneMinusTSmooth = 1.0f - tSmooth;

			for (int x = 0; x < width; x++)
			{
				SKColor pixel = workingImage.GetPixel(x, y);
				byte r = (byte)Math.Clamp((int)Math.Round(oneMinusTSmooth * pixel.Red + tSmooth * horizonColorR), 0, 255);
				byte g = (byte)Math.Clamp((int)Math.Round(oneMinusTSmooth * pixel.Green + tSmooth * horizonColorG), 0, 255);
				byte b = (byte)Math.Clamp((int)Math.Round(oneMinusTSmooth * pixel.Blue + tSmooth * horizonColorB), 0, 255);
				workingImage.SetPixel(x, y, new SKColor(r, g, b, 255));
			}
		}
	}

	private static (float r, float g, float b) DetermineZenithColor(SKBitmap workingImage, SKColor? zenithColor, int width)
	{
		if (zenithColor != null)
		{
			return (zenithColor.Value.Red, zenithColor.Value.Green, zenithColor.Value.Blue);
		}

		float sumR = 0f;
		float sumG = 0f;
		float sumB = 0f;
		for (int x = 0; x < width; x++)
		{
			SKColor pixel = workingImage.GetPixel(x, 0);
			sumR += pixel.Red;
			sumG += pixel.Green;
			sumB += pixel.Blue;
		}
		return (sumR / width, sumG / width, sumB / width);
	}

	private static void BlendZenith(SKBitmap workingImage, float zenithColorR, float zenithColorG, float zenithColorB, int zenithYEnd, int width)
	{
		for (int y = 0; y < zenithYEnd; y++)
		{
			float t = zenithYEnd > 0 ? y / (float)zenithYEnd : 0.0f;
			float tSmooth = 0.5f + 0.5f * MathF.Cos(MathF.PI * t);
			float oneMinusTSmooth = 1.0f - tSmooth;

			for (int x = 0; x < width; x++)
			{
				SKColor pixel = workingImage.GetPixel(x, y);
				byte r = (byte)Math.Clamp((int)Math.Round(oneMinusTSmooth * pixel.Red + tSmooth * zenithColorR), 0, 255);
				byte g = (byte)Math.Clamp((int)Math.Round(oneMinusTSmooth * pixel.Green + tSmooth * zenithColorG), 0, 255);
				byte b = (byte)Math.Clamp((int)Math.Round(oneMinusTSmooth * pixel.Blue + tSmooth * zenithColorB), 0, 255);
				workingImage.SetPixel(x, y, new SKColor(r, g, b, 255));
			}
		}
	}

	private static SKBitmap ApplyWrapBlend(SKBitmap workingImage, int blendWidth, int width, int height)
	{
		int newWidth = width - blendWidth;
		SKBitmap blendedImage = new SKBitmap(newWidth, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

		for (int y = 0; y < height; y++)
		{
			for (int x = blendWidth; x < newWidth; x++)
			{
				blendedImage.SetPixel(x, y, workingImage.GetPixel(x, y));
			}

			for (int x = 0; x < blendWidth; x++)
			{
				float t = blendWidth > 0 ? x / (float)blendWidth : 0.0f;
				float tSmooth = 0.5f - 0.5f * MathF.Cos(MathF.PI * t);
				float oneMinusTSmooth = 1.0f - tSmooth;

				SKColor leftVal = workingImage.GetPixel(x, y);
				SKColor rightVal = workingImage.GetPixel(newWidth + x, y);

				byte r = (byte)Math.Clamp((int)Math.Round(oneMinusTSmooth * rightVal.Red + tSmooth * leftVal.Red), 0, 255);
				byte g = (byte)Math.Clamp((int)Math.Round(oneMinusTSmooth * rightVal.Green + tSmooth * leftVal.Green), 0, 255);
				byte b = (byte)Math.Clamp((int)Math.Round(oneMinusTSmooth * rightVal.Blue + tSmooth * leftVal.Blue), 0, 255);
				blendedImage.SetPixel(x, y, new SKColor(r, g, b, 255));
			}
		}

		return blendedImage;
	}

	public static TextureConversionResult ProcessSkyboxFile(
		string inputPath,
		string outputPath,
		float horizonBlendStart = 0.5f,
		SKColor? horizonColor = null,
		float wrapBlendWidth = 0.05f,
		float zenithBlendEnd = 0.08f,
		SKColor? zenithColor = null)
	{
		string fullInput = Path.GetFullPath(inputPath);
		string fullOutput = Path.GetFullPath(outputPath);

		var result = new TextureConversionResult
		{
			InputPath = fullInput,
			OutputPath = fullOutput
		};

		if (!File.Exists(fullInput))
		{
			result.Success = false;
			result.ErrorMessage = $"Input file not found: {inputPath}";
			return result;
		}

		try
		{
			string ext = Path.GetExtension(fullOutput).ToLowerInvariant();
			if (ext == ".rtex")
			{
				return TextureConverter.ProcessAndSaveSkybox(
					fullInput,
					fullOutput,
					false,
					horizonBlendStart,
					horizonColor,
					wrapBlendWidth,
					zenithBlendEnd,
					zenithColor);
			}

			using var sourceImage = Path.GetExtension(fullInput).Equals(".rtex", StringComparison.OrdinalIgnoreCase)
				? TextureConverter.ExtractImageFromRtex(fullInput, 0) ?? throw new InvalidOperationException($"Failed to load image from RTEX: {fullInput}")
				: SKBitmap.Decode(fullInput);

			if (sourceImage == null)
			{
				throw new InvalidOperationException($"Failed to decode skybox image: {fullInput}");
			}

			using var processedImage = ProcessSkybox(
				sourceImage,
				horizonBlendStart,
				horizonColor,
				wrapBlendWidth,
				zenithBlendEnd,
				zenithColor);

			string? dir = Path.GetDirectoryName(fullOutput);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			if (ext == ".webp")
			{
				byte[] webpBytes = TextureConverter.EncodeWebp(processedImage, lossless: false, quality: 95);
				File.WriteAllBytes(fullOutput, webpBytes);
			}
			else
			{
				using var skImage = SKImage.FromBitmap(processedImage);
				using var data = skImage.Encode(SKEncodedImageFormat.Png, 100);
				using var stream = File.Create(fullOutput);
				data.SaveTo(stream);
			}

			result.Success = true;
			return result;
		}
		catch (Exception ex)
		{
			result.Success = false;
			result.ErrorMessage = ex.Message;
			return result;
		}
	}
}
