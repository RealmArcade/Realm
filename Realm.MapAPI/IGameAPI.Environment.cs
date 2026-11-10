// Extracted from IGameAPI.cs (task 6 decomposition).
using System.Numerics;


namespace Realm.MapAPI;


/// <summary>
/// Defines the safe, public API exposed to map scripts to interact with the game engine.
/// </summary>
public partial interface IGameAPI
{
	/// <summary>
	/// Starts a countdown timer UI on the screen.
	/// </summary>
	/// <param name="duration">The duration in seconds.</param>
	/// <param name="label">The title or description of the countdown timer.</param>
	void StartCountdownTimer(float duration, string label);

	/// <summary>
	/// Stops and hides the active countdown timer.
	/// </summary>
	void StopCountdownTimer();

	/// <summary>
	/// Shakes the game camera to simulate an explosion or impact.
	/// </summary>
	/// <param name="intensity">The shake strength multiplier.</param>
	/// <param name="duration">The time in seconds the shake lasts.</param>
	void ShakeCamera(float intensity, float duration);

	/// <summary>
	/// Sets the time of day to a specific hour (0.0 to 24.0).
	/// </summary>
	/// <param name="time">The hour value.</param>
	void SetTimeOfDay(float time);

	/// <summary>
	/// Enables or disables the day-night cycle.
	/// </summary>
	/// <param name="enabled">True to enable, false to freeze the time of day.</param>
	void SetDayNightCycleEnabled(bool enabled);

	/// <summary>
	/// Updates the countdown timer label without resetting the current timer.
	/// Useful for changing the displayed title mid-countdown.
	/// </summary>
	/// <param name="label">The new label text for the active countdown timer.</param>
	void SetCountdownTimerLabel(string label);

	/// <summary>
	/// Sets the active environment lighting, fog, and atmospheric preset immediately without transition interpolation.
	/// </summary>
	/// <param name="presetId">The unique identifier of the environment preset configured in map metadata.</param>
	void SetEnvironmentPreset(string presetId);

	/// <summary>
	/// Smoothly transitions active environment lighting, fog, SSAO, and post-processing visual settings to the target preset over the specified duration.
	/// </summary>
	/// <param name="presetId">The unique identifier of the target environment preset configured in map metadata.</param>
	/// <param name="durationSeconds">The transition duration in seconds. If 0 or negative, the preset applies immediately.</param>
	void TransitionEnvironmentPreset(string presetId, float durationSeconds);

	/// <summary>
	/// Retrieves the unique identifier of the currently active environment preset.
	/// </summary>
	/// <returns>The preset identifier string.</returns>
	string GetCurrentEnvironmentPreset();

	/// <summary>
	/// Sets the active weather effect condition (e.g., "clear", "rain", "fog", "snow").
	/// </summary>
	/// <param name="weatherType">The name of the weather condition.</param>
	void SetWeather(string weatherType);

	/// <summary>
	/// Retrieves the name of the currently active weather condition.
	/// </summary>
	/// <returns>The weather name string.</returns>
	string GetWeather();

	/// <summary>
	/// Disables the vision shroud, making the entire map and all entities fully visible.
	/// </summary>
	void DisableShroud();
}
