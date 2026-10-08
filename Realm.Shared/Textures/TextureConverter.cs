using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Realm.Shared.Metadata;
using SkiaSharp;
using Imazen.WebP;

namespace Realm.Shared.Textures;

public class TextureConversionResult
{
	public bool Success { get; set; }
	public string InputPath { get; set; } = string.Empty;
	public string OutputPath { get; set; } = string.Empty;
	public string ErrorMessage { get; set; } = string.Empty;
	public float ScaleFactor { get; set; } = 1.0f;
}

public static class TextureConverter
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
				dstRow[idx] = srcRow[idx + 2];     // Red
				dstRow[idx + 1] = srcRow[idx + 1]; // Green
				dstRow[idx + 2] = srcRow[idx];     // Blue
				dstRow[idx + 3] = srcRow[idx + 3]; // Alpha
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

	public static byte[] EncodeWebp(SKBitmap image, bool lossless = false, int quality = 90, int method = 6, bool sharpYuv = true)
	{
		int width = image.Width;
		int height = image.Height;
		byte[] pixelBytes = new byte[width * height * 4];

		SKBitmap workBitmap = image;
		bool disposeWork = false;

		if (image.ColorType != SKColorType.Rgba8888 && image.ColorType != SKColorType.Bgra8888)
		{
			workBitmap = image.Copy(SKColorType.Rgba8888);
			disposeWork = workBitmap != null && workBitmap != image;
			if (workBitmap == null) workBitmap = image;
		}

		try
		{
			ExtractPixelBytes(workBitmap, pixelBytes);
		}
		finally
		{
			if (disposeWork)
			{
				workBitmap.Dispose();
			}
		}

		int effMethod = Math.Clamp(method, 0, 6);
		var config = new WebPEncoderConfig();
		if (lossless)
		{
			config.SetLossless(true)
				.SetLosslessPreset(9)
				.SetMethod(effMethod)
				.SetExact(true)
				.SetMultiThreaded(true);
		}
		else
		{
			config.SetQuality(Math.Clamp(quality, 0, 100))
				.SetMethod(effMethod)
				.SetSharpYuv(sharpYuv)
				.SetMultiThreaded(true);
		}

		byte[] encoded = WebPEncoder.Encode(pixelBytes, width, height, width * 4, WebPPixelFormat.Rgba, config);
		if (encoded == null || encoded.Length == 0)
		{
			throw new InvalidOperationException("Failed to encode WebP image using libwebp.");
		}

		return encoded;
	}

	private static bool EncodeTwoLayerPbrRtex(
		SKBitmap layer0,
		SKBitmap layer1,
		string outputRtexPath,
		string metadataJson,
		out string errorMessage,
		bool compressAlbedo = true)
	{
		errorMessage = string.Empty;
		try
		{
			byte[] l0Bytes = EncodeWebp(layer0, lossless: !compressAlbedo, quality: 90);
			byte[] l1Bytes = EncodeWebp(layer1, lossless: true); // Always lossless for PBR normal/height/roughness

			string? dir = Path.GetDirectoryName(outputRtexPath);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

			byte[] rtexBytes = RtexFile.Build(metadataJson, [l0Bytes, l1Bytes]);
			File.WriteAllBytes(outputRtexPath, rtexBytes);
			RealmMetadataHelper.SyncBlake3Metadata(outputRtexPath);
			return true;
		}
		catch (Exception ex)
		{
			errorMessage = ex.Message;
			return false;
		}
	}

	private static bool EncodeSingleLayerRtex(
		SKBitmap image,
		string outputRtexPath,
		string metadataJson,
		out string errorMessage,
		bool lossless = false,
		int quality = 90)
	{
		errorMessage = string.Empty;
		try
		{
			byte[] l0Bytes = EncodeWebp(image, lossless: lossless, quality: quality);
			byte[] rtexBytes = RtexFile.Build(metadataJson, [l0Bytes]);
			string? dir = Path.GetDirectoryName(outputRtexPath);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
			File.WriteAllBytes(outputRtexPath, rtexBytes);
			RealmMetadataHelper.SyncBlake3Metadata(outputRtexPath);
			return true;
		}
		catch (Exception ex)
		{
			errorMessage = ex.Message;
			return false;
		}
	}

	public static TextureConversionResult ProcessAndSaveTerrainTexture(
		string rawImagePath,
		string outputRtexPath,
		float? forcedScaleFactor = null,
		bool enableRdo = true)
	{
		var result = new TextureConversionResult
		{
			InputPath = Path.GetFullPath(rawImagePath),
			OutputPath = Path.GetFullPath(outputRtexPath)
		};

		if (!File.Exists(result.InputPath))
		{
			result.Success = false;
			result.ErrorMessage = $"Input file not found: {rawImagePath}";
			return result;
		}

		try
		{
			byte[] originalBits = File.ReadAllBytes(result.InputPath);
			string originalBlake3 = RealmMetadataHelper.ComputeBlake3(originalBits, Path.GetExtension(result.InputPath));

			using var sourceImage = SKBitmap.Decode(result.InputPath);
			if (sourceImage == null)
			{
				result.Success = false;
				result.ErrorMessage = $"Failed to decode image file: {rawImagePath}";
				return result;
			}
			float scaleFactor = forcedScaleFactor ?? CalculateLuminanceScaleFactor(sourceImage);
			result.ScaleFactor = scaleFactor;

			ProcessTerrainPbr(sourceImage, isDecal: false, out var layer0, out var layer1);

			using var l0 = layer0;
			using var l1 = layer1;

			string metadataJson = $"{{\"created_utc\":\"{DateTime.UtcNow:O}\",\"type\":\"terrain_texture\",\"canonical_blake3\":\"{originalBlake3}\",\"scale_factor\":{scaleFactor.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)},\"layers\":2}}";
			bool encodeOk = EncodeTwoLayerPbrRtex(
				layer0,
				layer1,
				result.OutputPath,
				metadataJson,
				out string errorMsg,
				compressAlbedo: true);

			if (!encodeOk)
			{
				result.Success = false;
				result.ErrorMessage = errorMsg;
				return result;
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

	public static TextureConversionResult ProcessAndSaveDecalTexture(
		string rawImagePath,
		string outputRtexPath,
		float? forcedScaleFactor = null,
		bool enableRdo = true,
		int columns = 1,
		int rows = 1)
	{
		var result = new TextureConversionResult
		{
			InputPath = Path.GetFullPath(rawImagePath),
			OutputPath = Path.GetFullPath(outputRtexPath)
		};

		if (!File.Exists(result.InputPath))
		{
			result.Success = false;
			result.ErrorMessage = $"Input file not found: {rawImagePath}";
			return result;
		}

		try
		{
			byte[] originalBits = File.ReadAllBytes(result.InputPath);
			string originalBlake3 = RealmMetadataHelper.ComputeBlake3(originalBits, Path.GetExtension(result.InputPath));

			using var sourceImage = SKBitmap.Decode(result.InputPath);
			if (sourceImage == null)
			{
				result.Success = false;
				result.ErrorMessage = $"Failed to decode image file: {rawImagePath}";
				return result;
			}
			float scaleFactor = forcedScaleFactor ?? CalculateLuminanceScaleFactor(sourceImage);
			result.ScaleFactor = scaleFactor;

			int safeCols = Math.Max(1, columns);
			int safeRows = Math.Max(1, rows);

			ProcessTerrainPbr(sourceImage, isDecal: true, out var layer0, out var layer1);

			using var l0 = layer0;
			using var l1 = layer1;

			string metadataJson = $"{{\"created_utc\":\"{DateTime.UtcNow:O}\",\"type\":\"decal\",\"canonical_blake3\":\"{originalBlake3}\",\"scale_factor\":{scaleFactor.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)},\"columns\":{safeCols},\"rows\":{safeRows},\"layers\":2}}";
			bool encodeOk = EncodeTwoLayerPbrRtex(
				layer0,
				layer1,
				result.OutputPath,
				metadataJson,
				out string errorMsg,
				compressAlbedo: true);

			if (!encodeOk)
			{
				result.Success = false;
				result.ErrorMessage = errorMsg;
				return result;
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

	public static TextureConversionResult ProcessAndSaveSpritesheet(
		string rawImagePath,
		string outputRtexPath,
		int columns = 4,
		int rows = 4,
		float fps = 20.0f,
		bool enableRdo = false)
	{
		var result = new TextureConversionResult
		{
			InputPath = Path.GetFullPath(rawImagePath),
			OutputPath = Path.GetFullPath(outputRtexPath)
		};

		if (!File.Exists(result.InputPath))
		{
			result.Success = false;
			result.ErrorMessage = $"Input file not found: {rawImagePath}";
			return result;
		}

		try
		{
			byte[] originalBits = File.ReadAllBytes(result.InputPath);
			string originalBlake3 = RealmMetadataHelper.ComputeBlake3(originalBits, Path.GetExtension(result.InputPath));

			using var sourceImage = SKBitmap.Decode(result.InputPath);
			if (sourceImage == null)
			{
				result.Success = false;
				result.ErrorMessage = $"Failed to decode image file: {rawImagePath}";
				return result;
			}
			string metadataJson = $"{{\"created_utc\":\"{DateTime.UtcNow:O}\",\"type\":\"vfx_spritesheet\",\"canonical_blake3\":\"{originalBlake3}\",\"columns\":{columns},\"rows\":{rows},\"fps\":{fps.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},\"layers\":1}}";

			bool encodeOk = EncodeSingleLayerRtex(
				sourceImage,
				result.OutputPath,
				metadataJson,
				out string errorMsg,
				lossless: false,
				quality: 90);

			if (!encodeOk)
			{
				result.Success = false;
				result.ErrorMessage = errorMsg;
				return result;
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

	public static TextureConversionResult ProcessAndSaveSkybox(
		string rawImagePath,
		string outputRtexPath,
		bool enableRdo = false,
		float horizonBlendStart = 0.5f,
		SKColor? horizonColor = null,
		float wrapBlendWidth = 0.05f,
		float zenithBlendEnd = 0.08f,
		SKColor? zenithColor = null)
	{
		var result = new TextureConversionResult
		{
			InputPath = Path.GetFullPath(rawImagePath),
			OutputPath = Path.GetFullPath(outputRtexPath)
		};

		if (!File.Exists(result.InputPath))
		{
			result.Success = false;
			result.ErrorMessage = $"Input file not found: {rawImagePath}";
			return result;
		}

		try
		{
			byte[] originalBits = File.ReadAllBytes(result.InputPath);
			string originalBlake3 = RealmMetadataHelper.ComputeBlake3(originalBits, Path.GetExtension(result.InputPath));

			using var sourceImage = Path.GetExtension(result.InputPath).Equals(".rtex", StringComparison.OrdinalIgnoreCase)
				? ExtractImageFromRtex(result.InputPath, 0) ?? throw new InvalidOperationException($"Failed to load image from RTEX: {result.InputPath}")
				: SKBitmap.Decode(originalBits);

			if (sourceImage == null)
			{
				result.Success = false;
				result.ErrorMessage = $"Failed to decode skybox image: {rawImagePath}";
				return result;
			}

			using var processedImage = SkyboxProcessor.ProcessSkybox(
				sourceImage,
				horizonBlendStart,
				horizonColor,
				wrapBlendWidth,
				zenithBlendEnd,
				zenithColor);

			string metadataJson = $"{{\"created_utc\":\"{DateTime.UtcNow:O}\",\"type\":\"skybox\",\"canonical_blake3\":\"{originalBlake3}\",\"layers\":1}}";

			bool encodeOk = EncodeSingleLayerRtex(
				processedImage,
				result.OutputPath,
				metadataJson,
				out string errorMsg,
				lossless: false,
				quality: 95);

			if (!encodeOk)
			{
				result.Success = false;
				result.ErrorMessage = errorMsg;
				return result;
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

	public static TextureConversionResult ProcessAndSaveRibbonTexture(
		string rawImagePath,
		string outputRtexPath,
		bool enableRdo = false)
	{
		return ProcessAndSaveSingleLayerTexture(rawImagePath, outputRtexPath, "ribbon_texture", enableRdo);
	}

	public static TextureConversionResult ProcessAndSaveIconTexture(
		string rawImagePath,
		string outputRtexPath,
		bool enableRdo = true)
	{
		return ProcessAndSaveSingleLayerTexture(rawImagePath, outputRtexPath, "icon", enableRdo);
	}

	public static TextureConversionResult ProcessAndSaveVfxRadialTexture(
		string rawImagePath,
		string outputRtexPath,
		bool enableRdo = false)
	{
		return ProcessAndSaveSingleLayerTexture(rawImagePath, outputRtexPath, "vfx_radial", enableRdo);
	}

	public static TextureConversionResult ProcessAndSaveVfxVerticalTexture(
		string rawImagePath,
		string outputRtexPath,
		bool enableRdo = false)
	{
		return ProcessAndSaveSingleLayerTexture(rawImagePath, outputRtexPath, "vfx_vertical", enableRdo);
	}

	private static string BuildSingleLayerMetadata(string assetType, string originalBlake3, string? customMetadataJson)
	{
		string fallbackJson = $"{{\"created_utc\":\"{DateTime.UtcNow:O}\",\"type\":\"{assetType}\",\"canonical_blake3\":\"{originalBlake3}\",\"layers\":1}}";

		if (string.IsNullOrWhiteSpace(customMetadataJson))
		{
			return fallbackJson;
		}

		try
		{
			var metaObj = JsonNode.Parse(customMetadataJson)?.AsObject() ?? new JsonObject();
			metaObj["created_utc"] = $"{DateTime.UtcNow:O}";
			metaObj["type"] = assetType;
			metaObj["canonical_blake3"] = originalBlake3;
			metaObj["layers"] = 1;
			return metaObj.ToJsonString();
		}
		catch
		{
			return fallbackJson;
		}
	}

	public static TextureConversionResult ProcessAndSaveSingleLayerTexture(
		string rawImagePath,
		string outputRtexPath,
		string assetType,
		bool enableRdo = false,
		string? customMetadataJson = null)
	{
		var result = new TextureConversionResult
		{
			InputPath = Path.GetFullPath(rawImagePath),
			OutputPath = Path.GetFullPath(outputRtexPath)
		};

		if (!File.Exists(result.InputPath))
		{
			result.Success = false;
			result.ErrorMessage = $"Input file not found: {rawImagePath}";
			return result;
		}

		try
		{
			byte[] originalBits = File.ReadAllBytes(result.InputPath);
			string originalBlake3 = RealmMetadataHelper.ComputeBlake3(originalBits, Path.GetExtension(result.InputPath));

			using var sourceImage = SKBitmap.Decode(result.InputPath);
			if (sourceImage == null)
			{
				result.Success = false;
				result.ErrorMessage = $"Failed to decode image file: {rawImagePath}";
				return result;
			}

			string metadataJson = BuildSingleLayerMetadata(assetType, originalBlake3, customMetadataJson);

			bool encodeOk = EncodeSingleLayerRtex(
				sourceImage,
				result.OutputPath,
				metadataJson,
				out string errorMsg,
				lossless: false,
				quality: 90);

			if (!encodeOk)
			{
				result.Success = false;
				result.ErrorMessage = errorMsg;
				return result;
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

	public static SKBitmap? ExtractImageFromRtex(string rtexPath, int layer = 0)
	{
		if (!File.Exists(rtexPath)) return null;
		byte[] bytes = File.ReadAllBytes(rtexPath);
		return ExtractImageFromRtexBytes(bytes, layer);
	}

	public static SKBitmap? ExtractImageFromRtexBytes(ReadOnlySpan<byte> rtexBytes, int layer = 0)
	{
		byte[]? webpBytes = RtexFile.GetLayer(rtexBytes, layer);
		if (webpBytes == null || webpBytes.Length == 0) return null;
		return SKBitmap.Decode(webpBytes);
	}

	public static byte[]? ExtractWebpFromRtex(string rtexPath, int layer = 0)
	{
		if (!File.Exists(rtexPath)) return null;
		byte[] bytes = File.ReadAllBytes(rtexPath);
		return RtexFile.GetLayer(bytes, layer);
	}

	public static TextureConversionResult ExtractPngFromRtex(
		string inputRtexPath,
		string outputPngPath,
		int layer = 0)
	{
		string fullInput = Path.GetFullPath(inputRtexPath);
		string fullOutput = Path.GetFullPath(outputPngPath);

		var result = new TextureConversionResult
		{
			InputPath = fullInput,
			OutputPath = fullOutput
		};

		if (!File.Exists(fullInput))
		{
			result.Success = false;
			result.ErrorMessage = $"Input file not found: {inputRtexPath}";
			return result;
		}

		try
		{
			using var image = ExtractImageFromRtex(fullInput, layer);
			if (image == null)
			{
				result.Success = false;
				result.ErrorMessage = $"Failed to extract layer {layer} from RTEX '{inputRtexPath}'.";
				return result;
			}

			string? dir = Path.GetDirectoryName(fullOutput);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

			using var skImage = SKImage.FromBitmap(image);
			using var data = skImage.Encode(SKEncodedImageFormat.Png, 100);
			using var stream = File.Create(fullOutput);
			data.SaveTo(stream);

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

	public static TextureConversionResult ExtractWebpFromRtex(
		string inputRtexPath,
		string outputWebpPath,
		int layer = 0)
	{
		string fullInput = Path.GetFullPath(inputRtexPath);
		string fullOutput = Path.GetFullPath(outputWebpPath);

		var result = new TextureConversionResult
		{
			InputPath = fullInput,
			OutputPath = fullOutput
		};

		if (!File.Exists(fullInput))
		{
			result.Success = false;
			result.ErrorMessage = $"Input file not found: {inputRtexPath}";
			return result;
		}

		try
		{
			byte[]? webpBytes = ExtractWebpFromRtex(fullInput, layer);
			if (webpBytes == null || webpBytes.Length == 0)
			{
				result.Success = false;
				result.ErrorMessage = $"Failed to extract layer {layer} from RTEX '{inputRtexPath}'.";
				return result;
			}

			string? dir = Path.GetDirectoryName(fullOutput);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

			File.WriteAllBytes(fullOutput, webpBytes);

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

	private static void ExtractMetadataInfo(string fullInput, ref string normType, ref int? columns, ref int? rows, ref float? fps)
	{
		string? meta = RealmMetadataHelper.ExtractMetadata(fullInput);
		if (string.IsNullOrEmpty(meta)) return;

		try
		{
			var node = JsonNode.Parse(meta);
			if (node == null) return;

			string? metaType = node["type"]?.GetValue<string>()
				?? node["asset_type"]?.GetValue<string>()
				?? node["AssetType"]?.GetValue<string>();

			if (!string.IsNullOrEmpty(metaType))
			{
				normType = metaType.Trim().ToLowerInvariant();
			}

			columns ??= ExtractIntMetadata(node, "columns");
			rows ??= ExtractIntMetadata(node, "rows");
			fps ??= ExtractFloatMetadata(node, "fps");
		}
		catch
		{
			// Ignore JSON parsing errors
		}
	}

	private static int? ExtractIntMetadata(JsonNode node, string key)
	{
		if (node[key] != null && int.TryParse(node[key]?.ToString(), out int val) && val > 0)
		{
			return val;
		}
		return null;
	}

	private static float? ExtractFloatMetadata(JsonNode node, string key)
	{
		if (node[key] != null && float.TryParse(node[key]?.ToString(), out float val) && val > 0.001f)
		{
			return val;
		}
		return null;
	}

	public static TextureConversionResult ConvertTextureFile(
		string inputPath,
		string? outputPath,
		string? assetType = null,
		int? columns = null,
		int? rows = null,
		float? fps = null)
	{
		string fullInput = Path.GetFullPath(inputPath);
		string ext = Path.GetExtension(fullInput).ToLowerInvariant();

		if (ext == ".rtex")
		{
			return HandleRtexConversion(fullInput, outputPath);
		}

		string normType = ResolveAssetType(fullInput, assetType, ref columns, ref rows, ref fps);
		string targetRtex = GetTargetRtexPath(fullInput, outputPath);

		return ProcessByAssetType(normType, fullInput, targetRtex, columns, rows, fps);
	}

	private static TextureConversionResult HandleRtexConversion(string fullInput, string? outputPath)
	{
		string targetWebp = string.IsNullOrEmpty(outputPath)
			? Path.ChangeExtension(fullInput, ".webp")
			: Path.GetFullPath(outputPath);

		if (targetWebp.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
		{
			return ExtractPngFromRtex(fullInput, targetWebp);
		}
		
		return ExtractWebpFromRtex(fullInput, targetWebp);
	}

	private static string ResolveAssetType(string fullInput, string? assetType, ref int? columns, ref int? rows, ref float? fps)
	{
		string normType = (assetType ?? string.Empty).Trim().ToLowerInvariant();

		if (string.IsNullOrEmpty(normType))
		{
			ExtractMetadataInfo(fullInput, ref normType, ref columns, ref rows, ref fps);
		}

		if (string.IsNullOrEmpty(normType))
		{
			throw new InvalidOperationException($"Asset type was not specified and could not be detected from image metadata in '{fullInput}'. Please specify -t / --type (Decal, Icon, Noise, Ribbon, Skybox, Spritesheet, Terrain, vfx_radial, vfx_vertical).");
		}

		return normType;
	}

	private static string GetTargetRtexPath(string fullInput, string? outputPath)
	{
		if (string.IsNullOrEmpty(outputPath))
		{
			return Path.ChangeExtension(fullInput, ".rtex");
		}
		return Path.GetFullPath(outputPath);
	}

	private static TextureConversionResult ProcessByAssetType(string normType, string fullInput, string targetRtex, int? columns, int? rows, float? fps)
	{
		return normType switch
		{
			"terrain" => ProcessAndSaveTerrainTexture(fullInput, targetRtex),
			"decal" => ProcessDecalAsset(fullInput, targetRtex, columns, rows),
			"spritesheet" => ProcessSpritesheetAsset(fullInput, targetRtex, columns, rows, fps),
			"skybox" => ProcessSkybox(fullInput, targetRtex),
			"ribbon" => ProcessAndSaveRibbonTexture(fullInput, targetRtex),
			"noise" => ProcessAndSaveSingleLayerTexture(fullInput, targetRtex, "noise_texture"),
			"icon" => ProcessAndSaveIconTexture(fullInput, targetRtex),
			_ => ProcessVfxOrThrow(normType, fullInput, targetRtex)
		};
	}

	private static TextureConversionResult ProcessDecalAsset(string fullInput, string targetRtex, int? columns, int? rows)
	{
		return ProcessAndSaveDecalTexture(fullInput, targetRtex, columns: columns ?? 1, rows: rows ?? 1);
	}

	private static TextureConversionResult ProcessSpritesheetAsset(string fullInput, string targetRtex, int? columns, int? rows, float? fps)
	{
		return ProcessAndSaveSpritesheet(fullInput, targetRtex, columns ?? 4, rows ?? 4, fps: fps ?? 20.0f);
	}

	private static TextureConversionResult ProcessVfxOrThrow(string normType, string fullInput, string targetRtex)
	{
		return normType switch
		{
			"vfx_radial" => ProcessAndSaveVfxRadialTexture(fullInput, targetRtex),
			"vfx_vertical" => ProcessAndSaveVfxVerticalTexture(fullInput, targetRtex),
			_ => throw new InvalidOperationException($"Unsupported asset type '{normType}'. Supported types: Decal, Icon, Noise, Ribbon, Skybox, Spritesheet, Terrain, vfx_radial, vfx_vertical.")
		};
	}

	private static TextureConversionResult ProcessSkybox(string fullInput, string targetRtex)
	{
		if (Path.GetExtension(targetRtex).ToLowerInvariant() is not ".rtex")
		{
			return SkyboxProcessor.ProcessSkyboxFile(fullInput, targetRtex);
		}
		return ProcessAndSaveSkybox(fullInput, targetRtex);
	}

	private static void ProcessSingleDirectoryFile(
		string file,
		string fullInputDir,
		string? fullOutputDir,
		string? assetType,
		int? columns,
		int? rows,
		float? fps,
		ref int successCount,
		ref int failCount)
	{
		if (!ImageFormatConverter.IsImageFile(file)) return;

		string fileExt = Path.GetExtension(file).ToLowerInvariant();
		string targetExt = fileExt == ".rtex" ? ".webp" : ".rtex";

		string target;
		if (string.IsNullOrEmpty(fullOutputDir))
		{
			target = Path.ChangeExtension(file, targetExt);
		}
		else
		{
			string rel = Path.GetRelativePath(fullInputDir, file);
			target = Path.Combine(fullOutputDir, Path.ChangeExtension(rel, targetExt));
		}

		try
		{
			var res = ConvertTextureFile(file, target, assetType, columns, rows, fps);
			if (res.Success)
			{
				Console.WriteLine($"Converted: {file} -> {target}");
				successCount++;
			}
			else
			{
				Console.Error.WriteLine($"Failed to convert {file}: {res.ErrorMessage}");
				failCount++;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Failed to convert {file}: {ex.Message}");
			failCount++;
		}
	}

	public static int ConvertTextureDirectory(
		string inputDir,
		string? outputDir,
		string? assetType,
		bool recursive,
		int? columns = null,
		int? rows = null,
		float? fps = null)
	{
		string fullInputDir = Path.GetFullPath(inputDir);
		string? fullOutputDir = !string.IsNullOrEmpty(outputDir) ? Path.GetFullPath(outputDir) : null;

		var searchOpt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
		string[] files = Directory.GetFiles(fullInputDir, "*.*", searchOpt);
		int successCount = 0;
		int failCount = 0;

		foreach (var file in files)
		{
			ProcessSingleDirectoryFile(file, fullInputDir, fullOutputDir, assetType, columns, rows, fps, ref successCount, ref failCount);
		}

		Console.WriteLine($"Finished texture conversion. {successCount} succeeded, {failCount} failed.");
		return failCount > 0 ? 1 : 0;
	}

	private static float[,] ComputeSeparableBoxBlur(float[,] input, int w, int h, int radius, bool isDecal = false)
	{
		float[,] temp = new float[w, h];
		float[,] result = new float[w, h];
		int windowSize = 2 * radius + 1;
		float invWindow = 1.0f / windowSize;

		ComputeHorizontalBoxBlur(input, temp, w, h, radius, invWindow, isDecal);
		ComputeVerticalBoxBlur(temp, result, w, h, radius, invWindow, isDecal);

		return result;
	}

	private static void ComputeHorizontalBoxBlur(float[,] input, float[,] temp, int w, int h, int radius, float invWindow, bool isDecal)
	{
		for (int y = 0; y < h; y++)
		{
			float sum = 0.0f;
			for (int k = -radius; k <= radius; k++)
			{
				int px = isDecal ? Math.Clamp(k, 0, w - 1) : (k % w + w) % w;
				sum += input[px, y];
			}
			temp[0, y] = sum * invWindow;

			for (int x = 1; x < w; x++)
			{
				int removeX = isDecal ? Math.Clamp(x - 1 - radius, 0, w - 1) : ((x - 1 - radius) % w + w) % w;
				int addX = isDecal ? Math.Clamp(x + radius, 0, w - 1) : ((x + radius) % w + w) % w;
				sum += input[addX, y] - input[removeX, y];
				temp[x, y] = sum * invWindow;
			}
		}
	}

	private static void ComputeVerticalBoxBlur(float[,] temp, float[,] result, int w, int h, int radius, float invWindow, bool isDecal)
	{
		for (int x = 0; x < w; x++)
		{
			float sum = 0.0f;
			for (int k = -radius; k <= radius; k++)
			{
				int py = isDecal ? Math.Clamp(k, 0, h - 1) : (k % h + h) % h;
				sum += temp[x, py];
			}
			result[x, 0] = sum * invWindow;

			for (int y = 1; y < h; y++)
			{
				int removeY = isDecal ? Math.Clamp(y - 1 - radius, 0, h - 1) : ((y - 1 - radius) % h + h) % h;
				int addY = isDecal ? Math.Clamp(y + radius, 0, h - 1) : ((y + radius) % h + h) % h;
				sum += temp[x, addY] - temp[x, removeY];
				result[x, y] = sum * invWindow;
			}
		}
	}
}
