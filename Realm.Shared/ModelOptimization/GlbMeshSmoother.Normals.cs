using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>welding vertices and computing the smoothed normals and weights.</summary>
public static unsafe partial class GlbMeshSmoother
	{
	private static Vector3[,] CalculateNormalsAndWeights(int triangleCount, uint[] indices, Vector3[] positions, float cosThreshold)
	{
		var faceNormals = new Vector3[triangleCount];
		var cornerWeights = new float[triangleCount, 3];

		ComputeFaceNormalsAndWeights(triangleCount, indices, positions, faceNormals, cornerWeights);

		var spatialPosMap = BuildSpatialPosMap(triangleCount, indices, positions);

		return ComputeCornerNormals(triangleCount, spatialPosMap, faceNormals, cornerWeights, cosThreshold);
	}

	private static void WeldVertices(int triangleCount, uint[] indices, Vector3[] positions, Vector3[,] cornerNormals,
		bool hasUv0, Vector2[]? uvs0, bool hasUv1, Vector2[]? uvs1, bool hasJoints0, Vector4[]? joints0, bool hasWeights0, Vector4[]? weights0, bool hasColor0, Vector4[]? colors0,
		out List<SmoothedVertexData> weldedVertices, out List<uint> weldedIndices)
	{
		var uniqueVertexMap = new Dictionary<VertexWeldKey, uint>(positions.Length);
		weldedVertices = new List<SmoothedVertexData>(positions.Length);
		weldedIndices = new List<uint>(indices.Length);

		for (int t = 0; t < triangleCount; t++)
		{
			ProcessWeldTriangle(t, indices, positions, cornerNormals,
				hasUv0, uvs0, hasUv1, uvs1, hasJoints0, joints0, hasWeights0, weights0, hasColor0, colors0,
				uniqueVertexMap, weldedVertices, weldedIndices);
		}
	}

	private static void ProcessWeldTriangle(int t, uint[] indices, Vector3[] positions, Vector3[,] cornerNormals,
		bool hasUv0, Vector2[]? uvs0, bool hasUv1, Vector2[]? uvs1, bool hasJoints0, Vector4[]? joints0, bool hasWeights0, Vector4[]? weights0, bool hasColor0, Vector4[]? colors0,
		Dictionary<VertexWeldKey, uint> uniqueVertexMap, List<SmoothedVertexData> weldedVertices, List<uint> weldedIndices)
	{
		uint orig0 = indices[t * 3];
		uint orig1 = indices[t * 3 + 1];
		uint orig2 = indices[t * 3 + 2];

		if (orig0 >= positions.Length || orig1 >= positions.Length || orig2 >= positions.Length) return;

		uint c0 = ProcessSingleWeldCorner(orig0, t, 0, positions, cornerNormals, hasUv0, uvs0, hasUv1, uvs1, hasJoints0, joints0, hasWeights0, weights0, hasColor0, colors0, uniqueVertexMap, weldedVertices);
		uint c1 = ProcessSingleWeldCorner(orig1, t, 1, positions, cornerNormals, hasUv0, uvs0, hasUv1, uvs1, hasJoints0, joints0, hasWeights0, weights0, hasColor0, colors0, uniqueVertexMap, weldedVertices);
		uint c2 = ProcessSingleWeldCorner(orig2, t, 2, positions, cornerNormals, hasUv0, uvs0, hasUv1, uvs1, hasJoints0, joints0, hasWeights0, weights0, hasColor0, colors0, uniqueVertexMap, weldedVertices);

		if (c0 != c1 && c1 != c2 && c0 != c2)
		{
			weldedIndices.Add(c0);
			weldedIndices.Add(c1);
			weldedIndices.Add(c2);
		}
	}

	private static uint ProcessSingleWeldCorner(uint origIdx, int t, int cornerOffset, Vector3[] positions, Vector3[,] cornerNormals,
		bool hasUv0, Vector2[]? uvs0, bool hasUv1, Vector2[]? uvs1, bool hasJoints0, Vector4[]? joints0, bool hasWeights0, Vector4[]? weights0, bool hasColor0, Vector4[]? colors0,
		Dictionary<VertexWeldKey, uint> uniqueVertexMap, List<SmoothedVertexData> weldedVertices)
	{
		return ProcessWeldCorner(
			positions[origIdx], cornerNormals[t, cornerOffset],
			hasUv0 ? uvs0![origIdx] : Vector2.Zero, hasUv1 ? uvs1![origIdx] : Vector2.Zero,
			hasJoints0 ? joints0![origIdx] : Vector4.Zero, hasWeights0 ? weights0![origIdx] : Vector4.Zero,
			hasColor0 ? colors0![origIdx] : Vector4.One,
			uniqueVertexMap, weldedVertices);
	}

	private static void FinalizePrimitive(List<SmoothedVertexData> weldedVertices, List<uint> weldedIndices, bool hasTangents, bool hasUv0, JsonObject primObj, bool hasUv1, bool hasJoints0, bool hasWeights0, bool hasColor0, MemoryStream newBinStream, JsonArray bufferViews, JsonArray accessors)
	{
		if (hasTangents && hasUv0)
		{
			ComputeWeldedTangents(weldedVertices, weldedIndices);
		}

		OptimizeMeshLayout(weldedVertices, weldedIndices);

		WritePrimitiveToBin(primObj, weldedVertices, weldedIndices, hasUv0, hasUv1, hasJoints0, hasWeights0, hasColor0, hasTangents, newBinStream, bufferViews, accessors);
	}

	private static void ComputeFaceNormalsAndWeights(int triangleCount, uint[] indices, Vector3[] positions, Vector3[] faceNormals, float[,] cornerWeights)
	{
		for (int t = 0; t < triangleCount; t++)
		{
			uint i0 = indices[t * 3];
			uint i1 = indices[t * 3 + 1];
			uint i2 = indices[t * 3 + 2];

			if (i0 >= positions.Length || i1 >= positions.Length || i2 >= positions.Length)
			{
				faceNormals[t] = Vector3.UnitY;
				cornerWeights[t, 0] = 1.0f;
				cornerWeights[t, 1] = 1.0f;
				cornerWeights[t, 2] = 1.0f;
				continue;
			}

			Vector3 p0 = positions[i0];
			Vector3 p1 = positions[i1];
			Vector3 p2 = positions[i2];

			Vector3 e01 = p1 - p0;
			Vector3 e02 = p2 - p0;
			Vector3 e12 = p2 - p1;

			faceNormals[t] = CalculateFaceNormal(e01, e02);

			CalculateCornerWeights(e01, e02, e12, out float w0, out float w1, out float w2);

			cornerWeights[t, 0] = w0;
			cornerWeights[t, 1] = w1;
			cornerWeights[t, 2] = w2;
		}
	}

	private static Vector3 CalculateFaceNormal(Vector3 e01, Vector3 e02)
	{
		Vector3 cross = Vector3.Cross(e01, e02);
		float crossLen = cross.Length();
		return crossLen > 1e-7f ? (cross / crossLen) : Vector3.UnitY;
	}

	private static void CalculateCornerWeights(Vector3 e01, Vector3 e02, Vector3 e12, out float w0, out float w1, out float w2)
	{
		float l01 = e01.Length();
		float l02 = e02.Length();
		float l12 = e12.Length();

		w0 = CalculateCornerAngle(e01, e02, l01, l02);
		w1 = CalculateCornerAngle(-e01, e12, l01, l12);
		w2 = CalculateCornerAngle(-e02, -e12, l02, l12);
	}

	private static float CalculateCornerAngle(Vector3 edgeA, Vector3 edgeB, float lenA, float lenB)
	{
		if (lenA <= 1e-6f || lenB <= 1e-6f) return 1.0f;

		float dot = Math.Clamp(Vector3.Dot(edgeA, edgeB) / (lenA * lenB), -1.0f, 1.0f);
		float a = MathF.Acos(dot);

		if (!float.IsNaN(a) && a > 1e-4f) return a;

		return 1.0f;
	}
}
