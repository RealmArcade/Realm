using System;
using System.Collections.Generic;
using Godot;

namespace Realm.Client.Utils;

/// <summary>
/// Loads UX image files that ship in their original compressed format (importer="keep"), where
/// Godot stores the raw webp/jpg/svg bytes instead of an imported texture resource.
/// </summary>
public static class TrimmedTextureLoader
{
	private static readonly Dictionary<string, Texture2D> _cache = new(StringComparer.OrdinalIgnoreCase);

	public static Texture2D LoadTrimmedTexture(string resourcePath)
	{
		return LoadTrimmedTexture(resourcePath, 0);
	}

	public static Texture2D LoadTrimmedTexture(string resourcePath, int maxDimension)
	{
		if (string.IsNullOrEmpty(resourcePath))
		{
			return null;
		}

		string cacheKey = maxDimension > 0 ? $"{resourcePath}@{maxDimension}" : resourcePath;
		if (_cache.TryGetValue(cacheKey, out Texture2D cachedTexture) && GodotObject.IsInstanceValid(cachedTexture))
		{
			return cachedTexture;
		}

		Texture2D loadedTexture = LoadUncompressedTexture(resourcePath, maxDimension);
		if (loadedTexture == null)
		{
			return null;
		}

		_cache[cacheKey] = loadedTexture;
		return loadedTexture;
	}

	public static void ClearCache()
	{
		_cache.Clear();
	}

	private static Texture2D LoadUncompressedTexture(string resourcePath, int maxDimension)
	{
		try
		{
			if (FileAccess.FileExists(resourcePath) || System.IO.File.Exists(resourcePath))
			{
				Image image = Image.LoadFromFile(resourcePath);
				if (image != null && !image.IsEmpty())
				{
					if (maxDimension > 0)
					{
						image = DownscaleImage(image, maxDimension);
					}

					image = TrimTransparentBorders(image);

					return ImageTexture.CreateFromImage(image);
				}
			}
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[TrimmedTextureLoader] Failed to decode '{resourcePath}': {exception.Message}");
		}

		try
		{
			return GD.Load<Texture2D>(resourcePath);
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[TrimmedTextureLoader] Failed to load '{resourcePath}': {exception.Message}");
			return null;
		}
	}

	private static Image DownscaleImage(Image image, int maxDimension)
	{
		int width = image.GetWidth();
		int height = image.GetHeight();
		if (width <= maxDimension && height <= maxDimension)
		{
			return image;
		}

		if (image.GetFormat() != Image.Format.Rgba8)
		{
			image.Convert(Image.Format.Rgba8);
		}

		float aspect = (float)width / height;
		int targetWidth = aspect >= 1.0f ? maxDimension : Mathf.RoundToInt(maxDimension * aspect);
		int targetHeight = aspect <= 1.0f ? maxDimension : Mathf.RoundToInt(maxDimension / aspect);
		image.Resize(targetWidth, targetHeight, Image.Interpolation.Lanczos);
		return image;
	}

	private static Image TrimTransparentBorders(Image image)
	{
		Rect2I usedRect = image.GetUsedRect();
		if (usedRect.Size.X <= 0 || usedRect.Size.Y <= 0)
		{
			return image;
		}

		if (usedRect.Size.X == image.GetWidth() && usedRect.Size.Y == image.GetHeight())
		{
			return image;
		}

		return image.GetRegion(usedRect);
	}
}
