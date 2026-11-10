using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>ensuring every node is parented or in a scene.</summary>
public static partial class GlbManifestUtils
	{
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
}
