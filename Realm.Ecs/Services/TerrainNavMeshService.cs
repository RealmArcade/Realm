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
	/// <summary>
	/// How many obstacle volumes went into the last bake. Reported by the host and by each peer so a mesh that differs from
	/// the authority's can be traced to the input it was built from rather than to the bake itself.
	/// </summary>
	public int LastBakedObstacleCount { get; private set; }

	/// <summary>
	/// True when this instance bakes with obstacle volumes received from the authority rather than collecting them from the
	/// entities it can see. A peer does not run the map script, so most of the authority's obstacle volumes have no entity
	/// behind them on the peer at all. An empty authoritative set still counts: a map with no blockers is baked with no
	/// volumes rather than with whatever bodies the peer happens to hold.
	/// </summary>
	public bool UsesAuthoritativeObstacles => _hasAuthoritativeObstacles;

	/// <summary>
	/// True once this instance has built a mesh. The volumes a bake consumed are only meaningful after one, so a peer
	/// that is seated before the match bakes must be told nothing rather than told that the map has no blockers.
	/// </summary>
	public bool HasBakedMesh { get; private set; }

	private bool _hasAuthoritativeObstacles;
	private readonly List<Vector3> _authoritativeObstaclePositions = new();
	private readonly List<float> _authoritativeObstacleRadii = new();
	private readonly List<Vector3> _bakedObstaclePositions = new();
	private readonly List<float> _bakedObstacleRadii = new();
	private int _bakedObstacleSignature = int.MinValue;

	private const float NavMeshCellHeight = 0.1f;
	private const float AgentRadius = 1.2f;
	private const float AgentHeight = 2.5f;
	private const float AgentMaxClimb = 0.9f;
	private const float AgentMaxSlope = 55.0f;

	private readonly SimulationContext _context;
	private readonly EntityCallback<Position, MoveTo, PathFollow> _invalidatePathsDelegate;
	private Vector3 _invalidateCenter;
	private float _invalidateRadiusSq;

	private readonly EntityCallback<Position, CollisionRadius, PropIdentity> _collectPropObstacleDelegate;
	private readonly EntityCallback<Position, CollisionRadius, Building> _collectBuildingObstacleDelegate;
	private readonly EntityCallback<Position, CollisionRadius, PropIdentity> _checkPropObstacleDelegate;
	private readonly EntityCallback<Position, CollisionRadius, Building> _checkBuildingObstacleDelegate;
	private readonly List<(Vector3 Pos, float Radius)> _navMeshObstacles = new();
	private float _checkCellX;
	private float _checkCellZ;
	private bool _isCellBlockedByOtherObstacle;

	public TerrainNavMeshService(SimulationContext context)
	{
		_context = context;
		_invalidatePathsDelegate = InvalidatePathsQueryAction;
		_collectPropObstacleDelegate = CollectPropObstacle;
		_collectBuildingObstacleDelegate = CollectBuildingObstacle;
		_checkPropObstacleDelegate = CheckPropObstacle;
		_checkBuildingObstacleDelegate = CheckBuildingObstacle;
	}
}
