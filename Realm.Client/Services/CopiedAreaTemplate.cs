using System.Collections.Generic;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client.Services;

public class CopiedAreaTemplate
{
	public int Width;
	public int Depth;
	public int AnchorTileX;
	public int AnchorTileZ;
	public int SourceMinX;
	public int SourceMinZ;
	public int SourceMaxX;
	public int SourceMaxZ;
	public TerrainCell[,] Cells;
	public TerrainSplatWeights[,] SplatMap;
	public TerrainSplatWeights[,] CliffSplatMap;
	public int[,] Pathing;
	public List<CopiedEntityInfo> Entities;
	public bool[,] Mask;
}