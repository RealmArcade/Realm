using System;
using System.Collections.Generic;
using System.Text.Json;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client.Services;

public class MapPropertiesLoader
{
	public void LoadMapProperties(Entity worldEntity, string path)
	{
		if (!FileAccess.FileExists(path))
		{
			return;
		}

		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		if (file == null)
		{
			return;
		}

		if (GameContext.World is not { } world) return;

		try
		{
			string jsonText = file.GetAsText();
			using var jsonDoc = JsonDocument.Parse(jsonText);

			if (jsonDoc.RootElement.TryGetProperty("MapProperties", out var mapProps))
			{
				ProcessMapProperties(world, worldEntity, mapProps);
			}

			ApplyLobbyShroudOverride(worldEntity);

			if (jsonDoc.RootElement.TryGetProperty("CustomEnvironmentPresets", out var presetsProp) && presetsProp.ValueKind == JsonValueKind.Array)
			{
				ProcessCustomEnvironmentPresets(presetsProp);
			}

			if (jsonDoc.RootElement.TryGetProperty("DefaultEnvironmentPreset", out var defaultPresetProp) && defaultPresetProp.ValueKind == JsonValueKind.String)
			{
				ProcessDefaultEnvironmentPreset(defaultPresetProp);
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapPropertiesLoader] Failed to load map properties: {ex.Message}");
		}
	}

	private static void ProcessMapProperties(EcsWorld world, Entity worldEntity, JsonElement mapProps)
	{
		TryUpdateCameraBounds(world, worldEntity, mapProps);
		TryUpdateShroud(world, worldEntity, mapProps);
		TryUpdateWeather(world, worldEntity, mapProps);
	}

	private static void TryUpdateCameraBounds(EcsWorld world, Entity worldEntity, JsonElement mapProps)
	{
		if (mapProps.TryGetProperty("CameraBoundsLeft", out var leftProp) && leftProp.ValueKind == JsonValueKind.Number)
		{
			world.Mutate<CameraState>(worldEntity, (ref CameraState s) => s.LimitLeft = (float)leftProp.GetDouble());
		}

		if (mapProps.TryGetProperty("CameraBoundsRight", out var rightProp) && rightProp.ValueKind == JsonValueKind.Number)
		{
			world.Mutate<CameraState>(worldEntity, (ref CameraState s) => s.LimitRight = (float)rightProp.GetDouble());
		}

		if (mapProps.TryGetProperty("CameraBoundsTop", out var topProp) && topProp.ValueKind == JsonValueKind.Number)
		{
			world.Mutate<CameraState>(worldEntity, (ref CameraState s) => s.LimitTop = (float)topProp.GetDouble());
		}

		if (mapProps.TryGetProperty("CameraBoundsBottom", out var bottomProp) && bottomProp.ValueKind == JsonValueKind.Number)
		{
			world.Mutate<CameraState>(worldEntity, (ref CameraState s) => s.LimitBottom = (float)bottomProp.GetDouble());
		}
	}

	private static void TryUpdateShroud(EcsWorld world, Entity worldEntity, JsonElement mapProps)
	{
		if (!mapProps.TryGetProperty("ShroudType", out var shroudTypeProp)) return;
		if (shroudTypeProp.ValueKind != JsonValueKind.String)
		{
			GD.PushError("[MapPropertiesLoader] ShroudType must be the string ExplorationShroud, VisionShroud, or Revealed.");
			return;
		}

		string shroudType = shroudTypeProp.GetString() ?? string.Empty;
		if (!TryParseShroudVisibilityMode(shroudType, out ShroudVisibilityMode mode))
		{
			GD.PushError($"[MapPropertiesLoader] Unknown ShroudType '{shroudType}'. Expected ExplorationShroud, VisionShroud, or Revealed.");
			return;
		}

		world.Mutate<ShroudState>(worldEntity, (ref ShroudState s) => s.VisibilityMode = mode);
	}

	/// <summary>
	///     Applies the shroud mode the lobby's host configured, which a host sets to play a map it holds with vision
	///     limited, or with the whole board revealed, rather than with the mode the map itself states.
	/// </summary>
	/// <param name="worldEntity">The world whose visibility is being set.</param>
	private static void ApplyLobbyShroudOverride(Entity worldEntity)
	{
		if (worldEntity == Entity.Null) return;

		MatchShroudMode mode = GameContext.Lobby?.MatchOptions.GetShroudMode() ?? MatchShroudMode.FromMap;
		if (mode == MatchShroudMode.FromMap) return;

		ShroudVisibilityMode visibilityMode = mode switch
		{
			MatchShroudMode.Revealed => ShroudVisibilityMode.Revealed,
			MatchShroudMode.VisionShroud => ShroudVisibilityMode.VisionShroud,
			_ => ShroudVisibilityMode.ExplorationShroud
		};

		GameContext.World.Mutate<ShroudState>(worldEntity, (ref ShroudState s) => s.VisibilityMode = visibilityMode);
	}

	private static bool TryParseShroudVisibilityMode(string shroudType, out ShroudVisibilityMode mode)
	{
		if (shroudType == "ExplorationShroud")
		{
			mode = ShroudVisibilityMode.ExplorationShroud;
			return true;
		}

		if (shroudType == "VisionShroud")
		{
			mode = ShroudVisibilityMode.VisionShroud;
			return true;
		}

		if (shroudType == "Revealed")
		{
			mode = ShroudVisibilityMode.Revealed;
			return true;
		}

		mode = ShroudVisibilityMode.ExplorationShroud;
		return false;
	}

	private static void TryUpdateWeather(EcsWorld world, Entity worldEntity, JsonElement mapProps)
	{
		if (mapProps.TryGetProperty("WeatherType", out var weatherProp) && weatherProp.ValueKind == JsonValueKind.String)
		{
			string val = weatherProp.GetString() ?? "clear";
			world.Mutate<WeatherState>(worldEntity, (ref WeatherState s) => s.CurrentWeather = val);
		}
	}

	private void ProcessCustomEnvironmentPresets(JsonElement presetsProp)
	{
		var presets = JsonSerializer.Deserialize<List<EnvironmentPresetConfig>>(presetsProp.GetRawText());
		if (presets == null || presets.Count == 0)
		{
			return;
		}

		GameContext.EnvironmentService?.LoadPresets(presets);
	}

	private void ProcessDefaultEnvironmentPreset(JsonElement defaultPresetProp)
	{
		string defPreset = defaultPresetProp.GetString() ?? "day";
		if (GameContext.Host != null)
		{
			GameContext.EnvironmentService?.ApplyPresetById(defPreset);
		}
	}
}