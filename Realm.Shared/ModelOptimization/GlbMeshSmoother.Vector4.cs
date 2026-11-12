using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>reading vec4 accessor arrays and single components.</summary>
public static unsafe partial class GlbMeshSmoother
	{
	private static Vector4[]? ExtractVector4Array(int accessorIndex, JsonArray accessors, JsonArray bufferViews, byte[] binChunk, bool isNormalized)
	{
		if (!TryGetAccessorData(accessorIndex, accessors, bufferViews, out int count, out int compType, out int byteOffset, out int stride, out _)) return null;

		if (!TryGetAccessorType(accessors[accessorIndex] as JsonObject, out string type)) return null;
		if (type != "VEC4" && type != "VEC3") return null;

		int numComponents = type == "VEC4" ? 4 : 3;
		int bytesPerComp = GetBytesPerComponent(compType);
		if (bytesPerComp == 0) return null;

		int elemSize = numComponents * bytesPerComp;
		stride = Math.Max(stride, elemSize);

		long endOffset = (long)byteOffset + ((long)count * stride) - (stride - elemSize);
		if (endOffset > binChunk.Length) return null;

		return ParseVector4Array(count, stride, byteOffset, numComponents, bytesPerComp, compType, isNormalized, binChunk);
	}

	private static bool TryGetAccessorType(JsonObject? aObj, out string type)
	{
		type = string.Empty;
		if (aObj != null && aObj.TryGetPropertyValue("type", out var typeVal) && typeVal != null)
		{
			type = typeVal.GetValue<string>();
			return true;
		}

		return false;
	}

	private static int GetBytesPerComponent(int compType)
	{
		return compType switch { 5126 => 4, 5125 => 4, 5123 => 2, 5121 => 1, _ => 0 };
	}

	private static Vector4[] ParseVector4Array(int count, int stride, int byteOffset, int numComponents, int bytesPerComp, int compType, bool isNormalized, byte[] binChunk)
	{
		var result = new Vector4[count];
		for (int i = 0; i < count; i++)
		{
			int offset = byteOffset + (i * stride);
			result[i] = ParseVector4(binChunk, offset, numComponents, bytesPerComp, compType, isNormalized);
		}

		return result;
	}

	private static Vector4 ParseVector4(byte[] binChunk, int offset, int numComponents, int bytesPerComp, int compType, bool isNormalized)
	{
		float c0 = ParseComponent(binChunk, offset, compType, isNormalized);
		float c1 = numComponents > 1 ? ParseComponent(binChunk, offset + bytesPerComp, compType, isNormalized) : 0f;
		float c2 = numComponents > 2 ? ParseComponent(binChunk, offset + (2 * bytesPerComp), compType, isNormalized) : 0f;
		float c3 = numComponents > 3 ? ParseComponent(binChunk, offset + (3 * bytesPerComp), compType, isNormalized) : 1f;

		return new Vector4(c0, c1, c2, c3);
	}

	private static float ParseComponent(byte[] binChunk, int cOff, int compType, bool isNormalized)
	{
		return compType switch
		{
			5126 => BitConverter.ToSingle(binChunk, cOff),
			5125 => BitConverter.ToUInt32(binChunk, cOff),
			5123 => isNormalized ? (BitConverter.ToUInt16(binChunk, cOff) / 65535.0f) : BitConverter.ToUInt16(binChunk, cOff),
			5121 => isNormalized ? (binChunk[cOff] / 255.0f) : binChunk[cOff],
			_ => 0f
		};
	}
}
