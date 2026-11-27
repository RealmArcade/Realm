using System;

namespace Realm.MapGeneration;

public static class MapFolderTerrainConverter
{
	public static MapFolderTerrain CreateSnapshot(ReconstructedTerrain terrain, MapFolderTerrain template)
	{
		int width = terrain.Width;
		int depth = terrain.Depth;
		var snapshot = new MapFolderTerrain
		{
			Width = width,
			Depth = depth,
			FolderPath = template.FolderPath,
			VertexHeights = (float[])terrain.VertexHeights.Clone(),
			CellHeights = (float[])terrain.CellHeights.Clone(),
			WaterSurfaceHeights = (float[])terrain.WaterSurfaceHeights.Clone(),
			WaterModes = (byte[])terrain.WaterModes.Clone(),
			SplatIndices = (int[])terrain.SplatIndices.Clone(),
			PathingCodes = (byte[])terrain.PathingCodes.Clone()
		};

		for (int index = 0; index < template.Textures.Count; index++)
		{
			snapshot.Textures.Add(template.Textures[index]);
		}

		for (int index = 0; index < terrain.Props.Count; index++)
		{
			GeneratedProp prop = terrain.Props[index];
			snapshot.Props.Add(new MapPlacedProp(prop.TemplateId, prop.X, prop.Y, prop.Z, prop.RotationY, prop.Scale));
		}

		return snapshot;
	}

	public static float HeightAtCell(ReconstructedTerrain terrain, int x, int z)
	{
		if (x < 0 || x >= terrain.Width || z < 0 || z >= terrain.Depth)
		{
			return 0f;
		}

		return terrain.CellHeights[z * terrain.Width + x];
	}
}
