using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>appending the simplified primitive and its index stream.</summary>
public static unsafe partial class GlbLodGenerator
	{
	private static bool TryAppendCompactedPrimitive(JsonObject primObj, uint[] simplifiedIndices, PrimitiveGeometry geometry, JsonObject root, byte[] binBytes, MemoryStream newBinStream, JsonArray newPrimitives)
	{
		if (primObj["attributes"] is not JsonObject attributes) return false;
		if (simplifiedIndices.Length < 3) return false;

		var bufferViews = root["bufferViews"] as JsonArray;
		var accessors = root["accessors"] as JsonArray;
		if (bufferViews == null || accessors == null) return false;

		int[] vertexRemap = BuildUsedVertexRemap(simplifiedIndices, geometry.PositionCount, out int compactVertexCount);
		if (compactVertexCount <= 0) return false;

		uint[] compactIndices = RemapIndices(simplifiedIndices, vertexRemap);

		var compactAttributes = new JsonObject();
		foreach (var attribute in attributes)
		{
			if (!TryWriteCompactedAttribute(attribute.Key, attribute.Value, vertexRemap, compactVertexCount, bufferViews.Count, bufferViews, accessors, binBytes, newBinStream, compactAttributes))
				return false;
		}

		var newPrim = new JsonObject
		{
			["attributes"] = compactAttributes,
			["indices"] = AppendCompactedIndices(compactIndices, newBinStream, bufferViews, accessors)
		};
		if (primObj.ContainsKey("material"))
			newPrim["material"] = primObj["material"]!.GetValue<int>();

		newPrimitives.Add(newPrim);
		return true;
	}

	private static void WriteIndicesToStream(uint[] indices, bool useShort, MemoryStream stream)
	{
		for (int i = 0; i < indices.Length; i++)
		{
			WriteIndexToStream(indices[i], useShort, stream);
		}
	}

	private static void WriteIndexToStream(uint index, bool useShort, MemoryStream stream)
	{
		if (useShort)
			stream.Write(BitConverter.GetBytes((ushort)index), 0, 2);
		else
			stream.Write(BitConverter.GetBytes(index), 0, 4);
	}

	private static void AlignStream(MemoryStream stream)
	{
		while ((stream.Position % 4) != 0) stream.WriteByte(0);
	}

	private static void UpdateBuffersLength(JsonObject root, int length)
	{
		if (root["buffers"] is JsonArray buffers && buffers.Count > 0 && buffers[0] is JsonObject buf0)
			buf0["byteLength"] = length;
	}
}
