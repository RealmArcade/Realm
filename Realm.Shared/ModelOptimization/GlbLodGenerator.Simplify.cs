using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>simplifying a primitive and deciding whether it is worth it.</summary>
public static unsafe partial class GlbLodGenerator
	{
	private static uint[]? SimplifyIndices(uint[] originalIndices, PrimitiveGeometry geometry, float ratio, int t, byte[] binBytes, LodDecimationProfile? overrideProfile, float uvSeamMergeTolerance)
	{
		float[] weldedPositions = new float[geometry.PositionCount * 3];
		float[] weldedTexCoords = new float[geometry.PositionCount * 2];
		uint[] weldedIndices = new uint[originalIndices.Length];
		uint[] representatives = new uint[geometry.PositionCount];
		int weldedVertexCount = BuildPositionTexCoordWeldedMesh(weldedIndices, weldedPositions, weldedTexCoords, representatives, originalIndices, geometry, binBytes, uvSeamMergeTolerance);
		if (weldedVertexCount < 3) return null;

		float sharpEdgeRatio = MeasureSharpEdgeRatio(weldedIndices, weldedPositions, weldedVertexCount);
		LodDecimationProfile profile = overrideProfile ?? LodDecimationProfile.Detect(sharpEdgeRatio, originalIndices.Length / 3);

		int targetIndexCount = (int)(originalIndices.Length * profile.GetMinimumRatioForTier(ratio)) / 3 * 3;
		if (!IsTargetIndexCountValid(targetIndexCount, originalIndices.Length)) return null;

		float targetError = GetLodTargetError(t) * profile.TargetErrorScale;
		float texCoordWeight = geometry.HasTexCoords ? profile.TexCoordWeight : 0.0f;

		return TrySimplifyWeldedMesh(weldedIndices, weldedPositions, weldedTexCoords, representatives, weldedVertexCount, geometry, binBytes, targetIndexCount, targetError, texCoordWeight, originalIndices.Length);
	}

	private static uint[]? TrySimplifyWeldedMesh(
		uint[] weldedIndices,
		float[] weldedPositions,
		float[] weldedTexCoords,
		uint[] representatives,
		int weldedVertexCount,
		PrimitiveGeometry geometry,
		byte[] binBytes,
		int targetIndexCount,
		float targetError,
		float texCoordWeight,
		int originalIndexCount)
	{
		uint[] simplifiedIndices = new uint[originalIndexCount];
		nuint simplifiedCount;

		fixed (uint* pWelded = weldedIndices)
		fixed (float* pWeldedPositions = weldedPositions)
		fixed (float* pWeldedTexCoords = weldedTexCoords)
		fixed (uint* pRepresentatives = representatives)
		fixed (uint* pDest = simplifiedIndices)
		fixed (byte* pBin = binBytes)
		{
			float* pAttributeWeights = stackalloc float[2] { texCoordWeight, texCoordWeight };

			simplifiedCount = DoSimplifyIndices(pWelded, pDest, pWeldedPositions, (nuint)weldedVertexCount, pWeldedTexCoords, pAttributeWeights, targetIndexCount, targetError, (nuint)originalIndexCount);

			if (simplifiedCount < 3 || (simplifiedCount % 3) != 0 || simplifiedCount >= (nuint)originalIndexCount) return null;

			for (nuint i = 0; i < simplifiedCount; i++)
			{
				pDest[i] = pRepresentatives[pDest[i]];
			}

			float* pPositions = (float*)(pBin + geometry.PositionByteOffset);
			MeshOptimizerNative.meshopt_optimizeVertexCache(pDest, pDest, simplifiedCount, (nuint)geometry.PositionCount);
			MeshOptimizerNative.meshopt_optimizeOverdraw(pDest, pDest, simplifiedCount, pPositions, (nuint)geometry.PositionCount, (nuint)geometry.PositionByteStride, 1.05f);
		}

		uint[] finalIndices = new uint[(int)simplifiedCount];
		Array.Copy(simplifiedIndices, finalIndices, (int)simplifiedCount);
		return finalIndices;
	}
}
