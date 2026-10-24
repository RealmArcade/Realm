namespace Realm.Shared.ModelOptimization;

public enum MeshSurfaceCategory
{
	Organic = 0,
	HardEdged = 1
}

public readonly struct LodDecimationProfile
{
	public const float HardEdgedSharpEdgeRatioThreshold = 0.30f;
	public const int HardEdgedMaximumTriangleCount = 4000;
	public const float FeatureEdgeAngleDegrees = 35.0f;

	public LodDecimationProfile(
		string name,
		MeshSurfaceCategory category,
		float minimumRatio,
		float targetErrorScale,
		float texCoordWeight)
	{
		Name = name;
		Category = category;
		MinimumRatio = minimumRatio;
		TargetErrorScale = targetErrorScale;
		TexCoordWeight = texCoordWeight;
	}

	public string Name { get; }
	public MeshSurfaceCategory Category { get; }
	public float MinimumRatio { get; }
	public float TargetErrorScale { get; }
	public float TexCoordWeight { get; }

	public static LodDecimationProfile Organic { get; } = new("organic", MeshSurfaceCategory.Organic, 0.0f, 1.0f, 1.0f);

	public static LodDecimationProfile HardEdged { get; } = new("hard-edged", MeshSurfaceCategory.HardEdged, 0.20f, 0.55f, 2.0f);

	public static LodDecimationProfile ForCategory(MeshSurfaceCategory category)
	{
		return category == MeshSurfaceCategory.HardEdged ? HardEdged : Organic;
	}

	public static LodDecimationProfile Detect(float sharpEdgeRatio, int triangleCount, MeshSurfaceCategory fallback = MeshSurfaceCategory.Organic)
	{
		if (float.IsNaN(sharpEdgeRatio)) return ForCategory(fallback);
		if (triangleCount > HardEdgedMaximumTriangleCount) return Organic;
		return sharpEdgeRatio >= HardEdgedSharpEdgeRatioThreshold ? HardEdged : Organic;
	}

	public float GetMinimumRatioForTier(float tierRatio)
	{
		return tierRatio < MinimumRatio ? MinimumRatio : tierRatio;
	}
}
