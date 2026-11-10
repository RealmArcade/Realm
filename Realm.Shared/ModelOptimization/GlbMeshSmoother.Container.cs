using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>reading and rewriting the glb container around the meshes.</summary>
public static unsafe partial class GlbMeshSmoother
	{
	private static bool IsValidGlb(JsonObject root)
	{
		return root["meshes"] is JsonArray { Count: > 0 } &&
		       root["accessors"] is JsonArray &&
		       root["bufferViews"] is JsonArray &&
		       root["buffers"] is JsonArray;
	}

	private static void CopyRetainedBufferViews(JsonArray bufferViews, HashSet<int> retainedBufferViews, byte[] binChunk, MemoryStream newBinStream)
	{
		for (int bvIdx = 0; bvIdx < bufferViews.Count; bvIdx++)
		{
			CopyBufferView(bvIdx, bufferViews, retainedBufferViews, binChunk, newBinStream);
		}
	}

	private static void CopyBufferView(int bvIdx, JsonArray bufferViews, HashSet<int> retainedBufferViews, byte[] binChunk, MemoryStream newBinStream)
	{
		if (!retainedBufferViews.Contains(bvIdx)) return;
		if (bufferViews[bvIdx] is not JsonObject bv) return;

		int origOffset = 0;
		if (bv.TryGetPropertyValue("byteOffset", out var offsetNode) && offsetNode != null)
			origOffset = offsetNode.GetValue<int>();

		int origLength = 0;
		if (bv.TryGetPropertyValue("byteLength", out var lengthNode) && lengthNode != null)
			origLength = lengthNode.GetValue<int>();

		while ((newBinStream.Position % 4) != 0) newBinStream.WriteByte(0);
		int newOffset = (int)newBinStream.Position;

		if (origOffset + origLength <= binChunk.Length)
		{
			newBinStream.Write(binChunk, origOffset, origLength);
			while ((newBinStream.Position % 4) != 0) newBinStream.WriteByte(0);
		}

		bv["byteOffset"] = newOffset;
	}

	private static void RemoveMsftLodExtension(JsonObject root, string arrayName)
	{
		if (root[arrayName] is not JsonArray extArray) return;
		for (int i = extArray.Count - 1; i >= 0; i--)
		{
			if (extArray[i]?.GetValue<string>() == "MSFT_lod")
			{
				extArray.RemoveAt(i);
			}
		}
	}

	private static void CleanNodes(JsonObject root)
	{
		GlbManifestUtils.RemoveAndRemapNodes(root, nodeObj =>
		{
			string nodeName = nodeObj["name"]?.GetValue<string>() ?? string.Empty;
			return IsLodNode(nodeName);
		});

		if (root["nodes"] is JsonArray remainingNodes)
		{
			foreach (var n in remainingNodes)
			{
				if (n is not JsonObject nodeObj) continue;
				string nodeName = nodeObj["name"]?.GetValue<string>() ?? string.Empty;

				if (IsLod0Node(nodeName))
				{
					CleanLod0Node(nodeObj, nodeName);
				}

				CleanExtensions(nodeObj);
			}
		}
	}

	private static bool IsLodNode(string nodeName)
	{
		return nodeName.EndsWith("_LOD1", StringComparison.OrdinalIgnoreCase) ||
		       nodeName.EndsWith("_LOD2", StringComparison.OrdinalIgnoreCase) ||
		       nodeName.EndsWith("_LOD3", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsLod0Node(string nodeName)
	{
		return nodeName.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase);
	}

	private static void CleanLod0Node(JsonObject nodeObj, string nodeName)
	{
		nodeObj["name"] = nodeName.Substring(0, nodeName.Length - 5);
	}

	private static void CleanExtensions(JsonObject nodeObj)
	{
		if (nodeObj.TryGetPropertyValue("extensions", out var extNode) && extNode is JsonObject nodeExts)
		{
			nodeExts.Remove("MSFT_lod");
		}
	}

	private static HashSet<int> GetRetainedBufferViews(JsonObject root, int bufferViewCount, JsonArray accessors)
	{
		var retained = new HashSet<int>();

		CollectImageBufferViews(root, bufferViewCount, retained);
		CollectAnimationBufferViews(root, bufferViewCount, accessors, retained);
		CollectSkinBufferViews(root, bufferViewCount, accessors, retained);
		CollectMeshBufferViews(root, bufferViewCount, accessors, retained);

		return retained;
	}

	private static void CollectImageBufferViews(JsonObject root, int bufferViewCount, HashSet<int> retained)
	{
		if (root["images"] is not JsonArray images) return;

		foreach (var img in images)
		{
			if (img is JsonObject imgObj && imgObj.TryGetPropertyValue("bufferView", out var bvVal))
			{
				int bvIdx = bvVal?.GetValue<int>() ?? -1;
				if (bvIdx >= 0 && bvIdx < bufferViewCount) retained.Add(bvIdx);
			}
		}
	}

	private static void CollectAnimationBufferViews(JsonObject root, int bufferViewCount, JsonArray accessors, HashSet<int> retained)
	{
		if (root["animations"] is not JsonArray animations) return;

		foreach (var anim in animations)
		{
			if (anim is not JsonObject animObj || animObj["samplers"] is not JsonArray samplers) continue;
			foreach (var s in samplers)
			{
				if (s is not JsonObject sampObj) continue;
				AddRetainedAccessor(sampObj, "input", accessors, retained, bufferViewCount);
				AddRetainedAccessor(sampObj, "output", accessors, retained, bufferViewCount);
			}
		}
	}

	private static void CollectSkinBufferViews(JsonObject root, int bufferViewCount, JsonArray accessors, HashSet<int> retained)
	{
		if (root["skins"] is not JsonArray skins) return;

		foreach (var skin in skins)
		{
			CollectSkinBufferView(skin, bufferViewCount, accessors, retained);
		}
	}

	private static void CollectSkinBufferView(JsonNode? skin, int bufferViewCount, JsonArray accessors, HashSet<int> retained)
	{
		if (!TryGetIntProperty(skin as JsonObject, "inverseBindMatrices", out int aIdx)) return;
		if (aIdx < 0 || aIdx >= accessors.Count) return;

		if (!TryGetIntProperty(accessors[aIdx] as JsonObject, "bufferView", out int bv)) return;
		if (bv >= 0 && bv < bufferViewCount) retained.Add(bv);
	}

	private static void CollectMeshBufferViews(JsonObject root, int bufferViewCount, JsonArray accessors, HashSet<int> retained)
	{
		if (root["meshes"] is not JsonArray meshes) return;

		foreach (var m in meshes)
		{
			if (m is not JsonObject meshObj || meshObj["primitives"] is not JsonArray primitives) continue;
			foreach (var p in primitives)
			{
				if (p is not JsonObject primObj) continue;

				CollectPrimitiveAttributes(primObj, bufferViewCount, accessors, retained);
				CollectPrimitiveIndices(primObj, bufferViewCount, accessors, retained);
			}
		}
	}

	private static void CollectPrimitiveAttributes(JsonObject primObj, int bufferViewCount, JsonArray accessors, HashSet<int> retained)
	{
		if (primObj["attributes"] is not JsonObject attrs) return;

		foreach (var kvp in attrs)
		{
			CollectPrimitiveAttribute(kvp.Value, bufferViewCount, accessors, retained);
		}
	}

	private static void CollectPrimitiveAttribute(JsonNode? attrValue, int bufferViewCount, JsonArray accessors, HashSet<int> retained)
	{
		if (attrValue == null) return;

		int aIdx = attrValue.GetValue<int>();
		if (aIdx < 0 || aIdx >= accessors.Count) return;
		if (accessors[aIdx] is not JsonObject aObj) return;

		if (!aObj.TryGetPropertyValue("bufferView", out var bvVal) || bvVal == null) return;
		int bv = bvVal.GetValue<int>();

		if (bv >= 0 && bv < bufferViewCount) retained.Add(bv);
	}

	private static void CollectPrimitiveIndices(JsonObject primObj, int bufferViewCount, JsonArray accessors, HashSet<int> retained)
	{
		if (primObj.TryGetPropertyValue("indices", out var indVal) && indVal != null)
		{
			int aIdx = indVal.GetValue<int>();
			if (aIdx >= 0 && aIdx < accessors.Count && accessors[aIdx] is JsonObject aObj)
			{
				int bv = aObj["bufferView"]?.GetValue<int>() ?? -1;
				if (bv >= 0 && bv < bufferViewCount) retained.Add(bv);
			}
		}
	}

	private static void AddRetainedAccessor(JsonObject obj, string prop, JsonArray accessors, HashSet<int> retained, int bufferViewCount)
	{
		if (!obj.TryGetPropertyValue(prop, out var aVal) || aVal == null) return;

		int aIdx = aVal.GetValue<int>();
		if (aIdx < 0 || aIdx >= accessors.Count) return;

		if (accessors[aIdx] is not JsonObject aObj) return;

		if (!aObj.TryGetPropertyValue("bufferView", out var bvVal) || bvVal == null) return;

		int bv = bvVal.GetValue<int>();
		if (bv >= 0 && bv < bufferViewCount)
		{
			retained.Add(bv);
		}
	}
}
