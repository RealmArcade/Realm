using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>the vertex streams a primitive is read through.</summary>
public static unsafe partial class GlbLodGenerator
	{
	private readonly record struct AttributeStream(int ByteOffset, int ByteStride, int ElementSize);

	private readonly record struct TexCoordStream(int ByteOffset, int ByteStride, int ComponentType, bool Normalized);

	private readonly record struct VertexPositionKey(int PositionX, int PositionY, int PositionZ, ulong OtherAttributesHash);

	private readonly record struct PrimitiveGeometry(
		int PositionByteOffset,
		int PositionCount,
		int PositionByteStride,
		bool HasTexCoords,
		TexCoordStream TexCoords,
		AttributeStream[] OtherAttributes);

	private static PrimitiveGeometry? GetGeometry(JsonObject primObj, JsonObject root, byte[] binBytes)
	{
		var accessors = root["accessors"] as JsonArray;
		var bufferViews = root["bufferViews"] as JsonArray;
		if (accessors == null || bufferViews == null) return null;

		var posAcc = GetValidAccessor(primObj, accessors, "POSITION", "VEC3", 5126);
		if (posAcc == null) return null;

		var posBv = GetValidBufferView(posAcc, bufferViews);
		if (posBv == null) return null;

		var positions = ValidateAndCreatePositionData(posAcc, posBv, binBytes);
		if (positions == null) return null;

		if (!TryGetTexCoordStream(primObj, accessors, bufferViews, positions.Value.posCount, binBytes, out TexCoordStream texCoords, out bool hasTexCoords)) return null;

		var otherAttributes = CollectOtherAttributes(primObj, accessors, bufferViews, positions.Value.posCount, binBytes);
		if (otherAttributes == null) return null;

		return new PrimitiveGeometry(
			positions.Value.posByteOffset,
			positions.Value.posCount,
			positions.Value.posStride,
			hasTexCoords,
			texCoords,
			otherAttributes);
	}

	private static bool TryGetTexCoordStream(JsonObject primObj, JsonArray accessors, JsonArray bufferViews, int vertexCount, byte[] binBytes, out TexCoordStream texCoords, out bool hasTexCoords)
	{
		texCoords = default;
		hasTexCoords = false;

		int accessorIndex = GetAttributeIndex(primObj, "TEXCOORD_0");
		if (accessorIndex < 0) return true;
		if (accessorIndex >= accessors.Count) return false;
		if (accessors[accessorIndex] is not JsonObject accessor) return false;
		if (accessor.ContainsKey("sparse")) return false;
		if ((accessor["type"]?.GetValue<string>() ?? string.Empty) != "VEC2") return false;

		int componentType = accessor["componentType"]?.GetValue<int>() ?? 0;
		if (componentType != 5121 && componentType != 5122 && componentType != 5123 && componentType != 5126) return false;

		int accessorCount = accessor["count"]?.GetValue<int>() ?? 0;
		if (accessorCount != vertexCount) return false;

		if (GetValidBufferView(accessor, bufferViews) is not JsonObject bufferView) return false;

		int byteOffset = (accessor["byteOffset"]?.GetValue<int>() ?? 0) + (bufferView["byteOffset"]?.GetValue<int>() ?? 0);
		int byteStride = bufferView["byteStride"]?.GetValue<int>() ?? GetElementByteSize(accessor);
		if (!IsValidAttributeStream(byteOffset, byteStride, vertexCount, binBytes.Length)) return false;

		texCoords = new TexCoordStream(byteOffset, byteStride, componentType, accessor["normalized"]?.GetValue<bool>() ?? false);
		hasTexCoords = true;
		return true;
	}

	private static AttributeStream[]? CollectOtherAttributes(JsonObject primObj, JsonArray accessors, JsonArray bufferViews, int vertexCount, byte[] binBytes)
	{
		if (primObj["attributes"] is not JsonObject attributes) return null;

		var streams = new List<AttributeStream>(attributes.Count);
		foreach (var attribute in attributes)
		{
			if (attribute.Key is "POSITION" or "NORMAL" or "TANGENT" or "TEXCOORD_0") continue;

			if (!TryCreateAttributeStream(accessors, bufferViews, attribute.Value, vertexCount, binBytes, out AttributeStream stream, out bool isConstant)) return null;
			if (!isConstant) streams.Add(stream);
		}

		return streams.ToArray();
	}

	private static bool TryCreateAttributeStream(JsonArray accessors, JsonArray bufferViews, JsonNode? attributeValue, int vertexCount, byte[] binBytes, out AttributeStream stream, out bool isConstant)
	{
		stream = default;
		isConstant = false;

		int accessorIndex = attributeValue?.GetValue<int>() ?? -1;
		if (accessorIndex < 0 || accessorIndex >= accessors.Count) return false;
		if (accessors[accessorIndex] is not JsonObject accessor) return false;
		if (accessor.ContainsKey("sparse")) return false;

		if (!accessor.ContainsKey("bufferView"))
		{
			isConstant = true;
			return true;
		}

		if ((accessor["count"]?.GetValue<int>() ?? 0) != vertexCount) return false;

		if (GetValidBufferView(accessor, bufferViews) is not JsonObject bufferView) return false;

		int elementSize = GetElementByteSize(accessor);
		if (elementSize <= 0) return false;

		int byteOffset = (accessor["byteOffset"]?.GetValue<int>() ?? 0) + (bufferView["byteOffset"]?.GetValue<int>() ?? 0);
		int byteStride = bufferView["byteStride"]?.GetValue<int>() ?? elementSize;
		if (!IsValidAttributeStream(byteOffset, byteStride, vertexCount, binBytes.Length)) return false;

		stream = new AttributeStream(byteOffset, byteStride, elementSize);
		return true;
	}

	private static int GetElementByteSize(JsonObject accessor)
	{
		int componentSize = (accessor["componentType"]?.GetValue<int>() ?? 0) switch
		{
			5120 or 5121 => 1,
			5122 or 5123 => 2,
			5125 or 5126 => 4,
			_ => 0
		};

		int componentCount = (accessor["type"]?.GetValue<string>() ?? string.Empty) switch
		{
			"SCALAR" => 1,
			"VEC2" => 2,
			"VEC3" => 3,
			"VEC4" => 4,
			"MAT2" => 4,
			"MAT3" => 9,
			"MAT4" => 16,
			_ => 0
		};

		return componentSize * componentCount;
	}

	private static bool IsValidAttributeStream(int byteOffset, int byteStride, int vertexCount, int binBytesLength)
	{
		if (byteOffset < 0) return false;
		if (byteStride <= 0 || byteStride > 256) return false;

		long endOffset = (long)byteOffset + ((long)vertexCount * byteStride);
		return endOffset <= binBytesLength;
	}

	private static JsonObject? GetValidAccessor(JsonObject primObj, JsonArray accessors, string attributeKey, string expectedType, int expectedCompType)
	{
		int accIdx = GetAttributeIndex(primObj, attributeKey);
		if (accIdx < 0 || accIdx >= accessors.Count) return null;

		if (accessors[accIdx] is not JsonObject acc) return null;
		if (acc.ContainsKey("sparse")) return null;

		if (!IsValidAccessorFormat(acc, expectedType, expectedCompType)) return null;

		return acc;
	}

	private static int GetAttributeIndex(JsonObject primObj, string attributeKey)
	{
		if (primObj["attributes"] is not JsonObject attributes) return -1;
		if (!attributes.ContainsKey(attributeKey)) return -1;
		return attributes[attributeKey]!.GetValue<int>();
	}

	private static bool IsValidAccessorFormat(JsonObject acc, string expectedType, int expectedCompType)
	{
		string typeStr = acc["type"]?.GetValue<string>() ?? string.Empty;
		if (typeStr != expectedType) return false;

		int compType = acc["componentType"]?.GetValue<int>() ?? 0;
		if (compType != expectedCompType) return false;

		return true;
	}

	private static JsonObject? GetValidBufferView(JsonObject accessor, JsonArray bufferViews)
	{
		int bvIdx = accessor["bufferView"]?.GetValue<int>() ?? -1;
		if (bvIdx < 0 || bvIdx >= bufferViews.Count) return null;
		return bufferViews[bvIdx] as JsonObject;
	}
}
