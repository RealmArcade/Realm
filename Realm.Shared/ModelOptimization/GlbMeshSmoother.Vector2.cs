using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>reading vec3 and vec2 accessor arrays.</summary>
public static unsafe partial class GlbMeshSmoother
	{
	private static Vector3[]? ExtractVector3Array(int accessorIndex, JsonArray accessors, JsonArray bufferViews, byte[] binChunk)
	{
		if (!TryGetAccessorData(accessorIndex, accessors, bufferViews, out int count, out int compType, out int byteOffset, out int stride, out _)) return null;

		string type = accessors[accessorIndex]!["type"]?.GetValue<string>() ?? string.Empty;
		if (type != "VEC3" || compType != 5126) return null;

		if (stride < 12) stride = 12;
		if ((long)byteOffset + ((long)count * stride) - (stride - 12) > binChunk.Length) return null;

		var result = new Vector3[count];
		for (int i = 0; i < count; i++)
		{
			int offset = byteOffset + (i * stride);
			result[i] = new Vector3(
				BitConverter.ToSingle(binChunk, offset),
				BitConverter.ToSingle(binChunk, offset + 4),
				BitConverter.ToSingle(binChunk, offset + 8));
		}

		return result;
	}

	private static Vector2[]? ExtractVector2Array(int accessorIndex, JsonArray accessors, JsonArray bufferViews, byte[] binChunk)
	{
		if (!TryGetAccessorData(accessorIndex, accessors, bufferViews, out int count, out int compType, out int byteOffset, out int stride, out _)) return null;

		string type = string.Empty;
		if (accessors[accessorIndex] is JsonObject aObj && aObj.TryGetPropertyValue("type", out var typeVal) && typeVal != null)
			type = typeVal.GetValue<string>();

		if (type != "VEC2") return null;

		int elemSize = GetVector2ElementSize(compType);
		if (elemSize == 0) return null;

		if (stride < elemSize) stride = elemSize;
		if ((long)byteOffset + ((long)count * stride) - (stride - elemSize) > binChunk.Length) return null;

		return ParseVector2Array(count, stride, byteOffset, compType, binChunk);
	}

	private static int GetVector2ElementSize(int compType)
	{
		return compType switch { 5126 => 8, 5123 => 4, 5121 => 2, _ => 0 };
	}

	private static Vector2[] ParseVector2Array(int count, int stride, int byteOffset, int compType, byte[] binChunk)
	{
		var result = new Vector2[count];
		for (int i = 0; i < count; i++)
		{
			int offset = byteOffset + (i * stride);
			result[i] = ParseVector2(binChunk, offset, compType);
		}

		return result;
	}

	private static Vector2 ParseVector2(byte[] binChunk, int offset, int compType)
	{
		return compType switch
		{
			5126 => new Vector2(
				BitConverter.ToSingle(binChunk, offset),
				BitConverter.ToSingle(binChunk, offset + 4)),
			5123 => new Vector2(
				BitConverter.ToUInt16(binChunk, offset) / 65535.0f,
				BitConverter.ToUInt16(binChunk, offset + 2) / 65535.0f),
			5121 => new Vector2(
				binChunk[offset] / 255.0f,
				binChunk[offset + 1] / 255.0f),
			_ => Vector2.Zero
		};
	}
}
