using System.Numerics;
using System.Text.Json.Nodes;
using Assimp;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

public static class GlbTextureResolver
{
	public static int FindAlbedoImageIndex(JsonArray textures, JsonArray materials)
	{
		foreach (var mat in materials)
		{
			if (mat is not JsonObject matObj) continue;
			if (matObj["pbrMetallicRoughness"] is not JsonObject pbr) continue;

			int texIdx = GetTextureRefIndex(pbr, "baseColorTexture");
			int imgIdx = ResolveTextureToImage(texIdx, textures);
			if (imgIdx >= 0) return imgIdx;
		}

		return -1;
	}

	public static int GetTextureRefIndex(JsonObject container, string propertyName)
	{
		if (!container.TryGetPropertyValue(propertyName, out var texVal)) return -1;
		if (texVal is not JsonObject texObj) return -1;
		return texObj["index"]?.GetValue<int>() ?? -1;
	}

	private static int GetImageSourceFromExtension(JsonObject texExt, string extensionName)
	{
		if (texExt[extensionName] is JsonObject extObj)
		{
			int src = extObj["source"]?.GetValue<int>() ?? -1;
			if (src >= 0) return src;
		}

		return -1;
	}

	public static int ResolveTextureToImage(int textureIndex, JsonArray textures)
	{
		if (textureIndex < 0 || textureIndex >= textures.Count) return -1;
		if (textures[textureIndex] is not JsonObject texObj) return -1;

		if (texObj["extensions"] is JsonObject texExt)
		{
			int src = GetImageSourceFromExtension(texExt, "EXT_texture_webp");
			if (src >= 0) return src;

			src = GetImageSourceFromExtension(texExt, "KHR_texture_basisu");
			if (src >= 0) return src;
		}

		return texObj["source"]?.GetValue<int>() ?? -1;
	}

	public static int GetImageBufferViewIndex(JsonObject imgObj)
	{
		if (imgObj.TryGetPropertyValue("bufferView", out var bvVal) && bvVal != null)
		{
			return bvVal.GetValue<int>();
		}

		if (imgObj["extensions"] is JsonObject imgExt)
		{
			if (imgExt["EXT_texture_webp"] is JsonObject webp && webp.TryGetPropertyValue("bufferView", out var webpBv) && webpBv != null)
			{
				return webpBv.GetValue<int>();
			}

			if (imgExt["KHR_texture_basisu"] is JsonObject basis && basis.TryGetPropertyValue("bufferView", out var basisBv) && basisBv != null)
			{
				return basisBv.GetValue<int>();
			}
		}

		return -1;
	}

	public static byte[] ExtractImageBytes(int imageIndex, JsonArray images, JsonArray bufferViews, byte[] bin)
	{
		if (imageIndex < 0 || imageIndex >= images.Count) return Array.Empty<byte>();
		if (images[imageIndex] is not JsonObject imgObj) return Array.Empty<byte>();

		int bvIdx = GetImageBufferViewIndex(imgObj);
		if (bvIdx < 0 || bvIdx >= bufferViews.Count) return Array.Empty<byte>();
		if (bufferViews[bvIdx] is not JsonObject bv) return Array.Empty<byte>();

		int byteOffset = GetJsonNodeIntValue(bv, "byteOffset", 0);
		int byteLength = GetJsonNodeIntValue(bv, "byteLength", 0);

		if (byteOffset + byteLength > bin.Length) return Array.Empty<byte>();

		byte[] result = new byte[byteLength];
		Array.Copy(bin, byteOffset, result, 0, byteLength);
		return result;
	}

	public static int GetJsonNodeIntValue(JsonObject obj, string propertyName, int defaultValue)
	{
		if (obj.TryGetPropertyValue(propertyName, out var propVal) && propVal != null)
		{
			return propVal.GetValue<int>();
		}

		return defaultValue;
	}

	public static int FindOrmImageIndex(JsonArray textures, JsonArray materials)
	{
		foreach (var mat in materials)
		{
			if (mat is not JsonObject matObj) continue;

			if (matObj["pbrMetallicRoughness"] is JsonObject pbr)
			{
				int texIdx = GlbTextureResolver.GetTextureRefIndex(pbr, "metallicRoughnessTexture");
				int imgIdx = GlbTextureResolver.ResolveTextureToImage(texIdx, textures);
				if (imgIdx >= 0) return imgIdx;
			}

			{
				int texIdx = GlbTextureResolver.GetTextureRefIndex(matObj, "occlusionTexture");
				int imgIdx = GlbTextureResolver.ResolveTextureToImage(texIdx, textures);
				if (imgIdx >= 0) return imgIdx;
			}
		}

		return -1;
	}
}
