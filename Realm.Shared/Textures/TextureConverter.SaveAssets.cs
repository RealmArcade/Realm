using System.Globalization;
using System.Text.Json.Nodes;
using Imazen.WebP;
using Realm.Shared.Metadata;
using SkiaSharp;

namespace Realm.Shared.Textures;

/// <summary>converting and saving one texture per asset type.</summary>
public static partial class TextureConverter
	{
	public static TextureConversionResult ProcessAndSaveTerrainTexture(
		string rawImagePath,
		string outputRtexPath,
		float? forcedScaleFactor = null,
		bool enableRdo = true)
	{
		var (result, sourceImage, originalBits, originalBlake3) = LoadAndDecodeSourceImage(rawImagePath, outputRtexPath);
		if (!result.Success || sourceImage == null) return result;

		try
		{
			using (sourceImage)
			{
				float scaleFactor = forcedScaleFactor ?? CalculateLuminanceScaleFactor(sourceImage);
				result.ScaleFactor = scaleFactor;

				ProcessTerrainPbr(sourceImage, isDecal: false, out var layer0, out var layer1);

				using var l0 = layer0;
				using var l1 = layer1;

				string metadataJson = $"{{\"created_utc\":\"{DateTime.UtcNow:O}\",\"type\":\"terrain_texture\",\"canonical_blake3\":\"{originalBlake3}\",\"scale_factor\":{scaleFactor.ToString("F4", CultureInfo.InvariantCulture)},\"layers\":2}}";
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
		var (result, sourceImage, originalBits, originalBlake3) = LoadAndDecodeSourceImage(rawImagePath, outputRtexPath);
		if (!result.Success || sourceImage == null) return result;

		try
		{
			using (sourceImage)
			{
				float scaleFactor = forcedScaleFactor ?? CalculateLuminanceScaleFactor(sourceImage);
				result.ScaleFactor = scaleFactor;

				int safeCols = Math.Max(1, columns);
				int safeRows = Math.Max(1, rows);

				ProcessTerrainPbr(sourceImage, isDecal: true, out var layer0, out var layer1);

				using var l0 = layer0;
				using var l1 = layer1;

				string metadataJson = $"{{\"created_utc\":\"{DateTime.UtcNow:O}\",\"type\":\"decal\",\"canonical_blake3\":\"{originalBlake3}\",\"scale_factor\":{scaleFactor.ToString("F4", CultureInfo.InvariantCulture)},\"columns\":{safeCols},\"rows\":{safeRows},\"layers\":2}}";
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
		var (result, sourceImage, originalBits, originalBlake3) = LoadAndDecodeSourceImage(rawImagePath, outputRtexPath);
		if (!result.Success || sourceImage == null) return result;

		try
		{
			using (sourceImage)
			{
				string metadataJson = $"{{\"created_utc\":\"{DateTime.UtcNow:O}\",\"type\":\"vfx_spritesheet\",\"canonical_blake3\":\"{originalBlake3}\",\"columns\":{columns},\"rows\":{rows},\"fps\":{fps.ToString("F2", CultureInfo.InvariantCulture)},\"layers\":1}}";

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
}
