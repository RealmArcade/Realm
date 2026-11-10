using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>reading and cleaning the glb container before lods are built.</summary>
public static unsafe partial class GlbLodGenerator
	{
	private static (bool, byte[], string, int[]) ProcessGlb(byte[] input, float[] ratios, float[] visBegins, float[] visEnds, LodDecimationProfile? decimationProfile, float uvSeamMergeTolerance)
	{
		var (jsonNode, binBytes, glbVer) = GlbManifestUtils.ParseGlb(input);
		if (jsonNode is not JsonObject root)
			return (false, input, "Failed to parse glTF JSON root", Array.Empty<int>());

		CleanEarlyExtensions(root);
		CleanEarlyNodes(root);

		if (!HasRequiredGlbArrays(root))
			return (true, input, string.Empty, Array.Empty<int>());

		binBytes ??= Array.Empty<byte>();
		using var newBinStream = new MemoryStream();
		newBinStream.Write(binBytes, 0, binBytes.Length);

		int[] tierTriangleCounts = new int[ratios.Length];
		var meshLodMap = ProcessMeshes(root, ratios, binBytes, newBinStream, decimationProfile, tierTriangleCounts, uvSeamMergeTolerance);

		AlignStream(newBinStream);
		byte[] finalBinBytes = newBinStream.ToArray();

		UpdateBuffersLength(root, finalBinBytes.Length);
		UpdateSceneNodesWithLods(root, meshLodMap, visBegins, visEnds);
		SanityCheckFinalScenes(root);

		byte[] outputGlb = GlbManifestUtils.BuildGlb(root, finalBinBytes, glbVer);
		return (true, outputGlb, string.Empty, tierTriangleCounts);
	}

	private static void CleanEarlyExtensions(JsonObject root)
	{
		RemoveMsftLodFrom(root["extensionsUsed"] as JsonArray);
		RemoveMsftLodFrom(root["extensionsRequired"] as JsonArray);
	}

	private static void RemoveMsftLodFrom(JsonArray? extArray)
	{
		if (extArray == null) return;
		for (int i = extArray.Count - 1; i >= 0; i--)
		{
			RemoveIfMsftLod(extArray, i);
		}
	}

	private static void RemoveIfMsftLod(JsonArray extArray, int i)
	{
		if (extArray[i]?.GetValue<string>() == "MSFT_lod")
			extArray.RemoveAt(i);
	}

	private static void CleanEarlyNodes(JsonObject root)
	{
		GlbManifestUtils.RemoveAndRemapNodes(root, nodeObj =>
		{
			string nodeName = nodeObj["name"]?.GetValue<string>() ?? string.Empty;
			return nodeName.EndsWith("_LOD1", StringComparison.OrdinalIgnoreCase) ||
			       nodeName.EndsWith("_LOD2", StringComparison.OrdinalIgnoreCase) ||
			       nodeName.EndsWith("_LOD3", StringComparison.OrdinalIgnoreCase);
		});

		if (root["nodes"] is JsonArray remainingNodes)
		{
			foreach (var n in remainingNodes)
			{
				if (n is not JsonObject nodeObj) continue;
				string nodeName = nodeObj["name"]?.GetValue<string>() ?? string.Empty;

				if (nodeName.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase))
					nodeObj["name"] = nodeName.Substring(0, nodeName.Length - 5);

				if (nodeObj.TryGetPropertyValue("extensions", out var extNode) && extNode is JsonObject nodeExts)
					nodeExts.Remove("MSFT_lod");
			}
		}
	}

	private static bool HasRequiredGlbArrays(JsonObject root)
	{
		if (root["meshes"] is not JsonArray meshes || meshes.Count == 0) return false;
		if (root["accessors"] is not JsonArray) return false;
		if (root["bufferViews"] is not JsonArray) return false;
		if (root["buffers"] is not JsonArray) return false;
		return true;
	}
}
