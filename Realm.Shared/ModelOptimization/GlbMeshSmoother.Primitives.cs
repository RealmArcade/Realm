using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>walking the meshes and primitives and pulling out their attributes.</summary>
public static unsafe partial class GlbMeshSmoother
	{
	private static void ProcessMesh(JsonNode? meshNode, JsonArray accessors, JsonArray bufferViews, byte[] binChunk, float cosThreshold, MemoryStream newBinStream)
	{
		if (meshNode is not JsonObject meshObj) return;

		string mName = meshObj["name"]?.GetValue<string>() ?? string.Empty;
		if (mName.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase))
		{
			meshObj["name"] = mName.Substring(0, mName.Length - 5);
		}
		else if (mName.EndsWith("LOD0", StringComparison.OrdinalIgnoreCase))
		{
			meshObj["name"] = mName.Substring(0, mName.Length - 4);
		}

		if (meshObj["primitives"] is not JsonArray primitives || primitives.Count == 0) return;

		for (int p = 0; p < primitives.Count; p++)
		{
			ProcessPrimitive(primitives[p], accessors, bufferViews, binChunk, cosThreshold, newBinStream);
		}
	}

	private static void ProcessPrimitive(JsonNode? primNode, JsonArray accessors, JsonArray bufferViews, byte[] binChunk, float cosThreshold, MemoryStream newBinStream)
	{
		if (primNode is not JsonObject primObj) return;

		if (!ValidatePrimitiveAttributes(primObj)) return;

		var attributes = (JsonObject)primObj["attributes"]!;

		var positions = ExtractVector3Array(attributes["POSITION"]!.GetValue<int>(), accessors, bufferViews, binChunk);
		if (positions == null || positions.Length < 3) return;

		var indices = ExtractIndices(primObj, accessors, bufferViews, binChunk, positions.Length);
		if (indices == null || indices.Length < 3 || indices.Length % 3 != 0) return;

		ExtractPrimitiveAttributes(attributes, accessors, bufferViews, binChunk, positions.Length,
			out bool hasUv0, out Vector2[]? uvs0,
			out bool hasUv1, out Vector2[]? uvs1,
			out bool hasJoints0, out Vector4[]? joints0,
			out bool hasWeights0, out Vector4[]? weights0,
			out bool hasColor0, out Vector4[]? colors0,
			out bool hasTangents);

		int triangleCount = indices.Length / 3;

		var cornerNormals = CalculateNormalsAndWeights(triangleCount, indices, positions, cosThreshold);

		WeldVertices(triangleCount, indices, positions, cornerNormals,
			hasUv0, uvs0, hasUv1, uvs1, hasJoints0, joints0, hasWeights0, weights0, hasColor0, colors0,
			out var weldedVertices, out var weldedIndices);

		if (weldedVertices.Count == 0 || weldedIndices.Count < 3) return;

		FinalizePrimitive(weldedVertices, weldedIndices, hasTangents, hasUv0, primObj, hasUv1, hasJoints0, hasWeights0, hasColor0, newBinStream, bufferViews, accessors);
	}

	private static bool ValidatePrimitiveAttributes(JsonObject primObj)
	{
		if (primObj.TryGetPropertyValue("mode", out var modeVal) && modeVal != null && modeVal.GetValue<int>() != 4) return false;
		if (primObj.ContainsKey("extensions") && primObj["extensions"] != null) return false;
		if (primObj.ContainsKey("targets") && primObj["targets"] != null) return false;

		if (primObj["attributes"] is not JsonObject attributes) return false;
		if (!attributes.ContainsKey("POSITION")) return false;

		return true;
	}

	private static void ExtractPrimitiveAttributes(JsonObject attributes, JsonArray accessors, JsonArray bufferViews, byte[] binChunk, int positionsLength,
		out bool hasUv0, out Vector2[]? uvs0,
		out bool hasUv1, out Vector2[]? uvs1,
		out bool hasJoints0, out Vector4[]? joints0,
		out bool hasWeights0, out Vector4[]? weights0,
		out bool hasColor0, out Vector4[]? colors0,
		out bool hasTangents)
	{
		TryExtractVector2Attribute("TEXCOORD_0", attributes, accessors, bufferViews, binChunk, positionsLength, out hasUv0, out uvs0);
		TryExtractVector2Attribute("TEXCOORD_1", attributes, accessors, bufferViews, binChunk, positionsLength, out hasUv1, out uvs1);
		TryExtractVector4Attribute("JOINTS_0", attributes, accessors, bufferViews, binChunk, positionsLength, false, out hasJoints0, out joints0);
		TryExtractVector4Attribute("WEIGHTS_0", attributes, accessors, bufferViews, binChunk, positionsLength, true, out hasWeights0, out weights0);
		TryExtractVector4Attribute("COLOR_0", attributes, accessors, bufferViews, binChunk, positionsLength, true, out hasColor0, out colors0);

		hasTangents = attributes.ContainsKey("TANGENT");
	}

	private static void TryExtractVector2Attribute(string key, JsonObject attributes, JsonArray accessors, JsonArray bufferViews, byte[] binChunk, int positionsLength, out bool hasAttr, out Vector2[]? attrArray)
	{
		hasAttr = attributes.ContainsKey(key);
		attrArray = hasAttr ? ExtractVector2Array(attributes[key]!.GetValue<int>(), accessors, bufferViews, binChunk) : null;
		hasAttr = attrArray != null && attrArray.Length == positionsLength;
	}

	private static void TryExtractVector4Attribute(string key, JsonObject attributes, JsonArray accessors, JsonArray bufferViews, byte[] binChunk, int positionsLength, bool isNormalized, out bool hasAttr, out Vector4[]? attrArray)
	{
		hasAttr = attributes.ContainsKey(key);
		attrArray = hasAttr ? ExtractVector4Array(attributes[key]!.GetValue<int>(), accessors, bufferViews, binChunk, isNormalized) : null;
		hasAttr = attrArray != null && attrArray.Length == positionsLength;
	}
}
