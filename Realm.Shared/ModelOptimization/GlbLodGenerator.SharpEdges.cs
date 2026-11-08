using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>measuring the sharp edge ratio of a welded mesh.</summary>
public static unsafe partial class GlbLodGenerator
	{
	private static float MeasureSharpEdgeRatio(uint[] weldedIndices, float[] weldedPositions, int weldedVertexCount)
	{
		if (weldedVertexCount <= 0) return 0.0f;

		var canonicalPositions = new int[weldedVertexCount];
		var positionLookup = new Dictionary<SpatialPositionKey, int>(weldedVertexCount);
		for (int vertexIndex = 0; vertexIndex < weldedVertexCount; vertexIndex++)
		{
			int positionOffset = vertexIndex * 3;
			var key = new SpatialPositionKey(new Vector3(weldedPositions[positionOffset], weldedPositions[positionOffset + 1], weldedPositions[positionOffset + 2]));
			if (!positionLookup.TryGetValue(key, out int canonicalIndex))
			{
				canonicalIndex = positionLookup.Count;
				positionLookup[key] = canonicalIndex;
			}

			canonicalPositions[vertexIndex] = canonicalIndex;
		}

		float sharpCosThreshold = MathF.Cos(LodDecimationProfile.FeatureEdgeAngleDegrees * (MathF.PI / 180.0f));
		var edgeNormals = new Dictionary<long, Vector3>(weldedIndices.Length);

		int sharedEdgeCount = 0;
		int sharpEdgeCount = 0;

		for (int index = 0; index + 2 < weldedIndices.Length; index += 3)
		{
			uint index0 = weldedIndices[index];
			uint index1 = weldedIndices[index + 1];
			uint index2 = weldedIndices[index + 2];
			if (index0 >= (uint)weldedVertexCount || index1 >= (uint)weldedVertexCount || index2 >= (uint)weldedVertexCount) continue;

			Vector3 faceNormal = ComputeWeldedFaceNormal(weldedPositions, (int)index0, (int)index1, (int)index2);

			AccumulateEdge(edgeNormals, canonicalPositions, faceNormal, index0, index1, sharpCosThreshold, ref sharedEdgeCount, ref sharpEdgeCount);
			AccumulateEdge(edgeNormals, canonicalPositions, faceNormal, index1, index2, sharpCosThreshold, ref sharedEdgeCount, ref sharpEdgeCount);
			AccumulateEdge(edgeNormals, canonicalPositions, faceNormal, index2, index0, sharpCosThreshold, ref sharedEdgeCount, ref sharpEdgeCount);
		}

		return sharedEdgeCount == 0 ? 0.0f : sharpEdgeCount / (float)sharedEdgeCount;
	}

	private static void AccumulateEdge(
		Dictionary<long, Vector3> edgeNormals,
		int[] canonicalPositions,
		Vector3 faceNormal,
		uint vertexA,
		uint vertexB,
		float sharpCosThreshold,
		ref int sharedEdgeCount,
		ref int sharpEdgeCount)
	{
		int canonicalA = canonicalPositions[vertexA];
		int canonicalB = canonicalPositions[vertexB];
		long edgeKey = canonicalA < canonicalB
			? ((long)canonicalA << 32) | (uint)canonicalB
			: ((long)canonicalB << 32) | (uint)canonicalA;

		if (!edgeNormals.TryGetValue(edgeKey, out Vector3 previousFaceNormal))
		{
			edgeNormals[edgeKey] = faceNormal;
			return;
		}

		sharedEdgeCount++;
		if (Vector3.Dot(previousFaceNormal, faceNormal) >= sharpCosThreshold) return;

		sharpEdgeCount++;
	}

	private static Vector3 ComputeWeldedFaceNormal(float[] weldedPositions, int index0, int index1, int index2)
	{
		Vector3 position0 = ReadWeldedPosition(weldedPositions, index0);
		Vector3 position1 = ReadWeldedPosition(weldedPositions, index1);
		Vector3 position2 = ReadWeldedPosition(weldedPositions, index2);

		Vector3 cross = Vector3.Cross(position1 - position0, position2 - position0);
		float length = cross.Length();
		return length > 1e-7f ? cross / length : Vector3.UnitY;
	}

	private static Vector3 ReadWeldedPosition(float[] weldedPositions, int vertexIndex)
	{
		int offset = vertexIndex * 3;
		return new Vector3(weldedPositions[offset], weldedPositions[offset + 1], weldedPositions[offset + 2]);
	}
}
