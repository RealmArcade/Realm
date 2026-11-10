using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>walking the meshes and building each lod tier.</summary>
public static unsafe partial class GlbLodGenerator
	{
	private static Dictionary<int, List<int>> ProcessMeshes(JsonObject root, float[] ratios, byte[] binBytes, MemoryStream newBinStream, LodDecimationProfile? decimationProfile, int[] tierTriangleCounts, float uvSeamMergeTolerance)
	{
		var meshLodMap = new Dictionary<int, List<int>>();
		var meshes = root["meshes"] as JsonArray;
		if (meshes == null) return meshLodMap;

		for (int m = 0; m < meshes.Count; m++)
		{
			if (meshes[m] is not JsonObject meshObj) continue;
			ProcessSingleMesh(meshObj, m, root, ratios, binBytes, newBinStream, meshes, meshLodMap, decimationProfile, tierTriangleCounts, uvSeamMergeTolerance);
		}

		return meshLodMap;
	}

	private static void ProcessSingleMesh(JsonObject meshObj, int m, JsonObject root, float[] ratios, byte[] binBytes, MemoryStream newBinStream, JsonArray meshes, Dictionary<int, List<int>> meshLodMap, LodDecimationProfile? decimationProfile, int[] tierTriangleCounts, float uvSeamMergeTolerance)
	{
		if (meshObj["primitives"] is not JsonArray primitives || primitives.Count == 0) return;

		string meshName = meshObj["name"]?.GetValue<string>() ?? $"Mesh_{m}";
		if (meshName.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase))
			meshName = meshName.Substring(0, meshName.Length - 5);
		else if (meshName.EndsWith("LOD0", StringComparison.OrdinalIgnoreCase))
			meshName = meshName.Substring(0, meshName.Length - 4);
		else if (IsExcludedMeshName(meshName))
			return;

		var lodMeshIndices = new List<int>();
		for (int t = 1; t < ratios.Length; t++)
		{
			ProcessLodTier(t, ratios[t], meshObj, root, binBytes, newBinStream, meshes, meshName, primitives, lodMeshIndices, decimationProfile, tierTriangleCounts, uvSeamMergeTolerance);
		}

		if (lodMeshIndices.Count > 0)
		{
			meshLodMap[m] = lodMeshIndices;
			meshObj["name"] = $"{meshName}_LOD0";
			AddTriangleCount(tierTriangleCounts, 0, primitives, root);
		}
	}

	private static bool IsExcludedMeshName(string meshName)
	{
		return meshName.Contains("_LOD", StringComparison.OrdinalIgnoreCase) ||
		       meshName.Contains("LOD1", StringComparison.OrdinalIgnoreCase) ||
		       meshName.Contains("LOD2", StringComparison.OrdinalIgnoreCase) ||
		       meshName.Contains("LOD3", StringComparison.OrdinalIgnoreCase);
	}

	private static void ProcessLodTier(int t, float ratio, JsonObject meshObj, JsonObject root, byte[] binBytes, MemoryStream newBinStream, JsonArray meshes, string meshName, JsonArray primitives, List<int> lodMeshIndices, LodDecimationProfile? decimationProfile, int[] tierTriangleCounts, float uvSeamMergeTolerance)
	{
		var newPrimitives = new JsonArray();
		for (int p = 0; p < primitives.Count; p++)
		{
			if (primitives[p] is not JsonObject primObj) continue;
			ProcessPrimitive(primObj, t, ratio, root, binBytes, newBinStream, newPrimitives, decimationProfile, uvSeamMergeTolerance);
		}

		if (newPrimitives.Count > 0)
		{
			int newMeshIdx = meshes.Count;
			var newMesh = new JsonObject();
			newMesh["name"] = $"{meshName}_LOD{t}";
			newMesh["primitives"] = newPrimitives;
			meshes.Add(newMesh);
			lodMeshIndices.Add(newMeshIdx);
			AddTriangleCount(tierTriangleCounts, t, newPrimitives, root);
		}
	}

	private static void AddTriangleCount(int[] tierTriangleCounts, int tier, JsonArray primitives, JsonObject root)
	{
		if (tierTriangleCounts == null || tier < 0 || tier >= tierTriangleCounts.Length) return;
		if (root["accessors"] is not JsonArray accessors) return;

		int triangleCount = 0;
		for (int p = 0; p < primitives.Count; p++)
		{
			if (primitives[p] is not JsonObject primObj) continue;
			if (!primObj.TryGetPropertyValue("indices", out var indicesNode) || indicesNode == null) continue;

			int accessorIndex = indicesNode.GetValue<int>();
			if (accessorIndex < 0 || accessorIndex >= accessors.Count) continue;
			if (accessors[accessorIndex] is not JsonObject accessor) continue;

			triangleCount += (accessor["count"]?.GetValue<int>() ?? 0) / 3;
		}

		tierTriangleCounts[tier] += triangleCount;
	}
}
