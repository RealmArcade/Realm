using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>appending the albedo buffer view and pointing the textures at webp.</summary>
public static partial class GlbInMemoryColorPreprocessor
	{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void AlignStreamTo4Bytes(MemoryStream stream)
	{
		while ((stream.Position % 4) != 0) stream.WriteByte(0);
	}

	private static void CopyBufferViewData(byte[] binChunk, int origOffset, int origLength, MemoryStream newBinStream)
	{
		if (origOffset + origLength <= binChunk.Length && origLength > 0)
		{
			newBinStream.Write(binChunk, origOffset, origLength);
			AlignStreamTo4Bytes(newBinStream);
		}
	}

	private static int AppendNewAlbedoBufferView(MemoryStream newBinStream, byte[] newAlbedoBytes, JsonArray newBufferViewsList)
	{
		AlignStreamTo4Bytes(newBinStream);
		int albedoOffset = (int)newBinStream.Position;

		newBinStream.Write(newAlbedoBytes, 0, newAlbedoBytes.Length);
		AlignStreamTo4Bytes(newBinStream);

		newBufferViewsList.Add(new JsonObject
		{
			["byteOffset"] = albedoOffset,
			["byteLength"] = newAlbedoBytes.Length,
			["buffer"] = 0
		});
		return newBufferViewsList.Count - 1;
	}

	private static void UpdateAccessorBufferViews(JsonArray accessors, Dictionary<int, int> oldBvToNewBv)
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

	private static void UpdateImageBufferViews(JsonArray images, int albedoImageIndex, Dictionary<int, int> oldBvToNewBv)
	{
		for (int i = 0; i < images.Count; i++)
		{
			if (i == albedoImageIndex) continue;
			if (images[i] is JsonObject imgObj)
			{
				UpdateSingleImageBufferView(imgObj, oldBvToNewBv);
				UpdateImageWebpExtensionBufferView(imgObj, oldBvToNewBv);
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void UpdateSingleImageBufferView(JsonObject imgObj, Dictionary<int, int> oldBvToNewBv)
	{
		if (!imgObj.TryGetPropertyValue("bufferView", out var bvVal) || bvVal == null) return;
		int oldBv = bvVal.GetValue<int>();
		if (oldBvToNewBv.TryGetValue(oldBv, out int newBv))
		{
			imgObj["bufferView"] = newBv;
		}
	}

	private static void UpdateImageWebpExtensionBufferView(JsonObject imgObj, Dictionary<int, int> oldBvToNewBv)
	{
		if (imgObj["extensions"] is JsonObject imgExt && imgExt["EXT_texture_webp"] is JsonObject webp)
		{
			if (webp.TryGetPropertyValue("bufferView", out var wbVal) && wbVal != null)
			{
				int oldBv = wbVal.GetValue<int>();
				if (oldBvToNewBv.TryGetValue(oldBv, out int newBv))
				{
					webp["bufferView"] = newBv;
				}
			}
		}
	}

	private static void SetAlbedoImageProperties(JsonArray images, int albedoImageIndex, int newAlbedoBvIdx)
	{
		if (albedoImageIndex >= 0 && albedoImageIndex < images.Count && images[albedoImageIndex] is JsonObject albedoImgObj)
		{
			albedoImgObj["bufferView"] = newAlbedoBvIdx;
			albedoImgObj["mimeType"] = "image/webp";
			if (albedoImgObj.ContainsKey("uri")) albedoImgObj.Remove("uri");
			if (albedoImgObj.ContainsKey("extensions")) albedoImgObj.Remove("extensions");
		}
	}

	private static void EnsureTexturesUseWebp(JsonArray textures, int imagesCount)
	{
		for (int i = 0; i < textures.Count; i++)
		{
			if (textures[i] is not JsonObject texObj) continue;
			int src = texObj["source"]?.GetValue<int>() ?? -1;
			if (src < 0)
			{
				src = ResolveMissingTextureSource(i, textures, imagesCount);
				texObj["source"] = src;
			}

			if (src >= 0)
			{
				UpdateTextureWebpExtension(texObj, src);
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int ResolveMissingTextureSource(int textureIndex, JsonArray textures, int imagesCount)
	{
		int src = GlbTextureResolver.ResolveTextureToImage(textureIndex, textures);
		if (src >= 0 && src < imagesCount) return src;
		return imagesCount > 0 ? 0 : -1;
	}

	private static void UpdateTextureWebpExtension(JsonObject texObj, int src)
	{
		if (texObj["extensions"] is JsonObject texExt)
		{
			if (texExt.ContainsKey("KHR_texture_basisu")) texExt.Remove("KHR_texture_basisu");
			texExt["EXT_texture_webp"] = new JsonObject { ["source"] = src };
		}
		else
		{
			texObj["extensions"] = new JsonObject
			{
				["EXT_texture_webp"] = new JsonObject { ["source"] = src }
			};
		}
	}

	private static void EnsureExtensionsUsedContainsWebp(JsonObject root)
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
}
