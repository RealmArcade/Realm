using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Realm.Shared.Maps;
using Realm.Shared.Textures;

namespace Realm.MapGeneration;

public static class MapFolderTerrainReader
{
	public static MapFolderTerrain Read(string folderPath)
	{
		string terrainPath = Path.Combine(folderPath, "terrain.json");
		if (!File.Exists(terrainPath))
		{
			throw new FileNotFoundException($"The map folder '{folderPath}' has no terrain.json.", terrainPath);
		}

		using JsonDocument terrainDocument = JsonDocument.Parse(File.ReadAllText(terrainPath));
		JsonElement terrainRoot = terrainDocument.RootElement;

		int width = terrainRoot.GetProperty("Width").GetInt32();
		int depth = terrainRoot.GetProperty("Depth").GetInt32();
		var terrain = new MapFolderTerrain { Width = width, Depth = depth, FolderPath = folderPath };

		ReadHeights(folderPath, width, depth, terrain);
		ReadWater(folderPath, width, depth, terrain);
		ReadSplat(folderPath, width, depth, terrain);
		ReadPathing(folderPath, width, depth, terrain);
		ReadProps(terrainRoot, terrain);
		ReadTextures(folderPath, terrain);

		string thumbnailPath = Path.Combine(folderPath, "thumbnail.webp");
		if (File.Exists(thumbnailPath))
		{
			terrain.ThumbnailPath = thumbnailPath;
		}

		return terrain;
	}

	public static PropCatalog ReadPropCatalog(string folderPath)
	{
		var catalog = new PropCatalog();
		string metadataPath = Path.Combine(folderPath, "metadata.json");
		if (!File.Exists(metadataPath))
		{
			return catalog;
		}

		using JsonDocument metadataDocument = JsonDocument.Parse(File.ReadAllText(metadataPath));
		if (!metadataDocument.RootElement.TryGetProperty("Templates", out JsonElement templates))
		{
			return catalog;
		}

		AddTemplateEntries(templates, "Props", catalog);
		AddTemplateEntries(templates, "Resources", catalog);
		return catalog;
	}

	public static IReadOnlyList<string> ReadTemplateNames(string folderPath, string category)
	{
		var names = new List<string>();
		string metadataPath = Path.Combine(folderPath, "metadata.json");
		if (!File.Exists(metadataPath))
		{
			return names;
		}

		using JsonDocument metadataDocument = JsonDocument.Parse(File.ReadAllText(metadataPath));
		if (!metadataDocument.RootElement.TryGetProperty("Templates", out JsonElement templates) ||
		    !templates.TryGetProperty(category, out JsonElement entries) ||
		    entries.ValueKind != JsonValueKind.Array)
		{
			return names;
		}

		foreach (JsonElement entry in entries.EnumerateArray())
		{
			if (entry.ValueKind == JsonValueKind.Object &&
			    entry.TryGetProperty("TemplateID", out JsonElement templateId) &&
			    templateId.ValueKind == JsonValueKind.String)
			{
				names.Add(templateId.GetString() ?? string.Empty);
			}
		}

		return names;
	}

	private static void AddTemplateEntries(JsonElement templates, string category, PropCatalog catalog)
	{
		if (!templates.TryGetProperty(category, out JsonElement entries) || entries.ValueKind != JsonValueKind.Array)
		{
			return;
		}

		foreach (JsonElement entry in entries.EnumerateArray())
		{
			if (entry.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			string templateId = GetString(entry, "TemplateID");
			string name = GetString(entry, "Name");
			float scale = GetFloat(entry, "Scale", 1f);
			if (!string.IsNullOrWhiteSpace(templateId))
			{
				catalog.Add(templateId, name, scale);
			}
		}
	}

	private static void ReadHeights(string folderPath, int width, int depth, MapFolderTerrain terrain)
	{
		terrain.VertexHeights = new float[(width + 1) * (depth + 1)];
		terrain.CellHeights = new float[width * depth];

		string path = MapTerrainFiles.HeightsPath(folderPath);
		if (!File.Exists(path))
		{
			return;
		}

		float[] pixels = ExrImageCodec.DecodeRgbaFloat(path, out int imageWidth, out int imageHeight);
		if (imageWidth == width + 1 && imageHeight == depth + 1)
		{
			for (int index = 0; index < terrain.VertexHeights.Length; index++)
			{
				terrain.VertexHeights[index] = pixels[index * 4];
			}

			BuildCellHeightsFromVertices(width, depth, terrain);
			return;
		}

		if (imageWidth != width || imageHeight != depth)
		{
			throw new InvalidDataException($"The height map of '{folderPath}' is {imageWidth}x{imageHeight} but the terrain is {width}x{depth}.");
		}

		var sums = new float[(width + 1) * (depth + 1)];
		var counts = new int[(width + 1) * (depth + 1)];
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				int source = (z * width + x) * 4;
				float northWest = pixels[source];
				float northEast = pixels[source + 1];
				float southEast = pixels[source + 2];
				float southWest = pixels[source + 3];

				AddVertexSample(sums, counts, width, x, z, northWest);
				AddVertexSample(sums, counts, width, x + 1, z, northEast);
				AddVertexSample(sums, counts, width, x + 1, z + 1, southEast);
				AddVertexSample(sums, counts, width, x, z + 1, southWest);

				terrain.CellHeights[z * width + x] = (northWest + northEast + southEast + southWest) * 0.25f;
			}
		}

		for (int index = 0; index < terrain.VertexHeights.Length; index++)
		{
			terrain.VertexHeights[index] = counts[index] > 0 ? sums[index] / counts[index] : 0f;
		}
	}

	private static void BuildCellHeightsFromVertices(int width, int depth, MapFolderTerrain terrain)
	{
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				float sum = terrain.HeightAtVertex(x, z) + terrain.HeightAtVertex(x + 1, z) +
				            terrain.HeightAtVertex(x + 1, z + 1) + terrain.HeightAtVertex(x, z + 1);
				terrain.CellHeights[z * width + x] = sum * 0.25f;
			}
		}
	}

	private static void AddVertexSample(float[] sums, int[] counts, int width, int x, int z, float height)
	{
		int vertexIndex = z * (width + 1) + x;
		sums[vertexIndex] += height;
		counts[vertexIndex]++;
	}

	private static void ReadWater(string folderPath, int width, int depth, MapFolderTerrain terrain)
	{
		terrain.WaterModes = new byte[width * depth];
		terrain.WaterSurfaceHeights = new float[width * depth];

		string path = MapTerrainFiles.WaterPath(folderPath);
		if (!File.Exists(path))
		{
			return;
		}

		float[] pixels = ExrImageCodec.DecodeRgbaFloat(path, out int imageWidth, out int imageHeight);
		if (imageWidth != width || imageHeight != depth)
		{
			return;
		}

		for (int index = 0; index < width * depth; index++)
		{
			int source = index * 4;
			int mode = (int)MathF.Round(pixels[source]);
			terrain.WaterModes[index] = (byte)Math.Clamp(mode, 0, 2);
			terrain.WaterSurfaceHeights[index] = mode > 0 ? pixels[source + 2] : 0f;
		}
	}

	private static void ReadSplat(string folderPath, int width, int depth, MapFolderTerrain terrain)
	{
		terrain.SplatIndices = new int[(width + 1) * (depth + 1)];

		string indicesPath = MapTerrainFiles.SplatIndicesPath(folderPath);
		string weightsPath = MapTerrainFiles.SplatWeightsPath(folderPath);
		if (!File.Exists(indicesPath) || !File.Exists(weightsPath))
		{
			return;
		}

		float[] indices = ExrImageCodec.DecodeRgbaFloat(indicesPath, out int indicesWidth, out int indicesHeight);
		float[] weights = ExrImageCodec.DecodeRgbaFloat(weightsPath, out int weightsWidth, out int weightsHeight);
		if (indicesWidth != weightsWidth || indicesHeight != weightsHeight)
		{
			return;
		}

		for (int z = 0; z <= depth; z++)
		{
			int sourceZ = Math.Min(z, indicesHeight - 1);
			for (int x = 0; x <= width; x++)
			{
				int sourceX = Math.Min(x, indicesWidth - 1);
				int source = (sourceZ * indicesWidth + sourceX) * 4;
				int bestChannel = 0;
				float bestWeight = weights[source];
				for (int channel = 1; channel < 4; channel++)
				{
					if (weights[source + channel] > bestWeight)
					{
						bestWeight = weights[source + channel];
						bestChannel = channel;
					}
				}

				terrain.SplatIndices[z * (width + 1) + x] = (int)MathF.Round(indices[source + bestChannel]);
			}
		}
	}

	private static void ReadPathing(string folderPath, int width, int depth, MapFolderTerrain terrain)
	{
		terrain.PathingCodes = new byte[width * depth];
		byte[]? pixels = MapTerrainFiles.TryReadPathing(folderPath, out int imageWidth, out int imageHeight);
		if (pixels == null || imageWidth != width || imageHeight != depth)
		{
			return;
		}

		for (int index = 0; index < width * depth; index++)
		{
			terrain.PathingCodes[index] = pixels[index * 4];
		}
	}

	private static void ReadProps(JsonElement terrainRoot, MapFolderTerrain terrain)
	{
		if (!terrainRoot.TryGetProperty("Props", out JsonElement props) || props.ValueKind != JsonValueKind.Array)
		{
			return;
		}

		foreach (JsonElement prop in props.EnumerateArray())
		{
			if (prop.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			string templateId = GetString(prop, "TemplateId");
			if (string.IsNullOrWhiteSpace(templateId))
			{
				continue;
			}

			terrain.Props.Add(new MapPlacedProp(
				templateId,
				GetFloat(prop, "PosX", 0f),
				GetFloat(prop, "PosY", 0f),
				GetFloat(prop, "PosZ", 0f),
				GetFloat(prop, "RotationY", 0f),
				GetFloat(prop, "Scale", 1f)));
		}
	}

	private static void ReadTextures(string folderPath, MapFolderTerrain terrain)
	{
		string metadataPath = Path.Combine(folderPath, "metadata.json");
		if (!File.Exists(metadataPath))
		{
			return;
		}

		using JsonDocument metadataDocument = JsonDocument.Parse(File.ReadAllText(metadataPath));
		if (!metadataDocument.RootElement.TryGetProperty("textures", out JsonElement textures) ||
		    textures.ValueKind != JsonValueKind.Object)
		{
			return;
		}

		foreach (JsonProperty texture in textures.EnumerateObject())
		{
			JsonElement value = texture.Value;
			if (value.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			int swatchIndex = value.TryGetProperty("SwatchIndex", out JsonElement swatchElement) ? swatchElement.GetInt32() : -1;
			string texturePath = GetString(value, "TexturePath");
			if (swatchIndex < 0 || string.IsNullOrWhiteSpace(texturePath))
			{
				continue;
			}

			(float tintRedValue, float tintGreenValue, float tintBlueValue) = MapTextureSource.ParseTint(GetString(value, "Tint"));
			string fullPath = ResolveTexturePath(folderPath, texturePath);
			terrain.Textures.Add(new MapTextureSource(
				swatchIndex,
				texture.Name,
				fullPath,
				GetFloat(value, "ScaleFactor", 4f),
				GetFloat(value, "Brightness", 1f),
				tintRedValue,
				tintGreenValue,
				tintBlueValue,
				value.TryGetProperty("DefaultPathingCode", out JsonElement pathingElement) ? pathingElement.GetInt32() : 44));
		}

		terrain.Textures.Sort((left, right) => left.SwatchIndex.CompareTo(right.SwatchIndex));
	}

	private static string ResolveTexturePath(string folderPath, string texturePath)
	{
		if (Path.IsPathRooted(texturePath))
		{
			return texturePath;
		}

		string candidate = Path.Combine(folderPath, texturePath);
		if (File.Exists(candidate))
		{
			return candidate;
		}

		string assetsCandidate = Path.Combine(folderPath, "Assets", "textures", Path.GetFileName(texturePath));
		if (File.Exists(assetsCandidate))
		{
			return assetsCandidate;
		}

		return candidate;
	}

	private static string GetString(JsonElement element, string propertyName)
	{
		return element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString() ?? string.Empty
			: string.Empty;
	}

	private static float GetFloat(JsonElement element, string propertyName, float fallback)
	{
		return element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.Number
			? (float)value.GetDouble()
			: fallback;
	}
}
