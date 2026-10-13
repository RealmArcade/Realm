namespace Realm.Client.Services.ModelOptimization;

public struct LodTierConfig
{
	public int TierIndex { get; set; }
	public string Name { get; set; }
	public int ViewportHeight { get; set; }
	public float TargetError { get; set; }
	public float QualityThreshold { get; set; }
	public float TargetRatio { get; set; }
	public int MaxBoneInfluences { get; set; }
	public float BoneWeightThreshold { get; set; }
	public bool CastShadow { get; set; }
	public float VisibilityRangeBegin { get; set; }
	public float VisibilityRangeEnd { get; set; }

	public LodTierConfig(
		int tierIndex,
		string name,
		int viewportHeight,
		float targetError,
		float qualityThreshold,
		float targetRatio,
		int maxBoneInfluences,
		bool castShadow,
		float visBegin,
		float visEnd,
		float boneWeightThreshold = 0.01f)
	{
		TierIndex = tierIndex;
		Name = name;
		ViewportHeight = viewportHeight;
		TargetError = targetError;
		QualityThreshold = qualityThreshold;
		TargetRatio = targetRatio;
		MaxBoneInfluences = maxBoneInfluences;
		BoneWeightThreshold = boneWeightThreshold;
		CastShadow = castShadow;
		VisibilityRangeBegin = visBegin;
		VisibilityRangeEnd = visEnd;
	}

	public static LodTierConfig[] CreateDefaultTiers(float allowedPixelError = 1.5f)
	{
		return new LodTierConfig[]
		{
			new LodTierConfig(0, "LOD0", 800, ModelOptimizerService.CalculateTargetErrorFromScreenHeight(800, allowedPixelError), 0.94f, 1.00f, 4, true, 0f, 45f),
			new LodTierConfig(1, "LOD1", 250, ModelOptimizerService.CalculateTargetErrorFromScreenHeight(250, allowedPixelError), 0.87f, 0.50f, 2, true, 45f, 90f),
			new LodTierConfig(2, "LOD2", 100, ModelOptimizerService.CalculateTargetErrorFromScreenHeight(100, allowedPixelError), 0.78f, 0.25f, 1, true, 90f, 150f),
			new LodTierConfig(3, "LOD3", 35, ModelOptimizerService.CalculateTargetErrorFromScreenHeight(35, allowedPixelError), 0.68f, 0.10f, 1, true, 150f, 0f)
		};
	}
}