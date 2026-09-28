using System;
using System.IO;
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
			string hex = trimmed.TrimStart('#');
			if (hex.Length >= 6 &&
				byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
				byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
				byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
			{
				return new SKColor(r, g, b, 255);
			}
		}
		else if (trimmed.Contains(','))
		{
			string[] parts = trimmed.Split(',');
			if (parts.Length >= 3 &&
				byte.TryParse(parts[0].Trim(), out byte r) &&
				byte.TryParse(parts[1].Trim(), out byte g) &&
				byte.TryParse(parts[2].Trim(), out byte b))
			{
				return new SKColor(r, g, b, 255);
			}
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
		float horizonColorR;
		float horizonColorG;
		float horizonColorB;

		if (horizonColor == null)
		{
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
			horizonColorR = sumR / width;
			horizonColorG = sumG / width;
			horizonColorB = sumB / width;
		}
		else
		{
			horizonColorR = horizonColor.Value.Red;
			horizonColorG = horizonColor.Value.Green;
			horizonColorB = horizonColor.Value.Blue;
		}

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

		int zenithYEnd = Math.Clamp((int)(height * zenithBlendEnd), 0, height);
		float zenithColorR;
		float zenithColorG;
		float zenithColorB;

		if (zenithColor == null)
		{
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
			zenithColorR = sumR / width;
			zenithColorG = sumG / width;
			zenithColorB = sumB / width;
		}
		else
		{
			zenithColorR = zenithColor.Value.Red;
			zenithColorG = zenithColor.Value.Green;
			zenithColorB = zenithColor.Value.Blue;
		}

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

		int blendWidth = (int)(width * wrapBlendWidth);
		if (blendWidth <= 0 || blendWidth >= width)
		{
			return workingImage;
		}

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

		workingImage.Dispose();

		var resizedImage = blendedImage.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
		blendedImage.Dispose();

		return resizedImage ?? blendedImage;
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
