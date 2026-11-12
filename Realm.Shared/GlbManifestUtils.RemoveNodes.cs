using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>removing extensions and remapping node indices.</summary>
public static partial class GlbManifestUtils
	{
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
}
