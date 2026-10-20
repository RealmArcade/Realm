using System;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Terrain;

/// <summary>
/// Represents the domain data for atmospheric weather conditions and environmental fog.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct WeatherState
{
	public WeatherType Type;
	public float BaseFogDensity;

	public WeatherState(WeatherType weatherType, float baseFogDensity)
	{
		Type = weatherType;
		BaseFogDensity = baseFogDensity;
	}

	public WeatherState(string currentWeather, float baseFogDensity)
		: this(ParseWeatherType(currentWeather), baseFogDensity)
	{
	}

	public string CurrentWeather
	{
		get => Type switch
		{
			WeatherType.Rain => "rain",
			WeatherType.Snow => "snow",
			WeatherType.Fog => "fog",
			_ => "clear"
		};
		set => Type = ParseWeatherType(value);
	}

	private static WeatherType ParseWeatherType(string? weather)
	{
		return weather?.Trim().ToLowerInvariant() switch
		{
			"rain" => WeatherType.Rain,
			"snow" => WeatherType.Snow,
			"fog" => WeatherType.Fog,
			_ => WeatherType.Clear
		};
	}
}
