using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Realm.Shared.Maps;
using Realm.Shared.Terrain;
using Realm.Shared.Textures;

namespace Realm.MapGeneration;

public static class MapFolderTerrainWriter
{
	private static readonly string[] SkippedDirectories = { "bin", "obj", ".git", ".godot", ".sidecarcache" };

	private static readonly JsonSerializerOptions SerializerOptions = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	public static MapSaveData Write(string templateFolder, string outputFolder, ReconstructedTerrain terrain, string mapName)
	{
		PrepareOutputFolder(templateFolder, outputFolder);
		MapSaveData saveData = CreateSaveData(terrain, mapName);
		WriteSaveData(outputFolder, saveData);
		WriteTerrainImages(outputFolder, terrain);
		return saveData;
	}

	public static MapSaveData CreateSaveData(ReconstructedTerrain terrain, string mapName)
	{
		var props = new List<PropSaveData>();
		for (int index = 0; index < terrain.Props.Count; index++)
		{
			GeneratedProp prop = terrain.Props[index];
			props.Add(new PropSaveData
			{
				TemplateId = prop.TemplateId,
				PosX = prop.X,
				PosY = prop.Y,
				PosZ = prop.Z,
				RotationY = prop.RotationY,
				Scale = prop.Scale
			});
		}

		float halfWidth = terrain.Width * terrain.QuadSize * 0.5f;
		float halfDepth = terrain.Depth * terrain.QuadSize * 0.5f;
		return new MapSaveData
		{
			Width = terrain.Width,
			Depth = terrain.Depth,
			Units = new List<UnitSaveData>(),
			Props = props,
			Decals = new List<DecalSaveData>(),
			Items = new List<ItemSaveData>(),
			Vfx = new List<VfxSaveData>(),
			CameraBoundsLeft = -halfWidth,
			CameraBoundsRight = halfWidth,
			CameraBoundsTop = -halfDepth,
			CameraBoundsBottom = halfDepth,
			Coordinates = new List<CoordinateSaveData>(),
			SpawnAnchors = new List<SpawnAnchorSaveData>()
		};
	}

	public static void WriteSaveData(string outputFolder, MapSaveData saveData)
	{
		Directory.CreateDirectory(outputFolder);
		string path = Path.Combine(outputFolder, "terrain.json");
		File.WriteAllText(path, JsonSerializer.Serialize(saveData, SerializerOptions));
	}

	public static void WriteTerrainImages(string outputFolder, ReconstructedTerrain terrain)
	{
		Directory.CreateDirectory(outputFolder);
		WriteHeights(outputFolder, terrain);
		WriteWater(outputFolder, terrain);
		WriteSplat(outputFolder, terrain);
		WritePathing(outputFolder, terrain);
	}

	private static void PrepareOutputFolder(string templateFolder, string outputFolder)
	{
		if (Directory.Exists(outputFolder) && Directory.EnumerateFileSystemEntries(outputFolder).GetEnumerator().MoveNext())
		{
			return;
		}

		if (!Directory.Exists(templateFolder))
		{
			Directory.CreateDirectory(outputFolder);
			return;
		}

		CopyDirectory(templateFolder, outputFolder);
	}

	private static void CopyDirectory(string sourceFolder, string destinationFolder)
	{
		Directory.CreateDirectory(destinationFolder);
		foreach (string file in Directory.EnumerateFiles(sourceFolder))
		{
			string fileName = Path.GetFileName(file);
			File.Copy(file, Path.Combine(destinationFolder, fileName), true);
		}

		foreach (string directory in Directory.EnumerateDirectories(sourceFolder))
		{
			string name = Path.GetFileName(directory);
			if (Array.Exists(SkippedDirectories, skipped => string.Equals(skipped, name, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}

			CopyDirectory(directory, Path.Combine(destinationFolder, name));
		}
	}

	private static void WriteHeights(string outputFolder, ReconstructedTerrain terrain)
	{
		int width = terrain.Width;
		int depth = terrain.Depth;
		var pixels = new float[width * depth * 4];
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				int target = (z * width + x) * 4;
				pixels[target] = terrain.HeightAtVertex(x, z);
				pixels[target + 1] = terrain.HeightAtVertex(x + 1, z);
				pixels[target + 2] = terrain.HeightAtVertex(x + 1, z + 1);
				pixels[target + 3] = terrain.HeightAtVertex(x, z + 1);
			}
		}

		ExrImageCodec.EncodeRgbaFloat(MapTerrainFiles.HeightsPath(outputFolder), pixels, width, depth);
	}

	private static void WriteWater(string outputFolder, ReconstructedTerrain terrain)
	{
		int width = terrain.Width;
		int depth = terrain.Depth;
		var pixels = new float[width * depth * 4];
		for (int index = 0; index < width * depth; index++)
		{
			int target = index * 4;
			pixels[target] = terrain.WaterModes[index];
			pixels[target + 1] = terrain.WaterProfileIndex;
			pixels[target + 2] = terrain.WaterSurfaceHeights[index];
			pixels[target + 3] = 1f;
		}

		ExrImageCodec.EncodeRgbaFloat(MapTerrainFiles.WaterPath(outputFolder), pixels, width, depth);
	}

	private static void WriteSplat(string outputFolder, ReconstructedTerrain terrain)
	{
		int width = terrain.Width;
		int depth = terrain.Depth;
		int splatWidth = width + 1;
		int splatDepth = depth + 1;
		var indices = new float[splatWidth * splatDepth * 4];
		var weights = new float[splatWidth * splatDepth * 4];
		var cliffIndices = new float[splatWidth * splatDepth * 4];
		var cliffWeights = new float[splatWidth * splatDepth * 4];

		for (int index = 0; index < splatWidth * splatDepth; index++)
		{
			int target = index * 4;
			int ground = terrain.SplatIndices[index];
			int cliff = terrain.CliffSplatIndices[index];
			indices[target] = ground;
			indices[target + 1] = ground;
			indices[target + 2] = ground;
			indices[target + 3] = ground;
			weights[target] = 1f;
			cliffIndices[target] = cliff;
			cliffIndices[target + 1] = cliff;
			cliffIndices[target + 2] = cliff;
			cliffIndices[target + 3] = cliff;
			cliffWeights[target] = 1f;
		}

		ExrImageCodec.EncodeRgbaFloat(MapTerrainFiles.SplatIndicesPath(outputFolder), indices, splatWidth, splatDepth);
		ExrImageCodec.EncodeRgbaFloat(MapTerrainFiles.SplatWeightsPath(outputFolder), weights, splatWidth, splatDepth);
		ExrImageCodec.EncodeRgbaFloat(MapTerrainFiles.SplatIndicesPath(outputFolder, MapTerrainFiles.CliffSplatPrefix), cliffIndices, splatWidth, splatDepth);
		ExrImageCodec.EncodeRgbaFloat(MapTerrainFiles.SplatWeightsPath(outputFolder, MapTerrainFiles.CliffSplatPrefix), cliffWeights, splatWidth, splatDepth);
	}

	private static void WritePathing(string outputFolder, ReconstructedTerrain terrain)
	{
		int width = terrain.Width;
		int depth = terrain.Depth;
		var pixels = new byte[width * depth * 4];
		for (int index = 0; index < width * depth; index++)
		{
			int target = index * 4;
			pixels[target] = terrain.PathingCodes[index];
			pixels[target + 1] = 0;
			pixels[target + 2] = 0;
			pixels[target + 3] = 255;
		}

		PngImageCodec.EncodeRgba8(MapTerrainFiles.PathingPath(outputFolder), pixels, width, depth);
	}
}
