using System.Globalization;
using System.Text.Json.Nodes;
using Imazen.WebP;
using Realm.Shared.Metadata;
using SkiaSharp;

namespace Realm.Shared.Textures;

/// <summary>the single layer texture and its metadata.</summary>
public static partial class TextureConverter
	{
	private static string BuildSingleLayerMetadata(string assetType, string? originalBlake3, string? customMetadataJson)
	{
		string fallbackJson = $"{{\"created_utc\":\"{DateTime.UtcNow:O}\",\"type\":\"{assetType}\",\"canonical_blake3\":\"{originalBlake3}\",\"layers\":1}}";

		if (string.IsNullOrWhiteSpace(customMetadataJson))
		{
			return fallbackJson;
		}

		try
		{
			var metaObj = JsonNode.Parse(customMetadataJson)?.AsObject() ?? new JsonObject();
			metaObj[AssetMetadataKeys.CreatedUtc] = $"{DateTime.UtcNow:O}";
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
		var (result, sourceImage, originalBits, originalBlake3) = LoadAndDecodeSourceImage(rawImagePath, outputRtexPath);
		if (!result.Success || sourceImage == null) return result;

		try
		{
			using (sourceImage)
			{
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
		}
		catch (Exception ex)
		{
			result.Success = false;
			result.ErrorMessage = ex.Message;
			return result;
		}
	}
}
