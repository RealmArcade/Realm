using System;
using System.Collections.Generic;
using Godot;
using Realm.EditorAPI;
using Realm.MapGeneration;

namespace Realm.Client.Services.MapGeneration;

public class TerrainImageImportResult
{
	public bool Success { get; init; }

	public string Message { get; init; } = string.Empty;

	public int PropCount { get; init; }
}

public class TerrainImageImporter
{
	private static readonly string[] TreeKeywords = { "tree", "palm", "conifer", "oak", "pine", "bush" };
	private static readonly string[] NatureKeywords = { "fern", "flower", "mushroom", "grass", "reed", "bamboo", "vine" };
	private static readonly string[] RockKeywords = { "rock", "boulder", "stone", "crystal", "ore", "rubble" };
	private static readonly string[] StructureKeywords = { "column", "pillar", "statue", "wall", "gate", "tower", "altar", "shrine", "ruin", "crate", "barrel", "fence" };

	public TerrainImageImportResult Import(IEditorAPI editorApi, string imagePath)
	{
		editorApi.ResetMapToBlank();

		TerrainGridInfo gridInfo = editorApi.GetTerrainGridInfo();
		if (gridInfo.Width <= 0 || gridInfo.Depth <= 0)
		{
			return new TerrainImageImportResult { Success = false, Message = TranslationServer.Translate("The editor has no terrain grid to import into.") };
		}

		if (!TryLoadImage(imagePath, out MinimapImage? image, out string decodeError))
		{
			return new TerrainImageImportResult { Success = false, Message = decodeError };
		}

		MapGenerationPalette palette = BuildPalette(editorApi);
		if (palette.IsEmpty)
		{
			return new TerrainImageImportResult { Success = false, Message = TranslationServer.Translate("The workspace has no terrain textures to paint with.") };
		}

		PropCatalog catalog = BuildCatalog(editorApi);
		var options = new TerrainReconstructionOptions
		{
			Width = gridInfo.Width,
			Depth = gridInfo.Depth,
			QuadSize = gridInfo.QuadSize,
			TierHeight = gridInfo.TierHeight,
			Seed = DeterministicSeed.HashText(imagePath)
		};

		ReconstructedTerrain terrain = MinimapTerrainReconstructor.Reconstruct(image, palette, catalog, options);
		if (!editorApi.ApplyReconstructedTerrain(
			    terrain.Width,
			    terrain.Depth,
			    terrain.VertexHeights,
			    terrain.SplatIndices,
			    terrain.CliffSplatIndices,
			    terrain.WaterModes,
			    terrain.WaterSurfaceHeights,
			    terrain.WaterProfileIndex))
		{
			return new TerrainImageImportResult { Success = false, Message = TranslationServer.Translate("The imported terrain did not match the editor grid.") };
		}

		int propCount = PlaceProps(editorApi, terrain);
		editorApi.ResetWorkspaceFileWatchTimestamps();
		editorApi.RefreshEditorAfterTerrainChange(true);
		editorApi.MarkDocumentModified();

		return new TerrainImageImportResult { Success = true, Message = "Terrain imported from minimap image successfully!", PropCount = propCount };
	}

	private static int PlaceProps(IEditorAPI editorApi, ReconstructedTerrain terrain)
	{
		int placed = 0;
		for (int propIndex = 0; propIndex < terrain.Props.Count; propIndex++)
		{
			GeneratedProp prop = terrain.Props[propIndex];
			if (editorApi.PlaceProp(prop.TemplateId, prop.X, prop.Y, prop.Z, prop.RotationY, prop.Scale))
			{
				placed++;
			}
		}

		return placed;
	}

	private static MapGenerationPalette BuildPalette(IEditorAPI editorApi)
	{
		var palette = new MapGenerationPalette();
		var library = new MinimapTextureLibrary();
		TerrainTextureInfo[] textures = editorApi.GetTerrainTextureInfo();
		for (int textureIndex = 0; textureIndex < textures.Length; textureIndex++)
		{
			TerrainTextureInfo texture = textures[textureIndex];
			if (string.IsNullOrWhiteSpace(texture.TexturePath))
			{
				continue;
			}

			var source = new MapTextureSource(texture.SwatchIndex, texture.Name, texture.TexturePath, 4f, 1f, 1f, 1f, 1f, 0);
			MinimapTexture sampled = library.Get(source);
			palette.Add(texture.SwatchIndex, texture.Name, 0, sampled.AverageRed, sampled.AverageGreen, sampled.AverageBlue);
		}

		return palette;
	}

	private static PropCatalog BuildCatalog(IEditorAPI editorApi)
	{
		var catalog = new PropCatalog();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddIds(catalog, seen, editorApi.ResolvePropAssetIds(TreeKeywords, true));
		AddIds(catalog, seen, editorApi.ResolvePropAssetIds(NatureKeywords, false));
		AddIds(catalog, seen, editorApi.ResolvePropAssetIds(RockKeywords, false));
		AddIds(catalog, seen, editorApi.ResolvePropAssetIds(StructureKeywords, false));
		if (catalog.IsEmpty)
		{
			AddIds(catalog, seen, editorApi.ResolvePropAssetIds(new[] { string.Empty }, false));
		}

		return catalog;
	}

	private static void AddIds(PropCatalog catalog, HashSet<string> seen, List<string> templateIds)
	{
		for (int templateIndex = 0; templateIndex < templateIds.Count; templateIndex++)
		{
			string templateId = templateIds[templateIndex];
			if (string.IsNullOrWhiteSpace(templateId) || !seen.Add(templateId))
			{
				continue;
			}

			catalog.Add(templateId, templateId, 1f);
		}
	}

	private static bool TryLoadImage(string imagePath, out MinimapImage? image, out string error)
	{
		image = null;
		error = string.Empty;

		if (string.IsNullOrWhiteSpace(imagePath) || !System.IO.File.Exists(imagePath))
		{
			error = TranslationServer.Translate("The selected image file could not be found.");
			return false;
		}

		try
		{
			image = MinimapImage.FromFile(imagePath);
			return true;
		}
		catch (Exception exception)
		{
			error = string.Format(TranslationServer.Translate("The selected image could not be decoded: {0}"), exception.Message);
			return false;
		}
	}
}
