using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>laying the smoothed vertices out for the buffer.</summary>
public static unsafe partial class GlbMeshSmoother
	{
	private static void OptimizeMeshLayout(List<SmoothedVertexData> vertices, List<uint> indices)
	{
		int vertexCount = vertices.Count;
		int indexCount = indices.Count;
		if (vertexCount == 0 || indexCount < 3) return;

		var optIndices = indices.ToArray();
		var optPositions = new float[vertexCount * 3];
		for (int i = 0; i < vertexCount; i++)
		{
			optPositions[i * 3] = vertices[i].Position.X;
			optPositions[i * 3 + 1] = vertices[i].Position.Y;
			optPositions[i * 3 + 2] = vertices[i].Position.Z;
		}

		fixed (uint* pIndices = optIndices)
		fixed (float* pPositions = optPositions)
		{
			MeshOptimizerNative.meshopt_optimizeVertexCache(
				pIndices,
				pIndices,
				(nuint)indexCount,
				(nuint)vertexCount);

			MeshOptimizerNative.meshopt_optimizeOverdraw(
				pIndices,
				pIndices,
				(nuint)indexCount,
				pPositions,
				(nuint)vertexCount,
				(nuint)(3 * sizeof(float)),
				1.05f);

			var remap = new uint[vertexCount];
			fixed (uint* pRemap = remap)
			{
				nuint uniqueCount = MeshOptimizerNative.meshopt_optimizeVertexFetchRemap(
					pRemap,
					pIndices,
					(nuint)indexCount,
					(nuint)vertexCount);

				MeshOptimizerNative.meshopt_remapIndexBuffer(
					pIndices,
					pIndices,
					(nuint)indexCount,
					pRemap);

				var remappedVertices = new SmoothedVertexData[uniqueCount];
				for (int i = 0; i < vertexCount; i++)
				{
					uint newIdx = remap[i];
					if (newIdx != 0xFFFFFFFF && newIdx < uniqueCount)
					{
						remappedVertices[newIdx] = vertices[i];
					}
				}

				vertices.Clear();
				vertices.AddRange(remappedVertices);
				indices.Clear();
				indices.AddRange(optIndices);
			}
		}
	}
}
