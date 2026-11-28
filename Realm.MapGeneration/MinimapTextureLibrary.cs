using System;
using System.Collections.Generic;
using System.IO;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.MapGeneration;

public class MinimapTextureLibrary
{
	private readonly Dictionary<string, MinimapTexture> _cache = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<TerrainSurfaceKind, MinimapTexture> _fallbacks = new();

	public MinimapTexture Get(MapTextureSource source)
	{
		if (!string.IsNullOrWhiteSpace(source.FullPath) && File.Exists(source.FullPath))
		{
			if (!_cache.TryGetValue(source.FullPath, out MinimapTexture? texture))
			{
				texture = Load(source.FullPath) ?? CreateFallback(source.Kind);
				_cache[source.FullPath] = texture;
			}

			return texture;
		}

		return CreateFallback(source.Kind);
	}

	public MinimapTexture GetFallback(TerrainSurfaceKind kind)
	{
		return CreateFallback(kind);
	}

	private MinimapTexture CreateFallback(TerrainSurfaceKind kind)
	{
		if (_fallbacks.TryGetValue(kind, out MinimapTexture? fallback))
		{
			return fallback;
		}

		fallback = kind switch
		{
			TerrainSurfaceKind.Water => MinimapTexture.Solid(0.16f, 0.42f, 0.52f),
			TerrainSurfaceKind.Sand => MinimapTexture.Solid(0.66f, 0.58f, 0.38f),
			TerrainSurfaceKind.Soil => MinimapTexture.Solid(0.42f, 0.31f, 0.20f),
			TerrainSurfaceKind.Grass => MinimapTexture.Solid(0.32f, 0.49f, 0.19f),
			TerrainSurfaceKind.Forest => MinimapTexture.Solid(0.20f, 0.34f, 0.15f),
			TerrainSurfaceKind.Rock => MinimapTexture.Solid(0.47f, 0.46f, 0.43f),
			TerrainSurfaceKind.Snow => MinimapTexture.Solid(0.86f, 0.90f, 0.93f),
			_ => MinimapTexture.Solid(0.42f, 0.40f, 0.36f)
		};

		_fallbacks[kind] = fallback;
		return fallback;
	}

	private static MinimapTexture? Load(string path)
	{
		byte[]? layer = RtexFile.GetLayerFromFile(path, 0);
		MinimapTexture? texture = layer != null ? Decode(layer) : null;
		if (texture != null)
		{
			return texture;
		}

		try
		{
			return Decode(File.ReadAllBytes(path));
		}
		catch (IOException)
		{
			return null;
		}
	}

	private static MinimapTexture? Decode(byte[] encoded)
	{
		using SKBitmap? bitmap = SKBitmap.Decode(encoded);
		if (bitmap == null)
		{
			return null;
		}

		using SKBitmap converted = bitmap.ColorType == SKColorType.Rgba8888 && bitmap.AlphaType != SKAlphaType.Premul
			? bitmap.Copy()
			: bitmap.Copy(SKColorType.Rgba8888);

		int width = converted.Width;
		int height = converted.Height;
		var red = new float[width * height];
		var green = new float[width * height];
		var blue = new float[width * height];
		ReadOnlySpan<byte> pixels = converted.GetPixelSpan();
		int stride = converted.RowBytes;
		for (int y = 0; y < height; y++)
		{
			int rowStart = y * stride;
			int target = y * width;
			for (int x = 0; x < width; x++)
			{
				int source = rowStart + x * 4;
				red[target + x] = pixels[source] / 255f;
				green[target + x] = pixels[source + 1] / 255f;
				blue[target + x] = pixels[source + 2] / 255f;
			}
		}

		return new MinimapTexture(width, height, red, green, blue);
	}
}
