using System.Numerics;
using System.Text.Json.Nodes;
using Assimp;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

public static class GlbOrmRewriter
{
	internal static void RebuildBufferViews(
		JsonArray bufferViews, byte[] binChunk, HashSet<int> retainedBvIndices,
		MemoryStream newBinStream, JsonArray newBufferViewsList, Dictionary<int, int> oldBvToNewBv)
	{
		for (int oldBvIdx = 0; oldBvIdx < bufferViews.Count; oldBvIdx++)
		{
			if (!retainedBvIndices.Contains(oldBvIdx)) continue;
			if (bufferViews[oldBvIdx] is not JsonObject oldBv) continue;

			int origOffset = GlbTextureResolver.GetJsonNodeIntValue(oldBv, "byteOffset", 0);
			int origLength = GlbTextureResolver.GetJsonNodeIntValue(oldBv, "byteLength", 0);

			while ((newBinStream.Position % 4) != 0) newBinStream.WriteByte(0);
			int newOffset = (int)newBinStream.Position;

			if (origOffset + origLength <= binChunk.Length && origLength > 0)
			{
				newBinStream.Write(binChunk, origOffset, origLength);
				while ((newBinStream.Position % 4) != 0) newBinStream.WriteByte(0);
			}

			var clonedBv = (JsonObject)oldBv.DeepClone();
			clonedBv["byteOffset"] = newOffset;
			clonedBv["buffer"] = 0;

			newBufferViewsList.Add(clonedBv);
			oldBvToNewBv[oldBvIdx] = newBufferViewsList.Count - 1;
		}
	}

	internal static void UpdateAccessors(JsonArray accessors, Dictionary<int, int> oldBvToNewBv)
	{
		foreach (var acc in accessors)
		{
			if (acc is JsonObject accObj && accObj.TryGetPropertyValue("bufferView", out var bvVal) && bvVal != null)
			{
				int oldBv = bvVal.GetValue<int>();
				if (oldBvToNewBv.TryGetValue(oldBv, out int newBv))
				{
					accObj["bufferView"] = newBv;
				}
			}
		}
	}

	internal static void UpdateImageBufferViews(JsonArray images, Dictionary<int, int> oldBvToNewBv, int ormImageIndex)
	{
		for (int i = 0; i < images.Count; i++)
		{
			if (i == ormImageIndex) continue;
			if (images[i] is not JsonObject imgObj) continue;

			if (imgObj.TryGetPropertyValue("bufferView", out var bvVal) && bvVal != null)
			{
				int oldBv = bvVal.GetValue<int>();
				if (oldBvToNewBv.TryGetValue(oldBv, out int newBv)) imgObj["bufferView"] = newBv;
			}

			UpdateWebpExtensionBufferView(imgObj, oldBvToNewBv);
		}
	}

	internal static void CreateMissingOrmTexture(JsonArray images, JsonArray textures, JsonArray materials, int newOrmBvIdx)
	{
		int newOrmImageIdx = images.Count;
		images.Add(new JsonObject { ["mimeType"] = "image/webp", ["bufferView"] = newOrmBvIdx });

		int newOrmTextureIdx = textures.Count;
		textures.Add(new JsonObject
		{
			["source"] = newOrmImageIdx,
			["extensions"] = new JsonObject { ["EXT_texture_webp"] = new JsonObject { ["source"] = newOrmImageIdx } }
		});

		if (materials.Count > 0 && materials[0] is JsonObject firstMat)
		{
			if (firstMat["pbrMetallicRoughness"] is not JsonObject pbr)
			{
				pbr = new JsonObject();
				firstMat["pbrMetallicRoughness"] = pbr;
			}

			if (!pbr.ContainsKey("metallicRoughnessTexture"))
			{
				pbr["metallicRoughnessTexture"] = new JsonObject { ["index"] = newOrmTextureIdx };
			}
		}
	}

	internal static void UpdateImagesAndMaterials(
		JsonArray images, JsonArray textures, JsonArray materials, Dictionary<int, int> oldBvToNewBv,
		int ormImageIndex, int newOrmBvIdx)
	{
		UpdateImageBufferViews(images, oldBvToNewBv, ormImageIndex);

		if (ormImageIndex >= 0 && ormImageIndex < images.Count && images[ormImageIndex] is JsonObject ormImgObj)
		{
			ormImgObj["bufferView"] = newOrmBvIdx;
			ormImgObj["mimeType"] = "image/webp";
			if (ormImgObj.ContainsKey("uri")) ormImgObj.Remove("uri");
			if (ormImgObj.ContainsKey("extensions")) ormImgObj.Remove("extensions");
		}
		else
		{
			CreateMissingOrmTexture(images, textures, materials, newOrmBvIdx);
		}
	}

	internal static int EnsureTextureSource(JsonObject texObj, int textureIndex, JsonArray textures, JsonArray images)
	{
		int src = texObj["source"]?.GetValue<int>() ?? -1;
		if (src < 0)
		{
			src = GlbTextureResolver.ResolveTextureToImage(textureIndex, textures);
			if (src >= 0 && src < images.Count)
			{
				texObj["source"] = src;
			}
			else if (images.Count > 0)
			{
				texObj["source"] = 0;
				src = 0;
			}
		}

		return src;
	}

	internal static void UpdateTextureExtensions(JsonObject texObj, int src)
	{
		if (src < 0) return;

		if (texObj["extensions"] is JsonObject texExt)
		{
			if (texExt.ContainsKey("KHR_texture_basisu")) texExt.Remove("KHR_texture_basisu");
			texExt["EXT_texture_webp"] = new JsonObject { ["source"] = src };
		}
		else
		{
			texObj["extensions"] = new JsonObject { ["EXT_texture_webp"] = new JsonObject { ["source"] = src } };
		}
	}

	internal static void UpdateTextures(JsonArray textures, JsonArray images)
	{
		for (int i = 0; i < textures.Count; i++)
		{
			if (textures[i] is not JsonObject texObj) continue;

			int src = EnsureTextureSource(texObj, i, textures, images);
			UpdateTextureExtensions(texObj, src);
		}
	}

	internal static HashSet<int> GetRetainedBufferViews(JsonArray accessors, JsonArray bufferViews, JsonArray images, int excludeImageIndex)
	{
		var retainedBvIndices = new HashSet<int>();

		AddAccessorBufferViews(accessors, bufferViews, retainedBvIndices);
		AddImageBufferViews(images, bufferViews, retainedBvIndices, excludeImageIndex);

		return retainedBvIndices;
	}

	internal static void EnsureWebPExtension(JsonObject root)
	{
		if (root.TryGetPropertyValue("extensionsUsed", out var extNode) && extNode is JsonArray extArray)
		{
			bool exists = false;
			foreach (var item in extArray)
			{
				if (item?.GetValue<string>() == "EXT_texture_webp")
				{
					exists = true;
					break;
				}
			}

			if (!exists) extArray.Add("EXT_texture_webp");
		}
		else
		{
			root["extensionsUsed"] = new JsonArray("EXT_texture_webp");
		}
	}

	internal static byte[] RebuildGlbWithUpdatedOrmTexture(
		JsonObject root,
		byte[] binChunk,
		int ormImageIndex,
		byte[] newOrmBytes,
		uint glbVersion)
	{
		var accessors = GetJsonArrayOrDefault(root, "accessors");
		var bufferViews = GetJsonArrayOrDefault(root, "bufferViews");
		var images = GetJsonArrayOrDefault(root, "images");
		var textures = GetJsonArrayOrDefault(root, "textures");
		var materials = GetJsonArrayOrDefault(root, "materials");

		var retainedBvIndices = GetRetainedBufferViews(accessors, bufferViews, images, ormImageIndex);

		using var newBinStream = new MemoryStream();
		var oldBvToNewBv = new Dictionary<int, int>();
		var newBufferViewsList = new JsonArray();

		RebuildBufferViews(bufferViews, binChunk, retainedBvIndices, newBinStream, newBufferViewsList, oldBvToNewBv);

		while ((newBinStream.Position % 4) != 0) newBinStream.WriteByte(0);
		int ormOffset = (int)newBinStream.Position;
		newBinStream.Write(newOrmBytes, 0, newOrmBytes.Length);
		while ((newBinStream.Position % 4) != 0) newBinStream.WriteByte(0);

		newBufferViewsList.Add(new JsonObject
		{
			["byteOffset"] = ormOffset,
			["byteLength"] = newOrmBytes.Length,
			["buffer"] = 0
		});
		int newOrmBvIdx = newBufferViewsList.Count - 1;

		UpdateAccessors(accessors, oldBvToNewBv);
		UpdateImagesAndMaterials(images, textures, materials, oldBvToNewBv, ormImageIndex, newOrmBvIdx);
		UpdateTextures(textures, images);

		EnsureWebPExtension(root);

		root["bufferViews"] = newBufferViewsList;

		if (root["buffers"] is JsonArray buffers && buffers.Count > 0 && buffers[0] is JsonObject buf0)
		{
			buf0["byteLength"] = (int)newBinStream.Position;
		}

		return GlbManifestUtils.BuildGlb(root, newBinStream.ToArray(), glbVersion);
	}

	internal static void UpdateWebpExtensionBufferView(JsonObject imgObj, Dictionary<int, int> oldBvToNewBv)
	{
		if (imgObj.TryGetPropertyValue("extensions", out var extNode) && extNode is JsonObject imgExt &&
		    imgExt.TryGetPropertyValue("EXT_texture_webp", out var webpNode) && webpNode is JsonObject webp &&
		    webp.TryGetPropertyValue("bufferView", out var wbVal) && wbVal != null)
		{
			int oldBv = wbVal.GetValue<int>();
			if (oldBvToNewBv.TryGetValue(oldBv, out int newBv))
			{
				webp["bufferView"] = newBv;
			}
		}
	}

	internal static void AddAccessorBufferViews(JsonArray accessors, JsonArray bufferViews, HashSet<int> retainedBvIndices)
	{
		foreach (var acc in accessors)
		{
			if (acc is JsonObject accObj && accObj.TryGetPropertyValue("bufferView", out var bvVal) && bvVal != null)
			{
				int bvIdx = bvVal.GetValue<int>();
				if (bvIdx >= 0 && bvIdx < bufferViews.Count) retainedBvIndices.Add(bvIdx);
			}
		}
	}

	internal static void AddImageBufferViews(JsonArray images, JsonArray bufferViews, HashSet<int> retainedBvIndices, int excludeImageIndex)
	{
		for (int i = 0; i < images.Count; i++)
		{
			if (i == excludeImageIndex) continue;
			if (images[i] is JsonObject imgObj)
			{
				int imgBv = GlbTextureResolver.GetImageBufferViewIndex(imgObj);
				if (imgBv >= 0 && imgBv < bufferViews.Count) retainedBvIndices.Add(imgBv);
			}
		}
	}

	internal static JsonArray GetJsonArrayOrDefault(JsonObject root, string propertyName)
	{
		if (root.TryGetPropertyValue(propertyName, out var arrayNode) && arrayNode is JsonArray array)
		{
			return array;
		}

		return new JsonArray();
	}
}
