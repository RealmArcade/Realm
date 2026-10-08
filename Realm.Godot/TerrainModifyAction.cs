using System;
using Godot;
using Realm.Ecs.Components.Terrain;

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
		var existingCells = GameHost.Instance?.GroundTerrain?.Cells;
		return TerrainState.CalculateCells(cellW, cellD, heights, existingCells);
	}

	public void Undo()
	{
		if (GameHost.Instance?.GroundTerrain == null) return;
		var cells = GameHost.Instance.GroundTerrain.Cells;

		bool heightsChanged = _beforeCells != null;
		bool pathingChanged = _beforePathing != null;
		bool splatChanged = _beforeSplatMap != null || _beforeCliffSplatMap != null;

		if (_beforeCells != null && cells != null)
		{
			int cW = Math.Min(_width, _beforeCells.GetLength(0));
			int cD = Math.Min(_depth, _beforeCells.GetLength(1));
			for (int z = 0; z < cD; z++)
				for (int x = 0; x < cW; x++)
					if (_minX + x < cells.GetLength(0) && _minZ + z < cells.GetLength(1))
						cells[_minX + x, _minZ + z] = _beforeCells[x, z];
		}
		if (_beforeSplatMap != null && GameHost.Instance.GroundTerrain.SplatMap != null)
		{
			int sW = _beforeSplatMap.GetLength(0);
			int sD = _beforeSplatMap.GetLength(1);
			for (int z = 0; z < sD; z++)
				for (int x = 0; x < sW; x++)
					if (_minX + x < GameHost.Instance.GroundTerrain.SplatMap.GetLength(0) && _minZ + z < GameHost.Instance.GroundTerrain.SplatMap.GetLength(1))
						GameHost.Instance.GroundTerrain.SplatMap[_minX + x, _minZ + z] = _beforeSplatMap[x, z];
		}
		if (_beforeCliffSplatMap != null && GameHost.Instance.GroundTerrain.CliffSplatMap != null)
		{
			int sW = _beforeCliffSplatMap.GetLength(0);
			int sD = _beforeCliffSplatMap.GetLength(1);
			for (int z = 0; z < sD; z++)
				for (int x = 0; x < sW; x++)
					if (_minX + x < GameHost.Instance.GroundTerrain.CliffSplatMap.GetLength(0) && _minZ + z < GameHost.Instance.GroundTerrain.CliffSplatMap.GetLength(1))
						GameHost.Instance.GroundTerrain.CliffSplatMap[_minX + x, _minZ + z] = _beforeCliffSplatMap[x, z];
		}
		if (_beforePathing != null && GameHost.Instance.GroundTerrain.PathingCodes != null)
		{
			for (int z = 0; z < _depth; z++)
				for (int x = 0; x < _width; x++)
					if (_minX + x < GameHost.Instance.GroundTerrain.PathingCodes.GetLength(0) && _minZ + z < GameHost.Instance.GroundTerrain.PathingCodes.GetLength(1))
						GameHost.Instance.GroundTerrain.PathingCodes[_minX + x, _minZ + z] = _beforePathing[x, z];
		}

		if (splatChanged && GameHost.Instance.GroundTerrain.SplatMap != null)
		{
			ServiceLocator.Get<EditorService>()?.AlignSplatMapSlots(_minX - 2, _minZ - 2, _minX + _width + 2, _minZ + _depth + 2);
		}

		Rect2I affected = new Rect2I(_minX - 2, _minZ - 2, _width + 4, _depth + 4);
		if (heightsChanged)
		{
			GameHost.Instance.AlignAllEntitiesToTerrainExternal(affected);
			GameHost.Instance.RebuildGridOverlayMeshExternal();
		}
		GameHost.Instance.GroundTerrain.UpdateMeshAndPhysics(heightsChanged, false, affected, heightsChanged);
		if (pathingChanged)
		{
			GameHost.Instance.UpdatePathingOverlay();
		}
	}

	public void Redo()
	{
		if (GameHost.Instance?.GroundTerrain == null) return;
		var cells = GameHost.Instance.GroundTerrain.Cells;

		bool heightsChanged = _afterCells != null;
		bool pathingChanged = _afterPathing != null;
		bool splatChanged = _afterSplatMap != null || _afterCliffSplatMap != null;

		if (_afterCells != null && cells != null)
		{
			int cW = Math.Min(_width, _afterCells.GetLength(0));
			int cD = Math.Min(_depth, _afterCells.GetLength(1));
			for (int z = 0; z < cD; z++)
				for (int x = 0; x < cW; x++)
					if (_minX + x < cells.GetLength(0) && _minZ + z < cells.GetLength(1))
						cells[_minX + x, _minZ + z] = _afterCells[x, z];
		}
		if (_afterSplatMap != null && GameHost.Instance.GroundTerrain.SplatMap != null)
		{
			int sW = _afterSplatMap.GetLength(0);
			int sD = _afterSplatMap.GetLength(1);
			for (int z = 0; z < sD; z++)
				for (int x = 0; x < sW; x++)
					if (_minX + x < GameHost.Instance.GroundTerrain.SplatMap.GetLength(0) && _minZ + z < GameHost.Instance.GroundTerrain.SplatMap.GetLength(1))
						GameHost.Instance.GroundTerrain.SplatMap[_minX + x, _minZ + z] = _afterSplatMap[x, z];
		}
		if (_afterCliffSplatMap != null && GameHost.Instance.GroundTerrain.CliffSplatMap != null)
		{
			int sW = _afterCliffSplatMap.GetLength(0);
			int sD = _afterCliffSplatMap.GetLength(1);
			for (int z = 0; z < sD; z++)
				for (int x = 0; x < sW; x++)
					if (_minX + x < GameHost.Instance.GroundTerrain.CliffSplatMap.GetLength(0) && _minZ + z < GameHost.Instance.GroundTerrain.CliffSplatMap.GetLength(1))
						GameHost.Instance.GroundTerrain.CliffSplatMap[_minX + x, _minZ + z] = _afterCliffSplatMap[x, z];
		}
		if (_afterPathing != null && GameHost.Instance.GroundTerrain.PathingCodes != null)
		{
			for (int z = 0; z < _depth; z++)
				for (int x = 0; x < _width; x++)
					if (_minX + x < GameHost.Instance.GroundTerrain.PathingCodes.GetLength(0) && _minZ + z < GameHost.Instance.GroundTerrain.PathingCodes.GetLength(1))
						GameHost.Instance.GroundTerrain.PathingCodes[_minX + x, _minZ + z] = _afterPathing[x, z];
		}

		if (splatChanged && GameHost.Instance.GroundTerrain.SplatMap != null)
		{
			ServiceLocator.Get<EditorService>()?.AlignSplatMapSlots(_minX - 2, _minZ - 2, _minX + _width + 2, _minZ + _depth + 2);
		}

		Rect2I affected = new Rect2I(_minX - 2, _minZ - 2, _width + 4, _depth + 4);
		if (heightsChanged)
		{
			GameHost.Instance.AlignAllEntitiesToTerrainExternal(affected);
			GameHost.Instance.RebuildGridOverlayMeshExternal();
		}
		GameHost.Instance.GroundTerrain.UpdateMeshAndPhysics(heightsChanged, false, affected, heightsChanged);
		if (pathingChanged)
		{
			GameHost.Instance.UpdatePathingOverlay();
		}
	}
}
