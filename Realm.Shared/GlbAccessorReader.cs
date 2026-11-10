using System.Numerics;
using System.Text.Json.Nodes;
using Assimp;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

public static class GlbAccessorReader
{
	public static int GetAttributeIndex(JsonObject primObj, string attributeName)
	{
		if (primObj["attributes"] is not JsonObject attributes) return -1;
		if (!attributes.TryGetPropertyValue(attributeName, out var val)) return -1;
		return val?.GetValue<int>() ?? -1;
	}

	public static (bool Valid, int Count, int BaseOffset, int Stride, int ComponentType) GetAccessorBufferInfo(JsonArray accessors, JsonArray bufferViews, int accessorIndex, int defaultElementSize)
	{
		if (accessorIndex < 0 || accessorIndex >= accessors.Count) return (false, 0, 0, 0, 0);
		if (accessors[accessorIndex] is not JsonObject acc) return (false, 0, 0, 0, 0);

		int bvIdx = GlbTextureResolver.GetJsonNodeIntValue(acc, "bufferView", -1);
		int count = GlbTextureResolver.GetJsonNodeIntValue(acc, "count", 0);
		int accessorByteOffset = GlbTextureResolver.GetJsonNodeIntValue(acc, "byteOffset", 0);
		int componentType = GlbTextureResolver.GetJsonNodeIntValue(acc, "componentType", 5126);

		if (bvIdx < 0 || bvIdx >= bufferViews.Count) return (false, 0, 0, 0, 0);
		if (bufferViews[bvIdx] is not JsonObject bv) return (false, 0, 0, 0, 0);

		int bvByteOffset = GlbTextureResolver.GetJsonNodeIntValue(bv, "byteOffset", 0);
		int bvStride = GlbTextureResolver.GetJsonNodeIntValue(bv, "byteStride", 0);

		int stride = bvStride > 0 ? bvStride : defaultElementSize;
		int baseOffset = bvByteOffset + accessorByteOffset;

		return (true, count, baseOffset, stride, componentType);
	}

	public static List<Vector2> ReadAccessorVec2(JsonArray accessors, JsonArray bufferViews, byte[] bin, int accessorIndex)
	{
		var result = new List<Vector2>();
		var (valid, count, baseOffset, stride, componentType) = GetAccessorBufferInfo(accessors, bufferViews, accessorIndex, 8);
		if (!valid) return result;

		int elementSize = componentType == 5126 ? 8 : 4;
		stride = stride == 8 && elementSize == 4 ? 4 : stride;

		for (int i = 0; i < count; i++)
		{
			int offset = baseOffset + i * stride;
			if (offset + elementSize > bin.Length) break;

			float u, v;
			if (componentType == 5126)
			{
				u = BitConverter.ToSingle(bin, offset);
				v = BitConverter.ToSingle(bin, offset + 4);
			}
			else
			{
				u = BitConverter.ToUInt16(bin, offset) / 65535f;
				v = BitConverter.ToUInt16(bin, offset + 2) / 65535f;
			}

			result.Add(new Vector2(Math.Clamp(u, 0f, 1f), Math.Clamp(v, 0f, 1f)));
		}

		return result;
	}

	public static List<Vector3> ReadAccessorVec3(JsonArray accessors, JsonArray bufferViews, byte[] bin, int accessorIndex)
	{
		var result = new List<Vector3>();
		var (valid, count, baseOffset, stride, _) = GetAccessorBufferInfo(accessors, bufferViews, accessorIndex, 12);
		if (!valid) return result;

		for (int i = 0; i < count; i++)
		{
			int offset = baseOffset + i * stride;
			if (offset + 12 > bin.Length) break;

			float x = BitConverter.ToSingle(bin, offset);
			float y = BitConverter.ToSingle(bin, offset + 4);
			float z = BitConverter.ToSingle(bin, offset + 8);
			result.Add(new Vector3(x, y, z));
		}

		return result;
	}

	public static List<int> ReadAccessorIndices(JsonArray accessors, JsonArray bufferViews, byte[] bin, int accessorIndex)
	{
		var result = new List<int>();
		var (valid, count, baseOffset, _, componentType) = GetAccessorBufferInfo(accessors, bufferViews, accessorIndex, 4);
		if (!valid) return result;

		int elementSize = componentType switch
		{
			5121 => 1,
			5123 => 2,
			_ => 4
		};

		for (int i = 0; i < count; i++)
		{
			int offset = baseOffset + i * elementSize;
			if (offset + elementSize > bin.Length) break;

			int index = componentType switch
			{
				5121 => bin[offset],
				5123 => BitConverter.ToUInt16(bin, offset),
				_ => (int)BitConverter.ToUInt32(bin, offset)
			};

			result.Add(index);
		}

		return result;
	}

	public static bool TryGetMeshArrays(JsonObject root, out JsonArray meshes, out JsonArray accessors)
	{
		meshes = root["meshes"] as JsonArray ?? new JsonArray();
		accessors = root["accessors"] as JsonArray ?? new JsonArray();
		return root.ContainsKey("meshes") && root.ContainsKey("accessors");
	}

	public static bool TryGetBaseGlbArrays(JsonObject root, out JsonArray textures, out JsonArray materials, out JsonArray images, out JsonArray bufferViews)
	{
		textures = root["textures"] as JsonArray ?? new JsonArray();
		materials = root["materials"] as JsonArray ?? new JsonArray();
		images = root["images"] as JsonArray ?? new JsonArray();
		bufferViews = root["bufferViews"] as JsonArray ?? new JsonArray();

		return root.ContainsKey("textures") && root.ContainsKey("materials") && root.ContainsKey("images") && root.ContainsKey("bufferViews");
	}
}
