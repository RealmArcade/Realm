using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>reading index accessors and the accessor metadata helpers.</summary>
public static unsafe partial class GlbMeshSmoother
	{
	private static uint[]? ExtractIndices(JsonObject primObj, JsonArray accessors, JsonArray bufferViews, byte[] binChunk, int vertexCount)
	{
		if (!primObj.ContainsKey("indices") || primObj["indices"] == null)
		{
			return InitializeSequentialIndices(vertexCount);
		}

		int accIdx = primObj["indices"]!.GetValue<int>();
		if (!TryGetAccessorData(accIdx, accessors, bufferViews, out int count, out int compType, out int byteOffset, out _, out _)) return null;

		if (count < 3 || count % 3 != 0) return null;

		int elemSize = GetIndexElementSize(compType);
		if (elemSize == 0 || (long)byteOffset + ((long)count * elemSize) > binChunk.Length) return null;

		var indices = new uint[count];
		for (int i = 0; i < count; i++)
		{
			indices[i] = ParseIndex(binChunk, byteOffset, i, compType);
		}

		return indices;
	}

	private static uint[]? InitializeSequentialIndices(int vertexCount)
	{
		if (vertexCount < 3 || vertexCount % 3 != 0) return null;
		var sequentialIndices = new uint[vertexCount];
		for (uint i = 0; i < vertexCount; i++) sequentialIndices[i] = i;
		return sequentialIndices;
	}

	private static int GetIndexElementSize(int compType)
	{
		return compType switch { 5121 => 1, 5123 => 2, 5125 => 4, _ => 0 };
	}

	private static uint ParseIndex(byte[] binChunk, int byteOffset, int i, int compType)
	{
		return compType switch
		{
			5121 => binChunk[byteOffset + i],
			5123 => BitConverter.ToUInt16(binChunk, byteOffset + (i * 2)),
			5125 => BitConverter.ToUInt32(binChunk, byteOffset + (i * 4)),
			_ => 0
		};
	}

	private static bool TryGetAccessorData(int accessorIndex, JsonArray accessors, JsonArray bufferViews, out int count, out int compType, out int byteOffset, out int stride, out JsonObject bv)
	{
		count = 0;
		compType = 0;
		byteOffset = 0;
		stride = 0;
		bv = null!;

		if (accessorIndex < 0 || accessorIndex >= accessors.Count) return false;
		if (accessors[accessorIndex] is not JsonObject acc) return false;
		if (acc.ContainsKey("sparse")) return false;

		TryGetIntProperty(acc, "componentType", out compType);
		TryGetIntProperty(acc, "count", out count);
		if (count <= 0) return false;

		if (!TryGetBufferView(acc, bufferViews, out bv)) return false;

		TryGetIntProperty(acc, "byteOffset", out int accOffset);
		TryGetIntProperty(bv, "byteOffset", out int bvOffset);
		byteOffset = accOffset + bvOffset;

		TryGetIntProperty(bv, "byteStride", out stride);

		return true;
	}

	private static bool TryGetIntProperty(JsonObject? obj, string propertyName, out int value)
	{
		value = 0;
		if (obj != null && obj.TryGetPropertyValue(propertyName, out var valNode) && valNode != null)
		{
			value = valNode.GetValue<int>();
			return true;
		}

		return false;
	}

	private static bool TryGetBufferView(JsonObject acc, JsonArray bufferViews, out JsonObject bv)
	{
		bv = null!;

		if (!acc.TryGetPropertyValue("bufferView", out var bvIdxNode) || bvIdxNode == null) return false;
		int bvIdx = bvIdxNode.GetValue<int>();

		if (bvIdx < 0 || bvIdx >= bufferViews.Count) return false;
		if (bufferViews[bvIdx] is not JsonObject bvObj) return false;

		bv = bvObj;
		return true;
	}
}
