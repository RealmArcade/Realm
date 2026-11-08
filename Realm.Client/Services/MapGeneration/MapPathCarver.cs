﻿using System;
using System.Collections.Generic;

namespace Realm.Client.Services.MapGeneration;

/// <summary>
///     Carving the paths between the map's anchors, one bounded step per frame: the minimum spanning tree
///     over the anchors, an A* run per edge with land costlier than open ground, and the brush that turns
///     each route into walkable, must-be-clear ground. It writes into the generator's own grids, so the
///     later stages see the carved map.
/// </summary>
internal sealed class MapPathCarver
{
	private readonly bool[,] _land;
	private readonly bool[,] _path;
	private readonly bool[,] _walkable;
	private readonly int _width;
	private readonly int _depth;
	private readonly int _pathBrushRadius;
	private readonly ProceduralMapGenerator.AnchorCell[] _anchors;

	private readonly List<ProceduralMapGenerator.MstEdge> _mstEdges = new();
	private bool _mstComputed;
	private int _phase;
	private int _edgeIndex;
	private PriorityQueue<int, float>? _astarOpen;
	private float[]? _astarCost;
	private int[]? _astarCameFrom;
	private bool[]? _astarClosed;
	private int _astarStart;
	private int _astarGoal;
	private int _astarSequence;
	private readonly List<List<ProceduralMapGenerator.CellPosition>> _routes = new();
	private List<ProceduralMapGenerator.CellPosition> _currentRoute = new();
	private int _carveRouteIndex;
	private int _carvePointIndex;

	public float Progress { get; private set; }

	public MapPathCarver(
		bool[,] land,
		bool[,] path,
		bool[,] walkable,
		int width,
		int depth,
		ProceduralMapGenerator.AnchorCell[] anchors,
		int pathBrushRadius)
	{
		_land = land;
		_path = path;
		_walkable = walkable;
		_width = width;
		_depth = depth;
		_anchors = anchors;
		_pathBrushRadius = pathBrushRadius;
	}

	/// <summary>One bounded step of the carve; true once every route is carved and the anchors are cleared.</summary>
	public bool Step()
	{
		if (_phase == 0)
		{
			ComputeMst();
			_phase = 1;
			return false;
		}

		if (_phase == 1)
		{
			if (_edgeIndex >= _mstEdges.Count)
			{
				_phase = 2;
				return false;
			}

			ProceduralMapGenerator.MstEdge edge = _mstEdges[_edgeIndex];
			if (_astarOpen == null)
			{
				StartAStar(edge);
			}

			bool finished = StepAStar(750);
			Progress = 0.05f + 0.55f * (_edgeIndex + (finished ? 1f : 0.5f)) / Math.Max(1, _mstEdges.Count);
			if (!finished)
			{
				return false;
			}

			if (_currentRoute.Count > 0)
			{
				_routes.Add(_currentRoute);
			}

			_currentRoute = new List<ProceduralMapGenerator.CellPosition>();
			_astarOpen = null;
			_astarCost = null;
			_astarCameFrom = null;
			_astarClosed = null;
			_edgeIndex++;
			return false;
		}

		if (_phase == 2)
		{
			int carvedThisStep = 0;
			while (_carveRouteIndex < _routes.Count && carvedThisStep < 400)
			{
				List<ProceduralMapGenerator.CellPosition> route = _routes[_carveRouteIndex];
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

			Progress = 0.6f + 0.3f * (_carveRouteIndex / (float)Math.Max(1, _routes.Count));
			if (_carveRouteIndex < _routes.Count)
			{
				return false;
			}

			_phase = 3;
			return false;
		}

		for (int anchorIndex = 0; anchorIndex < _anchors.Length; anchorIndex++)
		{
			MarkWalkableCircle(_anchors[anchorIndex].CellX, _anchors[anchorIndex].CellZ, 10);
		}

		Progress = 1f;
		return true;
	}

	private void ComputeMst()
	{
		if (_mstComputed)
		{
			return;
		}

		_mstComputed = true;
		var inTree = new bool[ProceduralMapGenerator.AnchorCount];
		inTree[0] = true;
		for (int edgeIndex = 0; edgeIndex < ProceduralMapGenerator.AnchorCount - 1; edgeIndex++)
		{
			float bestDistance = float.MaxValue;
			int bestFrom = 0;
			int bestTo = 0;
			for (int fromIndex = 0; fromIndex < ProceduralMapGenerator.AnchorCount; fromIndex++)
			{
				if (!inTree[fromIndex])
				{
					continue;
				}

				for (int toIndex = 0; toIndex < ProceduralMapGenerator.AnchorCount; toIndex++)
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

			_mstEdges.Add(new ProceduralMapGenerator.MstEdge(bestFrom, bestTo));
			inTree[bestTo] = true;
		}
	}

	private void StartAStar(ProceduralMapGenerator.MstEdge edge)
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
		_currentRoute = new List<ProceduralMapGenerator.CellPosition>();
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
			_currentRoute.Add(new ProceduralMapGenerator.CellPosition(node % _width, node / _width));
			node = _astarCameFrom[node];
			if (_currentRoute.Count > _width * _depth)
			{
				break;
			}
		}

		_currentRoute.Reverse();
	}

	private void CarvePathAt(ProceduralMapGenerator.CellPosition position)
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
}
