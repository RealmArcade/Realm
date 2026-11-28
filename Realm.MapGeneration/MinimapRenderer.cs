using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;

namespace Realm.MapGeneration;

public static class MinimapRenderer
{
	private const float PropRadiusWorldUnits = 1.6f;

	public static MinimapImage Render(MapFolderTerrain terrain, MinimapRenderOptions? options = null)
	{
		options ??= new MinimapRenderOptions();
		int resolution = Math.Max(16, options.Resolution);
		var image = new MinimapImage(resolution, resolution);
		var textureLibrary = new MinimapTextureLibrary();
		var texturesBySwatch = BuildSwatchTextures(terrain, textureLibrary);

		float quadSize = options.QuadSize;
		float worldWidth = Math.Max(1f, terrain.Width * quadSize);
		float worldDepth = Math.Max(1f, terrain.Depth * quadSize);

		for (int pixelY = 0; pixelY < resolution; pixelY++)
		{
			float worldZ = ((pixelY + 0.5f) / resolution - 0.5f) * worldDepth;
			float cellZ = worldZ / quadSize + terrain.Depth * 0.5f;
			int clampedCellZ = Math.Clamp((int)cellZ, 0, Math.Max(0, terrain.Depth - 1));
			float nodeZ = Math.Clamp(worldZ / quadSize + terrain.Depth * 0.5f, 0f, terrain.Depth);
			int nodeZIndex = Math.Clamp((int)MathF.Round(nodeZ), 0, terrain.Depth);

			for (int pixelX = 0; pixelX < resolution; pixelX++)
			{
				float worldX = ((pixelX + 0.5f) / resolution - 0.5f) * worldWidth;
				float cellX = worldX / quadSize + terrain.Width * 0.5f;
				int clampedCellX = Math.Clamp((int)cellX, 0, Math.Max(0, terrain.Width - 1));
				float nodeX = Math.Clamp(worldX / quadSize + terrain.Width * 0.5f, 0f, terrain.Width);
				int nodeXIndex = Math.Clamp((int)MathF.Round(nodeX), 0, terrain.Width);

				int swatchIndex = terrain.SplatIndices.Length > 0
					? terrain.SplatIndices[nodeZIndex * (terrain.Width + 1) + nodeXIndex]
					: 0;

				MapTextureSource source = texturesBySwatch.TryGetValue(swatchIndex, out MapTextureSource found)
					? found
					: MapTextureSource.Empty(swatchIndex);
				MinimapTexture texture = textureLibrary.Get(source);
				float scaleFactor = MathF.Max(0.05f, source.ScaleFactor);
				texture.SampleTiled(worldX / scaleFactor, worldZ / scaleFactor, out float red, out float green, out float blue);

				red = Math.Clamp(red * source.Brightness * source.TintRed, 0f, 1f);
				green = Math.Clamp(green * source.Brightness * source.TintGreen, 0f, 1f);
				blue = Math.Clamp(blue * source.Brightness * source.TintBlue, 0f, 1f);

				if (options.DrawWater && terrain.WaterModes.Length == terrain.Width * terrain.Depth)
				{
					int cellIndex = clampedCellZ * terrain.Width + clampedCellX;
					if (terrain.WaterModes[cellIndex] > 0)
					{
						float depth = MathF.Max(0f, terrain.WaterSurfaceHeights[cellIndex] - terrain.CellHeights[cellIndex]);
						float shallow = Math.Clamp(1f - depth / 4f, 0f, 1f);
						float waterRed = 0.10f + shallow * 0.18f;
						float waterGreen = 0.38f + shallow * 0.16f;
						float waterBlue = 0.50f + shallow * 0.12f;
						float opacity = Math.Clamp(options.WaterOpacity + shallow * 0.08f, 0f, 1f);
						red = red * (1f - opacity) + waterRed * opacity;
						green = green * (1f - opacity) + waterGreen * opacity;
						blue = blue * (1f - opacity) + waterBlue * opacity;
					}
				}

				int index = pixelY * resolution + pixelX;
				image.Red[index] = red;
				image.Green[index] = green;
				image.Blue[index] = blue;
			}
		}

		if (options.DrawProps)
		{
			DrawProps(image, terrain, quadSize, worldWidth, worldDepth);
		}

		return image;
	}

	public static MinimapImage RenderFolder(string folderPath, MinimapRenderOptions? options = null)
	{
		return Render(MapFolderTerrainReader.Read(folderPath), options);
	}

	public static int SaveFolder(string folderPath, string outputPath, MinimapRenderOptions? options = null)
	{
		MinimapImage image = RenderFolder(folderPath, options);
		string? directory = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		image.SavePng(outputPath);
		return image.Width;
	}

	private static Dictionary<int, MapTextureSource> BuildSwatchTextures(MapFolderTerrain terrain, MinimapTextureLibrary _)
	{
		var lookup = new Dictionary<int, MapTextureSource>();
		for (int index = 0; index < terrain.Textures.Count; index++)
		{
			MapTextureSource texture = terrain.Textures[index];
			lookup[texture.SwatchIndex] = texture;
		}

		return lookup;
	}

	private static void DrawProps(MinimapImage image, MapFolderTerrain terrain, float quadSize, float worldWidth, float worldDepth)
	{
		if (terrain.Props.Count == 0)
		{
			return;
		}

		int resolution = image.Width;
		float pixelsPerWorldX = resolution / worldWidth;
		float pixelsPerWorldY = resolution / worldDepth;
		var propLists = new Dictionary<PropCategory, List<MapPlacedProp>>();
		for (int index = 0; index < terrain.Props.Count; index++)
		{
			MapPlacedProp prop = terrain.Props[index];
			PropCategory category = PropCatalogEntry.Classify(prop.TemplateId, prop.TemplateId);
			if (!propLists.TryGetValue(category, out List<MapPlacedProp>? list))
			{
				list = new List<MapPlacedProp>();
				propLists[category] = list;
			}

			list.Add(prop);
		}

		foreach (KeyValuePair<PropCategory, List<MapPlacedProp>> pair in propLists)
		{
			(float red, float green, float blue) = CategoryColor(pair.Key);
			foreach (MapPlacedProp prop in pair.Value)
			{
				float scale = prop.Scale <= 0f ? 1f : prop.Scale;
				float radiusX = PropRadiusWorldUnits * scale * pixelsPerWorldX;
				float radiusY = PropRadiusWorldUnits * scale * pixelsPerWorldY;
				int centreX = (int)((prop.X / worldWidth + 0.5f) * resolution);
				int centreY = (int)((prop.Z / worldDepth + 0.5f) * resolution);
				int radiusPixelsX = Math.Max(1, (int)radiusX);
				int radiusPixelsY = Math.Max(1, (int)radiusY);
				for (int offsetY = -radiusPixelsY; offsetY <= radiusPixelsY; offsetY++)
				{
					int pixelY = centreY + offsetY;
					if (pixelY < 0 || pixelY >= resolution) continue;
					float normalisedY = offsetY / (float)radiusPixelsY;
					for (int offsetX = -radiusPixelsX; offsetX <= radiusPixelsX; offsetX++)
					{
						int pixelX = centreX + offsetX;
						if (pixelX < 0 || pixelX >= resolution) continue;
						float normalisedX = offsetX / (float)radiusPixelsX;
						float distance = normalisedX * normalisedX + normalisedY * normalisedY;
						if (distance > 1f) continue;

						float shade = 0.7f + 0.3f * (1f - distance);
						int index = pixelY * resolution + pixelX;
						image.Red[index] = image.Red[index] * 0.1f + red * shade * 0.9f;
						image.Green[index] = image.Green[index] * 0.1f + green * shade * 0.9f;
						image.Blue[index] = image.Blue[index] * 0.1f + blue * shade * 0.9f;
					}
				}
			}
		}
	}

	private static (float Red, float Green, float Blue) CategoryColor(PropCategory category)
	{
		return category switch
		{
			PropCategory.Tree => (0.16f, 0.33f, 0.13f),
			PropCategory.Rock => (0.45f, 0.44f, 0.41f),
			PropCategory.Structure => (0.55f, 0.45f, 0.30f),
			PropCategory.Nature => (0.30f, 0.48f, 0.20f),
			_ => (0.62f, 0.32f, 0.62f)
		};
	}
}
