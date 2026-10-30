using System;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;

namespace Realm.Client.Services;

public class WindService
{
	private static readonly StringName WindStateParameter = new("rts_wind_state");

	private static readonly float[] TimeOfDayStrength = { 1.00f, 1.20f, 0.60f, 0.85f };

	private const float MinimumStrength = 0.15f;
	private const float MaximumStrength = 2.00f;
	private const float GustFrequency = 0.23f;
	private const float GustDepth = 0.15f;
	private const float DirectionDriftSpeed = 0.008f;
	private const float BaseDirectionRadians = 0.4636f;

	private float _elapsedSeconds;

	public Vector2 Direction { get; private set; } = new(0.894f, 0.447f);

	public float Strength { get; private set; } = 1f;

	public void Tick(float delta)
	{
		if (!float.IsFinite(delta) || delta < 0f) return;

		_elapsedSeconds += delta;

		int timeOfDayIndex = ResolveTimeOfDayIndex();
		string weather = GameContext.EnvironmentService?.GetCurrentWeather() ?? "clear";

		float phaseStrength = TimeOfDayStrength[Mathf.Clamp(timeOfDayIndex, 0, TimeOfDayStrength.Length - 1)];
		float weatherMultiplier = ResolveWeatherMultiplier(weather);
		float gust = 1f - GustDepth + GustDepth * MathF.Sin(_elapsedSeconds * GustFrequency * Mathf.Tau);

		Strength = Mathf.Clamp(phaseStrength * weatherMultiplier * gust, MinimumStrength, MaximumStrength);

		float directionRadians = BaseDirectionRadians + MathF.Sin(_elapsedSeconds * DirectionDriftSpeed) * 0.35f;
		Direction = new Vector2(MathF.Cos(directionRadians), MathF.Sin(directionRadians));

		RenderingServer.GlobalShaderParameterSet(
			WindStateParameter,
			new Vector4(Direction.X, Direction.Y, Strength, _elapsedSeconds));
	}

	public void Reset()
	{
		_elapsedSeconds = 0f;
		Direction = new Vector2(0.894f, 0.447f);
		Strength = 1f;
	}

	private static int ResolveTimeOfDayIndex()
	{
		if (GameContext.World == null) return 0;
		if (!GameContext.World.IsAlive(GameContext.WorldEntity)) return 0;
		if (!GameContext.World.Has<WorldState>(GameContext.WorldEntity)) return 0;

		return GameContext.World.Get<WorldState>(GameContext.WorldEntity).TimeOfDayIndex;
	}

	private static float ResolveWeatherMultiplier(string weather)
	{
		return weather?.ToLowerInvariant() switch
		{
			"rain" => 1.65f,
			"snow" => 1.05f,
			"fog" => 0.45f,
			_ => 1.0f
		};
	}
}
