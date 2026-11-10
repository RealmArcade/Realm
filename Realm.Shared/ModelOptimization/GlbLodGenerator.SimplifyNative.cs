using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>the native simplification entry point and its target error.</summary>
public static unsafe partial class GlbLodGenerator
	{
	private static nuint DoSimplifyIndices(
		uint* pWelded,
		uint* pDest,
		float* pWeldedPositions,
		nuint weldedVertexCount,
		float* pWeldedTexCoords,
		float* pAttributeWeights,
		int targetIndexCount,
		float targetError,
		nuint origLength)
	{
		float resultError = 0f;

		return MeshOptimizerNative.meshopt_simplifyWithAttributes(
			pDest, pWelded, origLength,
			pWeldedPositions, weldedVertexCount, 3 * sizeof(float),
			pWeldedTexCoords, 2 * sizeof(float), pAttributeWeights, 2,
			null, (nuint)targetIndexCount, targetError, MeshOptimizerNative.SimplifyPrune, &resultError);
	}

	private static float GetLodTargetError(int lodTier)
	{
		return lodTier switch
		{
			1 => 0.05f,
			2 => 0.15f,
			_ => 0.5f
		};
	}
}
