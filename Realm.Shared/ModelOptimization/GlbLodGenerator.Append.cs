using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>appending the simplified primitive and its index stream.</summary>
public static unsafe partial class GlbLodGenerator
	{
	private static void AppendSimplifiedPrimitive(JsonObject primObj, uint[] simplifiedIndices, JsonObject root, MemoryStream newBinStream, JsonArray newPrimitives)
	{
		AlignStream(newBinStream);

		uint maxIdx = 0;
		for (int i = 0; i < simplifiedIndices.Length; i++)
			if (simplifiedIndices[i] > maxIdx)
				maxIdx = simplifiedIndices[i];

		int newIndByteOffset = (int)newBinStream.Position;
		bool useShort = maxIdx <= 65535;
		int newIndByteLength = simplifiedIndices.Length * (useShort ? 2 : 4);

		WriteIndicesToStream(simplifiedIndices, useShort, newBinStream);

		var bufferViews = root["bufferViews"] as JsonArray;
		var accessors = root["accessors"] as JsonArray;
		if (bufferViews == null || accessors == null) return;

		int newBvIdx = bufferViews.Count;
		var newBv = new JsonObject();
		newBv["buffer"] = 0;
		newBv["byteOffset"] = newIndByteOffset;
		newBv["byteLength"] = newIndByteLength;
		newBv["target"] = 34963;
		bufferViews.Add(newBv);

		int newAccIdx = accessors.Count;
		var newAcc = new JsonObject();
		newAcc["bufferView"] = newBvIdx;
		newAcc["byteOffset"] = 0;
		newAcc["componentType"] = useShort ? 5123 : 5125;
		newAcc["count"] = simplifiedIndices.Length;
		newAcc["type"] = "SCALAR";
		accessors.Add(newAcc);

		var newPrim = new JsonObject
		{
			["attributes"] = primObj["attributes"]!.DeepClone(),
			["indices"] = newAccIdx
		};
		if (primObj.ContainsKey("material"))
			newPrim["material"] = primObj["material"]!.GetValue<int>();

		newPrimitives.Add(newPrim);
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
