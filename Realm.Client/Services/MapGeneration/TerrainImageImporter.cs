using System;
using System.Collections.Generic;
using Godot;
using Realm.EditorAPI;
using SkiaSharp;

namespace Realm.Client.Services.MapGeneration;

public class TerrainImageImportResult
{
	public bool Success { get; init; }
	public string Message { get; init; } = string.Empty;
	public int TreeCount { get; init; }
}

public class TerrainImageImporter
{
	private const float WaterChannelThreshold = 0.06f;
	private const float GreenChannelThreshold = 0.04f;
	private const float CliffGreenOverBlueThreshold = 0.06f;
	private const float CliffRedOverBlueThreshold = 0.1f;
	private const float NeutralChannelTolerance = 0.08f;
	private const float NeutralMinimumBrightness = 0.2f;
	private const float ForestDarknessThreshold = 0.35f;
	private const float TreeJitterWorldUnits = 0.75f;

	private enum TerrainClass
	{
		Water,
		Grass,
		Forest,
		Cliff,
		Stone,
	}

	public TerrainImageImportResult Import(IEditorAPI editorApi, string imagePath)
	{
		editorApi.ResetMapToBlank();

		TerrainGridInfo gridInfo = editorApi.GetTerrainGridInfo();
		if (gridInfo.Width <= 0 || gridInfo.Depth <= 0)
		{
			return new TerrainImageImportResult { Success = false, Message = TranslationServer.Translate("The editor has no terrain grid to import into.") };
		}

		if (!TryDecodeFirstFrame(imagePath, out SKColor[] pixels, out int imageWidth, out int imageHeight, out string decodeError))
		{
			return new TerrainImageImportResult { Success = false, Message = decodeError };
		}

		TerrainMaterialPalette palette = editorApi.GetTerrainMaterialPalette();
		int cellCount = gridInfo.Width * gridInfo.Depth;
		var tiers = new sbyte[cellCount];
		var materials = new int[cellCount];
		var forestMask = new bool[gridInfo.Width, gridInfo.Depth];

		for (int z = 0; z < gridInfo.Depth; z++)
		{
			for (int x = 0; x < gridInfo.Width; x++)
			{
				float normalisedX = (x + 0.5f) / gridInfo.Width;
				float normalisedZ = (z + 0.5f) / gridInfo.Depth;
				SampleBilinear(pixels, imageWidth, imageHeight, normalisedX, normalisedZ, out float red, out float green, out float blue);
				TerrainClass terrainClass = Classify(red, green, blue);

				int cellIndex = z * gridInfo.Width + x;
				switch (terrainClass)
				{
					case TerrainClass.Water:
						tiers[cellIndex] = -1;
						materials[cellIndex] = palette.Water;
						break;
					case TerrainClass.Cliff:
						tiers[cellIndex] = 1;
						materials[cellIndex] = palette.Cliff;
						break;
					case TerrainClass.Forest:
						tiers[cellIndex] = 0;
						materials[cellIndex] = palette.Ground;
						forestMask[x, z] = true;
						break;
					default:
						tiers[cellIndex] = 0;
						materials[cellIndex] = palette.Ground;
						break;
				}
			}
		}

		int cliffTextureIndex = editorApi.GetSelectedCliffTextureIndex();
		if (!editorApi.ApplyGeneratedTerrain(gridInfo.Width, gridInfo.Depth, tiers, materials, cliffTextureIndex))
		{
			return new TerrainImageImportResult { Success = false, Message = TranslationServer.Translate("The imported terrain did not match the editor grid.") };
		}

		FillWaterBasins(editorApi, tiers, gridInfo.Width, gridInfo.Depth);
		editorApi.RebuildTerrain();

		int treeCount = SpawnForestTrees(editorApi, gridInfo, forestMask, imagePath);
		editorApi.ResetWorkspaceFileWatchTimestamps();
		editorApi.RefreshEditorAfterTerrainChange(true);
		editorApi.MarkDocumentModified();

		return new TerrainImageImportResult { Success = true, Message = "Terrain imported from minimap image successfully!", TreeCount = treeCount };
	}

	private static void FillWaterBasins(IEditorAPI editorApi, sbyte[] tiers, int width, int depth)
	{
		var visited = new bool[width * depth];
		var regionCells = new List<int>();
		int scanIndex = 0;
		while (WaterPoolPlanner.TryFindNextPool(tiers, visited, width, depth, 1, ref scanIndex, regionCells, out WaterPoolSelection pool))
		{
			editorApi.FillWaterPoolAtCell(pool.SeedX, pool.SeedZ, pool.SurfaceY);
		}
	}

	private static bool TryDecodeFirstFrame(string imagePath, out SKColor[] pixels, out int width, out int height, out string error)
	{
		pixels = Array.Empty<SKColor>();
		width = 0;
		height = 0;
		error = string.Empty;

		if (string.IsNullOrWhiteSpace(imagePath) || !System.IO.File.Exists(imagePath))
		{
			error = TranslationServer.Translate("The selected image file could not be found.");
			return false;
		}

		try
		{
			using SKCodec? codec = SKCodec.Create(imagePath);
			if (codec == null)
			{
				error = TranslationServer.Translate("The selected file is not a readable image.");
				return false;
			}

			width = codec.Info.Width;
			height = codec.Info.Height;
			if (width <= 0 || height <= 0)
			{
				error = TranslationServer.Translate("The selected image has no pixels.");
				return false;
			}

			var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
			using var bitmap = new SKBitmap(imageInfo);
			SKCodecResult result = codec.GetPixels(imageInfo, bitmap.GetPixels());
			if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
			{
				error = string.Format(TranslationServer.Translate("The selected image could not be decoded ({0})."), result);
				return false;
			}

			pixels = bitmap.Pixels;
			return true;
		}
		catch (Exception exception)
		{
			error = string.Format(TranslationServer.Translate("The selected image could not be decoded: {0}"), exception.Message);
			return false;
		}
	}

	private static void SampleBilinear(SKColor[] pixels, int width, int height, float normalisedX, float normalisedZ, out float red, out float green, out float blue)
	{
		float pixelX = Math.Clamp(normalisedX * (width - 1), 0f, Math.Max(0, width - 1));
		float pixelZ = Math.Clamp(normalisedZ * (height - 1), 0f, Math.Max(0, height - 1));
		int x0 = Math.Clamp((int)pixelX, 0, Math.Max(0, width - 1));
		int z0 = Math.Clamp((int)pixelZ, 0, Math.Max(0, height - 1));
		int x1 = Math.Min(x0 + 1, width - 1);
		int z1 = Math.Min(z0 + 1, height - 1);
		float tx = pixelX - x0;
		float tz = pixelZ - z0;

		SKColor topLeft = pixels[z0 * width + x0];
		SKColor topRight = pixels[z0 * width + x1];
		SKColor bottomLeft = pixels[z1 * width + x0];
		SKColor bottomRight = pixels[z1 * width + x1];

		red = BlendChannel(topLeft.Red, topRight.Red, bottomLeft.Red, bottomRight.Red, tx, tz);
		green = BlendChannel(topLeft.Green, topRight.Green, bottomLeft.Green, bottomRight.Green, tx, tz);
		blue = BlendChannel(topLeft.Blue, topRight.Blue, bottomLeft.Blue, bottomRight.Blue, tx, tz);
	}

	private static float BlendChannel(byte topLeft, byte topRight, byte bottomLeft, byte bottomRight, float tx, float tz)
	{
		float top = (topLeft + (topRight - topLeft) * tx) / 255f;
		float bottom = (bottomLeft + (bottomRight - bottomLeft) * tx) / 255f;
		return top + (bottom - top) * tz;
	}

	private static TerrainClass Classify(float red, float green, float blue)
	{
		if (blue > red + WaterChannelThreshold && blue > green + WaterChannelThreshold)
		{
			return TerrainClass.Water;
		}

		if (green > red + GreenChannelThreshold && green > blue + GreenChannelThreshold)
		{
			float brightness = Math.Max(red, Math.Max(green, blue));
			return brightness < ForestDarknessThreshold ? TerrainClass.Forest : TerrainClass.Grass;
		}

		if (red > green + CliffGreenOverBlueThreshold && red > blue + CliffRedOverBlueThreshold && green > blue)
		{
			return TerrainClass.Cliff;
		}

		float maximumChannel = Math.Max(red, Math.Max(green, blue));
		float minimumChannel = Math.Min(red, Math.Min(green, blue));
		if (maximumChannel - minimumChannel <= NeutralChannelTolerance && maximumChannel > NeutralMinimumBrightness)
		{
			return TerrainClass.Stone;
		}

		if (blue > red && blue > green)
		{
			return TerrainClass.Water;
		}

		if (green > red && green > blue)
		{
			return TerrainClass.Grass;
		}

		return red > green && red > blue ? TerrainClass.Cliff : TerrainClass.Stone;
	}

	private static int SpawnForestTrees(IEditorAPI editorApi, TerrainGridInfo gridInfo, bool[,] forestMask, string imagePath)
	{
		List<string> treeIds = editorApi.ResolvePropAssetIds(new[] { "tree", "palm", "conifer", "oak", "pine", "bush" }, true);
		if (treeIds.Count == 0)
		{
			treeIds = editorApi.ResolvePropAssetIds(new[] { "resource/" }, true);
		}

		if (treeIds.Count == 0)
		{
			treeIds = editorApi.ResolvePropAssetIds(new[] { string.Empty }, false);
		}

		if (treeIds.Count == 0)
		{
			return 0;
		}

		var random = new SeededRandom(DeterministicSeed.HashText(imagePath));
		var visited = new bool[gridInfo.Width, gridInfo.Depth];
		var blobCells = new List<(int X, int Z)>();
		var queue = new Queue<(int X, int Z)>();
		int spawnedTrees = 0;

		for (int z = 0; z < gridInfo.Depth; z++)
		{
			for (int x = 0; x < gridInfo.Width; x++)
			{
				if (!forestMask[x, z] || visited[x, z])
				{
					continue;
				}

				blobCells.Clear();
				queue.Clear();
				queue.Enqueue((x, z));
				visited[x, z] = true;

				while (queue.Count > 0)
				{
					(int X, int Z) cell = queue.Dequeue();
					blobCells.Add(cell);
					EnqueueForestNeighbour(cell.X - 1, cell.Z, gridInfo, forestMask, visited, queue);
					EnqueueForestNeighbour(cell.X + 1, cell.Z, gridInfo, forestMask, visited, queue);
					EnqueueForestNeighbour(cell.X, cell.Z - 1, gridInfo, forestMask, visited, queue);
					EnqueueForestNeighbour(cell.X, cell.Z + 1, gridInfo, forestMask, visited, queue);
				}

				float density = blobCells.Count > 50 ? 0.55f : blobCells.Count > 15 ? 0.35f : 0.15f;
				foreach ((int X, int Z) cell in blobCells)
				{
					if (ComputeScatterHash(cell.X, cell.Z) > density)
					{
						continue;
					}

					string treeId = treeIds[random.NextInt(treeIds.Count)];
					float worldX = (cell.X + 0.5f - gridInfo.Width * 0.5f) * gridInfo.QuadSize + random.NextFloat(-TreeJitterWorldUnits, TreeJitterWorldUnits);
					float worldZ = (cell.Z + 0.5f - gridInfo.Depth * 0.5f) * gridInfo.QuadSize + random.NextFloat(-TreeJitterWorldUnits, TreeJitterWorldUnits);
					float yaw = random.NextFloat(0f, 360f);
					float scale = random.NextFloat(0.8f, 1.2f);
					if (editorApi.PlaceProp(treeId, worldX, 0f, worldZ, yaw, scale))
					{
						spawnedTrees++;
					}
				}
			}
		}

		return spawnedTrees;
	}

	private static void EnqueueForestNeighbour(int x, int z, TerrainGridInfo gridInfo, bool[,] forestMask, bool[,] visited, Queue<(int X, int Z)> queue)
	{
		if (x < 0 || x >= gridInfo.Width || z < 0 || z >= gridInfo.Depth)
		{
			return;
		}

		if (visited[x, z] || !forestMask[x, z])
		{
			return;
		}

		visited[x, z] = true;
		queue.Enqueue((x, z));
	}

	private static float ComputeScatterHash(int x, int z)
	{
		unchecked
		{
			uint hash = (uint)(x * 73856093) ^ (uint)(z * 19349663);
			hash ^= hash >> 13;
			hash *= 0x85ebca6bu;
			hash ^= hash >> 16;
			return (hash & 0xFFFFFF) / 16777215.0f;
		}
	}
}
