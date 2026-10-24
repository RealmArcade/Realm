using System;
using DotRecast.Detour;
using Realm.Ecs;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client.Services;

public struct NavigationParityDigest
{
	public bool HasTerrainState;
	public bool HasNavMesh;
	public int Width;
	public int Depth;
	public uint PathingHash;
	public int WalkableCellCount;
	public int BlockedCellCount;
	public int PolyCount;
	public int GroundPolyCount;
	public uint PolyHash;

	public bool HasEqualPathing(in NavigationParityDigest other)
	{
		return Width == other.Width
			&& Depth == other.Depth
			&& PathingHash == other.PathingHash
			&& WalkableCellCount == other.WalkableCellCount
			&& BlockedCellCount == other.BlockedCellCount;
	}

	public bool HasEqualNavMesh(in NavigationParityDigest other)
	{
		return HasNavMesh == other.HasNavMesh
			&& PolyCount == other.PolyCount
			&& GroundPolyCount == other.GroundPolyCount
			&& PolyHash == other.PolyHash;
	}

	public static NavigationParityDigest Capture(EcsWorld? world, Entity worldEntity)
	{
		NavigationParityDigest digest = default;
		if (world == null || worldEntity == Entity.Null) return digest;
		if (!world.IsAlive(worldEntity) || !world.Has<TerrainState>(worldEntity)) return digest;

		TerrainState state = world.Get<TerrainState>(worldEntity);
		digest.HasTerrainState = true;
		digest.Width = state.Width;
		digest.Depth = state.Depth;
		CapturePathing(ref digest, in state);
		CaptureNavMesh(ref digest, in state);
		return digest;
	}

	private static void CapturePathing(ref NavigationParityDigest digest, in TerrainState state)
	{
		int[,]? pathingCodes = state.PathingCodes;
		if (pathingCodes == null) return;

		int width = Math.Min(pathingCodes.GetLength(0), state.Width);
		int depth = Math.Min(pathingCodes.GetLength(1), state.Depth);
		uint hash = 2166136261u;
		int walkableCells = 0;
		int blockedCells = 0;
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				int code = pathingCodes[x, z];
				if (code == (int)TerrainPathingFlags.None) blockedCells++;
				else walkableCells++;
				unchecked
				{
					hash = (hash ^ (uint)x) * 16777619u;
					hash = (hash ^ (uint)z) * 16777619u;
					hash = (hash ^ (uint)code) * 16777619u;
				}
			}
		}

		digest.PathingHash = hash;
		digest.WalkableCellCount = walkableCells;
		digest.BlockedCellCount = blockedCells;
	}

	private static void CaptureNavMesh(ref NavigationParityDigest digest, in TerrainState state)
	{
		DtNavMesh? navMesh = state.NavMesh;
		if (navMesh == null) return;

		digest.HasNavMesh = true;
		uint hash = 2166136261u;
		int polyCount = 0;
		int groundPolyCount = 0;
		int maxTiles = navMesh.GetMaxTiles();
		for (int tileIndex = 0; tileIndex < maxTiles; tileIndex++)
		{
			DtMeshTile? tile = navMesh.GetTile(tileIndex);
			if (tile?.data?.header == null) continue;

			long polyRefBase = navMesh.GetPolyRefBase(tile);
			for (int polyIndex = 0; polyIndex < tile.data.header.polyCount; polyIndex++)
			{
				DtPoly poly = tile.data.polys[polyIndex];
				CalculatePolyCenter(tile, poly, out float centerX, out float centerZ);
				long polyRef = polyRefBase | (long)polyIndex;
				navMesh.GetPolyFlags(polyRef, out int polyFlags);
				navMesh.GetPolyArea(polyRef, out int polyArea);
				bool isGround = (polyFlags & (int)TerrainPathingFlags.Ground) != 0;
				polyCount++;
				if (isGround) groundPolyCount++;
				unchecked
				{
					hash = (hash ^ (uint)(int)MathF.Round(centerX * 100f)) * 16777619u;
					hash = (hash ^ (uint)(int)MathF.Round(centerZ * 100f)) * 16777619u;
					hash = (hash ^ (uint)polyFlags) * 16777619u;
					hash = (hash ^ (uint)polyArea) * 16777619u;
					hash = (hash ^ (uint)poly.vertCount) * 16777619u;
				}
			}
		}

		digest.PolyCount = polyCount;
		digest.GroundPolyCount = groundPolyCount;
		digest.PolyHash = hash;
	}

	private static void CalculatePolyCenter(DtMeshTile tile, DtPoly poly, out float centerX, out float centerZ)
	{
		float sumX = 0f;
		float sumZ = 0f;
		int vertexCount = poly.vertCount;
		for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
		{
			int vertexOffset = poly.verts[vertexIndex] * 3;
			sumX += tile.data.verts[vertexOffset];
			sumZ += tile.data.verts[vertexOffset + 2];
		}

		if (vertexCount <= 0)
		{
			centerX = 0f;
			centerZ = 0f;
			return;
		}

		centerX = sumX / vertexCount;
		centerZ = sumZ / vertexCount;
	}
}
