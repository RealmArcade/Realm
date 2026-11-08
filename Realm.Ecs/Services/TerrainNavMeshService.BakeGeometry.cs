// Extracted from TerrainNavMeshService.cs (task 6 decomposition).
using System;
using System.Collections.Generic;
using System.Numerics;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;

namespace Realm.Ecs.Services;

public partial class TerrainNavMeshService
{
	private float[,] GetBakeHeights(in TerrainState state, int width, int depth)
	{
		var bakeHeights = new float[width + 1, depth + 1];
		for (int z = 0; z <= depth; z++)
		{
			for (int x = 0; x <= width; x++)
			{
				bakeHeights[x, z] = GetVertexHeight(in state, x, z);
			}
		}

		return bakeHeights;
	}

	private void AddObstacleVolumes(List<(Vector3 Pos, float Radius)> obstacles, SimpleInputGeomProvider geom, float[,] bakeHeights, int width, int depth, float quadSize, float cellSize, float halfW, float halfD)
	{
		const int segments = 16;
		foreach (var obs in obstacles)
		{
			float radius = obs.Radius;
			if (radius <= GameplayConstants.MinimumAuthoredCollisionRadius) continue;

			float effectiveRadius = radius + AgentRadius + cellSize * 0.5f;
			float ground = SampleBakeHeight(bakeHeights, obs.Pos.X, obs.Pos.Z, width, depth, quadSize, halfW, halfD);

			var volumeVerts = new float[segments * 3];
			for (int i = 0; i < segments; i++)
			{
				float angle = MathF.Tau * i / segments;
				volumeVerts[i * 3] = obs.Pos.X + MathF.Cos(angle) * effectiveRadius;
				volumeVerts[i * 3 + 1] = ground;
				volumeVerts[i * 3 + 2] = obs.Pos.Z + MathF.Sin(angle) * effectiveRadius;
			}

			var volume = new RcConvexVolume
			{
				verts = volumeVerts,
				hmin = ground - 2.0f,
				hmax = ground + AgentHeight,
				areaMod = new RcAreaModification(0)
			};
			geom.AddConvexVolume(volume);
		}
	}

	private static float SampleBakeHeight(float[,] bakeHeights, float worldX, float worldZ, int width, int depth, float quadSize, float halfW, float halfD)
	{
		float gridX = Math.Clamp((worldX + halfW) / quadSize, 0f, width);
		float gridZ = Math.Clamp((worldZ + halfD) / quadSize, 0f, depth);
		int x0 = Math.Min((int)gridX, width - 1);
		int z0 = Math.Min((int)gridZ, depth - 1);
		float fractionX = gridX - x0;
		float fractionZ = gridZ - z0;

		float northWest = bakeHeights[x0, z0];
		float northEast = bakeHeights[x0 + 1, z0];
		float southWest = bakeHeights[x0, z0 + 1];
		float southEast = bakeHeights[x0 + 1, z0 + 1];
		float north = northWest + (northEast - northWest) * fractionX;
		float south = southWest + (southEast - southWest) * fractionX;
		return north + (south - north) * fractionZ;
	}

	private SimpleInputGeomProvider CreateInputGeom(float[,] bakeHeights, int width, int depth, float quadSize)
	{
		var meshVerts = new List<float>();
		for (int z = 0; z <= depth; z++)
		{
			for (int x = 0; x <= width; x++)
			{
				float lx = (x - width / 2.0f) * quadSize;
				float lz = (z - depth / 2.0f) * quadSize;
				meshVerts.Add(lx);
				meshVerts.Add(bakeHeights[x, z]);
				meshVerts.Add(lz);
			}
		}

		var indices = new List<int>();
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				int v00 = z * (width + 1) + x;
				int v10 = z * (width + 1) + (x + 1);
				int v01 = (z + 1) * (width + 1) + x;
				int v11 = (z + 1) * (width + 1) + (x + 1);
				indices.Add(v00);
				indices.Add(v01);
				indices.Add(v10);
				indices.Add(v10);
				indices.Add(v01);
				indices.Add(v11);
			}
		}

		return new SimpleInputGeomProvider(meshVerts, indices);
	}

	private void AddUnwalkableVolumes(in TerrainState state, SimpleInputGeomProvider geom, int width, int depth, float quadSize)
	{
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				var pathingCode = state.PathingCodes != null ? (TerrainPathingFlags)state.PathingCodes[x, z] : TerrainPathingFlags.Ground | TerrainPathingFlags.Buildable;
				bool isWalkable = pathingCode != TerrainPathingFlags.None;
				if (!isWalkable)
				{
					float lx = (x + 0.5f - width / 2.0f) * quadSize;
					float lz = (z + 0.5f - depth / 2.0f) * quadSize;
					float h = GetVertexHeight(in state, x, z);
					float halfS = quadSize * 0.45f;
					var vol = new RcConvexVolume
					{
						verts = new float[]
						{
							lx - halfS, h, lz - halfS,
							lx + halfS, h, lz - halfS,
							lx + halfS, h, lz + halfS,
							lx - halfS, h, lz + halfS
						},
						hmin = h - 5.0f,
						hmax = h + 5.0f,
						areaMod = new RcAreaModification(0)
					};
					geom.AddConvexVolume(vol);
				}
			}
		}
	}

	private void InitializeNavMeshPolys(DtNavMeshCreateParams pars, RcPolyMesh mesh, RcVec3f bmin, float quadSize, int width, int depth, in TerrainState state)
	{
		Span<Vector2> polyVerts = stackalloc Vector2[12];
		for (int i = 0; i < mesh.npolys; i++)
		{
			pars.polyAreas[i] = mesh.areas[i];

			int nv = 0;
			for (int j = 0; j < mesh.nvp; j++)
			{
				int vIdx = mesh.polys[i * mesh.nvp * 2 + j];
				if (vIdx < 0 || vIdx >= mesh.nverts)
					break;

				float wx = bmin.X + mesh.verts[vIdx * 3] * mesh.cs;
				float wz = bmin.Z + mesh.verts[vIdx * 3 + 2] * mesh.cs;

				if (nv < polyVerts.Length)
					polyVerts[nv++] = new Vector2(wx, wz);
			}

			pars.polyFlags[i] = ClassifyPolygonPathingFlags(state.PathingCodes, width, depth, quadSize, polyVerts[..nv]);
		}
	}

	private static int ClassifyPolygonPathingFlags(int[,]? pathingCodes, int width, int depth, float quadSize, ReadOnlySpan<Vector2> polygonVertices)
	{
		if (pathingCodes == null) return (int)(TerrainPathingFlags.Ground | TerrainPathingFlags.Buildable);
		if (polygonVertices.Length == 0) return 0;

		float halfW = width / 2.0f * quadSize;
		float halfD = depth / 2.0f * quadSize;
		float minX = float.MaxValue;
		float maxX = float.MinValue;
		float minZ = float.MaxValue;
		float maxZ = float.MinValue;
		float sumX = 0f;
		float sumZ = 0f;
		for (int i = 0; i < polygonVertices.Length; i++)
		{
			var vertex = polygonVertices[i];
			if (vertex.X < minX) minX = vertex.X;
			if (vertex.X > maxX) maxX = vertex.X;
			if (vertex.Y < minZ) minZ = vertex.Y;
			if (vertex.Y > maxZ) maxZ = vertex.Y;
			sumX += vertex.X;
			sumZ += vertex.Y;
		}

		int startX = Math.Clamp((int)Math.Floor((minX + halfW) / quadSize), 0, width - 1);
		int endX = Math.Clamp((int)Math.Floor((maxX + halfW) / quadSize), 0, width - 1);
		int startZ = Math.Clamp((int)Math.Floor((minZ + halfD) / quadSize), 0, depth - 1);
		int endZ = Math.Clamp((int)Math.Floor((maxZ + halfD) / quadSize), 0, depth - 1);

		int combinedFlags = 0;
		for (int z = startZ; z <= endZ; z++)
		{
			float centreZ = (z + 0.5f - depth / 2.0f) * quadSize;
			for (int x = startX; x <= endX; x++)
			{
				float centreX = (x + 0.5f - width / 2.0f) * quadSize;
				if (!IsPointInsidePolygon(polygonVertices, centreX, centreZ)) continue;
				combinedFlags |= pathingCodes[x, z];
			}
		}

		int centroidGridX = Math.Clamp((int)Math.Floor((sumX / polygonVertices.Length + halfW) / quadSize), 0, width - 1);
		int centroidGridZ = Math.Clamp((int)Math.Floor((sumZ / polygonVertices.Length + halfD) / quadSize), 0, depth - 1);
		combinedFlags |= pathingCodes[centroidGridX, centroidGridZ];

		return combinedFlags;
	}

	private static bool IsPointInsidePolygon(ReadOnlySpan<Vector2> polygonVertices, float pointX, float pointZ)
	{
		bool inside = false;
		int count = polygonVertices.Length;
		for (int i = 0, j = count - 1; i < count; j = i++)
		{
			var current = polygonVertices[i];
			var previous = polygonVertices[j];
			if ((current.Y > pointZ) == (previous.Y > pointZ)) continue;
			if (pointX >= (previous.X - current.X) * (pointZ - current.Y) / (previous.Y - current.Y) + current.X) continue;
			inside = !inside;
		}

		return inside;
	}

	private static int CollectPolygonVertices(DtMeshTile tile, DtPoly poly, Span<Vector2> destination)
	{
		int count = Math.Min(poly.vertCount, destination.Length);
		for (int i = 0; i < count; i++)
		{
			int vertexIndex = poly.verts[i];
			destination[i] = new Vector2(tile.data.verts[vertexIndex * 3], tile.data.verts[vertexIndex * 3 + 2]);
		}

		return count;
	}

	private void BuildNavMesh(ref TerrainState state, SimpleInputGeomProvider geom, int width, int depth, float quadSize)
	{
		RcConfig cfg = new RcConfig(
			RcPartition.WATERSHED,
			state.CellSize > 0.0001f ? state.CellSize : TerrainState.DefaultCellSize, NavMeshCellHeight,
			AgentMaxSlope, AgentHeight, AgentRadius, AgentMaxClimb,
			8, 20,
			3.0f, 1.3f,
			6,
			6.0f, 1.0f,
			true, true, true,
			new RcAreaModification(1),
			true
		);
		RcVec3f bmin = geom.GetMeshBoundsMin();
		RcVec3f bmax = geom.GetMeshBoundsMax();
		bmin.Y -= 10f;
		bmax.Y += 50f;
		var bcfg = new RcBuilderConfig(cfg, bmin, bmax);
		var builder = new RcBuilder();
		var result = builder.Build(geom, bcfg, true);

		if (result.Mesh == null || result.Mesh.npolys == 0)
		{
			Console.Error.WriteLine($"[BakeNavMesh] BUILDER FAILED: no polys generated. width={width} depth={depth} quadSize={quadSize}");
			return;
		}

		var pars = new DtNavMeshCreateParams();
		pars.verts = result.Mesh.verts;
		pars.vertCount = result.Mesh.nverts;
		pars.polys = result.Mesh.polys;
		pars.polyCount = result.Mesh.npolys;
		pars.nvp = result.Mesh.nvp;
		pars.bmin = result.Mesh.bmin;
		pars.bmax = result.Mesh.bmax;
		pars.cs = result.Mesh.cs;
		pars.ch = result.Mesh.ch;
		pars.buildBvTree = true;
		pars.walkableHeight = AgentHeight;
		pars.walkableRadius = AgentRadius;
		pars.walkableClimb = AgentMaxClimb;
		pars.polyAreas = new int[result.Mesh.npolys];
		pars.polyFlags = new int[result.Mesh.npolys];

		InitializeNavMeshPolys(pars, result.Mesh, result.Mesh.bmin, quadSize, width, depth, in state);

		if (result.MeshDetail != null)
		{
			pars.detailMeshes = result.MeshDetail.meshes;
			pars.detailVerts = result.MeshDetail.verts;
			pars.detailVertsCount = result.MeshDetail.nverts;
			pars.detailTris = result.MeshDetail.tris;
			pars.detailTriCount = result.MeshDetail.ntris;
		}

		var navMeshData = DtNavMeshBuilder.CreateNavMeshData(pars);
		if (navMeshData != null)
		{
			state.NavMesh = new DtNavMesh();
			state.NavMesh.Init(navMeshData, pars.nvp, 0);
			state.NavMeshQuery = new DtNavMeshQuery(state.NavMesh);
		}
	}
}
