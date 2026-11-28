using System.Collections.Generic;

namespace Realm.MapGeneration;

public class MapAccuracyReport
{
	public string MapName { get; set; } = string.Empty;

	public string MinimapSource { get; set; } = string.Empty;

	public int Width { get; set; }

	public int Depth { get; set; }

	public float HeightCorrelation { get; set; }

	public float HeightRmse { get; set; }

	public float HeightBandAccuracy { get; set; }

	public float WaterIou { get; set; }

	public float WaterPrecision { get; set; }

	public float WaterRecall { get; set; }

	public float WaterSurfaceConsistency { get; set; }

	public float PropF1 { get; set; }

	public float PropPrecision { get; set; }

	public float PropRecall { get; set; }

	public int OriginalPropCount { get; set; }

	public int GeneratedPropCount { get; set; }

	public float PropCountRatio { get; set; }

	public float TextureKindAccuracy { get; set; }

	public float TextureColourError { get; set; }

	public float AssetCategoryCoverage { get; set; }

	public int GeneratedDistinctAssets { get; set; }

	public int OriginalDistinctAssets { get; set; }

	public float RenderSimilarity { get; set; }

	public float RenderColourError { get; set; }

	public Dictionary<PropCategory, PropCategoryScore> PropByCategory { get; set; } = new();

	public Dictionary<TerrainSurfaceKind, float> TextureKindAgreement { get; set; } = new();

	public override string ToString()
	{
		return $"{MapName}: height r={HeightCorrelation:F3} rmse={HeightRmse:F2} band={HeightBandAccuracy:P1} | water IoU={WaterIou:P1} P={WaterPrecision:P1} R={WaterRecall:P1} | props F1={PropF1:P1} P={PropPrecision:P1} R={PropRecall:P1} ({GeneratedPropCount}/{OriginalPropCount}) | textures kind={TextureKindAccuracy:P1} colour={TextureColourError:F3} | assets={AssetCategoryCoverage:P1} ({GeneratedDistinctAssets}) | render={RenderSimilarity:F3}";
	}
}
