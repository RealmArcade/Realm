using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Terrain
{
	/// <summary>
	/// Represents the domain data for atmospheric weather conditions and environmental fog.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public struct WeatherState
	{
		public int StringId;
		public float BaseFogDensity;

		public WeatherState(string currentWeather, float baseFogDensity)
		{
			StringId = StringRegistry.GetOrAdd(currentWeather);
			BaseFogDensity = baseFogDensity;
		}

		public string CurrentWeather
		{
			get => StringRegistry.Get(StringId);
			set => StringId = StringRegistry.GetOrAdd(value);
		}
	}
}
