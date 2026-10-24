using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Realm.Client.Services.MapGeneration;

public readonly record struct ScatterPoint(float X, float Z);

public class PoissonScatter
{
	private const int AttemptsPerActivePoint = 20;

	private readonly int _width;
	private readonly int _depth;
	private readonly float _radius;
	private readonly float _cellSize;
	private readonly int _gridWidth;
	private readonly int _gridDepth;
	private readonly int[] _grid;
	private readonly int _maximumPoints;
	private readonly List<ScatterPoint> _points = new();
	private readonly List<int> _activePointIndices = new();
	private SeededRandom _random;
	private bool _initialized;

	public PoissonScatter(int width, int depth, float radius, int maximumPoints, int seed)
	{
		_width = Math.Max(1, width);
		_depth = Math.Max(1, depth);
		_radius = Math.Max(1f, radius);
		_cellSize = _radius / MathF.Sqrt(2f);
		_gridWidth = Math.Max(1, (int)MathF.Ceiling(_width / _cellSize));
		_gridDepth = Math.Max(1, (int)MathF.Ceiling(_depth / _cellSize));
		_grid = new int[_gridWidth * _gridDepth];
		_maximumPoints = Math.Max(1, maximumPoints);
		_random = new SeededRandom(seed);
	}

	public IReadOnlyList<ScatterPoint> Points => _points;

	public int PointCount => _points.Count;

	public bool IsComplete => _initialized && (_activePointIndices.Count == 0 || _points.Count >= _maximumPoints);

	public bool Advance(int budgetMilliseconds)
	{
		if (IsComplete)
		{
			return true;
		}

		if (!_initialized)
		{
			Initialize();
		}

		var stopwatch = Stopwatch.StartNew();
		int attemptsSinceCheck = 0;
		while (_activePointIndices.Count > 0 && _points.Count < _maximumPoints)
		{
			if (attemptsSinceCheck >= 32 && stopwatch.ElapsedMilliseconds >= budgetMilliseconds)
			{
				break;
			}

			int activeSlot = _random.NextInt(_activePointIndices.Count);
			int sourcePointIndex = _activePointIndices[activeSlot];
			ScatterPoint sourcePoint = _points[sourcePointIndex];
			bool accepted = false;

			for (int attempt = 0; attempt < AttemptsPerActivePoint; attempt++)
			{
				float angle = _random.NextFloat(0f, MathF.PI * 2f);
				float distance = _random.NextFloat(_radius, _radius * 2f);
				float candidateX = sourcePoint.X + MathF.Cos(angle) * distance;
				float candidateZ = sourcePoint.Z + MathF.Sin(angle) * distance;
				attemptsSinceCheck++;
				if (candidateX < 0f || candidateX >= _width || candidateZ < 0f || candidateZ >= _depth)
				{
					continue;
				}

				if (!IsFarEnoughFromExistingPoints(candidateX, candidateZ))
				{
					continue;
				}

				AddPoint(candidateX, candidateZ);
				accepted = true;
				break;
			}

			if (!accepted)
			{
				_activePointIndices[activeSlot] = _activePointIndices[^1];
				_activePointIndices.RemoveAt(_activePointIndices.Count - 1);
			}
		}

		return IsComplete;
	}

	private void Initialize()
	{
		_initialized = true;
		float startX = _random.NextFloat(_radius, MathF.Max(_radius, _width - _radius));
		float startZ = _random.NextFloat(_radius, MathF.Max(_radius, _depth - _radius));
		startX = Math.Clamp(startX, 0f, _width - 0.001f);
		startZ = Math.Clamp(startZ, 0f, _depth - 0.001f);
		AddPoint(startX, startZ);
	}

	private void AddPoint(float x, float z)
	{
		int pointIndex = _points.Count;
		_points.Add(new ScatterPoint(x, z));
		_activePointIndices.Add(pointIndex);
		int gridX = Math.Clamp((int)(x / _cellSize), 0, _gridWidth - 1);
		int gridZ = Math.Clamp((int)(z / _cellSize), 0, _gridDepth - 1);
		_grid[gridZ * _gridWidth + gridX] = pointIndex + 1;
	}

	private bool IsFarEnoughFromExistingPoints(float x, float z)
	{
		int gridX = Math.Clamp((int)(x / _cellSize), 0, _gridWidth - 1);
		int gridZ = Math.Clamp((int)(z / _cellSize), 0, _gridDepth - 1);
		int searchRadius = 2;
		float radiusSquared = _radius * _radius;

		for (int neighbourZ = Math.Max(0, gridZ - searchRadius); neighbourZ <= Math.Min(_gridDepth - 1, gridZ + searchRadius); neighbourZ++)
		{
			for (int neighbourX = Math.Max(0, gridX - searchRadius); neighbourX <= Math.Min(_gridWidth - 1, gridX + searchRadius); neighbourX++)
			{
				int storedIndex = _grid[neighbourZ * _gridWidth + neighbourX];
				if (storedIndex == 0)
				{
					continue;
				}

				ScatterPoint existingPoint = _points[storedIndex - 1];
				float deltaX = existingPoint.X - x;
				float deltaZ = existingPoint.Z - z;
				if (deltaX * deltaX + deltaZ * deltaZ < radiusSquared)
				{
					return false;
				}
			}
		}

		return true;
	}
}
