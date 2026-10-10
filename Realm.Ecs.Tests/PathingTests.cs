using Arch.Core;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Client;
using Realm.Client.Services;
using System.Numerics;

namespace Realm.Ecs.Tests
{
    [TestFixture]
    public class PathingTests
    {
	    private World _world
	    {
		    get
		    {
			    return ServiceLocator.Get<World>();
		    }
	    }

	    private WorldAccessor _worldAccessor
	    {
		    get
		    {
			    return ServiceLocator.Get<WorldAccessor>();
		    }
	    }

	    private TerrainNavMeshService _terrainNavMeshService
	    {
		    get
		    {
			    return ServiceLocator.Get<TerrainNavMeshService>();
		    }
	    }

	    private NavMeshPathfinder _pathfinder
	    {
		    get
		    {
			    return ServiceLocator.Get<NavMeshPathfinder>();
		    }
	    }

        private MovementAndPathfindingService _movementService = null!;
        private Entity _worldEntity;

        [SetUp]
        public void Setup()
        {
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
        }

        private void InitializeTerrain(int width = 64, int depth = 64, float quadSize = 2.0f, float cellSize = 0.5f)
        {
            float[,] heights = new float[width, depth];
            int[,] pathingCodes = new int[width, depth];
            for (int z = 0; z < depth; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    pathingCodes[x, z] = 8 | 4;
                }
            }

            var terrainState = new TerrainState(width, depth, quadSize, cellSize, heights, pathingCodes, new DotRecast.Detour.DtNavMesh(), new DotRecast.Detour.DtNavMeshQuery(new DotRecast.Detour.DtNavMesh()));
            _worldEntity = _world.Create(terrainState);

            _movementService = new MovementAndPathfindingService(_worldAccessor, _worldEntity, _pathfinder);
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
            // Regression: a destination that is not on any walkable polygon (e.g. the summit
            // of a steep mountain) must NOT fall back to a single straight-line waypoint at the
            // raw destination. That fallback let ground units climb terrain the navmesh rejects.
            InitializeTerrain();
            _terrainNavMeshService.BakeNavMesh(ref _world.Get<TerrainState>(_worldEntity));

            var start = new Vector3(0f, 0f, 0f);
            // Both points resolve to walkable polygons -> a normal path is expected.
            var walkableTarget = new Vector3(20f, 0f, 0f);

            var pf = new PathFollow();
            _pathfinder.ComputePath(_world.Get<TerrainState>(_worldEntity).NavMeshQuery, start, walkableTarget, (ushort)TerrainPathingFlags.Ground, ref pf);
            Assert.That(pf.WaypointCount, Is.GreaterThanOrEqualTo(1), "Walkable targets should produce a path.");
            Assert.That(pf.Waypoints[0], Is.Not.EqualTo(walkableTarget), "Normal path waypoint should follow the navmesh, not the raw destination.");

            // A destination entirely off the mesh (e.g. high above the flat terrain, like a
            // structure on a mountain) must NOT produce a straight-line waypoint at the raw
            // summit height: ground moves resolve to the walkable terrain beneath the target
            // (its "foot") so the unit walks up to the obstacle instead of climbing the wall.
            var unreachableTarget = new Vector3(0f, 100f, 0f);
            pf = new PathFollow();
            _pathfinder.ComputePath(_world.Get<TerrainState>(_worldEntity).NavMeshQuery, start, unreachableTarget, (ushort)TerrainPathingFlags.Ground, ref pf);
            Assert.That(pf.WaypointCount, Is.GreaterThanOrEqualTo(1),
                "An elevated off-mesh destination should resolve to the walkable ground beneath it.");
            for (int i = 0; i < pf.WaypointCount; i++)
            {
                Assert.That(pf.Waypoints[i].Y, Is.LessThan(5f),
                    "Waypoints must land on walkable ground, not at the raw (100m) destination height.");
            }

            // A destination just off the near walkable surface snaps to the closest walkable point.
            var nearEdgeTarget = new Vector3(20f, 2f, 20f);
            pf = new PathFollow();
            _pathfinder.ComputePath(_world.Get<TerrainState>(_worldEntity).NavMeshQuery, start, nearEdgeTarget, (ushort)TerrainPathingFlags.Ground, ref pf);
            Assert.That(pf.WaypointCount, Is.GreaterThanOrEqualTo(1), "Targets close to walkable terrain should still resolve to the nearest walkable point.");
        }


        [Test]
        public void TestOpenAreaPathing()
        {
            InitializeTerrain();
            _terrainNavMeshService.BakeNavMesh(ref _world.Get<TerrainState>(_worldEntity));

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

            ref var terrain = ref _world.Get<TerrainState>(_worldEntity);
            for (int z = 0; z < depth; z++)
            {
                if (z != 32 && z != 31 && z != 33)
                {
                    terrain.Heights![32, z] = 10.0f;
                }
            }

            _terrainNavMeshService.BakeNavMesh(ref terrain);

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
                if (Math.Abs(pt.X - 1.0f) < 0.5f)
                {
                    Assert.That(Math.Abs(pt.Z - 1.0f), Is.LessThan(4.0f));
                }
            }
        }

        [Test]
        public void TestGroundUnitRoundsSteepSlopeInsteadOfFreezingClimbing()
        {
            // Regression for the smoother-walking change: a ground unit whose destination lies
            // beyond a >30-degree slope must walk AROUND it (the 30-degree navmesh refuses the
            // slope) without freezing at the slope's lip or climbing it. Steep cells must never
            // push the unit upward: sliding keeps Y clamped to the current polygon height.
            int width = 64, depth = 64;
            float quadSize = 2.0f;
            InitializeTerrain(width, depth, quadSize);

            // Smooth cone (~37deg flanks, >30) rising out of a flat base, centered on the map.
            const float slopePerCell = 1.5f;
            const float coneRadiusCells = 6.0f;
            const float peakHeight = 6.0f;
            const int centerCell = 32;

            ref var terrain = ref _world.Get<TerrainState>(_worldEntity);
            for (int z = 0; z < depth; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    float r = (float)Math.Sqrt((x - centerCell) * (x - centerCell) + (z - centerCell) * (z - centerCell));
                    terrain.Heights![x, z] = Math.Max(0f, Math.Min(peakHeight, slopePerCell * (coneRadiusCells - r)));
                }
            }

            _terrainNavMeshService.BakeNavMesh(ref terrain);

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

            for (float z = -16.0f; z <= 16.0f; z += 2.0f)
            {
                SpawnObstacle(new Vector3(0f, 0f, z), 1.0f);
            }

            _terrainNavMeshService.BakeNavMesh(ref _world.Get<TerrainState>(_worldEntity));

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

            if (_world.Has<PathFollow>(unit))
            {
                var pf = _world.Get<PathFollow>(unit);
                Console.WriteLine($"Obstacle Wall test - Waypoint count: {pf.WaypointCount}, Current Index: {pf.CurrentWaypointIndex}");
                for (int i = 0; i < pf.WaypointCount; i++)
                {
                    Console.WriteLine($"  WP {i}: {pf.Waypoints[i]}");
                }
            }
            else
            {
                Console.WriteLine("Obstacle Wall test - No PathFollow component!");
            }
            Console.WriteLine($"Obstacle Wall test - Final position: {_world.Get<Position>(unit).Value}");

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

            _terrainNavMeshService.BakeNavMesh(ref _world.Get<TerrainState>(_worldEntity));

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

            _terrainNavMeshService.BakeNavMesh(ref _world.Get<TerrainState>(_worldEntity));

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

            SpawnObstacle(new Vector3(0f, 0f, -2.3f), 1.5f);
            SpawnObstacle(new Vector3(0f, 0f, 2.3f), 1.5f);
            for (float z = -64.0f; z <= 64.0f; z += 3.0f)
            {
                if (z > -3.5f && z < 3.5f) continue;
                SpawnObstacle(new Vector3(0f, 0f, z), 1.5f);
            }

            _terrainNavMeshService.BakeNavMesh(ref _world.Get<TerrainState>(_worldEntity));

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
            Console.WriteLine($"Squeeze and Unstuck Ticks: {ticks}");
        }

        [Test]
        public void TestLargeUnitSqueezesThroughNarrowGap()
        {
            // Collision separation only pushes units apart once they overlap past 45% of
            // their combined radius, so a large unit can squeeze through a gap that is
            // narrower than its collision diameter (intentional RTS clumping behavior).
            InitializeTerrain(128, 128, 0.5f);

            SpawnObstacle(new Vector3(0f, 0f, -2.3f), 1.5f);
            SpawnObstacle(new Vector3(0f, 0f, 2.3f), 1.5f);
            for (float z = -64.0f; z <= 64.0f; z += 3.0f)
            {
                if (z > -3.5f && z < 3.5f) continue;
                SpawnObstacle(new Vector3(0f, 0f, z), 1.5f);
            }

            _terrainNavMeshService.BakeNavMesh(ref _world.Get<TerrainState>(_worldEntity));

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

            _terrainNavMeshService.BakeNavMesh(ref terrain);

            var start = new Vector3(-20f, 0f, 0f);
            
            // Ground unit (pathing flag Ground) cannot cross the flying-only column,
            // so give it a destination on the near side and expect it to reach it.
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
            
            // Flying unit (pathing flag Flying) bypasses the navmesh and flies
            // straight to a destination past the flying-only column.
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
            _terrainNavMeshService.BakeNavMesh(ref _world.Get<TerrainState>(_worldEntity));

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
            _terrainNavMeshService.BakeNavMesh(ref _world.Get<TerrainState>(_worldEntity));

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

            // Raise the whole terrain so we can verify the unit sticks to the terrain height
            // instead of flying at a hardcoded absolute altitude.
            float[,] raised = new float[terrain.Width, terrain.Depth];
            for (int z = 0; z < terrain.Depth; z++)
            {
                for (int x = 0; x < terrain.Width; x++)
                {
                    raised[x, z] = 5.0f;
                }
            }
            terrain.SetHeights(raised);
            _terrainNavMeshService.BakeNavMesh(ref terrain);

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
