using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>validating positions and reading the original index buffer.</summary>
public static unsafe partial class GlbLodGenerator
	{
	private static (int posByteOffset, int posCount, int posStride)? ValidateAndCreatePositionData(JsonObject posAcc, JsonObject posBv, byte[] binBytes)
	{
		int posByteOffset = (posAcc["byteOffset"]?.GetValue<int>() ?? 0) + (posBv["byteOffset"]?.GetValue<int>() ?? 0);
		int posCount = posAcc["count"]?.GetValue<int>() ?? 0;
		int posStride = posBv["byteStride"]?.GetValue<int>() ?? 12;

		if (!IsValidPositionMetadata(posByteOffset, posCount, posStride, binBytes.Length)) return null;

		return (posByteOffset, posCount, posStride);
	}

	private static bool IsValidPositionMetadata(int posByteOffset, int posCount, int posStride, int binBytesLength)
	{
		if (posByteOffset < 0) return false;
		if (posCount < 3) return false;
		if (posStride < 12) return false;
		if ((posStride % 4) != 0) return false;
		if (posStride > 256) return false;

		long endOffset = (long)posByteOffset + ((long)posCount * posStride);
		if (endOffset > binBytesLength) return false;

		return true;
	}

	private static uint[]? GetOriginalIndices(JsonObject primObj, JsonObject root, byte[] binBytes, int posCount)
	{
		if (!primObj.ContainsKey("indices"))
			return GenerateSequentialIndices(posCount);

		var accessors = root["accessors"] as JsonArray;
		var bufferViews = root["bufferViews"] as JsonArray;
		if (accessors == null || bufferViews == null) return null;

		var indAcc = GetIndicesAccessor(primObj, accessors);
		if (indAcc == null) return null;

		var indBv = GetValidBufferView(indAcc, bufferViews);
		if (indBv == null) return null;

		return ValidateAndExtractIndices(indAcc, indBv, binBytes);
	}

	private static uint[]? GenerateSequentialIndices(int posCount)
	{
		if (posCount % 3 != 0) return null;
		uint[] indices = new uint[posCount];
		for (uint i = 0; i < posCount; i++) indices[i] = i;
		return indices;
	}

	private static JsonObject? GetIndicesAccessor(JsonObject primObj, JsonArray accessors)
	{
		int indAccIdx = primObj["indices"]!.GetValue<int>();
		if (indAccIdx < 0 || indAccIdx >= accessors.Count) return null;

		if (accessors[indAccIdx] is not JsonObject indAcc || indAcc.ContainsKey("sparse")) return null;
		if ((indAcc["type"]?.GetValue<string>() ?? string.Empty) != "SCALAR") return null;

		return indAcc;
	}

	private static uint[]? ValidateAndExtractIndices(JsonObject indAcc, JsonObject indBv, byte[] binBytes)
	{
		int indByteOffset = (indAcc["byteOffset"]?.GetValue<int>() ?? 0) + (indBv["byteOffset"]?.GetValue<int>() ?? 0);
		int indCount = indAcc["count"]?.GetValue<int>() ?? 0;
		int compType = indAcc["componentType"]?.GetValue<int>() ?? 0;

		if (!IsValidIndicesMetadata(indByteOffset, indCount, compType, binBytes.Length)) return null;

		return ExtractIndicesFromBytes(binBytes, indByteOffset, indCount, compType);
	}

	private static bool IsValidIndicesMetadata(int indByteOffset, int indCount, int compType, int binBytesLength)
	{
		if (indByteOffset < 0) return false;
		if (indCount < 3) return false;
		if ((indCount % 3) != 0) return false;

		int elemSize = compType switch { 5121 => 1, 5123 => 2, 5125 => 4, _ => 0 };
		if (elemSize == 0) return false;

		long endOffset = (long)indByteOffset + ((long)indCount * elemSize);
		if (endOffset > binBytesLength) return false;

		return true;
	}

	private static uint[]? ExtractIndicesFromBytes(byte[] binBytes, int offset, int count, int compType)
	{
		uint[] indices = new uint[count];
		if (compType == 5123)
		{
			for (int i = 0; i < count; i++) indices[i] = BitConverter.ToUInt16(binBytes, offset + (i * 2));
		}
		else if (compType == 5125)
		{
			for (int i = 0; i < count; i++) indices[i] = BitConverter.ToUInt32(binBytes, offset + (i * 4));
		}
		else if (compType == 5121)
		{
			for (int i = 0; i < count; i++) indices[i] = binBytes[offset + i];
		}
		else return null;

		return indices;
	}

	private static bool HasInvalidIndex(uint[] indices, int posCount)
	{
		for (int i = 0; i < indices.Length; i++)
		{
			if (indices[i] >= (uint)posCount) return true;
		}

		return false;
	}
}
