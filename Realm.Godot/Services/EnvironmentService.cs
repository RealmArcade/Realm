using Arch.Core;
using Realm.Ecs.Services;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;
using System;
using System.Collections.Generic;
using System.Linq;

public class EnvironmentService
{
	private struct LiveEnvironmentState
	{
		public float SunPitch;
		public float SunYaw;
		public float SunEnergy;
		public Color SunColor;
		public float ShadowBias;
		public float ShadowNormalBias;
		public float AmbientEnergy;
		public Color AmbientColor;
		public bool FogEnabled;
		public float FogDensity;
		public Color FogColor;
		public bool SsaoEnabled;
		public float SsaoRadius;
		public float SsaoIntensity;
		public float SsaoDetail;
		public float TonemapExposure;
		public float AdjustmentContrast;
		public float AdjustmentSaturation;
		public float GlowIntensity;
		public float GlowBloom;
		public float GlowStrength;
		public float CharacterFillEnergy;
		public string WeatherType;
	}

	private readonly WorldAccessor _ecsWorldAccessor;
	private World EcsWorld => _ecsWorldAccessor.Current;

	private readonly List<EnvironmentPresetConfig> _presets = new();
	private string _currentPresetId = "day";
	private string _targetPresetId = "day";
	private bool _isTransitioning = false;
	private float _transitionDuration = 0f;
	private float _transitionElapsed = 0f;

	private LiveEnvironmentState _currentState;
	private LiveEnvironmentState _fromState;
	private LiveEnvironmentState _toState;

	public EnvironmentService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
		LoadPresets(null);
		if (_presets.Count > 0)
		{
			_currentState = ConvertPresetToState(_presets[0]);
			_currentPresetId = _presets[0].Id;
		}
	}

	public void LoadPresets(IEnumerable<EnvironmentPresetConfig>? presets)
	{
		_presets.Clear();
		if (presets != null)
		{
			foreach (var p in presets)
			{
				_presets.Add(p.Clone());
			}
		}

		if (_presets.Count == 0)
		{
			_presets.AddRange(EnvironmentPresetConfig.CreateDefaultPresets());
		}
	}

	public IReadOnlyList<EnvironmentPresetConfig> GetPresets() => _presets;

	public EnvironmentPresetConfig? GetPreset(string id)
	{
		return _presets.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
	}

	public string GetCurrentPresetId() => _currentPresetId;

	public bool IsTransitioning => _isTransitioning;

	private LiveEnvironmentState ConvertPresetToState(EnvironmentPresetConfig preset)
	{
		return new LiveEnvironmentState
		{
			SunPitch = preset.SunPitch,
			SunYaw = preset.SunYaw,
			SunEnergy = preset.SunEnergy,
			SunColor = preset.GetSunColor(),
			ShadowBias = preset.ShadowBias,
			ShadowNormalBias = preset.ShadowNormalBias,
			AmbientEnergy = preset.AmbientEnergy,
			AmbientColor = preset.GetAmbientColor(),
			FogEnabled = preset.FogEnabled,
			FogDensity = preset.FogDensity,
			FogColor = preset.GetFogColor(),
			SsaoEnabled = preset.SsaoEnabled,
			SsaoRadius = preset.SsaoRadius,
			SsaoIntensity = preset.SsaoIntensity,
			SsaoDetail = preset.SsaoDetail,
			TonemapExposure = preset.TonemapExposure,
			AdjustmentContrast = preset.AdjustmentContrast,
			AdjustmentSaturation = preset.AdjustmentSaturation,
			GlowIntensity = preset.GlowIntensity,
			GlowBloom = preset.GlowBloom,
			GlowStrength = preset.GlowStrength,
			CharacterFillEnergy = preset.CharacterFillEnergy,
			WeatherType = preset.WeatherType ?? "clear"
		};
	}

	public void ApplyPreset(Node3D host, EnvironmentPresetConfig preset)
	{
		_isTransitioning = false;
		_currentPresetId = preset.Id;
		_targetPresetId = preset.Id;
		_currentState = ConvertPresetToState(preset);
		ApplyStateToHost(host, _currentState);

		SetCurrentWeather(preset.WeatherType ?? "clear");
		SetBaseFogDensity(preset.BaseFogDensity);
	}

	public void ApplyPresetById(Node3D host, string presetId)
	{
		var preset = GetPreset(presetId) ?? _presets.FirstOrDefault();
		if (preset != null)
		{
			ApplyPreset(host, preset);
		}
	}

	public void TransitionToPreset(Node3D host, string presetId, float durationSeconds)
	{
		var targetPreset = GetPreset(presetId);
		if (targetPreset == null)
		{
			targetPreset = _presets.FirstOrDefault(p => string.Equals(p.Id, "day", StringComparison.OrdinalIgnoreCase)) ?? _presets.FirstOrDefault();
		}

		if (targetPreset == null) return;

		if (durationSeconds <= 0.001f)
		{
			ApplyPreset(host, targetPreset);
			return;
		}

		_fromState = _currentState;
		_toState = ConvertPresetToState(targetPreset);
		_targetPresetId = targetPreset.Id;
		_transitionDuration = durationSeconds;
		_transitionElapsed = 0f;
		_isTransitioning = true;
	}

	public void UpdateTransition(Node3D host, float delta)
	{
		if (!_isTransitioning) return;

		_transitionElapsed += delta;
		float t = Mathf.Clamp(_transitionElapsed / _transitionDuration, 0f, 1f);

		_currentState.SunPitch = Mathf.RadToDeg(Mathf.LerpAngle(Mathf.DegToRad(_fromState.SunPitch), Mathf.DegToRad(_toState.SunPitch), t));
		_currentState.SunYaw = Mathf.RadToDeg(Mathf.LerpAngle(Mathf.DegToRad(_fromState.SunYaw), Mathf.DegToRad(_toState.SunYaw), t));
		_currentState.SunEnergy = Mathf.Lerp(_fromState.SunEnergy, _toState.SunEnergy, t);
		_currentState.SunColor = _fromState.SunColor.Lerp(_toState.SunColor, t);
		_currentState.ShadowBias = Mathf.Lerp(_fromState.ShadowBias, _toState.ShadowBias, t);
		_currentState.ShadowNormalBias = Mathf.Lerp(_fromState.ShadowNormalBias, _toState.ShadowNormalBias, t);

		_currentState.AmbientEnergy = Mathf.Lerp(_fromState.AmbientEnergy, _toState.AmbientEnergy, t);
		_currentState.AmbientColor = _fromState.AmbientColor.Lerp(_toState.AmbientColor, t);

		_currentState.FogEnabled = _toState.FogEnabled || _fromState.FogEnabled;
		_currentState.FogDensity = Mathf.Lerp(_fromState.FogDensity, _toState.FogDensity, t);
		_currentState.FogColor = _fromState.FogColor.Lerp(_toState.FogColor, t);

		_currentState.SsaoEnabled = _toState.SsaoEnabled;
		_currentState.SsaoRadius = Mathf.Lerp(_fromState.SsaoRadius, _toState.SsaoRadius, t);
		_currentState.SsaoIntensity = Mathf.Lerp(_fromState.SsaoIntensity, _toState.SsaoIntensity, t);
		_currentState.SsaoDetail = Mathf.Lerp(_fromState.SsaoDetail, _toState.SsaoDetail, t);

		_currentState.TonemapExposure = Mathf.Lerp(_fromState.TonemapExposure, _toState.TonemapExposure, t);
		_currentState.AdjustmentContrast = Mathf.Lerp(_fromState.AdjustmentContrast, _toState.AdjustmentContrast, t);
		_currentState.AdjustmentSaturation = Mathf.Lerp(_fromState.AdjustmentSaturation, _toState.AdjustmentSaturation, t);
		_currentState.GlowIntensity = Mathf.Lerp(_fromState.GlowIntensity, _toState.GlowIntensity, t);
		_currentState.GlowBloom = Mathf.Lerp(_fromState.GlowBloom, _toState.GlowBloom, t);
		_currentState.GlowStrength = Mathf.Lerp(_fromState.GlowStrength, _toState.GlowStrength, t);

		_currentState.CharacterFillEnergy = Mathf.Lerp(_fromState.CharacterFillEnergy, _toState.CharacterFillEnergy, t);

		ApplyStateToHost(host, _currentState);

		if (t >= 1.0f)
		{
			_isTransitioning = false;
			_currentPresetId = _targetPresetId;
			_currentState = _toState;
			SetCurrentWeather(_toState.WeatherType);
			var targetPreset = GetPreset(_targetPresetId);
			if (targetPreset != null)
			{
				SetBaseFogDensity(targetPreset.BaseFogDensity);
			}
		}
	}

	private void ApplyStateToHost(Node3D host, LiveEnvironmentState state)
	{
		if (host == null || !GodotObject.IsInstanceValid(host)) return;

		var worldEnv = host.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
		var sun = host.GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");

		if (worldEnv != null && worldEnv.Environment != null)
		{
			var env = worldEnv.Environment;
			env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
			env.AmbientLightColor = state.AmbientColor;
			env.AmbientLightEnergy = state.AmbientEnergy;

			GameSettings.ApplyEnvironmentQuality(env, GameSettings.QualityIdx);

			if (GameSettings.QualityIdx > GraphicsQuality.Low)
			{
				env.SsaoEnabled = state.SsaoEnabled;
				env.SsaoRadius = state.SsaoRadius;
				env.SsaoIntensity = state.SsaoIntensity;
				env.SsaoDetail = state.SsaoDetail;

				env.GlowIntensity = state.GlowIntensity;
				env.GlowStrength = state.GlowStrength;
				env.GlowBloom = state.GlowBloom;
				env.GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive;
			}

			env.TonemapExposure = state.TonemapExposure;
			env.AdjustmentContrast = state.AdjustmentContrast;
			env.AdjustmentSaturation = state.AdjustmentSaturation;

			env.FogEnabled = state.FogEnabled;
			env.FogLightColor = state.FogColor;
			env.FogDensity = state.FogDensity;
		}

		if (sun != null && GodotObject.IsInstanceValid(sun))
		{
			sun.DirectionalShadowBlendSplits = true;
			sun.DirectionalShadowFadeStart = 0.8f;
			sun.ShadowBias = state.ShadowBias;
			sun.ShadowNormalBias = state.ShadowNormalBias;
			sun.LightColor = state.SunColor;
			sun.LightEnergy = state.SunEnergy;
			sun.LightSpecular = 0.5f;
			sun.RotationDegrees = new Vector3(state.SunPitch, state.SunYaw, 0f);
			GameSettings.ApplyDirectionalLightQuality(sun, GameSettings.QualityIdx);
		}

		var fillLight = host.GetNodeOrNull<Camera3D>("Camera3D")?.GetNodeOrNull<DirectionalLight3D>("CharacterFillLight");
		if (fillLight != null && GodotObject.IsInstanceValid(fillLight))
		{
			fillLight.LightEnergy = state.CharacterFillEnergy;
		}
	}

	private Entity FindWorldEntity()
	{
		Entity worldEntity = Entity.Null;
		var query = QueryCache.AllWeatherStateQuery;
		EcsWorld.Query(in query, entity => worldEntity = entity);
		return worldEntity;
	}

	public string GetCurrentWeather()
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && EcsWorld.IsAlive(worldEntity) && EcsWorld.Has<WeatherState>(worldEntity))
		{
			return EcsWorld.Get<WeatherState>(worldEntity).CurrentWeather;
		}
		return "clear";
	}

	public void SetCurrentWeather(string weather)
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && EcsWorld.IsAlive(worldEntity) && EcsWorld.Has<WeatherState>(worldEntity))
		{
			ref var state = ref EcsWorld.Get<WeatherState>(worldEntity);
			state.CurrentWeather = weather;
		}
	}

	public float GetBaseFogDensity()
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && EcsWorld.IsAlive(worldEntity) && EcsWorld.Has<WeatherState>(worldEntity))
		{
			return EcsWorld.Get<WeatherState>(worldEntity).BaseFogDensity;
		}
		return 0f;
	}

	public void SetBaseFogDensity(float density)
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && EcsWorld.IsAlive(worldEntity) && EcsWorld.Has<WeatherState>(worldEntity))
		{
			ref var state = ref EcsWorld.Get<WeatherState>(worldEntity);
			state.BaseFogDensity = density;
		}
	}

	public void UpdateEnvironmentalFog(Camera3D camera3D, WorldEnvironment worldEnv)
	{
		if (worldEnv == null || worldEnv.Environment == null) return;

		if (GameHost.Instance != null && GameHost.Instance.IsMapEditorMode)
		{
			worldEnv.Environment.FogEnabled = false;
			return;
		}

		float baseFogDensity = GetBaseFogDensity();
		if (baseFogDensity > 0f && camera3D != null && GodotObject.IsInstanceValid(camera3D))
		{
			worldEnv.Environment.FogEnabled = true;
			float height = camera3D.GlobalPosition.Y;
			float scale = 18.0f / Mathf.Max(8.0f, height);
			worldEnv.Environment.FogDensity = baseFogDensity * scale;
		}
		else
		{
			worldEnv.Environment.FogEnabled = false;
		}
	}

	public string CycleWeather()
	{
		string current = GetCurrentWeather();
		string next = current switch
		{
			"clear" => "rain",
			"rain" => "fog",
			"fog" => "clear",
			_ => "clear"
		};
		SetCurrentWeather(next);

		float density = next switch
		{
			"clear" => 0f,
			"rain" => 0.008f,
			"fog" => 0.045f,
			_ => 0f
		};
		SetBaseFogDensity(density);

		return next;
	}

	public bool OverrideDayNightVisuals { get; set; } = false;

	public static readonly float[] SunPitches       = { -58.0f, -45.0f, -50.0f, -45.0f };
	public static readonly float[] SunYaws          = {  29.0f,-115.0f, 155.0f,  95.0f };
	public static readonly float[] SunEnergies      = {   2.50f,  1.50f,   0.50f,  2.70f };
	public static readonly Color[] SunColors        = {
		new Color(1.000f, 0.980f, 0.940f),
		new Color(1.000f, 0.700f, 0.380f),
		new Color(0.700f, 0.880f, 1.000f),
		new Color(1.000f, 0.880f, 0.720f)
	};

	public static readonly float[] AmbientEnergies  = {   0.80f,  0.80f,   0.95f,  0.90f };
	public static readonly Color[] AmbientColors    = {
		new Color(0.480f, 0.580f, 0.740f),
		new Color(0.420f, 0.460f, 0.720f),
		new Color(0.280f, 0.420f, 0.850f),
		new Color(0.580f, 0.540f, 0.840f)
	};

	public static readonly float[] FogDensities     = { 0.0080f, 0.0120f, 0.0150f, 0.0120f };
	public static readonly Color[] FogColors        = {
		new Color(0.550f, 0.650f, 0.750f),
		new Color(0.450f, 0.300f, 0.350f),
		new Color(0.080f, 0.120f, 0.200f),
		new Color(0.400f, 0.450f, 0.550f)
	};

	public static readonly float[] SsaoIntensities  = {   0.40f,  0.30f,   0.20f,  0.30f };
	public static readonly float[] SsaoRadii        = {   1.20f,  1.20f,   1.00f,  1.10f };

	public static readonly float[] Exposures        = {   1.18f,  1.14f,   1.14f,  1.18f };
	public static readonly float[] Contrasts        = {   1.02f,  1.02f,   1.00f,  1.02f };
	public static readonly float[] Saturations      = {   1.06f,  0.98f,   1.06f,  1.04f };

	public static readonly float[] GlowIntensities  = {   0.15f,  0.30f,   0.25f,  0.20f };
	public static readonly float[] GlowBlooms       = {   0.14f,  0.14f,   0.08f,  0.10f };

	public void UpdateDayNightVisuals(Node3D host, float progress)
	{
		if (OverrideDayNightVisuals) return;
		if (GameSettings.DisableDayNightLighting)
		{
			progress = 0f;
		}

		float normalizedProgress = Mathf.PosMod(progress, 1.0f);
		float segment = normalizedProgress * 4.0f;
		int phaseIndex = (int)Mathf.Floor(segment);
		float t = segment - phaseIndex;
		int nextIndex = (phaseIndex + 1) % 4;

		_currentState.AmbientColor = AmbientColors[phaseIndex].Lerp(AmbientColors[nextIndex], t);
		_currentState.AmbientEnergy = Mathf.Lerp(AmbientEnergies[phaseIndex], AmbientEnergies[nextIndex], t);

		_currentState.SsaoEnabled = true;
		_currentState.SsaoRadius = Mathf.Lerp(SsaoRadii[phaseIndex], SsaoRadii[nextIndex], t);
		_currentState.SsaoIntensity = Mathf.Lerp(SsaoIntensities[phaseIndex], SsaoIntensities[nextIndex], t);
		_currentState.SsaoDetail = 0.5f;

		_currentState.GlowIntensity = Mathf.Lerp(GlowIntensities[phaseIndex], GlowIntensities[nextIndex], t);
		_currentState.GlowStrength = 0.90f;
		_currentState.GlowBloom = Mathf.Lerp(GlowBlooms[phaseIndex], GlowBlooms[nextIndex], t);

		_currentState.TonemapExposure = Mathf.Lerp(Exposures[phaseIndex], Exposures[nextIndex], t);
		_currentState.AdjustmentContrast = Mathf.Lerp(Contrasts[phaseIndex], Contrasts[nextIndex], t);
		_currentState.AdjustmentSaturation = Mathf.Lerp(Saturations[phaseIndex], Saturations[nextIndex], t);

		_currentState.FogEnabled = true;
		_currentState.FogColor = FogColors[phaseIndex].Lerp(FogColors[nextIndex], t);
		_currentState.FogDensity = Mathf.Lerp(FogDensities[phaseIndex], FogDensities[nextIndex], t);

		_currentState.SunColor = SunColors[phaseIndex].Lerp(SunColors[nextIndex], t);
		_currentState.SunEnergy = Mathf.Lerp(SunEnergies[phaseIndex], SunEnergies[nextIndex], t);

		float radSunPitch = Mathf.LerpAngle(Mathf.DegToRad(SunPitches[phaseIndex]), Mathf.DegToRad(SunPitches[nextIndex]), t);
		float radSunYaw   = Mathf.LerpAngle(Mathf.DegToRad(SunYaws[phaseIndex]),   Mathf.DegToRad(SunYaws[nextIndex]),   t);

		_currentState.SunPitch = Mathf.RadToDeg(radSunPitch);
		_currentState.SunYaw = Mathf.RadToDeg(radSunYaw);
		_currentState.ShadowBias = 0.03f;
		_currentState.ShadowNormalBias = 1.2f;

		float[] fillEnergies = { 0.15f, 0.20f, 0.35f, 0.22f };
		_currentState.CharacterFillEnergy = Mathf.Lerp(fillEnergies[phaseIndex], fillEnergies[nextIndex], t);

		ApplyStateToHost(host, _currentState);
	}

	public (int TimeOfDayIndex, float TimeOfDayTimer) CycleTimeOfDay(Node3D host, Entity worldEntity, float cycleDuration)
	{
		if (!EcsWorld.IsAlive(worldEntity) || !EcsWorld.Has<WorldState>(worldEntity))
		{
			return (0, 0f);
		}

		ref var state = ref EcsWorld.Get<WorldState>(worldEntity);
		int nextIndex = (state.TimeOfDayIndex + 1) % 4;

		float progress = nextIndex * 0.25f;
		float nextTimer = progress * cycleDuration;

		UpdateDayNightVisuals(host, progress);

		EcsWorld.Set(worldEntity, new WorldState(state.GameElapsedTime, nextIndex, nextTimer, state.DayNightCycleEnabled));

		return (nextIndex, nextTimer);
	}

	public string GetTimeOfDayName(int timeOfDayIndex)
	{
		return timeOfDayIndex switch
		{
			0 => "Day",
			1 => "Dusk",
			2 => "Night",
			3 => "Dawn",
			_ => "Unknown"
		};
	}
}