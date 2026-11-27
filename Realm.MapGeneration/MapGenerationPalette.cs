using System;
using System.Collections.Generic;

namespace Realm.MapGeneration;

public class MapGenerationPalette
{
	private readonly List<TerrainSwatch> _swatches = new();

	public IReadOnlyList<TerrainSwatch> Swatches => _swatches;

	public int WaterProfileIndex { get; set; }

	public bool IsEmpty => _swatches.Count == 0;

	public void Add(TerrainSwatch swatch)
	{
		_swatches.Add(swatch);
	}

	public void Add(int index, string? name, int defaultPathingCode)
	{
		_swatches.Add(new TerrainSwatch(index, name ?? string.Empty, TerrainSurfaceClassifier.Classify(name), defaultPathingCode, 0.5f, 0.5f, 0.5f, false));
	}

	public void Add(int index, string? name, int defaultPathingCode, float red, float green, float blue)
	{
		_swatches.Add(new TerrainSwatch(index, name ?? string.Empty, TerrainSurfaceClassifier.Classify(name), defaultPathingCode, red, green, blue, true));
	}

	public TerrainSwatch FindForKind(TerrainSurfaceKind kind, int variation)
	{
		return FindForKind(kind, variation, 0.5f, 0.5f, 0.5f, false);
	}

	public TerrainSwatch FindForKind(TerrainSurfaceKind kind, int variation, float red, float green, float blue, bool hasTargetColour)
	{
		if (_swatches.Count == 0)
		{
			return TerrainSwatch.Empty(0);
		}

		var matches = CollectMatches(kind);
		if (matches.Count == 0)
		{
			matches = CollectFallback(kind);
		}

		if (matches.Count == 0)
		{
			return _swatches[0];
		}

		if (hasTargetColour && matches.Count > 1)
		{
			matches.Sort((left, right) => left.ColourDistance(red, green, blue).CompareTo(right.ColourDistance(red, green, blue)));
			int candidateCount = Math.Min(3, matches.Count);
			return matches[(int)((uint)variation % (uint)candidateCount)];
		}

		int index = (int)((uint)variation % (uint)matches.Count);
		return matches[index];
	}

	public TerrainSwatch FindClosest(TerrainSurfaceKind kind, float red, float green, float blue, int variation)
	{
		if (_swatches.Count == 0)
		{
			return TerrainSwatch.Empty(0);
		}

		const float kindMatchBonus = 0.06f;
		const float tieTolerance = 0.02f;
		var ranked = new List<TerrainSwatch>(_swatches);
		ranked.Sort((left, right) => Score(left).CompareTo(Score(right)));
		float best = Score(ranked[0]);
		int candidateCount = 1;
		while (candidateCount < Math.Min(3, ranked.Count) && Score(ranked[candidateCount]) <= best + tieTolerance)
		{
			candidateCount++;
		}

		return ranked[(int)((uint)variation % (uint)candidateCount)];

		float Score(TerrainSwatch swatch)
		{
			float distance = swatch.ColourDistance(red, green, blue);
			return swatch.Kind == kind ? distance - kindMatchBonus : distance;
		}
	}

	public int CountForKind(TerrainSurfaceKind kind)
	{
		int count = 0;
		for (int index = 0; index < _swatches.Count; index++)
		{
			if (_swatches[index].Kind == kind)
			{
				count++;
			}
		}

		return count;
	}

	public IReadOnlyList<string> DescribeKinds()
	{
		var descriptions = new List<string>();
		for (int index = 0; index < _swatches.Count; index++)
		{
			TerrainSwatch swatch = _swatches[index];
			descriptions.Add($"{swatch.Index}:{swatch.Name}:{swatch.Kind}");
		}

		return descriptions;
	}

	private List<TerrainSwatch> CollectMatches(TerrainSurfaceKind kind)
	{
		var matches = new List<TerrainSwatch>();
		for (int index = 0; index < _swatches.Count; index++)
		{
			if (_swatches[index].Kind == kind)
			{
				matches.Add(_swatches[index]);
			}
		}

		return matches;
	}

	private List<TerrainSwatch> CollectFallback(TerrainSurfaceKind kind)
	{
		switch (kind)
		{
			case TerrainSurfaceKind.Water:
			case TerrainSurfaceKind.Sand:
				return CollectMatches(TerrainSurfaceKind.Soil);
			case TerrainSurfaceKind.Soil:
				return CollectMatches(TerrainSurfaceKind.Grass);
			case TerrainSurfaceKind.Grass:
				return CollectMatches(TerrainSurfaceKind.Forest);
			case TerrainSurfaceKind.Forest:
				return CollectMatches(TerrainSurfaceKind.Grass);
			case TerrainSurfaceKind.Rock:
				return CollectMatches(TerrainSurfaceKind.Soil);
			case TerrainSurfaceKind.Snow:
				return CollectMatches(TerrainSurfaceKind.Rock);
			default:
				return CollectMatches(TerrainSurfaceKind.Grass);
		}
	}
}
