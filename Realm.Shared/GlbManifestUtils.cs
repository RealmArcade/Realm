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
			if (node.GetValueKind() == JsonValueKind.True) return true;
			if (node is JsonValue jv) return EvaluateJsonValue(jv);
			return false;
		}
		catch
		{
			return false;
		}
	}

	private static bool EvaluateJsonValue(JsonValue jv)
	{
		if (jv.TryGetValue<bool>(out var b)) return b;
		if (jv.TryGetValue<int>(out var num)) return num != 0;
		if (jv.TryGetValue<string>(out var str) && bool.TryParse(str, out var parsedB)) return parsedB;
		return false;
	}

	public static bool HasOptimizationFlag(byte[] glbBytes)
	{
		var (json, _, _) = ParseGlb(glbBytes);
		if (json is not JsonObject root) return false;

		if (HasCompletedExtras(root)) return true;
		if (HasCompletedAssetExtras(root)) return true;
		if (HasExtension(root, "extensionsUsed", "MSFT_lod")) return true;
		if (HasExtension(root, "extensionsRequired", "MSFT_lod")) return true;

		return false;
	}

	private static bool HasCompletedExtras(JsonObject root)
	{
		if (root.TryGetPropertyValue("extras", out var extrasNode) && extrasNode is JsonObject extras)
		{
			if (TryGetTrue(extras, "realm_optimize_completed")) return true;
			if (TryGetTrue(extras, "realm_decimate_completed")) return true;
		}

		return false;
	}

	private static bool HasCompletedAssetExtras(JsonObject root)
	{
		if (root.TryGetPropertyValue("asset", out var assetNode) && assetNode is JsonObject asset &&
		    asset.TryGetPropertyValue("extras", out var assetExtrasNode) && assetExtrasNode is JsonObject assetExtras)
		{
			if (TryGetTrue(assetExtras, "realm_optimize_completed")) return true;
			if (TryGetTrue(assetExtras, "realm_decimate_completed")) return true;
		}

		return false;
	}

	private static bool HasExtension(JsonObject root, string listName, string extensionName)
	{
		if (root.TryGetPropertyValue(listName, out var extNode) && extNode is JsonArray extArray)
		{
			foreach (var ext in extArray)
			{
				if (ext?.GetValue<string>() == extensionName) return true;
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

		InjectAssetOptimizationMetadata(root);
		ApplyExtraStats(extraStats, extras);

		return BuildGlb(root, bin, glbVer);
	}

	private static void InjectAssetOptimizationMetadata(JsonObject root)
	{
		if (!root.ContainsKey("asset") || root["asset"] == null)
			root["asset"] = new JsonObject();

		if (root["asset"] is not JsonObject assetObj) return;

		if (!assetObj.ContainsKey("extras") || assetObj["extras"] == null)
			assetObj["extras"] = new JsonObject();

		if (assetObj["extras"] is JsonObject assetExtras)
			assetExtras["realm_optimize_completed"] = true;
	}

	private static void ApplyExtraStats(Dictionary<string, object>? extraStats, JsonObject extras)
	{
		if (extraStats == null) return;

		foreach (var kvp in extraStats)
		{
			if (kvp.Value is int i) extras[kvp.Key] = i;
			else if (kvp.Value is float f) extras[kvp.Key] = f;
			else if (kvp.Value is bool b) extras[kvp.Key] = b;
			else if (kvp.Value is string s) extras[kvp.Key] = s;
		}
	}

	public static (byte[] UnoptimizedBytes, bool WasModified) StripOptimizationMetadata(byte[] glbBytes)
	{
		var (json, bin, glbVer) = ParseGlb(glbBytes);
		if (json is not JsonObject root) return (glbBytes, false);

		bool modified = false;

		modified |= StripExtras(root);
		modified |= StripAssetExtras(root);
		modified |= StripMsftLodExtensions(root);
		modified |= StripLodNodes(root);

		if (!modified) return (glbBytes, false);

		return (BuildGlb(root, bin, glbVer), true);
	}

	private static bool StripExtras(JsonObject root)
	{
		bool modified = false;
		if (root.TryGetPropertyValue("extras", out var extrasNode) && extrasNode is JsonObject extras)
		{
			if (extras.Remove("realm_optimize_completed")) modified = true;
			if (extras.Remove("realm_version")) modified = true;
			if (extras.Remove("optimization_timestamp")) modified = true;
			if (extras.Remove("msft_lod_embedded")) modified = true;
		}

		return modified;
	}

	private static bool StripAssetExtras(JsonObject root)
	{
		if (root.TryGetPropertyValue("asset", out var assetNode) && assetNode is JsonObject asset &&
		    asset.TryGetPropertyValue("extras", out var assetExtrasNode) && assetExtrasNode is JsonObject assetExtras)
		{
			if (assetExtras.Remove("realm_optimize_completed")) return true;
		}

		return false;
	}

	private static bool StripMsftLodExtensions(JsonObject root)
	{
		bool modified = false;
		modified |= RemoveExtension(root, "extensionsUsed", "MSFT_lod");
		modified |= RemoveExtension(root, "extensionsRequired", "MSFT_lod");
		modified |= RemoveNodeMsftLodExtensions(root);
		return modified;
	}

	public static bool RemoveNodeMsftLodExtensions(JsonObject root)
	{
		bool modified = false;
		if (root.TryGetPropertyValue("nodes", out var nodesNode) && nodesNode is JsonArray nodesArray)
		{
			foreach (var n in nodesArray)
			{
				if (n is JsonObject nodeObj && nodeObj.TryGetPropertyValue("extensions", out var extVal) && extVal is JsonObject extObj)
				{
					if (extObj.TryGetPropertyValue("MSFT_lod", out var msftLodVal) && msftLodVal is JsonObject msftLodObj)
					{
						if (msftLodObj.TryGetPropertyValue("ids", out var idsVal) && idsVal is JsonArray idsArr)
						{
							if (!nodeObj.TryGetPropertyValue("children", out var childrenVal) || childrenVal is not JsonArray childrenArr)
							{
								childrenArr = new JsonArray();
								nodeObj["children"] = childrenArr;
							}

							HashSet<int> existingChildren = new HashSet<int>();
							foreach (var childItem in childrenArr)
							{
								if (childItem is JsonValue jv && jv.TryGetValue(out int cIdx))
								{
									existingChildren.Add(cIdx);
								}
							}

							foreach (var idItem in idsArr)
							{
								if (idItem is JsonValue jv && jv.TryGetValue(out int lodIdx) && lodIdx >= 0 && lodIdx < nodesArray.Count)
								{
									if (existingChildren.Add(lodIdx))
									{
										childrenArr.Add(lodIdx);
										modified = true;
									}
								}
							}
						}
					}

					if (extObj.Remove("MSFT_lod"))
					{
						modified = true;
						if (extObj.Count == 0)
						{
							nodeObj.Remove("extensions");
						}
					}
				}
			}
		}

		modified |= EnsureAllNodesParentedOrInScene(root);
		return modified;
	}

	public static bool EnsureAllNodesParentedOrInScene(JsonObject root)
	{
		if (!root.TryGetPropertyValue("nodes", out var nodesNode) || nodesNode is not JsonArray nodesArray || nodesArray.Count == 0)
			return false;

		var referencedNodes = new HashSet<int>();

		if (root.TryGetPropertyValue("scenes", out var scenesNode) && scenesNode is JsonArray scenesArray)
		{
			foreach (var sc in scenesArray)
			{
				if (sc is JsonObject scObj && scObj.TryGetPropertyValue("nodes", out var scNodesVal) && scNodesVal is JsonArray scNodesArr)
				{
					foreach (var item in scNodesArr)
					{
						if (item is JsonValue jv && jv.TryGetValue(out int idx))
						{
							referencedNodes.Add(idx);
						}
					}
				}
			}
		}

		foreach (var n in nodesArray)
		{
			if (n is JsonObject nObj && nObj.TryGetPropertyValue("children", out var childrenVal) && childrenVal is JsonArray childrenArr)
			{
				foreach (var childItem in childrenArr)
				{
					if (childItem is JsonValue jv && jv.TryGetValue(out int cIdx))
					{
						referencedNodes.Add(cIdx);
					}
				}
			}
		}

		bool modified = false;
		JsonArray targetSceneNodes;

		if (!root.TryGetPropertyValue("scenes", out var scenesVal) || scenesVal is not JsonArray scenesList || scenesList.Count == 0)
		{
			scenesList = new JsonArray();
			root["scenes"] = scenesList;

			var defaultScene = new JsonObject();
			targetSceneNodes = new JsonArray();
			defaultScene["nodes"] = targetSceneNodes;
			scenesList.Add(defaultScene);
			root["scene"] = 0;
			modified = true;
		}
		else
		{
			var primaryScene = scenesList[0] as JsonObject;
			if (primaryScene == null)
			{
				primaryScene = new JsonObject();
				scenesList[0] = primaryScene;
				modified = true;
			}

			if (!primaryScene.TryGetPropertyValue("nodes", out var scNodesVal) || scNodesVal is not JsonArray scNodesArr)
			{
				scNodesArr = new JsonArray();
				primaryScene["nodes"] = scNodesArr;
				modified = true;
			}

			targetSceneNodes = scNodesArr;
		}

		for (int i = 0; i < nodesArray.Count; i++)
		{
			if (!referencedNodes.Contains(i))
			{
				targetSceneNodes.Add(i);
				referencedNodes.Add(i);
				modified = true;
			}
		}

		return modified;
	}

	private static bool RemoveExtension(JsonObject root, string listName, string extensionName)
	{
		bool modified = false;
		if (root.TryGetPropertyValue(listName, out var extNode) && extNode is JsonArray extArray)
		{
			for (int i = extArray.Count - 1; i >= 0; i--)
			{
				if (extArray[i]?.GetValue<string>() == extensionName)
				{
					extArray.RemoveAt(i);
					modified = true;
				}
			}
		}

		return modified;
	}

	public static bool RemoveAndRemapNodes(JsonObject root, Func<JsonObject, bool> shouldRemovePredicate)
	{
		if (!root.TryGetPropertyValue("nodes", out var nodesNode) || nodesNode is not JsonArray nodesArray || nodesArray.Count == 0)
			return false;

		int originalNodeCount = nodesArray.Count;
		var removedIndices = new HashSet<int>();

		for (int i = 0; i < originalNodeCount; i++)
		{
			if (nodesArray[i] is JsonObject nodeObj && shouldRemovePredicate(nodeObj))
			{
				removedIndices.Add(i);
			}
		}

		if (removedIndices.Count == 0) return false;

		for (int i = originalNodeCount - 1; i >= 0; i--)
		{
			if (removedIndices.Contains(i))
			{
				nodesArray.RemoveAt(i);
			}
		}

		int[] remap = new int[originalNodeCount];
		int shift = 0;
		for (int i = 0; i < originalNodeCount; i++)
		{
			if (removedIndices.Contains(i))
			{
				remap[i] = -1;
				shift++;
			}
			else
			{
				remap[i] = i - shift;
			}
		}

		if (root["scenes"] is JsonArray scenes)
		{
			foreach (var sc in scenes)
			{
				if (sc is JsonObject scObj && scObj["nodes"] is JsonArray scNodes)
				{
					RemapJsonIntArray(scNodes, remap);
				}
			}
		}

		if (root["nodes"] is JsonArray remainingNodes)
		{
			foreach (var n in remainingNodes)
			{
				if (n is not JsonObject nObj) continue;

				if (nObj["children"] is JsonArray children)
				{
					RemapJsonIntArray(children, remap);
					if (children.Count == 0)
					{
						nObj.Remove("children");
					}
				}

				if (nObj["extensions"] is JsonObject nExts && nExts["MSFT_lod"] is JsonObject msftLod && msftLod["ids"] is JsonArray ids)
				{
					RemapJsonIntArray(ids, remap);
					if (ids.Count == 0)
					{
						nExts.Remove("MSFT_lod");
						if (nExts.Count == 0)
						{
							nObj.Remove("extensions");
						}
					}
				}
			}
		}

		if (root["skins"] is JsonArray skins)
		{
			foreach (var sk in skins)
			{
				if (sk is not JsonObject skObj) continue;

				if (skObj["joints"] is JsonArray joints)
				{
					RemapJsonIntArray(joints, remap);
				}

				if (skObj.TryGetPropertyValue("skeleton", out var skelVal) && skelVal is JsonValue jv && jv.TryGetValue(out int skelIdx))
				{
					if (skelIdx >= 0 && skelIdx < originalNodeCount && remap[skelIdx] != -1)
					{
						skObj["skeleton"] = remap[skelIdx];
					}
					else
					{
						skObj.Remove("skeleton");
					}
				}
			}
		}

		if (root["animations"] is JsonArray animations)
		{
			foreach (var anim in animations)
			{
				if (anim is not JsonObject animObj || animObj["channels"] is not JsonArray channels) continue;

				for (int i = channels.Count - 1; i >= 0; i--)
				{
					if (channels[i] is JsonObject chObj && chObj["target"] is JsonObject targetObj && targetObj.TryGetPropertyValue("node", out var nodeVal) && nodeVal is JsonValue jv && jv.TryGetValue(out int targetNodeIdx))
					{
						if (targetNodeIdx >= 0 && targetNodeIdx < originalNodeCount)
						{
							int newIdx = remap[targetNodeIdx];
							if (newIdx != -1)
							{
								targetObj["node"] = newIdx;
							}
							else
							{
								channels.RemoveAt(i);
							}
						}
					}
				}
			}
		}

		return true;
	}

	private static void RemapJsonIntArray(JsonArray arr, int[] remap)
	{
		var seen = new HashSet<int>();
		for (int i = arr.Count - 1; i >= 0; i--)
		{
			if (arr[i] is JsonValue jv && jv.TryGetValue(out int idx) && idx >= 0 && idx < remap.Length)
			{
				int newIdx = remap[idx];
				if (newIdx != -1 && seen.Add(newIdx))
				{
					arr[i] = newIdx;
				}
				else
				{
					arr.RemoveAt(i);
				}
			}
			else
			{
				arr.RemoveAt(i);
			}
		}
	}

	private static bool StripLodNodes(JsonObject root)
	{
		return RemoveAndRemapNodes(root, nodeObj =>
		{
			string nameStr = nodeObj["name"]?.GetValue<string>() ?? "";
			return nameStr.Contains("_LOD1") || nameStr.Contains("_LOD2") || nameStr.Contains("_LOD3");
		});
	}

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

	private static bool TryGetTextureObject(string propName, JsonObject container, out JsonObject texObj)
	{
		texObj = null;
		if (!container.TryGetPropertyValue(propName, out var texVal)) return false;
		if (texVal is not JsonObject obj) return false;

		texObj = obj;
		return true;
	}

	private static bool TryGetValidTexture(JsonObject texObj, JsonArray textures, out JsonObject tex)
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

	private static (Dictionary<int, int>, HashSet<int>) IdentifyImageBufferViews(JsonArray images, JsonArray bufferViews)
	{
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

		return (imageBvMap, imageBufferViews);
	}

	private static Dictionary<int, byte[]> EncodeImagesToWebp(
		JsonArray images, JsonArray bufferViews, byte[] bin,
		HashSet<int> pbrImageIndices, Dictionary<int, int> imageBvMap, int maxResolution)
	{
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

			newImageBytes[i] = ProcessSingleImage(raw, pbrImageIndices.Contains(i), maxResolution);
		}

		return newImageBytes;
	}

	private static byte[] ProcessSingleImage(byte[] raw, bool isPbr, int maxResolution)
	{
		try
		{
			using var img = SKBitmap.Decode(raw);
			if (img == null) return raw;

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
				return TextureConverter.EncodeWebp(
					workingImg,
					lossless: isPbr,
					quality: isPbr ? 100 : 80
				);
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
			return raw;
		}
	}

	private static byte[] RebuildBinaryChunk(
		byte[] bin, JsonArray images, JsonArray bufferViews,
		Dictionary<int, int> imageBvMap, HashSet<int> imageBufferViews,
		Dictionary<int, byte[]> newImageBytes, JsonObject root)
	{
		using var newBinStream = new MemoryStream();
		var newBvOffsets = new int[bufferViews.Count];
		var newBvLengths = new int[bufferViews.Count];

		RebuildNonImageBufferViews(bin, bufferViews, imageBufferViews, newBinStream, newBvOffsets, newBvLengths);
		RebuildImageBufferViews(images, imageBvMap, newImageBytes, newBinStream, newBvOffsets, newBvLengths);

		UpdateBufferViewsJson(bufferViews, newBvOffsets, newBvLengths);
		UpdateBuffersByteLength(root, (int)newBinStream.Position);

		return newBinStream.ToArray();
	}

	private static void RebuildNonImageBufferViews(
		byte[] bin, JsonArray bufferViews, HashSet<int> imageBufferViews,
		MemoryStream newBinStream, int[] newBvOffsets, int[] newBvLengths)
	{
		for (int bvIdx = 0; bvIdx < bufferViews.Count; bvIdx++)
		{
			if (imageBufferViews.Contains(bvIdx)) continue;
			if (bufferViews[bvIdx] is not JsonObject bv) continue;

			int origOffset = bv["byteOffset"]?.GetValue<int>() ?? 0;
			int origLength = bv["byteLength"]?.GetValue<int>() ?? 0;

			newBvOffsets[bvIdx] = (int)newBinStream.Position;
			newBvLengths[bvIdx] = origLength;

			if (origOffset + origLength <= bin.Length)
			{
				newBinStream.Write(bin, origOffset, origLength);
				int pad = (4 - (origLength % 4)) % 4;
				for (int p = 0; p < pad; p++) newBinStream.WriteByte(0);
			}
		}
	}

	private static void RebuildImageBufferViews(
		JsonArray images, Dictionary<int, int> imageBvMap, Dictionary<int, byte[]> newImageBytes,
		MemoryStream newBinStream, int[] newBvOffsets, int[] newBvLengths)
	{
		for (int i = 0; i < images.Count; i++)
		{
			if (!imageBvMap.TryGetValue(i, out int bvIdx)) continue;
			if (!newImageBytes.TryGetValue(i, out byte[]? imgData)) continue;

			newBvOffsets[bvIdx] = (int)newBinStream.Position;
			newBvLengths[bvIdx] = imgData.Length;

			newBinStream.Write(imgData, 0, imgData.Length);
			int pad = (4 - (imgData.Length % 4)) % 4;
			for (int p = 0; p < pad; p++) newBinStream.WriteByte(0);

			if (images[i] is JsonObject imgObj)
			{
				imgObj["mimeType"] = "image/webp";
			}
		}
	}

	private static void UpdateBufferViewsJson(JsonArray bufferViews, int[] newBvOffsets, int[] newBvLengths)
	{
		for (int bvIdx = 0; bvIdx < bufferViews.Count; bvIdx++)
		{
			if (bufferViews[bvIdx] is JsonObject bv)
			{
				bv["byteOffset"] = newBvOffsets[bvIdx];
				bv["byteLength"] = newBvLengths[bvIdx];
			}
		}
	}

	private static void UpdateBuffersByteLength(JsonObject root, int totalLength)
	{
		if (root["buffers"] is JsonArray buffers && buffers.Count > 0 && buffers[0] is JsonObject buf0)
		{
			buf0["byteLength"] = totalLength;
		}
	}

	private static void UpdateTextureNodes(JsonArray textures)
	{
		for (int t = 0; t < textures.Count; t++)
		{
			if (textures[t] is not JsonObject tex) continue;
			UpdateSingleTextureNode(tex);
		}
	}

	private static void UpdateSingleTextureNode(JsonObject tex)
	{
		int src = tex["source"]?.GetValue<int>() ?? -1;

		if (tex.TryGetPropertyValue("extensions", out var extNode) && extNode is JsonObject texExt)
		{
			src = ProcessBasisuExtension(texExt, src);

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

	private static int ProcessBasisuExtension(JsonObject texExt, int src)
	{
		if (texExt.TryGetPropertyValue("KHR_texture_basisu", out var basVal) && basVal is JsonObject basObj)
		{
			if (src < 0) src = basObj["source"]?.GetValue<int>() ?? -1;
			texExt.Remove("KHR_texture_basisu");
		}

		return src;
	}

	private static void UpdateExtensionLists(JsonObject root, string listName)
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
}