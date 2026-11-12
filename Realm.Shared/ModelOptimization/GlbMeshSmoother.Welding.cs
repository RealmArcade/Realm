using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>the spatial lookup, corner normals and welded tangents.</summary>
public static unsafe partial class GlbMeshSmoother
	{
	private static Dictionary<SpatialPositionKey, List<(int TriIdx, int CornerIdx)>> BuildSpatialPosMap(int triangleCount, uint[] indices, Vector3[] positions)
	{
		var spatialPosMap = new Dictionary<SpatialPositionKey, List<(int TriIdx, int CornerIdx)>>(positions.Length);
		for (int t = 0; t < triangleCount; t++)
		{
			for (int c = 0; c < 3; c++)
			{
				uint origIdx = indices[t * 3 + c];
				if (origIdx >= positions.Length) continue;

				Vector3 pos = positions[origIdx];
				var key = new SpatialPositionKey(pos);
				if (!spatialPosMap.TryGetValue(key, out var list))
				{
					list = new List<(int TriIdx, int CornerIdx)>(4);
					spatialPosMap[key] = list;
				}

				list.Add((t, c));
			}
		}

		return spatialPosMap;
	}

	private static Vector3[,] ComputeCornerNormals(int triangleCount, Dictionary<SpatialPositionKey, List<(int TriIdx, int CornerIdx)>> spatialPosMap, Vector3[] faceNormals, float[,] cornerWeights, float cosThreshold)
	{
		var cornerNormals = new Vector3[triangleCount, 3];
		foreach (var kvp in spatialPosMap)
		{
			var corners = kvp.Value;
			int cornerCount = corners.Count;

			if (cornerCount == 1)
			{
				cornerNormals[corners[0].TriIdx, corners[0].CornerIdx] = faceNormals[corners[0].TriIdx];
				continue;
			}

			for (int i = 0; i < cornerCount; i++)
			{
				var (triA, cornerA) = corners[i];
				Vector3 normA = faceNormals[triA];

				Vector3 accum = Vector3.Zero;
				for (int j = 0; j < cornerCount; j++)
				{
					var (triB, cornerB) = corners[j];
					Vector3 normB = faceNormals[triB];

					float dot = Vector3.Dot(normA, normB);
					if (dot >= cosThreshold)
					{
						float weight = cornerWeights[triB, cornerB];
						accum += normB * weight;
					}
				}

				float len = accum.Length();
				cornerNormals[triA, cornerA] = len > 1e-6f ? (accum / len) : normA;
			}
		}

		return cornerNormals;
	}

	private static uint ProcessWeldCorner(
		Vector3 position,
		Vector3 normal,
		Vector2 uv0,
		Vector2 uv1,
		Vector4 joints0,
		Vector4 weights0,
		Vector4 color0,
		Dictionary<VertexWeldKey, uint> uniqueVertexMap,
		List<SmoothedVertexData> weldedVertices)
	{
		var key = new VertexWeldKey(position, normal, uv0, uv1, joints0, weights0, color0);
		if (!uniqueVertexMap.TryGetValue(key, out uint newIdx))
		{
			newIdx = (uint)weldedVertices.Count;
			weldedVertices.Add(new SmoothedVertexData
			{
				Position = position,
				Normal = normal,
				UV0 = uv0,
				UV1 = uv1,
				Joints0 = joints0,
				Weights0 = weights0,
				Color0 = color0,
				Tangent = Vector4.UnitX
			});
			uniqueVertexMap[key] = newIdx;
		}

		return newIdx;
	}

	private static void ComputeWeldedTangents(List<SmoothedVertexData> vertices, List<uint> indices)
	{
		int count = vertices.Count;
		var tan1 = new Vector3[count];
		var tan2 = new Vector3[count];

		for (int t = 0; t + 2 < indices.Count; t += 3)
		{
			uint i0 = indices[t];
			uint i1 = indices[t + 1];
			uint i2 = indices[t + 2];

			if (i0 >= count || i1 >= count || i2 >= count) continue;

			Vector3 v0 = vertices[(int)i0].Position;
			Vector3 v1 = vertices[(int)i1].Position;
			Vector3 v2 = vertices[(int)i2].Position;

			Vector2 w0 = vertices[(int)i0].UV0;
			Vector2 w1 = vertices[(int)i1].UV0;
			Vector2 w2 = vertices[(int)i2].UV0;

			float x1 = v1.X - v0.X;
			float x2 = v2.X - v0.X;
			float y1 = v1.Y - v0.Y;
			float y2 = v2.Y - v0.Y;
			float z1 = v1.Z - v0.Z;
			float z2 = v2.Z - v0.Z;

			float s1 = w1.X - w0.X;
			float s2 = w2.X - w0.X;
			float t1 = w1.Y - w0.Y;
			float t2 = w2.Y - w0.Y;

			float r = (s1 * t2 - s2 * t1);
			float invR = MathF.Abs(r) > 1e-7f ? 1.0f / r : 0.0f;

			Vector3 sdir = new Vector3((t2 * x1 - t1 * x2) * invR, (t2 * y1 - t1 * y2) * invR, (t2 * z1 - t1 * z2) * invR);
			Vector3 tdir = new Vector3((s1 * x2 - s2 * x1) * invR, (s1 * y2 - s2 * y1) * invR, (s1 * z2 - s2 * z1) * invR);

			tan1[i0] += sdir;
			tan1[i1] += sdir;
			tan1[i2] += sdir;

			tan2[i0] += tdir;
			tan2[i1] += tdir;
			tan2[i2] += tdir;
		}

		for (int a = 0; a < count; a++)
		{
			var v = vertices[a];
			Vector3 n = v.Normal;
			Vector3 t = tan1[a];

			Vector3 tangentVec = t - n * Vector3.Dot(n, t);
			float tanLen = tangentVec.Length();
			if (tanLen > 1e-6f)
			{
				tangentVec /= tanLen;
			}
			else
			{
				tangentVec = Vector3.UnitX;
			}

			float sign = (Vector3.Dot(Vector3.Cross(n, t), tan2[a]) < 0.0f) ? -1.0f : 1.0f;
			v.Tangent = new Vector4(tangentVec.X, tangentVec.Y, tangentVec.Z, sign);
			vertices[a] = v;
		}
	}
}
