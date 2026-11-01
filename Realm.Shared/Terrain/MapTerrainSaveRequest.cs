using System;
using System.IO;
using Realm.Ecs.Components.Terrain;
using Realm.Shared.Maps;
using Realm.Shared.Services;

namespace Realm.Shared.Terrain;

public class MapTerrainImageData
{
	public MapTerrainImageData(
		string directory,
		int width,
		int depth,
		TerrainCell[,]? cells,
		int[,]? pathingCodes,
		string[]? splatColors,
		string[]? cliffSplatColors)
	{
		Directory = directory;
		Width = width;
		Depth = depth;
		Cells = cells;
		PathingCodes = pathingCodes;
		SplatColors = CloneColors(splatColors);
		CliffSplatColors = CloneColors(cliffSplatColors);
	}

	public string Directory { get; }
	public int Width { get; }
	public int Depth { get; }
	public TerrainCell[,]? Cells { get; }
	public int[,]? PathingCodes { get; }
	public string[]? SplatColors { get; }
	public string[]? CliffSplatColors { get; }

	private static string[]? CloneColors(string[]? colors)
	{
		return colors == null ? null : (string[])colors.Clone();
	}

	public void WriteToDisk()
	{
		MapTerrainFiles.WriteHeightsAndWater(Directory, Width, Depth, Cells);
		MapTerrainFiles.WritePathing(Directory, Width, Depth, PathingCodes, WaterType.None);
		WriteSplat(SplatColors, MapTerrainFiles.SplatPrefix);
		WriteSplat(CliffSplatColors, MapTerrainFiles.CliffSplatPrefix);
	}

	private void WriteSplat(string[]? colors, string prefix)
	{
		if (colors == null) return;
		if (!MapTerrainFiles.TryResolveSplatDimensions(Width, Depth, colors.Length, out int splatWidth, out int splatDepth)) return;

		MapTerrainFiles.WriteSplat(Directory, prefix, splatWidth, splatDepth, colors);
	}
}

public class MapTerrainSaveRequest
{
	public string AbsolutePath { get; init; } = string.Empty;
	public MapSaveData SaveData { get; init; } = new();
	public MapTerrainImageData? Images { get; init; }

	public void WriteToDisk()
	{
		string? directory = Path.GetDirectoryName(AbsolutePath);
		if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}

		Images?.WriteToDisk();
		MapFileService.SaveTerrain(AbsolutePath, SaveData);
	}
}
