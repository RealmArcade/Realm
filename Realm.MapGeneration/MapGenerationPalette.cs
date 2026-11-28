using System;
using System.Collections.Generic;

namespace Realm.MapGeneration;

public class MapGenerationPalette
{
	private const float KindMatchBonus = 0.06f;

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
			return swatch.Kind == kind ? distance - KindMatchBonus : distance;
		}
	}

	public IReadOnlyList<TerrainSwatch> RankForKind(TerrainSurfaceKind kind, float red, float green, float blue)
	{
		var ranked = new List<TerrainSwatch>(_swatches);
		ranked.Sort((left, right) => Score(left).CompareTo(Score(right)));
		return ranked;

		float Score(TerrainSwatch swatch)
		{
			float distance = swatch.ColourDistance(red, green, blue);
			return swatch.Kind == kind ? distance - KindMatchBonus : distance;
		}
	}

	public TerrainSwatch FindByIndex(int index)
	{
		for (int swatchIndex = 0; swatchIndex < _swatches.Count; swatchIndex++)
		{
			if (_swatches[swatchIndex].Index == index)
			{
				return _swatches[swatchIndex];
			}
		}

		return TerrainSwatch.Empty(index);
	}

	public TerrainSwatch FindNearestColour(float red, float green, float blue)
	{
		if (_swatches.Count == 0)
		{
			return TerrainSwatch.Empty(0);
		}

		TerrainSwatch best = _swatches[0];
		float bestDistance = best.ColourDistance(red, green, blue);
		for (int index = 1; index < _swatches.Count; index++)
		{
			float distance = _swatches[index].ColourDistance(red, green, blue);
			if (distance < bestDistance)
			{
				bestDistance = distance;
				best = _swatches[index];
			}
		}

		return best;
	}

	public TerrainSwatch FindNearestColourOfKind(TerrainSurfaceKind kind, float red, float green, float blue)
	{
		TerrainSwatch best = TerrainSwatch.Empty(-1);
		float bestDistance = float.MaxValue;
		for (int index = 0; index < _swatches.Count; index++)
		{
			TerrainSwatch swatch = _swatches[index];
			if (!IsMaterialKindMatch(swatch.Kind, kind))
			{
				continue;
			}

			float distance = swatch.ColourDistance(red, green, blue);
			if (distance >= bestDistance)
			{
				continue;
			}

			bestDistance = distance;
			best = swatch;
		}

		return bestDistance < float.MaxValue ? best : FindNearestColour(red, green, blue);
	}

	private static bool IsMaterialKindMatch(TerrainSurfaceKind swatchKind, TerrainSurfaceKind requestedKind)
	{
		if (swatchKind == requestedKind)
		{
			return true;
		}

		return requestedKind == TerrainSurfaceKind.Forest && swatchKind == TerrainSurfaceKind.Grass;
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
