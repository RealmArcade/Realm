using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>the final scene check and the parameter validation it repeats.</summary>
public static unsafe partial class GlbLodGenerator
	{
	private static void SanityCheckFinalScenes(JsonObject root)
	{
		if (root["scenes"] is not JsonArray finalScenes || root["nodes"] is not JsonArray finalNodes) return;

		foreach (var sc in finalScenes)
		{
			SanityCheckScene(sc, finalNodes);
		}
	}

	private static void SanityCheckScene(JsonNode? sc, JsonArray finalNodes)
	{
		if (sc is not JsonObject scObj || scObj["nodes"] is not JsonArray scNodes) return;

		var distinctNodes = new JsonArray();
		var seen = new HashSet<int>();
		foreach (var item in scNodes)
		{
			TryAddDistinctNode(item, finalNodes, seen, distinctNodes);
		}

		scObj["nodes"] = distinctNodes;
	}

	private static void TryAddDistinctNode(JsonNode? item, JsonArray finalNodes, HashSet<int> seen, JsonArray distinctNodes)
	{
		if (item is not JsonValue jv || !jv.TryGetValue(out int nodeIdx)) return;
		if (nodeIdx >= 0 && nodeIdx < finalNodes.Count && seen.Add(nodeIdx))
			distinctNodes.Add(nodeIdx);
	}

	private static bool IsDecreasingRatios(float[] ratios)
	{
		for (int i = 0; i < ratios.Length; i++)
		{
			if (IsInvalidRatio(ratios[i])) return false;
			if (i > 0 && ratios[i] >= ratios[i - 1]) return false;
		}

		return true;
	}

	private static bool IsInvalidRatio(float ratio)
	{
		return float.IsNaN(ratio) || float.IsInfinity(ratio) || ratio <= 0f || ratio > 1.0f;
	}
}
