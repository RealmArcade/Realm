using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>building the welded mesh the simplifier runs on.</summary>
public static unsafe partial class GlbLodGenerator
	{
	private static int BuildPositionTexCoordWeldedMesh(uint[] weldedIndices, float[] weldedPositions, float[] weldedTexCoords, uint[] representatives, uint[] originalIndices, PrimitiveGeometry geometry, byte[] binBytes, float uvSeamMergeTolerance)
	{
		float texCoordTolerance = uvSeamMergeTolerance > 0f ? uvSeamMergeTolerance : WeldEpsilon;
		var firstWeldedByPosition = new Dictionary<VertexPositionKey, int>(geometry.PositionCount);
		var extraWeldedByFirst = new Dictionary<int, List<int>>();
		var vertexRemap = new uint[geometry.PositionCount];
		int weldedVertexCount = 0;

		for (int vertexIndex = 0; vertexIndex < geometry.PositionCount; vertexIndex++)
		{
			float x = ReadSingle(binBytes, geometry.PositionByteOffset + (vertexIndex * geometry.PositionByteStride));
			float y = ReadSingle(binBytes, geometry.PositionByteOffset + (vertexIndex * geometry.PositionByteStride) + 4);
			float z = ReadSingle(binBytes, geometry.PositionByteOffset + (vertexIndex * geometry.PositionByteStride) + 8);
			float u = 0f;
			float v = 0f;

			if (geometry.HasTexCoords)
			{
				int texCoordOffset = geometry.TexCoords.ByteOffset + (vertexIndex * geometry.TexCoords.ByteStride);
				int componentSize = GetComponentByteSize(geometry.TexCoords.ComponentType);
				u = ReadTexCoordComponent(binBytes, texCoordOffset, geometry.TexCoords);
				v = ReadTexCoordComponent(binBytes, texCoordOffset + componentSize, geometry.TexCoords);
			}

			var positionKey = new VertexPositionKey(
				QuantizeWeldValue(x),
				QuantizeWeldValue(y),
				QuantizeWeldValue(z),
				ComputeOtherAttributesHash(geometry.OtherAttributes, vertexIndex, binBytes));

			int weldedIndex = ResolveWeldedVertex(positionKey, u, v, texCoordTolerance, firstWeldedByPosition, extraWeldedByFirst, weldedTexCoords);
			if (weldedIndex < 0)
			{
				weldedIndex = weldedVertexCount++;
				weldedPositions[(weldedIndex * 3)] = x;
				weldedPositions[(weldedIndex * 3) + 1] = y;
				weldedPositions[(weldedIndex * 3) + 2] = z;
				weldedTexCoords[(weldedIndex * 2)] = u;
				weldedTexCoords[(weldedIndex * 2) + 1] = v;
				representatives[weldedIndex] = (uint)vertexIndex;

				if (!firstWeldedByPosition.TryAdd(positionKey, weldedIndex))
				{
					int firstWelded = firstWeldedByPosition[positionKey];
					if (!extraWeldedByFirst.TryGetValue(firstWelded, out List<int>? extras))
					{
						extras = new List<int>(2);
						extraWeldedByFirst[firstWelded] = extras;
					}

					extras.Add(weldedIndex);
				}
			}

			vertexRemap[vertexIndex] = (uint)weldedIndex;
		}

		for (int i = 0; i < originalIndices.Length; i++)
		{
			weldedIndices[i] = vertexRemap[originalIndices[i]];
		}

		return weldedVertexCount;
	}

	private static int QuantizeWeldValue(float value)
	{
		return (int)MathF.Round(value / WeldEpsilon);
	}

	private static int ResolveWeldedVertex(
		VertexPositionKey positionKey,
		float u,
		float v,
		float tolerance,
		Dictionary<VertexPositionKey, int> firstWeldedByPosition,
		Dictionary<int, List<int>> extraWeldedByFirst,
		float[] weldedTexCoords)
	{
		if (!firstWeldedByPosition.TryGetValue(positionKey, out int firstWelded)) return -1;
		if (IsTexCoordMatch(weldedTexCoords, firstWelded, u, v, tolerance)) return firstWelded;
		if (!extraWeldedByFirst.TryGetValue(firstWelded, out List<int>? extras)) return -1;

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

	private static ulong ComputeOtherAttributesHash(AttributeStream[] streams, int vertexIndex, byte[] binBytes)
	{
		ulong hash = 14695981039346656037UL;

		foreach (var stream in streams)
		{
			int offset = stream.ByteOffset + (vertexIndex * stream.ByteStride);
			for (int b = 0; b < stream.ElementSize; b++)
			{
				hash ^= binBytes[offset + b];
				hash *= 1099511628211UL;
			}
		}

		return hash;
	}
}
