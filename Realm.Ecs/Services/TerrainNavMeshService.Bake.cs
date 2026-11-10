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
	public void BakeNavMesh(ref TerrainState state)
	{
		int width = state.Width;
		int depth = state.Depth;
		float quadSize = state.QuadSize;
		float halfW = width / 2.0f * quadSize;
		float halfD = depth / 2.0f * quadSize;

		var obstacles = GetNavMeshObstacles();
		LastBakedObstacleCount = obstacles.Count;
		_bakedObstacleSignature = ComputeObstacleSignature(obstacles);
		_bakedObstaclePositions.Clear();
		_bakedObstacleRadii.Clear();
		for (int index = 0; index < obstacles.Count; index++)
		{
			_bakedObstaclePositions.Add(obstacles[index].Pos);
			_bakedObstacleRadii.Add(obstacles[index].Radius);
		}
		if (state.Cells == null) return;
		var bakeHeights = GetBakeHeights(in state, width, depth);

		var geom = CreateInputGeom(bakeHeights, width, depth, quadSize);
		AddObstacleVolumes(obstacles, geom, bakeHeights, width, depth, quadSize, state.CellSize, halfW, halfD);
		AddUnwalkableVolumes(in state, geom, width, depth, quadSize);

		BuildNavMesh(ref state, geom, width, depth, quadSize);
		HasBakedMesh = true;
	}

	private List<(Vector3 Pos, float Radius)> GetNavMeshObstacles()
	{
		_navMeshObstacles.Clear();
		if (UsesAuthoritativeObstacles)
		{
			for (int index = 0; index < _authoritativeObstaclePositions.Count; index++)
			{
				_navMeshObstacles.Add((_authoritativeObstaclePositions[index], _authoritativeObstacleRadii[index]));
			}

			return _navMeshObstacles;
		}

		var world = _context.World;
		if (world == null) return _navMeshObstacles;

		world.Query(in QueryCache.AllPositionAndCollisionRadiusAndPropIdentityNoneDeadQuery, _collectPropObstacleDelegate);
		world.Query(in QueryCache.AllPositionAndCollisionRadiusAndBuildingNoneDeadQuery, _collectBuildingObstacleDelegate);
		return _navMeshObstacles;
	}

	/// <summary>
	/// True when the volumes this instance would bake with are not the ones its mesh was last built from, which is how the
	/// authority notices that a prop, a building or a blocker its script spawned has appeared or gone since the last bake.
	/// A building counts as gone only once it is destroyed: it stops contributing a volume the moment it is dead, and the
	/// next bake paths units through the ground it used to stand on.
	/// </summary>
	public bool ObstaclesChangedSinceBake()
	{
		var obstacles = GetNavMeshObstacles();
		return ComputeObstacleSignature(obstacles) != _bakedObstacleSignature;
	}

	/// <summary>
	/// Fingerprints one set of obstacle volumes so two bakes can be compared without keeping the volumes themselves.
	/// </summary>
	private static int ComputeObstacleSignature(List<(Vector3 Pos, float Radius)> obstacles)
	{
		int hash = 17;
		for (int index = 0; index < obstacles.Count; index++)
		{
			Vector3 position = obstacles[index].Pos;
			float radius = obstacles[index].Radius;
			hash = hash * 31 + BitConverter.SingleToInt32Bits(position.X);
			hash = hash * 31 + BitConverter.SingleToInt32Bits(position.Y);
			hash = hash * 31 + BitConverter.SingleToInt32Bits(position.Z);
			hash = hash * 31 + BitConverter.SingleToInt32Bits(radius);
		}

		return hash;
	}

	/// <summary>
	/// Replaces the obstacle volumes this instance bakes with the ones the authority baked with. Called on a peer after the
	/// authority reports a bake, so both sides build their mesh from the same volumes and answer the same path query with
	/// the same corridor.
	/// </summary>
	public void SetAuthoritativeObstacleVolumes(ReadOnlySpan<Vector3> positions, ReadOnlySpan<float> radii)
	{
		_hasAuthoritativeObstacles = true;
		_authoritativeObstaclePositions.Clear();
		_authoritativeObstacleRadii.Clear();
		int count = Math.Min(positions.Length, radii.Length);
		for (int index = 0; index < count; index++)
		{
			_authoritativeObstaclePositions.Add(positions[index]);
			_authoritativeObstacleRadii.Add(radii[index]);
		}
	}

	/// <summary>
	/// The obstacle volumes the last bake used. The authority sends these to its peers so a peer bakes an identical mesh.
	/// </summary>
	public ReadOnlySpan<Vector3> BakedObstaclePositions => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_bakedObstaclePositions);

	/// <summary>
	/// The radii that belong to <see cref="BakedObstaclePositions"/>.
	/// </summary>
	public ReadOnlySpan<float> BakedObstacleRadii => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_bakedObstacleRadii);
}
