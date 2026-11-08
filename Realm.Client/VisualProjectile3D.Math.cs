using System;
using System.Collections.Generic;
using System.IO;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Services.ModelOptimization;
using Realm.Client.Utils;
using Realm.Client.VFX;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Services;
using Realm.Shared.Textures;
using FileAccess = Godot.FileAccess;

namespace Realm.Client;

public partial class VisualProjectile3D : Node3D
{
	private static float CalculateSpeed(float baseSpeed, float elapsedTime, float totalDuration, ProjectileSpeedCurve speedCurve, float acceleration)
	{
		float speed = Mathf.Max(0.1f, baseSpeed + (acceleration * elapsedTime));

		if (speedCurve == ProjectileSpeedCurve.Constant)
			return speed;

		float progress = Mathf.Clamp(elapsedTime / Mathf.Max(0.001f, totalDuration), 0.0f, 1.0f);

		return speedCurve switch
		{
			ProjectileSpeedCurve.Accelerate => speed * (0.3f + 1.4f * progress * progress),
			ProjectileSpeedCurve.Decelerate => speed * (1.7f - 1.4f * progress * progress),
			ProjectileSpeedCurve.EaseInOut => speed * (0.4f + 1.2f * (progress * progress * (3.0f - 2.0f * progress))),
			ProjectileSpeedCurve.RocketBoost => speed * (0.15f + 2.35f * Mathf.Pow(progress, 3.0f)),
			ProjectileSpeedCurve.Burst => speed * (1.0f + 1.5f * Mathf.Exp(-4.0f * progress)),
			_ => speed,
		};
	}

	private static float CalculateSquashStretchScale(float t)
	{
		if (t < 0.2f) return Mathf.Max(0.001f, (t / 0.2f) * 1.2f);
		if (t < 0.4f) return Mathf.Max(0.001f, 1.2f - ((t - 0.2f) / 0.2f) * 0.2f);
		if (t > 0.85f) return Mathf.Max(0.001f, (1.0f - t) / 0.15f);
		return 1.0f;
	}

	private static float CalculateScaleOverLifetime(float t, ProjectileScaleCurve scaleCurve)
	{
		if (scaleCurve == ProjectileScaleCurve.Constant)
			return 1.0f;

		return scaleCurve switch
		{
			ProjectileScaleCurve.Grow => Mathf.Clamp(t * 1.5f, 0.001f, 1.0f),
			ProjectileScaleCurve.Shrink => Mathf.Clamp(1.0f - t, 0.001f, 1.0f),
			ProjectileScaleCurve.GrowShrink => Mathf.Max(0.001f, Mathf.Sin(Mathf.Clamp(t, 0.0f, 1.0f) * Mathf.Pi)),
			ProjectileScaleCurve.SquashStretch => CalculateSquashStretchScale(t),
			ProjectileScaleCurve.ImpactShrink => t > 0.8f ? Mathf.Clamp((1.0f - t) / 0.2f, 0.001f, 1.0f) : 1.0f,
			_ => 1.0f,
		};
	}

	private static float ApplyEaseCurve(float t, ProjectileEaseCurve easeCurve)
	{
		return easeCurve switch
		{
			ProjectileEaseCurve.EaseIn => t * t,
			ProjectileEaseCurve.EaseOut => 1.0f - (1.0f - t) * (1.0f - t),
			ProjectileEaseCurve.EaseInOut => t < 0.5f ? 2.0f * t * t : 1.0f - Mathf.Pow(-2.0f * t + 2.0f, 2.0f) / 2.0f,
			_ => t,
		};
	}

	private static Color ParseColor(string? hex, Color fallback)
	{
		if (string.IsNullOrEmpty(hex)) return fallback;
		try
		{
			return Color.FromHtml(hex);
		}
		catch
		{
			return fallback;
		}
	}

	private static Texture2D? LoadTextureSafe(string path)
	{
		if (string.IsNullOrEmpty(path)) return null;

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string cacheKey = $"{wsPath}|{path}";
		if (_textureCache.TryGetValue(cacheKey, out var cached) && (cached == null || GodotObject.IsInstanceValid(cached)))
		{
			return cached;
		}

		Texture2D? result = null;
		if (path.StartsWith("res://") || path.StartsWith("user://"))
		{
			if (ResourceLoader.Exists(path))
			{
				result = GD.Load<Texture2D>(path);
			}
		}
		else
		{
			string? resolvedPath = ModelCache.ResolveModelPath(path);
			if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath))
			{
				if (resolvedPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
				{
					var img = LoadImageFromRtex(resolvedPath);
					if (img != null)
					{
						img.GenerateMipmaps();
						result = ImageTexture.CreateFromImage(img);
					}
				}
				else
				{
					var img = Image.LoadFromFile(resolvedPath);
					if (img != null)
					{
						img.GenerateMipmaps();
						result = ImageTexture.CreateFromImage(img);
					}
				}
			}
		}

		_textureCache[cacheKey] = result;
		return result;
	}

	private static Image? LoadImageFromRtex(string rtexPath, int layer = 0)
	{
		string globalPath = ProjectSettings.GlobalizePath(rtexPath);
		if (!File.Exists(globalPath)) return null;

		try
		{
			byte[] bytes = File.ReadAllBytes(globalPath);
			byte[]? layerData = RtexFile.GetLayer(bytes, layer);
			if (layerData == null || layerData.Length == 0) return null;

			var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
			if (img.LoadWebpFromBuffer(layerData) != Error.Ok)
			{
				img.LoadPngFromBuffer(layerData);
			}

			return img;
		}
		catch
		{
			return null;
		}
	}
}
