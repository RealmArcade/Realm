using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Godot.Services;
using Realm.Shared.Textures;

namespace Realm.Godot.Utils;

public static class RtexIconLoader
{
	private static readonly Dictionary<string, Texture2D?> _cache = new(StringComparer.OrdinalIgnoreCase);
	private static readonly HashSet<string> _negativeCache = new(StringComparer.OrdinalIgnoreCase);

	public static Texture2D? Load(string? iconPath)
	{
		if (string.IsNullOrWhiteSpace(iconPath)) return null;

		if (_negativeCache.Contains(iconPath)) return null;

		if (_cache.TryGetValue(iconPath, out var cachedTexture)) return cachedTexture;

		var loadedTexture = LoadInternal(iconPath);
		if (loadedTexture != null)
		{
			_cache[iconPath] = loadedTexture;
			return loadedTexture;
		}

		_negativeCache.Add(iconPath);
		return null;
	}

	private static Texture2D? LoadInternal(string iconPath)
	{
		string? resolvedPath = ResolvePath(iconPath);
		if (string.IsNullOrEmpty(resolvedPath))
		{
			return LoadGodotResource(iconPath);
		}

		if (resolvedPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			return LoadRtexFile(resolvedPath);
		}

		return LoadStandardImage(resolvedPath);
	}

	private static Texture2D? LoadGodotResource(string iconPath)
	{
		if (!iconPath.StartsWith("res://", StringComparison.OrdinalIgnoreCase) || !ResourceLoader.Exists(iconPath))
			return null;

		try
		{
			return GD.Load<Texture2D>(iconPath);
		}
		catch
		{
			return null;
		}
	}

	private static Texture2D? LoadRtexFile(string resolvedPath)
	{
		try
		{
			byte[] rtexBytes = File.ReadAllBytes(resolvedPath);
			byte[]? layerData = RtexFile.GetLayer(rtexBytes, 0);
			if (layerData == null || layerData.Length == 0)
				return null;

			var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
			if (image.LoadWebpFromBuffer(layerData) != Error.Ok)
			{
				image.LoadPngFromBuffer(layerData);
			}
			
			return CreateTextureFromImage(image);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[RtexIconLoader] Failed to decode .rtex from '{resolvedPath}': {ex.Message}");
			return null;
		}
	}

	private static Texture2D? LoadStandardImage(string resolvedPath)
	{
		try
		{
			var image = Image.LoadFromFile(resolvedPath);
			if (image == null)
				return null;

			return CreateTextureFromImage(image);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[RtexIconLoader] Failed to load image from '{resolvedPath}': {ex.Message}");
			return null;
		}
	}

	private static Texture2D? CreateTextureFromImage(Image image)
	{
		if (!image.HasMipmaps())
		{
			image.GenerateMipmaps();
		}
		return ImageTexture.CreateFromImage(image);
	}

	private static string? ResolvePath(string iconPath)
	{
		if (File.Exists(iconPath)) return iconPath;

		string workspacePath = MapWorkspaceService.GetActiveWorkspacePath();
		string relativePath = iconPath;
		if (relativePath.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
		{
			relativePath = relativePath[6..];
		}

		relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);

		if (!string.IsNullOrEmpty(workspacePath))
		{
			string candidate = Path.Combine(workspacePath, relativePath);
			if (File.Exists(candidate)) return candidate;

			string fileName = Path.GetFileName(iconPath);
			candidate = Path.Combine(workspacePath, "Assets", "icons", "abilities", fileName);
			if (File.Exists(candidate)) return candidate;

			candidate = Path.Combine(workspacePath, "Assets", "icons", fileName);
			if (File.Exists(candidate)) return candidate;
		}

		string globalizedPath = ProjectSettings.GlobalizePath(iconPath);
		if (File.Exists(globalizedPath)) return globalizedPath;

		return null;
	}

	public static void ClearCache()
	{
		_cache.Clear();
		_negativeCache.Clear();
	}
}
