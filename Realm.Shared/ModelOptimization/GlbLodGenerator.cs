using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

public static unsafe partial class GlbLodGenerator
	{
	public static readonly float[] DefaultLodRatios = new float[] { 1.0f, 0.50f, 0.25f, 0.10f };
	public static readonly float[] DefaultVisibilityBegins = new float[] { 0f, 9f, 19f, 40f };
	public static readonly float[] DefaultVisibilityEnds = new float[] { 9f, 19f, 40f, 0f };

	public static (bool Success, byte[] OutputGlbBytes, string ErrorMessage, int[] LodTriangleCounts) GenerateLods(
		byte[] inputGlbBytes,
		float[]? lodRatios = null,
		float[]? visBegins = null,
		float[]? visEnds = null,
		LodDecimationProfile? decimationProfile = null,
		float uvSeamMergeTolerance = 0f)
	{
		if (inputGlbBytes == null || inputGlbBytes.Length < 20)
			return (false, inputGlbBytes ?? Array.Empty<byte>(), "Invalid GLB buffer", Array.Empty<int>());

		if (!AreLodParametersValid(lodRatios, visBegins, visEnds))
		{
			lodRatios = DefaultLodRatios;
			visBegins = DefaultVisibilityBegins;
			visEnds = DefaultVisibilityEnds;
		}

		try
		{
			return ProcessGlb(inputGlbBytes, lodRatios!, visBegins!, visEnds!, decimationProfile, uvSeamMergeTolerance);
		}
		catch (Exception ex)
		{
			return (false, inputGlbBytes, $"Failed to generate LODs: {ex.Message}", Array.Empty<int>());
		}
	}

	private static bool AreLodParametersValid(float[]? lodRatios, float[]? visBegins, float[]? visEnds)
	{
		if (lodRatios == null || visBegins == null || visEnds == null) return false;
		if (lodRatios.Length < 2 || visBegins.Length != lodRatios.Length || visEnds.Length != lodRatios.Length) return false;
		return IsDecreasingRatios(lodRatios);
	}

}
