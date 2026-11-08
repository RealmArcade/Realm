using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using Realm.Client.Core;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;
using Realm.Shared.Textures;

namespace Realm.Client.Services;

/// <summary>
/// One terrain edit in progress: the block and plateau targets being painted towards, the before-images the
/// undo action is built from, and the draw bounds that action covers. It owns that state for the whole drag
/// and writes through <see cref="EditorTerrainSurface"/>, so the editor itself only starts, feeds and ends it.
/// </summary>
internal sealed class EditorTerrainEditSession
{
	private readonly EditorTerrainSurface _surface;
	private readonly Dictionary<Vector2I, long> _paintCoordLastTimeMs = new();

	private bool _hasBlockTargetHeight;
	private float _activeBlockTargetHeight;
	private WaterType _activeBlockTargetWaterMode = WaterType.None;
	private byte _activeBlockTargetWaterProfile = 0;
	private float _activeBlockTargetWaterHeight = 0f;
	private float? _activePlateauHeight;
	private WaterType _activePlateauWaterMode = WaterType.None;
	private byte _activePlateauWaterProfile = 0;
	private float _activePlateauWaterHeight = 0f;

	private TerrainCell[,]? _terrainCellsBefore;
	private TerrainSplatWeights[,]? _terrainSplatMapBefore;
	private TerrainSplatWeights[,]? _terrainCliffSplatMapBefore;
	private int[,]? _terrainPathingBefore;
	private bool _isDrawing;
	private int _drawMinX;
	private int _drawMinZ;
	private int _drawMaxX;
	private int _drawMaxZ;

	public EditorTerrainEditSession(EditorTerrainSurface surface)
	{
		_surface = surface;
	}

	public bool IsDrawing => _isDrawing;

	public void ResetPaintThrottle() => _paintCoordLastTimeMs.Clear();

	public static bool IsHeightTool(EditorTool tool)
	{
		return tool == EditorTool.Raise || tool == EditorTool.Lower ||
			   tool == EditorTool.Height || tool == EditorTool.Smooth ||
			   tool == EditorTool.Plateau || tool == EditorTool.Noise;
	}

	public void ApplyBlock(ref TerrainState terrain, ref ContinuousEditContext ctx, float blockLevelHeight)
	{
		if (ctx.ActiveTool == EditorTool.PaintTexture)
		{
			ProcessBlockPaint(ref terrain, ref ctx);
			return;
		}

		int cx = Mathf.Clamp((int)Math.Floor(ctx.WorldPos.X / ctx.QuadSize + ctx.Width / 2.0f), 0, ctx.Width - 1);
		int cz = Mathf.Clamp((int)Math.Floor(ctx.WorldPos.Z / ctx.QuadSize + ctx.Depth / 2.0f), 0, ctx.Depth - 1);
		int N = Mathf.Max(1, (int)Mathf.Round(ctx.BrushRadius));
		int quadMinX = cx - (N - 1) / 2;
		int quadMaxX = cx + N / 2;
		int quadMinZ = cz - (N - 1) / 2;
		int quadMaxZ = cz + N / 2;

		if (ctx.IsHeights) InitializeBlockHeights(ctx.ActiveTool, ctx.WorldPos, blockLevelHeight);

		float targetHeight = Math.Clamp(_activeBlockTargetHeight, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
		sbyte targetMacroTier = (sbyte)Math.Clamp((int)MathF.Round(targetHeight / TerrainCell.TIER_HEIGHT), TerrainCell.MIN_MACRO_TIER, TerrainCell.MAX_MACRO_TIER);

		for (int z = quadMinZ; z <= quadMaxZ; z++)
		{
			for (int x = quadMinX; x <= quadMaxX; x++)
			{
				if (x >= 0 && x < ctx.Width && z >= 0 && z < ctx.Depth)
				{
					if (IsPointInBrush(x, z, quadMinX, quadMinZ, N, ctx.BrushIsSquare))
					{
						ProcessBlockCell(ref terrain, ref ctx, x, z, targetHeight, targetMacroTier);
					}
				}
			}
		}
	}

	public void ApplyContinuous(ref TerrainState terrain, ref ContinuousEditContext ctx)
	{
		int minGX = Math.Clamp((int)Math.Floor((ctx.WorldPos.X - ctx.BrushRadius) / ctx.QuadSize + ctx.Width / 2.0f), 0, ctx.Width);
		int maxGX = Math.Clamp((int)Math.Ceiling((ctx.WorldPos.X + ctx.BrushRadius) / ctx.QuadSize + ctx.Width / 2.0f), 0, ctx.Width);
		int minGZ = Math.Clamp((int)Math.Floor((ctx.WorldPos.Z - ctx.BrushRadius) / ctx.QuadSize + ctx.Depth / 2.0f), 0, ctx.Depth);
		int maxGZ = Math.Clamp((int)Math.Ceiling((ctx.WorldPos.Z + ctx.BrushRadius) / ctx.QuadSize + ctx.Depth / 2.0f), 0, ctx.Depth);

		for (int z = minGZ; z <= maxGZ; z++)
		{
			for (int x = minGX; x <= maxGX; x++)
			{
				float dist = CalculateBrushDistance(x, z, ref ctx);
				if (dist <= ctx.BrushRadius)
				{
					ProcessContinuousCell(ref terrain, ref ctx, x, z, dist);
				}
			}
		}
	}

	public TerrainEditResult Finalize(ContinuousEditContext ctx, bool isHeights, bool isPaint, bool isPathing)
	{
		var result = new TerrainEditResult { PathingModified = ctx.PathingModified };
		if (!ctx.Modified) return result;

		result.HeightsModified = isHeights;
		result.SplatModified = isPaint || (isHeights && ctx.ActiveTool != EditorTool.Smooth && ctx.ActiveTool != EditorTool.Noise);
		result.PathingModified = result.PathingModified || isPathing;

		if (ctx.ModMinX < _drawMinX) _drawMinX = ctx.ModMinX;
		if (ctx.ModMaxX > _drawMaxX) _drawMaxX = ctx.ModMaxX;
		if (ctx.ModMinZ < _drawMinZ) _drawMinZ = ctx.ModMinZ;
		if (ctx.ModMaxZ > _drawMaxZ) _drawMaxZ = ctx.ModMaxZ;

		result.MinX = ctx.ModMinX;
		result.MinZ = ctx.ModMinZ;
		result.MaxX = ctx.ModMaxX;
		result.MaxZ = ctx.ModMaxZ;
		if (result.SplatModified) _surface.AlignSplatMapSlots(ctx.ModMinX - 2, ctx.ModMinZ - 2, ctx.ModMaxX + 2, ctx.ModMaxZ + 2);

		return result;
	}

	public void BeginDraw(
		Godot.Vector3 hitPos,
		EditorTool activeTool,
		bool blockMode,
		float blockLevelHeight,
		float[,]? currentHeights,
		TerrainSplatWeights[,]? currentSplatMap,
		int[,]? currentPathing,
		TerrainSplatWeights[,]? currentCliffSplatMap)
	{
		ref var terrain = ref EditorService.GetTerrainState();
		_isDrawing = true;
		_drawMinX = int.MaxValue;
		_drawMinZ = int.MaxValue;
		_drawMaxX = -1;
		_drawMaxZ = -1;
		_terrainCellsBefore = terrain.Cells != null ? (TerrainCell[,])terrain.Cells.Clone() : null;
		_terrainSplatMapBefore = currentSplatMap != null ? (TerrainSplatWeights[,])currentSplatMap.Clone() : null;
		_terrainCliffSplatMapBefore = currentCliffSplatMap != null ? (TerrainSplatWeights[,])currentCliffSplatMap.Clone() : null;
		_terrainPathingBefore = currentPathing != null ? (int[,])currentPathing.Clone() : null;

		float startHeight = EditorTerrainQueries.GetHeightAt(hitPos);

		if (activeTool == EditorTool.Plateau)
		{
			_activePlateauHeight = blockMode ? (float)MathF.Round(startHeight / TerrainCell.TIER_HEIGHT) * TerrainCell.TIER_HEIGHT : startHeight;
			_activePlateauWaterMode = WaterModeAt(hitPos);
			_activePlateauWaterProfile = WaterProfileIndexAt(hitPos);
			_activePlateauWaterHeight = WaterHeightAt(hitPos);
		}

		if (blockMode || activeTool == EditorTool.Height)
		{
			SetBlockTargetHeight(activeTool, startHeight, blockLevelHeight, hitPos);
		}
	}

	public TerrainModifyAction EndDraw(
		float[,]? currentHeights,
		TerrainSplatWeights[,]? currentSplatMap,
		int[,]? currentPathing,
		TerrainSplatWeights[,]? currentCliffSplatMap)
	{
		_isDrawing = false;
		_hasBlockTargetHeight = false;
		_activePlateauHeight = null;
		_activePlateauWaterMode = WaterType.None;
		_activePlateauWaterProfile = 0;
		_activeBlockTargetWaterMode = WaterType.None;
		_activeBlockTargetWaterProfile = 0;

		ref var terrain = ref EditorService.GetTerrainState();
		var currentCells = terrain.Cells;

		if (_drawMinX <= _drawMaxX && _drawMinZ <= _drawMaxZ)
		{
			bool cellsChanged = false;
			bool pathingChanged = false;
			bool splatChanged = false;

			int w = _drawMaxX - _drawMinX + 1;
			int d = _drawMaxZ - _drawMinZ + 1;

			if (_terrainCellsBefore != null && currentCells != null)
			{
				for (int z = 0; z < d && !cellsChanged; z++)
				{
					for (int x = 0; x < w && !cellsChanged; x++)
					{
						int mapX = _drawMinX + x;
						int mapZ = _drawMinZ + z;
						if (mapX < _terrainCellsBefore.GetLength(0) && mapZ < _terrainCellsBefore.GetLength(1) &&
							mapX < currentCells.GetLength(0) && mapZ < currentCells.GetLength(1))
						{
							var b = _terrainCellsBefore[mapX, mapZ];
							var a = currentCells[mapX, mapZ];
							if (b.Y_NW != a.Y_NW || b.Y_NE != a.Y_NE || b.Y_SE != a.Y_SE || b.Y_SW != a.Y_SW || b.WaterMode != a.WaterMode || b.WaterProfileIndex != a.WaterProfileIndex)
							{
								cellsChanged = true;
							}
						}
					}
				}
			}

			if (_terrainPathingBefore != null && currentPathing != null)
			{
				for (int z = 0; z < d && !pathingChanged; z++)
				{
					for (int x = 0; x < w && !pathingChanged; x++)
					{
						int mapX = _drawMinX + x;
						int mapZ = _drawMinZ + z;
						if (mapX < _terrainPathingBefore.GetLength(0) && mapZ < _terrainPathingBefore.GetLength(1) &&
							mapX < currentPathing.GetLength(0) && mapZ < currentPathing.GetLength(1))
						{
							if (_terrainPathingBefore[mapX, mapZ] != currentPathing[mapX, mapZ])
							{
								pathingChanged = true;
							}
						}
					}
				}
			}

			int splatW = currentSplatMap != null ? currentSplatMap.GetLength(0) : terrain.Width + 1;
			int splatD = currentSplatMap != null ? currentSplatMap.GetLength(1) : terrain.Depth + 1;
			int splatNodeW = Math.Min(w + 1, splatW - _drawMinX);
			int splatNodeD = Math.Min(d + 1, splatD - _drawMinZ);

			if (_terrainSplatMapBefore != null && currentSplatMap != null)
			{
				for (int z = 0; z < splatNodeD && !splatChanged; z++)
				{
					for (int x = 0; x < splatNodeW && !splatChanged; x++)
					{
						int mapX = _drawMinX + x;
						int mapZ = _drawMinZ + z;
						if (mapX < _terrainSplatMapBefore.GetLength(0) && mapZ < _terrainSplatMapBefore.GetLength(1) &&
							mapX < currentSplatMap.GetLength(0) && mapZ < currentSplatMap.GetLength(1))
						{
							var b = _terrainSplatMapBefore[mapX, mapZ];
							var a = currentSplatMap[mapX, mapZ];
							if (b.Index0 != a.Index0 || b.Index1 != a.Index1 || b.Index2 != a.Index2 || b.Index3 != a.Index3 ||
								b.Weight0 != a.Weight0 || b.Weight1 != a.Weight1 || b.Weight2 != a.Weight2 || b.Weight3 != a.Weight3)
							{
								splatChanged = true;
							}
						}
					}
				}
			}

			if (!splatChanged && _terrainCliffSplatMapBefore != null && currentCliffSplatMap != null)
			{
				for (int z = 0; z < splatNodeD && !splatChanged; z++)
				{
					for (int x = 0; x < splatNodeW && !splatChanged; x++)
					{
						int mapX = _drawMinX + x;
						int mapZ = _drawMinZ + z;
						if (mapX < _terrainCliffSplatMapBefore.GetLength(0) && mapZ < _terrainCliffSplatMapBefore.GetLength(1) &&
							mapX < currentCliffSplatMap.GetLength(0) && mapZ < currentCliffSplatMap.GetLength(1))
						{
							var b = _terrainCliffSplatMapBefore[mapX, mapZ];
							var a = currentCliffSplatMap[mapX, mapZ];
							if (b.Index0 != a.Index0 || b.Index1 != a.Index1 || b.Index2 != a.Index2 || b.Index3 != a.Index3 ||
								b.Weight0 != a.Weight0 || b.Weight1 != a.Weight1 || b.Weight2 != a.Weight2 || b.Weight3 != a.Weight3)
							{
								splatChanged = true;
							}
						}
					}
				}
			}

			TerrainCell[,]? beforeC = null;
			TerrainCell[,]? afterC = null;
			if (cellsChanged)
			{
				beforeC = new TerrainCell[w, d];
				afterC = new TerrainCell[w, d];
				for (int z = 0; z < d; z++)
				{
					for (int x = 0; x < w; x++)
					{
						int mapX = _drawMinX + x;
						int mapZ = _drawMinZ + z;
						if (_terrainCellsBefore != null && mapX < _terrainCellsBefore.GetLength(0) && mapZ < _terrainCellsBefore.GetLength(1))
						{
							beforeC[x, z] = _terrainCellsBefore[mapX, mapZ];
						}

						if (currentCells != null && mapX < currentCells.GetLength(0) && mapZ < currentCells.GetLength(1))
						{
							afterC[x, z] = currentCells[mapX, mapZ];
						}
					}
				}
			}

			int[,]? beforeP = null;
			int[,]? afterP = null;
			if (pathingChanged)
			{
				beforeP = new int[w, d];
				afterP = new int[w, d];
				for (int z = 0; z < d; z++)
				{
					for (int x = 0; x < w; x++)
					{
						int mapX = _drawMinX + x;
						int mapZ = _drawMinZ + z;
						if (_terrainPathingBefore != null && mapX < _terrainPathingBefore.GetLength(0) && mapZ < _terrainPathingBefore.GetLength(1))
						{
							beforeP[x, z] = _terrainPathingBefore[mapX, mapZ];
						}

						if (currentPathing != null && mapX < currentPathing.GetLength(0) && mapZ < currentPathing.GetLength(1))
						{
							afterP[x, z] = currentPathing[mapX, mapZ];
						}
					}
				}
			}

			TerrainSplatWeights[,]? beforeS = null;
			TerrainSplatWeights[,]? afterS = null;
			TerrainSplatWeights[,]? beforeCliffS = null;
			TerrainSplatWeights[,]? afterCliffS = null;
			if (splatChanged)
			{
				if (currentSplatMap != null)
				{
					beforeS = new TerrainSplatWeights[splatNodeW, splatNodeD];
					afterS = new TerrainSplatWeights[splatNodeW, splatNodeD];
					for (int z = 0; z < splatNodeD; z++)
					{
						for (int x = 0; x < splatNodeW; x++)
						{
							int mapX = _drawMinX + x;
							int mapZ = _drawMinZ + z;
							if (_terrainSplatMapBefore != null && mapX < _terrainSplatMapBefore.GetLength(0) && mapZ < _terrainSplatMapBefore.GetLength(1))
							{
								beforeS[x, z] = _terrainSplatMapBefore[mapX, mapZ];
							}

							if (mapX < currentSplatMap.GetLength(0) && mapZ < currentSplatMap.GetLength(1))
							{
								afterS[x, z] = currentSplatMap[mapX, mapZ];
							}
						}
					}
				}

				if (currentCliffSplatMap != null)
				{
					beforeCliffS = new TerrainSplatWeights[splatNodeW, splatNodeD];
					afterCliffS = new TerrainSplatWeights[splatNodeW, splatNodeD];
					for (int z = 0; z < splatNodeD; z++)
					{
						for (int x = 0; x < splatNodeW; x++)
						{
							int mapX = _drawMinX + x;
							int mapZ = _drawMinZ + z;
							if (_terrainCliffSplatMapBefore != null && mapX < _terrainCliffSplatMapBefore.GetLength(0) && mapZ < _terrainCliffSplatMapBefore.GetLength(1))
							{
								beforeCliffS[x, z] = _terrainCliffSplatMapBefore[mapX, mapZ];
							}

							if (mapX < currentCliffSplatMap.GetLength(0) && mapZ < currentCliffSplatMap.GetLength(1))
							{
								afterCliffS[x, z] = currentCliffSplatMap[mapX, mapZ];
							}
						}
					}
				}
			}

			return new TerrainModifyAction(_drawMinX, _drawMinZ, w, d, beforeC, afterC, beforeS, afterS, beforeP, afterP, beforeCliffS, afterCliffS);
		}

		return new TerrainModifyAction(
			_terrainCellsBefore, currentCells,
			_terrainSplatMapBefore, currentSplatMap,
			_terrainPathingBefore, currentPathing,
			_terrainCliffSplatMapBefore, currentCliffSplatMap);
	}

	public void ResetDrawState()
	{
		_hasBlockTargetHeight = false;
		_activePlateauHeight = null;
		_activePlateauWaterMode = WaterType.None;
		_activeBlockTargetWaterMode = WaterType.None;
	}

	/// <summary>Drops the whole edit, including the target height a reloaded map must not inherit.</summary>
	public void ResetAll()
	{
		ResetDrawState();
		_activeBlockTargetHeight = 0.0f;
	}

	private bool ShouldPaintCoord(int x, int z, long nowMs)
	{
		Vector2I coord = new Vector2I(x, z);
		if (_paintCoordLastTimeMs.TryGetValue(coord, out long lastTime))
		{
			if (nowMs - lastTime < 250)
			{
				return false;
			}
		}

		_paintCoordLastTimeMs[coord] = nowMs;
		return true;
	}

	private void InitializeBlockHeights(EditorTool tool, Godot.Vector3 worldPos, float blockLevelHeight)
	{
		if (_hasBlockTargetHeight) return;
		float startHeight = EditorTerrainQueries.GetHeightAt(worldPos);
		if (tool == EditorTool.Height) _activeBlockTargetHeight = blockLevelHeight;
		else if (tool == EditorTool.Raise) _activeBlockTargetHeight = startHeight + blockLevelHeight;
		else if (tool == EditorTool.Lower) _activeBlockTargetHeight = startHeight - blockLevelHeight;
		else if (tool == EditorTool.Plateau)
		{
			_activeBlockTargetHeight = (float)MathF.Round(startHeight / TerrainCell.TIER_HEIGHT) * TerrainCell.TIER_HEIGHT;
			_activeBlockTargetWaterMode = WaterModeAt(worldPos);
			_activeBlockTargetWaterProfile = WaterProfileIndexAt(worldPos);
			_activeBlockTargetWaterHeight = WaterHeightAt(worldPos);
		}

		_activeBlockTargetHeight = Math.Clamp(_activeBlockTargetHeight, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
		_hasBlockTargetHeight = true;
	}

	private static bool IsPointInBrush(int x, int z, int quadMinX, int quadMinZ, int N, bool brushIsSquare)
	{
		if (brushIsSquare) return true;
		float dx = (x + 0.5f) - (quadMinX + N / 2.0f);
		float dz = (z + 0.5f) - (quadMinZ + N / 2.0f);
		return (dx * dx + dz * dz) <= (N / 2.0f * N / 2.0f);
	}

	private void ProcessBlockCell(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float targetHeight, sbyte targetMacroTier)
	{
		if (ctx.IsHeights) ProcessBlockHeights(ref terrain, ref ctx, x, z, targetHeight, targetMacroTier);
		else if (ctx.ActiveTool == EditorTool.PaintPathing) ProcessBlockPathing(ref terrain, ref ctx, x, z);
	}

	private void ProcessBlockHeights(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float targetHeight, sbyte targetMacroTier)
	{
		if (ctx.ActiveTool == EditorTool.Smooth)
		{
			ApplyBlockSmooth(ref terrain, ref ctx, x, z);
			return;
		}

		ref var cell = ref terrain.Cells![x, z];
		bool waterChanged = UpdateCellWater(ref cell, ctx.ActiveTool);

		if (MathF.Abs(cell.CenterHeight - targetHeight) > 0.001f || waterChanged)
		{
			UpdateBlockCellHeightAndPathing(ref terrain, ref ctx, x, z, targetHeight, targetMacroTier, ref cell);
			ApplyBlockTextures(ref terrain, ref ctx, x, z, targetHeight);
			UpdateContextBounds(ref ctx, x, z);
		}
	}

	private bool UpdateCellWater(ref TerrainCell cell, EditorTool tool)
	{
		if (tool == EditorTool.Raise || tool == EditorTool.Height)
		{
			if (cell.WaterMode == WaterType.None) return false;
			cell.WaterMode = WaterType.None;
			cell.WaterHeight = 0f;
			return true;
		}

		if (tool == EditorTool.Plateau)
		{
			if (cell.WaterMode == _activeBlockTargetWaterMode && cell.WaterProfileIndex == _activeBlockTargetWaterProfile && cell.WaterHeight == _activeBlockTargetWaterHeight) return false;
			cell.WaterMode = _activeBlockTargetWaterMode;
			cell.WaterProfileIndex = _activeBlockTargetWaterProfile;
			cell.WaterHeight = _activeBlockTargetWaterHeight;
			return true;
		}

		return false;
	}

	private void UpdateBlockCellHeightAndPathing(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float targetHeight, sbyte targetMacroTier, ref TerrainCell cell)
	{
		if (terrain.PathingCodes != null)
		{
			if (cell.WaterMode != WaterType.None)
			{
				var waterProf = GameContext.Terrain?.GetWaterProfile(cell.WaterProfileIndex);
				terrain.PathingCodes[x, z] = waterProf != null ? waterProf.DefaultPathingCode : EditableTerrain.GetDefaultPathingCode(cell.WaterMode);
				ctx.PathingModified = true;
				float wY = (targetMacroTier * TerrainCell.TIER_HEIGHT) + (cell.WaterHeight > 0.001f ? cell.WaterHeight : RuntimeTerrain.WATER_DELTA);
				var waterSpawns = EditorProceduralSpawns.BuildWater(x, z, cell.WaterProfileIndex, wY, ctx.QuadSize, ctx.Width, ctx.Depth);
				GameContext.Host?.ApplyProceduralSpawns(x, z, CollectionsMarshal.AsSpan(waterSpawns), ProceduralSpawnLayer.Water);
			}
			else
			{
				GameContext.Host?.ClearProceduralSpawns(x, z, ProceduralSpawnLayer.Water);
				if (terrain.PathingCodes[x, z] == EditableTerrain.GetDefaultPathingCode(cell))
				{
					terrain.PathingCodes[x, z] = EditableTerrain.GetDefaultPathingCode(cell);
					ctx.PathingModified = true;
				}
			}
		}

		EditorTerrainGeometry.SetGridNodeHeight(ref terrain, x, z, targetHeight);
		EditorTerrainGeometry.SetGridNodeHeight(ref terrain, x + 1, z, targetHeight);
		EditorTerrainGeometry.SetGridNodeHeight(ref terrain, x + 1, z + 1, targetHeight);
		EditorTerrainGeometry.SetGridNodeHeight(ref terrain, x, z + 1, targetHeight);
	}

	private void ApplyBlockTextures(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float targetHeight)
	{
		if (ctx.ApplyGroundTexture && _surface.SplatMap != null)
		{
			for (int gz = z; gz <= z + 1; gz++)
			for (int gx = x; gx <= x + 1; gx++)
				if (gx >= 0 && gx < _surface.SplatMap.GetLength(0) && gz >= 0 && gz < _surface.SplatMap.GetLength(1))
					_surface.SplatMap[gx, gz] = TerrainSplatWeights.CreateSolid(ctx.PaintTextureIndex);
			UpdatePathingFromMetadata(ref terrain, ref ctx, x, z);
			var terrainSpawns = EditorProceduralSpawns.BuildTerrain(x, z, ctx.PaintTextureIndex, targetHeight, ctx.QuadSize, ctx.Width, ctx.Depth);
			GameContext.Host?.ApplyProceduralSpawns(x, z, CollectionsMarshal.AsSpan(terrainSpawns), ProceduralSpawnLayer.Terrain);
		}

		if (ctx.ApplyCliffTexture && _surface.CliffSplatMap != null)
		{
			for (int nz = z - 1; nz <= z + 2; nz++)
			for (int nx = x - 1; nx <= x + 2; nx++)
				if (nx >= 0 && nx < _surface.CliffSplatMap.GetLength(0) && nz >= 0 && nz < _surface.CliffSplatMap.GetLength(1))
					_surface.CliffSplatMap[nx, nz] = TerrainSplatWeights.CreateSolid(ctx.CliffPaintTextureIndex);
		}
	}

	private void ApplyBlockSmooth(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z)
	{
		int sum = 0, count = 0;
		for (int nz = -1; nz <= 1; nz++)
		{
			for (int nx = -1; nx <= 1; nx++)
			{
				if (x + nx >= 0 && x + nx < ctx.Width && z + nz >= 0 && z + nz < ctx.Depth)
				{
					sum += terrain.Cells![x + nx, z + nz].MacroTier;
					count++;
				}
			}
		}

		sbyte avgMacro = (sbyte)Math.Clamp((int)MathF.Round((float)sum / count), -16, 16);
		float targetH = avgMacro * TerrainCell.TIER_HEIGHT;
		if (MathF.Abs(terrain.Cells![x, z].CenterHeight - targetH) > 0.001f)
		{
			EditorTerrainGeometry.SetGridNodeHeight(ref terrain, x, z, targetH);
			EditorTerrainGeometry.SetGridNodeHeight(ref terrain, x + 1, z, targetH);
			EditorTerrainGeometry.SetGridNodeHeight(ref terrain, x + 1, z + 1, targetH);
			EditorTerrainGeometry.SetGridNodeHeight(ref terrain, x, z + 1, targetH);
			UpdateContextBounds(ref ctx, x, z);
		}
	}

	private void ProcessBlockPaint(ref TerrainState terrain, ref ContinuousEditContext ctx)
	{
		int splatW = _surface.SplatMap!.GetLength(0);
		int splatD = _surface.SplatMap.GetLength(1);
		int intensityLevel = Math.Clamp((int)MathF.Round(ctx.BrushStrength), 0, 10);

		int minPaintX = Math.Clamp((int)Math.Floor((ctx.WorldPos.X - ctx.BrushRadius) / ctx.QuadSize + ctx.Width / 2.0f), 0, splatW - 1);
		int maxPaintX = Math.Clamp((int)Math.Ceiling((ctx.WorldPos.X + ctx.BrushRadius) / ctx.QuadSize + ctx.Width / 2.0f), 0, splatW - 1);
		int minPaintZ = Math.Clamp((int)Math.Floor((ctx.WorldPos.Z - ctx.BrushRadius) / ctx.QuadSize + ctx.Depth / 2.0f), 0, splatD - 1);
		int maxPaintZ = Math.Clamp((int)Math.Ceiling((ctx.WorldPos.Z + ctx.BrushRadius) / ctx.QuadSize + ctx.Depth / 2.0f), 0, splatD - 1);

		for (int pz = minPaintZ; pz <= maxPaintZ; pz++)
		{
			for (int px = minPaintX; px <= maxPaintX; px++)
			{
				if (IsPaintPointInBrush(px, pz, ref ctx) && ShouldPaintCoord(px, pz, ctx.NowMs))
				{
					ApplyPaintToCell(ref terrain, ref ctx, px, pz, intensityLevel);
				}
			}
		}
	}

	private static bool IsPaintPointInBrush(int px, int pz, ref ContinuousEditContext ctx)
	{
		float vx = (px - ctx.Width / 2.0f) * ctx.QuadSize;
		float vz = (pz - ctx.Depth / 2.0f) * ctx.QuadSize;
		if (ctx.BrushIsSquare) return Mathf.Abs(vx - ctx.WorldPos.X) <= ctx.BrushRadius && Mathf.Abs(vz - ctx.WorldPos.Z) <= ctx.BrushRadius;
		return new Vector2(vx - ctx.WorldPos.X, vz - ctx.WorldPos.Z).Length() <= ctx.BrushRadius;
	}

	private void ApplyPaintToCell(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, int intensityLevel)
	{
		if (ctx.ApplyGroundTexture)
		{
			_surface.SplatMap![x, z] = TerrainSplatWeights.PaintVertexWeighted(_surface.SplatMap[x, z], ctx.PaintTextureIndex, intensityLevel);
			UpdatePathingFromMetadata(ref terrain, ref ctx, x, z);
			if (terrain.Cells != null && x < ctx.Width && z < ctx.Depth)
			{
				var terrainSpawns = EditorProceduralSpawns.BuildTerrain(x, z, ctx.PaintTextureIndex, terrain.Cells[x, z].CenterHeight, ctx.QuadSize, ctx.Width, ctx.Depth);
				GameContext.Host?.ApplyProceduralSpawns(x, z, CollectionsMarshal.AsSpan(terrainSpawns), ProceduralSpawnLayer.Terrain);
			}
		}

		if (ctx.ApplyCliffTexture && _surface.CliffSplatMap != null && x < _surface.CliffSplatMap.GetLength(0) && z < _surface.CliffSplatMap.GetLength(1))
		{
			_surface.CliffSplatMap[x, z] = TerrainSplatWeights.PaintVertexWeighted(_surface.CliffSplatMap[x, z], ctx.CliffPaintTextureIndex, intensityLevel);
		}

		UpdateContextBoundsTight(ref ctx, x, z);
	}

	private static void UpdatePathingFromMetadata(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z)
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		if (!string.IsNullOrEmpty(wsPath) && GameContext.MetadataService.TryLoadMetadata(wsPath, out var metaRoot) && metaRoot != null)
		{
			string? swatchName = null;
			var list = GameContext.Host?.GroundTerrain?.LoadedTextureList;
			if (list != null && ctx.PaintTextureIndex >= 0 && ctx.PaintTextureIndex < list.Count)
			{
				swatchName = list[ctx.PaintTextureIndex];
			}

			if (!string.IsNullOrEmpty(swatchName) && terrain.PathingCodes != null && x < ctx.Width && z < ctx.Depth)
			{
				var prof = metaRoot.GetTerrainProfile(swatchName);
				if (prof != null)
				{
					terrain.PathingCodes[x, z] = prof.DefaultPathingCode;
					ctx.PathingModified = true;
				}
			}
		}
	}

	private static void ProcessBlockPathing(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z)
	{
		if (ctx.PathingAdd) terrain.PathingCodes![x, z] |= ctx.PathingMask;
		else terrain.PathingCodes![x, z] &= ~ctx.PathingMask;
		UpdateContextBoundsTight(ref ctx, x, z);
	}

	private static float CalculateBrushDistance(int x, int z, ref ContinuousEditContext ctx)
	{
		float vx = (x - ctx.Width / 2.0f) * ctx.QuadSize;
		float vz = (z - ctx.Depth / 2.0f) * ctx.QuadSize;
		if (ctx.BrushIsSquare) return Mathf.Max(Mathf.Abs(vx - ctx.WorldPos.X), Mathf.Abs(vz - ctx.WorldPos.Z));
		return new Vector2(vx - ctx.WorldPos.X, vz - ctx.WorldPos.Z).Length();
	}

	private void ProcessContinuousCell(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float dist)
	{
		if (ctx.IsHeights)
		{
			float falloff = Mathf.Sin((1.0f - (dist / ctx.BrushRadius)) * Mathf.Pi / 2.0f);
			ProcessContinuousHeight(ref terrain, ref ctx, x, z, falloff, dist);
		}
		else if (ctx.ActiveTool == EditorTool.PaintTexture)
		{
			int splatW = _surface.SplatMap!.GetLength(0);
			int splatD = _surface.SplatMap.GetLength(1);
			if (x >= 0 && x < splatW && z >= 0 && z < splatD && ShouldPaintCoord(x, z, ctx.NowMs))
			{
				int intensityLevel = Math.Clamp((int)MathF.Round(ctx.BrushStrength), 0, 10);
				ApplyPaintToCell(ref terrain, ref ctx, x, z, intensityLevel);
			}
		}
		else if (ctx.ActiveTool == EditorTool.PaintPathing && x < ctx.Width && z < ctx.Depth)
		{
			float vx = (x - ctx.Width / 2.0f) * ctx.QuadSize;
			float vz = (z - ctx.Depth / 2.0f) * ctx.QuadSize;
			int icx = Mathf.Clamp((int)Math.Round(vx / ctx.QuadSize + ctx.Width / 2.0f), 0, ctx.Width - 1);
			int icz = Mathf.Clamp((int)Math.Round(vz / ctx.QuadSize + ctx.Depth / 2.0f), 0, ctx.Depth - 1);
			ProcessBlockPathing(ref terrain, ref ctx, icx, icz);
		}
	}

	private void ProcessContinuousHeight(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float falloff, float dist)
	{
		float oldH = EditorTerrainGeometry.GetGridNodeHeight(in terrain, x, z);
		float newH = CalculateNewContinuousHeight(in terrain, ref ctx, x, z, oldH, falloff, dist);

		if (Mathf.Abs(newH - oldH) > 0.001f)
		{
			if (ctx.ActiveTool == EditorTool.Plateau && terrain.PathingCodes != null)
			{
				int cellX = Math.Clamp(x, 0, ctx.Width - 1);
				int cellZ = Math.Clamp(z, 0, ctx.Depth - 1);
				int defaultPathBefore = EditableTerrain.GetDefaultPathingCode(terrain.Cells![cellX, cellZ]);
				if (terrain.PathingCodes[cellX, cellZ] == defaultPathBefore)
				{
					terrain.PathingCodes[cellX, cellZ] = EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
					ctx.PathingModified = true;
				}
			}

			EditorTerrainGeometry.SetGridNodeHeight(ref terrain, x, z, newH);
			if (ctx.ActiveTool == EditorTool.Raise || ctx.ActiveTool == EditorTool.Lower || ctx.ActiveTool == EditorTool.Plateau)
			{
				ApplyContinuousCliffSplat(ref ctx, x, z);
			}

			UpdateContextBoundsTight(ref ctx, x, z);
		}
	}

	private float CalculateNewContinuousHeight(in TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float oldH, float falloff, float dist)
	{
		if (ctx.ActiveTool == EditorTool.Raise) return Mathf.Clamp(oldH + ctx.BrushStrength * falloff * ctx.Delta, -10.0f, 50.0f);
		if (ctx.ActiveTool == EditorTool.Lower) return Mathf.Clamp(oldH - ctx.BrushStrength * falloff * ctx.Delta, -10.0f, 50.0f);
		if (ctx.ActiveTool == EditorTool.Plateau) return CalculatePlateauHeight(in terrain, ref ctx, x, z, oldH, falloff);
		if (ctx.ActiveTool == EditorTool.Smooth) return CalculateSmoothHeight(in terrain, ref ctx, x, z, oldH, falloff);
		if (ctx.ActiveTool == EditorTool.Noise) return CalculateNoiseHeight(oldH, x, z, dist, ref ctx);
		return oldH;
	}

	private float CalculatePlateauHeight(in TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float oldH, float falloff)
	{
		if (!_activePlateauHeight.HasValue)
		{
			_activePlateauHeight = EditorTerrainQueries.GetHeightAt(ctx.WorldPos);
			_activePlateauWaterMode = WaterModeAt(ctx.WorldPos);
			_activePlateauWaterProfile = WaterProfileIndexAt(ctx.WorldPos);
			_activePlateauWaterHeight = WaterHeightAt(ctx.WorldPos);
		}

		float newH = Mathf.Clamp(Mathf.Lerp(oldH, _activePlateauHeight.Value, falloff), -10.0f, 50.0f);

		int cellX = Math.Clamp(x, 0, ctx.Width - 1);
		int cellZ = Math.Clamp(z, 0, ctx.Depth - 1);
		if (terrain.Cells != null && (terrain.Cells[cellX, cellZ].WaterMode != _activePlateauWaterMode || terrain.Cells[cellX, cellZ].WaterProfileIndex != _activePlateauWaterProfile || terrain.Cells[cellX, cellZ].WaterHeight != _activePlateauWaterHeight))
		{
			terrain.Cells[cellX, cellZ].WaterMode = _activePlateauWaterMode;
			terrain.Cells[cellX, cellZ].WaterProfileIndex = _activePlateauWaterProfile;
			terrain.Cells[cellX, cellZ].WaterHeight = _activePlateauWaterHeight;
			if (terrain.PathingCodes != null)
			{
				var waterProf = GameContext.Terrain?.GetWaterProfile(terrain.Cells[cellX, cellZ].WaterProfileIndex);
				terrain.PathingCodes[cellX, cellZ] = waterProf != null ? waterProf.DefaultPathingCode : EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
				ctx.PathingModified = true;
			}
		}

		return newH;
	}

	private static float CalculateSmoothHeight(in TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float oldH, float falloff)
	{
		float avg = 0f;
		int count = 0;
		for (int nz = -1; nz <= 1; nz++)
		for (int nx = -1; nx <= 1; nx++)
			if (x + nx >= 0 && x + nx <= ctx.Width && z + nz >= 0 && z + nz <= ctx.Depth)
			{
				avg += EditorTerrainGeometry.GetGridNodeHeight(in terrain, x + nx, z + nz);
				count++;
			}

		avg /= count;
		float effectiveStrength = ctx.BrushStrength * 15.0f + 5.0f;
		return Mathf.Clamp(Mathf.Lerp(oldH, avg, Mathf.Clamp(effectiveStrength * falloff * ctx.Delta, 0.0f, 1.0f)), -10.0f, 50.0f);
	}

	private static float CalculateNoiseHeight(float oldH, int x, int z, float dist, ref ContinuousEditContext ctx)
	{
		float dRatio = Mathf.Clamp(dist / ctx.BrushRadius, 0.0f, 1.0f);
		float falloffWeight = 1.0f - (dRatio * dRatio * (3.0f - 2.0f * dRatio));
		float noiseValue = (SimplexNoise.Simplex2D(x * 0.05f, z * 0.05f) * 1.0f + SimplexNoise.Simplex2D(x * 0.2f, z * 0.2f) * 0.35f) / 1.35f;
		return Mathf.Clamp(oldH + noiseValue * (ctx.BrushStrength * 0.25f + 8.25f) * falloffWeight * ctx.Delta * 0.5f, -10.0f, 50.0f);
	}

	private void ApplyContinuousCliffSplat(ref ContinuousEditContext ctx, int x, int z)
	{
		if (_surface.CliffSplatMap == null) return;
		for (int cz = z - 1; cz <= z; cz++)
		{
			for (int cx = x - 1; cx <= x; cx++)
			{
				if (cx >= 0 && cx < ctx.Width && cz >= 0 && cz < ctx.Depth) ApplyCliffSplatToNeighbors(ref ctx, cx, cz);
			}
		}
	}

	private void ApplyCliffSplatToNeighbors(ref ContinuousEditContext ctx, int cx, int cz)
	{
		for (int nz = cz - 1; nz <= cz + 1; nz++)
		{
			for (int nx = cx - 1; nx <= cx + 1; nx++)
			{
				if (nx >= 0 && nx < ctx.Width && nz >= 0 && nz < ctx.Depth) _surface.CliffSplatMap![nx, nz] = TerrainSplatWeights.CreateSolid(ctx.CliffPaintTextureIndex);
			}
		}
	}

	private static void UpdateContextBounds(ref ContinuousEditContext ctx, int x, int z)
	{
		int minX = Math.Max(0, x - 1);
		int maxX = Math.Min(ctx.Width - 1, x + 1);
		int minZ = Math.Max(0, z - 1);
		int maxZ = Math.Min(ctx.Depth - 1, z + 1);
		if (minX < ctx.ModMinX) ctx.ModMinX = minX;
		if (maxX > ctx.ModMaxX) ctx.ModMaxX = maxX;
		if (minZ < ctx.ModMinZ) ctx.ModMinZ = minZ;
		if (maxZ > ctx.ModMaxZ) ctx.ModMaxZ = maxZ;
		ctx.Modified = true;
	}

	private static void UpdateContextBoundsTight(ref ContinuousEditContext ctx, int x, int z)
	{
		if (x < ctx.ModMinX) ctx.ModMinX = x;
		if (x > ctx.ModMaxX) ctx.ModMaxX = x;
		if (z < ctx.ModMinZ) ctx.ModMinZ = z;
		if (z > ctx.ModMaxZ) ctx.ModMaxZ = z;
		ctx.Modified = true;
	}

	private void SetBlockTargetHeight(EditorTool activeTool, float startHeight, float blockLevelHeight, Godot.Vector3 hitPos)
	{
		if (activeTool == EditorTool.Height)
		{
			_activeBlockTargetHeight = Math.Clamp(blockLevelHeight, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
			_hasBlockTargetHeight = true;
		}
		else if (activeTool == EditorTool.Raise)
		{
			_activeBlockTargetHeight = Math.Clamp(startHeight + blockLevelHeight, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
			_hasBlockTargetHeight = true;
		}
		else if (activeTool == EditorTool.Lower)
		{
			_activeBlockTargetHeight = Math.Clamp(startHeight - blockLevelHeight, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
			_hasBlockTargetHeight = true;
		}
		else if (activeTool == EditorTool.Plateau)
		{
			_activeBlockTargetHeight = Math.Clamp((float)MathF.Round(startHeight / TerrainCell.TIER_HEIGHT) * TerrainCell.TIER_HEIGHT, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
			_activeBlockTargetWaterMode = WaterModeAt(hitPos);
			_activeBlockTargetWaterProfile = WaterProfileIndexAt(hitPos);
			_activeBlockTargetWaterHeight = WaterHeightAt(hitPos);
			_hasBlockTargetHeight = true;
		}
	}

	private static WaterType WaterModeAt(Godot.Vector3 worldPos)
	{
		ref var terrain = ref EditorService.GetTerrainState();
		return EditorTerrainQueries.GetWaterModeAt(in terrain, worldPos);
	}

	private static byte WaterProfileIndexAt(Godot.Vector3 worldPos)
	{
		ref var terrain = ref EditorService.GetTerrainState();
		return EditorTerrainQueries.GetWaterProfileIndexAt(in terrain, worldPos);
	}

	private static float WaterHeightAt(Godot.Vector3 worldPos)
	{
		ref var terrain = ref EditorService.GetTerrainState();
		return EditorTerrainQueries.GetWaterHeightAt(in terrain, worldPos);
	}
}
