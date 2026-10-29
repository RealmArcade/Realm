using System.Numerics;
using DotRecast.Detour;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Client;
using Realm.Client.Services;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;

namespace Realm.Ecs.Tests
{
	[TestFixture]
	public class PathingTests
	{
		private EcsWorld _world
		{
			get { return GameContext.Simulation.World!; }
		}

		private TerrainNavMeshService _terrainNavMeshService
		{
			get { return GameContext.Simulation.TerrainNavMeshService!; }
		}

		private readonly NavMeshPathfinder _pathfinder = new();

		private MovementAndPathfindingService _movementService = null!;
		private Entity _worldEntity;

		[TearDown]
		public void TearDown()
		{
			GameContext.RecreateSimulation();
		}

		private void InitializeTerrain(int width = 64, int depth = 64, float quadSize = 2.0f, float cellSize = 0.5f)
		{
			var heights = new float[width, depth];
			var pathingCodes = new int[width, depth];
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					pathingCodes[x, z] = 8 | 4;
				}
			}

			EcsWorld world = _world;
			var terrainState = new TerrainState(width, depth, quadSize, cellSize, heights, pathingCodes);
			_worldEntity = world.Create(terrainState);
			GameContext.Simulation.WorldEntity = _worldEntity;

			_movementService = new MovementAndPathfindingService(GameContext.Simulation);
		}

		private void SetTerrainHeights(float[,] heights)
		{
			ref var terrain = ref _world.Get<TerrainState>(_worldEntity);
			terrain.SetHeights(heights);
			Assert.That(terrain.Cells, Is.Not.Null, "The terrain cells the bake reads have to exist.");
		}

		private void BakeAndAssert(int expectedObstacleCount)
		{
			ref var terrain = ref _world.Get<TerrainState>(_worldEntity);
			Assert.That(terrain.Cells, Is.Not.Null, "No terrain cells: the bake would return without doing anything.");

			int spawnedObstacles = _world.CountEntities(in QueryCache.AllPositionAndCollisionRadiusAndPropIdentityNoneDeadQuery)
				+ _world.CountEntities(in QueryCache.AllPositionAndCollisionRadiusAndBuildingNoneDeadQuery);
			Assert.That(spawnedObstacles, Is.EqualTo(expectedObstacleCount), "The test spawned a different set of obstacles than it expects to path around.");

			_terrainNavMeshService.BakeNavMesh(ref terrain);

			Assert.That(terrain.NavMesh, Is.Not.Null, "The bake produced no navigation mesh.");
			Assert.That(terrain.NavMeshQuery, Is.Not.Null, "The bake produced no navigation query.");
			Assert.That(_terrainNavMeshService.LastBakedObstacleCount, Is.EqualTo(expectedObstacleCount),
				"The bake did not use the obstacle volumes the test spawned.");
			Assert.That(CountNavMeshPolygons(terrain.NavMesh), Is.GreaterThan(0), "The baked mesh has no polygons, so no unit could path on it.");

			GameContext.Simulation.WorldEntity = _worldEntity;
		}

		private static int CountNavMeshPolygons(DtNavMesh? navMesh)
		{
			if (navMesh == null) return 0;

			int polygons = 0;
			int maxTiles = navMesh.GetMaxTiles();
			for (int tileIndex = 0; tileIndex < maxTiles; tileIndex++)
			{
				var tile = navMesh.GetTile(tileIndex);
				if (tile?.data?.header == null) continue;
				polygons += tile.data.header.polyCount;
			}

			return polygons;
		}

		private void SpawnObstacle(Vector3 pos, float radius)
		{
			_world.Create(
				new Position(pos),
				new CollisionRadius(radius),
				new PropIdentity("rock")
			);
		}

		private Entity SpawnUnit(Vector3 pos, float speed = 5.0f)
		{
			return _world.Create(
				new Position(pos),
				new MovementStats(speed, 10.0f, 10.0f),
				new CollisionRadius(0.5f),
				new DefinitionId("worker"),
				new Movable()
			);
		}

		[Test]
		public void TestUnreachableTargetDoesNotProduceStraightLineWaypoint()
		{
			InitializeTerrain();
			BakeAndAssert(expectedObstacleCount: 0);

			var start = new Vector3(0f, 0f, 0f);
			var walkableTarget = new Vector3(20f, 0f, 0f);

			var pf = new PathFollow();
			_pathfinder.ComputePath(_world.Get<TerrainState>(_worldEntity).NavMeshQuery!, start, walkableTarget, (ushort)TerrainPathingFlags.Ground, ref pf);
			Assert.That(pf.WaypointCount, Is.GreaterThanOrEqualTo(1), "Walkable targets should produce a path.");
			Assert.That(pf.Waypoints[0], Is.Not.EqualTo(walkableTarget), "Normal path waypoint should follow the navmesh, not the raw destination.");

			var unreachableTarget = new Vector3(0f, 100f, 0f);
			pf = new PathFollow();
			_pathfinder.ComputePath(_world.Get<TerrainState>(_worldEntity).NavMeshQuery!, start, unreachableTarget, (ushort)TerrainPathingFlags.Ground, ref pf);
			Assert.That(pf.WaypointCount, Is.GreaterThanOrEqualTo(1),
				"An elevated off-mesh destination should resolve to the walkable ground beneath it.");
			for (int i = 0; i < pf.WaypointCount; i++)
			{
				Assert.That(pf.Waypoints[i].Y, Is.LessThan(5f),
					"Waypoints must land on walkable ground, not at the raw (100m) destination height.");
			}

			var nearEdgeTarget = new Vector3(20f, 2f, 20f);
			pf = new PathFollow();
			_pathfinder.ComputePath(_world.Get<TerrainState>(_worldEntity).NavMeshQuery!, start, nearEdgeTarget, (ushort)TerrainPathingFlags.Ground, ref pf);
			Assert.That(pf.WaypointCount, Is.GreaterThanOrEqualTo(1), "Targets close to walkable terrain should still resolve to the nearest walkable point.");
		}

		[Test]
		public void TestOpenAreaPathing()
		{
			InitializeTerrain();
			BakeAndAssert(expectedObstacleCount: 0);

			var unit = SpawnUnit(new Vector3(-20f, 0f, -20f));
			var destination = new Vector3(20f, 0f, 20f);

			_world.Add(unit, new MoveTo(destination));

			var path = new List<Vector3>();
			path.Add(_world.Get<Position>(unit).Value);

			int ticks = 0;
			while (_world.Has<MoveTo>(unit) && ticks < 200)
			{
				_movementService.StepMovement(0.1f);
				path.Add(_world.Get<Position>(unit).Value);
				ticks++;
			}

			Assert.That(_world.Has<MoveTo>(unit), Is.False);
			float pathLength = 0;
			for (int i = 1; i < path.Count; i++)
			{
				pathLength += Vector3.Distance(path[i - 1], path[i]);
			}

			float straightLineDistance = Vector3.Distance(new Vector3(-20f, 0f, -20f), destination);
			Assert.That(pathLength, Is.LessThan(straightLineDistance * 1.1f));
		}

		[Test]
		public void TestPathingAroundCliffWall()
		{
			int width = 64;
			int depth = 64;
			float quadSize = 2.0f;
			InitializeTerrain(width, depth, quadSize);

			var heights = new float[width, depth];
			for (int z = 0; z < depth; z++)
			{
				if (z != 32 && z != 31 && z != 33)
				{
					heights[32, z] = 10.0f;
				}
			}

			SetTerrainHeights(heights);
			BakeAndAssert(expectedObstacleCount: 0);

			var start = new Vector3(-20f, 0f, 0f);
			var destination = new Vector3(20f, 0f, 0f);
			var unit = SpawnUnit(start);

			_world.Add(unit, new MoveTo(destination));

			var path = new List<Vector3>();
			path.Add(start);

			int ticks = 0;
			while (_world.Has<MoveTo>(unit) && ticks < 400)
			{
				_movementService.StepMovement(0.1f);
				path.Add(_world.Get<Position>(unit).Value);
				ticks++;
			}

			Assert.That(_world.Has<MoveTo>(unit), Is.False);

			float pathLength = 0;
			for (int i = 1; i < path.Count; i++)
			{
				pathLength += Vector3.Distance(path[i - 1], path[i]);
			}

			Assert.That(pathLength, Is.LessThan(48.0f));

			foreach (var pt in path)
			{
				if (Math.Abs(pt.X) < 1.5f)
				{
					Assert.That(Math.Abs(pt.Z), Is.LessThan(5.0f), $"The path crossed the cliff wall at Z={pt.Z}.");
				}
			}
		}

		[Test]
		public void TestGroundUnitRoundsSteepSlopeInsteadOfFreezingClimbing()
		{
			int width = 64, depth = 64;
			float quadSize = 2.0f;
			InitializeTerrain(width, depth, quadSize);

			const float slopePerCell = 1.5f;
			const float coneRadiusCells = 6.0f;
			const float peakHeight = 6.0f;
			const int centerCell = 32;

			var heights = new float[width, depth];
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					float r = (float)Math.Sqrt((x - centerCell) * (x - centerCell) + (z - centerCell) * (z - centerCell));
					heights[x, z] = Math.Max(0f, Math.Min(peakHeight, slopePerCell * (coneRadiusCells - r)));
				}
			}

			SetTerrainHeights(heights);
			BakeAndAssert(expectedObstacleCount: 0);

			var start = new Vector3(-20f, 0f, 0f);
			var destination = new Vector3(20f, 0f, 0f);
			var unit = SpawnUnit(start);
			_world.Add(unit, new MoveTo(destination));

			float maxY = 0f;
			int consecutiveStuckTicks = 0;
			int maxStuckRun = 0;

			int ticks = 0;
			while (_world.Has<MoveTo>(unit) && ticks < 600)
			{
				var beforePos = _world.Get<Position>(unit).Value;
				_movementService.StepMovement(0.1f);
				var afterPos = _world.Get<Position>(unit).Value;

				maxY = Math.Max(maxY, afterPos.Y);

				if (Vector3.Distance(beforePos, afterPos) < 0.02f)
				{
					consecutiveStuckTicks++;
					maxStuckRun = Math.Max(maxStuckRun, consecutiveStuckTicks);
				}
				else
				{
					consecutiveStuckTicks = 0;
				}

				ticks++;
			}

			Assert.That(_world.Has<MoveTo>(unit), Is.False,
				$"Unit should round the steep slope and arrive; max stuck run = {maxStuckRun} ticks, final = {_world.Get<Position>(unit).Value}.");
			Assert.That(maxY, Is.LessThan(peakHeight - 2.0f),
				$"Ground unit must not climb the >30deg slope (max Y reached {maxY:F2}).");
		}

		[Test]
		public void TestPathingAroundObstacleWall()
		{
			InitializeTerrain();

			int obstacles = 0;
			for (float z = -16.0f; z <= 16.0f; z += 2.0f)
			{
				SpawnObstacle(new Vector3(0f, 0f, z), 1.0f);
				obstacles++;
			}

			BakeAndAssert(obstacles);

			var start = new Vector3(-20f, 0f, 0f);
			var destination = new Vector3(20f, 0f, 0f);
			var unit = SpawnUnit(start);

			_world.Add(unit, new MoveTo(destination));

			var path = new List<Vector3>();
			path.Add(start);

			int ticks = 0;
			while (_world.Has<MoveTo>(unit) && ticks < 400)
			{
				var beforePos = _world.Get<Position>(unit).Value;
				_movementService.StepMovement(0.1f);
				var afterPos = _world.Get<Position>(unit).Value;
				path.Add(afterPos);

				if (Vector3.Distance(beforePos, afterPos) < 0.001f && _world.Has<MoveTo>(unit))
				{
					break;
				}

				ticks++;
			}

			Assert.That(_world.Has<MoveTo>(unit), Is.False);

			float pathLength = 0;
			for (int i = 1; i < path.Count; i++)
			{
				pathLength += Vector3.Distance(path[i - 1], path[i]);
			}

			Assert.That(pathLength, Is.LessThan(65.0f));
		}

		[Test]
		public void TestPathingAroundLargeBuilding()
		{
			InitializeTerrain();

			SpawnObstacle(new Vector3(0f, 0f, 0f), 3.0f);

			BakeAndAssert(expectedObstacleCount: 1);

			var start = new Vector3(-20f, 0f, 0f);
			var destination = new Vector3(20f, 0f, 0f);
			var unit = SpawnUnit(start);

			_world.Add(unit, new MoveTo(destination));

			var path = new List<Vector3>();
			path.Add(start);

			int ticks = 0;
			while (_world.Has<MoveTo>(unit) && ticks < 300)
			{
				_movementService.StepMovement(0.1f);
				path.Add(_world.Get<Position>(unit).Value);
				ticks++;
			}

			Assert.That(_world.Has<MoveTo>(unit), Is.False);

			float pathLength = 0;
			for (int i = 1; i < path.Count; i++)
			{
				pathLength += Vector3.Distance(path[i - 1], path[i]);
			}

			Assert.That(pathLength, Is.LessThan(45.0f));
		}

		[Test]
		public void TestPathingAroundDynamicUnits()
		{
			InitializeTerrain();

			for (float z = -6.0f; z <= 6.0f; z += 2.0f)
			{
				_world.Create(
					new Position(new Vector3(0f, 0f, z)),
					new CollisionRadius(0.8f),
					new DefinitionId("blocking_worker")
				);
			}

			BakeAndAssert(expectedObstacleCount: 0);

			var start = new Vector3(-20f, 0f, 0f);
			var destination = new Vector3(20f, 0f, 0f);
			var unit = SpawnUnit(start);

			_world.Add(unit, new MoveTo(destination));

			var path = new List<Vector3>();
			path.Add(start);

			int ticks = 0;
			while (_world.Has<MoveTo>(unit) && ticks < 300)
			{
				_movementService.StepMovement(0.1f);
				path.Add(_world.Get<Position>(unit).Value);
				ticks++;
			}

			Assert.That(_world.Has<MoveTo>(unit), Is.False);

			float pathLength = 0;
			for (int i = 1; i < path.Count; i++)
			{
				pathLength += Vector3.Distance(path[i - 1], path[i]);
			}

			Assert.That(pathLength, Is.LessThan(48.0f));
		}

		[Test]
		public void TestPathingSqueezeAndUnstuck()
		{
			InitializeTerrain(128, 128, 0.5f);

			int obstacles = 0;
			SpawnObstacle(new Vector3(0f, 0f, -2.3f), 1.5f);
			SpawnObstacle(new Vector3(0f, 0f, 2.3f), 1.5f);
			obstacles += 2;
			for (float z = -64.0f; z <= 64.0f; z += 3.0f)
			{
				if (z > -3.5f && z < 3.5f) continue;
				SpawnObstacle(new Vector3(0f, 0f, z), 1.5f);
				obstacles++;
			}

			BakeAndAssert(obstacles);

			var start = new Vector3(-20f, 0f, 0f);
			var destination = new Vector3(20f, 0f, 0f);
			var unit = SpawnUnit(start);

			_world.Add(unit, new MoveTo(destination));

			int ticks = 0;
			while (_world.Has<MoveTo>(unit) && ticks < 1000)
			{
				_movementService.StepMovement(0.0333f);
				ticks++;
			}

			Assert.That(_world.Has<MoveTo>(unit), Is.False);
			var finalPos = _world.Get<Position>(unit).Value;
			Assert.That(Vector3.Distance(finalPos, destination), Is.LessThan(1.5f));
		}

		[Test]
		public void TestLargeUnitSqueezesThroughNarrowGap()
		{
			InitializeTerrain(128, 128, 0.5f);

			int obstacles = 0;
			SpawnObstacle(new Vector3(0f, 0f, -2.3f), 1.5f);
			SpawnObstacle(new Vector3(0f, 0f, 2.3f), 1.5f);
			obstacles += 2;
			for (float z = -64.0f; z <= 64.0f; z += 3.0f)
			{
				if (z > -3.5f && z < 3.5f) continue;
				SpawnObstacle(new Vector3(0f, 0f, z), 1.5f);
				obstacles++;
			}

			BakeAndAssert(obstacles);

			var start = new Vector3(-20f, 0f, 0f);
			var destination = new Vector3(20f, 0f, 0f);
			var unit = _world.Create(
				new Position(start),
				new MovementStats(5.0f, 10.0f, 10.0f),
				new CollisionRadius(1.6f),
				new DefinitionId("worker_large"),
				new Movable()
			);

			_world.Add(unit, new MoveTo(destination));

			int ticks = 0;
			while (_world.Has<MoveTo>(unit) && ticks < 600)
			{
				_movementService.StepMovement(0.0333f);
				ticks++;
			}

			Assert.That(_world.Has<MoveTo>(unit), Is.False, $"Unit failed to squeeze through the gap within {ticks} ticks.");
			var finalPos = _world.Get<Position>(unit).Value;
			Assert.That(Vector3.Distance(finalPos, destination), Is.LessThan(1.5f));
		}

		[Test]
		public void TestLayeredPathing()
		{
			int width = 64;
			int depth = 64;
			float quadSize = 2.0f;
			InitializeTerrain(width, depth, quadSize);

			ref var terrain = ref _world.Get<TerrainState>(_worldEntity);
			for (int z = 0; z < depth; z++)
			{
				terrain.PathingCodes![45, z] = (int)TerrainPathingFlags.Flying;
			}

			BakeAndAssert(expectedObstacleCount: 0);

			var start = new Vector3(-20f, 0f, 0f);

			var groundUnit = SpawnUnit(start);
			_world.Add(groundUnit, new PathingFlags((int)TerrainPathingFlags.Ground));
			_world.Add(groundUnit, new MoveTo(new Vector3(20f, 0f, 0f)));

			int ticks = 0;
			while (_world.Has<MoveTo>(groundUnit) && ticks < 200)
			{
				_movementService.StepMovement(0.1f);
				ticks++;
			}

			var groundPos = _world.Get<Position>(groundUnit).Value;
			Assert.That(_world.Has<MoveTo>(groundUnit), Is.False, "Ground unit should reach its destination before the flying-only column.");
			Assert.That(groundPos.X, Is.GreaterThan(18f), $"Ground unit stopped at X={groundPos.X} instead of reaching X=20.");

			var flyingUnit = SpawnUnit(start);
			_world.Add(flyingUnit, new PathingFlags((int)TerrainPathingFlags.Flying));
			_world.Add(flyingUnit, new MoveTo(new Vector3(40f, 0f, 0f)));

			ticks = 0;
			while (_world.Has<MoveTo>(flyingUnit) && ticks < 200)
			{
				_movementService.StepMovement(0.1f);
				ticks++;
			}

			Assert.That(_world.Has<MoveTo>(flyingUnit), Is.False);
			var flyingPos = _world.Get<Position>(flyingUnit).Value;
			Assert.That(flyingPos.X, Is.GreaterThan(30f), "Flying unit should cross over the flying-only column.");
		}

		[Test]
		public void TestArrivalToleratesSmallHeightDifference()
		{
			InitializeTerrain();
			BakeAndAssert(expectedObstacleCount: 0);

			var unit = SpawnUnit(new Vector3(20f, 0f, 20f));
			_world.Add(unit, new PathingFlags((int)TerrainPathingFlags.Flying));
			var destination = new Vector3(20f, 1.0f, 20f);

			_world.Add(unit, new MoveTo(destination));

			int ticks = 0;
			while (_world.Has<MoveTo>(unit) && ticks < 100)
			{
				_movementService.StepMovement(0.1f);
				ticks++;
			}

			Assert.That(_world.Has<MoveTo>(unit), Is.False, $"Unit did not arrive at a destination with a 1.0 height offset within {ticks} ticks.");
		}

		[Test]
		public void TestFlyingUnitArrivesAcrossHeight()
		{
			InitializeTerrain();
			BakeAndAssert(expectedObstacleCount: 0);

			var unit = SpawnUnit(new Vector3(20f, 0f, 20f));
			_world.Add(unit, new PathingFlags((int)TerrainPathingFlags.Flying));
			var destination = new Vector3(20f, 5f, 20f);

			_world.Add(unit, new MoveTo(destination));

			int ticks = 0;
			while (_world.Has<MoveTo>(unit) && ticks < 100)
			{
				_movementService.StepMovement(0.1f);
				ticks++;
			}

			Assert.That(_world.Has<MoveTo>(unit), Is.False, "A flying unit hovers above the terrain and reaches its destination by horizontal distance.");
		}

		[Test]
		public void TestFlyingUnitFollowsTerrainHeight()
		{
			InitializeTerrain();

			ref var terrain = ref _world.Get<TerrainState>(_worldEntity);
			var raised = new float[terrain.Width, terrain.Depth];
			for (int z = 0; z < terrain.Depth; z++)
			{
				for (int x = 0; x < terrain.Width; x++)
				{
					raised[x, z] = 5.0f;
				}
			}

			SetTerrainHeights(raised);
			BakeAndAssert(expectedObstacleCount: 0);

			var unit = SpawnUnit(new Vector3(0f, 0f, 0f));
			_world.Add(unit, new PathingFlags((int)TerrainPathingFlags.Flying));
			_world.Add(unit, new MoveTo(new Vector3(20f, 0f, 0f)));

			int ticks = 0;
			while (_world.Has<MoveTo>(unit) && ticks < 60)
			{
				_movementService.StepMovement(0.1f);
				ticks++;
			}

			float altitude = _world.Get<Position>(unit).Value.Y;
			Assert.That(altitude, Is.InRange(4.5f, 5.5f), $"Flying unit should follow the terrain height (5.0), but was at Y={altitude}.");
		}
	}
}
