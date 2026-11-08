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
	private static System.Numerics.Vector3 ToEcsVector(Vector3 value) => new(value.X, value.Y, value.Z);

	private static Vector3 ToGodotVector(System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);

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
