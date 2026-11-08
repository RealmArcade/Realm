using System.Numerics;
using System.Text.Json.Nodes;
using Assimp;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>extracting face uvs and positions.</summary>
public static partial class GlbPlayerColorProcessor
	{
	private static void AppendIndexedFaces<T>(List<(T, T, T)> result, List<T> data, List<int> indices)
	{
		for (int i = 0; i + 2 < indices.Count; i += 3)
		{
			int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
			if (i0 < data.Count && i1 < data.Count && i2 < data.Count)
			{
				result.Add((data[i0], data[i1], data[i2]));
			}
		}
	}

	private static void AppendSequentialFaces<T>(List<(T, T, T)> result, List<T> data)
	{
		for (int i = 0; i + 2 < data.Count; i += 3)
		{
			result.Add((data[i], data[i + 1], data[i + 2]));
		}
	}

	private static List<(Vector2 UV0, Vector2 UV1, Vector2 UV2)> ExtractFaceUvData(
		JsonObject primObj,
		JsonArray accessors,
		JsonArray bufferViews,
		byte[] bin)
	{
		var result = new List<(Vector2, Vector2, Vector2)>();

		int uvAccessorIndex = GlbAccessorReader.GetAttributeIndex(primObj, "TEXCOORD_0");
		if (uvAccessorIndex < 0 || uvAccessorIndex >= accessors.Count) return result;

		var uvData = GlbAccessorReader.ReadAccessorVec2(accessors, bufferViews, bin, uvAccessorIndex);
		if (uvData.Count == 0) return result;

		int indexAccessorIndex = primObj["indices"]?.GetValue<int>() ?? -1;

		if (indexAccessorIndex >= 0 && indexAccessorIndex < accessors.Count)
		{
			var indices = GlbAccessorReader.ReadAccessorIndices(accessors, bufferViews, bin, indexAccessorIndex);
			AppendIndexedFaces(result, uvData, indices);
		}
		else
		{
			AppendSequentialFaces(result, uvData);
		}

		return result;
	}

	private static List<(Vector3 Pos0, Vector3 Pos1, Vector3 Pos2)> ExtractFacePositionData(
		JsonObject primObj,
		JsonArray accessors,
		JsonArray bufferViews,
		byte[] bin)
	{
		var result = new List<(Vector3, Vector3, Vector3)>();

		int posAccessorIndex = GlbAccessorReader.GetAttributeIndex(primObj, "POSITION");
		if (posAccessorIndex < 0 || posAccessorIndex >= accessors.Count) return result;

		var posData = GlbAccessorReader.ReadAccessorVec3(accessors, bufferViews, bin, posAccessorIndex);
		if (posData.Count == 0) return result;

		int indexAccessorIndex = primObj["indices"]?.GetValue<int>() ?? -1;

		if (indexAccessorIndex >= 0 && indexAccessorIndex < accessors.Count)
		{
			var indices = GlbAccessorReader.ReadAccessorIndices(accessors, bufferViews, bin, indexAccessorIndex);
			AppendIndexedFaces(result, posData, indices);
		}
		else
		{
			AppendSequentialFaces(result, posData);
		}

		return result;
	}
}
