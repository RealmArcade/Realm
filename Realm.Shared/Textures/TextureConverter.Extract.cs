using System.Globalization;
using System.Text.Json.Nodes;
using Imazen.WebP;
using Realm.Shared.Metadata;
using SkiaSharp;

namespace Realm.Shared.Textures;

/// <summary>pulling images and webp back out of an rtex, and the metadata parsing it needs.</summary>
public static partial class TextureConverter
	{
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

			string? metaType = node[AssetMetadataKeys.AssetType]?.GetValue<string>()
			                   ?? node["type"]?.GetValue<string>();

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
}
