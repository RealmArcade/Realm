using Godot;
using System;
using System.Collections.Generic;
using Realm.Ecs.Components.Terrain;

public static class MapGenerator
{
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

	private static void CarvePathAt(Vector2I pos, int[,] baseGrid, int chokeWidth)
	{
		int radius = (int)(1.0f + (chokeWidth / 10.0f) * 3.0f);
		for (int z = -radius; z <= radius; z++)
		{
			for (int x = -radius; x <= radius; x++)
			{
				if (x * x + z * z > radius * radius) continue;
				
				int cx = pos.X + x;
				int cz = pos.Y + z;
				if (cx >= 0 && cx < 126 && cz >= 0 && cz < 126)
				{
					baseGrid[cx, cz] = 0;
				}
			}
		}
	}

	private static void InitTerrainState(Arch.Core.World world, Arch.Core.Entity worldEntity, int width, int depth, float quadSize)
	{
		if (!world.Has<Realm.Ecs.Components.Terrain.TerrainState>(worldEntity))
		{
			world.Add(worldEntity, new Realm.Ecs.Components.Terrain.TerrainState(
				width, depth, quadSize, 0.5f,
				new Realm.Ecs.Components.Terrain.TerrainCell[width, depth],
				new int[width, depth],
				null, null
			));
		}
		else
		{
			ref var ts = ref world.Get<Realm.Ecs.Components.Terrain.TerrainState>(worldEntity);
			bool modified = false;
			if (ts.Cells == null || ts.Cells.GetLength(0) != width || ts.Cells.GetLength(1) != depth)
			{
				ts.Cells = new Realm.Ecs.Components.Terrain.TerrainCell[width, depth];
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

	private static int[,] GenerateBaseGrid(int width, int depth, double fillChance, Random random)
	{
		int[,] baseGrid = new int[width, depth];
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				baseGrid[x, z] = (random.NextDouble() < fillChance) ? 1 : 0;
			}
		}

		for (int step = 0; step < 4; step++)
		{
			int[,] nextGrid = new int[width, depth];
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					int count = CountNeighborsBaseGrid(baseGrid, x, z, width, depth);
					nextGrid[x, z] = (count >= 5) ? 1 : 0;
				}
			}
			baseGrid = nextGrid;
		}
		return baseGrid;
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
		while (curr != start)
		{
			CarvePathAt(curr, baseGrid, chokeWidth);
			MarkPathRadius(curr, radius, isPath, width, depth);
			curr = cameFrom[curr];
		}
		CarvePathAt(start, baseGrid, chokeWidth);
		isPath[start.X, start.Y] = true;
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

	private static void GeneratePaths(Vector2I[] sites, int[,] baseGrid, bool[,] isPath, int chokeWidth, int width, int depth)
	{
		CalculatePathingEdges(sites, out List<Tuple<int, int>> edges);

		foreach (var edge in edges)
		{
			Vector2I start = sites[edge.Item1];
			Vector2I end = sites[edge.Item2];
			CalculatePathing(start, end, baseGrid, isPath, chokeWidth, width, depth);
		}

		ClearPathRadii(sites, isPath, width, depth);
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

	private static float[,] GenerateDensityMap(int[,] baseGrid, int width, int depth)
	{
		float[,] densityMap = new float[width, depth];
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				densityMap[x, z] = CalculateCellDensity(x, z, baseGrid, width, depth);
			}
		}
		return densityMap;
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

	private static sbyte[,] CalculateTiers(int[,] baseGrid, bool[,] isPath, float[,] densityMap, FastNoiseLite noise, FastNoiseLite detailNoise, float waterCutoff, float maxMountainTiers, int hillsDensity, int width, int depth)
	{
		sbyte[,] tiers = new sbyte[width, depth];
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				tiers[x, z] = CalculateCellTier(x, z, baseGrid, isPath, densityMap, noise, detailNoise, waterCutoff, maxMountainTiers, hillsDensity);
			}
		}
		return tiers;
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

	private static sbyte[,] SmoothTiers(sbyte[,] tiers, bool[,] isPath, int width, int depth)
	{
		for (int pass = 0; pass < 2; pass++)
		{
			sbyte[,] smoothedTiers = (sbyte[,])tiers.Clone();
			for (int z = 1; z < depth - 1; z++)
			{
				for (int x = 1; x < width - 1; x++)
				{
					if (tiers[x, z] > 0 && !isPath[x, z])
					{
						smoothedTiers[x, z] = SmoothCellTier(tiers, x, z);
					}
				}
			}
			tiers = smoothedTiers;
		}
		return tiers;
	}

		private static (TerrainCell[,], int[,]) ApplyTiersToTerrain(GameHost host, sbyte[,] tiers, int width, int depth)
		{
			var cells = CreateTerrainCells(tiers, width, depth);
			host.GroundTerrain.Cells = cells;

			EnsureTerrainMapsExist(host, width, depth);

			var pathingCodes = EnsurePathingCodesExist(host, width, depth);
			PopulateTerrainMaps(host, tiers, cells, pathingCodes, width, depth);

			host.GroundTerrain.PathingCodes = pathingCodes;
			
			return (cells, pathingCodes);
		}

		private static TerrainCell[,] CreateTerrainCells(sbyte[,] tiers, int width, int depth)
		{
			var cells = new TerrainCell[width, depth];
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					sbyte tier = tiers[x, z];
					float height = tier * EditableTerrain.TIER_HEIGHT;
					cells[x, z] = new TerrainCell(height);
				}
			}
			return cells;
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

		private static void PopulateTerrainMaps(GameHost host, sbyte[,] tiers, TerrainCell[,] cells, int[,] pathingCodes, int width, int depth)
		{
			for (int z = 0; z <= depth; z++)
			{
				for (int x = 0; x <= width; x++)
				{
					int cellX = Math.Clamp(x, 0, width - 1);
					int cellZ = Math.Clamp(z, 0, depth - 1);
					sbyte tier = tiers[cellX, cellZ];

					host.GroundTerrain.SplatMap[x, z] = GetTerrainSplatWeight(tier);
					host.GroundTerrain.CliffSplatMap[x, z] = TerrainSplatWeights.CreateSolid(1);

					if (x < width && z < depth)
					{
						pathingCodes[x, z] = EditableTerrain.GetDefaultPathingCode(cells[x, z]);
					}
				}
			}
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

	private static void SpawnUnitsAndProps(GameHost host, Vector2I[] sites, int width, int depth, float quadSize, Random random)
	{
		Vector3 p1Base = GridToWorld(sites[0].X, sites[0].Y, width, depth, quadSize, host.GroundTerrain.GetGridNodeHeight(sites[0].X, sites[0].Y));
		host.SpawnUnitExternal("castle", p1Base, false, 0f, 1.0f);
		host.SpawnUnitExternal("adventurer", p1Base + new Vector3(-6f, 0f, -6f), false, 45f, 1.0f);
		host.SpawnUnitExternal("adventurer", p1Base + new Vector3(-6f, 0f, 6f), false, 135f, 1.0f);
		host.SpawnUnitExternal("adventurer", p1Base + new Vector3(6f, 0f, -6f), false, 315f, 1.0f);
		host.SpawnUnitExternal("armored_battlelord", p1Base + new Vector3(8f, 0f, 8f), false, 225f, 1.0f);
		host.SpawnUnitExternal("armored_battlelord", p1Base + new Vector3(0f, 0f, 10f), false, 180f, 1.0f);

		Vector3 p2Base = GridToWorld(sites[1].X, sites[1].Y, width, depth, quadSize, host.GroundTerrain.GetGridNodeHeight(sites[1].X, sites[1].Y));
		host.SpawnUnitExternal("castle", p2Base, true, 180f, 1.0f);
		host.SpawnUnitExternal("adventurer", p2Base + new Vector3(-6f, 0f, -6f), true, 45f, 1.0f);
		host.SpawnUnitExternal("adventurer", p2Base + new Vector3(-6f, 0f, 6f), true, 135f, 1.0f);
		host.SpawnUnitExternal("adventurer", p2Base + new Vector3(6f, 0f, -6f), true, 315f, 1.0f);
		host.SpawnUnitExternal("armored_battlelord", p2Base + new Vector3(-8f, 0f, -8f), true, 45f, 1.0f);
		host.SpawnUnitExternal("armored_battlelord", p2Base + new Vector3(0f, 0f, -10f), true, 0f, 1.0f);

		Vector3 g1 = GridToWorld(28, 20, width, depth, quadSize, host.GroundTerrain.GetGridNodeHeight(28, 20));
		host.SpawnPropExternalWithParams("goldmine", g1, 0f, 1.0f);

		Vector3 g2 = GridToWorld(98, 106, width, depth, quadSize, host.GroundTerrain.GetGridNodeHeight(98, 106));
		host.SpawnPropExternalWithParams("goldmine", g2, 180f, 1.0f);

		Vector3 gMid = GridToWorld(sites[2].X, sites[2].Y, width, depth, quadSize, host.GroundTerrain.GetGridNodeHeight(sites[2].X, sites[2].Y));
		host.SpawnPropExternalWithParams("goldmine", gMid, 90f, 1.0f);

		for (int i = 3; i < sites.Length; i++)
		{
			Vector3 expPos = GridToWorld(sites[i].X, sites[i].Y, width, depth, quadSize, host.GroundTerrain.GetGridNodeHeight(sites[i].X, sites[i].Y));
			host.SpawnPropExternalWithParams("goldmine", expPos, (float)(random.NextDouble() * 360.0), 1.0f);
		}
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

	private static void TrySpawnTree(GameHost host, Vector2 p, sbyte[,] tiers, float[,] pathDist, int width, int depth, float quadSize, Random random)
	{
		Vector3 worldPos = new Vector3(p.X - (width - 1) * quadSize / 2.0f, 0f, p.Y - (depth - 1) * quadSize / 2.0f);
		WorldToGrid(worldPos, width, depth, quadSize, out int gx, out int gz);
		if (gx < 0 || gx >= width || gz < 0 || gz >= depth) return;
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
		Vector3 worldPos = new Vector3(p.X - (width - 1) * quadSize / 2.0f, 0f, p.Y - (depth - 1) * quadSize / 2.0f);
		WorldToGrid(worldPos, width, depth, quadSize, out int gx, out int gz);
		if (gx < 0 || gx >= width || gz < 0 || gz >= depth) return;
		if (tiers[gx, gz] >= 0 && pathDist[gx, gz] > 2f)
		{
			worldPos.Y = host.GroundTerrain.GetGridNodeHeight(gx, gz);
			float rot = (float)(random.NextDouble() * 360.0);
			float scale = 0.7f + (float)(random.NextDouble() * 0.6);
			string propId = decoIds[random.Next(decoIds.Length)];
			host.SpawnPropExternalWithParams(propId, worldPos, rot, scale);
		}
	}

	private static void SpawnTreesAndDeco(GameHost host, sbyte[,] tiers, float[,] pathDist, int treeDensity, int decoDensity, int width, int depth, float quadSize, Random random)
	{
		float r_trees = 6.0f - (treeDensity / 10.0f) * 4.0f;
		List<Vector2> treePoints = PoissonDiscSample(random, (width - 1) * quadSize, (depth - 1) * quadSize, r_trees);
		foreach (var p in treePoints)
		{
			TrySpawnTree(host, p, tiers, pathDist, width, depth, quadSize, random);
		}

		float r_deco = 15.0f - (decoDensity / 10.0f) * 10.0f;
		List<Vector2> decoPoints = PoissonDiscSample(random, (width - 1) * quadSize, (depth - 1) * quadSize, r_deco);
		string[] decoIds = new string[] { "rock", "pillar", "flag" };
		foreach (var p in decoPoints)
		{
			TrySpawnDeco(host, p, tiers, pathDist, decoIds, width, depth, quadSize, random);
		}
	}

	public static void GenerateMap(
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
		if (host?.GroundTerrain == null) return;

		if (!int.TryParse(seedString, out int seed))
		{
			seed = seedString.GetHashCode();
		}
		Random random = new Random(seed);

		host.ClearMapEntirely();

		int width = host.GroundTerrain.Width;
		int depth = host.GroundTerrain.Depth;
		float quadSize = host.GroundTerrain.QuadSize;

		var world = host.EcsWorld;
		var worldEntity = host.WorldEntity;
		if (world != null && world.IsAlive(worldEntity))
		{
			InitTerrainState(world, worldEntity, width, depth, quadSize);
		}

		double fillChance = 0.25 + (hillsDensity / 10.0) * 0.40;
		int[,] baseGrid = GenerateBaseGrid(width, depth, fillChance, random);

		Vector2I[] sites = new Vector2I[]
		{
			new Vector2I(20, 20),
			new Vector2I(106, 106),
			new Vector2I(63, 63),
			new Vector2I(20, 106),
			new Vector2I(106, 20),
			new Vector2I(63, 20),
			new Vector2I(63, 106),
			new Vector2I(20, 63),
			new Vector2I(106, 63)
		};

		ClearSiteRadii(baseGrid, sites, width, depth);

		bool[,] isPath = new bool[width, depth];
		GeneratePaths(sites, baseGrid, isPath, chokeWidth, width, depth);

		var noise = new FastNoiseLite();
		noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
		noise.Seed = seed;
		noise.Frequency = 0.01f + (terrainRoughness / 10.0f) * 0.04f;

		var detailNoise = new FastNoiseLite();
		detailNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
		detailNoise.Seed = seed + 777;
		detailNoise.Frequency = 0.035f;

		float waterCutoff = 0.15f + (waterLevel / 10.0f) * 0.25f;
		float maxMountainTiers = 2.0f + (mountainHeight / 10.0f) * 6.0f;

		float[,] densityMap = GenerateDensityMap(baseGrid, width, depth);
		sbyte[,] tiers = CalculateTiers(baseGrid, isPath, densityMap, noise, detailNoise, waterCutoff, maxMountainTiers, hillsDensity, width, depth);
		tiers = SmoothTiers(tiers, isPath, width, depth);

		if (world != null && world.IsAlive(worldEntity))
		{
			ref var state = ref world.Get<Realm.Ecs.Components.Terrain.TerrainState>(worldEntity);
			var (cells, pathingCodes) = ApplyTiersToTerrain(host, tiers, width, depth);
			state.Cells = cells;
			state.PathingCodes = pathingCodes;
			world.Set(worldEntity, state);
		}
		else
		{
			ApplyTiersToTerrain(host, tiers, width, depth);
		}

		host.AlignTerrainSplatMapExternal();
		host.GroundTerrain.UpdateMeshAndPhysics();

		SpawnUnitsAndProps(host, sites, width, depth, quadSize, random);

		float[,] pathDist = CalculatePathDistances(isPath, width, depth);

		SpawnTreesAndDeco(host, tiers, pathDist, treeDensity, decoDensity, width, depth, quadSize, random);

		host.EditorHasUnsavedChanges = true;
		host.UpdateGridOverlayVisibility();
		host.UpdateCameraBoundsOverlayVisibility();
		host.UpdatePathingOverlay();
		MapEditorHUD.Instance?.ShowFeedbackExternal("Random map generation completed!");
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
}
