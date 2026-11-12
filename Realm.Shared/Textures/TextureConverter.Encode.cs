using System.Globalization;
using System.Text.Json.Nodes;
using Imazen.WebP;
using Realm.Shared.Metadata;
using SkiaSharp;

namespace Realm.Shared.Textures;

/// <summary>encoding webp and the rtex container it is wrapped in.</summary>
public static partial class TextureConverter
	{
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
			byte[] l1Bytes = EncodeWebp(layer1, lossless: true);

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

	private static (TextureConversionResult Result, SKBitmap? SourceImage, byte[]? OriginalBits, string? OriginalBlake3) LoadAndDecodeSourceImage(string rawImagePath, string outputRtexPath)
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
			return (result, null, null, null);
		}

		try
		{
			byte[] originalBits = File.ReadAllBytes(result.InputPath);
			string originalBlake3 = RealmMetadataHelper.ComputeBlake3(originalBits, Path.GetExtension(result.InputPath));

			var sourceImage = SKBitmap.Decode(result.InputPath);
			if (sourceImage == null)
			{
				result.Success = false;
				result.ErrorMessage = $"Failed to decode image file: {rawImagePath}";
				return (result, null, originalBits, originalBlake3);
			}

			result.Success = true;
			return (result, sourceImage, originalBits, originalBlake3);
		}
		catch (Exception ex)
		{
			result.Success = false;
			result.ErrorMessage = ex.Message;
			return (result, null, null, null);
		}
	}
}
