using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>sanitising material texture sources and base colours.</summary>
public static partial class GlbManifestUtils
	{
	public static byte[] SanitizeMaterials(byte[] glbBytes)
	{
		var (json, bin, glbVer) = ParseGlb(glbBytes);
		if (json is not JsonObject root) return glbBytes;

		SanitizeTextureSources(root);
		SanitizeBaseColorFactors(root);

		return BuildGlb(root, bin, glbVer);
	}

	private static void SanitizeTextureSources(JsonObject root)
	{
		if (!root.TryGetPropertyValue("textures", out var texNode) || texNode is not JsonArray texArray)
			return;

		int imageCount = (root.TryGetPropertyValue("images", out var imgNode) && imgNode is JsonArray imgArray) ? imgArray.Count : 0;
		foreach (var t in texArray)
		{
			if (t is JsonObject texObj)
			{
				SanitizeSingleTextureSource(texObj, imageCount);
			}
		}
	}

	private static void SanitizeSingleTextureSource(JsonObject texObj, int imageCount)
	{
		if (texObj.ContainsKey("source") && texObj["source"] != null) return;

		int src = -1;
		if (texObj.TryGetPropertyValue("extensions", out var extVal) && extVal is JsonObject extObj)
		{
			src = GetTextureSourceFromExtensions(extObj);
		}

		if (src >= 0 && src < imageCount)
		{
			texObj["source"] = src;
		}
		else if (imageCount > 0)
		{
			texObj["source"] = 0;
		}
	}

	private static int GetTextureSourceFromExtensions(JsonObject extObj)
	{
		int src = -1;
		if (extObj.TryGetPropertyValue("EXT_texture_webp", out var webpVal) && webpVal is JsonObject webpObj)
		{
			src = webpObj["source"]?.GetValue<int>() ?? -1;
		}

		if (src < 0 && extObj.TryGetPropertyValue("KHR_texture_basisu", out var basVal) && basVal is JsonObject basObj)
		{
			src = basObj["source"]?.GetValue<int>() ?? -1;
		}

		return src;
	}

	private static void SanitizeBaseColorFactors(JsonObject root)
	{
		if (!root.TryGetPropertyValue("materials", out var matNode) || matNode is not JsonArray matArray)
			return;

		foreach (var m in matArray)
		{
			if (m is JsonObject matObj && matObj.TryGetPropertyValue("pbrMetallicRoughness", out var pbrVal) && pbrVal is JsonObject pbrObj)
			{
				if (pbrObj.ContainsKey("baseColorTexture") && !pbrObj.ContainsKey("baseColorFactor"))
				{
					pbrObj["baseColorFactor"] = new JsonArray(1.0, 1.0, 1.0, 1.0);
				}
			}
		}
	}
}
