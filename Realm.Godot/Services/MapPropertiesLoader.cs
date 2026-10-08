using Arch.Core;
using Realm.Ecs.Services;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;
using System;
using System.Collections.Generic;

public class MapPropertiesLoader
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World EcsWorld => _ecsWorldAccessor.Current;

	public MapPropertiesLoader(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

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
			using var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonText);
			
			if (jsonDoc.RootElement.TryGetProperty("MapProperties", out var mapProps))
			{
				ProcessMapProperties(worldEntity, mapProps);
			}

			if (jsonDoc.RootElement.TryGetProperty("CustomEnvironmentPresets", out var presetsProp) && presetsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
			{
				ProcessCustomEnvironmentPresets(presetsProp);
			}

			if (jsonDoc.RootElement.TryGetProperty("DefaultEnvironmentPreset", out var defaultPresetProp) && defaultPresetProp.ValueKind == System.Text.Json.JsonValueKind.String)
			{
				ProcessDefaultEnvironmentPreset(defaultPresetProp);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapPropertiesLoader] Failed to load map properties: {ex.Message}");
		}
	}

	private void ProcessMapProperties(Entity worldEntity, System.Text.Json.JsonElement mapProps)
	{
		TryUpdateCameraBounds(worldEntity, mapProps);
		TryUpdateShroud(worldEntity, mapProps);
		TryUpdateWeather(worldEntity, mapProps);
	}

	private void TryUpdateCameraBounds(Entity worldEntity, System.Text.Json.JsonElement mapProps)
	{
		if (mapProps.TryGetProperty("CameraBoundsLeft", out var leftProp) && leftProp.ValueKind == System.Text.Json.JsonValueKind.Number)
		{
			EcsWorld.Mutate<CameraState>(worldEntity, (ref CameraState s) => s.LimitLeft = (float)leftProp.GetDouble());
		}
		if (mapProps.TryGetProperty("CameraBoundsRight", out var rightProp) && rightProp.ValueKind == System.Text.Json.JsonValueKind.Number)
		{
			EcsWorld.Mutate<CameraState>(worldEntity, (ref CameraState s) => s.LimitRight = (float)rightProp.GetDouble());
		}
		if (mapProps.TryGetProperty("CameraBoundsTop", out var topProp) && topProp.ValueKind == System.Text.Json.JsonValueKind.Number)
		{
			EcsWorld.Mutate<CameraState>(worldEntity, (ref CameraState s) => s.LimitTop = (float)topProp.GetDouble());
		}
		if (mapProps.TryGetProperty("CameraBoundsBottom", out var bottomProp) && bottomProp.ValueKind == System.Text.Json.JsonValueKind.Number)
		{
			EcsWorld.Mutate<CameraState>(worldEntity, (ref CameraState s) => s.LimitBottom = (float)bottomProp.GetDouble());
		}
	}

	private void TryUpdateShroud(Entity worldEntity, System.Text.Json.JsonElement mapProps)
	{
		if (mapProps.TryGetProperty("ShroudType", out var shroudTypeProp) && shroudTypeProp.ValueKind == System.Text.Json.JsonValueKind.String)
		{
			string val = shroudTypeProp.GetString() ?? "VisionShroud";
			EcsWorld.Mutate<ShroudState>(worldEntity, (ref ShroudState s) => s.ShroudType = val);
		}
	}

	private void TryUpdateWeather(Entity worldEntity, System.Text.Json.JsonElement mapProps)
	{
		if (mapProps.TryGetProperty("WeatherType", out var weatherProp) && weatherProp.ValueKind == System.Text.Json.JsonValueKind.String)
		{
			string val = weatherProp.GetString() ?? "clear";
			EcsWorld.Mutate<WeatherState>(worldEntity, (ref WeatherState s) => s.CurrentWeather = val);
		}
	}

	private void ProcessCustomEnvironmentPresets(System.Text.Json.JsonElement presetsProp)
	{
		var presets = System.Text.Json.JsonSerializer.Deserialize<List<EnvironmentPresetConfig>>(presetsProp.GetRawText());
		if (presets == null || presets.Count == 0)
		{
			return;
		}

		GameHost.Instance?.EnvironmentService?.LoadPresets(presets);
	}

	private void ProcessDefaultEnvironmentPreset(System.Text.Json.JsonElement defaultPresetProp)
	{
		string defPreset = defaultPresetProp.GetString() ?? "day";
		if (GameHost.Instance != null)
		{
			GameHost.Instance.EnvironmentService?.ApplyPresetById(GameHost.Instance, defPreset);
		}
	}

}
