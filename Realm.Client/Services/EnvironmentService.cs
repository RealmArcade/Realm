using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.UI;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Environment = Godot.Environment;

namespace Realm.Client.Services;

public class EnvironmentService
{
	public void SetActiveWaterProfileIndex(byte index)
	{
		if (GameHost.Instance != null)
		{
			GameHost.Instance.ActiveWaterProfileIndex = index;
		}
	}

	public float TimeOfDayCycleDuration { get; set; } = 90f;
	public float TuneCliffJitterStrength { get; set; } = 1.0f;
	public float TuneCliffJitterScale { get; set; } = 0.20f;
	public float TuneCliffRimNoiseStrength { get; set; } = 0.30f;
	public float TuneHeightBlendSoftness { get; set; } = 0.04f;
	public float TuneBlendNoiseStrength { get; set; } = 0.22f;
	public float TuneBlendNoiseScale { get; set; } = 0.22f;

	private readonly List<EnvironmentPresetConfig> _presets = new();
	private string _currentPresetId = "day";
	private string _targetPresetId = "day";
	private bool _isTransitioning = false;
	private float _transitionDuration = 0f;
	private float _transitionElapsed = 0f;

	private LiveEnvironmentState _currentState;
	private LiveEnvironmentState _fromState;
	private LiveEnvironmentState _toState;

	private CpuParticles3D? _weatherParticles = null;
	private string _activeWeatherVisualType = "clear";
	private int _activeParticleDensity = 0;

	public EnvironmentService()
	{
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
		ApplyWeatherVisuals(host, preset.WeatherType ?? "clear", preset.RainParticleDensity);
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
				ApplyWeatherVisuals(host, targetPreset.WeatherType ?? "clear", targetPreset.RainParticleDensity);
			}
		}
	}

	private Node3D? _cachedHost;
	private WorldEnvironment? _cachedWorldEnv;
	private DirectionalLight3D? _cachedSun;
	private DirectionalLight3D? _cachedFillLight;
	private GraphicsQuality? _lastAppliedEnvQuality;
	private GraphicsQuality? _lastAppliedSunQuality;

	private void EnsureHostNodesCached(Node3D host)
	{
		if (!ReferenceEquals(_cachedHost, host))
		{
			_cachedHost = host;
			_cachedWorldEnv = null;
			_cachedSun = null;
			_cachedFillLight = null;
			_lastAppliedEnvQuality = null;
			_lastAppliedSunQuality = null;
		}

		if (_cachedWorldEnv == null || !GodotObject.IsInstanceValid(_cachedWorldEnv))
		{
			_cachedWorldEnv = host.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
			_lastAppliedEnvQuality = null;
		}

		if (_cachedSun == null || !GodotObject.IsInstanceValid(_cachedSun))
		{
			_cachedSun = host.GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
			_lastAppliedSunQuality = null;
		}

		if (_cachedFillLight == null || !GodotObject.IsInstanceValid(_cachedFillLight))
		{
			_cachedFillLight = host.GetNodeOrNull<Camera3D>("Camera3D")?.GetNodeOrNull<DirectionalLight3D>("CharacterFillLight");
		}
	}

	private void ApplyStateToHost(Node3D host, LiveEnvironmentState state)
	{
		if (host == null || !GodotObject.IsInstanceValid(host)) return;

		EnsureHostNodesCached(host);
		ApplyWorldEnvironmentState(state);
		ApplySunState(state);
		ApplyFillLightState(state);
	}

	private void ApplyWorldEnvironmentState(LiveEnvironmentState state)
	{
		var worldEnv = _cachedWorldEnv;
		if (worldEnv == null || worldEnv.Environment == null) return;

		var env = worldEnv.Environment;
		env.AmbientLightSource = Environment.AmbientSource.Color;
		env.AmbientLightColor = state.AmbientColor;
		env.AmbientLightEnergy = state.AmbientEnergy;

		if (_lastAppliedEnvQuality != GameSettings.QualityIdx)
		{
			_lastAppliedEnvQuality = GameSettings.QualityIdx;
			GameSettings.ApplyEnvironmentQuality(env, GameSettings.QualityIdx);
		}

		if (GameSettings.QualityIdx > GraphicsQuality.Low)
		{
			env.SsaoEnabled = state.SsaoEnabled;
			env.SsaoRadius = state.SsaoRadius;
			env.SsaoIntensity = state.SsaoIntensity;
			env.SsaoDetail = state.SsaoDetail;

			env.GlowIntensity = state.GlowIntensity;
			env.GlowStrength = state.GlowStrength;
			env.GlowBloom = state.GlowBloom;
			env.GlowBlendMode = Environment.GlowBlendModeEnum.Additive;
		}

		env.TonemapExposure = state.TonemapExposure;
		env.AdjustmentContrast = state.AdjustmentContrast;
		env.AdjustmentSaturation = state.AdjustmentSaturation;

		string weather = state.WeatherType ?? "clear";
		bool isFogActive = state.FogEnabled && (state.FogDensity > 0f || weather != "clear" || GetBaseFogDensity() > 0f);
		env.FogEnabled = isFogActive;
		env.FogLightColor = state.FogColor;
		env.FogDensity = isFogActive ? state.FogDensity : 0f;
	}

	private void ApplySunState(LiveEnvironmentState state)
	{
		var sun = _cachedSun;
		if (sun == null || !GodotObject.IsInstanceValid(sun)) return;

		sun.DirectionalShadowBlendSplits = true;
		sun.DirectionalShadowFadeStart = 0.8f;
		sun.ShadowBias = state.ShadowBias;
		sun.ShadowNormalBias = state.ShadowNormalBias;
		sun.LightColor = state.SunColor;
		sun.LightEnergy = state.SunEnergy;
		sun.LightSpecular = 0.5f;

		Vector3 currentRot = sun.RotationDegrees;
		if (Mathf.Abs(currentRot.X - state.SunPitch) > 0.05f || Mathf.Abs(currentRot.Y - state.SunYaw) > 0.05f || Mathf.Abs(currentRot.Z) > 0.05f)
		{
			sun.RotationDegrees = new Vector3(state.SunPitch, state.SunYaw, 0f);
		}

		if (_lastAppliedSunQuality != GameSettings.QualityIdx)
		{
			_lastAppliedSunQuality = GameSettings.QualityIdx;
			GameSettings.ApplyDirectionalLightQuality(sun, GameSettings.QualityIdx);
		}
	}

	private void ApplyFillLightState(LiveEnvironmentState state)
	{
		var fillLight = _cachedFillLight;
		if (fillLight == null || !GodotObject.IsInstanceValid(fillLight)) return;

		fillLight.LightEnergy = state.CharacterFillEnergy;
	}

	private Entity FindWorldEntity()
	{
		if (ECSContext.WorldEntity != Entity.Null && GameContext.World != null && GameContext.World.IsAlive(ECSContext.WorldEntity))
		{
			return ECSContext.WorldEntity;
		}

		return Entity.Null;
	}

	public string GetCurrentWeather()
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && GameContext.World.IsAlive(worldEntity) && GameContext.World.Has<WeatherState>(worldEntity))
		{
			return GameContext.World.Get<WeatherState>(worldEntity).CurrentWeather;
		}

		return "clear";
	}

	public void SetCurrentWeather(string weather)
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && GameContext.World.IsAlive(worldEntity) && GameContext.World.Has<WeatherState>(worldEntity))
		{
			ref var state = ref GameContext.World.Get<WeatherState>(worldEntity);
			state.CurrentWeather = weather;
		}
	}

	public float GetBaseFogDensity()
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && GameContext.World.IsAlive(worldEntity) && GameContext.World.Has<WeatherState>(worldEntity))
		{
			return GameContext.World.Get<WeatherState>(worldEntity).BaseFogDensity;
		}

		return 0f;
	}

	public void SetBaseFogDensity(float density)
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && GameContext.World.IsAlive(worldEntity) && GameContext.World.Has<WeatherState>(worldEntity))
		{
			ref var state = ref GameContext.World.Get<WeatherState>(worldEntity);
			state.BaseFogDensity = density;
		}
	}

	public void UpdateEnvironmentalFog(Camera3D camera3D, WorldEnvironment worldEnv)
	{
		if (worldEnv?.Environment == null) return;

		var env = worldEnv.Environment;
		bool enableFog = ShouldEnableFog();

		if (!enableFog)
		{
			if (env.FogEnabled)
			{
				env.FogEnabled = false;
			}
			return;
		}

		if (!env.FogEnabled)
		{
			env.FogEnabled = true;
		}

		if (env.FogLightColor != _currentState.FogColor)
		{
			env.FogLightColor = _currentState.FogColor;
		}

		float targetDensity = CalculateFogDensity(camera3D, GetBaseFogDensity());
		if (Mathf.Abs(env.FogDensity - targetDensity) > 0.00001f)
		{
			env.FogDensity = targetDensity;
		}
	}

	private bool ShouldEnableFog()
	{
		if (!_currentState.FogEnabled) return false;
		if (GetCurrentWeather() != "clear") return true;
		if (GetBaseFogDensity() > 0f) return true;
		return _currentState.FogDensity > 0f;
	}

	private float CalculateFogDensity(Camera3D camera3D, float baseFogDensity)
	{
		bool isValidCamera = camera3D != null && GodotObject.IsInstanceValid(camera3D);
		if (!isValidCamera || baseFogDensity <= 0f)
		{
			return _currentState.FogDensity;
		}

		float height = camera3D.GlobalPosition.Y;
		float scale = 18.0f / Mathf.Max(8.0f, height);
		float scaledDensity = baseFogDensity * scale;

		if (_currentState.FogDensity > 0f)
		{
			return _currentState.FogDensity + scaledDensity;
		}

		return scaledDensity;
	}

	public void ApplyWeatherVisuals(Node3D host, string weatherType, int particleDensity = 0)
	{
		if (host == null || !GodotObject.IsInstanceValid(host)) return;

		weatherType = (weatherType ?? "clear").Trim().ToLowerInvariant();

		if (!IsValidWeather(weatherType))
		{
			Cleanup();
			return;
		}

		int count = GetParticleDensity(weatherType, particleDensity);

		if (_activeWeatherVisualType == weatherType && GodotObject.IsInstanceValid(_weatherParticles))
		{
			UpdateExistingWeatherParticles(count);
			return;
		}

		Cleanup();

		_activeWeatherVisualType = weatherType;
		_activeParticleDensity = count;

		_weatherParticles = InitializeWeatherParticles(count);
		ConfigureParticleType(_weatherParticles, weatherType);

		Node3D parentNode = GetWeatherHost(host);
		parentNode.AddChild(_weatherParticles);
		UpdateWeatherParticlePosition(GameHost.Instance?.MainCamera);
	}

	private bool IsValidWeather(string weatherType)
	{
		return weatherType == "rain" || weatherType == "snow";
	}

	private int GetParticleDensity(string weatherType, int particleDensity)
	{
		if (particleDensity > 0) return particleDensity;
		return weatherType == "rain" ? 800 : 600;
	}

	private Node3D GetWeatherHost(Node3D host)
	{
		if (host is GameHost && GameHost.Instance?.MainNode is Node3D node3D)
		{
			return node3D;
		}

		return host;
	}

	private void UpdateExistingWeatherParticles(int newCount)
	{
		if (_activeParticleDensity != newCount)
		{
			_activeParticleDensity = newCount;
			_weatherParticles.Amount = Mathf.Clamp(newCount, 10, 5000);
		}

		UpdateWeatherParticlePosition(GameHost.Instance?.MainCamera);
	}

	private CpuParticles3D InitializeWeatherParticles(int count)
	{
		var particles = new CpuParticles3D
		{
			Name = "ActiveWeatherParticles",
			Amount = Mathf.Clamp(count, 10, 5000),
			Preprocess = 2.0f,
			EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
			EmissionBoxExtents = new Vector3(150f, 1f, 150f)
		};
		return particles;
	}

	private void ConfigureParticleType(CpuParticles3D particles, string weatherType)
	{
		if (weatherType == "rain")
		{
			particles.Lifetime = 2.0f;
			var mesh = new BoxMesh { Size = new Vector3(0.05f, 1.5f, 0.05f) };
			var mat = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.5f, 0.6f, 0.9f, 0.4f),
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
			};
			mesh.Material = mat;
			particles.Mesh = mesh;
			particles.Direction = new Vector3(0.1f, -1f, 0f);
			particles.Spread = 5f;
			particles.InitialVelocityMin = 20f;
			particles.InitialVelocityMax = 30f;
		}
		else if (weatherType == "snow")
		{
			particles.Lifetime = 4.0f;
			particles.Preprocess = 4.0f;
			var mesh = new BoxMesh { Size = new Vector3(0.12f, 0.12f, 0.12f) };
			var mat = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.95f, 0.95f, 1.0f, 0.8f),
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
			};
			mesh.Material = mat;
			particles.Mesh = mesh;
			particles.Direction = new Vector3(0.1f, -1f, 0.05f);
			particles.Spread = 20f;
			particles.InitialVelocityMin = 2f;
			particles.InitialVelocityMax = 5f;
		}
	}

	public void UpdateWeatherParticlePosition(Camera3D? camera)
	{
		if (GodotObject.IsInstanceValid(_weatherParticles))
		{
			if (camera != null && GodotObject.IsInstanceValid(camera))
			{
				_weatherParticles.GlobalPosition = new Vector3(camera.GlobalPosition.X, Mathf.Max(35f, camera.GlobalPosition.Y + 10f), camera.GlobalPosition.Z);
			}
			else
			{
				_weatherParticles.GlobalPosition = new Vector3(0f, 35f, 0f);
			}
		}
	}

	public void Cleanup()
	{
		if (GodotObject.IsInstanceValid(_weatherParticles))
		{
			_weatherParticles.QueueFree();
			_weatherParticles = null;
		}

		_activeWeatherVisualType = "clear";
		_activeParticleDensity = 0;
	}

	public string SetWeather(string weatherType, Node3D? host = null)
	{
		string weather = NormalizeWeatherType(weatherType);
		SetCurrentWeather(weather);

		var settings = GetWeatherSettings(weather);
		SetBaseFogDensity(settings.Density);

		_currentState.WeatherType = weather;
		_currentState.FogEnabled = weather != "clear";

		if (weather == "clear")
		{
			_currentState.FogDensity = 0f;
		}

		ApplyWeatherToTarget(host ?? GameHost.Instance, weather, settings.ParticleDensity);

		return weather;
	}

	private string NormalizeWeatherType(string? weatherType)
	{
		return weatherType?.ToLowerInvariant() switch
		{
			"rain" => "rain",
			"snow" => "snow",
			"fog" => "fog",
			_ => "clear"
		};
	}

	private (float Density, int ParticleDensity) GetWeatherSettings(string weather)
	{
		return weather switch
		{
			"rain" => (0.0075f, 800),
			"snow" => (0.005f, 600),
			"fog" => (0.045f, 0),
			_ => (0f, 0)
		};
	}

	private void ApplyWeatherToTarget(Node3D? targetHost, string weather, int particleDensity)
	{
		if (targetHost == null) return;

		ApplyWeatherVisuals(targetHost, weather, particleDensity);

		if (weather == "clear")
		{
			var worldEnv = targetHost.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
			if (worldEnv?.Environment != null)
			{
				worldEnv.Environment.FogEnabled = false;
				worldEnv.Environment.FogDensity = 0f;
			}
		}
	}

	public string CycleWeather(Node3D? host = null)
	{
		string current = GetCurrentWeather();
		string next = current switch
		{
			"clear" => "rain",
			"rain" => "snow",
			"snow" => "fog",
			"fog" => "clear",
			_ => "clear"
		};
		return SetWeather(next, host);
	}

	public bool OverrideDayNightVisuals { get; set; } = false;

	public static readonly float[] SunPitches = { -58.0f, -45.0f, -50.0f, -45.0f };
	public static readonly float[] SunYaws = { 29.0f, -115.0f, 155.0f, 95.0f };
	public static readonly float[] SunEnergies = { 1.65f, 1.35f, 0.45f, 1.70f };

	public static readonly Color[] SunColors =
	{
		new Color(1.000f, 0.980f, 0.940f),
		new Color(1.000f, 0.700f, 0.380f),
		new Color(0.700f, 0.880f, 1.000f),
		new Color(1.000f, 0.880f, 0.720f)
	};

	public static readonly float[] AmbientEnergies = { 0.70f, 0.75f, 0.95f, 0.75f };

	public static readonly Color[] AmbientColors =
	{
		new Color(0.480f, 0.580f, 0.740f),
		new Color(0.420f, 0.460f, 0.720f),
		new Color(0.280f, 0.420f, 0.850f),
		new Color(0.580f, 0.540f, 0.840f)
	};

	public static readonly float[] FogDensities = { 0.0080f, 0.0120f, 0.0150f, 0.0120f };

	public static readonly Color[] FogColors =
	{
		new Color(0.550f, 0.650f, 0.750f),
		new Color(0.450f, 0.300f, 0.350f),
		new Color(0.080f, 0.120f, 0.200f),
		new Color(0.400f, 0.450f, 0.550f)
	};

	public static readonly float[] SsaoIntensities = { 0.35f, 0.30f, 0.20f, 0.30f };
	public static readonly float[] SsaoRadii = { 1.20f, 1.20f, 1.00f, 1.10f };

	public static readonly float[] Exposures = { 1.08f, 1.10f, 1.12f, 1.08f };
	public static readonly float[] Contrasts = { 1.04f, 1.04f, 1.02f, 1.04f };
	public static readonly float[] Saturations = { 1.06f, 1.00f, 1.08f, 1.06f };

	public static readonly float[] GlowIntensities = { 0.15f, 0.30f, 0.25f, 0.20f };
	public static readonly float[] GlowBlooms = { 0.14f, 0.14f, 0.08f, 0.10f };

	public bool SyncWorldStateDomain(float gameElapsedTime, int timeOfDayIndex, float timeOfDayTimer, bool dayNightCycleEnabled)
	{
				var ecsWorld = GameContext.World;
		if (ecsWorld != null && ECSContext.WorldEntity != Entity.Null && ecsWorld.IsAlive(ECSContext.WorldEntity))
		{
			ecsWorld.SetOrAdd(ECSContext.WorldEntity, new WorldState(gameElapsedTime, timeOfDayIndex, timeOfDayTimer, dayNightCycleEnabled));
			return true;
		}
		return false;
	}

	public (bool UpdateVisuals, float Progress) UpdateClientWorldStateDomain(float fDelta, bool isMapEditorMode, bool advanceSimulationClock)
	{
				if (GameContext.World == null || !GameContext.World.IsAlive(ECSContext.WorldEntity) || !GameContext.World.Has<WorldState>(ECSContext.WorldEntity))
			return (false, 0f);

		var state = GameContext.World.Get<WorldState>(ECSContext.WorldEntity);
		if (!advanceSimulationClock)
		{
			float replicatedProgress = state.TimeOfDayTimer / TimeOfDayCycleDuration;
			return (state.DayNightCycleEnabled && !isMapEditorMode, replicatedProgress);
		}

		float elapsed = state.GameElapsedTime + fDelta;
		float timer = state.TimeOfDayTimer;
		int index = state.TimeOfDayIndex;
		bool updateVisuals = false;
		float progress = 0f;

		if (state.DayNightCycleEnabled)
		{
			timer += fDelta;
			if (timer >= TimeOfDayCycleDuration)
			{
				timer -= TimeOfDayCycleDuration;
			}

			progress = timer / TimeOfDayCycleDuration;
			index = (int)(progress * 4f) % 4;

			if (!isMapEditorMode)
			{
				updateVisuals = true;
			}
		}

		GameContext.World.Set(ECSContext.WorldEntity, new WorldState(elapsed, index, timer, state.DayNightCycleEnabled));
		return (updateVisuals, progress);
	}

	private float _lastAppliedDayNightProgress = -1f;
	private string _lastAppliedDayNightWeather = string.Empty;
	private GraphicsQuality? _lastDayNightQualityIdx;

	public void UpdateDayNightVisuals(Node3D host, float progress)
	{
		if (OverrideDayNightVisuals) return;
		if (GameSettings.DisableDayNightLighting)
		{
			progress = 0f;
		}

		float normalizedProgress = Mathf.PosMod(progress, 1.0f);
		string weather = GetCurrentWeather();
		if (ReferenceEquals(_cachedHost, host) &&
		    _lastDayNightQualityIdx == GameSettings.QualityIdx &&
		    _lastAppliedDayNightWeather == weather &&
		    _lastAppliedDayNightProgress >= 0f &&
		    Mathf.Abs(normalizedProgress - _lastAppliedDayNightProgress) < 0.0005f)
		{
			return;
		}

		_lastAppliedDayNightProgress = normalizedProgress;
		_lastAppliedDayNightWeather = weather;
		_lastDayNightQualityIdx = GameSettings.QualityIdx;

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

		float baseFog = GetBaseFogDensity();
		bool hasFog = baseFog > 0f || (weather != "clear" && weather != "");
		_currentState.FogEnabled = hasFog;
		_currentState.FogColor = FogColors[phaseIndex].Lerp(FogColors[nextIndex], t);
		_currentState.FogDensity = hasFog ? Mathf.Lerp(FogDensities[phaseIndex], FogDensities[nextIndex], t) : 0f;

		_currentState.SunColor = SunColors[phaseIndex].Lerp(SunColors[nextIndex], t);
		_currentState.SunEnergy = Mathf.Lerp(SunEnergies[phaseIndex], SunEnergies[nextIndex], t);

		float radSunPitch = Mathf.LerpAngle(Mathf.DegToRad(SunPitches[phaseIndex]), Mathf.DegToRad(SunPitches[nextIndex]), t);
		float radSunYaw = Mathf.LerpAngle(Mathf.DegToRad(SunYaws[phaseIndex]), Mathf.DegToRad(SunYaws[nextIndex]), t);

		_currentState.SunPitch = Mathf.RadToDeg(radSunPitch);
		_currentState.SunYaw = Mathf.RadToDeg(radSunYaw);
		_currentState.ShadowBias = 0.03f;
		_currentState.ShadowNormalBias = 1.2f;

		float[] fillEnergies = { 0.15f, 0.20f, 0.35f, 0.22f };
		_currentState.CharacterFillEnergy = Mathf.Lerp(fillEnergies[phaseIndex], fillEnergies[nextIndex], t);

		ApplyStateToHost(host, _currentState);
	}

	public (int TimeOfDayIndex, float TimeOfDayTimer) SetTimeOfDay(Node3D host, Entity worldEntity, int timeOfDayIndex, float cycleDuration)
	{
		if (!GameContext.World.IsAlive(worldEntity) || !GameContext.World.Has<WorldState>(worldEntity))
		{
			return (0, 0f);
		}

		ref var state = ref GameContext.World.Get<WorldState>(worldEntity);
		int targetIndex = Mathf.Clamp(timeOfDayIndex, 0, 3);

		float progress = targetIndex * 0.25f;
		float nextTimer = progress * cycleDuration;

		UpdateDayNightVisuals(host, progress);

		GameContext.World.Set(worldEntity, new WorldState(state.GameElapsedTime, targetIndex, nextTimer, state.DayNightCycleEnabled));

		return (targetIndex, nextTimer);
	}

	public (int TimeOfDayIndex, float TimeOfDayTimer) CycleTimeOfDay(Node3D host, Entity worldEntity, float cycleDuration)
	{
		if (!GameContext.World.IsAlive(worldEntity) || !GameContext.World.Has<WorldState>(worldEntity))
		{
			return (0, 0f);
		}

		ref var state = ref GameContext.World.Get<WorldState>(worldEntity);
		int nextIndex = (state.TimeOfDayIndex + 1) % 4;
		return SetTimeOfDay(host, worldEntity, nextIndex, cycleDuration);
	}

	public (string timeName, string icon) SetEnvironmentLightingDomain(int index)
	{
		if (GameHost.Instance == null)
			return ("Day", "☀️");

		var res = GameHost.Instance.SetTimeOfDay(index);
		string timeName = GetTimeOfDayName(res.TimeOfDayIndex);
		string icon = res.TimeOfDayIndex switch
		{
			0 => "☀️",
			1 => "🌅",
			2 => "🌙",
			3 => "🌄",
			_ => "☀️"
		};
		return (timeName, icon);
	}

	public (string applied, string icon) SetEnvironmentWeatherDomain(int index)
	{
		string weatherType = index switch
		{
			1 => "rain",
			2 => "snow",
			3 => "fog",
			_ => "clear"
		};
		string applied = SetWeather(weatherType, GameHost.Instance);
		string icon = applied switch
		{
			"rain" => "🌧️",
			"snow" => "❄️",
			"fog" => "🌫️",
			_ => "☀️"
		};
		return (applied, icon);
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

	public void ApplyLiveLightingTuning(EditableTerrain terrain)
	{
		if (terrain?.Material != null)
		{
			terrain.CliffJitterStrength = TuneCliffJitterStrength;
			terrain.CliffJitterScale = TuneCliffJitterScale;
			terrain.CliffRimNoiseStrength = TuneCliffRimNoiseStrength;
			terrain.BlendSoftness = TuneHeightBlendSoftness;
			terrain.BlendNoiseStrength = TuneBlendNoiseStrength;
			terrain.BlendNoiseScale = TuneBlendNoiseScale;

			terrain.Material.SetShaderParameter("cliff_jitter_strength", TuneCliffJitterStrength);
			terrain.Material.SetShaderParameter("cliff_jitter_scale", TuneCliffJitterScale);
			terrain.Material.SetShaderParameter("blend_softness", TuneHeightBlendSoftness);
			terrain.Material.SetShaderParameter("blend_noise_strength", TuneBlendNoiseStrength);
			terrain.Material.SetShaderParameter("blend_noise_scale", TuneBlendNoiseScale);
		}
	}

	public float ProcessTimeOfDay(float time)
	{
		float clampedTime = Mathf.Clamp(time, 0f, 24f);

		int index;
		if (clampedTime >= 5f && clampedTime < 6f) index = 3;
		else if (clampedTime >= 6f && clampedTime < 18f) index = 0;
		else if (clampedTime >= 18f && clampedTime < 20f) index = 1;
		else index = 2;

		float timer = (clampedTime / 24f) * TimeOfDayCycleDuration;

				if (GameContext.World != null && GameContext.World.IsAlive(ECSContext.WorldEntity) && GameContext.World.Has<WorldState>(ECSContext.WorldEntity))
		{
			GameContext.World.Mutate<WorldState>(ECSContext.WorldEntity, (ref WorldState state) =>
				GameContext.World.Set(ECSContext.WorldEntity, new WorldState(state.GameElapsedTime, index, timer, state.DayNightCycleEnabled)));
		}

		return clampedTime / 24f;
	}

	public string GetCurrentEnvironmentPresetDomain()
	{
		return GetCurrentPresetId() ?? "day";
	}

	public string GetWeatherDomain()
	{
		return GetCurrentWeather() ?? "clear";
	}
}