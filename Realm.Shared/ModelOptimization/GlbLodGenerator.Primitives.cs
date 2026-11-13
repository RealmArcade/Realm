using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>deciding which primitives take part in a tier.</summary>
public static unsafe partial class GlbLodGenerator
	{
	private static void ProcessPrimitive(JsonObject primObj, int t, float ratio, JsonObject root, byte[] binBytes, MemoryStream newBinStream, JsonArray newPrimitives, LodDecimationProfile? decimationProfile, float uvSeamMergeTolerance)
	{
		if (ShouldSkipPrimitive(primObj))
		{
			newPrimitives.Add(primObj.DeepClone());
			return;
		}

		var geometry = GetGeometry(primObj, root, binBytes);
		if (geometry == null) return;

		uint[]? originalIndices = GetOriginalIndices(primObj, root, binBytes, geometry.Value.PositionCount);
		if (!IsOriginalIndicesValid(originalIndices, geometry.Value.PositionCount))
		{
			newPrimitives.Add(primObj.DeepClone());
			return;
		}

		int targetIndexCount = (int)(originalIndices!.Length * ratio) / 3 * 3;
		if (!IsTargetIndexCountValid(targetIndexCount, originalIndices.Length))
		{
			newPrimitives.Add(primObj.DeepClone());
			return;
		}

		var simplifiedIndices = SimplifyIndices(originalIndices, geometry.Value, ratio, t, binBytes, decimationProfile, uvSeamMergeTolerance);
		if (simplifiedIndices == null)
		{
			newPrimitives.Add(primObj.DeepClone());
			return;
		}

		if (!TryAppendCompactedPrimitive(primObj, simplifiedIndices, geometry.Value, root, binBytes, newBinStream, newPrimitives))
			newPrimitives.Add(primObj.DeepClone());
	}

	private static bool ShouldSkipPrimitive(JsonObject primObj)
	{
		if (primObj.TryGetPropertyValue("mode", out var modeVal) && modeVal != null && modeVal.GetValue<int>() != 4)
			return true;
		if (primObj.ContainsKey("extensions") && primObj["extensions"] != null)
			return true;
		return false;
	}

	private static bool IsOriginalIndicesValid(uint[]? originalIndices, int posCount)
	{
		if (originalIndices == null || originalIndices.Length < 12) return false;
		return !HasInvalidIndex(originalIndices, posCount);
	}

	private static bool IsTargetIndexCountValid(int targetIndexCount, int originalLength)
	{
		return targetIndexCount >= 3 && targetIndexCount < originalLength;
	}

	private const float WeldEpsilon = 0.0001f;
}
