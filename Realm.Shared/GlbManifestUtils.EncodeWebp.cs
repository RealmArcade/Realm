using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>encoding the glb textures as webp and finding the images involved.</summary>
public static partial class GlbManifestUtils
	{
	public static byte[] EncodeGlbTexturesWebp(byte[] glbBytes, int maxResolution = 1024)
	{
		var (json, bin, glbVer) = ParseGlb(glbBytes);
		if (json is not JsonObject root || bin == null) return glbBytes;

		if (root["images"] is not JsonArray images || images.Count == 0 ||
		    root["bufferViews"] is not JsonArray bufferViews ||
		    root["textures"] is not JsonArray textures)
		{
			return glbBytes;
		}

		var pbrImageIndices = IdentifyPbrImages(root, textures);
		var (imageBvMap, imageBufferViews) = IdentifyImageBufferViews(images, bufferViews);
		var newImageBytes = EncodeImagesToWebp(images, bufferViews, bin, pbrImageIndices, imageBvMap, maxResolution);

		byte[] newBin = RebuildBinaryChunk(bin, images, bufferViews, imageBvMap, imageBufferViews, newImageBytes, root);

		UpdateTextureNodes(textures);
		UpdateExtensionLists(root, "extensionsUsed");
		UpdateExtensionLists(root, "extensionsRequired");

		return BuildGlb(root, newBin, glbVer);
	}

	private static HashSet<int> IdentifyPbrImages(JsonObject root, JsonArray textures)
	{
		var pbrImageIndices = new HashSet<int>();
		if (root["materials"] is not JsonArray materials) return pbrImageIndices;

		foreach (var matNode in materials)
		{
			if (matNode is not JsonObject mat) continue;
			ExtractMaterialTextureIndices(mat, textures, pbrImageIndices);
		}

		return pbrImageIndices;
	}

	private static void ExtractMaterialTextureIndices(JsonObject mat, JsonArray textures, HashSet<int> pbrImageIndices)
	{
		if (mat.TryGetPropertyValue("pbrMetallicRoughness", out var pbrVal) && pbrVal is JsonObject pbr)
		{
			MarkTextureImage("metallicRoughnessTexture", pbr, textures, pbrImageIndices);
		}

		MarkTextureImage("normalTexture", mat, textures, pbrImageIndices);
		MarkTextureImage("occlusionTexture", mat, textures, pbrImageIndices);

		ExtractExtensionMaterialTextures(mat, textures, pbrImageIndices);
	}

	private static void ExtractExtensionMaterialTextures(JsonObject mat, JsonArray textures, HashSet<int> pbrImageIndices)
	{
		if (mat.TryGetPropertyValue("extensions", out var extVal) && extVal is JsonObject matExt)
		{
			if (matExt.TryGetPropertyValue("KHR_materials_clearcoat", out var ccVal) && ccVal is JsonObject cc)
			{
				MarkTextureImage("clearcoatRoughnessTexture", cc, textures, pbrImageIndices);
				MarkTextureImage("clearcoatNormalTexture", cc, textures, pbrImageIndices);
			}

			if (matExt.TryGetPropertyValue("KHR_materials_sheen", out var sheenVal) && sheenVal is JsonObject sheen)
			{
				MarkTextureImage("sheenRoughnessTexture", sheen, textures, pbrImageIndices);
			}

			if (matExt.TryGetPropertyValue("KHR_materials_specular", out var specVal) && specVal is JsonObject spec)
			{
				MarkTextureImage("specularTexture", spec, textures, pbrImageIndices);
			}
		}
	}

	private static void MarkTextureImage(string propName, JsonObject container, JsonArray textures, HashSet<int> pbrImageIndices)
	{
		if (!TryGetTextureObject(propName, container, out var texObj)) return;
		if (!TryGetValidTexture(texObj, textures, out var tex)) return;

		ExtractImageSource(tex, pbrImageIndices);
		MarkExtensionTextureImage(tex, pbrImageIndices);
	}

	private static bool TryGetTextureObject(string propName, JsonObject container, [MaybeNullWhen(false)] out JsonObject texObj)
	{
		texObj = null;
		if (!container.TryGetPropertyValue(propName, out var texVal)) return false;
		if (texVal is not JsonObject obj) return false;

		texObj = obj;
		return true;
	}

	private static bool TryGetValidTexture(JsonObject texObj, JsonArray textures, [MaybeNullWhen(false)] out JsonObject tex)
	{
		tex = null;
		if (!texObj.TryGetPropertyValue("index", out var indexNode)) return false;
		if (indexNode == null) return false;

		int texIdx = indexNode.GetValue<int>();
		if (texIdx < 0) return false;
		if (texIdx >= textures.Count) return false;

		var texNode = textures[texIdx];
		if (texNode is not JsonObject t) return false;

		tex = t;
		return true;
	}

	private static void ExtractImageSource(JsonObject tex, HashSet<int> pbrImageIndices)
	{
		if (!tex.TryGetPropertyValue("source", out var sourceNode)) return;
		if (sourceNode == null) return;

		int src = sourceNode.GetValue<int>();
		if (src >= 0)
		{
			pbrImageIndices.Add(src);
		}
	}

	private static void MarkExtensionTextureImage(JsonObject tex, HashSet<int> pbrImageIndices)
	{
		if (tex.TryGetPropertyValue("extensions", out var extNode) && extNode is JsonObject texExt)
		{
			MarkSpecificExtensionImage(texExt, "KHR_texture_basisu", pbrImageIndices);
			MarkSpecificExtensionImage(texExt, "EXT_texture_webp", pbrImageIndices);
		}
	}

	private static void MarkSpecificExtensionImage(JsonObject texExt, string extensionName, HashSet<int> pbrImageIndices)
	{
		if (texExt.TryGetPropertyValue(extensionName, out var extVal) && extVal is JsonObject extObj)
		{
			int extSrc = extObj["source"]?.GetValue<int>() ?? -1;
			if (extSrc >= 0) pbrImageIndices.Add(extSrc);
		}
	}
}
