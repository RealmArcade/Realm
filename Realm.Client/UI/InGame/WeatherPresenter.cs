using Godot;
using Realm.Client.Services;
using System;
using System.Collections.Generic;

namespace Realm.Client.UI.InGame;

/// <summary>
///     Weather as the match shows it: the fog density a forecast implies, the visuals the environment applies, and the
///     line the player is told about it. The environment service owns the weather itself; this is the part that has to
///     explain it.
/// </summary>
public static class WeatherPresenter
{
	// The line is a factory rather than a stored string because the localization catalog is built from literals: the
	// forecast is translated when it is announced, in whichever language the player is on at the time.
	private sealed record Forecast(float FogDensity, Func<string> Feedback, Color Tint);

	private static readonly Dictionary<string, Forecast> Forecasts = new(StringComparer.OrdinalIgnoreCase)
	{
		["clear"] = new Forecast(0f, () => TranslationServer.Translate("Weather Forecast: Clear Skies"), new Color(0.3f, 0.9f, 1.0f)),
		["rain"] = new Forecast(0.0075f, () => TranslationServer.Translate("Weather Forecast: Light Rain Shower"), new Color(0.2f, 0.5f, 0.9f)),
		["snow"] = new Forecast(0.005f, () => TranslationServer.Translate("Weather Forecast: Snowfall"), new Color(0.8f, 0.9f, 1.0f)),
		["fog"] = new Forecast(0.045f, () => TranslationServer.Translate("Weather Forecast: Dense Fog Warning"), new Color(0.7f, 0.7f, 0.8f))
	};

	private static Forecast For(string weather) =>
		weather != null && Forecasts.TryGetValue(weather, out Forecast? forecast) ? forecast : Forecasts["clear"];

	/// <summary>
	///     Sets the weather and everything that follows from it: the fog density the forecast implies, the environment
	///     visuals, and the player's notice.
	/// </summary>
	public static void Sync(string weather, Action<string, Color> feedback)
	{
		if (GameContext.Host != null)
		{
			Forecast forecast = For(weather);
			GameContext.EnvironmentService.SetCurrentWeather(weather);
			GameContext.EnvironmentService.SetBaseFogDensity(forecast.FogDensity);
			GameContext.EnvironmentService.ApplyWeatherVisuals(weather);
		}

		ApplyEffects(weather, feedback);
	}

	/// <summary>
	///     The half of a weather sync a client can do for itself: the visuals and the notice, without claiming to change
	///     the forecast.
	/// </summary>
	public static void ApplyEffects(string weather, Action<string, Color> feedback)
	{
		if (GameContext.Host != null)
		{
			GameContext.EnvironmentService.ApplyWeatherVisuals(weather);
		}

		// An unrecognised forecast has nothing to announce, which is why this is a lookup and not a fallback to clear.
		if (weather == null || !Forecasts.TryGetValue(weather, out Forecast? forecast)) return;
		feedback(forecast.Feedback(), forecast.Tint);
	}

	/// <summary>
	///     The weather the loaded map declares. Reading it means reloading the map's metadata, because the forecast is a
	///     property of the map rather than of the session that loaded it.
	/// </summary>
	public static string ResolveCurrent()
	{
		if (GameContext.Host == null) return "clear";

		string mapDir = !string.IsNullOrEmpty(GameContext.MapStorageService.CurrentMapDirectory)
			? GameContext.MapStorageService.CurrentMapDirectory
			: MapWorkspaceService.GetActiveWorkspacePath();
		string metaPath = System.IO.Path.Combine(mapDir, "metadata.json").Replace("\\", "/");
		GameContext.Host.LoadMapProperties(metaPath);
		return GameContext.EnvironmentService?.GetCurrentWeather() ?? "clear";
	}
}
