using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>simplifying a primitive and deciding whether it is worth it.</summary>
public static unsafe partial class GlbLodGenerator
	{
	private readonly record struct WeldedPrimitiveGeometry(
		uint[] WeldedIndices,
		float[] WeldedPositions,
		float[] WeldedTexCoords,
		uint[] Representatives,
		int WeldedVertexCount,
		LodDecimationProfile Profile);

	private static uint[]? SimplifyIndices(uint[] originalIndices, PrimitiveGeometry geometry, float ratio, int t, byte[] binBytes, LodDecimationProfile? overrideProfile, float uvSeamMergeTolerance)
	{
		var weldedGeometry = BuildWeldedPrimitiveGeometry(originalIndices, geometry, binBytes, overrideProfile, uvSeamMergeTolerance);
		if (weldedGeometry == null) return null;

		int targetIndexCount = (int)(originalIndices.Length * weldedGeometry.Value.Profile.GetMinimumRatioForTier(ratio)) / 3 * 3;
		if (!IsTargetIndexCountValid(targetIndexCount, originalIndices.Length)) return null;

		float targetError = GetLodTargetError(t) * weldedGeometry.Value.Profile.TargetErrorScale;

		return SimplifyWeldedGeometry(weldedGeometry.Value, geometry, binBytes, targetIndexCount, targetError, originalIndices.Length);
	}

	private static uint[]? SimplifyIndicesToBudget(
		uint[] originalIndices,
		PrimitiveGeometry geometry,
		int targetIndexCount,
		float targetError,
		byte[] binBytes,
		LodDecimationProfile? overrideProfile,
		float uvSeamMergeTolerance)
	{
		var weldedGeometry = BuildWeldedPrimitiveGeometry(originalIndices, geometry, binBytes, overrideProfile, uvSeamMergeTolerance);
		if (weldedGeometry == null) return null;

		targetIndexCount = targetIndexCount / 3 * 3;
		if (!IsTargetIndexCountValid(targetIndexCount, originalIndices.Length)) return null;

		return SimplifyWeldedGeometry(weldedGeometry.Value, geometry, binBytes, targetIndexCount, targetError, originalIndices.Length);
	}

	private static WeldedPrimitiveGeometry? BuildWeldedPrimitiveGeometry(uint[] originalIndices, PrimitiveGeometry geometry, byte[] binBytes, LodDecimationProfile? overrideProfile, float uvSeamMergeTolerance)
	{
		float[] weldedPositions = new float[geometry.PositionCount * 3];
		float[] weldedTexCoords = new float[geometry.PositionCount * 2];
		uint[] weldedIndices = new uint[originalIndices.Length];
		uint[] representatives = new uint[geometry.PositionCount];
		int weldedVertexCount = BuildPositionTexCoordWeldedMesh(weldedIndices, weldedPositions, weldedTexCoords, representatives, originalIndices, geometry, binBytes, uvSeamMergeTolerance);
		if (weldedVertexCount < 3) return null;

		int triangleCount = originalIndices.Length / 3;
		float sharpEdgeRatio = triangleCount > LodDecimationProfile.HardEdgedMaximumTriangleCount
			? float.NaN
			: MeasureSharpEdgeRatio(weldedIndices, weldedPositions, weldedVertexCount);
		LodDecimationProfile profile = overrideProfile ?? LodDecimationProfile.Detect(sharpEdgeRatio, triangleCount);

		return new WeldedPrimitiveGeometry(weldedIndices, weldedPositions, weldedTexCoords, representatives, weldedVertexCount, profile);
	}

	private static uint[]? SimplifyWeldedGeometry(
		WeldedPrimitiveGeometry weldedGeometry,
		PrimitiveGeometry geometry,
		byte[] binBytes,
		int targetIndexCount,
		float targetError,
		int originalIndexCount)
	{
		float texCoordWeight = geometry.HasTexCoords ? weldedGeometry.Profile.TexCoordWeight : 0.0f;

		return TrySimplifyWeldedMesh(
			weldedGeometry.WeldedIndices,
			weldedGeometry.WeldedPositions,
			weldedGeometry.WeldedTexCoords,
			weldedGeometry.Representatives,
			weldedGeometry.WeldedVertexCount,
			geometry,
			binBytes,
			targetIndexCount,
			targetError,
			texCoordWeight,
			originalIndexCount);
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
