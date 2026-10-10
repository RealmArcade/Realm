using Godot;
using Realm.Ecs.Components.Terrain;
using Realm.Client.Services;
using System;

namespace Realm.Client;

public class TerrainModifyAction : IEditorAction
{
	private readonly int _minX;
	private readonly int _minZ;
	private readonly int _width;
	private readonly int _depth;

	private readonly TerrainCell[,] _beforeCells;
	private readonly TerrainCell[,] _afterCells;
	private readonly TerrainSplatWeights[,] _beforeSplatMap;
	private readonly TerrainSplatWeights[,] _afterSplatMap;
	private readonly TerrainSplatWeights[,] _beforeCliffSplatMap;
	private readonly TerrainSplatWeights[,] _afterCliffSplatMap;
	private readonly int[,] _beforePathing;
	private readonly int[,] _afterPathing;

	public TerrainModifyAction(
		TerrainCell[,] beforeCells,
		TerrainCell[,] afterCells,
		TerrainSplatWeights[,] beforeSplatMap,
		TerrainSplatWeights[,] afterSplatMap,
		int[,] beforePathing = null,
		int[,] afterPathing = null,
		TerrainSplatWeights[,] beforeCliffSplatMap = null,
		TerrainSplatWeights[,] afterCliffSplatMap = null)
	{
		_beforeCells = beforeCells;
		_afterCells = afterCells;
		_beforeSplatMap = beforeSplatMap;
		_afterSplatMap = afterSplatMap;
		_beforeCliffSplatMap = beforeCliffSplatMap;
		_afterCliffSplatMap = afterCliffSplatMap;
		_beforePathing = beforePathing;
		_afterPathing = afterPathing;
		_minX = 0;
		_minZ = 0;
		if (beforeCells != null)
		{
			_width = beforeCells.GetLength(0);
			_depth = beforeCells.GetLength(1);
		}
		else if (beforeSplatMap != null)
		{
			_width = beforeSplatMap.GetLength(0);
			_depth = beforeSplatMap.GetLength(1);
		}
		else if (beforeCliffSplatMap != null)
		{
			_width = beforeCliffSplatMap.GetLength(0);
			_depth = beforeCliffSplatMap.GetLength(1);
		}
		else if (beforePathing != null)
		{
			_width = beforePathing.GetLength(0);
			_depth = beforePathing.GetLength(1);
		}
	}

	public TerrainModifyAction(
		int minX, int minZ, int width, int depth,
		TerrainCell[,] beforeCells,
		TerrainCell[,] afterCells,
		TerrainSplatWeights[,] beforeSplatMap,
		TerrainSplatWeights[,] afterSplatMap,
		int[,] beforePathing = null,
		int[,] afterPathing = null,
		TerrainSplatWeights[,] beforeCliffSplatMap = null,
		TerrainSplatWeights[,] afterCliffSplatMap = null)
	{
		_minX = minX;
		_minZ = minZ;
		_width = width;
		_depth = depth;
		_beforeCells = beforeCells;
		_afterCells = afterCells;
		_beforeSplatMap = beforeSplatMap;
		_afterSplatMap = afterSplatMap;
		_beforeCliffSplatMap = beforeCliffSplatMap;
		_afterCliffSplatMap = afterCliffSplatMap;
		_beforePathing = beforePathing;
		_afterPathing = afterPathing;
	}

	public TerrainModifyAction(
		float[,] beforeHeights, float[,] afterHeights,
		TerrainSplatWeights[,] beforeSplatMap, TerrainSplatWeights[,] afterSplatMap,
		int[,] beforePathing = null, int[,] afterPathing = null,
		TerrainSplatWeights[,] beforeCliffSplatMap = null, TerrainSplatWeights[,] afterCliffSplatMap = null)
		: this(ConvertHeights(beforeHeights), ConvertHeights(afterHeights), beforeSplatMap, afterSplatMap, beforePathing, afterPathing, beforeCliffSplatMap, afterCliffSplatMap)
	{
	}

	public TerrainModifyAction(
		int minX, int minZ, int width, int depth,
		float[,] beforeHeights, float[,] afterHeights,
		TerrainSplatWeights[,] beforeSplatMap, TerrainSplatWeights[,] afterSplatMap,
		int[,] beforePathing = null, int[,] afterPathing = null,
		TerrainSplatWeights[,] beforeCliffSplatMap = null, TerrainSplatWeights[,] afterCliffSplatMap = null)
		: this(minX, minZ, width, depth, ConvertHeights(beforeHeights), ConvertHeights(afterHeights), beforeSplatMap, afterSplatMap, beforePathing, afterPathing, beforeCliffSplatMap, afterCliffSplatMap)
	{
	}

	private static TerrainCell[,] ConvertHeights(float[,] heights)
	{
		if (heights == null) return null;
		int w = heights.GetLength(0);
		int d = heights.GetLength(1);
		int cellW = Math.Max(1, w - 1);
		int cellD = Math.Max(1, d - 1);
		var existingCells = Realm.Client.Core.GameHost.Instance?.GroundTerrain?.Cells;
		return TerrainState.CalculateCells(cellW, cellD, heights, existingCells);
	}

	public void Undo()
	{
		var host = Realm.Client.Core.GameHost.Instance;
		if (host == null) return;
		var terrain = host.GroundTerrain;
		if (terrain == null) return;

		bool heightsChanged = _beforeCells != null;
		bool pathingChanged = _beforePathing != null;
		bool splatChanged = GetSplatChanged(_beforeSplatMap, _beforeCliffSplatMap);

		ApplyState(_beforeCells, _beforeSplatMap, _beforeCliffSplatMap, _beforePathing);
		UpdateTerrainSystems(heightsChanged, pathingChanged, splatChanged);
	}

	private void ApplyState(
		TerrainCell[,] cells,
		TerrainSplatWeights[,] splatMap,
		TerrainSplatWeights[,] cliffSplatMap,
		int[,] pathingCodes)
	{
		var terrain = Realm.Client.Core.GameHost.Instance.GroundTerrain;
		if (cells != null && terrain.Cells != null)
		{
			ApplyArray(cells, terrain.Cells, Math.Min(_width, cells.GetLength(0)), Math.Min(_depth, cells.GetLength(1)));
		}
		if (splatMap != null && terrain.SplatMap != null)
		{
			ApplyArray(splatMap, terrain.SplatMap, splatMap.GetLength(0), splatMap.GetLength(1));
		}
		if (cliffSplatMap != null && terrain.CliffSplatMap != null)
		{
			ApplyArray(cliffSplatMap, terrain.CliffSplatMap, cliffSplatMap.GetLength(0), cliffSplatMap.GetLength(1));
		}
		if (pathingCodes != null && terrain.PathingCodes != null)
		{
			ApplyArray(pathingCodes, terrain.PathingCodes, _width, _depth);
		}
	}

	private void ApplyArray<T>(T[,] source, T[,] target, int width, int depth)
	{
		for (int z = 0; z < depth; z++)
		{
			ApplyArrayRow(source, target, width, z);
		}
	}

	private void ApplyArrayRow<T>(T[,] source, T[,] target, int width, int z)
	{
		for (int x = 0; x < width; x++)
		{
			if (_minX + x < target.GetLength(0) && _minZ + z < target.GetLength(1))
			{
				target[_minX + x, _minZ + z] = source[x, z];
			}
		}
	}

	private void UpdateTerrainSystems(bool heightsChanged, bool pathingChanged, bool splatChanged)
	{
		var terrain = Realm.Client.Core.GameHost.Instance.GroundTerrain;
		if (splatChanged && terrain.SplatMap != null)
		{
			var editorService = ServiceLocator.Get<EditorService>();
			if (editorService != null)
			{
				editorService.AlignSplatMapSlots(_minX - 2, _minZ - 2, _minX + _width + 2, _minZ + _depth + 2);
			}
		}

		Rect2I affected = new Rect2I(_minX - 2, _minZ - 2, _width + 4, _depth + 4);
		if (heightsChanged)
		{
			Realm.Client.Core.GameHost.Instance.AlignAllEntitiesToTerrainExternal(affected);
			Realm.Client.Core.GameHost.Instance.RebuildGridOverlayMeshExternal();
		}
		terrain.UpdateMeshAndPhysics(heightsChanged, false, affected, heightsChanged);
		if (pathingChanged)
		{
			Realm.Client.Core.GameHost.Instance.UpdatePathingOverlay();
		}
	}

	private bool GetSplatChanged(TerrainSplatWeights[,] splatMap, TerrainSplatWeights[,] cliffSplatMap)
	{
		return splatMap != null || cliffSplatMap != null;
	}

	public void Redo()
	{
		var host = Realm.Client.Core.GameHost.Instance;
		if (host == null) return;
		var terrain = host.GroundTerrain;
		if (terrain == null) return;

		bool heightsChanged = _afterCells != null;
		bool pathingChanged = _afterPathing != null;
		bool splatChanged = GetSplatChanged(_afterSplatMap, _afterCliffSplatMap);

		ApplyState(_afterCells, _afterSplatMap, _afterCliffSplatMap, _afterPathing);
		UpdateTerrainSystems(heightsChanged, pathingChanged, splatChanged);
	}
}