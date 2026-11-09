using System;
using System.Collections.Generic;

namespace Realm.Client.Services.MapGeneration;

/// <summary>
///     How far every cell is from the carved paths, by breadth-first distance from the walkable cells: the
///     scatter stages read it to keep trees and decorations off the roads and the anchor clearings.
/// </summary>
public sealed class PathDistanceField
{
	private readonly bool[,] _walkable;
	private readonly int[,] _distance;
	private readonly int _width;
	private readonly int _depth;
	private readonly Queue<int> _distanceQueue = new();

	private int _distanceRow;
	private bool _distanceInitialized;

	public float Progress { get; private set; }

	public PathDistanceField(bool[,] walkable, int[,] distance, int width, int depth)
	{
		_walkable = walkable;
		_distance = distance;
		_width = width;
		_depth = depth;
	}

	public bool Step(int remainingBudgetMilliseconds)
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

			Progress = 0.4f * (_distanceRow / (float)_depth);
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

		Progress = _distanceQueue.Count == 0 ? 1f : 0.4f + 0.6f * Math.Min(0.99f, processedNodes / (float)(processedNodes + _distanceQueue.Count));
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
}
