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

		try
		{
			string jsonText = file.GetAsText();
			using var jsonDoc = JsonDocument.Parse(jsonText);

			if (jsonDoc.RootElement.TryGetProperty("MapProperties", out var mapProps))
			{
				ProcessMapProperties(worldEntity, mapProps);
			}

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
			GD.PrintErr($"[MapPropertiesLoader] Failed to load map properties: {ex.Message}");
		}
	}

	private void ProcessMapProperties(Entity worldEntity, JsonElement mapProps)
	{
		TryUpdateCameraBounds(worldEntity, mapProps);
		TryUpdateShroud(worldEntity, mapProps);
		TryUpdateWeather(worldEntity, mapProps);
	}

	private void TryUpdateCameraBounds(Entity worldEntity, JsonElement mapProps)
	{
		if (mapProps.TryGetProperty("CameraBoundsLeft", out var leftProp) && leftProp.ValueKind == JsonValueKind.Number)
		{
			GameContext.World.Mutate<CameraState>(worldEntity, (ref CameraState s) => s.LimitLeft = (float)leftProp.GetDouble());
		}

		if (mapProps.TryGetProperty("CameraBoundsRight", out var rightProp) && rightProp.ValueKind == JsonValueKind.Number)
		{
			GameContext.World.Mutate<CameraState>(worldEntity, (ref CameraState s) => s.LimitRight = (float)rightProp.GetDouble());
		}

		if (mapProps.TryGetProperty("CameraBoundsTop", out var topProp) && topProp.ValueKind == JsonValueKind.Number)
		{
			GameContext.World.Mutate<CameraState>(worldEntity, (ref CameraState s) => s.LimitTop = (float)topProp.GetDouble());
		}

		if (mapProps.TryGetProperty("CameraBoundsBottom", out var bottomProp) && bottomProp.ValueKind == JsonValueKind.Number)
		{
			GameContext.World.Mutate<CameraState>(worldEntity, (ref CameraState s) => s.LimitBottom = (float)bottomProp.GetDouble());
		}
	}

	private void TryUpdateShroud(Entity worldEntity, JsonElement mapProps)
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

		GameContext.World.Mutate<ShroudState>(worldEntity, (ref ShroudState s) => s.VisibilityMode = mode);
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

	private void TryUpdateWeather(Entity worldEntity, JsonElement mapProps)
	{
		if (mapProps.TryGetProperty("WeatherType", out var weatherProp) && weatherProp.ValueKind == JsonValueKind.String)
		{
			string val = weatherProp.GetString() ?? "clear";
			GameContext.World.Mutate<WeatherState>(worldEntity, (ref WeatherState s) => s.CurrentWeather = val);
		}
	}

	private void ProcessCustomEnvironmentPresets(JsonElement presetsProp)
	{
		var presets = JsonSerializer.Deserialize<List<EnvironmentPresetConfig>>(presetsProp.GetRawText());
		if (presets == null || presets.Count == 0)
		{
			return;
		}

		GameHost tempQualifier = GameContext.Host;
		(tempQualifier != null ? GameContext.EnvironmentService : null)?.LoadPresets(presets);
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