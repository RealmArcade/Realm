using System;
using System.Collections.Generic;
using System.Diagnostics;
using Realm.EditorAPI;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client.Services.MapGeneration;

public class ProceduralMapGenerator
{
	private const int AnchorCount = 9;
	private const int MaximumScatterPoints = 20000;

	private static readonly float[] StageWeights = { 2f, 10f, 3f, 14f, 4f, 8f, 3f, 3f, 5f, 9f, 4f, 4f, 10f, 8f, 6f, 5f, 2f };
	private static readonly float[] StageStartPercents = BuildStageStartPercents();
	private static readonly float[] AnchorFractions = { 1f / 6f, 0.5f, 5f / 6f };

	private readonly IEditorAPI _editorApi;
	private readonly TerrainMaterialPalette _palette;
	private readonly string[] _treePropIds;
	private readonly string[] _decorationPropIds;
	private readonly int _width;
	private readonly int _depth;
	private readonly float _quadSize;
	private readonly int _seed;
	private readonly MapGenerationParameters _parameters;
	private SeededRandom _random;
	private readonly SeededValueNoise _coarseNoise;
	private readonly SeededValueNoise _detailNoise;
	private readonly AnchorCell[] _anchors = new AnchorCell[AnchorCount];

	private readonly bool[,] _land;
	private readonly bool[,] _landScratch;
	private readonly bool[,] _path;
	private readonly bool[,] _walkable;
	private readonly float[,] _density;
	private readonly sbyte[,] _tier;
	private readonly sbyte[,] _tierScratch;
	private readonly int[] _tierCounts = new int[32];
	private readonly int[,] _material;
	private readonly int[,] _distance;
	private readonly sbyte[] _flatTiers;
	private readonly int[] _flatMaterials;

	private readonly float _landFillChance;
	private readonly float _waterNoiseCutoff;
	private readonly float _maximumMountainTiers;
	private readonly float _primaryNoiseFrequency;
	private readonly float _detailNoiseFrequency;
	private readonly float _hillIslandThreshold;
	private readonly int _pathBrushRadius;
	private readonly float _treeScatterRadius;
	private readonly float _decorationScatterRadius;

	private int _stageIndex;
	private float _stageProgress;
	private float _reportedPercent;

	private int _pass;
	private int _row;
	private bool _passPrepared;
	private int _anchorIndex;

	private List<MstEdge> _mstEdges = new();
	private bool _mstComputed;
	private int _pathPhase;
	private int _edgeIndex;
	private PriorityQueue<int, float>? _astarOpen;
	private float[]? _astarCost;
	private int[]? _astarCameFrom;
	private bool[]? _astarClosed;
	private int _astarStart;
	private int _astarGoal;
	private int _astarSequence;
	private readonly List<List<CellPosition>> _routes = new();
	private List<CellPosition> _currentRoute = new();
	private int _carveRouteIndex;
	private int _carvePointIndex;
	private int _distanceRow;
	private bool _distanceInitialized;
	private readonly Queue<int> _distanceQueue = new();
	private PoissonScatter? _treeScatter;
	private PoissonScatter? _decorationScatter;
	private int _treePlantIndex;
	private int _decorationPlantIndex;
	private readonly bool[] _waterScanned;
	private readonly List<int> _waterRegionCells = new();
	private readonly List<WaterPoolSelection> _waterPools = new();
	private int _waterScanIndex;
	private int _waterPoolIndex;

	public ProceduralMapGenerator(
		IEditorAPI editorApi,
		int width,
		int depth,
		float quadSize,
		string seedText,
		MapGenerationParameters parameters,
		TerrainMaterialPalette palette,
		string[] treePropIds,
		string[] decorationPropIds)
	{
		_editorApi = editorApi;
		_width = Math.Max(1, width);
		_depth = Math.Max(1, depth);
		_quadSize = quadSize > 0f ? quadSize : TerrainState.DefaultQuadSize;
		_parameters = parameters.Clamped();
		_palette = palette;
		_treePropIds = treePropIds ?? Array.Empty<string>();
		_decorationPropIds = decorationPropIds ?? Array.Empty<string>();
		_seed = DeterministicSeed.HashText(seedText);
		Seed = seedText ?? string.Empty;
		_random = new SeededRandom(_seed);
		_coarseNoise = new SeededValueNoise(_seed);
		_detailNoise = new SeededValueNoise(unchecked(_seed ^ 0x5f3759df));

		_land = new bool[_width, _depth];
		_landScratch = new bool[_width, _depth];
		_path = new bool[_width, _depth];
		_walkable = new bool[_width, _depth];
		_density = new float[_width, _depth];
		_tier = new sbyte[_width, _depth];
		_tierScratch = new sbyte[_width, _depth];
		_material = new int[_width, _depth];
		_distance = new int[_width, _depth];
		_flatTiers = new sbyte[_width * _depth];
		_flatMaterials = new int[_width * _depth];
		_waterScanned = new bool[_width * _depth];

		float hills = _parameters.HillsDensity;
		float roughness = _parameters.TerrainRoughness;
		float mountain = _parameters.MountainHeight;
		float choke = _parameters.ChokePointWidth;
		float water = _parameters.WaterLevel;

		_landFillChance = 0.25f + hills / 10f * 0.40f;
		_waterNoiseCutoff = 0.15f + water / 10f * 0.25f;
		_maximumMountainTiers = 2f + mountain / 10f * 6f;
		_primaryNoiseFrequency = 0.01f + roughness / 10f * 0.04f;
		_detailNoiseFrequency = 0.11f;
		_hillIslandThreshold = 0.62f - (hills - 1f) * 0.03f;
		_pathBrushRadius = 1 + (int)MathF.Round((choke - 1f) / 9f * 3f);

		float mapScale = Math.Clamp(MathF.Sqrt(_width * _depth / 16384f), 1f, 2f);
		float abundanceMultiplier = 0.6f + _parameters.ResourceAbundance * 0.1f;
		float effectiveTreeDensity = Math.Clamp(_parameters.TreeClumpDensity * abundanceMultiplier / 10f, 0.05f, 1.6f);
		_treeScatterRadius = Math.Clamp(6f - 4.5f * effectiveTreeDensity, 1.5f, 6f) * mapScale;

		float decorationDensity = (_parameters.DecorativePropDensity - 1f) / 9f;
		_decorationScatterRadius = (15f - decorationDensity * 10f) * mapScale;
	}

	public string Seed { get; }

	public bool HasFailed { get; private set; }

	public bool IsComplete { get; private set; }

	public string? FailureMessage { get; private set; }

	public MapGenerationStage Stage => (MapGenerationStage)Math.Min(_stageIndex, StageWeights.Length - 1);

	public float Percent => IsComplete ? 100f : Math.Clamp(_reportedPercent, 0f, 99.9f);

	public bool Advance(int budgetMilliseconds)
	{
		if (IsComplete || HasFailed)
		{
			return true;
		}

		var stopwatch = Stopwatch.StartNew();
		do
		{
			int remainingBudget = Math.Max(1, budgetMilliseconds - (int)stopwatch.ElapsedMilliseconds);
			bool stageFinished;
			try
			{
				stageFinished = RunStageStep(remainingBudget);
			}
			catch (Exception exception)
			{
				HasFailed = true;
				FailureMessage = exception.Message;
				return true;
			}

			if (stageFinished)
			{
				AdvanceStage();
			}

			_reportedPercent = Math.Max(_reportedPercent, ComputePercent());
		}
		while (!IsComplete && stopwatch.ElapsedMilliseconds < budgetMilliseconds);

		return IsComplete;
	}

	private void AdvanceStage()
	{
		_stageIndex++;
		_stageProgress = 0f;
		_pass = 0;
		_row = 0;
		_passPrepared = false;
		_anchorIndex = 0;
		_pathPhase = 0;
		_edgeIndex = 0;
		_carveRouteIndex = 0;
		_carvePointIndex = 0;
		_distanceRow = 0;
		_distanceInitialized = false;
		_distanceQueue.Clear();
		_treePlantIndex = 0;
		_decorationPlantIndex = 0;
		_waterScanIndex = 0;
		_waterPoolIndex = 0;

		if (_stageIndex >= StageWeights.Length)
		{
			IsComplete = true;
			_reportedPercent = 100f;
			return;
		}

		_reportedPercent = Math.Max(_reportedPercent, StageStartPercents[_stageIndex]);
	}

	private float ComputePercent()
	{
		if (IsComplete)
		{
			return 100f;
		}

		int stageIndex = Math.Min(_stageIndex, StageWeights.Length - 1);
		return StageStartPercents[stageIndex] + Math.Clamp(_stageProgress, 0f, 1f) * StageWeights[stageIndex];
	}

	private static float[] BuildStageStartPercents()
	{
		var starts = new float[StageWeights.Length];
		float running = 0f;
		for (int stageIndex = 0; stageIndex < StageWeights.Length; stageIndex++)
		{
			starts[stageIndex] = running;
			running += StageWeights[stageIndex];
		}

		return starts;
	}

	private bool RunStageStep(int remainingBudgetMilliseconds)
	{
		return (MapGenerationStage)_stageIndex switch
		{
			MapGenerationStage.Preparing => RunPreparing(),
			MapGenerationStage.ShapingLandMasses => RunLandmass(),
			MapGenerationStage.ClearingStartAreas => RunClearStartAreas(),
			MapGenerationStage.CarvingPaths => RunPaths(remainingBudgetMilliseconds),
			MapGenerationStage.MeasuringDensity => RunDensity(),
			MapGenerationStage.RaisingMountainsAndWater => RunHeightTiers(),
			MapGenerationStage.SmoothingTiers => RunSmoothing(),
			MapGenerationStage.BuildingHeights => RunBuildHeights(),
			MapGenerationStage.PaintingMaterials => RunPaintMaterials(),
			MapGenerationStage.UploadingTerrain => RunUploadTerrain(),
			MapGenerationStage.FillingWater => RunFillWater(),
			MapGenerationStage.MeasuringDistanceToPaths => RunDistanceToPaths(remainingBudgetMilliseconds),
			MapGenerationStage.ScatteringTrees => RunScatterTrees(remainingBudgetMilliseconds),
			MapGenerationStage.PlantingTrees => RunPlantTrees(),
			MapGenerationStage.ScatteringDecorations => RunScatterDecorations(remainingBudgetMilliseconds),
			MapGenerationStage.PlacingDecorations => RunPlaceDecorations(),
			MapGenerationStage.Finishing => RunFinishing(),
			_ => true,
		};
	}

	private bool RunPreparing()
	{
		for (int zIndex = 0; zIndex < 3; zIndex++)
		{
			for (int xIndex = 0; xIndex < 3; xIndex++)
			{
				int anchorIndex = zIndex * 3 + xIndex;
				int cellX = Math.Clamp((int)MathF.Round(AnchorFractions[xIndex] * (_width - 1)), 0, _width - 1);
				int cellZ = Math.Clamp((int)MathF.Round(AnchorFractions[zIndex] * (_depth - 1)), 0, _depth - 1);
				int clearRadius = anchorIndex is 0 or 8 ? 15 : anchorIndex == 4 ? 12 : 8;
				_anchors[anchorIndex] = new AnchorCell(cellX, cellZ, clearRadius);
			}
		}

		_stageProgress = 1f;
		return true;
	}

	private bool RunLandmass()
	{
		if (_row >= _depth)
		{
			if (_pass == 0)
			{
				_pass = 1;
				_row = 0;
				_passPrepared = false;
				return false;
			}

			_pass++;
			_row = 0;
			_passPrepared = false;
			if (_pass > 4)
			{
				_stageProgress = 1f;
				return true;
			}
		}

		if (_pass == 0)
		{
			for (int x = 0; x < _width; x++)
			{
				_land[x, _row] = _random.NextFloat() < _landFillChance;
			}

			_stageProgress = _row / (float)_depth * 0.2f;
			_row++;
			return false;
		}

		if (!_passPrepared)
		{
			Array.Copy(_land, _landScratch, _land.Length);
			_passPrepared = true;
		}

		int z = _row;
		for (int x = 0; x < _width; x++)
		{
			int landNeighbours = 0;
			for (int neighbourZ = z - 1; neighbourZ <= z + 1; neighbourZ++)
			{
				bool rowOutside = neighbourZ < 0 || neighbourZ >= _depth;
				for (int neighbourX = x - 1; neighbourX <= x + 1; neighbourX++)
				{
					if (rowOutside || neighbourX < 0 || neighbourX >= _width || _landScratch[neighbourX, neighbourZ])
					{
						landNeighbours++;
					}
				}
			}

			_land[x, z] = landNeighbours >= 5;
		}

		_row++;
		_stageProgress = (0.2f + (_pass - 1 + _row / (float)_depth) / 4f * 0.8f);
		return false;
	}

	private bool RunClearStartAreas()
	{
		AnchorCell anchor = _anchors[_anchorIndex];
		int radiusSquared = anchor.ClearRadius * anchor.ClearRadius;
		for (int z = Math.Max(0, anchor.CellZ - anchor.ClearRadius); z <= Math.Min(_depth - 1, anchor.CellZ + anchor.ClearRadius); z++)
		{
			int deltaZ = z - anchor.CellZ;
			for (int x = Math.Max(0, anchor.CellX - anchor.ClearRadius); x <= Math.Min(_width - 1, anchor.CellX + anchor.ClearRadius); x++)
			{
				int deltaX = x - anchor.CellX;
				if (deltaX * deltaX + deltaZ * deltaZ <= radiusSquared)
				{
					_land[x, z] = false;
				}
			}
		}

		_anchorIndex++;
		_stageProgress = _anchorIndex / (float)AnchorCount;
		return _anchorIndex >= AnchorCount;
	}

	private bool RunPaths(int remainingBudgetMilliseconds)
	{
		if (_pathPhase == 0)
		{
			ComputeMst();
			_pathPhase = 1;
			return false;
		}

		if (_pathPhase == 1)
		{
			if (_edgeIndex >= _mstEdges.Count)
			{
				_pathPhase = 2;
				return false;
			}

			MstEdge edge = _mstEdges[_edgeIndex];
			if (_astarOpen == null)
			{
				StartAStar(edge);
			}

			bool finished = StepAStar(750);
			_stageProgress = 0.05f + 0.55f * (_edgeIndex + (finished ? 1f : 0.5f)) / Math.Max(1, _mstEdges.Count);
			if (!finished)
			{
				return false;
			}

			if (_currentRoute.Count > 0)
			{
				_routes.Add(_currentRoute);
			}

			_currentRoute = new List<CellPosition>();
			_astarOpen = null;
			_astarCost = null;
			_astarCameFrom = null;
			_astarClosed = null;
			_edgeIndex++;
			return false;
		}

		if (_pathPhase == 2)
		{
			int carvedThisStep = 0;
			while (_carveRouteIndex < _routes.Count && carvedThisStep < 400)
			{
				List<CellPosition> route = _routes[_carveRouteIndex];
				while (_carvePointIndex < route.Count && carvedThisStep < 400)
				{
					CarvePathAt(route[_carvePointIndex]);
					_carvePointIndex++;
					carvedThisStep++;
				}

				if (_carvePointIndex < route.Count)
				{
					break;
				}

				_carveRouteIndex++;
				_carvePointIndex = 0;
			}

			_stageProgress = 0.6f + 0.3f * (_carveRouteIndex / (float)Math.Max(1, _routes.Count));
			if (_carveRouteIndex < _routes.Count)
			{
				return false;
			}

			_pathPhase = 3;
			return false;
		}

		for (int anchorIndex = 0; anchorIndex < AnchorCount; anchorIndex++)
		{
			MarkWalkableCircle(_anchors[anchorIndex].CellX, _anchors[anchorIndex].CellZ, 10);
		}

		_stageProgress = 1f;
		return true;
	}

	private void ComputeMst()
	{
		if (_mstComputed)
		{
			return;
		}

		_mstComputed = true;
		var inTree = new bool[AnchorCount];
		inTree[0] = true;
		for (int edgeIndex = 0; edgeIndex < AnchorCount - 1; edgeIndex++)
		{
			float bestDistance = float.MaxValue;
			int bestFrom = 0;
			int bestTo = 0;
			for (int fromIndex = 0; fromIndex < AnchorCount; fromIndex++)
			{
				if (!inTree[fromIndex])
				{
					continue;
				}

				for (int toIndex = 0; toIndex < AnchorCount; toIndex++)
				{
					if (inTree[toIndex])
					{
						continue;
					}

					float deltaX = _anchors[fromIndex].CellX - _anchors[toIndex].CellX;
					float deltaZ = _anchors[fromIndex].CellZ - _anchors[toIndex].CellZ;
					float distance = deltaX * deltaX + deltaZ * deltaZ;
					if (distance < bestDistance)
					{
						bestDistance = distance;
						bestFrom = fromIndex;
						bestTo = toIndex;
					}
				}
			}

			_mstEdges.Add(new MstEdge(bestFrom, bestTo));
			inTree[bestTo] = true;
		}
	}

	private void StartAStar(MstEdge edge)
	{
		int cellCount = _width * _depth;
		_astarCost = _astarCost == null || _astarCost.Length != cellCount ? new float[cellCount] : _astarCost;
		_astarCameFrom = _astarCameFrom == null || _astarCameFrom.Length != cellCount ? new int[cellCount] : _astarCameFrom;
		_astarClosed = _astarClosed == null || _astarClosed.Length != cellCount ? new bool[cellCount] : _astarClosed;
		Array.Fill(_astarCost, float.PositiveInfinity);
		Array.Fill(_astarCameFrom, -1);
		Array.Fill(_astarClosed, false);
		_astarOpen = new PriorityQueue<int, float>();
		_astarStart = _anchors[edge.From].CellZ * _width + _anchors[edge.From].CellX;
		_astarGoal = _anchors[edge.To].CellZ * _width + _anchors[edge.To].CellX;
		_astarCost[_astarStart] = 0f;
		_astarSequence = 0;
		_astarOpen.Enqueue(_astarStart, Heuristic(_astarStart));
		_currentRoute = new List<CellPosition>();
	}

	private bool StepAStar(int nodeBudget)
	{
		if (_astarOpen == null || _astarCost == null || _astarCameFrom == null || _astarClosed == null)
		{
			return true;
		}

		for (int processed = 0; processed < nodeBudget; processed++)
		{
			if (!_astarOpen.TryDequeue(out int currentNode, out _))
			{
				return true;
			}

			if (_astarClosed[currentNode])
			{
				continue;
			}

			if (currentNode == _astarGoal)
			{
				ReconstructRoute();
				return true;
			}

			_astarClosed[currentNode] = true;
			int currentX = currentNode % _width;
			int currentZ = currentNode / _width;
			ExpandAStarNeighbour(currentNode, currentX - 1, currentZ);
			ExpandAStarNeighbour(currentNode, currentX + 1, currentZ);
			ExpandAStarNeighbour(currentNode, currentX, currentZ - 1);
			ExpandAStarNeighbour(currentNode, currentX, currentZ + 1);
		}

		return false;
	}

	private void ExpandAStarNeighbour(int currentNode, int neighbourX, int neighbourZ)
	{
		if (_astarCost == null || _astarClosed == null || _astarOpen == null || _astarCameFrom == null)
		{
			return;
		}

		if (neighbourX < 0 || neighbourX >= _width || neighbourZ < 0 || neighbourZ >= _depth)
		{
			return;
		}

		int neighbourIndex = neighbourZ * _width + neighbourX;
		if (_astarClosed[neighbourIndex])
		{
			return;
		}

		float stepCost = _land[neighbourX, neighbourZ] ? 16f : 1f;
		float candidateCost = _astarCost[currentNode] + stepCost;
		if (candidateCost >= _astarCost[neighbourIndex])
		{
			return;
		}

		_astarCost[neighbourIndex] = candidateCost;
		_astarCameFrom[neighbourIndex] = currentNode;
		float priority = candidateCost + Heuristic(neighbourIndex) + _astarSequence++ * 0.00001f;
		_astarOpen.Enqueue(neighbourIndex, priority);
	}

	private float Heuristic(int cellIndex)
	{
		int x = cellIndex % _width;
		int z = cellIndex / _width;
		int goalX = _astarGoal % _width;
		int goalZ = _astarGoal / _width;
		float deltaX = x - goalX;
		float deltaZ = z - goalZ;
		return MathF.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
	}

	private void ReconstructRoute()
	{
		if (_astarCameFrom == null)
		{
			return;
		}

		int node = _astarGoal;
		while (node != -1)
		{
			_currentRoute.Add(new CellPosition(node % _width, node / _width));
			node = _astarCameFrom[node];
			if (_currentRoute.Count > _width * _depth)
			{
				break;
			}
		}

		_currentRoute.Reverse();
	}

	private void CarvePathAt(CellPosition position)
	{
		MarkLandCircle(position.X, position.Z, _pathBrushRadius);
		MarkWalkableCircle(position.X, position.Z, _pathBrushRadius + 1);
	}

	private void MarkLandCircle(int centerX, int centerZ, int radius)
	{
		int radiusSquared = radius * radius;
		for (int z = Math.Max(0, centerZ - radius); z <= Math.Min(_depth - 1, centerZ + radius); z++)
		{
			int deltaZ = z - centerZ;
			for (int x = Math.Max(0, centerX - radius); x <= Math.Min(_width - 1, centerX + radius); x++)
			{
				int deltaX = x - centerX;
				if (deltaX * deltaX + deltaZ * deltaZ > radiusSquared)
				{
					continue;
				}

				_land[x, z] = false;
				_path[x, z] = true;
				_walkable[x, z] = true;
			}
		}
	}

	private void MarkWalkableCircle(int centerX, int centerZ, int radius)
	{
		int radiusSquared = radius * radius;
		for (int z = Math.Max(0, centerZ - radius); z <= Math.Min(_depth - 1, centerZ + radius); z++)
		{
			int deltaZ = z - centerZ;
			for (int x = Math.Max(0, centerX - radius); x <= Math.Min(_width - 1, centerX + radius); x++)
			{
				int deltaX = x - centerX;
				if (deltaX * deltaX + deltaZ * deltaZ <= radiusSquared)
				{
					_walkable[x, z] = true;
				}
			}
		}
	}

	private bool RunDensity()
	{
		int z = _row;
		for (int x = 0; x < _width; x++)
		{
			int landCells = 0;
			int sampledCells = 0;
			for (int neighbourZ = z - 2; neighbourZ <= z + 2; neighbourZ++)
			{
				if (neighbourZ < 0 || neighbourZ >= _depth)
				{
					continue;
				}

				for (int neighbourX = x - 2; neighbourX <= x + 2; neighbourX++)
				{
					if (neighbourX < 0 || neighbourX >= _width)
					{
						continue;
					}

					sampledCells++;
					if (_land[neighbourX, neighbourZ])
					{
						landCells++;
					}
				}
			}

			_density[x, z] = sampledCells > 0 ? landCells / (float)sampledCells : 0f;
		}

		_row++;
		_stageProgress = _row / (float)_depth;
		return _row >= _depth;
	}

	private bool RunHeightTiers()
	{
		int z = _row;
		for (int x = 0; x < _width; x++)
		{
			float coarse = _coarseNoise.Sample(x * _primaryNoiseFrequency, z * _primaryNoiseFrequency);
			float detail = _detailNoise.Sample(x * _detailNoiseFrequency, z * _detailNoiseFrequency);

			if (_path[x, z])
			{
				_tier[x, z] = 0;
				continue;
			}

			if (_land[x, z])
			{
				float rawHeight = (_density[x, z] * 0.5f + coarse * 0.35f + detail * 0.15f) * _maximumMountainTiers;
				int plateauSteps = (int)MathF.Round(rawHeight / 2f) * 2;
				int tier = 1 + plateauSteps;
				_tier[x, z] = (sbyte)Math.Clamp(tier, 1, TerrainCell.MAX_MACRO_TIER);
				continue;
			}

			if (coarse < _waterNoiseCutoff)
			{
				float depthBelowCutoff = Math.Clamp((_waterNoiseCutoff - coarse) / Math.Max(0.001f, _waterNoiseCutoff), 0f, 1f);
				int tier = -(1 + (int)(depthBelowCutoff * 3.99f));
				_tier[x, z] = (sbyte)Math.Clamp(tier, -4, -1);
				continue;
			}

			if (_parameters.HillsDensity > 2 && coarse > _hillIslandThreshold)
			{
				_tier[x, z] = (sbyte)Math.Clamp(1 + (int)(detail * 3.99f), 1, 4);
				continue;
			}

			_tier[x, z] = 0;
		}

		_row++;
		_stageProgress = _row / (float)_depth;
		return _row >= _depth;
	}

	private bool RunSmoothing()
	{
		if (_pass >= 2)
		{
			_stageProgress = 1f;
			return true;
		}

		if (_row == 0 && !_passPrepared)
		{
			Array.Copy(_tier, _tierScratch, _tier.Length);
			_passPrepared = true;
		}

		int z = _row;
		for (int x = 0; x < _width; x++)
		{
			if (!_land[x, z] || _path[x, z] || _tierScratch[x, z] <= 0)
			{
				continue;
			}

			Array.Clear(_tierCounts);
			for (int neighbourZ = z - 1; neighbourZ <= z + 1; neighbourZ++)
			{
				if (neighbourZ < 0 || neighbourZ >= _depth)
				{
					continue;
				}

				for (int neighbourX = x - 1; neighbourX <= x + 1; neighbourX++)
				{
					if (neighbourX < 0 || neighbourX >= _width || (neighbourX == x && neighbourZ == z))
					{
						continue;
					}

					int neighbourTier = _tierScratch[neighbourX, neighbourZ];
					if (neighbourTier > 0)
					{
						_tierCounts[Math.Min(_tierCounts.Length - 1, neighbourTier)]++;
					}
				}
			}

			for (int candidateTier = _tierCounts.Length - 1; candidateTier >= 1; candidateTier--)
			{
				if (_tierCounts[candidateTier] >= 4)
				{
					_tier[x, z] = (sbyte)candidateTier;
					break;
				}
			}
		}

		_row++;
		if (_row >= _depth)
		{
			_pass++;
			_row = 0;
			_passPrepared = false;
		}

		_stageProgress = (_pass + _row / (float)_depth) / 2f;
		return _pass >= 2;
	}

	private bool RunBuildHeights()
	{
		for (int z = 0; z < _depth; z++)
		{
			for (int x = 0; x < _width; x++)
			{
				if (_tier[x, z] < -4)
				{
					_tier[x, z] = -4;
				}
				else if (_tier[x, z] > TerrainCell.MAX_MACRO_TIER)
				{
					_tier[x, z] = TerrainCell.MAX_MACRO_TIER;
				}
			}
		}

		_stageProgress = 1f;
		return true;
	}

	private bool RunPaintMaterials()
	{
		int z = _row;
		int rowOffset = z * _width;
		for (int x = 0; x < _width; x++)
		{
			sbyte tier = _tier[x, z];
			int cellIndex = rowOffset + x;
			_flatTiers[cellIndex] = tier;
			_material[x, z] = MaterialForTier(tier);
			_flatMaterials[cellIndex] = _material[x, z];
		}

		_row++;
		_stageProgress = _row / (float)_depth;
		return _row >= _depth;
	}

	private int MaterialForTier(sbyte tier)
	{
		if (tier < 0)
		{
			return _palette.Water;
		}

		if (tier == 0)
		{
			return _palette.Ground;
		}

		return tier <= 2 ? _palette.Rock : _palette.Cliff;
	}

	private bool RunUploadTerrain()
	{
		bool applied = _editorApi.ApplyGeneratedTerrain(_width, _depth, _flatTiers, _flatMaterials, _editorApi.GetSelectedCliffTextureIndex());
		if (!applied)
		{
			throw new InvalidOperationException("The generated terrain did not match the editor grid.");
		}

		_stageProgress = 1f;
		return true;
	}

	private bool RunFillWater()
	{
		const int PoolsPerStep = 2;
		int cellCount = _width * _depth;

		if (_waterScanIndex < cellCount)
		{
			int discovered = 0;
			while (_waterScanIndex < cellCount && discovered < PoolsPerStep)
			{
				if (!WaterPoolPlanner.TryFindNextPool(_flatTiers, _waterScanned, _width, _depth, WaterPoolPlanner.MinimumPoolCells, ref _waterScanIndex, _waterRegionCells, out WaterPoolSelection pool))
				{
					break;
				}

				_waterPools.Add(pool);
				discovered++;
			}

			_stageProgress = 0.5f * Math.Min(1f, _waterScanIndex / (float)cellCount);
			if (_waterScanIndex < cellCount)
			{
				return false;
			}
		}

		if (_waterPoolIndex < _waterPools.Count)
		{
			WaterPoolSelection pool = _waterPools[_waterPoolIndex];
			_editorApi.FillWaterPoolAtCell(pool.SeedX, pool.SeedZ, pool.SurfaceY);
			_waterPoolIndex++;
			_stageProgress = 0.5f + 0.45f * (_waterPoolIndex / (float)_waterPools.Count);
			if (_waterPoolIndex < _waterPools.Count)
			{
				return false;
			}
		}

		if (_waterPools.Count > 0)
		{
			_editorApi.RebuildTerrain();
		}

		_stageProgress = 1f;
		return true;
	}

	private bool RunDistanceToPaths(int remainingBudgetMilliseconds)
	{
		if (!_distanceInitialized)
		{
			if (_distanceRow == 0)
			{
				for (int z = 0; z < _depth; z++)
				{
					for (int x = 0; x < _width; x++)
					{
						_distance[x, z] = int.MaxValue;
					}
				}
			}

			for (int rowIndex = 0; rowIndex < 32 && _distanceRow < _depth; rowIndex++, _distanceRow++)
			{
				int z = _distanceRow;
				for (int x = 0; x < _width; x++)
				{
					if (!_walkable[x, z])
					{
						continue;
					}

					_distance[x, z] = 0;
					_distanceQueue.Enqueue(z * _width + x);
				}
			}

			_stageProgress = 0.4f * (_distanceRow / (float)_depth);
			if (_distanceRow < _depth)
			{
				return false;
			}

			_distanceInitialized = true;
		}

		int processedNodes = 0;
		int nodeBudget = Math.Max(2048, remainingBudgetMilliseconds * 2048);
		while (_distanceQueue.Count > 0 && processedNodes < nodeBudget)
		{
			int node = _distanceQueue.Dequeue();
			int currentX = node % _width;
			int currentZ = node / _width;
			int nextDistance = _distance[currentX, currentZ] + 1;
			ExpandDistanceNeighbour(currentX - 1, currentZ, nextDistance);
			ExpandDistanceNeighbour(currentX + 1, currentZ, nextDistance);
			ExpandDistanceNeighbour(currentX, currentZ - 1, nextDistance);
			ExpandDistanceNeighbour(currentX, currentZ + 1, nextDistance);
			processedNodes++;
		}

		_stageProgress = _distanceQueue.Count == 0 ? 1f : 0.4f + 0.6f * Math.Min(0.99f, processedNodes / (float)(processedNodes + _distanceQueue.Count));
		return _distanceQueue.Count == 0;
	}

	private void ExpandDistanceNeighbour(int x, int z, int nextDistance)
	{
		if (x < 0 || x >= _width || z < 0 || z >= _depth)
		{
			return;
		}

		if (_distance[x, z] <= nextDistance)
		{
			return;
		}

		_distance[x, z] = nextDistance;
		_distanceQueue.Enqueue(z * _width + x);
	}

	private bool RunScatterTrees(int remainingBudgetMilliseconds)
	{
		if (_treePropIds.Length == 0)
		{
			_stageProgress = 1f;
			return true;
		}

		_treeScatter ??= new PoissonScatter(_width, _depth, _treeScatterRadius, MaximumScatterPoints, unchecked(_seed ^ 0x2545f491));
		bool complete = _treeScatter.Advance(Math.Max(1, remainingBudgetMilliseconds));
		float expectedPoints = Math.Max(1f, _width * _depth / Math.Max(1f, _treeScatterRadius * _treeScatterRadius * 1.7f));
		_stageProgress = complete ? 1f : Math.Min(0.99f, _treeScatter.PointCount / expectedPoints);
		return complete;
	}

	private bool RunPlantTrees()
	{
		if (_treePropIds.Length == 0)
		{
			_stageProgress = 1f;
			return true;
		}

		IReadOnlyList<ScatterPoint> points = _treeScatter?.Points ?? Array.Empty<ScatterPoint>();
		int plantedThisStep = 0;
		while (_treePlantIndex < points.Count && plantedThisStep < 10)
		{
			ScatterPoint point = points[_treePlantIndex];
			_treePlantIndex++;
			plantedThisStep++;

			int cellX = Math.Clamp((int)point.X, 0, _width - 1);
			int cellZ = Math.Clamp((int)point.Z, 0, _depth - 1);
			sbyte tier = _tier[cellX, cellZ];
			if (tier < 0 || tier > 2 || _distance[cellX, cellZ] <= 4)
			{
				continue;
			}

			string treeId = _treePropIds[_random.NextInt(_treePropIds.Length)];
			float worldX = (point.X - _width * 0.5f) * _quadSize;
			float worldZ = (point.Z - _depth * 0.5f) * _quadSize;
			float yaw = _random.NextFloat(0f, 360f);
			float scale = _random.NextFloat(0.8f, 1.2f);
			_editorApi.PlaceProp(treeId, worldX, 0f, worldZ, yaw, scale);
		}

		_stageProgress = points.Count == 0 ? 1f : _treePlantIndex / (float)points.Count;
		return _treePlantIndex >= points.Count;
	}

	private bool RunScatterDecorations(int remainingBudgetMilliseconds)
	{
		if (_decorationPropIds.Length == 0)
		{
			_stageProgress = 1f;
			return true;
		}

		_decorationScatter ??= new PoissonScatter(_width, _depth, _decorationScatterRadius, MaximumScatterPoints, unchecked(_seed ^ 0x6a09e667));
		bool complete = _decorationScatter.Advance(Math.Max(1, remainingBudgetMilliseconds));
		float expectedPoints = Math.Max(1f, _width * _depth / Math.Max(1f, _decorationScatterRadius * _decorationScatterRadius * 1.7f));
		_stageProgress = complete ? 1f : Math.Min(0.99f, _decorationScatter.PointCount / expectedPoints);
		return complete;
	}

	private bool RunPlaceDecorations()
	{
		if (_decorationPropIds.Length == 0)
		{
			_stageProgress = 1f;
			return true;
		}

		IReadOnlyList<ScatterPoint> points = _decorationScatter?.Points ?? Array.Empty<ScatterPoint>();
		int plantedThisStep = 0;
		while (_decorationPlantIndex < points.Count && plantedThisStep < 10)
		{
			ScatterPoint point = points[_decorationPlantIndex];
			_decorationPlantIndex++;
			plantedThisStep++;

			int cellX = Math.Clamp((int)point.X, 0, _width - 1);
			int cellZ = Math.Clamp((int)point.Z, 0, _depth - 1);
			if (_tier[cellX, cellZ] < 0 || _distance[cellX, cellZ] <= 2)
			{
				continue;
			}

			string decorationId = _decorationPropIds[_random.NextInt(_decorationPropIds.Length)];
			float worldX = (point.X - _width * 0.5f) * _quadSize;
			float worldZ = (point.Z - _depth * 0.5f) * _quadSize;
			float yaw = _random.NextFloat(0f, 360f);
			float scale = _random.NextFloat(0.7f, 1.3f);
			_editorApi.PlaceProp(decorationId, worldX, 0f, worldZ, yaw, scale);
		}

		_stageProgress = points.Count == 0 ? 1f : _decorationPlantIndex / (float)points.Count;
		return _decorationPlantIndex >= points.Count;
	}

	private bool RunFinishing()
	{
		_editorApi.ResetWorkspaceFileWatchTimestamps();
		_editorApi.RefreshEditorAfterTerrainChange(true);
		_editorApi.MarkDocumentModified();
		_stageProgress = 1f;
		return true;
	}

	private readonly record struct AnchorCell(int CellX, int CellZ, int ClearRadius);

	private readonly record struct MstEdge(int From, int To);

	private readonly record struct CellPosition(int X, int Z);
}
