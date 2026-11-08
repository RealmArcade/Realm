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
	public TerrainCell[,] Cells = null!;
	public TerrainSplatWeights[,]? SplatMap;
	public TerrainSplatWeights[,]? CliffSplatMap;
	public int[,] Pathing = null!;
	public List<CopiedEntityInfo> Entities = null!;
	public bool[,]? Mask;
}