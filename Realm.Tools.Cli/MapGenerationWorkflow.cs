using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Realm.MapGeneration;

namespace Realm.Tools.Cli;

public static class MapGenerationWorkflow
{
	private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };

	public static ReconstructedTerrain ReconstructFromImage(string imagePath, string templateFolder, int width, int depth, int seed)
	{
		MinimapImage image = MinimapImage.FromFile(imagePath);
		MapFolderTerrain template = MapFolderTerrainReader.Read(templateFolder);
		var palette = template.BuildPalette(new MinimapTextureLibrary());
		PropCatalog catalog = MapFolderTerrainReader.ReadPropCatalog(templateFolder);
		var options = new TerrainReconstructionOptions { Width = width, Depth = depth, Seed = seed, QuadSize = 2f, TierHeight = 3f };
		return MinimapTerrainReconstructor.Reconstruct(image, palette, catalog, options);
	}

	public static string WriteGeneratedFolder(ReconstructedTerrain terrain, string templateFolder, string outputFolder, string mapName)
	{
		MapFolderTerrainWriter.Write(templateFolder, outputFolder, terrain, mapName);
		MapFolderTerrain template = MapFolderTerrainReader.Read(templateFolder);
		MapFolderTerrain snapshot = MapFolderTerrainConverter.CreateSnapshot(terrain, template);
		MinimapImage thumbnail = MinimapRenderer.Render(snapshot, new MinimapRenderOptions { Resolution = 512 });
		string thumbnailPath = Path.Combine(outputFolder, "generated_minimap.png");
		thumbnail.SavePng(thumbnailPath);
		return thumbnailPath;
	}

	public static MapAccuracyReport CompareFolders(string originalFolder, string generatedFolder, string minimapSource, MinimapImage inputImage)
	{
		MapFolderTerrain original = MapFolderTerrainReader.Read(originalFolder);
		MapFolderTerrain generated = MapFolderTerrainReader.Read(generatedFolder);
		MapAccuracyReport report = MapReconstructionMetrics.Compare(original, generated, 6f);
		report.MinimapSource = minimapSource;

		MapFolderTerrain snapshot = MapFolderTerrainConverter.CreateSnapshot(
			ReconstructSnapshot(generated),
			MapFolderTerrainReader.Read(generatedFolder));
		MinimapImage rendered = MinimapRenderer.Render(snapshot, new MinimapRenderOptions { Resolution = 512 });
		MinimapSimilarityResult similarity = MinimapSimilarity.Compare(inputImage, rendered);
		report.RenderSimilarity = similarity.StructuralSimilarity;
		report.RenderColourError = similarity.ColourError;
		return report;
	}

	public static ReconstructedTerrain ReconstructSnapshot(MapFolderTerrain terrain)
	{
		var reconstructed = new ReconstructedTerrain
		{
			Width = terrain.Width,
			Depth = terrain.Depth,
			VertexHeights = terrain.VertexHeights,
			CellHeights = terrain.CellHeights,
			WaterSurfaceHeights = terrain.WaterSurfaceHeights,
			WaterModes = terrain.WaterModes,
			SplatIndices = terrain.SplatIndices,
			CliffSplatIndices = terrain.SplatIndices,
			PathingCodes = terrain.PathingCodes,
			WaterProfileIndex = 0
		};
		for (int index = 0; index < terrain.Props.Count; index++)
		{
			MapPlacedProp prop = terrain.Props[index];
			reconstructed.Props.Add(new GeneratedProp(prop.TemplateId, PropCatalogEntry.Classify(prop.TemplateId, prop.TemplateId), prop.X, prop.Y, prop.Z, prop.RotationY, prop.Scale));
		}

		return reconstructed;
	}

	public static string ResolveMinimap(string mapFolder, bool useThumbnails, int resolution, string outputDirectory)
	{
		if (useThumbnails)
		{
			string thumbnailPath = Path.Combine(mapFolder, "thumbnail.webp");
			if (File.Exists(thumbnailPath))
			{
				return thumbnailPath;
			}
		}

		string renderedPath = Path.Combine(outputDirectory, Path.GetFileName(Path.TrimEndingDirectorySeparator(mapFolder)) + "_input.png");
		MinimapRenderer.SaveFolder(mapFolder, renderedPath, new MinimapRenderOptions { Resolution = resolution });
		return renderedPath;
	}

	public static string WriteReport(string outputPath, IReadOnlyList<MapAccuracyReport> reports, MapAccuracyReport aggregate)
	{
		var payload = new
		{
			maps = reports,
			aggregate
		};
		File.WriteAllText(outputPath, JsonSerializer.Serialize(payload, ReportOptions));
		return outputPath;
	}

	public static MapAccuracyReport Aggregate(IReadOnlyList<MapAccuracyReport> reports)
	{
		var aggregate = new MapAccuracyReport { MapName = "aggregate" };
		if (reports.Count == 0)
		{
			return aggregate;
		}

		foreach (MapAccuracyReport report in reports)
		{
			aggregate.HeightCorrelation += report.HeightCorrelation;
			aggregate.HeightRmse += report.HeightRmse;
			aggregate.HeightBandAccuracy += report.HeightBandAccuracy;
			aggregate.WaterIou += report.WaterIou;
			aggregate.WaterPrecision += report.WaterPrecision;
			aggregate.WaterRecall += report.WaterRecall;
			aggregate.WaterSurfaceConsistency += report.WaterSurfaceConsistency;
			aggregate.PropF1 += report.PropF1;
			aggregate.PropPrecision += report.PropPrecision;
			aggregate.PropRecall += report.PropRecall;
			aggregate.TextureKindAccuracy += report.TextureKindAccuracy;
			aggregate.AssetCategoryCoverage += report.AssetCategoryCoverage;
			aggregate.RenderSimilarity += report.RenderSimilarity;
			aggregate.RenderColourError += report.RenderColourError;
			aggregate.OriginalPropCount += report.OriginalPropCount;
			aggregate.GeneratedPropCount += report.GeneratedPropCount;
		}

		float count = reports.Count;
		aggregate.HeightCorrelation /= count;
		aggregate.HeightRmse /= count;
		aggregate.HeightBandAccuracy /= count;
		aggregate.WaterIou /= count;
		aggregate.WaterPrecision /= count;
		aggregate.WaterRecall /= count;
		aggregate.WaterSurfaceConsistency /= count;
		aggregate.PropF1 /= count;
		aggregate.PropPrecision /= count;
		aggregate.PropRecall /= count;
		aggregate.TextureKindAccuracy /= count;
		aggregate.AssetCategoryCoverage /= count;
		aggregate.RenderSimilarity /= count;
		aggregate.RenderColourError /= count;
		return aggregate;
	}
}
