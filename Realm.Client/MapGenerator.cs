using System;
using System.Collections.Generic;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;

namespace Realm.Client;

public static class MapGenerator
{
	private const int RowsPerChunk = 16;
	private const int SpawnsPerChunk = 4;
	private const int MillisecondsPerTick = 8;
	private const int MaxSampledPoints = 20000;
	private const float ReferenceMapTiles = 128.0f;

	private static readonly double[] SiteFractionX = { 20.0 / 127.0, 106.0 / 127.0, 63.0 / 127.0, 20.0 / 127.0, 106.0 / 127.0, 63.0 / 127.0, 63.0 / 127.0, 20.0 / 127.0, 106.0 / 127.0 };
	private static readonly double[] SiteFractionZ = { 20.0 / 127.0, 106.0 / 127.0, 63.0 / 127.0, 106.0 / 127.0, 20.0 / 127.0, 20.0 / 127.0, 106.0 / 127.0, 63.0 / 127.0, 63.0 / 127.0 };

	public static MapGenerationJob? ActiveJob { get; private set; }

	public static MapGenerationJob? StartGeneration(
		GameHost host,
		int hillsDensity,
		int terrainRoughness,
		int mountainHeight,
		int chokeWidth,
		int waterLevel,
		int treeDensity,
		int resourceAbundance,
		int decoDensity,
		string seedString
	)
	{
		if (host?.GroundTerrain == null) return null;

		CancelActiveGeneration();

		var job = new MapGenerationJob(host, hillsDensity, terrainRoughness, mountainHeight, chokeWidth, waterLevel, treeDensity, resourceAbundance, decoDensity, seedString);
		ActiveJob = job;
		return job;
	}

	public static void CancelActiveGeneration()
	{
		ActiveJob?.Cancel();
	}

	public static void Tick()
	{
		MapGenerationJob? job = ActiveJob;
		if (job == null) return;

		if (job.IsCancelled)
		{
			job.CleanupAfterCancel();
			ActiveJob = null;
			return;
		}

		job.AdvanceUntilBudget(MillisecondsPerTick);

		if (job.IsCancelled)
		{
			job.CleanupAfterCancel();
			ActiveJob = null;
		}
		else if (job.IsComplete)
		{
			ActiveJob = null;
		}
	}

	private static Vector2I[] BuildSites(int width, int depth)
	{
		var sites = new Vector2I[SiteFractionX.Length];
		for (int i = 0; i < sites.Length; i++)
		{
			int x = (int)Math.Round(SiteFractionX[i] * (width - 1));
			int z = (int)Math.Round(SiteFractionZ[i] * (depth - 1));
			sites[i] = new Vector2I(Math.Clamp(x, 0, width - 1), Math.Clamp(z, 0, depth - 1));
		}

		return sites;
	}

	private static void WorldToGrid(Vector3 worldPos, int width, int depth, float quadSize, out int x, out int z)
	{
		x = (int)Math.Round((worldPos.X / quadSize) + width / 2.0f);
		z = (int)Math.Round((worldPos.Z / quadSize) + depth / 2.0f);
	}

	private static Vector3 GridToWorld(int x, int z, int width, int depth, float quadSize, float height)
	{
		float lx = (x - width / 2.0f) * quadSize;
		float lz = (z - depth / 2.0f) * quadSize;
		return new Vector3(lx, height, lz);
	}

	private static void CarvePathAt(Vector2I pos, int[,] baseGrid, int chokeWidth, int width, int depth)
	{
		int radius = (int)(1.0f + (chokeWidth / 10.0f) * 3.0f);
		for (int z = -radius; z <= radius; z++)
		{
			for (int x = -radius; x <= radius; x++)
			{
				if (x * x + z * z > radius * radius) continue;

				int cx = pos.X + x;
				int cz = pos.Y + z;
				if (cx >= 0 && cx < width && cz >= 0 && cz < depth)
				{
					baseGrid[cx, cz] = 0;
				}
			}
		}
	}

	private static void InitTerrainState(EcsWorld world, Entity worldEntity, int width, int depth, float quadSize)
	{
		if (!world.Has<TerrainState>(worldEntity))
		{
			world.Add(worldEntity, new TerrainState(
				width, depth, quadSize, 0.5f,
				new TerrainCell[width, depth],
				new int[width, depth],
				null, null
			));
		}
		else
		{
			ref var ts = ref world.Get<TerrainState>(worldEntity);
			bool modified = false;
			if (ts.Cells == null || ts.Cells.GetLength(0) != width || ts.Cells.GetLength(1) != depth)
			{
				ts.Cells = new TerrainCell[width, depth];
				modified = true;
			}

			if (ts.PathingCodes == null || ts.PathingCodes.GetLength(0) != width || ts.PathingCodes.GetLength(1) != depth)
			{
				ts.PathingCodes = new int[width, depth];
				modified = true;
			}

			if (modified)
			{
				world.Set(worldEntity, ts);
			}
		}
	}

	private static int CountNeighborsBaseGrid(int[,] baseGrid, int x, int z, int width, int depth)
	{
		int count = 0;
		for (int nz = -1; nz <= 1; nz++)
		{
			for (int nx = -1; nx <= 1; nx++)
			{
				int checkX = x + nx;
				int checkZ = z + nz;
				if (checkX >= 0 && checkX < width && checkZ >= 0 && checkZ < depth)
				{
					count += baseGrid[checkX, checkZ];
				}
				else
				{
					count++;
				}
			}
		}

		return count;
	}

	private static void ClearSingleSiteRadius(int[,] baseGrid, Vector2I center, int clearRadius, int width, int depth)
	{
		for (int z = -clearRadius; z <= clearRadius; z++)
		{
			for (int x = -clearRadius; x <= clearRadius; x++)
			{
				if (x * x + z * z > clearRadius * clearRadius) continue;
				int cx = center.X + x;
				int cz = center.Y + z;
				if (cx >= 0 && cx < width && cz >= 0 && cz < depth)
				{
					baseGrid[cx, cz] = 0;
				}
			}
		}
	}

	private static void ClearSiteRadii(int[,] baseGrid, Vector2I[] sites, int width, int depth)
	{
		for (int i = 0; i < sites.Length; i++)
		{
			int clearRadius = 8;
			if (i == 0 || i == 1) clearRadius = 15;
			else if (i == 2) clearRadius = 12;

			Vector2I center = sites[i];
			ClearSingleSiteRadius(baseGrid, center, clearRadius, width, depth);
		}
	}

	private static void FindBestEdge(Vector2I[] sites, bool[] visited, out int bestU, out int bestV)
	{
		double minDist = double.MaxValue;
		bestU = -1;
		bestV = -1;
		for (int u = 0; u < sites.Length; u++)
		{
			if (!visited[u]) continue;
			for (int v = 0; v < sites.Length; v++)
			{
				if (visited[v]) continue;
				double dx = sites[u].X - sites[v].X;
				double dy = sites[u].Y - sites[v].Y;
				double dist = Math.Sqrt(dx * dx + dy * dy);
				if (dist < minDist)
				{
					minDist = dist;
					bestU = u;
					bestV = v;
				}
			}
		}
	}

	private static void CalculatePathingEdges(Vector2I[] sites, out List<Tuple<int, int>> edges)
	{
		edges = new List<Tuple<int, int>>();
		bool[] visited = new bool[sites.Length];
		visited[0] = true;
		for (int iter = 0; iter < sites.Length - 1; iter++)
		{
			FindBestEdge(sites, visited, out int bestU, out int bestV);
			if (bestV != -1)
			{
				visited[bestV] = true;
				edges.Add(new Tuple<int, int>(bestU, bestV));
			}
		}
	}

	private static void MarkPathRadius(Vector2I curr, int radius, bool[,] isPath, int width, int depth)
	{
		for (int z = -radius; z <= radius; z++)
		{
			for (int x = -radius; x <= radius; x++)
			{
				if (x * x + z * z > radius * radius) continue;

				int cx = curr.X + x;
				int cz = curr.Y + z;
				if (cx >= 0 && cx < width && cz >= 0 && cz < depth)
				{
					isPath[cx, cz] = true;
				}
			}
		}
	}

	private static void CarvePath(Dictionary<Vector2I, Vector2I> cameFrom, Vector2I start, Vector2I end, int[,] baseGrid, bool[,] isPath, int chokeWidth, int width, int depth)
	{
		Vector2I curr = end;
		int radius = (int)(1.0f + (chokeWidth / 10.0f) * 3.0f);
		int maxSteps = width * depth;
		int steps = 0;
		while (curr != start && steps < maxSteps)
		{
			CarvePathAt(curr, baseGrid, chokeWidth, width, depth);
			MarkPathRadius(curr, radius, isPath, width, depth);
			if (!cameFrom.TryGetValue(curr, out Vector2I previous)) break;
			curr = previous;
			steps++;
		}

		CarvePathAt(start, baseGrid, chokeWidth, width, depth);
		if (start.X >= 0 && start.X < width && start.Y >= 0 && start.Y < depth)
		{
			isPath[start.X, start.Y] = true;
		}
	}

	private static void ProcessPathNeighbor(Vector2I n, Vector2I current, Vector2I end, int[,] baseGrid, Dictionary<Vector2I, Vector2I> cameFrom, Dictionary<Vector2I, float> gScore, PriorityQueue<Vector2I, float> openSet, int width, int depth)
	{
		if (n.X < 0 || n.X >= width || n.Y < 0 || n.Y >= depth) return;

		float stepCost = 1.0f;
		if (baseGrid[n.X, n.Y] == 1)
		{
			stepCost += 15.0f;
		}

		float tentativeG = gScore[current] + stepCost;
		if (!gScore.TryGetValue(n, out float currentG) || tentativeG < currentG)
		{
			gScore[n] = tentativeG;
			cameFrom[n] = current;
			float dx = n.X - end.X;
			float dy = n.Y - end.Y;
			float h = (float)Math.Sqrt(dx * dx + dy * dy);
			openSet.Enqueue(n, tentativeG + h);
		}
	}

	private static void CalculatePathing(Vector2I start, Vector2I end, int[,] baseGrid, bool[,] isPath, int chokeWidth, int width, int depth)
	{
		if (start.X < 0 || start.X >= width || start.Y < 0 || start.Y >= depth) return;
		if (end.X < 0 || end.X >= width || end.Y < 0 || end.Y >= depth) return;

		PriorityQueue<Vector2I, float> openSet = new PriorityQueue<Vector2I, float>();
		Dictionary<Vector2I, Vector2I> cameFrom = new Dictionary<Vector2I, Vector2I>();
		Dictionary<Vector2I, float> gScore = new Dictionary<Vector2I, float>();

		gScore[start] = 0f;
		openSet.Enqueue(start, 0f);

		while (openSet.Count > 0)
		{
			Vector2I current = openSet.Dequeue();
			if (current == end) break;

			Vector2I[] neighbors = new Vector2I[]
			{
				new Vector2I(current.X + 1, current.Y),
				new Vector2I(current.X - 1, current.Y),
				new Vector2I(current.X, current.Y + 1),
				new Vector2I(current.X, current.Y - 1)
			};

			foreach (var n in neighbors)
			{
				ProcessPathNeighbor(n, current, end, baseGrid, cameFrom, gScore, openSet, width, depth);
			}
		}

		if (cameFrom.ContainsKey(end))
		{
			CarvePath(cameFrom, start, end, baseGrid, isPath, chokeWidth, width, depth);
		}
	}

	private static void ClearPathRadii(Vector2I[] sites, bool[,] isPath, int width, int depth)
	{
		for (int i = 0; i < sites.Length; i++)
		{
			Vector2I center = sites[i];
			int radius = 10;
			for (int z = -radius; z <= radius; z++)
			{
				for (int x = -radius; x <= radius; x++)
				{
					if (x * x + z * z > radius * radius) continue;

					int cx = center.X + x;
					int cz = center.Y + z;
					if (cx >= 0 && cx < width && cz >= 0 && cz < depth)
					{
						isPath[cx, cz] = true;
					}
				}
			}
		}
	}

	private static float CalculateCellDensity(int x, int z, int[,] baseGrid, int width, int depth)
	{
		if (baseGrid[x, z] != 1) return 0f;

		int count = 0;
		for (int dz = -2; dz <= 2; dz++)
		{
			for (int dx = -2; dx <= 2; dx++)
			{
				int cx = Math.Clamp(x + dx, 0, width - 1);
				int cz = Math.Clamp(z + dz, 0, depth - 1);
				if (baseGrid[cx, cz] == 1) count++;
			}
		}

		return count / 25.0f;
	}

	private static sbyte CalculateCellTier(int x, int z, int[,] baseGrid, bool[,] isPath, float[,] densityMap, FastNoiseLite noise, FastNoiseLite detailNoise, float waterCutoff, float maxMountainTiers, int hillsDensity)
	{
		if (isPath[x, z]) return 0;
		if (baseGrid[x, z] == 1)
		{
			float dFactor = densityMap[x, z];
			float nVal = (noise.GetNoise2D(x * 1.5f, z * 1.5f) + 1.0f) * 0.5f;
			float dtVal = (detailNoise.GetNoise2D(x * 3.0f, z * 3.0f) + 1.0f) * 0.5f;

			float rawTier = 1.0f + (dFactor * 0.5f + nVal * 0.35f + dtVal * 0.15f) * maxMountainTiers;
			return (sbyte)Math.Clamp((int)(MathF.Round(rawTier / 2.0f) * 2.0f), 1, 127);
		}

		float nVal2 = (noise.GetNoise2D(x, z) + 1.0f) * 0.5f;
		float dtVal2 = (detailNoise.GetNoise2D(x * 2.5f, z * 2.5f) + 1.0f) * 0.5f;

		if (nVal2 < waterCutoff)
		{
			float waterDepthRatio = (waterCutoff - nVal2) / waterCutoff;
			return (sbyte)-Math.Clamp((int)MathF.Round(1.0f + waterDepthRatio * 2.0f), 1, 4);
		}

		if (hillsDensity > 2 && nVal2 > 0.65f - (hillsDensity / 10.0f) * 0.20f)
		{
			return (sbyte)Math.Clamp((int)MathF.Round(1.0f + dtVal2 * (hillsDensity / 10.0f) * 2.0f), 1, 4);
		}

		return 0;
	}

	private static sbyte SmoothCellTier(sbyte[,] tiers, int x, int z)
	{
		sbyte maxNeighborTier = tiers[x, z];
		for (int dz = -1; dz <= 1; dz++)
		{
			for (int dx = -1; dx <= 1; dx++)
			{
				if (tiers[x + dx, z + dz] > maxNeighborTier)
				{
					maxNeighborTier = tiers[x + dx, z + dz];
				}
			}
		}

		int count = 0;
		for (int dz = -1; dz <= 1; dz++)
		{
			for (int dx = -1; dx <= 1; dx++)
			{
				if (tiers[x + dx, z + dz] == maxNeighborTier) count++;
			}
		}

		if (count >= 4) return maxNeighborTier;
		return tiers[x, z];
	}

	private static void EnsureTerrainMapsExist(GameHost host, int width, int depth)
	{
		if (host.GroundTerrain.SplatMap == null || host.GroundTerrain.SplatMap.GetLength(0) < width + 1 || host.GroundTerrain.SplatMap.GetLength(1) < depth + 1)
		{
			host.GroundTerrain.SplatMap = new TerrainSplatWeights[width + 1, depth + 1];
		}

		if (host.GroundTerrain.CliffSplatMap == null || host.GroundTerrain.CliffSplatMap.GetLength(0) < width + 1 || host.GroundTerrain.CliffSplatMap.GetLength(1) < depth + 1)
		{
			host.GroundTerrain.CliffSplatMap = new TerrainSplatWeights[width + 1, depth + 1];
		}
	}

	private static int[,] EnsurePathingCodesExist(GameHost host, int width, int depth)
	{
		var pathingCodes = host.GroundTerrain.PathingCodes;
		if (pathingCodes == null || pathingCodes.GetLength(0) != width || pathingCodes.GetLength(1) != depth)
		{
			return new int[width, depth];
		}

		return pathingCodes;
	}

	private static TerrainSplatWeights GetTerrainSplatWeight(sbyte tier)
	{
		return tier switch
		{
			< 0 => TerrainSplatWeights.CreateSolid(4),
			0 => TerrainSplatWeights.CreateSolid(3),
			1 or 2 => TerrainSplatWeights.CreateSolid(2),
			_ => TerrainSplatWeights.CreateSolid(0)
		};
	}

	private static void ProcessDistanceNeighbor(Vector2I n, float d, float[,] pathDist, Queue<Vector2I> queue, int width, int depth)
	{
		if (n.X < 0 || n.X >= width || n.Y < 0 || n.Y >= depth) return;
		if (pathDist[n.X, n.Y] > d + 1f)
		{
			pathDist[n.X, n.Y] = d + 1f;
			queue.Enqueue(n);
		}
	}

	private static float[,] CalculatePathDistances(bool[,] isPath, int width, int depth)
	{
		float[,] pathDist = new float[width, depth];
		Queue<Vector2I> queue = new Queue<Vector2I>();
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				if (isPath[x, z])
				{
					pathDist[x, z] = 0f;
					queue.Enqueue(new Vector2I(x, z));
				}
				else
				{
					pathDist[x, z] = float.MaxValue;
				}
			}
		}

		while (queue.Count > 0)
		{
			Vector2I curr = queue.Dequeue();
			float d = pathDist[curr.X, curr.Y];
			Vector2I[] neighbors = new Vector2I[]
			{
				new Vector2I(curr.X + 1, curr.Y),
				new Vector2I(curr.X - 1, curr.Y),
				new Vector2I(curr.X, curr.Y + 1),
				new Vector2I(curr.X, curr.Y - 1)
			};
			foreach (var n in neighbors)
			{
				ProcessDistanceNeighbor(n, d, pathDist, queue, width, depth);
			}
		}

		return pathDist;
	}

	private static bool TryGetValidGridPos(Vector2 p, int width, int depth, float quadSize, out Vector3 worldPos, out int gx, out int gz)
	{
		worldPos = new Vector3(p.X - (width - 1) * quadSize / 2.0f, 0f, p.Y - (depth - 1) * quadSize / 2.0f);
		WorldToGrid(worldPos, width, depth, quadSize, out gx, out gz);
		return gx >= 0 && gx < width && gz >= 0 && gz < depth;
	}

	private static void TrySpawnTree(GameHost host, Vector2 p, sbyte[,] tiers, float[,] pathDist, int width, int depth, float quadSize, Random random)
	{
		if (!TryGetValidGridPos(p, width, depth, quadSize, out Vector3 worldPos, out int gx, out int gz)) return;
		if (tiers[gx, gz] >= 0 && tiers[gx, gz] <= 2 && pathDist[gx, gz] > 4f)
		{
			worldPos.Y = host.GroundTerrain.GetGridNodeHeight(gx, gz);
			float rot = (float)(random.NextDouble() * 360.0);
			float scale = 0.8f + (float)(random.NextDouble() * 0.4);
			host.SpawnPropExternalWithParams("tree", worldPos, rot, scale);
		}
	}

	private static void TrySpawnDeco(GameHost host, Vector2 p, sbyte[,] tiers, float[,] pathDist, string[] decoIds, int width, int depth, float quadSize, Random random)
	{
		if (!TryGetValidGridPos(p, width, depth, quadSize, out Vector3 worldPos, out int gx, out int gz)) return;
		if (tiers[gx, gz] >= 0 && pathDist[gx, gz] > 2f)
		{
			worldPos.Y = host.GroundTerrain.GetGridNodeHeight(gx, gz);
			float rot = (float)(random.NextDouble() * 360.0);
			float scale = 0.7f + (float)(random.NextDouble() * 0.6);
			string propId = decoIds[random.Next(decoIds.Length)];
			host.SpawnPropExternalWithParams(propId, worldPos, rot, scale);
		}
	}

	private static bool IsFarEnough(Vector2 candidate, Vector2?[,] grid, float radius, int cX, int cY, int gridWidth, int gridHeight)
	{
		int startX = Math.Max(0, cX - 2);
		int endX = Math.Min(gridWidth - 1, cX + 2);
		int startY = Math.Max(0, cY - 2);
		int endY = Math.Min(gridHeight - 1, cY + 2);

		for (int gx = startX; gx <= endX; gx++)
		{
			for (int gy = startY; gy <= endY; gy++)
			{
				if (!grid[gx, gy].HasValue) continue;

				float dist = (grid[gx, gy]!.Value - candidate).Length();
				if (dist < radius) return false;
			}
		}

		return true;
	}

	private static List<Vector2> PoissonDiscSample(
		Random random,
		float width,
		float height,
		float radius,
		int maxCandidates = 30
	)
	{
		float cellSize = radius / (float)Math.Sqrt(2);
		int gridWidth = (int)Math.Ceiling(width / cellSize);
		int gridHeight = (int)Math.Ceiling(height / cellSize);
		Vector2?[,] grid = new Vector2?[gridWidth, gridHeight];

		List<Vector2> points = new List<Vector2>();
		List<Vector2> activeList = new List<Vector2>();

		Vector2 firstPoint = new Vector2(
			(float)(random.NextDouble() * width),
			(float)(random.NextDouble() * height)
		);

		AddPointToGrid(firstPoint, points, activeList, grid, cellSize, gridWidth, gridHeight);

		while (activeList.Count > 0)
		{
			int index = random.Next(activeList.Count);
			Vector2 point = activeList[index];

			if (!TryAddNextCandidate(random, point, maxCandidates, radius, width, height, cellSize, gridWidth, gridHeight, grid, points, activeList))
			{
				activeList.RemoveAt(index);
			}
		}

		return points;
	}

	private static void AddPointToGrid(Vector2 point, List<Vector2> points, List<Vector2> activeList, Vector2?[,] grid, float cellSize, int gridWidth, int gridHeight)
	{
		points.Add(point);
		activeList.Add(point);

		int gX = (int)(point.X / cellSize);
		int gY = (int)(point.Y / cellSize);

		if (gX >= 0 && gX < gridWidth && gY >= 0 && gY < gridHeight)
		{
			grid[gX, gY] = point;
		}
	}

	private static bool TryAddNextCandidate(Random random, Vector2 point, int maxCandidates, float radius, float width, float height, float cellSize, int gridWidth, int gridHeight, Vector2?[,] grid, List<Vector2> points, List<Vector2> activeList)
	{
		for (int k = 0; k < maxCandidates; k++)
		{
			double angle = random.NextDouble() * 2 * Math.PI;
			double r = radius + random.NextDouble() * radius;
			Vector2 candidate = new Vector2(
				point.X + (float)(r * Math.Cos(angle)),
				point.Y + (float)(r * Math.Sin(angle))
			);

			if (!IsCandidateInBounds(candidate, width, height)) continue;

			int cX = (int)(candidate.X / cellSize);
			int cY = (int)(candidate.Y / cellSize);

			if (IsFarEnough(candidate, grid, radius, cX, cY, gridWidth, gridHeight))
			{
				AddPointToGrid(candidate, points, activeList, grid, cellSize, gridWidth, gridHeight);
				return true;
			}
		}

		return false;
	}

	private static bool IsCandidateInBounds(Vector2 candidate, float width, float height)
	{
		return candidate.X >= 0 && candidate.X < width && candidate.Y >= 0 && candidate.Y < height;
	}

	private static float GetSamplingScale(int width, int depth)
	{
		float tileCount = MathF.Max(width * depth, 1);
		float referenceTileCount = ReferenceMapTiles * ReferenceMapTiles;
		return MathF.Max(1.0f, MathF.Sqrt(tileCount / referenceTileCount));
	}

	private static void TrimSampledPoints(List<Vector2> points)
	{
		if (points.Count <= MaxSampledPoints) return;

		points.RemoveRange(MaxSampledPoints, points.Count - MaxSampledPoints);
	}

	public class MapGenerationJob
	{
		private const int StagePrepare = 0;
		private const int StageBaseGrid = 1;
		private const int StageSites = 2;
		private const int StagePaths = 3;
		private const int StageDensity = 4;
		private const int StageTiers = 5;
		private const int StageSmooth = 6;
		private const int StageCells = 7;
		private const int StageSplatMaps = 8;
		private const int StageApplyTerrain = 9;
		private const int StageUnitsAndProps = 10;
		private const int StagePathDistances = 11;
		private const int StageTreeSampling = 12;
		private const int StageTreeSpawns = 13;
		private const int StageDecoSampling = 14;
		private const int StageDecoSpawns = 15;
		private const int StageFinalize = 16;
		private const int StageComplete = 17;

		private static readonly float[] StageStartProgress =
		{
			0.00f, 0.02f, 0.05f, 0.06f, 0.10f, 0.14f, 0.24f,
			0.30f, 0.36f, 0.46f, 0.52f, 0.58f, 0.60f,
			0.74f, 0.76f, 0.92f, 0.98f, 1.00f
		};

		private readonly GameHost _host;
		private readonly int _hillsDensity;
		private readonly int _terrainRoughness;
		private readonly int _mountainHeight;
		private readonly int _chokeWidth;
		private readonly int _waterLevel;
		private readonly int _treeDensity;
		private readonly int _decoDensity;
		private readonly string _seedString;

		private int _stage = StagePrepare;
		private float _stageProgress;
		private bool _terrainTouched;

		private int _seed;
		private int _width;
		private int _depth;
		private float _quadSize;
		private double _fillChance;
		private float _waterCutoff;
		private float _maxMountainTiers;
		private Random _random;
		private FastNoiseLite _noise;
		private FastNoiseLite _detailNoise;
		private Vector2I[] _sites;

		private int[,] _baseGrid;
		private int[,] _nextGrid;
		private int _gridPass;
		private int _gridRow;

		private List<Tuple<int, int>> _pathEdges;
		private bool[,] _isPath;
		private int _pathIndex;

		private float[,] _densityMap;
		private sbyte[,] _tiers;
		private sbyte[,] _smoothedTiers;
		private int _smoothPass;
		private int _rowCursor;

		private TerrainCell[,] _cells;
		private int[,] _pathingCodes;
		private List<SpawnEntry> _spawnEntries;
		private int _spawnCursor;
		private float[,] _pathDist;
		private List<Vector2> _treePoints;
		private int _treeCursor;
		private List<Vector2> _decoPoints;
		private int _decoCursor;

		public MapGenerationJob(
			GameHost host,
			int hillsDensity,
			int terrainRoughness,
			int mountainHeight,
			int chokeWidth,
			int waterLevel,
			int treeDensity,
			int resourceAbundance,
			int decoDensity,
			string seedString)
		{
			_host = host;
			_hillsDensity = hillsDensity;
			_terrainRoughness = terrainRoughness;
			_mountainHeight = mountainHeight;
			_chokeWidth = chokeWidth;
			_waterLevel = waterLevel;
			_treeDensity = treeDensity;
			_decoDensity = decoDensity;
			_seedString = seedString ?? string.Empty;
		}

		public bool IsComplete => _stage >= StageComplete;

		public bool IsCancelled { get; private set; }

		public float Progress => IsComplete ? 1.0f : StageStartProgress[Math.Clamp(_stage, 0, StageComplete - 1)] + _stageProgress * (StageStartProgress[Math.Clamp(_stage + 1, 0, StageComplete)] - StageStartProgress[Math.Clamp(_stage, 0, StageComplete - 1)]);

		public string StageDescription { get; private set; } = string.Empty;

		public void Cancel()
		{
			IsCancelled = true;
		}

		public void CleanupAfterCancel()
		{
			if (!_terrainTouched) return;

			_host.ClearMapEntirely();
		}

		public void AdvanceUntilBudget(int milliseconds)
		{
			if (IsComplete || IsCancelled) return;

			ulong startTicks = Time.GetTicksMsec();
			while (!IsComplete && !IsCancelled)
			{
				Advance();
				if (Time.GetTicksMsec() - startTicks >= (ulong)milliseconds) break;
			}
		}

		public void Advance()
		{
			if (IsComplete || IsCancelled) return;

			switch (_stage)
			{
				case StagePrepare:
					Prepare();
					break;
				case StageBaseGrid:
					StepBaseGrid();
					break;
				case StageSites:
					StepSites();
					break;
				case StagePaths:
					StepPaths();
					break;
				case StageDensity:
					StepDensity();
					break;
				case StageTiers:
					StepTiers();
					break;
				case StageSmooth:
					StepSmooth();
					break;
				case StageCells:
					StepCells();
					break;
				case StageSplatMaps:
					StepSplatMaps();
					break;
				case StageApplyTerrain:
					StepApplyTerrain();
					break;
				case StageUnitsAndProps:
					StepUnitsAndProps();
					break;
				case StagePathDistances:
					StepPathDistances();
					break;
				case StageTreeSampling:
					StepTreeSampling();
					break;
				case StageTreeSpawns:
					StepTreeSpawns();
					break;
				case StageDecoSampling:
					StepDecoSampling();
					break;
				case StageDecoSpawns:
					StepDecoSpawns();
					break;
				case StageFinalize:
					StepFinalize();
					break;
			}
		}

		private void EnterStage(int stage, string description)
		{
			_stage = stage;
			_stageProgress = 0.0f;
			_rowCursor = 0;
			StageDescription = TranslationServer.Translate(description);
		}

		private void Prepare()
		{
			if (!int.TryParse(_seedString, out _seed))
			{
				_seed = _seedString.GetHashCode();
			}

			_host.ClearMapEntirely();

			_width = _host.GroundTerrain.Width;
			_depth = _host.GroundTerrain.Depth;
			_quadSize = _host.GroundTerrain.QuadSize;

			EcsWorld world = GameContext.World;
			Entity worldEntity = ECSContext.WorldEntity;
			if (world != null && world.IsAlive(worldEntity))
			{
				InitTerrainState(world, worldEntity, _width, _depth, _quadSize);
			}

			_random = new Random(_seed);
			_fillChance = 0.25 + (_hillsDensity / 10.0) * 0.40;
			_waterCutoff = 0.15f + (_waterLevel / 10.0f) * 0.25f;
			_maxMountainTiers = 2.0f + (_mountainHeight / 10.0f) * 6.0f;

			_noise = new FastNoiseLite();
			_noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
			_noise.Seed = _seed;
			_noise.Frequency = 0.01f + (_terrainRoughness / 10.0f) * 0.04f;

			_detailNoise = new FastNoiseLite();
			_detailNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
			_detailNoise.Seed = _seed + 777;
			_detailNoise.Frequency = 0.035f;

			_sites = BuildSites(_width, _depth);

			EnterStage(StageBaseGrid, "Shaping land masses");
		}

		private void StepBaseGrid()
		{
			if (_baseGrid == null)
			{
				_baseGrid = new int[_width, _depth];
			}

			int rows = Math.Min(RowsPerChunk, _depth - _gridRow);
			for (int i = 0; i < rows; i++)
			{
				int z = _gridRow + i;
				if (_gridPass == 0)
				{
					for (int x = 0; x < _width; x++)
					{
						_baseGrid[x, z] = (_random.NextDouble() < _fillChance) ? 1 : 0;
					}
				}
				else
				{
					_nextGrid ??= new int[_width, _depth];
					for (int x = 0; x < _width; x++)
					{
						int count = CountNeighborsBaseGrid(_baseGrid, x, z, _width, _depth);
						_nextGrid[x, z] = (count >= 5) ? 1 : 0;
					}
				}
			}

			_gridRow += rows;
			_stageProgress = _gridRow / (float)Math.Max(_depth, 1);

			if (_gridRow < _depth) return;

			_gridRow = 0;
			if (_gridPass > 0)
			{
				(_baseGrid, _nextGrid) = (_nextGrid, _baseGrid);
			}

			_gridPass++;
			if (_gridPass <= 4) return;

			_nextGrid = null;
			EnterStage(StageSites, "Clearing player start areas");
		}

		private void StepSites()
		{
			ClearSiteRadii(_baseGrid, _sites, _width, _depth);
			EnterStage(StagePaths, "Carving paths between sites");
		}

		private void StepPaths()
		{
			if (_pathEdges == null)
			{
				CalculatePathingEdges(_sites, out List<Tuple<int, int>> edges);
				_pathEdges = edges;
				_isPath = new bool[_width, _depth];
			}

			if (_pathIndex < _pathEdges.Count)
			{
				Tuple<int, int> edge = _pathEdges[_pathIndex++];
				CalculatePathing(_sites[edge.Item1], _sites[edge.Item2], _baseGrid, _isPath, _chokeWidth, _width, _depth);
				_stageProgress = _pathIndex / (float)Math.Max(_pathEdges.Count, 1);
				return;
			}

			ClearPathRadii(_sites, _isPath, _width, _depth);
			_densityMap = new float[_width, _depth];
			EnterStage(StageDensity, "Measuring land density");
		}

		private void StepDensity()
		{
			int rows = Math.Min(RowsPerChunk, _depth - _rowCursor);
			for (int i = 0; i < rows; i++)
			{
				int z = _rowCursor + i;
				for (int x = 0; x < _width; x++)
				{
					_densityMap[x, z] = CalculateCellDensity(x, z, _baseGrid, _width, _depth);
				}
			}

			_rowCursor += rows;
			_stageProgress = _rowCursor / (float)Math.Max(_depth, 1);
			if (_rowCursor < _depth) return;

			_tiers = new sbyte[_width, _depth];
			EnterStage(StageTiers, "Raising mountains and water");
		}

		private void StepTiers()
		{
			int rows = Math.Min(RowsPerChunk, _depth - _rowCursor);
			for (int i = 0; i < rows; i++)
			{
				int z = _rowCursor + i;
				for (int x = 0; x < _width; x++)
				{
					_tiers[x, z] = CalculateCellTier(x, z, _baseGrid, _isPath, _densityMap, _noise, _detailNoise, _waterCutoff, _maxMountainTiers, _hillsDensity);
				}
			}

			_rowCursor += rows;
			_stageProgress = _rowCursor / (float)Math.Max(_depth, 1);
			if (_rowCursor < _depth) return;

			_densityMap = null;
			_baseGrid = null;
			EnterStage(StageSmooth, "Smoothing terrain tiers");
		}

		private void StepSmooth()
		{
			if (_smoothedTiers == null)
			{
				_smoothedTiers = (sbyte[,])_tiers.Clone();
			}

			int startZ = Math.Max(1, _rowCursor);
			int endZ = Math.Min(_depth - 1, startZ + RowsPerChunk);
			if (startZ < _depth - 1)
			{
				for (int z = startZ; z < endZ; z++)
				{
					for (int x = 1; x < _width - 1; x++)
					{
						if (_tiers[x, z] > 0 && !_isPath[x, z])
						{
							_smoothedTiers[x, z] = SmoothCellTier(_tiers, x, z);
						}
					}
				}

				_rowCursor = endZ;
				_stageProgress = _rowCursor / (float)Math.Max(_depth, 1);
				return;
			}

			(_tiers, _smoothedTiers) = (_smoothedTiers, _tiers);
			_smoothedTiers = null;
			_smoothPass++;
			_rowCursor = 0;
			if (_smoothPass < 2) return;

			_cells = new TerrainCell[_width, _depth];
			_host.GroundTerrain.Cells = _cells;
			_terrainTouched = true;
			EnterStage(StageCells, "Building terrain heights");
		}

		private void StepCells()
		{
			int rows = Math.Min(RowsPerChunk, _depth - _rowCursor);
			for (int i = 0; i < rows; i++)
			{
				int z = _rowCursor + i;
				for (int x = 0; x < _width; x++)
				{
					_cells[x, z] = new TerrainCell(_tiers[x, z] * EditableTerrain.TIER_HEIGHT);
				}
			}

			_rowCursor += rows;
			_stageProgress = _rowCursor / (float)Math.Max(_depth, 1);
			if (_rowCursor < _depth) return;

			EnsureTerrainMapsExist(_host, _width, _depth);
			_pathingCodes = EnsurePathingCodesExist(_host, _width, _depth);
			EnterStage(StageSplatMaps, "Painting terrain materials");
		}

		private void StepSplatMaps()
		{
			int rows = Math.Min(RowsPerChunk, _depth + 1 - _rowCursor);
			for (int i = 0; i < rows; i++)
			{
				int z = _rowCursor + i;
				if (z > _depth) break;

				int cellZ = Math.Clamp(z, 0, _depth - 1);
				for (int x = 0; x <= _width; x++)
				{
					int cellX = Math.Clamp(x, 0, _width - 1);
					sbyte tier = _tiers[cellX, cellZ];

					_host.GroundTerrain.SplatMap[x, z] = GetTerrainSplatWeight(tier);
					_host.GroundTerrain.CliffSplatMap[x, z] = TerrainSplatWeights.CreateSolid(1);

					if (x < _width && z < _depth)
					{
						_pathingCodes[x, z] = EditableTerrain.GetDefaultPathingCode(_cells[x, z]);
					}
				}
			}

			_rowCursor += rows;
			_stageProgress = _rowCursor / (float)Math.Max(_depth + 1, 1);
			if (_rowCursor < _depth + 1) return;

			_host.GroundTerrain.PathingCodes = _pathingCodes;
			EnterStage(StageApplyTerrain, "Uploading terrain");
		}

		private void StepApplyTerrain()
		{
			EcsWorld world = GameContext.World;
			Entity worldEntity = ECSContext.WorldEntity;
			if (world != null && world.IsAlive(worldEntity) && world.Has<TerrainState>(worldEntity))
			{
				ref var state = ref world.Get<TerrainState>(worldEntity);
				state.Cells = _cells;
				state.PathingCodes = _pathingCodes;
				world.Set(worldEntity, state);
			}

			_host.AlignTerrainSplatMapExternal();
			_host.GroundTerrain.UpdateMeshAndPhysics();
			_pathMask = _isPath;
			_isPath = null;

			EnterStage(StageUnitsAndProps, "Placing sites and resources");
		}

		private void StepUnitsAndProps()
		{
			_spawnEntries ??= BuildSpawnEntries();

			int remaining = _spawnEntries.Count - _spawnCursor;
			int count = Math.Min(SpawnsPerChunk, remaining);
			for (int i = 0; i < count; i++)
			{
				SpawnEntry entry = _spawnEntries[_spawnCursor++];
				if (entry.IsProp)
				{
					_host.SpawnPropExternalWithParams(entry.Id, entry.Position, entry.Rotation, entry.Scale);
				}
				else
				{
					_host.SpawnUnitExternal(entry.Id, entry.Position, entry.Rotation, entry.Scale, entry.Player);
				}
			}

			_stageProgress = _spawnEntries.Count > 0 ? _spawnCursor / (float)_spawnEntries.Count : 1.0f;
			if (_spawnCursor < _spawnEntries.Count) return;

			_spawnEntries = null;
			EnterStage(StagePathDistances, "Measuring distance to paths");
		}

		private void StepPathDistances()
		{
			_pathDist = CalculatePathDistancesForJob();
			_pathMask = null;

			EnterStage(StageTreeSampling, "Scattering trees");
		}

		private float[,] CalculatePathDistancesForJob()
		{
			float[,] pathDist = new float[_width, _depth];
			Queue<Vector2I> queue = new Queue<Vector2I>();
			for (int z = 0; z < _depth; z++)
			{
				for (int x = 0; x < _width; x++)
				{
					if (_pathMask[x, z])
					{
						pathDist[x, z] = 0f;
						queue.Enqueue(new Vector2I(x, z));
					}
					else
					{
						pathDist[x, z] = float.MaxValue;
					}
				}
			}

			while (queue.Count > 0)
			{
				Vector2I curr = queue.Dequeue();
				float d = pathDist[curr.X, curr.Y];
				ProcessDistanceNeighbor(new Vector2I(curr.X + 1, curr.Y), d, pathDist, queue, _width, _depth);
				ProcessDistanceNeighbor(new Vector2I(curr.X - 1, curr.Y), d, pathDist, queue, _width, _depth);
				ProcessDistanceNeighbor(new Vector2I(curr.X, curr.Y + 1), d, pathDist, queue, _width, _depth);
				ProcessDistanceNeighbor(new Vector2I(curr.X, curr.Y - 1), d, pathDist, queue, _width, _depth);
			}

			return pathDist;
		}

		private bool[,] _pathMask;

		private void StepTreeSampling()
		{
			float radius = (6.0f - (_treeDensity / 10.0f) * 4.0f) * GetSamplingScale(_width, _depth);
			_treePoints = PoissonDiscSample(_random, (_width - 1) * _quadSize, (_depth - 1) * _quadSize, radius);
			TrimSampledPoints(_treePoints);
			_treeCursor = 0;
			EnterStage(StageTreeSpawns, "Planting trees");
		}

		private void StepTreeSpawns()
		{
			int count = Math.Min(SpawnsPerChunk, _treePoints.Count - _treeCursor);
			for (int i = 0; i < count; i++)
			{
				TrySpawnTree(_host, _treePoints[_treeCursor++], _tiers, _pathDist, _width, _depth, _quadSize, _random);
			}

			_stageProgress = _treePoints.Count > 0 ? _treeCursor / (float)_treePoints.Count : 1.0f;
			if (_treeCursor < _treePoints.Count) return;

			_treePoints = null;
			EnterStage(StageDecoSampling, "Scattering decorations");
		}

		private void StepDecoSampling()
		{
			float radius = (15.0f - (_decoDensity / 10.0f) * 10.0f) * GetSamplingScale(_width, _depth);
			_decoPoints = PoissonDiscSample(_random, (_width - 1) * _quadSize, (_depth - 1) * _quadSize, radius);
			TrimSampledPoints(_decoPoints);
			_decoCursor = 0;
			EnterStage(StageDecoSpawns, "Placing decorations");
		}

		private void StepDecoSpawns()
		{
			string[] decoIds = { "rock", "pillar", "flag" };
			int count = Math.Min(SpawnsPerChunk, _decoPoints.Count - _decoCursor);
			for (int i = 0; i < count; i++)
			{
				TrySpawnDeco(_host, _decoPoints[_decoCursor++], _tiers, _pathDist, decoIds, _width, _depth, _quadSize, _random);
			}

			_stageProgress = _decoPoints.Count > 0 ? _decoCursor / (float)_decoPoints.Count : 1.0f;
			if (_decoCursor < _decoPoints.Count) return;

			_decoPoints = null;
			_pathDist = null;
			EnterStage(StageFinalize, "Finishing generation");
		}

		private void StepFinalize()
		{
			_tiers = null;
			_isPath = null;

			_host.EditorHasUnsavedChanges = true;
			_host.UpdateGridOverlayVisibility();
			_host.UpdateCameraBoundsOverlayVisibility();
			_host.UpdatePathingOverlay();
			GameContext.MapEditorHud?.ShowFeedbackExternal("Random map generation completed!");

			_stage = StageComplete;
			_stageProgress = 1.0f;
			StageDescription = TranslationServer.Translate("Generation complete");
		}

		private List<SpawnEntry> BuildSpawnEntries()
		{
			var entries = new List<SpawnEntry>();

			Vector3 p1Base = GridToWorld(_sites[0].X, _sites[0].Y, _width, _depth, _quadSize, _host.GroundTerrain.GetGridNodeHeight(_sites[0].X, _sites[0].Y));
			entries.Add(new SpawnEntry("castle", p1Base, 0f, 1.0f, 0, false));
			entries.Add(new SpawnEntry("adventurer", p1Base + new Vector3(-6f, 0f, -6f), 45f, 1.0f, 0, false));
			entries.Add(new SpawnEntry("adventurer", p1Base + new Vector3(-6f, 0f, 6f), 135f, 1.0f, 0, false));
			entries.Add(new SpawnEntry("adventurer", p1Base + new Vector3(6f, 0f, -6f), 315f, 1.0f, 0, false));
			entries.Add(new SpawnEntry("armored_battlelord", p1Base + new Vector3(8f, 0f, 8f), 225f, 1.0f, 0, false));
			entries.Add(new SpawnEntry("armored_battlelord", p1Base + new Vector3(0f, 0f, 10f), 180f, 1.0f, 0, false));

			Vector3 p2Base = GridToWorld(_sites[1].X, _sites[1].Y, _width, _depth, _quadSize, _host.GroundTerrain.GetGridNodeHeight(_sites[1].X, _sites[1].Y));
			entries.Add(new SpawnEntry("castle", p2Base, 180f, 1.0f, 1, false));
			entries.Add(new SpawnEntry("adventurer", p2Base + new Vector3(-6f, 0f, -6f), 45f, 1.0f, 1, false));
			entries.Add(new SpawnEntry("adventurer", p2Base + new Vector3(-6f, 0f, 6f), 135f, 1.0f, 1, false));
			entries.Add(new SpawnEntry("adventurer", p2Base + new Vector3(6f, 0f, -6f), 315f, 1.0f, 1, false));
			entries.Add(new SpawnEntry("armored_battlelord", p2Base + new Vector3(-8f, 0f, -8f), 45f, 1.0f, 1, false));
			entries.Add(new SpawnEntry("armored_battlelord", p2Base + new Vector3(0f, 0f, -10f), 0f, 1.0f, 1, false));

			int goldmineOneX = Math.Clamp(28, 0, _width - 1);
			int goldmineOneZ = Math.Clamp(20, 0, _depth - 1);
			Vector3 g1 = GridToWorld(goldmineOneX, goldmineOneZ, _width, _depth, _quadSize, _host.GroundTerrain.GetGridNodeHeight(goldmineOneX, goldmineOneZ));
			entries.Add(new SpawnEntry("goldmine", g1, 0f, 1.0f, 0, true));

			int goldmineTwoX = Math.Clamp(98, 0, _width - 1);
			int goldmineTwoZ = Math.Clamp(106, 0, _depth - 1);
			Vector3 g2 = GridToWorld(goldmineTwoX, goldmineTwoZ, _width, _depth, _quadSize, _host.GroundTerrain.GetGridNodeHeight(goldmineTwoX, goldmineTwoZ));
			entries.Add(new SpawnEntry("goldmine", g2, 180f, 1.0f, 0, true));

			Vector3 gMid = GridToWorld(_sites[2].X, _sites[2].Y, _width, _depth, _quadSize, _host.GroundTerrain.GetGridNodeHeight(_sites[2].X, _sites[2].Y));
			entries.Add(new SpawnEntry("goldmine", gMid, 90f, 1.0f, 0, true));

			for (int i = 3; i < _sites.Length; i++)
			{
				Vector3 expPos = GridToWorld(_sites[i].X, _sites[i].Y, _width, _depth, _quadSize, _host.GroundTerrain.GetGridNodeHeight(_sites[i].X, _sites[i].Y));
				entries.Add(new SpawnEntry("goldmine", expPos, (float)(_random.NextDouble() * 360.0), 1.0f, 0, true));
			}

			return entries;
		}

		private readonly struct SpawnEntry
		{
			public SpawnEntry(string id, Vector3 position, float rotation, float scale, int player, bool isProp)
			{
				Id = id;
				Position = position;
				Rotation = rotation;
				Scale = scale;
				Player = player;
				IsProp = isProp;
			}

			public string Id { get; }
			public Vector3 Position { get; }
			public float Rotation { get; }
			public float Scale { get; }
			public int Player { get; }
			public bool IsProp { get; }
		}
	}
}
