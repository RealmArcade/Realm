using System.Numerics;
using System.Text.Json.Nodes;
using Assimp;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>the smooth adjacency the surface pass walks.</summary>
public static partial class GlbPlayerColorProcessor
	{
	private static void BuildSmoothAdjacency(
		List<(Vector3 Pos0, Vector3 Pos1, Vector3 Pos2)> facePositions,
		int faceCount, float cosCreaseThreshold,
		out Dictionary<int, HashSet<int>> smoothAdjacency)
	{
		var faceNormals = new Vector3[faceCount];
		for (int i = 0; i < faceCount; i++)
		{
			var (p0, p1, p2) = facePositions[i];
			Vector3 e01 = p1 - p0;
			Vector3 e02 = p2 - p0;
			Vector3 cross = Vector3.Cross(e01, e02);
			float len = cross.Length();
			faceNormals[i] = len > 1e-7f ? (cross / len) : Vector3.UnitY;
		}

		var edgeToFaces = new Dictionary<(long, long), List<int>>();
		for (int faceIdx = 0; faceIdx < faceCount; faceIdx++)
		{
			var (p0, p1, p2) = facePositions[faceIdx];
			long k0 = QuantizePosition(p0);
			long k1 = QuantizePosition(p1);
			long k2 = QuantizePosition(p2);

			AddEdge(edgeToFaces, k0, k1, faceIdx);
			AddEdge(edgeToFaces, k1, k2, faceIdx);
			AddEdge(edgeToFaces, k2, k0, faceIdx);
		}

		smoothAdjacency = new Dictionary<int, HashSet<int>>();
		for (int i = 0; i < faceCount; i++)
		{
			smoothAdjacency[i] = new HashSet<int>();
		}

		foreach (var faceList in edgeToFaces.Values)
		{
			for (int i = 0; i < faceList.Count; i++)
			{
				for (int j = i + 1; j < faceList.Count; j++)
				{
					int fa = faceList[i];
					int fb = faceList[j];
					if (fa == fb) continue;

					float dot = Vector3.Dot(faceNormals[fa], faceNormals[fb]);
					if (dot >= cosCreaseThreshold)
					{
						smoothAdjacency[fa].Add(fb);
						smoothAdjacency[fb].Add(fa);
					}
				}
			}
		}
	}
}
