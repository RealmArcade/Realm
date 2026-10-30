using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

public class UvChartReductionStatistics
{
	public int PrimitiveCount { get; set; }
	public int IslandCountBefore { get; set; }
	public int IslandCountAfter { get; set; }
	public int MergedIslandCount => IslandCountBefore - IslandCountAfter;
	public float TexCoordTolerance { get; set; }
	public bool Applied { get; set; }
}

public static class UvChartReducer
{
	public const float DefaultTexCoordTolerance = 1.0f / 2048.0f;
	public const float ExactTexCoordTolerance = 0.0001f;
	private const float PositionWeldEpsilon = 0.0001f;

	private readonly record struct AccessorLayout(int ByteOffset, int Count, int ByteStride, int ComponentType, int ComponentCount, bool Normalized);

	private readonly record struct PrimitiveData(AccessorLayout Positions, AccessorLayout TexCoords, AccessorLayout? Indices, byte[] Bin, int VertexCount);

	private readonly record struct ChartPositionKey(int PositionX, int PositionY, int PositionZ);

	public static UvChartReductionStatistics Analyze(byte[] glbBytes, float seamMergeTolerance)
	{
		float mergedTolerance = seamMergeTolerance > 0f ? seamMergeTolerance : ExactTexCoordTolerance;
		var statistics = new UvChartReductionStatistics
		{
			TexCoordTolerance = mergedTolerance,
			Applied = seamMergeTolerance > 0f
		};

		if (glbBytes == null || glbBytes.Length < 20) return statistics;

		var (jsonNode, binChunk, _) = GlbManifestUtils.ParseGlb(glbBytes);
		if (jsonNode is not JsonObject root || binChunk == null || binChunk.Length == 0) return statistics;
		if (root["meshes"] is not JsonArray meshes) return statistics;

		foreach (var meshNode in meshes)
		{
			if (meshNode is not JsonObject meshObj) continue;
			if (meshObj["primitives"] is not JsonArray primitives) continue;

			foreach (var primitiveNode in primitives)
			{
				if (primitiveNode is not JsonObject primitiveObj) continue;
				if (!TryReadPrimitive(root, primitiveObj, binChunk, out PrimitiveData data)) continue;

				statistics.PrimitiveCount++;
				statistics.IslandCountBefore += CountIslands(data, ExactTexCoordTolerance);
				statistics.IslandCountAfter += CountIslands(data, mergedTolerance);
			}
		}

		return statistics;
	}

	private static bool TryReadPrimitive(JsonObject root, JsonObject primitiveObj, byte[] binChunk, out PrimitiveData data)
	{
		data = default;

		if (primitiveObj.TryGetPropertyValue("mode", out var modeNode) && modeNode != null && modeNode.GetValue<int>() != 4) return false;
		if (primitiveObj["attributes"] is not JsonObject attributes) return false;

		if (!TryGetAccessor(root, attributes["POSITION"], binChunk, out AccessorLayout positions)) return false;
		if (positions.ComponentType != 5126 || positions.ComponentCount != 3) return false;
		if (!TryGetAccessor(root, attributes["TEXCOORD_0"], binChunk, out AccessorLayout texCoords)) return false;
		if (texCoords.ComponentCount != 2) return false;
		if (texCoords.Count != positions.Count) return false;

		AccessorLayout? indices = null;
		if (primitiveObj["indices"] is JsonNode indicesNode)
		{
			if (!TryGetAccessor(root, indicesNode, binChunk, out AccessorLayout indexLayout)) return false;
			if (indexLayout.ComponentType is not (5121 or 5123 or 5125)) return false;
			indices = indexLayout;
		}

		data = new PrimitiveData(positions, texCoords, indices, binChunk, positions.Count);
		return true;
	}

	private static bool TryGetAccessor(JsonObject root, JsonNode? accessorIndexNode, byte[] binChunk, out AccessorLayout layout)
	{
		layout = default;

		if (accessorIndexNode == null) return false;
		if (root["accessors"] is not JsonArray accessors) return false;
		if (root["bufferViews"] is not JsonArray bufferViews) return false;

		int accessorIndex = accessorIndexNode.GetValue<int>();
		if (accessorIndex < 0 || accessorIndex >= accessors.Count) return false;
		if (accessors[accessorIndex] is not JsonObject accessor) return false;
		if (accessor.ContainsKey("sparse")) return false;
		if (accessor["bufferView"] is not JsonNode bufferViewNode) return false;

		int bufferViewIndex = bufferViewNode.GetValue<int>();
		if (bufferViewIndex < 0 || bufferViewIndex >= bufferViews.Count) return false;
		if (bufferViews[bufferViewIndex] is not JsonObject bufferView) return false;

		int componentType = accessor["componentType"]?.GetValue<int>() ?? 0;
		int componentSize = GetComponentByteSize(componentType);
		if (componentSize <= 0) return false;

		int componentCount = (accessor["type"]?.GetValue<string>() ?? string.Empty) switch
		{
			"SCALAR" => 1,
			"VEC2" => 2,
			"VEC3" => 3,
			"VEC4" => 4,
			_ => 0
		};
		if (componentCount == 0) return false;

		int count = accessor["count"]?.GetValue<int>() ?? 0;
		if (count <= 0) return false;

		int elementSize = componentSize * componentCount;
		int byteStride = bufferView["byteStride"]?.GetValue<int>() ?? elementSize;
		if (byteStride < elementSize) return false;

		int byteOffset = (accessor["byteOffset"]?.GetValue<int>() ?? 0) + (bufferView["byteOffset"]?.GetValue<int>() ?? 0);
		if (byteOffset < 0) return false;
		if (byteOffset + (count - 1) * byteStride + elementSize > binChunk.Length) return false;

		layout = new AccessorLayout(byteOffset, count, byteStride, componentType, componentCount, accessor["normalized"]?.GetValue<bool>() ?? false);
		return true;
	}

	private static int GetComponentByteSize(int componentType)
	{
		return componentType switch
		{
			5120 or 5121 => 1,
			5122 or 5123 => 2,
			5125 or 5126 => 4,
			_ => 0
		};
	}

	private static float ReadComponent(byte[] bin, AccessorLayout layout, int elementIndex, int componentIndex)
	{
		int offset = layout.ByteOffset + elementIndex * layout.ByteStride + componentIndex * GetComponentByteSize(layout.ComponentType);

		return layout.ComponentType switch
		{
			5126 => BitConverter.ToSingle(bin, offset),
			5125 => BitConverter.ToUInt32(bin, offset),
			5123 => layout.Normalized ? BitConverter.ToUInt16(bin, offset) / 65535f : BitConverter.ToUInt16(bin, offset),
			5122 => layout.Normalized ? MathF.Max(BitConverter.ToInt16(bin, offset) / 32767f, -1f) : BitConverter.ToInt16(bin, offset),
			5121 => layout.Normalized ? bin[offset] / 255f : bin[offset],
			_ => (sbyte)bin[offset]
		};
	}

	private static int ReadIndex(PrimitiveData data, int indexPosition)
	{
		if (data.Indices is not AccessorLayout indexLayout) return indexPosition;

		int offset = indexLayout.ByteOffset + indexPosition * indexLayout.ByteStride;

		return indexLayout.ComponentType switch
		{
			5121 => data.Bin[offset],
			5123 => BitConverter.ToUInt16(data.Bin, offset),
			_ => (int)BitConverter.ToUInt32(data.Bin, offset)
		};
	}

	private static int CountIslands(PrimitiveData data, float texCoordTolerance)
	{
		var firstWeldedByPosition = new Dictionary<ChartPositionKey, int>(data.VertexCount);
		var extraWeldedByFirst = new Dictionary<int, List<int>>();
		var weldedTexCoords = new float[data.VertexCount * 2];
		var weldedIndexByVertex = new int[data.VertexCount];
		int weldedVertexCount = 0;

		for (int vertexIndex = 0; vertexIndex < data.VertexCount; vertexIndex++)
		{
			var positionKey = new ChartPositionKey(
				QuantizePosition(ReadComponent(data.Bin, data.Positions, vertexIndex, 0)),
				QuantizePosition(ReadComponent(data.Bin, data.Positions, vertexIndex, 1)),
				QuantizePosition(ReadComponent(data.Bin, data.Positions, vertexIndex, 2)));

			float u = ReadComponent(data.Bin, data.TexCoords, vertexIndex, 0);
			float v = ReadComponent(data.Bin, data.TexCoords, vertexIndex, 1);

			int weldedIndex = ResolveWeldedIndex(positionKey, u, v, texCoordTolerance, firstWeldedByPosition, extraWeldedByFirst, weldedTexCoords);
			if (weldedIndex < 0)
			{
				weldedIndex = weldedVertexCount++;
				weldedTexCoords[weldedIndex * 2] = u;
				weldedTexCoords[weldedIndex * 2 + 1] = v;

				if (!firstWeldedByPosition.TryAdd(positionKey, weldedIndex))
				{
					int firstWelded = firstWeldedByPosition[positionKey];
					if (!extraWeldedByFirst.TryGetValue(firstWelded, out List<int> extras))
					{
						extras = new List<int>(2);
						extraWeldedByFirst[firstWelded] = extras;
					}

					extras.Add(weldedIndex);
				}
			}

			weldedIndexByVertex[vertexIndex] = weldedIndex;
		}

		if (weldedVertexCount == 0) return 0;

		var parents = new int[weldedVertexCount];
		var used = new bool[weldedVertexCount];
		for (int i = 0; i < weldedVertexCount; i++)
		{
			parents[i] = i;
		}

		int indexCount = data.Indices.HasValue ? data.Indices.Value.Count : data.VertexCount;
		for (int indexPosition = 0; indexPosition + 2 < indexCount; indexPosition += 3)
		{
			int index0 = ReadIndex(data, indexPosition);
			int index1 = ReadIndex(data, indexPosition + 1);
			int index2 = ReadIndex(data, indexPosition + 2);
			if (index0 < 0 || index1 < 0 || index2 < 0) continue;
			if (index0 >= data.VertexCount || index1 >= data.VertexCount || index2 >= data.VertexCount) continue;

			int welded0 = weldedIndexByVertex[index0];
			int welded1 = weldedIndexByVertex[index1];
			int welded2 = weldedIndexByVertex[index2];

			used[welded0] = true;
			used[welded1] = true;
			used[welded2] = true;

			Union(parents, welded0, welded1);
			Union(parents, welded1, welded2);
		}

		var islandRoots = new HashSet<int>();
		for (int i = 0; i < weldedVertexCount; i++)
		{
			if (!used[i]) continue;
			islandRoots.Add(FindRoot(parents, i));
		}

		return islandRoots.Count;
	}

	private static int ResolveWeldedIndex(
		ChartPositionKey positionKey,
		float u,
		float v,
		float tolerance,
		Dictionary<ChartPositionKey, int> firstWeldedByPosition,
		Dictionary<int, List<int>> extraWeldedByFirst,
		float[] weldedTexCoords)
	{
		if (!firstWeldedByPosition.TryGetValue(positionKey, out int firstWelded)) return -1;
		if (IsTexCoordMatch(weldedTexCoords, firstWelded, u, v, tolerance)) return firstWelded;
		if (!extraWeldedByFirst.TryGetValue(firstWelded, out List<int> extras)) return -1;

		for (int i = 0; i < extras.Count; i++)
		{
			if (IsTexCoordMatch(weldedTexCoords, extras[i], u, v, tolerance)) return extras[i];
		}

		return -1;
	}

	private static bool IsTexCoordMatch(float[] weldedTexCoords, int weldedIndex, float u, float v, float tolerance)
	{
		return MathF.Abs(weldedTexCoords[weldedIndex * 2] - u) <= tolerance
		       && MathF.Abs(weldedTexCoords[weldedIndex * 2 + 1] - v) <= tolerance;
	}

	private static int FindRoot(int[] parents, int index)
	{
		while (parents[index] != index)
		{
			parents[index] = parents[parents[index]];
			index = parents[index];
		}

		return index;
	}

	private static void Union(int[] parents, int first, int second)
	{
		int firstRoot = FindRoot(parents, first);
		int secondRoot = FindRoot(parents, second);
		if (firstRoot != secondRoot)
		{
			parents[secondRoot] = firstRoot;
		}
	}

	private static int QuantizePosition(float value)
	{
		return (int)MathF.Round(value / PositionWeldEpsilon);
	}
}
