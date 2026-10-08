using Arch.Core;
using Realm.Ecs.Services;
using Godot;
using Realm.Ecs.Components.Terrain;
using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;

public class MapEditorTerrainImportService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World EcsWorld => _ecsWorldAccessor.Current;

	public MapEditorTerrainImportService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	public bool ImportTerrain(
		Entity worldEntity,
		string selectedPath,
		out float[,] smoothedHeights,
		out TerrainSplatWeights[,] splatMap,
		out List<(float X, float Y, float Z, float Rot, float Scale)> treePositions)
	{
		smoothedHeights = null;
		splatMap = null;
		treePositions = new List<(float X, float Y, float Z, float Rot, float Scale)>();

		if (!EcsWorld.TryGet<TerrainState>(worldEntity, out var terrain))
		{
			return false;
		}

		var img = LoadImageForImport(selectedPath);
		if (img == null)
		{
			return false;
		}

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		float[,] heights = new float[width, depth];
		splatMap = new TerrainSplatWeights[width, depth];
		bool[,] isTreeColored = new bool[width, depth];

		ProcessTerrainCells(img, terrain, heights, splatMap, isTreeColored, width, depth);

		smoothedHeights = heights;

		ProcessTreeGeneration(isTreeColored, smoothedHeights, treePositions, width, depth, quadSize);

		return true;
	}

	private void ProcessTerrainCells(
		Image img, 
		TerrainState terrain, 
		float[,] heights, 
		TerrainSplatWeights[,] splatMap, 
		bool[,] isTreeColored, 
		int width, 
		int depth)
	{
		for (int gz = 0; gz < depth; gz++)
		{
			for (int gx = 0; gx < width; gx++)
			{
				ProcessTerrainCell(img, terrain, heights, splatMap, isTreeColored, width, depth, gx, gz);
			}
		}
	}

	private void ProcessTerrainCell(
		Image img, 
		TerrainState terrain, 
		float[,] heights, 
		TerrainSplatWeights[,] splatMap, 
		bool[,] isTreeColored, 
		int width, 
		int depth, 
		int gx, 
		int gz)
	{
		var color = InterpolateColor(img, width, depth, gx, gz);
		string type = DetermineTerrainType(color.R, color.G, color.B);
		
		(sbyte tier, int texIdx) = GetTierAndTextureIndex(type);
		
		if (type == "forest")
		{
			isTreeColored[gx, gz] = true;
		}

		if (terrain.Cells != null)
		{
			float h = tier * EditableTerrain.TIER_HEIGHT;
			terrain.Cells[gx, gz] = new TerrainCell(h);
		}
		heights[gx, gz] = tier * EditableTerrain.TIER_HEIGHT;
		splatMap[gx, gz] = TerrainSplatWeights.CreateSolid(texIdx);
	}

	private Color InterpolateColor(Image img, int width, int depth, int gx, int gz)
	{
		float srcX = (gx / (float)(width - 1)) * (img.GetWidth() - 1);
		float srcZ = (gz / (float)(depth - 1)) * (img.GetHeight() - 1);

		int x0 = (int)MathF.Floor(srcX);
		int x1 = Math.Min(x0 + 1, img.GetWidth() - 1);
		int z0 = (int)MathF.Floor(srcZ);
		int z1 = Math.Min(z0 + 1, img.GetHeight() - 1);

		float tx = srcX - x0;
		float tz = srcZ - z0;

		Color p00 = img.GetPixel(x0, z0);
		Color p10 = img.GetPixel(x1, z0);
		Color p01 = img.GetPixel(x0, z1);
		Color p11 = img.GetPixel(x1, z1);

		float r = (1f - tx) * (1f - tz) * p00.R + tx * (1f - tz) * p10.R + (1f - tx) * tz * p01.R + tx * tz * p11.R;
		float g = (1f - tx) * (1f - tz) * p00.G + tx * (1f - tz) * p10.G + (1f - tx) * tz * p01.G + tx * tz * p11.G;
		float b = (1f - tx) * (1f - tz) * p00.B + tx * (1f - tz) * p10.B + (1f - tx) * tz * p01.B + tx * tz * p11.B;
		
		return new Color(r, g, b);
	}

	private string DetermineTerrainType(float r, float g, float b)
	{
		if (b > r + 0.06f && b > g + 0.06f)
		{
			return "water";
		}
		
		if (g > r + 0.04f && g > b + 0.04f)
		{
			if (r < 0.4f && g < 0.5f && b < 0.4f)
				return "forest";
			return "grass";
		}
		
		if (r > g + 0.06f && r > b + 0.1f && g > b)
		{
			return "cliff";
		}
		
		if (MathF.Abs(r - g) < 0.08f && MathF.Abs(g - b) < 0.08f && MathF.Abs(r - b) < 0.08f && r > 0.2f)
		{
			return "stone";
		}
		
		float max = Math.Max(r, Math.Max(g, b));
		if (max == b) return "water";
		if (max == g) return "grass";
		if (max == r && g > b) return "cliff";
		
		return "stone";
	}

	private (sbyte, int) GetTierAndTextureIndex(string type)
	{
		switch (type)
		{
			case "water":
				return (-1, 9);
			case "cliff":
				return (1, 0);
			case "forest":
			case "grass":
			case "stone":
				return (0, 3);
			default:
				return (0, 3);
		}
	}

	private void ProcessTreeGeneration(
		bool[,] isTreeColored,
		float[,] smoothedHeights,
		List<(float X, float Y, float Z, float Rot, float Scale)> treePositions,
		int width,
		int depth,
		float quadSize)
	{
		var random = new Random();
		bool[,] visited = new bool[width, depth];

		for (int gz = 0; gz < depth; gz++)
		{
			for (int gx = 0; gx < width; gx++)
			{
				ProcessTreeCell(isTreeColored, smoothedHeights, treePositions, visited, width, depth, quadSize, random, gx, gz);
			}
		}
	}
	
	private void ProcessTreeCell(
		bool[,] isTreeColored,
		float[,] smoothedHeights,
		List<(float X, float Y, float Z, float Rot, float Scale)> treePositions,
		bool[,] visited,
		int width,
		int depth,
		float quadSize,
		Random random,
		int gx,
		int gz)
	{
		if (!isTreeColored[gx, gz] || visited[gx, gz])
		{
			return;
		}

		var blob = FindTreeBlob(isTreeColored, visited, width, depth, gx, gz);
		PlaceTreesInBlob(blob, smoothedHeights, treePositions, width, depth, quadSize, random);
	}

	private List<Vector2I> FindTreeBlob(
		bool[,] isTreeColored,
		bool[,] visited,
		int width,
		int depth,
		int startX,
		int startZ)
	{
		var blob = new List<Vector2I>();
		var queue = new Queue<Vector2I>();
		var start = new Vector2I(startX, startZ);
		queue.Enqueue(start);
		visited[startX, startZ] = true;

		while (queue.Count > 0)
		{
			var curr = queue.Dequeue();
			blob.Add(curr);
			EnqueueValidNeighbors(curr, isTreeColored, visited, queue, width, depth);
		}
		
		return blob;
	}

	private void EnqueueValidNeighbors(
		Vector2I curr,
		bool[,] isTreeColored,
		bool[,] visited,
		Queue<Vector2I> queue,
		int width,
		int depth)
	{
		Vector2I[] neighbors = new Vector2I[]
		{
			new Vector2I(curr.X + 1, curr.Y),
			new Vector2I(curr.X - 1, curr.Y),
			new Vector2I(curr.X, curr.Y + 1),
			new Vector2I(curr.X, curr.Y - 1)
		};

		foreach (var n in neighbors)
		{
			if (n.X >= 0 && n.X < width && n.Y >= 0 && n.Y < depth)
			{
				if (isTreeColored[n.X, n.Y] && !visited[n.X, n.Y])
				{
					visited[n.X, n.Y] = true;
					queue.Enqueue(n);
				}
			}
		}
	}

	private void PlaceTreesInBlob(
		List<Vector2I> blob,
		float[,] smoothedHeights,
		List<(float X, float Y, float Z, float Rot, float Scale)> treePositions,
		int width,
		int depth,
		float quadSize,
		Random random)
	{
		int size = blob.Count;
		float baseDensity = 0.15f;
		if (size > 15) baseDensity = 0.35f;
		if (size > 50) baseDensity = 0.55f;

		foreach (var cell in blob)
		{
			float height = smoothedHeights[cell.X, cell.Y];
			if (height >= 0.0f && Noise2D(cell.X, cell.Y) < baseDensity)
			{
				float offsetX = (random.NextSingle() - 0.5f) * 1.5f;
				float offsetZ = (random.NextSingle() - 0.5f) * 1.5f;
				float worldX = (cell.X - (width - 1) / 2.0f) * quadSize + offsetX;
				float worldZ = (cell.Y - (depth - 1) / 2.0f) * quadSize + offsetZ;
				float rot = random.NextSingle() * 360f;
				float scale = 0.8f + random.NextSingle() * 0.4f;

				treePositions.Add((worldX, height, worldZ, rot, scale));
			}
		}
	}

	private float Noise2D(float x, float z)
	{
		float val = MathF.Sin(x * 12.9898f + z * 78.233f) * 43758.5453123f;
		return val - MathF.Floor(val);
	}

	private static Image LoadImageForImport(string path)
	{
		string extension = Path.GetExtension(path).ToLowerInvariant();
		if (extension == ".gif" || extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".webp")
		{
			using var skBitmap = SKBitmap.Decode(path);
			if (skBitmap == null)
			{
				return null;
			}
			using var converted = skBitmap.Copy(SKColorType.Rgba8888);
			byte[] rgba = converted.Bytes;
			return Image.CreateFromData(converted.Width, converted.Height, false, Image.Format.Rgba8, rgba);
		}
		return Image.LoadFromFile(path);
	}
}
