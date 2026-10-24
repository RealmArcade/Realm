using System;

namespace Realm.Client.Services.MapGeneration;

public readonly record struct MapGenerationParameters(
	int HillsDensity,
	int TerrainRoughness,
	int MountainHeight,
	int ChokePointWidth,
	int WaterLevel,
	int TreeClumpDensity,
	int ResourceAbundance,
	int DecorativePropDensity)
{
	public const int MinimumSliderValue = 1;
	public const int MaximumSliderValue = 10;
	public const int DefaultSliderValue = 5;

	public MapGenerationParameters Clamped()
	{
		return new MapGenerationParameters(
			Math.Clamp(HillsDensity, MinimumSliderValue, MaximumSliderValue),
			Math.Clamp(TerrainRoughness, MinimumSliderValue, MaximumSliderValue),
			Math.Clamp(MountainHeight, MinimumSliderValue, MaximumSliderValue),
			Math.Clamp(ChokePointWidth, MinimumSliderValue, MaximumSliderValue),
			Math.Clamp(WaterLevel, MinimumSliderValue, MaximumSliderValue),
			Math.Clamp(TreeClumpDensity, MinimumSliderValue, MaximumSliderValue),
			Math.Clamp(ResourceAbundance, MinimumSliderValue, MaximumSliderValue),
			Math.Clamp(DecorativePropDensity, MinimumSliderValue, MaximumSliderValue));
	}

	public static MapGenerationParameters Defaults => new(
		DefaultSliderValue,
		DefaultSliderValue,
		DefaultSliderValue,
		DefaultSliderValue,
		DefaultSliderValue,
		DefaultSliderValue,
		DefaultSliderValue,
		DefaultSliderValue);
}
