using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>updating the texture nodes and the extension lists that point at them.</summary>
public static partial class GlbManifestUtils
	{
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
