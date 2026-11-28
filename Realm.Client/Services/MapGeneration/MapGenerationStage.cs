namespace Realm.Client.Services.MapGeneration;

public enum MapGenerationStage
{
	Preparing,
	ShapingLandMasses,
	ClearingStartAreas,
	CarvingPaths,
	MeasuringDensity,
	RaisingMountainsAndWater,
	SmoothingTiers,
	BuildingHeights,
	PaintingMaterials,
	UploadingTerrain,
	FillingWater,
	ApplyingPathing,
	MeasuringDistanceToPaths,
	ScatteringTrees,
	PlantingTrees,
	ScatteringDecorations,
	PlacingDecorations,
	Finishing,
}
