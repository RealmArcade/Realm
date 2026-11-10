using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>the msft lod extension on nodes and the parent map.</summary>
public static partial class GlbManifestUtils
	{
	public static bool RemoveNodeMsftLodExtensions(JsonObject root)
	{
		bool modified = false;
		if (root.TryGetPropertyValue("nodes", out var nodesNode) && nodesNode is JsonArray nodesArray)
		{
			var parentNodeIndices = BuildParentNodeIndexMap(nodesArray);
			for (int n = 0; n < nodesArray.Count; n++)
			{
				if (nodesArray[n] is not JsonObject nodeObj || !nodeObj.TryGetPropertyValue("extensions", out var extVal) || extVal is not JsonObject extObj)
					continue;

				if (extObj.TryGetPropertyValue("MSFT_lod", out var msftLodVal) && msftLodVal is JsonObject msftLodObj)
				{
					if (msftLodObj.TryGetPropertyValue("ids", out var idsVal) && idsVal is JsonArray idsArr)
					{
						var siblingNodeIndices = ResolveSiblingNodeIndices(root, nodesArray, n, parentNodeIndices) ?? GetOrCreateNodeChildren(nodeObj);
						foreach (var idItem in idsArr)
						{
							if (idItem is JsonValue jv && jv.TryGetValue(out int lodIdx) && lodIdx >= 0 && lodIdx < nodesArray.Count)
							{
								modified |= AppendNodeIndexIfMissing(siblingNodeIndices, lodIdx);
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

		modified |= EnsureAllNodesParentedOrInScene(root);
		return modified;
	}

	private static Dictionary<int, int> BuildParentNodeIndexMap(JsonArray nodes)
	{
		var parentNodeIndices = new Dictionary<int, int>();
		for (int n = 0; n < nodes.Count; n++)
		{
			if (nodes[n] is not JsonObject nodeObj || nodeObj["children"] is not JsonArray children) continue;

			foreach (var childItem in children)
			{
				if (childItem is not JsonValue childValue || !childValue.TryGetValue(out int childIndex)) continue;
				if (childIndex < 0 || parentNodeIndices.ContainsKey(childIndex)) continue;

				parentNodeIndices[childIndex] = n;
			}
		}

		return parentNodeIndices;
	}

	private static JsonArray? ResolveSiblingNodeIndices(JsonObject root, JsonArray nodes, int nodeIndex, Dictionary<int, int> parentNodeIndices)
	{
		if (parentNodeIndices.TryGetValue(nodeIndex, out int parentIndex) && parentIndex >= 0 && parentIndex < nodes.Count && nodes[parentIndex] is JsonObject parentObj)
		{
			return GetOrCreateNodeChildren(parentObj);
		}

		if (root["scenes"] is JsonArray scenes)
		{
			foreach (var sceneItem in scenes)
			{
				if (sceneItem is not JsonObject sceneObj || sceneObj["nodes"] is not JsonArray sceneNodes) continue;
				if (!ContainsNodeIndex(sceneNodes, nodeIndex)) continue;

				return sceneNodes;
			}
		}

		return null;
	}

	private static JsonArray GetOrCreateNodeChildren(JsonObject nodeObj)
	{
		if (nodeObj["children"] is JsonArray existingChildren) return existingChildren;

		var childrenArray = new JsonArray();
		nodeObj["children"] = childrenArray;
		return childrenArray;
	}

	private static bool ContainsNodeIndex(JsonArray nodeIndices, int nodeIndex)
	{
		foreach (var item in nodeIndices)
		{
			if (item is JsonValue value && value.TryGetValue(out int existingIndex) && existingIndex == nodeIndex) return true;
		}

		return false;
	}

	private static bool AppendNodeIndexIfMissing(JsonArray nodeIndices, int nodeIndex)
	{
		if (ContainsNodeIndex(nodeIndices, nodeIndex)) return false;

		nodeIndices.Add(nodeIndex);
		return true;
	}
}
