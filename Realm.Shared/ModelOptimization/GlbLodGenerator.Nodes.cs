using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>wiring the lod meshes into the scene nodes.</summary>
public static unsafe partial class GlbLodGenerator
	{
	private static void UpdateSceneNodesWithLods(JsonObject root, Dictionary<int, List<int>> meshLodMap, float[] visBegins, float[] visEnds)
	{
		if (root["nodes"] is not JsonArray nodes || nodes.Count == 0) return;

		var nodeParentMap = BuildNodeParentMap(nodes);
		var sceneRootLists = GetSceneRootLists(root);

		int initialNodeCount = nodes.Count;
		for (int n = 0; n < initialNodeCount; n++)
		{
			if (nodes[n] is not JsonObject nodeObj || !nodeObj.ContainsKey("mesh")) continue;

			int meshIdx = nodeObj["mesh"]!.GetValue<int>();
			if (!meshLodMap.TryGetValue(meshIdx, out var lodMeshesList)) continue;

			ProcessNodeLod(nodeObj, n, lodMeshesList, nodes, nodeParentMap, sceneRootLists, visBegins, visEnds);
		}
	}

	private static Dictionary<int, int> BuildNodeParentMap(JsonArray nodes)
	{
		var nodeParentMap = new Dictionary<int, int>(nodes.Count);
		for (int i = 0; i < nodes.Count; i++)
		{
			if (nodes[i] is not JsonObject nodeObj || nodeObj["children"] is not JsonArray children) continue;

			foreach (var childItem in children)
			{
				if (childItem is JsonValue childValue && childValue.TryGetValue(out int childIndex))
				{
					nodeParentMap[childIndex] = i;
				}
			}
		}

		return nodeParentMap;
	}

	private static List<JsonArray> GetSceneRootLists(JsonObject root)
	{
		if (root["scenes"] is not JsonArray scenes)
		{
			scenes = new JsonArray();
			root["scenes"] = scenes;
		}

		if (scenes.Count == 0)
		{
			scenes.Add(new JsonObject { ["nodes"] = new JsonArray() });
		}

		var sceneRootLists = new List<JsonArray>(scenes.Count);
		foreach (var sceneItem in scenes)
		{
			if (sceneItem is not JsonObject sceneObj) continue;

			if (sceneObj["nodes"] is not JsonArray sceneRoots)
			{
				sceneRoots = new JsonArray();
				sceneObj["nodes"] = sceneRoots;
			}

			sceneRootLists.Add(sceneRoots);
		}

		return sceneRootLists;
	}

	private static void ProcessNodeLod(JsonObject nodeObj, int nodeIndex, List<int> lodMeshesList, JsonArray nodes, Dictionary<int, int> nodeParentMap, List<JsonArray> sceneRootLists, float[] visBegins, float[] visEnds)
	{
		string nodeName = nodeObj["name"]?.GetValue<string>() ?? $"Node_{nodeIndex}";
		if (nodeName.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase))
			nodeName = nodeName.Substring(0, nodeName.Length - 5);
		nodeObj["name"] = $"{nodeName}_LOD0";

		JsonArray siblingList = GetNodeSiblingList(nodes, nodeParentMap, sceneRootLists, nodeIndex);
		for (int t = 0; t < lodMeshesList.Count; t++)
		{
			int lodTier = t + 1;
			var lodNode = CreateLodNode(nodeObj, nodeName, lodMeshesList[t], lodTier, visBegins, visEnds);

			int newLodNodeIdx = nodes.Count;
			nodes.Add(lodNode);
			AppendUniqueChildReference(siblingList, newLodNodeIdx);
		}

		UpdateNodeExtensionsWithLod(nodeObj, visBegins[0], visEnds[0]);
	}

	private static JsonArray GetNodeSiblingList(JsonArray nodes, Dictionary<int, int> nodeParentMap, List<JsonArray> sceneRootLists, int nodeIndex)
	{
		if (nodeParentMap.TryGetValue(nodeIndex, out int parentIndex) && nodes[parentIndex] is JsonObject parentObj && parentObj["children"] is JsonArray siblings)
		{
			return siblings;
		}

		foreach (var sceneRoots in sceneRootLists)
		{
			if (ContainsNodeIndex(sceneRoots, nodeIndex)) return sceneRoots;
		}

		if (sceneRootLists.Count == 0) return new JsonArray();

		JsonArray defaultRoots = sceneRootLists[0];
		AppendUniqueChildReference(defaultRoots, nodeIndex);
		return defaultRoots;
	}

	private static bool ContainsNodeIndex(JsonArray nodeIndices, int nodeIndex)
	{
		foreach (var item in nodeIndices)
		{
			if (item is JsonValue value && value.TryGetValue(out int existingIndex) && existingIndex == nodeIndex) return true;
		}

		return false;
	}

	private static void AppendUniqueChildReference(JsonArray childrenArray, int childIndex)
	{
		foreach (var childItem in childrenArray)
		{
			if (childItem is JsonValue childValue && childValue.TryGetValue(out int existingIndex) && existingIndex == childIndex)
			{
				return;
			}
		}

		childrenArray.Add(childIndex);
	}

	private static JsonObject CreateLodNode(JsonObject nodeObj, string nodeName, int lodMeshIdx, int lodTier, float[] visBegins, float[] visEnds)
	{
		var lodNode = new JsonObject
		{
			["name"] = $"{nodeName}_LOD{lodTier}",
			["mesh"] = lodMeshIdx
		};

		if (nodeObj.ContainsKey("skin")) lodNode["skin"] = nodeObj["skin"]!.GetValue<int>();
		if (nodeObj.ContainsKey("matrix")) lodNode["matrix"] = nodeObj["matrix"]!.DeepClone();
		if (nodeObj.ContainsKey("translation")) lodNode["translation"] = nodeObj["translation"]!.DeepClone();
		if (nodeObj.ContainsKey("rotation")) lodNode["rotation"] = nodeObj["rotation"]!.DeepClone();
		if (nodeObj.ContainsKey("scale")) lodNode["scale"] = nodeObj["scale"]!.DeepClone();

		var extras = new JsonObject();
		extras["visibility_range_begin"] = visBegins[lodTier];
		extras["visibility_range_end"] = visEnds[lodTier];
		lodNode["extras"] = extras;

		return lodNode;
	}

	private static void UpdateNodeExtensionsWithLod(JsonObject nodeObj, float visBegin, float visEnd)
	{
		if (nodeObj["extensions"] is JsonObject nodeExts)
		{
			nodeExts.Remove("MSFT_lod");
			if (nodeExts.Count == 0)
			{
				nodeObj.Remove("extensions");
			}
		}

		if (nodeObj["extras"] is not JsonObject nExtras)
		{
			nExtras = new JsonObject();
			nodeObj["extras"] = nExtras;
		}

		nExtras["visibility_range_begin"] = visBegin;
		nExtras["visibility_range_end"] = visEnd;
	}
}
