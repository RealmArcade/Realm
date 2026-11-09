using Godot;
using Realm.Client.Core;
using Realm.Client.UI;

namespace Realm.Client.Services;

/// <summary>
/// Owns the single particle node that draws rain or snow around the camera, and the fog side-effects of a weather
/// change. <see cref="EnvironmentService"/> keeps the public weather surface and forwards here.
/// </summary>
public sealed class WeatherParticleEffects
{
	private CpuParticles3D? _weatherParticles = null;
	private string _activeWeatherVisualType = "clear";
	private int _activeParticleDensity = 0;

	public static Node3D? GetEnvironmentHost()
	{
		Node3D? host = GameContext.Host;
		return host != null && GodotObject.IsInstanceValid(host) ? host : null;
	}

	public void ApplyWeatherVisuals(string weatherType, int particleDensity = 0)
	{
		if (GameSettings.DisableWeatherEffects)
		{
			Cleanup();
			return;
		}

		Node3D? host = GetEnvironmentHost();
		if (host == null) return;

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
		UpdateWeatherParticlePosition(GameContext.Host?.MainCamera);
	}

	private static bool IsValidWeather(string weatherType)
	{
		return weatherType == "rain" || weatherType == "snow";
	}

	private static int GetParticleDensity(string weatherType, int particleDensity)
	{
		if (particleDensity > 0) return particleDensity;
		return weatherType == "rain" ? 800 : 600;
	}

	private static Node3D GetWeatherHost(Node3D host)
	{
		if (host is GameHost && GameContext.Host?.MainNode is Node3D node3D)
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
			if (_weatherParticles is { } particles) particles.Amount = Mathf.Clamp(newCount, 10, 5000);
		}

		UpdateWeatherParticlePosition(GameContext.Host?.MainCamera);
	}

	private static CpuParticles3D InitializeWeatherParticles(int count)
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

	private static void ConfigureParticleType(CpuParticles3D particles, string weatherType)
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

	/// <summary>
	/// Applies a weather's particles, and turns fog off on the host for clear weather. Non-clear fog is left to
	/// <see cref="EnvironmentService.UpdateEnvironmentalFog"/>.
	/// </summary>
	public void ApplyWeatherToTarget(Node3D? targetHost, string weather, int particleDensity)
	{
		if (targetHost == null) return;

		ApplyWeatherVisuals(weather, particleDensity);

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
}
