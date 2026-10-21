using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using DotRecast.Detour;
using Godot;
using Godot.Collections;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Array = Godot.Collections.Array;

namespace Realm.Client;

[GlobalClass]
public partial class RuntimeTerrain : Node3D
{
	public const uint TerrainCollisionLayer = 2;
	public const uint TerrainVisualLayer = 1 | (1 << 2);
	public const uint TerrainDecalCullMask = 1 << 2;
	public static RuntimeTerrain Instance { get; protected set; }
	public static bool IsMinimapRendering { get; set; } = false;

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
	}

	public RuntimeTerrain()
	{
		Instance = this;
	}

	protected virtual bool IsRuntimeOnly => true;
	public const int TargetTextureResolutionBits = 9;
	public const int TargetTextureResolution = 1 << TargetTextureResolutionBits;

	public static Image ProcessAndResizeImage(Image sourceImg, int targetSize)
	{
		if (sourceImg == null) return null;
		if (sourceImg.GetWidth() == targetSize && sourceImg.GetHeight() == targetSize)
		{
			return sourceImg;
		}

		Image resized = (Image)sourceImg.Duplicate();
		Image.Interpolation interpolationMode = sourceImg.GetWidth() > targetSize
			? Image.Interpolation.Lanczos
			: Image.Interpolation.Cubic;

		resized.Resize(targetSize, targetSize, interpolationMode);
		return resized;
	}

	public const float DefaultQuadSize = TerrainState.DefaultQuadSize;
	public const float DefaultCellSize = TerrainState.DefaultCellSize;

	internal TerrainState GetTerrainStateSafe()
	{
		if (GameHost.Instance != null &&
		    GameContext.World != null &&
		    GameContext.World.IsAlive(ECSContext.WorldEntity) &&
		    GameContext.World.Has<TerrainState>(ECSContext.WorldEntity))
		{
			return GameContext.World.Get<TerrainState>(ECSContext.WorldEntity);
		}

		return new TerrainState(128, 128, DefaultQuadSize, DefaultCellSize, (TerrainCell[,])null, null, null, null);
	}

	public float CellSize
	{
		get => GetTerrainStateSafe().CellSize;
		protected set
		{
			ref var state = ref GameContext.World.Get<TerrainState>(ECSContext.WorldEntity);
			GameContext.World.Set(ECSContext.WorldEntity, new TerrainState(
				state.Width, state.Depth, state.QuadSize, value,
				state.Cells, state.PathingCodes, state.NavMesh, state.NavMeshQuery
			));
		}
	}

	public DtNavMesh NavMesh => GetTerrainStateSafe().NavMesh;
	public DtNavMeshQuery NavMeshQuery => GetTerrainStateSafe().NavMeshQuery;

	public int Width
	{
		get => GetTerrainStateSafe().Width;
		protected set
		{
			ref var state = ref GameContext.World.Get<TerrainState>(ECSContext.WorldEntity);
			GameContext.World.Set(ECSContext.WorldEntity, new TerrainState(
				value, state.Depth, state.QuadSize, state.CellSize,
				state.Cells, state.PathingCodes, state.NavMesh, state.NavMeshQuery
			));
		}
	}

	public int Depth
	{
		get => GetTerrainStateSafe().Depth;
		protected set
		{
			ref var state = ref GameContext.World.Get<TerrainState>(ECSContext.WorldEntity);
			GameContext.World.Set(ECSContext.WorldEntity, new TerrainState(
				state.Width, value, state.QuadSize, state.CellSize,
				state.Cells, state.PathingCodes, state.NavMesh, state.NavMeshQuery
			));
		}
	}

	public float QuadSize
	{
		get => GetTerrainStateSafe().QuadSize;
		protected set
		{
			ref var state = ref GameContext.World.Get<TerrainState>(ECSContext.WorldEntity);
			GameContext.World.Set(ECSContext.WorldEntity, new TerrainState(
				state.Width, state.Depth, value, state.CellSize,
				state.Cells, state.PathingCodes, state.NavMesh, state.NavMeshQuery
			));
		}
	}

	public const float TIER_HEIGHT = TerrainCell.TIER_HEIGHT;
	public const float WATER_DELTA = 0.9f;

	public const int PATHING_SHALLOW_WATER = (int)TerrainPathingFlags.ShallowWater;
	public const int PATHING_DEEP_WATER = (int)TerrainPathingFlags.DeepWater;
	public const int PATHING_FLYING = (int)TerrainPathingFlags.Flying;
	public const int PATHING_GROUND = (int)TerrainPathingFlags.Ground;
	public const int PATHING_BUILDABLE = (int)TerrainPathingFlags.Buildable;

	public static int GetDefaultPathingCode(WaterType waterMode)
	{
		switch (waterMode)
		{
			case WaterType.Shallow:
				return PATHING_SHALLOW_WATER | PATHING_FLYING;
			case WaterType.Deep:
				return PATHING_DEEP_WATER | PATHING_FLYING;
			case WaterType.None:
			default:
				return PATHING_GROUND | PATHING_BUILDABLE | PATHING_FLYING;
		}
	}

	public static int GetDefaultPathingCode(TerrainCell cell)
	{
		if (cell.WaterMode != WaterType.None && Instance != null)
		{
			var prof = Instance.GetWaterProfile(cell.WaterProfileIndex);
			if (prof != null) return prof.DefaultPathingCode;
		}

		return GetDefaultPathingCode(cell.WaterMode);
	}

	public static void ReconcileScaledWater(TerrainCell[,] oldCells, int oldWidth, int oldDepth, TerrainCell[,] newCells, int[,] newPathing, int newWidth, int newDepth)
	{
		if (oldCells == null || newCells == null || oldWidth <= 0 || oldDepth <= 0 || newWidth <= 0 || newDepth <= 0) return;

		var waterBodies = FindOldWaterBodies(oldCells, oldWidth, oldDepth);
		ClearNewWaterData(newCells, newWidth, newDepth);

		if (waterBodies.Count == 0)
		{
			ClearNewWaterPathing(newCells, newPathing, newWidth, newDepth);
			return;
		}

		FloodFillNewWaterBodies(waterBodies, newCells, newPathing, newWidth, newDepth, oldWidth, oldDepth);
		ClearUnusedWaterPathing(newCells, newPathing, newWidth, newDepth);
	}

	private static List<(WaterType WaterMode, byte WaterProfileIndex, float WaterHeight, sbyte MacroTier, List<(int x, int z)> OldCoords)> FindOldWaterBodies(TerrainCell[,] oldCells, int oldWidth, int oldDepth)
	{
		var oldVisited = new bool[oldWidth, oldDepth];
		var waterBodies = new List<(WaterType WaterMode, byte WaterProfileIndex, float WaterHeight, sbyte MacroTier, List<(int x, int z)> OldCoords)>();
		int[] dx = { 0, 0, -1, 1 };
		int[] dz = { -1, 1, 0, 0 };

		for (int oz = 0; oz < oldDepth; oz++)
		{
			for (int ox = 0; ox < oldWidth; ox++)
			{
				if (oldVisited[ox, oz] || oldCells[ox, oz].WaterMode == WaterType.None) continue;
				waterBodies.Add(ExtractWaterBody(ox, oz, oldCells, oldVisited, oldWidth, oldDepth, dx, dz));
			}
		}

		return waterBodies;
	}

	private static (WaterType, byte, float, sbyte, List<(int x, int z)>) ExtractWaterBody(int startX, int startZ, TerrainCell[,] oldCells, bool[,] oldVisited, int oldWidth, int oldDepth, int[] dx, int[] dz)
	{
		var startCell = oldCells[startX, startZ];
		var oldCoords = new List<(int x, int z)>();
		var oldQueue = new Queue<(int x, int z)>();

		oldVisited[startX, startZ] = true;
		oldQueue.Enqueue((startX, startZ));
		oldCoords.Add((startX, startZ));

		while (oldQueue.Count > 0)
		{
			var (cx, cz) = oldQueue.Dequeue();
			ProcessNeighborsForWaterBody(cx, cz, startCell, oldCells, oldVisited, oldWidth, oldDepth, dx, dz, oldQueue, oldCoords);
		}

		return (startCell.WaterMode, startCell.WaterProfileIndex, startCell.WaterHeight, startCell.MacroTier, oldCoords);
	}

	private static void ProcessNeighborsForWaterBody(int cx, int cz, TerrainCell startCell, TerrainCell[,] oldCells, bool[,] oldVisited, int oldWidth, int oldDepth, int[] dx, int[] dz, Queue<(int x, int z)> oldQueue, List<(int x, int z)> oldCoords)
	{
		for (int i = 0; i < 4; i++)
		{
			int nx = cx + dx[i];
			int nz = cz + dz[i];

			if (IsInvalidOrVisitedWaterNeighbor(nx, nz, oldWidth, oldDepth, oldVisited)) continue;

			var nCell = oldCells[nx, nz];
			if (!IsSameWaterBody(nCell, startCell)) continue;

			oldVisited[nx, nz] = true;
			oldQueue.Enqueue((nx, nz));
			oldCoords.Add((nx, nz));
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsInvalidOrVisitedWaterNeighbor(int nx, int nz, int oldWidth, int oldDepth, bool[,] oldVisited)
	{
		return nx < 0 || nx >= oldWidth || nz < 0 || nz >= oldDepth || oldVisited[nx, nz];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsSameWaterBody(TerrainCell nCell, TerrainCell startCell)
	{
		return nCell.WaterMode == startCell.WaterMode &&
		       nCell.WaterProfileIndex == startCell.WaterProfileIndex &&
		       MathF.Abs(nCell.WaterHeight - startCell.WaterHeight) < 0.01f &&
		       nCell.MacroTier == startCell.MacroTier;
	}

	private static void ClearNewWaterData(TerrainCell[,] newCells, int newWidth, int newDepth)
	{
		for (int z = 0; z < newDepth; z++)
		{
			for (int x = 0; x < newWidth; x++)
			{
				newCells[x, z].WaterMode = WaterType.None;
				newCells[x, z].WaterProfileIndex = 0;
				newCells[x, z].WaterHeight = 0f;
			}
		}
	}

	private static void ClearNewWaterPathing(TerrainCell[,] newCells, int[,] newPathing, int newWidth, int newDepth)
	{
		if (newPathing == null) return;
		for (int z = 0; z < newDepth; z++)
		{
			for (int x = 0; x < newWidth; x++)
			{
				if ((newPathing[x, z] & (PATHING_SHALLOW_WATER | PATHING_DEEP_WATER)) != 0)
				{
					newPathing[x, z] = GetDefaultPathingCode(newCells[x, z]);
				}
			}
		}
	}

	private static void ClearUnusedWaterPathing(TerrainCell[,] newCells, int[,] newPathing, int newWidth, int newDepth)
	{
		if (newPathing == null) return;
		for (int z = 0; z < newDepth; z++)
		{
			for (int x = 0; x < newWidth; x++)
			{
				if (newCells[x, z].WaterMode == WaterType.None && (newPathing[x, z] & (PATHING_SHALLOW_WATER | PATHING_DEEP_WATER)) != 0)
				{
					newPathing[x, z] = GetDefaultPathingCode(newCells[x, z]);
				}
			}
		}
	}

	private static void FloodFillNewWaterBodies(List<(WaterType WaterMode, byte WaterProfileIndex, float WaterHeight, sbyte MacroTier, List<(int x, int z)> OldCoords)> waterBodies, TerrainCell[,] newCells, int[,] newPathing, int newWidth, int newDepth, int oldWidth, int oldDepth)
	{
		var overallVisited = new bool[newWidth, newDepth];
		float stepCliffThreshold = TerrainCell.TIER_HEIGHT * 0.70f;
		int[] dx = { 0, 0, -1, 1 };
		int[] dz = { -1, 1, 0, 0 };

		foreach (var body in waterBodies)
		{
			var seedCells = FindSeedCells(body, newCells, newWidth, newDepth, oldWidth, oldDepth);
			FillSingleWaterBody(body, seedCells, newCells, newPathing, overallVisited, newWidth, newDepth, dx, dz, stepCliffThreshold);
		}
	}

	private static HashSet<(int x, int z)> FindSeedCells((WaterType WaterMode, byte WaterProfileIndex, float WaterHeight, sbyte MacroTier, List<(int x, int z)> OldCoords) body, TerrainCell[,] newCells, int newWidth, int newDepth, int oldWidth, int oldDepth)
	{
		var seedCells = new HashSet<(int x, int z)>();
		(int x, int z) closestSeed = (-1, -1);
		float minHeightDiff = float.MaxValue;
		float expectedHeight = body.MacroTier * TerrainCell.TIER_HEIGHT;

		foreach (var (ox, oz) in body.OldCoords)
		{
			int nx = Math.Clamp((int)Math.Round(ox * (float)newWidth / oldWidth), 0, newWidth - 1);
			int nz = Math.Clamp((int)Math.Round(oz * (float)newDepth / oldDepth), 0, newDepth - 1);

			float hDiff = MathF.Abs(newCells[nx, nz].CenterHeight - expectedHeight);
			if (hDiff < minHeightDiff)
			{
				minHeightDiff = hDiff;
				closestSeed = (nx, nz);
			}

			if (newCells[nx, nz].MacroTier == body.MacroTier)
			{
				seedCells.Add((nx, nz));
			}
		}

		if (seedCells.Count == 0 && closestSeed.x >= 0)
		{
			seedCells.Add(closestSeed);
		}

		return seedCells;
	}

	private static void FillSingleWaterBody((WaterType WaterMode, byte WaterProfileIndex, float WaterHeight, sbyte MacroTier, List<(int x, int z)> OldCoords) body, HashSet<(int x, int z)> seedCells, TerrainCell[,] newCells, int[,] newPathing, bool[,] overallVisited, int newWidth, int newDepth, int[] dx, int[] dz, float stepCliffThreshold)
	{
		float effectiveWaterHeight = body.WaterHeight > 0.001f ? body.WaterHeight : 0.9f;
		float startTerrainHeight = body.MacroTier * TerrainCell.TIER_HEIGHT;
		float baseWaterLevel = startTerrainHeight + effectiveWaterHeight;

		var newQueue = new Queue<(int x, int z)>();
		EnqueueSeedCells(seedCells, overallVisited, newQueue);

		while (newQueue.Count > 0)
		{
			var (currX, currZ) = newQueue.Dequeue();
			ApplyWaterToCell(currX, currZ, body, newCells, newPathing);

			var currCell = newCells[currX, currZ];
			SpreadWaterToNeighbors(currX, currZ, currCell, newCells, overallVisited, newWidth, newDepth, dx, dz, stepCliffThreshold, baseWaterLevel, newQueue);
		}
	}

	private static void EnqueueSeedCells(HashSet<(int x, int z)> seedCells, bool[,] overallVisited, Queue<(int x, int z)> newQueue)
	{
		foreach (var seed in seedCells)
		{
			if (overallVisited[seed.x, seed.z]) continue;

			overallVisited[seed.x, seed.z] = true;
			newQueue.Enqueue(seed);
		}
	}

	private static void ApplyWaterToCell(int x, int z, (WaterType WaterMode, byte WaterProfileIndex, float WaterHeight, sbyte MacroTier, List<(int x, int z)> OldCoords) body, TerrainCell[,] newCells, int[,] newPathing)
	{
		newCells[x, z].WaterMode = body.WaterMode;
		newCells[x, z].WaterProfileIndex = body.WaterProfileIndex;
		newCells[x, z].WaterHeight = body.WaterHeight;

		if (newPathing != null)
		{
			newPathing[x, z] = GetDefaultPathingCode(newCells[x, z]);
		}
	}

	private static void SpreadWaterToNeighbors(int currX, int currZ, TerrainCell currCell, TerrainCell[,] newCells, bool[,] overallVisited, int newWidth, int newDepth, int[] dx, int[] dz, float stepCliffThreshold, float baseWaterLevel, Queue<(int x, int z)> newQueue)
	{
		for (int i = 0; i < 4; i++)
		{
			int nextX = currX + dx[i];
			int nextZ = currZ + dz[i];

			if (nextX < 0 || nextX >= newWidth || nextZ < 0 || nextZ >= newDepth || overallVisited[nextX, nextZ]) continue;

			if (ShouldSpreadWater(currCell, newCells[nextX, nextZ], baseWaterLevel, stepCliffThreshold))
			{
				overallVisited[nextX, nextZ] = true;
				newQueue.Enqueue((nextX, nextZ));
			}
		}
	}

	private static bool ShouldSpreadWater(TerrainCell currCell, TerrainCell nextCell, float baseWaterLevel, float stepCliffThreshold)
	{
		float currHeight = currCell.CenterHeight;
		float nextHeight = nextCell.CenterHeight;
		float deltaH = nextHeight - currHeight;

		float nextMaxH = MathF.Max(MathF.Max(nextCell.Y_NW, nextCell.Y_NE), MathF.Max(nextCell.Y_SW, nextCell.Y_SE));
		float nextMinH = MathF.Min(MathF.Min(nextCell.Y_NW, nextCell.Y_NE), MathF.Min(nextCell.Y_SW, nextCell.Y_SE));
		float nextCellInternalSpan = nextMaxH - nextMinH;

		bool isCliffStepUp = (nextCell.MacroTier - currCell.MacroTier >= 1) || deltaH >= stepCliffThreshold || nextCellInternalSpan >= stepCliffThreshold;
		bool isCliffStepDown = (currCell.MacroTier - nextCell.MacroTier >= 1) || (-deltaH) >= stepCliffThreshold;

		if (isCliffStepDown) return false;

		if (isCliffStepUp)
		{
			float obstacleHeight = MathF.Max(nextMaxH, (float)nextCell.MacroTier * TerrainCell.TIER_HEIGHT);
			return baseWaterLevel >= obstacleHeight;
		}

		return true;
	}

	protected TerrainCell[,] _localCells;
	protected int[,] _localPathingCodes;

		private void UpdateTerrainState(TerrainCell[,] newCells, int[,] newPathingCodes)
		{
			if (GameHost.Instance != null &&
			    GameContext.World != null &&
			    GameContext.World.IsAlive(ECSContext.WorldEntity) &&
			    GameContext.World.Has<TerrainState>(ECSContext.WorldEntity))
			{
				ref var state = ref GameContext.World.Get<TerrainState>(ECSContext.WorldEntity);
				
				int targetWidth = state.Width;
				int targetDepth = state.Depth;
				
				if (newCells != null)
				{
					targetWidth = newCells.GetLength(0);
					targetDepth = newCells.GetLength(1);
				}
				else if (newPathingCodes != null)
				{
					targetWidth = newPathingCodes.GetLength(0);
					targetDepth = newPathingCodes.GetLength(1);
				}

				GameContext.World.Set(ECSContext.WorldEntity, new TerrainState(
					targetWidth, targetDepth, state.QuadSize, state.CellSize,
					newCells ?? state.Cells, newPathingCodes ?? state.PathingCodes, state.NavMesh, state.NavMeshQuery
				));
			}
		}

	public TerrainCell[,] Cells
	{
		get
		{
			if (GameHost.Instance != null &&
			    GameContext.World != null &&
			    GameContext.World.IsAlive(ECSContext.WorldEntity) &&
			    GameContext.World.Has<TerrainState>(ECSContext.WorldEntity))
			{
				ref var state = ref GameContext.World.Get<TerrainState>(ECSContext.WorldEntity);
				if (state.Cells == null)
				{
					state.Cells = new TerrainCell[state.Width, state.Depth];
				}

				return state.Cells;
			}

			if (_localCells == null)
			{
				_localCells = new TerrainCell[Width, Depth];
			}

			return _localCells;
		}
		set
		{
			_localCells = value;
				UpdateTerrainState(value, null);
		}
	}

	public sbyte[,] MacroTiers
	{
		get
		{
			var c = Cells;
			if (c == null) return null;
			int w = Width;
			int d = Depth;
			var result = new sbyte[w, d];
			for (int z = 0; z < d; z++)
			for (int x = 0; x < w; x++)
				result[x, z] = c[x, z].MacroTier;
			return result;
		}
	}

	public float[,] Heights
	{
		get => TerrainState.CalculateHeights(Width, Depth, Cells);
		set => SetHeights(value);
	}

	public void SetHeights(float[,] heights)
	{
		if (heights == null) return;
		Cells = TerrainState.CalculateCells(Width, Depth, heights);
	}

	public TerrainSplatWeights[,] SplatMap { get; set; }
	public TerrainSplatWeights[,] CliffSplatMap { get; set; }

	public float CliffJitterStrength { get; set; } = 1.0f;
	public float CliffJitterScale { get; set; } = 0.20f;
	public float CliffRimNoiseStrength { get; set; } = 0.30f;
	public float BlendSoftness { get; set; } = 0.04f;
	public float BlendNoiseStrength { get; set; } = 0.22f;
	public float BlendNoiseScale { get; set; } = 0.22f;

	public int[,] PathingCodes
	{
		get
		{
			if (GameHost.Instance != null &&
			    GameContext.World != null &&
			    GameContext.World.IsAlive(ECSContext.WorldEntity) &&
			    GameContext.World.Has<TerrainState>(ECSContext.WorldEntity))
			{
				ref var state = ref GameContext.World.Get<TerrainState>(ECSContext.WorldEntity);
				if (state.PathingCodes == null)
				{
					state.PathingCodes = new int[state.Width, state.Depth];
					var cells = Cells;
					for (int z = 0; z < state.Depth; z++)
					{
						for (int x = 0; x < state.Width; x++)
						{
							state.PathingCodes[x, z] = GetDefaultPathingCode(cells[x, z]);
						}
					}
				}

				return state.PathingCodes;
			}

			if (_localPathingCodes == null)
			{
				_localPathingCodes = new int[Width, Depth];
				var cells = Cells;
				for (int z = 0; z < Depth; z++)
				{
					for (int x = 0; x < Width; x++)
					{
						_localPathingCodes[x, z] = GetDefaultPathingCode(cells[x, z]);
					}
				}
			}

			return _localPathingCodes;
		}
		set
		{
			_localPathingCodes = value;
				UpdateTerrainState(null, value);
		}
	}

	public const int CHUNK_SIZE = 16;
	protected int _chunkedWidth = 0;
	protected int _chunkedDepth = 0;


	protected List<TerrainChunk> _chunks = new List<TerrainChunk>();
	public IReadOnlyList<TerrainChunk> Chunks => _chunks;
	protected ShaderMaterial _material;
	public ShaderMaterial Material => _material;
	protected ShaderMaterial _shallowWaterMaterial;
	protected ShaderMaterial _deepWaterMaterial;
	protected System.Collections.Generic.Dictionary<byte, WaterProfileSaveData> _waterProfiles = new();
	protected System.Collections.Generic.Dictionary<byte, ShaderMaterial> _waterMaterials = new();
	protected Shader _waterUberShader;

	public System.Collections.Generic.Dictionary<byte, WaterProfileSaveData> WaterProfiles => _waterProfiles;
	public System.Collections.Generic.Dictionary<byte, ShaderMaterial> WaterMaterials => _waterMaterials;

	public IReadOnlyDictionary<byte, WaterProfileSaveData> GetWaterProfiles()
	{
		if (_waterProfiles.Count == 0)
		{
			ReloadWaterProfiles();
		}

		return _waterProfiles;
	}

	public WaterProfileSaveData GetWaterProfile(byte profileIndex)
	{
		if (_waterProfiles.Count == 0)
		{
			ReloadWaterProfiles();
		}

		if (_waterProfiles.TryGetValue(profileIndex, out var prof)) return prof;
		var defaults = WaterProfileSaveData.CreateDefaultProfiles();
		var def = defaults.Find(d => d.ProfileIndex == profileIndex);
		if (def != null)
		{
			_waterProfiles[profileIndex] = def;
			return def;
		}

		return _waterProfiles.Values.FirstOrDefault() ?? new WaterProfileSaveData();
	}

	public void EnsureWaterUberShader()
	{
		if (_waterUberShader == null)
		{
			_waterUberShader = GD.Load<Shader>("res://Assets/shaders/water_uber.gdshader");
			if (_waterUberShader == null)
			{
				string shaderPath = PathUtils.FindPath("Assets/shaders/water_uber.gdshader");
				if (File.Exists(shaderPath))
				{
					_waterUberShader = new Shader { Code = File.ReadAllText(shaderPath) };
				}
			}
		}
	}

	public ShaderMaterial GetWaterMaterial(byte profileIndex)
	{
		EnsureWaterUberShader();
		if (_waterProfiles.Count == 0)
		{
			ReloadWaterProfiles();
		}

		if (_waterMaterials.TryGetValue(profileIndex, out var mat) && mat != null) return mat;
		var newMat = new ShaderMaterial { Shader = _waterUberShader };
		var prof = GetWaterProfile(profileIndex);
		ApplyWaterProfileToMaterial(newMat, prof);
		_waterMaterials[profileIndex] = newMat;
		return newMat;
	}

	public void SetWaterProfiles(List<WaterProfileSaveData> profiles)
	{
		EnsureWaterUberShader();
		_waterProfiles.Clear();
		foreach (var def in WaterProfileSaveData.CreateDefaultProfiles())
		{
			_waterProfiles[def.ProfileIndex] = def;
		}

		foreach (var p in profiles)
		{
			_waterProfiles[p.ProfileIndex] = p;
		}

		foreach (var kvp in _waterProfiles)
		{
			byte idx = kvp.Key;
			var prof = kvp.Value;
			if (!_waterMaterials.TryGetValue(idx, out var mat) || mat == null)
			{
				mat = new ShaderMaterial { Shader = _waterUberShader };
				_waterMaterials[idx] = mat;
			}

			ApplyWaterProfileToMaterial(mat, prof);
		}

		_shallowWaterMaterial = GetWaterMaterial(0);
		_deepWaterMaterial = GetWaterMaterial(1);
		UpdateWaterTransform();
		RegenerateWaterMesh();
	}

	public void ApplyWaterProfileToMaterial(ShaderMaterial mat, WaterProfileSaveData profile)
	{
		if (mat == null || profile == null) return;

		ApplyWaterColors(mat, profile);
		ApplyWaterBaseParameters(mat, profile);
		ApplyWaterTextures(mat, profile);
		ApplyWaterFlowAndCaustics(mat, profile);
		ApplyWaterSubsurface(mat, profile);
		ApplyWaterDetail(mat, profile);
	}

	private void ApplyWaterColors(ShaderMaterial mat, WaterProfileSaveData profile)
	{
		Color shallow = Color.HtmlIsValid(profile.ShallowColorHex) ? Color.FromHtml(profile.ShallowColorHex) : new Color(0.05f, 0.30f, 0.38f, 0.55f);
		Color deep = Color.HtmlIsValid(profile.DeepColorHex) ? Color.FromHtml(profile.DeepColorHex) : new Color(0.01f, 0.06f, 0.14f, 0.98f);
		Color foam = Color.HtmlIsValid(profile.FoamColorHex) ? Color.FromHtml(profile.FoamColorHex) : new Color(0.85f, 0.95f, 1.0f, 0.85f);

		mat.SetShaderParameter("shallow_color", shallow);
		mat.SetShaderParameter("deep_color", deep);
		mat.SetShaderParameter("foam_color", foam);
	}

	private void ApplyWaterBaseParameters(ShaderMaterial mat, WaterProfileSaveData profile)
	{
		mat.SetShaderParameter("max_depth", profile.MaxDepth);
		mat.SetShaderParameter("foam_depth", profile.FoamDepth);
		mat.SetShaderParameter("wave_speed", profile.WaveSpeed);
		mat.SetShaderParameter("wave_strength", profile.WaveStrength);
	}

	private void ApplyWaterTextures(ShaderMaterial mat, WaterProfileSaveData profile)
	{
		mat.SetShaderParameter("use_normal_texture", profile.UseNormalTexture && !string.IsNullOrEmpty(profile.NormalTexturePath));
		if (profile.UseNormalTexture && !string.IsNullOrEmpty(profile.NormalTexturePath))
		{
			var tex = LoadTextureFromActiveWorkspace(profile.NormalTexturePath, preferNormal: true);
			if (tex != null) mat.SetShaderParameter("normal_texture", tex);
		}

		mat.SetShaderParameter("normal_scale", profile.NormalScale);
	}

	private void ApplyWaterFlowAndCaustics(ShaderMaterial mat, WaterProfileSaveData profile)
	{
		mat.SetShaderParameter("flow_direction", new Vector2(profile.FlowDirectionX, profile.FlowDirectionY));
		mat.SetShaderParameter("flow_speed", profile.FlowSpeed);

		mat.SetShaderParameter("use_flow_map", profile.UseFlowMap && !string.IsNullOrEmpty(profile.FlowMapPath));
		if (profile.UseFlowMap && !string.IsNullOrEmpty(profile.FlowMapPath))
		{
			var ftex = LoadTextureFromActiveWorkspace(profile.FlowMapPath);
			if (ftex != null) mat.SetShaderParameter("flow_map_texture", ftex);
		}

		mat.SetShaderParameter("refraction_strength", profile.RefractionStrength);
		mat.SetShaderParameter("caustic_strength", profile.CausticStrength);
		mat.SetShaderParameter("caustic_scale", profile.CausticScale);
		mat.SetShaderParameter("caustic_speed", profile.CausticSpeed);
	}

	private void ApplyWaterSubsurface(ShaderMaterial mat, WaterProfileSaveData profile)
	{
		Color emission = Color.HtmlIsValid(profile.EmissionColorHex) ? Color.FromHtml(profile.EmissionColorHex) : Colors.Black;
		Color core = Color.HtmlIsValid(profile.CoreColorHex) ? Color.FromHtml(profile.CoreColorHex) : Colors.White;
		Color sss = Color.HtmlIsValid(profile.SubsurfaceColorHex) ? Color.FromHtml(profile.SubsurfaceColorHex) : Colors.Black;

		mat.SetShaderParameter("emission_color", emission);
		mat.SetShaderParameter("emission_boost", profile.EmissionBoost);
		mat.SetShaderParameter("core_color", core);
		mat.SetShaderParameter("core_threshold", profile.CoreThreshold);
		mat.SetShaderParameter("subsurface_color", sss);
		mat.SetShaderParameter("subsurface_strength", profile.SubsurfaceStrength);
	}

	private void ApplyWaterDetail(ShaderMaterial mat, WaterProfileSaveData profile)
	{
		mat.SetShaderParameter("use_detail_texture", profile.UseDetailTexture && !string.IsNullOrEmpty(profile.DetailTexturePath));
		if (profile.UseDetailTexture && !string.IsNullOrEmpty(profile.DetailTexturePath))
		{
			var dtex = LoadTextureFromActiveWorkspace(profile.DetailTexturePath, preferNormal: false);
			if (dtex != null) mat.SetShaderParameter("detail_texture", dtex);
		}

		mat.SetShaderParameter("detail_tile_mode", string.Equals(profile.DetailTileMode, "Grid", StringComparison.OrdinalIgnoreCase) ? 0 : 1);
		mat.SetShaderParameter("detail_uv_scale", new Vector2(profile.DetailUvScaleX, profile.DetailUvScaleY));
		mat.SetShaderParameter("detail_uv_scroll", new Vector2(profile.DetailUvScrollX, profile.DetailUvScrollY));
		mat.SetShaderParameter("detail_stochastic_tile_size", profile.DetailStochasticTileSize > 0.001f ? profile.DetailStochasticTileSize : 1.0f);
		mat.SetShaderParameter("detail_cross_fade", Math.Clamp(profile.DetailCrossFade * 0.01f, 0.0f, 0.10f));
		mat.SetShaderParameter("detail_alpha", profile.DetailAlpha);
		mat.SetShaderParameter("detail_blend_mode", profile.DetailBlendMode);
	}

	private Texture2D? LoadTextureFromActiveWorkspace(string texturePath, bool preferNormal = false)
	{
		if (string.IsNullOrWhiteSpace(texturePath)) return null;

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string? fullPath = FindTexturePath(wsPath, texturePath);

		if (fullPath == null || !File.Exists(fullPath)) return null;

		if (fullPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			var (alb, norm) = LoadRtexLayers(fullPath);
			var targetImg = (preferNormal && norm != null) ? norm : alb;
			return targetImg != null ? ImageTexture.CreateFromImage(targetImg) : null;
		}

		var img = Image.LoadFromFile(fullPath);
		return img != null ? ImageTexture.CreateFromImage(img) : null;
	}

	private string? FindTexturePath(string wsPath, string texturePath)
	{
		string fullPath = Path.Combine(wsPath, "Assets", "textures", texturePath);
		if (File.Exists(fullPath)) return fullPath;

		fullPath = Path.Combine(wsPath, "Assets", "noise", texturePath);
		if (File.Exists(fullPath)) return fullPath;

		fullPath = Path.Combine(wsPath, "Assets", "decals", texturePath);
		if (File.Exists(fullPath)) return fullPath;

		fullPath = Path.Combine(wsPath, "Assets", "ribbons", texturePath);
		if (File.Exists(fullPath)) return fullPath;

		fullPath = Path.Combine(wsPath, "Assets", "vfx", texturePath);
		if (File.Exists(fullPath)) return fullPath;

		fullPath = Path.Combine(wsPath, texturePath);
		if (File.Exists(fullPath)) return fullPath;

		fullPath = PathUtils.FindPath($"Assets/textures/{texturePath}");
		if (File.Exists(fullPath)) return fullPath;

		fullPath = PathUtils.FindPath($"Assets/noise/{texturePath}");
		if (File.Exists(fullPath)) return fullPath;

		fullPath = PathUtils.FindPath(texturePath);
		if (File.Exists(fullPath)) return fullPath;

		return null;
	}

	public void ReloadWaterProfiles()
	{
		EnsureWaterUberShader();

		_waterProfiles.Clear();
		foreach (var def in WaterProfileSaveData.CreateDefaultProfiles())
		{
			_waterProfiles[def.ProfileIndex] = def;
		}

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		if (!string.IsNullOrEmpty(wsPath) && GameContext.MetadataService.TryLoadMetadata(wsPath, out var metadataRoot) && metadataRoot?.CustomWaterProfiles != null)
		{
			foreach (var p in metadataRoot.CustomWaterProfiles)
			{
				_waterProfiles[p.ProfileIndex] = p;
			}
		}

		foreach (var kvp in _waterProfiles)
		{
			byte idx = kvp.Key;
			var prof = kvp.Value;
			if (!_waterMaterials.TryGetValue(idx, out var mat) || mat == null)
			{
				mat = new ShaderMaterial { Shader = _waterUberShader };
				_waterMaterials[idx] = mat;
			}

			ApplyWaterProfileToMaterial(mat, prof);
		}

		_shallowWaterMaterial = GetWaterMaterial(0);
		_deepWaterMaterial = GetWaterMaterial(1);
	}

	protected void CreateWater()
	{
		ReloadWaterProfiles();
		UpdateWaterTransform();
	}

	private (float waterY, WaterType waterMode, byte waterProfileIndex) GetCellWaterInfo(int x, int z, TerrainCell[,] cells, int w, int d)
	{
		var cell = cells[x, z];
		if (cell.WaterMode != WaterType.None)
		{
			float delta = cell.WaterHeight > 0.001f ? cell.WaterHeight : WATER_DELTA;
			return ((cell.MacroTier * TerrainCell.TIER_HEIGHT) + delta, cell.WaterMode, cell.WaterProfileIndex);
		}

		float maxCornerH = Math.Max(Math.Max(cell.Y_NW, cell.Y_NE), Math.Max(cell.Y_SE, cell.Y_SW));
		return FindAdjacentWaterInfo(x, z, cell, maxCornerH, cells, w, d);
	}

	private (float waterY, WaterType waterMode, byte waterProfileIndex) FindAdjacentWaterInfo(int x, int z, TerrainCell cell, float maxCornerH, TerrainCell[,] cells, int w, int d)
	{
		float bestWaterY = float.MaxValue;
		WaterType bestWaterMode = WaterType.None;
		byte bestProfileIndex = 0;

		for (int dz = -1; dz <= 1; dz++)
		{
			for (int dx = -1; dx <= 1; dx++)
			{
				EvaluateAdjacentWaterCell(x, z, dx, dz, cell, maxCornerH, cells, w, d, ref bestWaterY, ref bestWaterMode, ref bestProfileIndex);
			}
		}

		return bestWaterMode != WaterType.None ? (bestWaterY, bestWaterMode, bestProfileIndex) : (0f, WaterType.None, (byte)0);
	}

	private void EvaluateAdjacentWaterCell(int x, int z, int dx, int dz, TerrainCell cell, float maxCornerH, TerrainCell[,] cells, int w, int d, ref float bestWaterY, ref WaterType bestWaterMode, ref byte bestProfileIndex)
	{
		if (dx == 0 && dz == 0) return;

		int nx = x + dx;
		int nz = z + dz;
		if (nx < 0 || nx >= w || nz < 0 || nz >= d) return;

		var nCell = cells[nx, nz];
		if (nCell.WaterMode == WaterType.None) return;

		float nWaterY = CalculateNeighborWaterY(nCell);
		float sharedMinH = GetSharedMinHeight(cell, dx, dz);

		if (ShouldSkipWaterEvaluation(sharedMinH, maxCornerH, nWaterY, bestWaterY)) return;

		bestWaterY = nWaterY;
		bestWaterMode = nCell.WaterMode;
		bestProfileIndex = nCell.WaterProfileIndex;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float CalculateNeighborWaterY(TerrainCell nCell)
	{
		float nDelta = nCell.WaterHeight > 0.001f ? nCell.WaterHeight : WATER_DELTA;
		return (nCell.MacroTier * TerrainCell.TIER_HEIGHT) + nDelta;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool ShouldSkipWaterEvaluation(float sharedMinH, float maxCornerH, float nWaterY, float bestWaterY)
	{
		return sharedMinH > nWaterY || maxCornerH <= nWaterY || nWaterY >= bestWaterY;
	}

	private float GetSharedMinHeight(TerrainCell cell, int dx, int dz)
	{
		return (dx, dz) switch
		{
			(-1, 0) => Math.Min(cell.Y_NW, cell.Y_SW),
			(1, 0) => Math.Min(cell.Y_NE, cell.Y_SE),
			(0, -1) => Math.Min(cell.Y_NW, cell.Y_NE),
			(0, 1) => Math.Min(cell.Y_SW, cell.Y_SE),
			(-1, -1) => cell.Y_NW,
			(1, -1) => cell.Y_NE,
			(1, 1) => cell.Y_SE,
			_ => cell.Y_SW,
		};
	}

	public void RegenerateWaterMesh()
	{
		EnsureWaterUberShader();
		UpdateWaterTransform();

		var cells = Cells;
		if (cells == null) return;

		int w = Width;
		int d = Depth;
		float quadSize = QuadSize;
		float halfWQuadSize = (w / 2.0f) * quadSize;
		float halfDQuadSize = (d / 2.0f) * quadSize;

		var profileVerts = new System.Collections.Generic.Dictionary<byte, List<Vector3>>();
		var profileNorms = new System.Collections.Generic.Dictionary<byte, List<Vector3>>();
		var profileUvs = new System.Collections.Generic.Dictionary<byte, List<Vector2>>();
		var profileIndices = new System.Collections.Generic.Dictionary<byte, List<int>>();

		foreach (var chunk in _chunks)
		{
			profileVerts.Clear();
			profileNorms.Clear();
			profileUvs.Clear();
			profileIndices.Clear();

			GenerateWaterGeometryForChunk(chunk, cells, w, d, quadSize, halfWQuadSize, halfDQuadSize, profileVerts, profileNorms, profileUvs, profileIndices);
			UpdateWaterMeshesForChunk(chunk, profileVerts, profileNorms, profileUvs, profileIndices);
		}
	}

	private void GenerateWaterGeometryForChunk(
		TerrainChunk chunk, TerrainCell[,] cells, int w, int d, float quadSize, float halfWQuadSize, float halfDQuadSize, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileVerts, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileNorms, System.Collections.Generic.Dictionary<byte, List<Vector2>> profileUvs, System.Collections.Generic.Dictionary<byte, List<int>> profileIndices)
	{
		for (int z = chunk.StartZ; z < chunk.EndZ; z++)
		{
			for (int x = chunk.StartX; x < chunk.EndX; x++)
			{
				ProcessWaterCell(x, z, cells, w, d, quadSize, halfWQuadSize, halfDQuadSize, profileVerts, profileNorms, profileUvs, profileIndices);
			}
		}
	}

	private void ProcessWaterCell(
		int x, int z, TerrainCell[,] cells, int w, int d, float quadSize, float halfWQuadSize, float halfDQuadSize, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileVerts, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileNorms, System.Collections.Generic.Dictionary<byte, List<Vector2>> profileUvs, System.Collections.Generic.Dictionary<byte, List<int>> profileIndices)
	{
		var (waterY1, activeWaterMode1, activeWaterProfile1) = GetCellWaterInfo(x, z, cells, w, d);
		if (activeWaterMode1 == WaterType.None) return;

		AddWaterQuad(x, z, waterY1, activeWaterProfile1, quadSize, halfWQuadSize, halfDQuadSize, profileVerts, profileNorms, profileUvs, profileIndices);

		if (x + 1 < w)
		{
			AddWaterWallX(x, z, waterY1, activeWaterProfile1, cells, w, d, quadSize, halfWQuadSize, halfDQuadSize, profileVerts, profileNorms, profileUvs, profileIndices);
		}

		if (z + 1 < d)
		{
			AddWaterWallZ(x, z, waterY1, activeWaterProfile1, cells, w, d, quadSize, halfWQuadSize, halfDQuadSize, profileVerts, profileNorms, profileUvs, profileIndices);
		}
	}

	private void AddWaterQuad(
		int x, int z, float waterY1, byte activeWaterProfile1, float quadSize, float halfWQuadSize, float halfDQuadSize, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileVerts, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileNorms, System.Collections.Generic.Dictionary<byte, List<Vector2>> profileUvs, System.Collections.Generic.Dictionary<byte, List<int>> profileIndices)
	{
		Vector3 nw = new Vector3(x * quadSize - halfWQuadSize, waterY1, z * quadSize - halfDQuadSize);
		Vector3 ne = new Vector3((x + 1) * quadSize - halfWQuadSize, waterY1, z * quadSize - halfDQuadSize);
		Vector3 se = new Vector3((x + 1) * quadSize - halfWQuadSize, waterY1, (z + 1) * quadSize - halfDQuadSize);
		Vector3 sw = new Vector3(x * quadSize - halfWQuadSize, waterY1, (z + 1) * quadSize - halfDQuadSize);

		var geom = GetGeomLists(activeWaterProfile1, profileVerts, profileNorms, profileUvs, profileIndices);
		int baseIdx = geom.verts.Count;

		geom.verts.Add(nw);
		geom.verts.Add(ne);
		geom.verts.Add(se);
		geom.verts.Add(sw);
		Vector3 normal = Vector3.Up;
		geom.norms.Add(normal);
		geom.norms.Add(normal);
		geom.norms.Add(normal);
		geom.norms.Add(normal);
		geom.uvs.Add(new Vector2(0, 0));
		geom.uvs.Add(new Vector2(1, 0));
		geom.uvs.Add(new Vector2(1, 1));
		geom.uvs.Add(new Vector2(0, 1));

		geom.indices.Add(baseIdx + 0);
		geom.indices.Add(baseIdx + 1);
		geom.indices.Add(baseIdx + 2);
		geom.indices.Add(baseIdx + 0);
		geom.indices.Add(baseIdx + 2);
		geom.indices.Add(baseIdx + 3);
	}

	private void AddWallGeometry(
		System.Collections.Generic.List<Vector3> verts, System.Collections.Generic.List<Vector3> norms, 
		System.Collections.Generic.List<Vector2> uvs, System.Collections.Generic.List<int> indices,
		int wBase, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 wallNorm)
	{
		verts.Add(v0);
		verts.Add(v1);
		verts.Add(v2);
		verts.Add(v3);

		norms.Add(wallNorm);
		norms.Add(wallNorm);
		norms.Add(wallNorm);
		norms.Add(wallNorm);

		uvs.Add(new Vector2(0, 0));
		uvs.Add(new Vector2(1, 0));
		uvs.Add(new Vector2(1, 1));
		uvs.Add(new Vector2(0, 1));

		indices.Add(wBase + 0);
		indices.Add(wBase + 1);
		indices.Add(wBase + 2);
		indices.Add(wBase + 0);
		indices.Add(wBase + 2);
		indices.Add(wBase + 3);
		indices.Add(wBase + 0);
		indices.Add(wBase + 2);
		indices.Add(wBase + 1);
		indices.Add(wBase + 0);
		indices.Add(wBase + 3);
		indices.Add(wBase + 2);
	}

	private void AddWaterWallX(
		int x, int z, float waterY1, byte activeWaterProfile1, TerrainCell[,] cells, int w, int d, float quadSize, float halfWQuadSize, float halfDQuadSize, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileVerts, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileNorms, System.Collections.Generic.Dictionary<byte, List<Vector2>> profileUvs, System.Collections.Generic.Dictionary<byte, List<int>> profileIndices)
	{
		var (waterY2, activeWaterMode2, activeWaterProfile2) = GetCellWaterInfo(x + 1, z, cells, w, d);
		if (activeWaterMode2 == WaterType.None || MathF.Abs(waterY1 - waterY2) <= 0.01f) return;

		float yMin = MathF.Min(waterY1, waterY2);
		float yMax = MathF.Max(waterY1, waterY2);
		float wallX = (x + 1) * quadSize - halfWQuadSize;
		float z0 = z * quadSize - halfDQuadSize;
		float z1 = (z + 1) * quadSize - halfDQuadSize;

		byte wallProfile = (waterY1 >= waterY2) ? activeWaterProfile1 : activeWaterProfile2;
		var wGeom = GetGeomLists(wallProfile, profileVerts, profileNorms, profileUvs, profileIndices);

		int wBase = wGeom.verts.Count;
		Vector3 wallNorm = (waterY1 > waterY2) ? Vector3.Right : Vector3.Left;
		AddWallGeometry(wGeom.verts, wGeom.norms, wGeom.uvs, wGeom.indices, wBase,
			new Vector3(wallX, yMin, z0), new Vector3(wallX, yMin, z1), new Vector3(wallX, yMax, z1), new Vector3(wallX, yMax, z0), wallNorm);
	}

	private void AddWaterWallZ(
		int x, int z, float waterY1, byte activeWaterProfile1, TerrainCell[,] cells, int w, int d, float quadSize, float halfWQuadSize, float halfDQuadSize, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileVerts, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileNorms, System.Collections.Generic.Dictionary<byte, List<Vector2>> profileUvs, System.Collections.Generic.Dictionary<byte, List<int>> profileIndices)
	{
		var (waterY2, activeWaterMode2, activeWaterProfile2) = GetCellWaterInfo(x, z + 1, cells, w, d);
		if (activeWaterMode2 == WaterType.None || MathF.Abs(waterY1 - waterY2) <= 0.01f) return;

		float yMin = MathF.Min(waterY1, waterY2);
		float yMax = MathF.Max(waterY1, waterY2);
		float wallZ = (z + 1) * quadSize - halfDQuadSize;
		float x0 = x * quadSize - halfWQuadSize;
		float x1 = (x + 1) * quadSize - halfWQuadSize;

		byte wallProfile = (waterY1 >= waterY2) ? activeWaterProfile1 : activeWaterProfile2;
		var wGeom = GetGeomLists(wallProfile, profileVerts, profileNorms, profileUvs, profileIndices);

		int wBase = wGeom.verts.Count;
		Vector3 wallNorm = (waterY1 > waterY2) ? Vector3.Back : Vector3.Forward;
		AddWallGeometry(wGeom.verts, wGeom.norms, wGeom.uvs, wGeom.indices, wBase,
			new Vector3(x0, yMin, wallZ), new Vector3(x1, yMin, wallZ), new Vector3(x1, yMax, wallZ), new Vector3(x0, yMax, wallZ), wallNorm);
	}

	private void UpdateWaterMeshesForChunk(
		TerrainChunk chunk, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileVerts, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileNorms, System.Collections.Generic.Dictionary<byte, List<Vector2>> profileUvs, System.Collections.Generic.Dictionary<byte, List<int>> profileIndices)
	{
		foreach (var (pIdx, gVerts) in profileVerts)
		{
			if (!chunk.ProfileWaterMeshes.TryGetValue(pIdx, out var meshPair))
			{
				var arrMesh = new ArrayMesh();
				var meshInst = new MeshInstance3D();
				meshInst.Name = $"WaterProfileChunk_{pIdx}_{chunk.StartX}_{chunk.StartZ}";
				meshInst.Mesh = arrMesh;
				meshInst.Layers = TerrainVisualLayer;
				meshInst.MaterialOverride = GetWaterMaterial(pIdx);
				AddChild(meshInst);
				meshPair = (meshInst, arrMesh);
				chunk.ProfileWaterMeshes[pIdx] = meshPair;
			}
			else
			{
				meshPair.MeshInstance.MaterialOverride = GetWaterMaterial(pIdx);
			}

			UpdateSingleWaterMesh(meshPair.MeshInstance, meshPair.ArrayMesh, gVerts, profileNorms[pIdx], profileUvs[pIdx], profileIndices[pIdx]);
		}

		foreach (var kvp in chunk.ProfileWaterMeshes)
		{
			if (!profileVerts.ContainsKey(kvp.Key))
			{
				kvp.Value.ArrayMesh.ClearSurfaces();
			}
		}
	}

	private (List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> indices) GetGeomLists(
		byte pIdx, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileVerts, System.Collections.Generic.Dictionary<byte, List<Vector3>> profileNorms, System.Collections.Generic.Dictionary<byte, List<Vector2>> profileUvs, System.Collections.Generic.Dictionary<byte, List<int>> profileIndices)
	{
		if (!profileVerts.TryGetValue(pIdx, out var vList))
		{
			vList = new List<Vector3>();
			profileVerts[pIdx] = vList;
			profileNorms[pIdx] = new List<Vector3>();
			profileUvs[pIdx] = new List<Vector2>();
			profileIndices[pIdx] = new List<int>();
		}

		return (vList, profileNorms[pIdx], profileUvs[pIdx], profileIndices[pIdx]);
	}

	private void UpdateSingleWaterMesh(MeshInstance3D meshInstance, ArrayMesh arrayMesh, List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> indices)
	{
		if (meshInstance == null || arrayMesh == null) return;

		while (arrayMesh.GetSurfaceCount() > 0)
		{
			arrayMesh.SurfaceFindByName("");
			arrayMesh.ClearSurfaces();
		}

		if (verts.Count == 0)
		{
			meshInstance.Visible = false;
			return;
		}

		var surfaceArray = new Array();
		surfaceArray.Resize((int)Mesh.ArrayType.Max);
		surfaceArray[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
		surfaceArray[(int)Mesh.ArrayType.Normal] = norms.ToArray();
		surfaceArray[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
		surfaceArray[(int)Mesh.ArrayType.Index] = indices.ToArray();

		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surfaceArray);
		meshInstance.Visible = true;
	}

	public void UpdateWaterTransform()
	{
		var worldSize = new Vector2(Width * QuadSize, Depth * QuadSize);

		if (_material != null)
		{
			_material.SetShaderParameter("terrain_size", worldSize);
			_material.SetShaderParameter("grid_spacing", QuadSize);
		}
	}

	public void UpdateWaterSize()
	{
		UpdateWaterTransform();
		RegenerateWaterMesh();
	}

	public override void _Ready()
	{
		if (_material == null)
		{
			_terrainUberShader ??= GD.Load<Shader>("res://Assets/shaders/terrain_uber.gdshader");
			_material = new ShaderMaterial();
			_material.Shader = _terrainUberShader;
		}

		ReloadTerrainTextures();
		CreatePolarOverlayMaterial();

		_material.SetShaderParameter("grid_spacing", QuadSize);
		_material.SetShaderParameter("terrain_size", new Vector2(Width * QuadSize, Depth * QuadSize));
		_material.SetShaderParameter("texture_scale", 1.0f / QuadSize);
		_material.SetShaderParameter("cliff_jitter_strength", CliffJitterStrength);
		_material.SetShaderParameter("cliff_jitter_scale", CliffJitterScale);
		_material.SetShaderParameter("blend_softness", BlendSoftness);
		_material.SetShaderParameter("blend_noise_strength", BlendNoiseStrength);
		_material.SetShaderParameter("blend_noise_scale", BlendNoiseScale);

		ApplyQualitySettings(GameSettings.QualityIdx);

		CreateWater();
		CreateChunks();
		UpdateMeshAndPhysics();
	}

	public void ApplyQualitySettings(GraphicsQuality quality)
	{
		ApplyQualitySettings((int)quality);
	}

	public void ApplyQualitySettings(int qualityIdx)
	{
		if (_material == null) return;

		switch (qualityIdx)
		{
			case 0:
			case 1:
				_material.SetShaderParameter("enable_macro_noise", false);
				_material.SetShaderParameter("enable_height_blend", false);
				_material.SetShaderParameter("enable_normal_mapping", false);
				_material.SetShaderParameter("specular_amt", 0.0f);
				break;
			case 2:
			case 3:
			default:
				_material.SetShaderParameter("enable_macro_noise", true);
				_material.SetShaderParameter("enable_height_blend", true);
				_material.SetShaderParameter("enable_normal_mapping", true);
				_material.SetShaderParameter("specular_amt", 0.2f);
				break;
		}

		_material.SetShaderParameter("cliff_jitter_strength", CliffJitterStrength);
		_material.SetShaderParameter("blend_noise_strength", BlendNoiseStrength);
		_material.SetShaderParameter("cliff_jitter_scale", CliffJitterScale);
		_material.SetShaderParameter("blend_noise_scale", BlendNoiseScale);
	}

	protected static Texture2DArray? _cachedAlbedoTextureArray;
	protected static Texture2DArray? _cachedNormalRoughnessTextureArray;
	protected static string? _cachedMapDir;
	protected List<string> _loadedTextureList = new List<string>();
	public IReadOnlyList<string> LoadedTextureList => _loadedTextureList;
	protected Vector4[] _swatchParamsCache = new Vector4[TextureSwatchSlots.MaxSlots];
	protected Vector4[] _swatchHeightParamsCache = new Vector4[TextureSwatchSlots.MaxSlots];
	protected Vector4[] _swatchAlbedoParamsCache = new Vector4[TextureSwatchSlots.MaxSlots];


	protected static readonly System.Collections.Generic.Dictionary<string, SwatchLiveConfig> _liveSwatchOverrides = new(StringComparer.OrdinalIgnoreCase);

	public virtual void UpdateTextureParamDirect(
		string swatchName,
		string tileMode,
		float uvScale,
		float stochasticTileSize,
		float crossFade = 0.0f,
		float? brightness = null,
		string? tintStr = null,
		float heightScale = 1.0f,
		float heightOffset = 0.0f,
		float crevicePower = 1.0f,
		float normalScale = 1.0f,
		float roughnessScale = 1.0f)
	{
		if (_material == null) return;
		string cleanName = Path.GetFileNameWithoutExtension(swatchName);

		UpdateLiveSwatchOverrides(cleanName, tileMode, uvScale, stochasticTileSize, crossFade, brightness, tintStr, heightScale, heightOffset, crevicePower, normalScale, roughnessScale);

		int targetIndex = FindSwatchIndex(cleanName);
		if (targetIndex < 0) return;

		ApplySwatchParams(targetIndex, cleanName, tileMode, uvScale, stochasticTileSize, crossFade, heightScale, heightOffset, crevicePower, normalScale);
		ApplySwatchAlbedo(targetIndex, cleanName, brightness, tintStr, roughnessScale);
		UpdateEcsTerrainSwatchConfig(targetIndex, heightScale, heightOffset, crevicePower, normalScale);
	}

		private (float tm, float uv, float stoch, float cf, float hs, float ho, float cp, float ns) ClampSwatchParams(string tileMode, float uvScale, float stochasticTileSize, float crossFade, float heightScale, float heightOffset, float crevicePower, float normalScale)
	{
		float tm = string.Equals(tileMode, "Grid", StringComparison.OrdinalIgnoreCase) ? 0.0f : 1.0f;
		float uv = Math.Clamp(uvScale, 0.1f, 4.0f);
		float stoch = Math.Clamp(stochasticTileSize, 0.5f, 3.0f);
		float cf = Math.Clamp(crossFade, 0.0f, 10.0f) * 0.01f;

		float hs = Math.Clamp(heightScale, 0.1f, 3.0f);
		float ho = Math.Clamp(heightOffset, -1.0f, 1.0f);
		float cp = Math.Clamp(crevicePower, 0.5f, 4.0f);
		float ns = Math.Clamp(normalScale, 0.0f, 3.0f);
			return (tm, uv, stoch, cf, hs, ho, cp, ns);
		}

		private void UpdateLiveSwatchOverrides(string cleanName, string tileMode, float uvScale, float stochasticTileSize, float crossFade, float? brightness, string? tintStr, float heightScale, float heightOffset, float crevicePower, float normalScale, float roughnessScale)
		{
			var clamped = ClampSwatchParams(tileMode, uvScale, stochasticTileSize, crossFade, heightScale, heightOffset, crevicePower, normalScale);
		float rs = Math.Clamp(roughnessScale, 0.1f, 3.0f);

		Color? parsedTint = (!string.IsNullOrEmpty(tintStr) && Color.HtmlIsValid(tintStr)) ? Color.FromHtml(tintStr) : null;

		_liveSwatchOverrides[cleanName] = new SwatchLiveConfig
		{
				TileMode = clamped.tm,
				UvScale = clamped.uv,
				StochasticTileSize = clamped.stoch,
				CrossFade = clamped.cf,
			Brightness = brightness,
			Tint = parsedTint,
				HeightScale = clamped.hs,
				HeightOffset = clamped.ho,
				CrevicePower = clamped.cp,
				NormalScale = clamped.ns,
			RoughnessScale = rs
		};
	}

	private int FindSwatchIndex(string cleanName)
	{
		for (int i = 0; i < _loadedTextureList.Count && i < TextureSwatchSlots.MaxSlots; i++)
		{
			if (string.Equals(_loadedTextureList[i], cleanName, StringComparison.OrdinalIgnoreCase))
			{
				return i;
			}
		}

		return -1;
	}

	private void ApplySwatchParams(int targetIndex, string cleanName, string tileMode, float uvScale, float stochasticTileSize, float crossFade, float heightScale, float heightOffset, float crevicePower, float normalScale)
	{
		var clamped = ClampSwatchParams(tileMode, uvScale, stochasticTileSize, crossFade, heightScale, heightOffset, crevicePower, normalScale);

		_swatchParamsCache[targetIndex] = new Vector4(clamped.tm, clamped.uv, clamped.stoch, clamped.cf);
		_swatchHeightParamsCache[targetIndex] = new Vector4(clamped.hs, clamped.ho, clamped.cp, clamped.ns);

		_material.SetShaderParameter("swatch_params", _swatchParamsCache);
		_material.SetShaderParameter("swatch_height_params", _swatchHeightParamsCache);
	}

	private void ApplySwatchAlbedo(int targetIndex, string cleanName, float? brightness, string? tintStr, float roughnessScale)
	{
		float rs = Math.Clamp(roughnessScale, 0.1f, 3.0f);
		Color? parsedTint = (!string.IsNullOrEmpty(tintStr) && Color.HtmlIsValid(tintStr)) ? Color.FromHtml(tintStr) : null;

		float currentScaleFactor = GetScaleFactor(cleanName);
		float currentBrightness = GetResolvedBrightness(cleanName, brightness);
		Color currentTint = GetResolvedTint(cleanName, parsedTint);

		if (currentScaleFactor <= 0.0001f) currentScaleFactor = 1.0f;
		if (currentBrightness <= 0.0001f) currentBrightness = 1.0f;

		float effectiveMultiplier = currentScaleFactor * currentBrightness;
		if (effectiveMultiplier <= 0.0001f) effectiveMultiplier = 1.0f;

		_swatchAlbedoParamsCache[targetIndex] = new Vector4(currentTint.R * effectiveMultiplier, currentTint.G * effectiveMultiplier, currentTint.B * effectiveMultiplier, rs);
		_material.SetShaderParameter("swatch_albedo_params", _swatchAlbedoParamsCache);
	}

	private float GetResolvedBrightness(string cleanName, float? brightness)
	{
		if (brightness.HasValue) return brightness.Value;
		if (_liveSwatchOverrides.TryGetValue(cleanName, out var over) && over.Brightness.HasValue) return over.Brightness.Value;
		return 1.0f;
	}

	private Color GetResolvedTint(string cleanName, Color? parsedTint)
	{
		if (parsedTint.HasValue) return parsedTint.Value;
		if (_liveSwatchOverrides.TryGetValue(cleanName, out var over) && over.Tint.HasValue) return over.Tint.Value;
		return Colors.White;
	}

	private float GetScaleFactor(string cleanName)
	{
		string mapDir = MapWorkspaceService.GetActiveWorkspacePath();
		try
		{
			var metadata = MapFileService.LoadMetadata(mapDir);
			if (metadata?.Textures != null && metadata.Textures.TryGetValue(cleanName + ".rtex", out var texMeta) && texMeta != null && texMeta.ScaleFactor > 0.0001f)
			{
				return Math.Clamp(texMeta.ScaleFactor, 0.10f, 4.0f);
			}
		}
		catch
		{
		}

		return ResolveRtexScaleFactor(cleanName, mapDir);
	}

	private float ResolveRtexScaleFactor(string cleanName, string mapDir)
	{
		string rtexPath = Path.Combine(mapDir, "Assets", "textures", cleanName + ".rtex");
		if (!File.Exists(rtexPath)) rtexPath = Path.Combine(mapDir, cleanName + ".rtex");
		if (!File.Exists(rtexPath)) rtexPath = PathUtils.FindPath($"Assets/textures/{cleanName}.rtex");
		if (!File.Exists(rtexPath)) rtexPath = PathUtils.FindPath($"MapTemplate/Assets/textures/{cleanName}.rtex");

		float rtexSf = ExtractRtexScaleFactor(rtexPath);
		if (rtexSf > 0.0001f && MathF.Abs(rtexSf - 1.0f) > 0.001f) return rtexSf;

		if (File.Exists(rtexPath))
		{
			float calc = TextureConverter.CalculateLuminanceScaleFactor(rtexPath);
			return calc > 0.0001f ? Math.Clamp(calc, 0.10f, 4.0f) : 1.0f;
		}

		return 1.0f;
	}

	private void UpdateEcsTerrainSwatchConfig(int targetIndex, float heightScale, float heightOffset, float crevicePower, float normalScale)
	{
		if (GameHost.Instance != null && GameContext.World != null && GameContext.World.IsAlive(ECSContext.WorldEntity) && GameContext.World.Has<TerrainState>(ECSContext.WorldEntity))
		{
			ref var ts = ref GameContext.World.Get<TerrainState>(ECSContext.WorldEntity);
			if (ts.SwatchConfigs == null || ts.SwatchConfigs.Length != TextureSwatchSlots.MaxSlots)
			{
				ts.SwatchConfigs = new TerrainSwatchConfig[TextureSwatchSlots.MaxSlots];
			}

			ts.SwatchConfigs[targetIndex] = new TerrainSwatchConfig(
				Math.Clamp(heightScale, 0.1f, 3.0f),
				Math.Clamp(heightOffset, -1.0f, 1.0f),
				Math.Clamp(crevicePower, 0.5f, 4.0f),
				Math.Clamp(normalScale, 0.0f, 3.0f)
			);
		}
	}


	public virtual ActiveSwatchConfig GetActiveSwatchConfig(string swatchName)
	{
		string cleanName = Path.GetFileNameWithoutExtension(swatchName);
		int targetIndex = FindSwatchIndex(cleanName);

		var config = CreateDefaultSwatchConfig();
		ApplyCachedParamsToConfig(config, targetIndex);
		ApplyLiveOverridesToConfig(config, cleanName);

		return config;
	}

	private ActiveSwatchConfig CreateDefaultSwatchConfig()
	{
		return new ActiveSwatchConfig
		{
			TileMode = "Stochastic",
			UvScale = 1.0f,
			StochasticTileSize = 1.0f,
			CrossFade = 0.0f,
			HeightScale = 1.0f,
			HeightOffset = 0.0f,
			CrevicePower = 1.0f,
			NormalScale = 1.0f,
			RoughnessScale = 1.0f,
			Brightness = 1.0f,
			Tint = Colors.White
		};
	}

	private void ApplyCachedParamsToConfig(ActiveSwatchConfig config, int targetIndex)
	{
		if (targetIndex < 0 || _swatchParamsCache == null || targetIndex >= _swatchParamsCache.Length) return;

		ApplyCachedBaseParamsToConfig(config, targetIndex);
		ApplyCachedHeightParamsToConfig(config, targetIndex);
		ApplyCachedAlbedoParamsToConfig(config, targetIndex);
	}

	private void ApplyCachedBaseParamsToConfig(ActiveSwatchConfig config, int targetIndex)
	{
		var p = _swatchParamsCache[targetIndex];
		config.TileMode = p.X < 0.5f ? "Grid" : "Stochastic";
		config.UvScale = p.Y > 0.001f ? p.Y : 1.0f;
		config.StochasticTileSize = p.Z > 0.001f ? p.Z : 1.0f;
		config.CrossFade = p.W * 100.0f;
	}

	private void ApplyCachedHeightParamsToConfig(ActiveSwatchConfig config, int targetIndex)
	{
		var hp = _swatchHeightParamsCache[targetIndex];
		config.HeightScale = hp.X > 0.001f ? hp.X : 1.0f;
		config.HeightOffset = hp.Y;
		config.CrevicePower = hp.Z > 0.001f ? hp.Z : 1.0f;
		config.NormalScale = hp.W > 0.0001f ? hp.W : 1.0f;
	}

	private void ApplyCachedAlbedoParamsToConfig(ActiveSwatchConfig config, int targetIndex)
	{
		if (_swatchAlbedoParamsCache == null || targetIndex >= _swatchAlbedoParamsCache.Length) return;
		config.RoughnessScale = _swatchAlbedoParamsCache[targetIndex].W > 0.0001f ? _swatchAlbedoParamsCache[targetIndex].W : 1.0f;
	}

	private void ApplyLiveOverridesToConfig(ActiveSwatchConfig config, string cleanName)
	{
		if (!_liveSwatchOverrides.TryGetValue(cleanName, out var liveOver)) return;
		ApplyLiveOverridesCore(config, liveOver);
	}

	private static void ApplyLiveOverridesCore(ActiveSwatchConfig config, SwatchLiveConfig liveOver)
	{
		ApplyLiveOverridesTexture(config, liveOver);
		ApplyLiveOverridesMaterial(config, liveOver);
	}

	private static void ApplyLiveOverridesTexture(ActiveSwatchConfig config, SwatchLiveConfig liveOver)
	{
		if (liveOver.TileMode.HasValue)
		{
			if (liveOver.TileMode.Value < 0.5f) config.TileMode = "Grid";
			else config.TileMode = "Stochastic";
		}

		if (liveOver.UvScale.HasValue) config.UvScale = liveOver.UvScale.Value;
		if (liveOver.StochasticTileSize.HasValue) config.StochasticTileSize = liveOver.StochasticTileSize.Value;
		if (liveOver.CrossFade.HasValue) config.CrossFade = liveOver.CrossFade.Value * 100.0f;
		if (liveOver.HeightScale.HasValue) config.HeightScale = liveOver.HeightScale.Value;
		if (liveOver.HeightOffset.HasValue) config.HeightOffset = liveOver.HeightOffset.Value;
	}

	private static void ApplyLiveOverridesMaterial(ActiveSwatchConfig config, SwatchLiveConfig liveOver)
	{
		if (liveOver.CrevicePower.HasValue) config.CrevicePower = liveOver.CrevicePower.Value;
		if (liveOver.NormalScale.HasValue) config.NormalScale = liveOver.NormalScale.Value;
		if (liveOver.RoughnessScale.HasValue) config.RoughnessScale = liveOver.RoughnessScale.Value;
		if (liveOver.Brightness.HasValue) config.Brightness = liveOver.Brightness.Value;
		if (liveOver.Tint.HasValue) config.Tint = liveOver.Tint.Value;
	}

	public virtual void ClearLiveSwatchOverrides(string? swatchName = null)
	{
		if (string.IsNullOrEmpty(swatchName))
		{
			_liveSwatchOverrides.Clear();
		}
		else
		{
			string cleanName = Path.GetFileNameWithoutExtension(swatchName);
			_liveSwatchOverrides.Remove(cleanName);
		}
	}

	private static float ExtractRtexScaleFactor(string rtexPath)
	{
		if (string.IsNullOrEmpty(rtexPath) || !File.Exists(rtexPath)) return 1.0f;

		try
		{
			string? rtexMeta = RealmMetadataHelper.ExtractMetadata(rtexPath);
			if (string.IsNullOrEmpty(rtexMeta)) return 1.0f;

			var rNode = JsonNode.Parse(rtexMeta);
			if (rNode is JsonObject rObj)
			{
				return ParseScaleFactorFromJsonObject(rObj);
			}
		}
		catch
		{
		}

		return 1.0f;
	}

	private static float ParseScaleFactorFromJsonObject(JsonObject rObj)
	{
		if (rObj.TryGetPropertyValue("scale_factor", out var sfVal) ||
		    rObj.TryGetPropertyValue("Scale_Factor", out sfVal) ||
		    rObj.TryGetPropertyValue("scaleFactor", out sfVal) ||
		    rObj.TryGetPropertyValue("ScaleFactor", out sfVal))
		{
			if (float.TryParse(sfVal?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedScale))
			{
				return Math.Clamp(parsedScale, 0.10f, 4.0f);
			}
		}

		return 1.0f;
	}

	private (Image? AlbedoHeight, Image? NormalRoughness) LoadRtexLayers(string rtexPath)
	{
		string globalRtexPath = ProjectSettings.GlobalizePath(rtexPath);
		if (!File.Exists(globalRtexPath)) return (null, null);

		try
		{
			byte[] bytes = File.ReadAllBytes(globalRtexPath);
			var (_, layers, _) = RtexFile.Parse(bytes);
			if (layers.Count == 0) return (null, null);

			Image? img0 = null;
			if (layers.Count > 0 && layers[0].Length > 0)
			{
				img0 = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
				if (img0.LoadWebpFromBuffer(layers[0]) != Error.Ok)
				{
					img0.LoadPngFromBuffer(layers[0]);
				}
			}

			Image? img1 = null;
			if (layers.Count > 1 && layers[1].Length > 0)
			{
				img1 = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
				if (img1.LoadWebpFromBuffer(layers[1]) != Error.Ok)
				{
					img1.LoadPngFromBuffer(layers[1]);
				}
			}

			return (img0, img1);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to load RTEX layers from '{globalRtexPath}': {ex.Message}");
			return (null, null);
		}
	}

	public void ReloadTerrainTextures(bool forceReload = false)
	{
		if (_material == null) return;
		if (forceReload)
		{
			_liveSwatchOverrides.Clear();
			_cachedAlbedoTextureArray = null;
			_cachedNormalRoughnessTextureArray = null;
			_cachedMapDir = null;
		}

		string mapDir = MapWorkspaceService.GetActiveWorkspacePath();
		var swatchSlots = TextureSwatchSlots.ResolveSlots(null, mapDir);

		var swatchParams = new Vector4[TextureSwatchSlots.MaxSlots];
		var swatchHeightParams = new Vector4[TextureSwatchSlots.MaxSlots];
		var swatchAlbedoParams = new Vector4[TextureSwatchSlots.MaxSlots];
		var textureList = new List<string>();

		for (int i = 0; i < TextureSwatchSlots.MaxSlots; i++)
		{
			ProcessSwatchSlot(i, swatchSlots[i], mapDir, textureList, swatchParams, swatchHeightParams, swatchAlbedoParams);
		}

		UpdateMaterialAndEcs(textureList, swatchParams, swatchHeightParams, swatchAlbedoParams);

		if (!forceReload && _cachedAlbedoTextureArray != null && _cachedNormalRoughnessTextureArray != null && _cachedMapDir == mapDir)
		{
			RestoreCachedTextureArrays();
			return;
		}

		RegenerateTextureArrays(swatchSlots, mapDir);
	}

	private void ProcessSwatchSlot(int index, SwatchSlotInfo slot, string mapDir, List<string> textureList, Vector4[] swatchParams, Vector4[] swatchHeightParams, Vector4[] swatchAlbedoParams)
	{
		textureList.Add(slot.BaseName ?? "");

		if (slot.IsFiller || string.IsNullOrEmpty(slot.BaseName))
		{
			SetDefaultSwatchParams(index, swatchParams, swatchHeightParams, swatchAlbedoParams);
			return;
		}

		var config = ExtractSwatchConfigFromSlot(slot, mapDir);
		ApplyLiveOverridesToSwatchConfig(slot.BaseName, config);

		float effectiveMultiplier = Math.Max(config.ScaleFactor * config.Brightness, 0.0001f);

		swatchParams[index] = new Vector4(config.TileMode, config.UvScale, config.StochasticTileSize, config.CrossFade);
		swatchHeightParams[index] = new Vector4(config.HeightScale, config.HeightOffset, config.CrevicePower, config.NormalScale);
		swatchAlbedoParams[index] = new Vector4(config.Tint.R * effectiveMultiplier, config.Tint.G * effectiveMultiplier, config.Tint.B * effectiveMultiplier, config.RoughnessScale);
	}


	private ParsedSwatchConfig ExtractSwatchConfigFromSlot(SwatchSlotInfo slot, string mapDir)
	{
		var c = new ParsedSwatchConfig();
		if (slot.MetadataNode != null)
		{
			ApplyMetadataToSwatchConfig(c, slot.MetadataNode);
		}

		if (c.ScaleFactor <= 0.0001f || MathF.Abs(c.ScaleFactor - 1.0f) < 0.0001f)
		{
			c.ScaleFactor = ResolveRtexScaleFactor(slot.BaseName, mapDir);
		}

		return c;
	}

	private void ApplyMetadataToSwatchConfig(ParsedSwatchConfig c, TextureMetadata sObj)
	{
		ApplyMetadataTileMode(c, sObj);
		ApplyMetadataScaleAndFade(c, sObj);
		ApplyMetadataVisuals(c, sObj);
		ApplyMetadataHeight(c, sObj);
	}

	private void ApplyMetadataTileMode(ParsedSwatchConfig c, TextureMetadata sObj)
	{
		if (!string.IsNullOrEmpty(sObj.TileMode)) c.TileMode = string.Equals(sObj.TileMode, "Grid", StringComparison.OrdinalIgnoreCase) ? 0.0f : 1.0f;
		if (sObj.UvScale > 0.0001f) c.UvScale = Math.Clamp(sObj.UvScale, 0.1f, 4.0f);
		if (sObj.StochasticTileSize > 0.0001f) c.StochasticTileSize = Math.Clamp(sObj.StochasticTileSize, 0.5f, 3.0f);
	}

	private void ApplyMetadataScaleAndFade(ParsedSwatchConfig c, TextureMetadata sObj)
	{
		if (sObj.CrossFade >= 0.0f) c.CrossFade = Math.Clamp(sObj.CrossFade, 0.0f, 10.0f) * 0.01f;
		if (sObj.ScaleFactor > 0.0001f) c.ScaleFactor = Math.Clamp(sObj.ScaleFactor, 0.10f, 4.0f);
	}

	private void ApplyMetadataVisuals(ParsedSwatchConfig c, TextureMetadata sObj)
	{
		if (sObj.Brightness > 0f) c.Brightness = Math.Clamp(sObj.Brightness, 0.1f, 5.0f);
		if (sObj.NormalScale >= 0f) c.NormalScale = Math.Clamp(sObj.NormalScale, 0.0f, 3.0f);
		if (sObj.RoughnessScale > 0f) c.RoughnessScale = Math.Clamp(sObj.RoughnessScale, 0.10f, 3.0f);
		if (!string.IsNullOrEmpty(sObj.Tint) && sObj.Tint.StartsWith("#")) c.Tint = Color.FromHtml(sObj.Tint);
	}

	private void ApplyMetadataHeight(ParsedSwatchConfig c, TextureMetadata sObj)
	{
		if (sObj.HeightScale > 0f) c.HeightScale = Math.Clamp(sObj.HeightScale, 0.1f, 3.0f);
		if (sObj.HeightOffset != 0f) c.HeightOffset = Math.Clamp(sObj.HeightOffset, -1.0f, 1.0f);
		if (sObj.CrevicePower > 0f) c.CrevicePower = Math.Clamp(sObj.CrevicePower, 0.5f, 4.0f);
	}

	private void ApplyLiveOverridesToSwatchConfig(string name, ParsedSwatchConfig c)
	{
		if (_liveSwatchOverrides.TryGetValue(name, out var liveOver))
		{
			ApplyLiveOverridesTileMode(liveOver, c);
			ApplyLiveOverridesVisuals(liveOver, c);
			ApplyLiveOverridesHeight(liveOver, c);
		}

		if (c.ScaleFactor <= 0.0001f) c.ScaleFactor = 1.0f;
		if (c.Brightness <= 0.0001f) c.Brightness = 1.0f;
	}

	private void ApplyLiveOverridesTileMode(SwatchLiveConfig liveOver, ParsedSwatchConfig c)
	{
		if (liveOver.TileMode.HasValue) c.TileMode = liveOver.TileMode.Value;
		if (liveOver.UvScale.HasValue) c.UvScale = liveOver.UvScale.Value;
		if (liveOver.StochasticTileSize.HasValue) c.StochasticTileSize = liveOver.StochasticTileSize.Value;
	}

	private void ApplyLiveOverridesVisuals(SwatchLiveConfig liveOver, ParsedSwatchConfig c)
	{
		if (liveOver.CrossFade.HasValue) c.CrossFade = liveOver.CrossFade.Value;
		if (liveOver.NormalScale.HasValue) c.NormalScale = liveOver.NormalScale.Value;
		if (liveOver.RoughnessScale.HasValue) c.RoughnessScale = liveOver.RoughnessScale.Value;
		if (liveOver.Brightness.HasValue) c.Brightness = liveOver.Brightness.Value;
		if (liveOver.Tint.HasValue) c.Tint = liveOver.Tint.Value;
	}

	private void ApplyLiveOverridesHeight(SwatchLiveConfig liveOver, ParsedSwatchConfig c)
	{
		if (liveOver.HeightScale.HasValue) c.HeightScale = liveOver.HeightScale.Value;
		if (liveOver.HeightOffset.HasValue) c.HeightOffset = liveOver.HeightOffset.Value;
		if (liveOver.CrevicePower.HasValue) c.CrevicePower = liveOver.CrevicePower.Value;
	}

	private void SetDefaultSwatchParams(int index, Vector4[] swatchParams, Vector4[] swatchHeightParams, Vector4[] swatchAlbedoParams)
	{
		swatchParams[index] = new Vector4(1.0f, 1.0f, 1.0f, 0.05f);
		swatchHeightParams[index] = new Vector4(1.0f, 0.0f, 1.0f, 0.0f);
		swatchAlbedoParams[index] = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
	}

	private void UpdateMaterialAndEcs(List<string> textureList, Vector4[] swatchParams, Vector4[] swatchHeightParams, Vector4[] swatchAlbedoParams)
	{
		_loadedTextureList = textureList;
		_swatchParamsCache = swatchParams;
		_swatchHeightParamsCache = swatchHeightParams;
		_swatchAlbedoParamsCache = swatchAlbedoParams;

		_material.SetShaderParameter("swatch_params", swatchParams);
		_material.SetShaderParameter("swatch_height_params", swatchHeightParams);
		_material.SetShaderParameter("swatch_albedo_params", swatchAlbedoParams);

		if (GameHost.Instance != null && GameContext.World != null && GameContext.World.IsAlive(ECSContext.WorldEntity) && GameContext.World.Has<TerrainState>(ECSContext.WorldEntity))
		{
			ref var ts = ref GameContext.World.Get<TerrainState>(ECSContext.WorldEntity);
			ts.SwatchConfigs = new TerrainSwatchConfig[TextureSwatchSlots.MaxSlots];
			for (int i = 0; i < TextureSwatchSlots.MaxSlots; i++)
			{
				ts.SwatchConfigs[i] = new TerrainSwatchConfig(swatchHeightParams[i].X, swatchHeightParams[i].Y, swatchHeightParams[i].Z, swatchHeightParams[i].W);
			}
		}
	}

	private void RestoreCachedTextureArrays()
	{
		_material.SetShaderParameter("terrain_textures", _cachedAlbedoTextureArray);
		_material.SetShaderParameter("terrain_normals_pbr", _cachedNormalRoughnessTextureArray);
		_material.SetShaderParameter("cliff_textures", _cachedAlbedoTextureArray);
		_material.SetShaderParameter("cliff_normals_pbr", _cachedNormalRoughnessTextureArray);
	}

	private void RegenerateTextureArrays(SwatchSlotInfo[] swatchSlots, string mapDir)
	{
		var albedoHeightImages = new Array<Image>();
		var normalRoughnessImages = new Array<Image>();

		for (int i = 0; i < TextureSwatchSlots.MaxSlots; i++)
		{
			ProcessAndAddSwatchImages(swatchSlots[i], mapDir, albedoHeightImages, normalRoughnessImages);
		}

		var albedoTextureArray = new Texture2DArray();
		albedoTextureArray.CreateFromImages(albedoHeightImages);
		var normalTextureArray = new Texture2DArray();
		normalTextureArray.CreateFromImages(normalRoughnessImages);

		_cachedAlbedoTextureArray = albedoTextureArray;
		_cachedNormalRoughnessTextureArray = normalTextureArray;
		_cachedMapDir = mapDir;

		_material.SetShaderParameter("terrain_textures", albedoTextureArray);
		_material.SetShaderParameter("terrain_normals_pbr", normalTextureArray);
		_material.SetShaderParameter("cliff_textures", albedoTextureArray);
		_material.SetShaderParameter("cliff_normals_pbr", normalTextureArray);
	}

	private void ProcessAndAddSwatchImages(SwatchSlotInfo slot, string mapDir, Array<Image> albedoHeightImages, Array<Image> normalRoughnessImages)
	{
		Image? imgLayer0 = null;
		Image? imgLayer1 = null;

		if (!slot.IsFiller && !string.IsNullOrEmpty(slot.BaseName))
		{
			LoadImagesForSwatch(slot, mapDir, ref imgLayer0, ref imgLayer1);
		}

		if (imgLayer0 == null || imgLayer1 == null)
		{
			imgLayer0 = Image.CreateEmpty(TargetTextureResolution, TargetTextureResolution, false, Image.Format.Rgba8);
			imgLayer0.Fill(new Color(0.5f, 0.5f, 0.5f, 1.0f));
			imgLayer1 = Image.CreateEmpty(TargetTextureResolution, TargetTextureResolution, false, Image.Format.Rgba8);
			imgLayer1.Fill(new Color(0.5f, 0.5f, 1.0f, 0.8f));
		}

		FormatAndAddImage(imgLayer0, albedoHeightImages);
		FormatAndAddImage(imgLayer1, normalRoughnessImages);
	}

	private void LoadImagesForSwatch(SwatchSlotInfo slot, string mapDir, ref Image? imgLayer0, ref Image? imgLayer1)
	{
		string name = slot.BaseName;
		string rtexFileName = slot.MetadataNode?.TexturePath ?? slot.FileName ?? (name + ".rtex");
		if (!rtexFileName.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase)) rtexFileName += ".rtex";
		rtexFileName = Path.GetFileName(rtexFileName);

		string rtexPath = ResolveRtexPath(rtexFileName, name, mapDir);

		if (!File.Exists(rtexPath)) return;

		try
		{
			var layers = LoadRtexLayers(rtexPath);
			imgLayer0 = layers.AlbedoHeight;
			imgLayer1 = layers.NormalRoughness;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to load dynamic RTEX layers for {name}: {ex.Message}");
		}
	}

	private string ResolveRtexPath(string rtexFileName, string baseName, string mapDir)
	{
		string[] pathsToTry = new string[]
		{
			Path.Combine(mapDir, "Assets", "textures", rtexFileName),
			Path.Combine(mapDir, rtexFileName),
			PathUtils.FindPath($"Assets/textures/{rtexFileName}"),
			PathUtils.FindPath($"MapTemplate/Assets/textures/{rtexFileName}"),
			ProjectSettings.GlobalizePath($"res://Assets/2d/TileSheets/{rtexFileName}")
		};

		foreach (var path in pathsToTry)
		{
			if (File.Exists(path)) return path;
		}

		string fallbackRtexPath = ProjectSettings.GlobalizePath($"res://Assets/2d/TileSheets/{rtexFileName}");
		string pngPath = ProjectSettings.GlobalizePath($"res://Assets/2d/TileSheets/{baseName}.png");
		if (File.Exists(pngPath))
		{
			ProcessAndSaveRawTexture(pngPath, fallbackRtexPath);
		}

		return fallbackRtexPath;
	}

	private void FormatAndAddImage(Image img, Array<Image> targetArray)
	{
		if (img.GetWidth() != TargetTextureResolution || img.GetHeight() != TargetTextureResolution)
		{
			img.Resize(TargetTextureResolution, TargetTextureResolution, Image.Interpolation.Bilinear);
		}

		if (img.GetFormat() != Image.Format.Rgba8)
		{
			img.Convert(Image.Format.Rgba8);
		}

		img.GenerateMipmaps();
		targetArray.Add(img);
	}

	protected void CreateChunks()
	{
		foreach (var chunk in _chunks)
		{
			if (GodotObject.IsInstanceValid(chunk.MeshInstance)) chunk.MeshInstance.QueueFree();
			if (chunk.ProfileWaterMeshes != null)
			{
				foreach (var (pMesh, _) in chunk.ProfileWaterMeshes.Values)
				{
					if (GodotObject.IsInstanceValid(pMesh)) pMesh.QueueFree();
				}

				chunk.ProfileWaterMeshes.Clear();
			}
		}

		_chunks.Clear();

		int w = Width;
		int d = Depth;
		_chunkedWidth = w;
		_chunkedDepth = d;

		for (int z = 0; z < d; z += CHUNK_SIZE)
		{
			for (int x = 0; x < w; x += CHUNK_SIZE)
			{
				int ex = Math.Min(x + CHUNK_SIZE, w);
				int ez = Math.Min(z + CHUNK_SIZE, d);

				var chunk = new TerrainChunk
				{
					StartX = x,
					StartZ = z,
					EndX = ex,
					EndZ = ez,
					ArrayMesh = new ArrayMesh()
				};

				chunk.MeshInstance = new MeshInstance3D();
				chunk.MeshInstance.Name = $"TerrainChunk_{x}_{z}";
				chunk.MeshInstance.Mesh = chunk.ArrayMesh;
				chunk.MeshInstance.MaterialOverride = _material;
				chunk.MeshInstance.Layers = TerrainVisualLayer;
				AddChild(chunk.MeshInstance);

				_chunks.Add(chunk);
			}
		}

		if (_material != null)
		{
			_material.SetShaderParameter("terrain_size", new Vector2(w * QuadSize, d * QuadSize));
			_material.SetShaderParameter("grid_spacing", QuadSize);
		}
	}

	protected ShaderMaterial _polarOverlayMaterial;
	private Shader _terrainUberShader;
	private Shader _polarOverlayShader;

	private void CreatePolarOverlayMaterial()
	{
		_polarOverlayShader ??= GD.Load<Shader>("res://Assets/shaders/terrain_polar_overlay.gdshader");
		_polarOverlayMaterial = new ShaderMaterial();
		_polarOverlayMaterial.Shader = _polarOverlayShader;
		_polarOverlayMaterial.RenderPriority = 100;
		_polarOverlayMaterial.SetShaderParameter("grid_spacing", QuadSize);
		ApplyOverlayPass();
	}

	private bool _gridWanted;
	private bool _polarWanted;
	private bool _overlaySuppressed;
	private Vector2 _polarCenterWanted;
	private float _polarRingSpacingWanted = 8.0f;
	private float _polarRadialStepWanted = 45.0f;

	private void ApplyOverlayPass()
	{
		if (_material == null || _polarOverlayMaterial == null) return;
		_polarOverlayMaterial.SetShaderParameter("grid_visible", _gridWanted);
		_polarOverlayMaterial.SetShaderParameter("polar_overlay_visible", _polarWanted);
		_polarOverlayMaterial.SetShaderParameter("polar_center", _polarCenterWanted);
		_polarOverlayMaterial.SetShaderParameter("polar_ring_spacing", _polarRingSpacingWanted);
		_polarOverlayMaterial.SetShaderParameter("polar_radial_step_deg", _polarRadialStepWanted);
		_polarOverlayMaterial.SetShaderParameter("terrain_size", _material.GetShaderParameter("terrain_size"));
		_polarOverlayMaterial.SetShaderParameter("grid_spacing", _material.GetShaderParameter("grid_spacing"));
		bool active = !_overlaySuppressed && (_gridWanted || _polarWanted);
		_material.NextPass = active ? _polarOverlayMaterial : null;
	}

	public virtual void SetGridVisible(bool visible)
	{
		_gridWanted = visible;
		ApplyOverlayPass();
	}

	public virtual void SetPolarOverlayVisible(bool visible)
	{
		_polarWanted = visible;
		ApplyOverlayPass();
	}

	public virtual void SetPolarCenter(Vector2 center)
	{
		_polarCenterWanted = center;
		ApplyOverlayPass();
	}

	public virtual void SetPolarRingSpacing(float spacing)
	{
		_polarRingSpacingWanted = spacing;
		ApplyOverlayPass();
	}

	public virtual void SetPolarRadialStep(float angleDegrees)
	{
		_polarRadialStepWanted = angleDegrees;
		ApplyOverlayPass();
	}

	private bool _wasPathingVisibleBeforeMinimap;

	public static void ClearCache()
	{
		_cachedAlbedoTextureArray = null;
		_cachedNormalRoughnessTextureArray = null;
		_cachedMapDir = null;
		_liveSwatchOverrides.Clear();
	}

	public void BeginMinimapCapture()
	{
		if (_material != null)
		{
			_overlaySuppressed = true;
			ApplyOverlayPass();

			var pathingParam = _material.GetShaderParameter("pathing_visible");
			_wasPathingVisibleBeforeMinimap = pathingParam.VariantType == Variant.Type.Bool && pathingParam.AsBool();
			if (_wasPathingVisibleBeforeMinimap)
			{
				_material.SetShaderParameter("pathing_visible", false);
			}
		}

		SetAllChunksVisible(true);
	}

	public void EndMinimapCapture()
	{
		if (_material != null)
		{
			_overlaySuppressed = false;
			ApplyOverlayPass();
			if (_wasPathingVisibleBeforeMinimap)
			{
				_material.SetShaderParameter("pathing_visible", true);
			}
		}
	}

	public void UpdateMeshAndPhysics(bool rebuildPhysics = true, bool rebuildNavMesh = true, Rect2I? affectedRegion = null, bool rebuildWater = true)
	{
		UpdateMeshAndPhysics(rebuildPhysics, rebuildNavMesh, affectedRegion.HasValue ? new[] { affectedRegion.Value } : (IEnumerable<Rect2I>?)null, rebuildWater);
	}

	public void UpdateMeshAndPhysics(bool rebuildPhysics, bool rebuildNavMesh, IEnumerable<Rect2I>? affectedRegions, bool rebuildWater = true)
	{
		int w = Width;
		int d = Depth;

		EnsureSplatMaps(w, d);

		if (_chunks.Count == 0 || _chunkedWidth != w || _chunkedDepth != d)
		{
			CreateChunks();
		}
		else if (_material != null)
		{
			_material.SetShaderParameter("terrain_size", new Vector2(w * QuadSize, d * QuadSize));
			_material.SetShaderParameter("grid_spacing", QuadSize);
		}

		UpdateAffectedChunks(rebuildPhysics, affectedRegions);

		if (rebuildWater)
		{
			RegenerateWaterMesh();
		}
	}

	private void EnsureSplatMaps(int w, int d)
	{
		if (SplatMap == null || SplatMap.GetLength(0) < w + 1 || SplatMap.GetLength(1) < d + 1)
		{
			var newSplatMap = new TerrainSplatWeights[w + 1, d + 1];
			CopyOrInitSplatMap(SplatMap, newSplatMap, w, d, 0);
			SplatMap = newSplatMap;
		}

		if (CliffSplatMap == null || CliffSplatMap.GetLength(0) < w + 1 || CliffSplatMap.GetLength(1) < d + 1)
		{
			var newCliffSplatMap = new TerrainSplatWeights[w + 1, d + 1];
			CopyOrInitSplatMap(CliffSplatMap, newCliffSplatMap, w, d, 1);
			CliffSplatMap = newCliffSplatMap;
		}
	}

	private void CopyOrInitSplatMap(TerrainSplatWeights[,]? source, TerrainSplatWeights[,] target, int w, int d, int defaultIndex)
	{
		for (int z = 0; z <= d; z++)
		{
			for (int x = 0; x <= w; x++)
			{
				if (source != null && x < source.GetLength(0) && z < source.GetLength(1))
					target[x, z] = source[x, z];
				else
					target[x, z] = TerrainSplatWeights.CreateSolid(defaultIndex);
			}
		}
	}

	private void UpdateAffectedChunks(bool rebuildPhysics, IEnumerable<Rect2I>? affectedRegions)
	{
		foreach (var chunk in _chunks)
		{
			if (affectedRegions != null && !ChunkIntersectsAnyRegion(chunk, affectedRegions))
			{
				continue;
			}

			UpdateChunkMesh(chunk, rebuildPhysics);
		}
	}

	private bool ChunkIntersectsAnyRegion(TerrainChunk chunk, IEnumerable<Rect2I> regions)
	{
		foreach (var region in regions)
		{
			if (!(chunk.EndX < region.Position.X || chunk.StartX > region.Position.X + region.Size.X ||
			      chunk.EndZ < region.Position.Y || chunk.StartZ > region.Position.Y + region.Size.Y))
			{
				return true;
			}
		}

		return false;
	}

	public void SanitizeCornerHeights()
	{
		var cells = Cells;
		if (cells == null) return;

		int w = Width;
		int d = Depth;

		for (int gz = 0; gz <= d; gz++)
		{
			for (int gx = 0; gx <= w; gx++)
			{
				UpdateCornerHeightsAtNode(gx, gz, cells, w, d);
			}
		}
	}

	private void UpdateCornerHeightsAtNode(int gx, int gz, TerrainCell[,] cells, int w, int d)
	{
		float h = GetGridNodeHeight(gx, gz, cells, w, d);
		ApplyCornerHeightSE(gx, gz, cells, w, d, h);
		ApplyCornerHeightSW(gx, gz, cells, w, d, h);
		ApplyCornerHeightNE(gx, gz, cells, w, d, h);
		ApplyCornerHeightNW(gx, gz, cells, w, d, h);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void ApplyCornerHeightSE(int gx, int gz, TerrainCell[,] cells, int w, int d, float h)
	{
		if (gx > 0 && gx - 1 < w && gz > 0 && gz - 1 < d) cells[gx - 1, gz - 1].Y_SE = h;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void ApplyCornerHeightSW(int gx, int gz, TerrainCell[,] cells, int w, int d, float h)
	{
		if (gx < w && gz > 0 && gz - 1 < d) cells[gx, gz - 1].Y_SW = h;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void ApplyCornerHeightNE(int gx, int gz, TerrainCell[,] cells, int w, int d, float h)
	{
		if (gx > 0 && gx - 1 < w && gz < d) cells[gx - 1, gz].Y_NE = h;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void ApplyCornerHeightNW(int gx, int gz, TerrainCell[,] cells, int w, int d, float h)
	{
		if (gx < w && gz < d) cells[gx, gz].Y_NW = h;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float GetGridNodeHeight(int gx, int gz)
	{
		return GetGridNodeHeight(gx, gz, Cells, Width, Depth);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float GetGridNodeHeight(int gx, int gz, TerrainCell[,] cells, int w, int d)
	{
		if (cells == null || w <= 0 || d <= 0) return 0f;
		if ((uint)gx < (uint)w && (uint)gz < (uint)d) return cells[gx, gz].Y_NW;
		int cellX = Math.Clamp(gx, 0, w - 1);
		int cellZ = Math.Clamp(gz, 0, d - 1);
		var cell = cells[cellX, cellZ];
		if (gx >= w && gz >= d) return cell.Y_SE;
		if (gx >= w) return cell.Y_NE;
		if (gz >= d) return cell.Y_SW;
		return cell.Y_NW;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float GetGridNodeHeight(int gx, int gz, float[,] heights, int w, int d)
	{
		if (heights == null || w <= 0 || d <= 0) return 0f;
		int hW = heights.GetLength(0);
		int hD = heights.GetLength(1);
		int cellX = Math.Clamp(gx, 0, hW - 1);
		int cellZ = Math.Clamp(gz, 0, hD - 1);
		return heights[cellX, cellZ];
	}

	public virtual void SetPathingVisible(bool visible)
	{
	}

	public virtual void SetWireframeMode(bool enabled)
	{
	}

	public virtual void ToggleWireframeMode()
	{
	}

	public virtual void UpdatePathingTexture()
	{
	}

	public virtual void ResizeTerrain(int newWidth, int newDepth)
	{
	}

	public virtual void ScaleTerrainData(int newWidth, int newDepth)
	{
	}

	public virtual void RemapSplatIndices(IReadOnlyDictionary<int, int> remap)
	{
	}

	public virtual void RestoreTerrainFromSnapshot(int newWidth, int newDepth, float quadSize, TerrainCell[,] cells, int[,] pathingCodes, TerrainSplatWeights[,] splatMap, TerrainSplatWeights[,] cliffSplatMap = null)
	{
	}

	public virtual void RestoreTerrainFromSnapshot(int newWidth, int newDepth, float quadSize, float[,] heights, int[,] pathingCodes, TerrainSplatWeights[,] splatMap, TerrainSplatWeights[,] cliffSplatMap = null)
	{
	}

	public virtual void ProcessAndSaveRawTexture(string rawPngPath, string outputRtexPath)
	{
	}

	public void UpdatePhysics(Rect2I? affectedRegion = null)
	{
	}

	protected void UpdateChunkMesh(TerrainChunk chunk, bool rebuildPhysics)
	{
		var cells = Cells;
		if (cells == null) return;

		int w = Width;
		int d = Depth;
		float quadSize = QuadSize;
		float halfWQuadSize = (w / 2.0f) * quadSize;
		float halfDQuadSize = (d / 2.0f) * quadSize;
		var splatMap = SplatMap;
		var cliffSplatMap = CliffSplatMap ?? splatMap;

		int maxVertices = 0;
		int maxIndices = 0;

		CountChunkElements(chunk, cells, w, d, ref maxVertices, ref maxIndices);
		EnsureChunkCaches(chunk, maxVertices, maxIndices);

		int vertexIndex = 0;
		int indexIndex = 0;

		ProcessChunkQuads(chunk, cells, w, d, quadSize, halfWQuadSize, halfDQuadSize, splatMap, cliffSplatMap, ref vertexIndex, ref indexIndex);
		WeldChunkVertices(chunk, ref vertexIndex, indexIndex);
		BuildMeshArraysFromCache(chunk, vertexIndex, indexIndex);
		UpdateChunkBoundsAndPhysics(chunk, w, d, quadSize, vertexIndex, rebuildPhysics);

		if (IsRuntimeOnly) ClearChunkCaches(chunk);
	}

	private void CountChunkElements(TerrainChunk chunk, TerrainCell[,] cells, int w, int d, ref int maxVertices, ref int maxIndices)
	{
		for (int z = chunk.StartZ; z < chunk.EndZ; z++)
		{
			for (int x = chunk.StartX; x < chunk.EndX; x++)
			{
				CountQuadElements(x, z, cells, w, d, ref maxVertices, ref maxIndices);
			}
		}
	}

	private void EnsureChunkCaches(TerrainChunk chunk, int maxVertices, int maxIndices)
	{
		if (chunk.VerticesCache == null || chunk.VerticesCache.Length < maxVertices)
		{
			chunk.VerticesCache = new Vector3[maxVertices];
			chunk.NormalsCache = new Vector3[maxVertices];
			chunk.UvsCache = new Vector2[maxVertices];
			chunk.ColorsCache = new Color[maxVertices];
			chunk.TexIndicesCache = new float[maxVertices * 4];
			chunk.TexWeightsCache01 = new float[maxVertices * 4];
			chunk.CliffIndicesCache = new float[maxVertices * 4];
			chunk.CliffWeightsCache = new float[maxVertices * 4];
		}

		if (chunk.IndicesCache == null || chunk.IndicesCache.Length < maxIndices)
		{
			chunk.IndicesCache = new int[maxIndices];
		}
	}

	private void ProcessChunkQuads(TerrainChunk chunk, TerrainCell[,] cells, int w, int d, float quadSize, float halfWQuadSize, float halfDQuadSize, TerrainSplatWeights[,] splatMap, TerrainSplatWeights[,] cliffSplatMap, ref int vertexIndex, ref int indexIndex)
	{
		for (int z = chunk.StartZ; z < chunk.EndZ; z++)
		{
			for (int x = chunk.StartX; x < chunk.EndX; x++)
			{
				ProcessCellQuad(chunk, x, z, cells, w, d, quadSize, halfWQuadSize, halfDQuadSize, splatMap, cliffSplatMap, ref vertexIndex, ref indexIndex);
			}
		}
	}

	private void BuildMeshArraysFromCache(TerrainChunk chunk, int vertexIndex, int indexIndex)
	{
		Vector3[] finalVertices = chunk.VerticesCache;
		Color[] finalColors = chunk.ColorsCache;
		Vector3[] finalNormals = chunk.NormalsCache;
		Vector2[] finalUvs = chunk.UvsCache;
		float[] finalTexIndices = chunk.TexIndicesCache;
		float[] finalTexWeights = chunk.TexWeightsCache01;
		float[] finalCliffIndices = chunk.CliffIndicesCache;
		float[] finalCliffWeights = chunk.CliffWeightsCache;
		int[] finalIndices = chunk.IndicesCache;

		if (vertexIndex < chunk.VerticesCache.Length)
		{
			finalVertices = new Vector3[vertexIndex];
			System.Array.Copy(chunk.VerticesCache, finalVertices, vertexIndex);
			finalColors = new Color[vertexIndex];
			System.Array.Copy(chunk.ColorsCache, finalColors, vertexIndex);
			finalNormals = new Vector3[vertexIndex];
			System.Array.Copy(chunk.NormalsCache, finalNormals, vertexIndex);
			finalUvs = new Vector2[vertexIndex];
			System.Array.Copy(chunk.UvsCache, finalUvs, vertexIndex);
			finalTexIndices = new float[vertexIndex * 4];
			System.Array.Copy(chunk.TexIndicesCache, finalTexIndices, vertexIndex * 4);
			finalTexWeights = new float[vertexIndex * 4];
			System.Array.Copy(chunk.TexWeightsCache01, finalTexWeights, vertexIndex * 4);
			finalCliffIndices = new float[vertexIndex * 4];
			System.Array.Copy(chunk.CliffIndicesCache, finalCliffIndices, vertexIndex * 4);
			finalCliffWeights = new float[vertexIndex * 4];
			System.Array.Copy(chunk.CliffWeightsCache, finalCliffWeights, vertexIndex * 4);
		}

		if (indexIndex < chunk.IndicesCache.Length)
		{
			finalIndices = new int[indexIndex];
			System.Array.Copy(chunk.IndicesCache, finalIndices, indexIndex);
		}

		var arrays = new Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = finalVertices;
		arrays[(int)Mesh.ArrayType.Color] = finalColors;
		arrays[(int)Mesh.ArrayType.Normal] = finalNormals;
		arrays[(int)Mesh.ArrayType.TexUV] = finalUvs;
		arrays[(int)Mesh.ArrayType.Custom0] = finalTexIndices;
		arrays[(int)Mesh.ArrayType.Custom1] = finalTexWeights;
		arrays[(int)Mesh.ArrayType.Custom2] = finalCliffIndices;
		arrays[(int)Mesh.ArrayType.Custom3] = finalCliffWeights;
		arrays[(int)Mesh.ArrayType.Index] = finalIndices;

		chunk.ArrayMesh.ClearSurfaces();
		if (vertexIndex > 0)
		{
			int custom0Format = (int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift;
			int custom1Format = (int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift;
			int custom2Format = (int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom2Shift;
			int custom3Format = (int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom3Shift;
			chunk.ArrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays,
				new Array<Array>(),
				null,
				(Mesh.ArrayFormat)((int)(Mesh.ArrayFormat.FormatCustom0 | Mesh.ArrayFormat.FormatCustom1 | Mesh.ArrayFormat.FormatCustom2 | Mesh.ArrayFormat.FormatCustom3) | custom0Format | custom1Format | custom2Format | custom3Format));
		}
	}

	private void UpdateChunkBoundsAndPhysics(TerrainChunk chunk, int w, int d, float quadSize, int vertexIndex, bool rebuildPhysics)
	{
		float minY = float.MaxValue;
		float maxY = float.MinValue;

		for (int i = 0; i < vertexIndex; i++)
		{
			float y = chunk.VerticesCache[i].Y;
			if (y < minY) minY = y;
			if (y > maxY) maxY = y;
		}

		if (vertexIndex == 0)
		{
			minY = -2f;
			maxY = 2f;
		}

		chunk.MinY = minY;
		chunk.MaxY = maxY;

		float halfW = (w / 2.0f) * quadSize;
		float halfD = (d / 2.0f) * quadSize;
		float minX = chunk.StartX * quadSize - halfW;
		float maxX = chunk.EndX * quadSize - halfW;
		float minZ = chunk.StartZ * quadSize - halfD;
		float maxZ = chunk.EndZ * quadSize - halfD;

		chunk.WorldAabb = new Aabb(new Vector3(minX, minY - 2f, minZ), new Vector3(maxX - minX, Math.Max(0.5f, maxY - minY + 10f), maxZ - minZ));
		chunk.MeshInstance.CustomAabb = chunk.WorldAabb;

		if (chunk.ProfileWaterMeshes != null)
		{
			foreach (var (pMesh, _) in chunk.ProfileWaterMeshes.Values)
			{
				if (GodotObject.IsInstanceValid(pMesh)) pMesh.CustomAabb = chunk.WorldAabb;
			}
		}

	}

	private void ClearChunkCaches(TerrainChunk chunk)
	{
		chunk.VerticesCache = null;
		chunk.NormalsCache = null;
		chunk.ColorsCache = null;
		chunk.TexIndicesCache = null;
		chunk.TexWeightsCache01 = null;
		chunk.CliffIndicesCache = null;
		chunk.CliffWeightsCache = null;
		chunk.UvsCache = null;
		chunk.IndicesCache = null;
		chunk.MapDataCache = null;
		chunk.WeldVertexMap = null;
		chunk.WeldRemapTable = null;
	}

	public override void _Process(double delta)
	{
		UpdateFrustumCulling();
	}

	private Vector3 _lastFrustumCamPos;
	private Vector3 _lastFrustumCamRot;
	private readonly Plane[] _cachedFrustumPlanes = new Plane[6];

	protected void UpdateFrustumCulling()
	{
		if (IsMinimapRendering)
		{
			SetAllChunksVisible(true);
			return;
		}

		var viewport = GetViewport();
		var camera = viewport?.GetCamera3D();
		if (camera == null || !GodotObject.IsInstanceValid(camera)) return;

		if (camera.Projection == Camera3D.ProjectionType.Orthogonal)
		{
			SetAllChunksVisible(true);
			return;
		}

		Vector3 camPos = camera.GlobalPosition;
		Vector3 camRot = camera.GlobalRotation;
		if ((camPos - _lastFrustumCamPos).LengthSquared() < 0.0001f && (camRot - _lastFrustumCamRot).LengthSquared() < 0.0001f)
		{
			return;
		}

		_lastFrustumCamPos = camPos;
		_lastFrustumCamRot = camRot;

		UpdateVisibilityFromFrustum(camera);
	}

	private void UpdateVisibilityFromFrustum(Camera3D camera)
	{
		var frustum = camera.GetFrustum();
		if (frustum == null || frustum.Count < 6) return;

		for (int i = 0; i < 6; i++)
		{
			_cachedFrustumPlanes[i] = frustum[i];
		}

		ReadOnlySpan<Plane> planesSpan = _cachedFrustumPlanes;
		foreach (var chunk in _chunks)
		{
			bool visible = IntersectsFrustum(planesSpan, chunk.WorldAabb);
			UpdateChunkVisibility(chunk, visible);
		}
	}

	private void UpdateChunkVisibility(TerrainChunk chunk, bool visible)
	{
		if (GodotObject.IsInstanceValid(chunk.MeshInstance) && chunk.MeshInstance.Visible != visible)
		{
			chunk.MeshInstance.Visible = visible;
		}

		if (chunk.ProfileWaterMeshes != null)
		{
			foreach (var (pMesh, _) in chunk.ProfileWaterMeshes.Values)
			{
				if (GodotObject.IsInstanceValid(pMesh) && pMesh.Visible != visible)
				{
					pMesh.Visible = visible;
				}
			}
		}
	}

	public void SetAllChunksVisible(bool visible)
	{
		foreach (var chunk in _chunks)
		{
			if (GodotObject.IsInstanceValid(chunk.MeshInstance) && chunk.MeshInstance.Visible != visible) chunk.MeshInstance.Visible = visible;
			if (chunk.ProfileWaterMeshes != null)
			{
				foreach (var (pMesh, _) in chunk.ProfileWaterMeshes.Values)
				{
					if (GodotObject.IsInstanceValid(pMesh) && pMesh.Visible != visible) pMesh.Visible = visible;
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IntersectsFrustum(ReadOnlySpan<Plane> frustumPlanes, Aabb aabb)
	{
		aabb = aabb.Grow(8.0f);
		Vector3 min = aabb.Position;
		Vector3 max = aabb.End;

		for (int i = 0; i < frustumPlanes.Length; i++)
		{
			ref readonly Plane plane = ref frustumPlanes[i];
			Vector3 n = new Vector3(
				plane.Normal.X >= 0 ? min.X : max.X,
				plane.Normal.Y >= 0 ? min.Y : max.Y,
				plane.Normal.Z >= 0 ? min.Z : max.Z
			);

			if (plane.DistanceTo(n) > 0)
			{
				return false;
			}
		}

		return true;
	}

	public static bool IntersectsFrustum(Array<Plane> frustumPlanes, Aabb aabb)
	{
		if (frustumPlanes == null || frustumPlanes.Count == 0) return true;
		Span<Plane> span = stackalloc Plane[Math.Min(frustumPlanes.Count, 32)];
		for (int i = 0; i < span.Length; i++) span[i] = frustumPlanes[i];
		return IntersectsFrustum((ReadOnlySpan<Plane>)span, aabb);
	}

	private Vector3 GetWorldPosition(float gridX, float gridZ, float height, float halfWQuadSize, float halfDQuadSize, float quadSize)
	{
		float lx = gridX * quadSize - halfWQuadSize;
		float lz = gridZ * quadSize - halfDQuadSize;
		return new Vector3(lx, height, lz);
	}

	private Vector3 GetWorldPosition(float gridX, float gridZ, float height)
	{
		return GetWorldPosition(gridX, gridZ, height, (Width / 2.0f) * QuadSize, (Depth / 2.0f) * QuadSize, QuadSize);
	}

	private void CountQuadElements(
		int x, int z,
		TerrainCell[,] cells,
		int w, int d,
		ref int totalVertices,
		ref int totalIndices)
	{
		if (cells == null) return;
		int cellW = cells.GetLength(0);
		int cellD = cells.GetLength(1);
		if (x < 0 || x >= cellW || z < 0 || z >= cellD) return;

		totalVertices += 12;
		totalIndices += 12;
	}

	private (int tex0, int tex1, int tex2, int tex3) GetQuadDominantTextures(ReadOnlySpan<TerrainSplatWeights> splats)
	{
		Span<int> indices = stackalloc int[16];
		Span<float> weights = stackalloc float[16];
		int count = AccumulateSplatWeights(splats, indices, weights);

		if (count == 0)
		{
			int defaultTex = splats.Length > 0 ? splats[0].Index0 : 0;
			return (defaultTex, defaultTex, defaultTex, defaultTex);
		}

		SortWeightsDescending(indices, weights, count);

		int res0 = indices[0];
		int res1 = count > 1 ? indices[1] : res0;
		int res2 = count > 2 ? indices[2] : res0;
		int res3 = count > 3 ? indices[3] : res0;

		return (res0, res1, res2, res3);
	}

	private int AccumulateSplatWeights(ReadOnlySpan<TerrainSplatWeights> splats, Span<int> indices, Span<float> weights)
	{
		int count = 0;

		for (int sIdx = 0; sIdx < splats.Length; sIdx++)
		{
			TerrainSplatWeights s = splats[sIdx];
			for (int k = 0; k < 4; k++)
			{
				int idx = k switch { 0 => s.Index0, 1 => s.Index1, 2 => s.Index2, _ => s.Index3 };
				float w = k switch { 0 => s.Weight0, 1 => s.Weight1, 2 => s.Weight2, _ => s.Weight3 };
				if (w <= 0.0001f) continue;

				count = AddOrUpdateWeight(idx, w, indices, weights, count);
			}
		}

		return count;
	}

	private int AddOrUpdateWeight(int idx, float w, Span<int> indices, Span<float> weights, int count)
	{
		for (int i = 0; i < count; i++)
		{
			if (indices[i] == idx)
			{
				weights[i] += w;
				return count;
			}
		}

		if (count < 16)
		{
			indices[count] = idx;
			weights[count] = w;
			return count + 1;
		}

		return count;
	}

	private void SortWeightsDescending(Span<int> indices, Span<float> weights, int count)
	{
		for (int i = 0; i < count - 1; i++)
		{
			for (int j = i + 1; j < count; j++)
			{
				if (weights[j] > weights[i])
				{
					float tempW = weights[i];
					weights[i] = weights[j];
					weights[j] = tempW;

					int tempIdx = indices[i];
					indices[i] = indices[j];
					indices[j] = tempIdx;
				}
			}
		}
	}

	private (int tex0, int tex1, int tex2, int tex3) GetQuadDominantTextures(TerrainSplatWeights s0, TerrainSplatWeights s1, TerrainSplatWeights s2, TerrainSplatWeights s3)
	{
		Span<TerrainSplatWeights> splats = stackalloc TerrainSplatWeights[4] { s0, s1, s2, s3 };
		return GetQuadDominantTextures(splats);
	}

	private (float w0, float w1, float w2, float w3) GetSplatWeightsForQuad(TerrainSplatWeights s, int tex0, int tex1, int tex2, int tex3)
	{
		float w0 = GetWeightForTexture(s, tex0);
		float w1 = (tex1 != tex0) ? GetWeightForTexture(s, tex1) : 0.0f;
		float w2 = (tex2 != tex0 && tex2 != tex1) ? GetWeightForTexture(s, tex2) : 0.0f;
		float w3 = (tex3 != tex0 && tex3 != tex1 && tex3 != tex2) ? GetWeightForTexture(s, tex3) : 0.0f;

		return NormalizeSplatWeights(w0, w1, w2, w3);
	}

	private float GetWeightForTexture(in TerrainSplatWeights splat, int targetTexIndex)
	{
		float w = 0.0f;
		if (splat.Index0 == targetTexIndex) w += splat.Weight0;
		if (splat.Index1 == targetTexIndex) w += splat.Weight1;
		if (splat.Index2 == targetTexIndex) w += splat.Weight2;
		if (splat.Index3 == targetTexIndex) w += splat.Weight3;
		return w;
	}

	private (float w0, float w1, float w2, float w3) NormalizeSplatWeights(float w0, float w1, float w2, float w3)
	{
		float sumW = w0 + w1 + w2 + w3;
		if (sumW > 0.0001f)
		{
			float invSum = 1.0f / sumW;
			return (w0 * invSum, w1 * invSum, w2 * invSum, w3 * invSum);
		}

		return (1.0f, 0.0f, 0.0f, 0.0f);
	}

	private TerrainSplatWeights NormalizeAndCreateSplatWeights(int tex0, int tex1, int tex2, int tex3, float avg0, float avg1, float avg2, float avg3)
	{
		float sum = avg0 + avg1 + avg2 + avg3;
		if (sum > 0.0001f)
		{
			float invSum = 1.0f / sum;
			avg0 *= invSum;
			avg1 *= invSum;
			avg2 *= invSum;
			avg3 *= invSum;
		}
		else
		{
			avg0 = 1.0f;
			avg1 = 0f;
			avg2 = 0f;
			avg3 = 0f;
		}

		return new TerrainSplatWeights
		{
			Index0 = tex0,
			Index1 = tex1,
			Index2 = tex2,
			Index3 = tex3,
			Weight0 = avg0,
			Weight1 = avg1,
			Weight2 = avg2,
			Weight3 = avg3
		};
	}

	private TerrainSplatWeights BlendSplatWeights(TerrainSplatWeights s0, TerrainSplatWeights s1, TerrainSplatWeights s2, TerrainSplatWeights s3)
	{
		var (tex0, tex1, tex2, tex3) = GetQuadDominantTextures(s0, s1, s2, s3);
		var (w00, w01, w02, w03) = GetSplatWeightsForQuad(s0, tex0, tex1, tex2, tex3);
		var (w10, w11, w12, w13) = GetSplatWeightsForQuad(s1, tex0, tex1, tex2, tex3);
		var (w20, w21, w22, w23) = GetSplatWeightsForQuad(s2, tex0, tex1, tex2, tex3);
		var (w30, w31, w32, w33) = GetSplatWeightsForQuad(s3, tex0, tex1, tex2, tex3);

		float avg0 = (w00 + w10 + w20 + w30) * 0.25f;
		float avg1 = (w01 + w11 + w21 + w31) * 0.25f;
		float avg2 = (w02 + w12 + w22 + w32) * 0.25f;
		float avg3 = (w03 + w13 + w23 + w33) * 0.25f;

		return NormalizeAndCreateSplatWeights(tex0, tex1, tex2, tex3, avg0, avg1, avg2, avg3);
	}

	private TerrainSplatWeights BlendSplatWeights(TerrainSplatWeights s0, TerrainSplatWeights s1, TerrainSplatWeights s2)
	{
		var (tex0, tex1, tex2, tex3) = GetQuadDominantTextures(s0, s1, s2, s0);
		var (w00, w01, w02, w03) = GetSplatWeightsForQuad(s0, tex0, tex1, tex2, tex3);
		var (w10, w11, w12, w13) = GetSplatWeightsForQuad(s1, tex0, tex1, tex2, tex3);
		var (w20, w21, w22, w23) = GetSplatWeightsForQuad(s2, tex0, tex1, tex2, tex3);

		float avg0 = (w00 + w10 + w20) / 3.0f;
		float avg1 = (w01 + w11 + w21) / 3.0f;
		float avg2 = (w02 + w12 + w22) / 3.0f;
		float avg3 = (w03 + w13 + w23) / 3.0f;

		return NormalizeAndCreateSplatWeights(tex0, tex1, tex2, tex3, avg0, avg1, avg2, avg3);
	}

	public static float Smoothstep(float edge0, float edge1, float x)
	{
		float t = Mathf.Clamp((x - edge0) / (edge1 - edge0), 0.0f, 1.0f);
		return t * t * (3.0f - 2.0f * t);
	}

	public static float GetCliffFactor(Vector3 normal)
	{
		float ny = Mathf.Clamp(normal.Y, 0.0f, 1.0f);
		return 1.0f - Smoothstep(0.70f, 0.90f, ny);
	}

	public static float GetVertexCliffWeight(int gx, int gz, TerrainCell[,] cells, int w, int d, float quadSize)
	{
		if (cells == null || w <= 0 || d <= 0) return 0f;

		float maxDelta = CalculateMaxHeightDelta(gx, gz, cells, w, d);
		var (minTier, maxTier, cellCount) = CalculateTierBounds(gx, gz, cells, w, d);

		float tierCliffDeltaThreshold = TerrainCell.TIER_HEIGHT * 0.70f;
		bool hasTierDifference = cellCount > 1 && (maxTier - minTier) >= 1;
		bool hasStepHeightDelta = maxDelta >= tierCliffDeltaThreshold;

		if (!hasTierDifference && !hasStepHeightDelta) return 0.0f;

		float deltaCliff = Smoothstep(TerrainCell.TIER_HEIGHT * 0.50f, TerrainCell.TIER_HEIGHT * 0.85f, maxDelta);
		if (hasTierDifference)
		{
			deltaCliff = Math.Max(deltaCliff, 1.0f);
		}

		return Math.Clamp(deltaCliff, 0.0f, 1.0f);
	}

	private static float CalculateMaxHeightDelta(int gx, int gz, TerrainCell[,] cells, int w, int d)
	{
		float h = GetGridNodeHeight(gx, gz, cells, w, d);
		float maxDelta = 0f;

		if (gx > 0) maxDelta = Math.Max(maxDelta, Math.Abs(h - GetGridNodeHeight(gx - 1, gz, cells, w, d)));
		if (gx < w) maxDelta = Math.Max(maxDelta, Math.Abs(h - GetGridNodeHeight(gx + 1, gz, cells, w, d)));
		if (gz > 0) maxDelta = Math.Max(maxDelta, Math.Abs(h - GetGridNodeHeight(gx, gz - 1, cells, w, d)));
		if (gz < d) maxDelta = Math.Max(maxDelta, Math.Abs(h - GetGridNodeHeight(gx, gz + 1, cells, w, d)));

		return maxDelta;
	}

	private static (sbyte minTier, sbyte maxTier, int cellCount) CalculateTierBounds(int gx, int gz, TerrainCell[,] cells, int w, int d)
	{
		sbyte minTier = sbyte.MaxValue;
		sbyte maxTier = sbyte.MinValue;
		int cellCount = 0;

		void CheckCell(int cx, int cz)
		{
			if (cx >= 0 && cx < w && cz >= 0 && cz < d)
			{
				sbyte t = cells[cx, cz].MacroTier;
				if (t < minTier) minTier = t;
				if (t > maxTier) maxTier = t;
				cellCount++;
			}
		}

		CheckCell(gx - 1, gz - 1);
		CheckCell(gx, gz - 1);
		CheckCell(gx - 1, gz);
		CheckCell(gx, gz);

		return (minTier, maxTier, cellCount);
	}

	private void ProcessCellQuad(
		TerrainChunk chunk,
		int x, int z,
		TerrainCell[,] cells,
		int w, int d,
		float quadSize,
		float halfWQuadSize,
		float halfDQuadSize,
		TerrainSplatWeights[,] splatMap,
		TerrainSplatWeights[,] cliffSplatMap,
		ref int vertexIndex,
		ref int indexIndex)
	{
		var cell = cells[x, z];
		var heights = ExtractQuadHeights(cell);
		var pos = CalculateQuadPositions(x, z, heights, halfWQuadSize, halfDQuadSize, quadSize);
		var norms = CalculateQuadNormals(x, z, cells, w, d, quadSize);

		float quadCliffWeight = CalculateQuadCliffWeight(x, z, cell, cells, w, d, heights);
		var cliffWeights = CalculateQuadVertexCliffWeights(x, z, cells, w, d, quadSize);

		var (floorS00, floorS10, floorS11, floorS01, floorSC) = GetSplatMapQuad(x, z, splatMap);
		var (cliffS00, cliffS10, cliffS11, cliffS01, cliffSC) = GetSplatMapQuad(x, z, cliffSplatMap);

		var uvs = CalculateQuadUvs(pos);

		var gTex = GetQuadDominantTextures(floorS00, floorS10, floorS11, floorS01);
		var cTex = GetQuadDominantTextures(cliffS00, cliffS10, cliffS11, cliffS01);

		ProcessSubTriangleGround(chunk, pos.NW, pos.NE, pos.C, norms.NW, norms.NE, norms.C, uvs.NW, uvs.NE, uvs.C, cliffWeights.NW, cliffWeights.NE, cliffWeights.C, quadCliffWeight, floorS00, floorS10, floorSC, gTex.tex0, gTex.tex1, gTex.tex2, gTex.tex3, cliffS00, cliffS10, cliffSC, cTex.tex0, cTex.tex1, cTex.tex2, cTex.tex3, ref vertexIndex, ref indexIndex);
		ProcessSubTriangleGround(chunk, pos.NE, pos.SE, pos.C, norms.NE, norms.SE, norms.C, uvs.NE, uvs.SE, uvs.C, cliffWeights.NE, cliffWeights.SE, cliffWeights.C, quadCliffWeight, floorS10, floorS11, floorSC, gTex.tex0, gTex.tex1, gTex.tex2, gTex.tex3, cliffS10, cliffS11, cliffSC, cTex.tex0, cTex.tex1, cTex.tex2, cTex.tex3, ref vertexIndex, ref indexIndex);
		ProcessSubTriangleGround(chunk, pos.SE, pos.SW, pos.C, norms.SE, norms.SW, norms.C, uvs.SE, uvs.SW, uvs.C, cliffWeights.SE, cliffWeights.SW, cliffWeights.C, quadCliffWeight, floorS11, floorS01, floorSC, gTex.tex0, gTex.tex1, gTex.tex2, gTex.tex3, cliffS11, cliffS01, cliffSC, cTex.tex0, cTex.tex1, cTex.tex2, cTex.tex3, ref vertexIndex, ref indexIndex);
		ProcessSubTriangleGround(chunk, pos.SW, pos.NW, pos.C, norms.SW, norms.NW, norms.C, uvs.SW, uvs.NW, uvs.C, cliffWeights.SW, cliffWeights.NW, cliffWeights.C, quadCliffWeight, floorS01, floorS00, floorSC, gTex.tex0, gTex.tex1, gTex.tex2, gTex.tex3, cliffS01, cliffS00, cliffSC, cTex.tex0, cTex.tex1, cTex.tex2, cTex.tex3, ref vertexIndex, ref indexIndex);
	}

	private (float hNW, float hNE, float hSE, float hSW, float hC) ExtractQuadHeights(TerrainCell cell)
	{
		return (cell.Y_NW, cell.Y_NE, cell.Y_SE, cell.Y_SW, cell.CenterHeight);
	}

	private (Vector3 NW, Vector3 NE, Vector3 SE, Vector3 SW, Vector3 C) CalculateQuadPositions(int x, int z, (float hNW, float hNE, float hSE, float hSW, float hC) heights, float halfWQuadSize, float halfDQuadSize, float quadSize)
	{
		return (
			GetWorldPosition(x, z, heights.hNW, halfWQuadSize, halfDQuadSize, quadSize),
			GetWorldPosition(x + 1, z, heights.hNE, halfWQuadSize, halfDQuadSize, quadSize),
			GetWorldPosition(x + 1, z + 1, heights.hSE, halfWQuadSize, halfDQuadSize, quadSize),
			GetWorldPosition(x, z + 1, heights.hSW, halfWQuadSize, halfDQuadSize, quadSize),
			GetWorldPosition(x + 0.5f, z + 0.5f, heights.hC, halfWQuadSize, halfDQuadSize, quadSize)
		);
	}

	private (Vector3 NW, Vector3 NE, Vector3 SE, Vector3 SW, Vector3 C) CalculateQuadNormals(int x, int z, TerrainCell[,] cells, int w, int d, float quadSize)
	{
		Vector3 normNW = GetVertexNormal(x, z, cells, w, d, quadSize);
		Vector3 normNE = GetVertexNormal(x + 1, z, cells, w, d, quadSize);
		Vector3 normSE = GetVertexNormal(x + 1, z + 1, cells, w, d, quadSize);
		Vector3 normSW = GetVertexNormal(x, z + 1, cells, w, d, quadSize);
		Vector3 normC = (normNW + normNE + normSE + normSW).Normalized();

		return (normNW, normNE, normSE, normSW, normC);
	}

	private float CalculateQuadCliffWeight(int x, int z, TerrainCell cell, TerrainCell[,] cells, int w, int d, (float hNW, float hNE, float hSE, float hSW, float hC) heights)
	{
		bool bordersDifferentTier = CheckBordersDifferentTier(x, z, cell.MacroTier, cells, w, d);

		float maxDelta = Math.Max(
			Math.Max(Math.Abs(heights.hNW - heights.hNE), Math.Abs(heights.hNE - heights.hSE)),
			Math.Max(Math.Abs(heights.hSE - heights.hSW), Math.Abs(heights.hSW - heights.hNW))
		);
		float maxCenterDelta = Math.Max(
			Math.Max(Math.Abs(heights.hC - heights.hNW), Math.Abs(heights.hC - heights.hNE)),
			Math.Max(Math.Abs(heights.hC - heights.hSE), Math.Abs(heights.hC - heights.hSW))
		);
		float totalQuadDelta = Math.Max(maxDelta, maxCenterDelta);

		float tierStepThreshold = TerrainCell.TIER_HEIGHT * 0.70f;
		bool isCliffQuad = (bordersDifferentTier && totalQuadDelta >= tierStepThreshold * 0.5f) || totalQuadDelta >= tierStepThreshold;

		return isCliffQuad ? 1.0f : 0.0f;
	}

	private bool CheckBordersDifferentTier(int x, int z, sbyte currentTier, TerrainCell[,] cells, int w, int d)
	{
		if (x > 0 && Math.Abs(cells[x - 1, z].MacroTier - currentTier) >= 1) return true;
		if (x < w - 1 && Math.Abs(cells[x + 1, z].MacroTier - currentTier) >= 1) return true;
		if (z > 0 && Math.Abs(cells[x, z - 1].MacroTier - currentTier) >= 1) return true;
		if (z < d - 1 && Math.Abs(cells[x, z + 1].MacroTier - currentTier) >= 1) return true;
		return false;
	}

	private (float NW, float NE, float SE, float SW, float C) CalculateQuadVertexCliffWeights(int x, int z, TerrainCell[,] cells, int w, int d, float quadSize)
	{
		float cliffNW = GetVertexCliffWeight(x, z, cells, w, d, quadSize);
		float cliffNE = GetVertexCliffWeight(x + 1, z, cells, w, d, quadSize);
		float cliffSE = GetVertexCliffWeight(x + 1, z + 1, cells, w, d, quadSize);
		float cliffSW = GetVertexCliffWeight(x, z + 1, cells, w, d, quadSize);
		float cliffC = (cliffNW + cliffNE + cliffSE + cliffSW) * 0.25f;

		return (cliffNW, cliffNE, cliffSE, cliffSW, cliffC);
	}

	private (TerrainSplatWeights s00, TerrainSplatWeights s10, TerrainSplatWeights s11, TerrainSplatWeights s01, TerrainSplatWeights sC) GetSplatMapQuad(int x, int z, TerrainSplatWeights[,] splatMap)
	{
		if (splatMap == null || splatMap.GetLength(0) == 0 || splatMap.GetLength(1) == 0)
		{
			return (default, default, default, default, default);
		}

		int mapW = splatMap.GetLength(0);
		int mapD = splatMap.GetLength(1);

		TerrainSplatWeights s00 = splatMap[Math.Clamp(x, 0, mapW - 1), Math.Clamp(z, 0, mapD - 1)];
		TerrainSplatWeights s10 = splatMap[Math.Clamp(x + 1, 0, mapW - 1), Math.Clamp(z, 0, mapD - 1)];
		TerrainSplatWeights s11 = splatMap[Math.Clamp(x + 1, 0, mapW - 1), Math.Clamp(z + 1, 0, mapD - 1)];
		TerrainSplatWeights s01 = splatMap[Math.Clamp(x, 0, mapW - 1), Math.Clamp(z + 1, 0, mapD - 1)];
		TerrainSplatWeights sC = BlendSplatWeights(s00, s10, s11, s01);

		return (s00, s10, s11, s01, sC);
	}

	private (Vector2 NW, Vector2 NE, Vector2 SE, Vector2 SW, Vector2 C) CalculateQuadUvs((Vector3 NW, Vector3 NE, Vector3 SE, Vector3 SW, Vector3 C) pos)
	{
		return (
			new Vector2(pos.NW.X, pos.NW.Z),
			new Vector2(pos.NE.X, pos.NE.Z),
			new Vector2(pos.SE.X, pos.SE.Z),
			new Vector2(pos.SW.X, pos.SW.Z),
			new Vector2(pos.C.X, pos.C.Z)
		);
	}

	private void ProcessSubTriangleGround(
		TerrainChunk chunk,
		Vector3 pos0, Vector3 pos1, Vector3 pos2,
		Vector3 norm0, Vector3 norm1, Vector3 norm2,
		Vector2 uv0, Vector2 uv1, Vector2 uv2,
		float cliff0, float cliff1, float cliff2,
		float quadCliff,
		TerrainSplatWeights gS0, TerrainSplatWeights gS1, TerrainSplatWeights gS2,
		int gTex0, int gTex1, int gTex2, int gTex3,
		TerrainSplatWeights cS0, TerrainSplatWeights cS1, TerrainSplatWeights cS2,
		int cTex0, int cTex1, int cTex2, int cTex3,
		ref int vertexIndex,
		ref int indexIndex)
	{
		int baseIndex = vertexIndex;

		var (gw00, gw01, gw02, gw03) = GetSplatWeightsForQuad(gS0, gTex0, gTex1, gTex2, gTex3);
		var (gw10, gw11, gw12, gw13) = GetSplatWeightsForQuad(gS1, gTex0, gTex1, gTex2, gTex3);
		var (gw20, gw21, gw22, gw23) = GetSplatWeightsForQuad(gS2, gTex0, gTex1, gTex2, gTex3);

		var (cw00, cw01, cw02, cw03) = GetSplatWeightsForQuad(cS0, cTex0, cTex1, cTex2, cTex3);
		var (cw10, cw11, cw12, cw13) = GetSplatWeightsForQuad(cS1, cTex0, cTex1, cTex2, cTex3);
		var (cw20, cw21, cw22, cw23) = GetSplatWeightsForQuad(cS2, cTex0, cTex1, cTex2, cTex3);

		PopulateExplicitVertex(chunk, pos0, uv0, norm0, cliff0, quadCliff, gTex0, gTex1, gTex2, gTex3, gw00, gw01, gw02, gw03, cTex0, cTex1, cTex2, cTex3, cw00, cw01, cw02, cw03, ref vertexIndex);
		PopulateExplicitVertex(chunk, pos1, uv1, norm1, cliff1, quadCliff, gTex0, gTex1, gTex2, gTex3, gw10, gw11, gw12, gw13, cTex0, cTex1, cTex2, cTex3, cw10, cw11, cw12, cw13, ref vertexIndex);
		PopulateExplicitVertex(chunk, pos2, uv2, norm2, cliff2, quadCliff, gTex0, gTex1, gTex2, gTex3, gw20, gw21, gw22, gw23, cTex0, cTex1, cTex2, cTex3, cw20, cw21, cw22, cw23, ref vertexIndex);

		chunk.IndicesCache[indexIndex++] = baseIndex;
		chunk.IndicesCache[indexIndex++] = baseIndex + 1;
		chunk.IndicesCache[indexIndex++] = baseIndex + 2;
	}

	private void PopulateExplicitVertex(
		TerrainChunk chunk,
		Vector3 position,
		Vector2 uv,
		Vector3 faceNormal,
		float cliffWeight,
		float quadCliffWeight,
		int gTex0, int gTex1, int gTex2, int gTex3,
		float gw0, float gw1, float gw2, float gw3,
		int cTex0, int cTex1, int cTex2, int cTex3,
		float cw0, float cw1, float cw2, float cw3,
		ref int vertexIndex)
	{
		chunk.VerticesCache[vertexIndex] = position;
		chunk.NormalsCache[vertexIndex] = faceNormal;
		chunk.UvsCache[vertexIndex] = uv;
		chunk.ColorsCache[vertexIndex] = new Color(quadCliffWeight, 1.0f, 1.0f, cliffWeight);

		int sIdx = vertexIndex * 4;
		chunk.TexIndicesCache[sIdx + 0] = gTex0;
		chunk.TexIndicesCache[sIdx + 1] = gTex1;
		chunk.TexIndicesCache[sIdx + 2] = gTex2;
		chunk.TexIndicesCache[sIdx + 3] = gTex3;

		chunk.TexWeightsCache01[sIdx + 0] = gw0 > 0.0001f ? gw0 : 0.0f;
		chunk.TexWeightsCache01[sIdx + 1] = gw1 > 0.0001f ? gw1 : 0.0f;
		chunk.TexWeightsCache01[sIdx + 2] = gw2 > 0.0001f ? gw2 : 0.0f;
		chunk.TexWeightsCache01[sIdx + 3] = gw3 > 0.0001f ? gw3 : 0.0f;

		chunk.CliffIndicesCache[sIdx + 0] = cTex0;
		chunk.CliffIndicesCache[sIdx + 1] = cTex1;
		chunk.CliffIndicesCache[sIdx + 2] = cTex2;
		chunk.CliffIndicesCache[sIdx + 3] = cTex3;

		chunk.CliffWeightsCache[sIdx + 0] = cw0 > 0.0001f ? cw0 : 0.0f;
		chunk.CliffWeightsCache[sIdx + 1] = cw1 > 0.0001f ? cw1 : 0.0f;
		chunk.CliffWeightsCache[sIdx + 2] = cw2 > 0.0001f ? cw2 : 0.0f;
		chunk.CliffWeightsCache[sIdx + 3] = cw3 > 0.0001f ? cw3 : 0.0f;

		vertexIndex++;
	}

	private void WeldChunkVertices(TerrainChunk chunk, ref int vertexIndex, int indexIndex)
	{
		if (vertexIndex == 0)
		{
			return;
		}

		if (chunk.WeldVertexMap == null)
		{
			chunk.WeldVertexMap = new System.Collections.Generic.Dictionary<TerrainVertexKey, int>(vertexIndex);
		}
		else
		{
			chunk.WeldVertexMap.Clear();
		}

		if (chunk.WeldRemapTable == null || chunk.WeldRemapTable.Length < vertexIndex)
		{
			chunk.WeldRemapTable = new int[Math.Max(vertexIndex, chunk.WeldRemapTable != null ? chunk.WeldRemapTable.Length * 2 : 1024)];
		}

		var vertexMap = chunk.WeldVertexMap;
		var remapTable = chunk.WeldRemapTable;
		int uniqueVertexCount = 0;

		for (int v = 0; v < vertexIndex; v++)
		{
			int texIdx = v * 4;
			TerrainVertexKey key = new TerrainVertexKey(
				chunk.VerticesCache[v],
				chunk.NormalsCache[v],
				chunk.UvsCache[v],
				chunk.ColorsCache[v],
				chunk.TexIndicesCache[texIdx],
				chunk.TexIndicesCache[texIdx + 1],
				chunk.TexIndicesCache[texIdx + 2],
				chunk.TexIndicesCache[texIdx + 3],
				chunk.TexWeightsCache01[texIdx],
				chunk.TexWeightsCache01[texIdx + 1],
				chunk.TexWeightsCache01[texIdx + 2],
				chunk.TexWeightsCache01[texIdx + 3],
				chunk.CliffIndicesCache[texIdx],
				chunk.CliffIndicesCache[texIdx + 1],
				chunk.CliffIndicesCache[texIdx + 2],
				chunk.CliffIndicesCache[texIdx + 3],
				chunk.CliffWeightsCache[texIdx],
				chunk.CliffWeightsCache[texIdx + 1],
				chunk.CliffWeightsCache[texIdx + 2],
				chunk.CliffWeightsCache[texIdx + 3]
			);

			if (vertexMap.TryGetValue(key, out int existingIndex))
			{
				remapTable[v] = existingIndex;
			}
			else
			{
				int newIndex = uniqueVertexCount++;
				vertexMap[key] = newIndex;
				remapTable[v] = newIndex;

				if (newIndex != v)
				{
					chunk.VerticesCache[newIndex] = chunk.VerticesCache[v];
					chunk.NormalsCache[newIndex] = chunk.NormalsCache[v];
					chunk.UvsCache[newIndex] = chunk.UvsCache[v];
					chunk.ColorsCache[newIndex] = chunk.ColorsCache[v];

					int newTexIdx = newIndex * 4;
					chunk.TexIndicesCache[newTexIdx] = chunk.TexIndicesCache[texIdx];
					chunk.TexIndicesCache[newTexIdx + 1] = chunk.TexIndicesCache[texIdx + 1];
					chunk.TexIndicesCache[newTexIdx + 2] = chunk.TexIndicesCache[texIdx + 2];
					chunk.TexIndicesCache[newTexIdx + 3] = chunk.TexIndicesCache[texIdx + 3];

					chunk.TexWeightsCache01[newTexIdx] = chunk.TexWeightsCache01[texIdx];
					chunk.TexWeightsCache01[newTexIdx + 1] = chunk.TexWeightsCache01[texIdx + 1];
					chunk.TexWeightsCache01[newTexIdx + 2] = chunk.TexWeightsCache01[texIdx + 2];
					chunk.TexWeightsCache01[newTexIdx + 3] = chunk.TexWeightsCache01[texIdx + 3];

					chunk.CliffIndicesCache[newTexIdx] = chunk.CliffIndicesCache[texIdx];
					chunk.CliffIndicesCache[newTexIdx + 1] = chunk.CliffIndicesCache[texIdx + 1];
					chunk.CliffIndicesCache[newTexIdx + 2] = chunk.CliffIndicesCache[texIdx + 2];
					chunk.CliffIndicesCache[newTexIdx + 3] = chunk.CliffIndicesCache[texIdx + 3];

					chunk.CliffWeightsCache[newTexIdx] = chunk.CliffWeightsCache[texIdx];
					chunk.CliffWeightsCache[newTexIdx + 1] = chunk.CliffWeightsCache[texIdx + 1];
					chunk.CliffWeightsCache[newTexIdx + 2] = chunk.CliffWeightsCache[texIdx + 2];
					chunk.CliffWeightsCache[newTexIdx + 3] = chunk.CliffWeightsCache[texIdx + 3];
				}
			}
		}

		for (int i = 0; i < indexIndex; i++)
		{
			chunk.IndicesCache[i] = remapTable[chunk.IndicesCache[i]];
		}

		vertexIndex = uniqueVertexCount;
	}

	public void BakeNavMesh()
	{
		GameHost tempQualifier = GameHost.Instance;
		var world = (tempQualifier != null ? GameContext.World : null);
		var worldEntity = GameHost.Instance != null ? ECSContext.WorldEntity : default;
		if (world != null && world.IsAlive(worldEntity) && world.Has<TerrainState>(worldEntity))
		{
			ref var state = ref world.Get<TerrainState>(worldEntity);
			var terrainNavMeshService = ECSContext.TerrainNavMeshService;
			terrainNavMeshService.BakeNavMesh(ref state);
			world.Set(worldEntity, state);
		}
	}

	public void GetHeightAndNormal(float worldX, float worldZ, out float height, out Vector3 normal)
	{
		var cells = Cells;
		if (cells == null)
		{
			height = 0.0f;
			normal = Vector3.Up;
			return;
		}

		int cellW = cells.GetLength(0);
		int cellD = cells.GetLength(1);
		if (cellW <= 0 || cellD <= 0)
		{
			height = 0.0f;
			normal = Vector3.Up;
			return;
		}

		float halfW = (cellW / 2.0f) * QuadSize;
		float halfD = (cellD / 2.0f) * QuadSize;

		float localX = (worldX + halfW) / QuadSize;
		float localZ = (worldZ + halfD) / QuadSize;

		int x0 = Math.Clamp((int)MathF.Floor(localX), 0, cellW - 1);
		int z0 = Math.Clamp((int)MathF.Floor(localZ), 0, cellD - 1);

		float tx = Math.Clamp(localX - x0, 0.0f, 1.0f);
		float tz = Math.Clamp(localZ - z0, 0.0f, 1.0f);

		var cell = cells[x0, z0];
		float h00 = cell.Y_NW;
		float h10 = cell.Y_NE;
		float h01 = cell.Y_SW;
		float h11 = cell.Y_SE;
		height = (1 - tx) * (1 - tz) * h00 + tx * (1 - tz) * h10 + (1 - tx) * tz * h01 + tx * tz * h11;
		Vector3 n00 = GetVertexNormal(x0, z0);
		Vector3 n10 = GetVertexNormal(x0 + 1, z0);
		Vector3 n01 = GetVertexNormal(x0, z0 + 1);
		Vector3 n11 = GetVertexNormal(x0 + 1, z0 + 1);
		normal = ((1 - tx) * (1 - tz) * n00 + tx * (1 - tz) * n10 + (1 - tx) * tz * n01 + tx * tz * n11).Normalized();
	}

	public static Vector3 GetVertexNormal(int x, int z, TerrainCell[,] cells, int w, int d, float quadSize)
	{
		if (cells == null || w <= 0 || d <= 0) return Vector3.Up;

		float dx = CalculateNormalDeltaX(x, z, cells, w, d, quadSize);
		float dz = CalculateNormalDeltaZ(x, z, cells, w, d, quadSize);

		if (Math.Abs(dx) < 0.0001f && Math.Abs(dz) < 0.0001f)
		{
			return Vector3.Up;
		}

		Vector3 tangentX = new Vector3(quadSize, dx * quadSize, 0.0f).Normalized();
		Vector3 tangentZ = new Vector3(0.0f, dz * quadSize, quadSize).Normalized();
		return tangentZ.Cross(tangentX).Normalized();
	}

	private static float CalculateNormalDeltaX(int x, int z, TerrainCell[,] cells, int w, int d, float quadSize)
	{
		if (x > 0 && x < w)
		{
			return (GetGridNodeHeight(x + 1, z, cells, w, d) - GetGridNodeHeight(x - 1, z, cells, w, d)) / (2.0f * quadSize);
		}

		if (x < w)
		{
			return (GetGridNodeHeight(x + 1, z, cells, w, d) - GetGridNodeHeight(x, z, cells, w, d)) / quadSize;
		}

		if (x > 0)
		{
			return (GetGridNodeHeight(x, z, cells, w, d) - GetGridNodeHeight(x - 1, z, cells, w, d)) / quadSize;
		}

		return 0.0f;
	}

	private static float CalculateNormalDeltaZ(int x, int z, TerrainCell[,] cells, int w, int d, float quadSize)
	{
		if (z > 0 && z < d)
		{
			return (GetGridNodeHeight(x, z + 1, cells, w, d) - GetGridNodeHeight(x, z - 1, cells, w, d)) / (2.0f * quadSize);
		}

		if (z < d)
		{
			return (GetGridNodeHeight(x, z + 1, cells, w, d) - GetGridNodeHeight(x, z, cells, w, d)) / quadSize;
		}

		if (z > 0)
		{
			return (GetGridNodeHeight(x, z, cells, w, d) - GetGridNodeHeight(x, z - 1, cells, w, d)) / quadSize;
		}

		return 0.0f;
	}

	public Vector3 GetVertexNormal(int x, int z)
	{
		return GetVertexNormal(x, z, Cells, Width, Depth, QuadSize);
	}
}