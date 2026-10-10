namespace Realm.Ecs.Components.Terrain
{
	/// <summary>
	/// Represents the domain data for atmospheric weather conditions and environmental fog.
	/// </summary>
	public struct WeatherState
	{
		public string CurrentWeather;
		public float BaseFogDensity;

		public WeatherState(string currentWeather, float baseFogDensity)
		{
			CurrentWeather = currentWeather;
			BaseFogDensity = baseFogDensity;
		}
	}
}
