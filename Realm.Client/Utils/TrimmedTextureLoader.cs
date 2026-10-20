using System;
using System.Collections.Generic;
using Godot;

namespace Realm.Client.Utils;

public struct TrimmedTextureLoadRecord
{
	public string ResourcePath;
	public int SourceWidth;
	public int SourceHeight;
	public int ScaledWidth;
	public int ScaledHeight;
	public int UsedRectX;
	public int UsedRectY;
	public int UsedWidth;
	public int UsedHeight;
	public int ResultWidth;
	public int ResultHeight;
}

/// <summary>
/// Loads UX image files that ship in their original compressed format (importer="keep"), where
/// Godot stores the raw webp/jpg/svg bytes instead of an imported texture resource.
/// </summary>
public static class TrimmedTextureLoader
{
	private static readonly Dictionary<string, Texture2D> _cache = new(StringComparer.OrdinalIgnoreCase);
	private static readonly List<TrimmedTextureLoadRecord> _loadRecords = new();
	private static readonly Dictionary<ulong, string> _textureSourcePaths = new();

	public static bool LogUsedRect { get; set; } = System.Environment.GetEnvironmentVariable("REALM_LOG_UX_USED_RECT") == "1";

	public static IReadOnlyList<TrimmedTextureLoadRecord> LoadRecords => _loadRecords;

	public static bool TryGetTextureSourcePath(Texture2D texture, out string resourcePath)
	{
		resourcePath = string.Empty;
		if (texture == null)
		{
			return false;
		}

		if (_textureSourcePaths.TryGetValue(texture.GetInstanceId(), out string recordedPath))
		{
			resourcePath = recordedPath;
			return true;
		}

		return false;
	}

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
		_textureSourcePaths[loadedTexture.GetInstanceId()] = resourcePath;
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
				Image image = DecodeImageFile(resourcePath);
				if (image != null && !image.IsEmpty())
				{
					int sourceWidth = image.GetWidth();
					int sourceHeight = image.GetHeight();

					if (maxDimension > 0)
					{
						image = DownscaleImage(image, maxDimension);
					}

					int downscaledWidth = image.GetWidth();
					int downscaledHeight = image.GetHeight();

					image = TrimTransparentBorders(image, out Rect2I usedRect);

					RecordLoad(resourcePath, sourceWidth, sourceHeight, usedRect, downscaledWidth, downscaledHeight, image.GetWidth(), image.GetHeight());

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

	private static Image DecodeImageFile(string resourcePath)
	{
		byte[] fileBytes = FileAccess.GetFileAsBytes(resourcePath);
		if (fileBytes == null || fileBytes.Length == 0)
		{
			return null;
		}

		string extension = System.IO.Path.GetExtension(resourcePath).ToLowerInvariant();
		if (extension != ".webp" && extension != ".png" && extension != ".jpg" && extension != ".jpeg" && extension != ".bmp" && extension != ".tga" && extension != ".svg")
		{
			return Image.LoadFromFile(resourcePath);
		}

		var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
		Error decodeError = extension switch
		{
			".webp" => image.LoadWebpFromBuffer(fileBytes),
			".png" => image.LoadPngFromBuffer(fileBytes),
			".jpg" => image.LoadJpgFromBuffer(fileBytes),
			".jpeg" => image.LoadJpgFromBuffer(fileBytes),
			".bmp" => image.LoadBmpFromBuffer(fileBytes),
			".svg" => image.LoadSvgFromBuffer(fileBytes, 1.0f),
			_ => image.LoadTgaFromBuffer(fileBytes)
		};

		if (decodeError != Error.Ok)
		{
			GD.PrintErr($"[TrimmedTextureLoader] Failed to decode '{resourcePath}': {decodeError}");
			image.Dispose();
			return null;
		}

		return image;
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

	private static Image TrimTransparentBorders(Image image, out Rect2I usedRect)
	{
		usedRect = image.GetUsedRect();
		if (usedRect.Size.X <= 0 || usedRect.Size.Y <= 0)
		{
			usedRect = new Rect2I(0, 0, image.GetWidth(), image.GetHeight());
			return image;
		}

		if (usedRect.Size.X == image.GetWidth() && usedRect.Size.Y == image.GetHeight())
		{
			usedRect = new Rect2I(0, 0, image.GetWidth(), image.GetHeight());
			return image;
		}

		return image.GetRegion(usedRect);
	}

	private static void RecordLoad(string resourcePath, int sourceWidth, int sourceHeight, Rect2I usedRect, int downscaledWidth, int downscaledHeight, int resultWidth, int resultHeight)
	{
		var record = new TrimmedTextureLoadRecord
		{
			ResourcePath = resourcePath,
			SourceWidth = sourceWidth,
			SourceHeight = sourceHeight,
			ScaledWidth = downscaledWidth,
			ScaledHeight = downscaledHeight,
			UsedRectX = usedRect.Position.X,
			UsedRectY = usedRect.Position.Y,
			UsedWidth = usedRect.Size.X,
			UsedHeight = usedRect.Size.Y,
			ResultWidth = resultWidth,
			ResultHeight = resultHeight
		};

		_loadRecords.Add(record);

		if (LogUsedRect)
		{
			GD.Print($"[TrimmedTextureLoader] {resourcePath} source={sourceWidth}x{sourceHeight} scaled={downscaledWidth}x{downscaledHeight} usedRect=({usedRect.Position.X}, {usedRect.Position.Y}, {usedRect.Size.X}, {usedRect.Size.Y}) result={resultWidth}x{resultHeight}");
		}
	}
}
