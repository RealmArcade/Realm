using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

public static class GlbManifestUtils
{
	private const uint GlbMagic = 0x46546C67; // 'glTF'
	private const uint JsonChunkType = 0x4E4F534A; // 'JSON'
	private const uint BinChunkType = 0x004E4942; // 'BIN\0'

	public static bool IsValidGlb(byte[] glbBytes)
	{
		if (glbBytes == null || glbBytes.Length < 20) return false;
		uint magic = BitConverter.ToUInt32(glbBytes, 0);
		return magic == GlbMagic;
	}

	public static (JsonNode? Json, byte[]? BinChunk, uint Version) ParseGlb(byte[] glbBytes)
	{
		if (!IsValidGlb(glbBytes)) return (null, null, 0);

		uint version = BitConverter.ToUInt32(glbBytes, 4);
		int currentOffset = 12;

		if (currentOffset + 8 > glbBytes.Length) return (null, null, version);

		uint jsonLength = BitConverter.ToUInt32(glbBytes, currentOffset);
		uint jsonType = BitConverter.ToUInt32(glbBytes, currentOffset + 4);
		if (jsonType != JsonChunkType || currentOffset + 8 + jsonLength > glbBytes.Length) return (null, null, version);

		string jsonString = Encoding.UTF8.GetString(glbBytes, currentOffset + 8, (int)jsonLength);
		var jsonNode = JsonNode.Parse(jsonString);

		int binOffset = currentOffset + 8 + (int)jsonLength;
		byte[]? binChunk = null;
		if (binOffset + 8 <= glbBytes.Length)
		{
			uint binLength = BitConverter.ToUInt32(glbBytes, binOffset);
			uint binType = BitConverter.ToUInt32(glbBytes, binOffset + 4);
			if (binType == BinChunkType && binOffset + 8 + binLength <= glbBytes.Length)
			{
				binChunk = new byte[binLength];
				Array.Copy(glbBytes, binOffset + 8, binChunk, 0, binLength);
			}
			else if (binOffset + 8 < glbBytes.Length)
			{
				int remaining = glbBytes.Length - (binOffset + 8);
				binChunk = new byte[remaining];
				Array.Copy(glbBytes, binOffset + 8, binChunk, 0, remaining);
			}
		}

		return (jsonNode, binChunk, version);
	}

	public static byte[] BuildGlb(JsonNode jsonNode, byte[]? binChunk, uint version = 2)
	{
		byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonNode.ToJsonString());
		int paddedJsonLength = (jsonBytes.Length + 3) & ~3;
		byte[] paddedJson = new byte[paddedJsonLength];
		Array.Copy(jsonBytes, paddedJson, jsonBytes.Length);
		for (int i = jsonBytes.Length; i < paddedJsonLength; i++)
		{
			paddedJson[i] = 0x20; // GLTF JSON padding with ASCII space
		}

		int paddedBinLength = binChunk != null ? ((binChunk.Length + 3) & ~3) : 0;
		byte[]? paddedBin = null;
		if (binChunk != null && binChunk.Length > 0)
		{
			paddedBin = new byte[paddedBinLength];
			Array.Copy(binChunk, paddedBin, binChunk.Length);
		}

		using var ms = new MemoryStream();
		using var writer = new BinaryWriter(ms);

		writer.Write(GlbMagic);
		writer.Write(version);

		uint totalLength = 12 + 8 + (uint)paddedJsonLength;
		if (paddedBin != null)
		{
			totalLength += 8 + (uint)paddedBinLength;
		}
		writer.Write(totalLength);

		// JSON chunk
		writer.Write((uint)paddedJsonLength);
		writer.Write(JsonChunkType);
		writer.Write(paddedJson);

		// BIN chunk
		if (paddedBin != null)
		{
			writer.Write((uint)paddedBinLength);
			writer.Write(BinChunkType);
			writer.Write(paddedBin);
		}

		return ms.ToArray();
	}

	private static bool TryGetTrue(JsonObject? container, string propertyName)
	{
		if (container == null || !container.TryGetPropertyValue(propertyName, out var node) || node == null)
			return false;
		try
		{
			if (node.GetValueKind() == System.Text.Json.JsonValueKind.True) return true;
			if (node is JsonValue jv)
			{
				if (jv.TryGetValue<bool>(out var b)) return b;
				if (jv.TryGetValue<int>(out var num)) return num != 0;
				if (jv.TryGetValue<string>(out var str) && bool.TryParse(str, out var parsedB)) return parsedB;
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	public static bool HasOptimizationFlag(byte[] glbBytes)
	{
		var (json, _, _) = ParseGlb(glbBytes);
		if (json is not JsonObject root) return false;

		if (root.TryGetPropertyValue("extras", out var extrasNode) && extrasNode is JsonObject extras)
		{
			if (TryGetTrue(extras, "realm_optimize_completed")) return true;
			if (TryGetTrue(extras, "realm_decimate_completed")) return true;
		}

		if (root.TryGetPropertyValue("asset", out var assetNode) && assetNode is JsonObject asset &&
			asset.TryGetPropertyValue("extras", out var assetExtrasNode) && assetExtrasNode is JsonObject assetExtras)
		{
			if (TryGetTrue(assetExtras, "realm_optimize_completed")) return true;
			if (TryGetTrue(assetExtras, "realm_decimate_completed")) return true;
		}

		if (root.TryGetPropertyValue("extensionsUsed", out var extUsedNode) && extUsedNode is JsonArray extUsed)
		{
			foreach (var ext in extUsed)
			{
				if (ext?.GetValue<string>() == "MSFT_lod") return true;
			}
		}

		if (root.TryGetPropertyValue("extensionsRequired", out var extReqNode) && extReqNode is JsonArray extReq)
		{
			foreach (var ext in extReq)
			{
				if (ext?.GetValue<string>() == "MSFT_lod") return true;
			}
		}

		return false;
	}

	public static byte[] InjectOptimizationMetadata(
		byte[] glbBytes,
		Dictionary<string, object>? extraStats = null)
	{
		var (json, bin, glbVer) = ParseGlb(glbBytes);
		if (json is not JsonObject root) return glbBytes;

		JsonObject extras;
		if (root.TryGetPropertyValue("extras", out var extrasNode) && extrasNode is JsonObject existingExtras)
		{
			extras = existingExtras;
		}
		else
		{
			extras = new JsonObject();
			root["extras"] = extras;
		}

		extras["realm_optimize_completed"] = true;
		extras["realm_version"] = RealmVersion.GameBinaryVersion;
		extras["optimization_timestamp"] = DateTime.UtcNow.ToString("o");

		if (!root.ContainsKey("asset") || root["asset"] == null)
		{
			root["asset"] = new JsonObject();
		}
		if (root["asset"] is JsonObject assetObj)
		{
			if (!assetObj.ContainsKey("extras") || assetObj["extras"] == null)
			{
				assetObj["extras"] = new JsonObject();
			}
			if (assetObj["extras"] is JsonObject assetExtras)
			{
				assetExtras["realm_optimize_completed"] = true;
			}
		}

		if (extraStats != null)
		{
			foreach (var kvp in extraStats)
			{
				if (kvp.Value is int i) extras[kvp.Key] = i;
				else if (kvp.Value is float f) extras[kvp.Key] = f;
				else if (kvp.Value is bool b) extras[kvp.Key] = b;
				else if (kvp.Value is string s) extras[kvp.Key] = s;
			}
		}

		return BuildGlb(root, bin, glbVer);
	}

	public static (byte[] UnoptimizedBytes, bool WasModified) StripOptimizationMetadata(byte[] glbBytes)
	{
		var (json, bin, glbVer) = ParseGlb(glbBytes);
		if (json is not JsonObject root) return (glbBytes, false);

		bool modified = false;

		if (root.TryGetPropertyValue("extras", out var extrasNode) && extrasNode is JsonObject extras)
		{
			if (extras.Remove("realm_optimize_completed")) modified = true;
			if (extras.Remove("realm_version")) modified = true;
			if (extras.Remove("optimization_timestamp")) modified = true;
			if (extras.Remove("msft_lod_embedded")) modified = true;
		}

		if (root.TryGetPropertyValue("asset", out var assetNode) && assetNode is JsonObject asset &&
			asset.TryGetPropertyValue("extras", out var assetExtrasNode) && assetExtrasNode is JsonObject assetExtras)
		{
			if (assetExtras.Remove("realm_optimize_completed")) modified = true;
		}

		// Remove MSFT_lod extension from extensionsUsed and extensionsRequired if present
		if (root.TryGetPropertyValue("extensionsUsed", out var extUsedNode) && extUsedNode is JsonArray extUsed)
		{
			for (int i = extUsed.Count - 1; i >= 0; i--)
			{
				if (extUsed[i]?.GetValue<string>() == "MSFT_lod")
				{
					extUsed.RemoveAt(i);
					modified = true;
				}
			}
		}

		if (root.TryGetPropertyValue("extensionsRequired", out var extReqNode) && extReqNode is JsonArray extReq)
		{
			for (int i = extReq.Count - 1; i >= 0; i--)
			{
				if (extReq[i]?.GetValue<string>() == "MSFT_lod")
				{
					extReq.RemoveAt(i);
					modified = true;
				}
			}
		}

		// Remove LOD nodes (e.g. *_LOD1, *_LOD2, *_LOD3)
		if (root.TryGetPropertyValue("nodes", out var nodesNode) && nodesNode is JsonArray nodesArray)
		{
			var lodIndices = new HashSet<int>();
			for (int i = 0; i < nodesArray.Count; i++)
			{
				if (nodesArray[i] is JsonObject nodeObj && nodeObj.TryGetPropertyValue("name", out var nameVal))
				{
					string nameStr = nameVal?.GetValue<string>() ?? "";
					if (nameStr.Contains("_LOD1") || nameStr.Contains("_LOD2") || nameStr.Contains("_LOD3"))
					{
						lodIndices.Add(i);
					}
				}
			}

			if (lodIndices.Count > 0)
			{
				// Remove LOD children references from remaining nodes
				foreach (var n in nodesArray)
				{
					if (n is JsonObject parentObj && parentObj.TryGetPropertyValue("children", out var childrenVal) && childrenVal is JsonArray childrenArr)
					{
						for (int c = childrenArr.Count - 1; c >= 0; c--)
						{
							int childIdx = childrenArr[c]?.GetValue<int>() ?? -1;
							if (lodIndices.Contains(childIdx))
							{
								childrenArr.RemoveAt(c);
								modified = true;
							}
						}
					}
				}
			}
		}

		if (!modified) return (glbBytes, false);

		return (BuildGlb(root, bin, glbVer), true);
	}

	public static byte[] SanitizeMaterials(byte[] glbBytes)
	{
		var (json, bin, glbVer) = ParseGlb(glbBytes);
		if (json is not JsonObject root) return glbBytes;

		// 1. Ensure every texture in textures array has a valid source property
		if (root.TryGetPropertyValue("textures", out var texNode) && texNode is JsonArray texArray)
		{
			int imageCount = (root.TryGetPropertyValue("images", out var imgNode) && imgNode is JsonArray imgArray) ? imgArray.Count : 0;
			foreach (var t in texArray)
			{
				if (t is JsonObject texObj)
				{
					if (!texObj.ContainsKey("source") || texObj["source"] == null)
					{
						int src = -1;
						if (texObj.TryGetPropertyValue("extensions", out var extVal) && extVal is JsonObject extObj)
						{
							if (extObj.TryGetPropertyValue("EXT_texture_webp", out var webpVal) && webpVal is JsonObject webpObj)
							{
								src = webpObj["source"]?.GetValue<int>() ?? -1;
							}
							if (src < 0 && extObj.TryGetPropertyValue("KHR_texture_basisu", out var basVal) && basVal is JsonObject basObj)
							{
								src = basObj["source"]?.GetValue<int>() ?? -1;
							}
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
				}
			}
		}

		// 2. Ensure baseColorFactor exists when baseColorTexture is present
		if (root.TryGetPropertyValue("materials", out var matNode) && matNode is JsonArray matArray)
		{
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

		return BuildGlb(root, bin, glbVer);
	}

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

		// 1. Identify which image index corresponds to PBR/Normal vs Albedo/Color
		var pbrImageIndices = new HashSet<int>();
		if (root["materials"] is JsonArray materials)
		{
			foreach (var matNode in materials)
			{
				if (matNode is not JsonObject mat) continue;

				void MarkTextureImage(string propName, JsonObject container)
				{
					if (container.TryGetPropertyValue(propName, out var texVal) && texVal is JsonObject texObj)
					{
						int texIdx = texObj["index"]?.GetValue<int>() ?? -1;
						if (texIdx >= 0 && texIdx < textures.Count && textures[texIdx] is JsonObject tex)
						{
							int src = tex["source"]?.GetValue<int>() ?? -1;
							if (src >= 0) pbrImageIndices.Add(src);

							if (tex.TryGetPropertyValue("extensions", out var extNode) && extNode is JsonObject texExt)
							{
								if (texExt.TryGetPropertyValue("KHR_texture_basisu", out var basVal) && basVal is JsonObject basObj)
								{
									int basSrc = basObj["source"]?.GetValue<int>() ?? -1;
									if (basSrc >= 0) pbrImageIndices.Add(basSrc);
								}
								if (texExt.TryGetPropertyValue("EXT_texture_webp", out var webpVal) && webpVal is JsonObject webpObj)
								{
									int webpSrc = webpObj["source"]?.GetValue<int>() ?? -1;
									if (webpSrc >= 0) pbrImageIndices.Add(webpSrc);
								}
							}
						}
					}
				}

				if (mat.TryGetPropertyValue("pbrMetallicRoughness", out var pbrVal) && pbrVal is JsonObject pbr)
				{
					MarkTextureImage("metallicRoughnessTexture", pbr);
				}
				MarkTextureImage("normalTexture", mat);
				MarkTextureImage("occlusionTexture", mat);

				if (mat.TryGetPropertyValue("extensions", out var extVal) && extVal is JsonObject matExt)
				{
					if (matExt.TryGetPropertyValue("KHR_materials_clearcoat", out var ccVal) && ccVal is JsonObject cc)
					{
						MarkTextureImage("clearcoatRoughnessTexture", cc);
						MarkTextureImage("clearcoatNormalTexture", cc);
					}
					if (matExt.TryGetPropertyValue("KHR_materials_sheen", out var sheenVal) && sheenVal is JsonObject sheen)
					{
						MarkTextureImage("sheenRoughnessTexture", sheen);
					}
					if (matExt.TryGetPropertyValue("KHR_materials_specular", out var specVal) && specVal is JsonObject spec)
					{
						MarkTextureImage("specularTexture", spec);
					}
				}
			}
		}

		// 2. Identify buffer views used by images vs geometry/accessors
		var imageBvMap = new Dictionary<int, int>();
		var imageBufferViews = new HashSet<int>();
		for (int i = 0; i < images.Count; i++)
		{
			if (images[i] is JsonObject imgObj && imgObj.TryGetPropertyValue("bufferView", out var bvVal))
			{
				int bvIdx = bvVal?.GetValue<int>() ?? -1;
				if (bvIdx >= 0 && bvIdx < bufferViews.Count)
				{
					imageBvMap[i] = bvIdx;
					imageBufferViews.Add(bvIdx);
				}
			}
		}

		// 3. Process and re-encode images
		var newImageBytes = new Dictionary<int, byte[]>();
		for (int i = 0; i < images.Count; i++)
		{
			if (!imageBvMap.TryGetValue(i, out int bvIdx)) continue;
			if (bufferViews[bvIdx] is not JsonObject bv) continue;

			int byteOffset = bv["byteOffset"]?.GetValue<int>() ?? 0;
			int byteLength = bv["byteLength"]?.GetValue<int>() ?? 0;
			if (byteOffset + byteLength > bin.Length) continue;

			byte[] raw = new byte[byteLength];
			Array.Copy(bin, byteOffset, raw, 0, byteLength);

			try
			{
				using var img = SKBitmap.Decode(raw);
				if (img == null)
				{
					newImageBytes[i] = raw;
					continue;
				}

				SKBitmap workingImg = img;
				bool ownsResized = false;
				if (img.Width > maxResolution || img.Height > maxResolution)
				{
					float scale = Math.Min((float)maxResolution / img.Width, (float)maxResolution / img.Height);
					int targetW = Math.Max(1, (int)(img.Width * scale));
					int targetH = Math.Max(1, (int)(img.Height * scale));
					var resized = img.Resize(new SKImageInfo(targetW, targetH), new SKSamplingOptions(SKCubicResampler.Mitchell));
					if (resized != null)
					{
						workingImg = resized;
						ownsResized = true;
					}
				}

				try
				{
					bool isPbr = pbrImageIndices.Contains(i);
					byte[] webpData = TextureConverter.EncodeWebp(
						workingImg,
						lossless: isPbr,
						quality: isPbr ? 100 : 80
					);

					newImageBytes[i] = webpData;
				}
				finally
				{
					if (ownsResized)
					{
						workingImg.Dispose();
					}
				}
			}
			catch
			{
				newImageBytes[i] = raw;
			}
		}

		// 4. Rebuild binary chunk and update bufferViews
		using var newBinStream = new MemoryStream();
		var newBvOffsets = new int[bufferViews.Count];
		var newBvLengths = new int[bufferViews.Count];

		// First pass: non-image buffer views
		for (int bvIdx = 0; bvIdx < bufferViews.Count; bvIdx++)
		{
			if (imageBufferViews.Contains(bvIdx)) continue;
			if (bufferViews[bvIdx] is not JsonObject bv) continue;

			int origOffset = bv["byteOffset"]?.GetValue<int>() ?? 0;
			int origLength = bv["byteLength"]?.GetValue<int>() ?? 0;

			int newOffset = (int)newBinStream.Position;
			newBvOffsets[bvIdx] = newOffset;
			newBvLengths[bvIdx] = origLength;

			if (origOffset + origLength <= bin.Length)
			{
				newBinStream.Write(bin, origOffset, origLength);
				int pad = (4 - (origLength % 4)) % 4;
				for (int p = 0; p < pad; p++) newBinStream.WriteByte(0);
			}
		}

		// Second pass: image buffer views
		for (int i = 0; i < images.Count; i++)
		{
			if (!imageBvMap.TryGetValue(i, out int bvIdx)) continue;
			if (!newImageBytes.TryGetValue(i, out byte[]? imgData)) continue;

			int newOffset = (int)newBinStream.Position;
			newBvOffsets[bvIdx] = newOffset;
			newBvLengths[bvIdx] = imgData.Length;

			newBinStream.Write(imgData, 0, imgData.Length);
			int pad = (4 - (imgData.Length % 4)) % 4;
			for (int p = 0; p < pad; p++) newBinStream.WriteByte(0);

			if (images[i] is JsonObject imgObj)
			{
				imgObj["mimeType"] = "image/webp";
			}
		}

		// Update bufferViews JSON
		for (int bvIdx = 0; bvIdx < bufferViews.Count; bvIdx++)
		{
			if (bufferViews[bvIdx] is JsonObject bv)
			{
				bv["byteOffset"] = newBvOffsets[bvIdx];
				bv["byteLength"] = newBvLengths[bvIdx];
			}
		}

		// Update buffers[0].byteLength
		if (root["buffers"] is JsonArray buffers && buffers.Count > 0 && buffers[0] is JsonObject buf0)
		{
			buf0["byteLength"] = (int)newBinStream.Position;
		}

		// Update textures: set extensions.EXT_texture_webp.source = tex.source, remove KHR_texture_basisu
		for (int t = 0; t < textures.Count; t++)
		{
			if (textures[t] is not JsonObject tex) continue;
			int src = tex["source"]?.GetValue<int>() ?? -1;

			if (tex.TryGetPropertyValue("extensions", out var extNode) && extNode is JsonObject texExt)
			{
				if (texExt.TryGetPropertyValue("KHR_texture_basisu", out var basVal) && basVal is JsonObject basObj)
				{
					if (src < 0) src = basObj["source"]?.GetValue<int>() ?? -1;
					texExt.Remove("KHR_texture_basisu");
				}

				if (src >= 0)
				{
					tex["source"] = src;
					texExt["EXT_texture_webp"] = new JsonObject { ["source"] = src };
				}
			}
			else if (src >= 0)
			{
				tex["extensions"] = new JsonObject
				{
					["EXT_texture_webp"] = new JsonObject { ["source"] = src }
				};
			}
		}

		// Update extensionsUsed and extensionsRequired
		void UpdateExtensionLists(string listName)
		{
			if (root.TryGetPropertyValue(listName, out var extList) && extList is JsonArray arr)
			{
				bool hasWebp = false;
				for (int e = arr.Count - 1; e >= 0; e--)
				{
					string? name = arr[e]?.GetValue<string>();
					if (name == "KHR_texture_basisu")
					{
						arr.RemoveAt(e);
					}
					else if (name == "EXT_texture_webp")
					{
						hasWebp = true;
					}
				}
				if (!hasWebp)
				{
					arr.Add("EXT_texture_webp");
				}
			}
			else
			{
				root[listName] = new JsonArray("EXT_texture_webp");
			}
		}

		UpdateExtensionLists("extensionsUsed");
		UpdateExtensionLists("extensionsRequired");

		return BuildGlb(root, newBinStream.ToArray(), glbVer);
	}
}
