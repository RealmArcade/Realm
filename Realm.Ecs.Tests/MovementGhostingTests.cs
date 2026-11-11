using System;
using System.Numerics;
using NUnit.Framework;
using Realm.Client;
using Realm.Client.Services;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;

namespace Realm.Ecs.Tests
{
	/// <summary>
	///     The movement rules a body follows when another body is in its way: a hero walks through soldiers and through
	///     buildings that are not worth blocking, while a soldier, a second hero and another worker of the same player
	///     cannot pass through and go around the contact instead. Every case here walks a mover down the Z=0 line at a
	///     stationary body spawned after the navigation bake, so the path the navigation mesh gives is straight through
	///     it and any sideways movement is the collision rule answering.
	/// </summary>
	[TestFixture]
	public class MovementGhostingTests
	{
		private static readonly Vector3 Start = new(-8f, 0f, 0f);
		private static readonly Vector3 Destination = new(8f, 0f, 0f);
		private const float LateralDriftThatProvesABlock = 0.75f;

		private EcsWorld World => GameContext.Simulation.World!;

		private TerrainNavMeshService _terrainNavMeshService => GameContext.Simulation.TerrainNavMeshService!;

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

			// The ambient world has to exist before the terrain state is built: its cold payload - the cells - registers
			// against whichever world is ambient at that moment, and a payload registered before any world exists is never
			// the one the world it is created for resolves.
			EcsWorld world = World;
			var terrainState = new TerrainState(width, depth, quadSize, cellSize, heights, pathingCodes);
			_worldEntity = world.Create(terrainState);
			GameContext.Simulation.WorldEntity = _worldEntity;
			_movementService = new MovementAndPathfindingService(GameContext.Simulation);

			ref TerrainState terrain = ref World.Get<TerrainState>(_worldEntity);
			Assert.That(terrain.Cells, Is.Not.Null, "The test terrain produced no cells, so the bake has nothing to build on.");
			_terrainNavMeshService.BakeNavMesh(ref terrain);
			Assert.That(terrain.NavMeshQuery, Is.Not.Null, "The bake produced no navigation query.");
			GameContext.Simulation.WorldEntity = _worldEntity;
		}

		private Entity SpawnUnit(Vector3 position, int playerIndex = 1, string definitionId = "unit/footman")
		{
			return World.Create(
				new Position(position),
				new MovementStats(5f, 10f, 10f),
				new CollisionRadius(0.5f),
				new DefinitionId(definitionId),
				new UnitOwnerPlayer(playerIndex),
				new Movable()
			);
		}

		private Entity SpawnBuilding(Vector3 position, int playerIndex)
		{
			return World.Create(
				new Position(position),
				new CollisionRadius(2f),
				new DefinitionId("building/hall"),
				new UnitOwnerPlayer(playerIndex),
				new Building()
			);
		}

		private Vector3 WalkThroughTheLine(Entity mover, out float maximumLateralDrift, out float closestApproachToTheBlocker, int maximumSteps = 400)
		{
			World.Add(mover, new MoveTo(Destination));
			maximumLateralDrift = 0f;
			closestApproachToTheBlocker = float.MaxValue;

			int steps = 0;
			while (World.Has<MoveTo>(mover) && steps < maximumSteps)
			{
				_movementService.StepMovement(0.1f);
				Vector3 position = World.Get<Position>(mover).Value;
				float drift = MathF.Abs(position.Z - Start.Z);
				if (drift > maximumLateralDrift) maximumLateralDrift = drift;

				float approach = MathF.Sqrt(position.X * position.X + position.Z * position.Z);
				if (approach < closestApproachToTheBlocker) closestApproachToTheBlocker = approach;
				steps++;
			}

			return World.Get<Position>(mover).Value;
		}

		/// <summary>
		///     A body that passes through the one on the line keeps walking: it crosses the origin the blocker stands on.
		/// </summary>
		private static void AssertCrossedTheBlocker(Vector3 end)
		{
			Assert.That(end.X, Is.GreaterThan(4f), $"The mover stopped at {end} instead of crossing the body on its line.");
		}

		/// <summary>
		///     A body that is blocked never occupies the blocker's space: it slides around the contact instead, which is the
		///     behaviour the ghosting rule has to be compared against.
		/// </summary>
		private static void AssertWentAroundTheBlocker(float closestApproach, float contactDistance)
		{
			Assert.That(closestApproach, Is.GreaterThanOrEqualTo(contactDistance), $"The mover came within {closestApproach:0.##} of the blocker, closer than the {contactDistance:0.##} its contact allows.");
		}

		[Test]
		public void HeroPassesThroughItsOwnSoldier()
		{
			InitializeTerrain();
			SpawnUnit(Vector3.Zero);

			Entity hero = SpawnUnit(Start);
			World.Add(hero, new Hero());

			Vector3 end = WalkThroughTheLine(hero, out float drift, out float closestApproach);

			AssertCrossedTheBlocker(end);
			Assert.That(closestApproach, Is.LessThan(0.3f), $"The hero went around its own soldier instead of through it (closest approach {closestApproach:0.##}).");
			Assert.That(drift, Is.LessThan(LateralDriftThatProvesABlock),
				$"The hero was pushed aside by its own soldier (drift {drift:0.##}).");
		}

		[Test]
		public void SoldierIsStillBodyBlockedByAnotherSoldier()
		{
			InitializeTerrain();
			SpawnUnit(Vector3.Zero);

			Entity soldier = SpawnUnit(Start);

			Vector3 end = WalkThroughTheLine(soldier, out float drift, out float closestApproach);

			AssertWentAroundTheBlocker(closestApproach, 0.55f);
			Assert.That(drift, Is.GreaterThan(0.4f),
				$"A soldier slid sideways around another soldier (drift {drift:0.##}); the mover has to leave the blocker's line.");
		}

		[Test]
		public void HeroPassesThroughAnAlliedBuilding()
		{
			InitializeTerrain();
			SpawnBuilding(Vector3.Zero, playerIndex: 1);

			Entity hero = SpawnUnit(Start, playerIndex: 1);
			World.Add(hero, new Hero());

			Vector3 end = WalkThroughTheLine(hero, out float drift, out float closestApproach);

			AssertCrossedTheBlocker(end);
			Assert.That(closestApproach, Is.LessThan(0.3f), $"The hero went around its own building instead of through it (closest approach {closestApproach:0.##}).");
			Assert.That(drift, Is.LessThan(LateralDriftThatProvesABlock),
				$"The hero was stopped by its own building (drift {drift:0.##}).");
		}

		[Test]
		public void HeroGoesAroundAFunctionalEnemyBuilding()
		{
			InitializeTerrain();
			SpawnBuilding(Vector3.Zero, playerIndex: 2);

			Entity hero = SpawnUnit(Start, playerIndex: 1);
			World.Add(hero, new Hero());

			Vector3 end = WalkThroughTheLine(hero, out float drift, out float closestApproach);

			AssertWentAroundTheBlocker(closestApproach, 1.4f);
			Assert.That(drift, Is.GreaterThan(1f), $"The hero had to leave the building's line to get past it (drift {drift:0.##}).");
		}

		[Test]
		public void HeroPassesThroughAnEnemyBuildingStillUnderConstruction()
		{
			InitializeTerrain();
			Entity building = SpawnBuilding(Vector3.Zero, playerIndex: 2);
			World.Add(building, new UnderConstruction());

			Entity hero = SpawnUnit(Start, playerIndex: 1);
			World.Add(hero, new Hero());

			Vector3 end = WalkThroughTheLine(hero, out float drift, out float closestApproach);

			AssertCrossedTheBlocker(end);
			Assert.That(closestApproach, Is.LessThan(0.3f), $"The hero went around a construction silhouette instead of through it (closest approach {closestApproach:0.##}).");
			Assert.That(drift, Is.LessThan(LateralDriftThatProvesABlock),
				$"The hero was stopped by a construction silhouette (drift {drift:0.##}).");
		}

		[Test]
		public void HeroPassesThroughADormantEnemyBuilding()
		{
			InitializeTerrain();
			Entity building = SpawnBuilding(Vector3.Zero, playerIndex: 2);
			World.Add(building, new RepairableBuilding());
			World.Add(building, new Dormant());

			Entity hero = SpawnUnit(Start, playerIndex: 1);
			World.Add(hero, new Hero());

			Vector3 end = WalkThroughTheLine(hero, out float drift, out float closestApproach);

			AssertCrossedTheBlocker(end);
			Assert.That(closestApproach, Is.LessThan(0.3f), $"The hero went around a dormant ruin instead of through it (closest approach {closestApproach:0.##}).");
			Assert.That(drift, Is.LessThan(LateralDriftThatProvesABlock),
				$"The hero was stopped by a dormant ruin (drift {drift:0.##}).");
		}

		[Test]
		public void WorkersOfTheSamePlayerBodyBlockEachOther()
		{
			InitializeTerrain();
			Entity waitingWorker = SpawnUnit(Vector3.Zero, playerIndex: 1, definitionId: "unit/building_worker");
			World.Add(waitingWorker, new Worker());

			Entity movingWorker = SpawnUnit(Start, playerIndex: 1, definitionId: "unit/building_worker");
			World.Add(movingWorker, new Worker());

			Vector3 end = WalkThroughTheLine(movingWorker, out float drift, out float closestApproach);

			AssertWentAroundTheBlocker(closestApproach, 0.55f);
			Assert.That(drift, Is.GreaterThan(0.4f),
				$"One of a player's workers had to leave the other's line to get past it (drift {drift:0.##}).");
		}

		[Test]
		public void WorkersOfDifferentPlayersPassThroughEachOther()
		{
			InitializeTerrain();
			Entity awaitingWorker = SpawnUnit(Vector3.Zero, playerIndex: 2, definitionId: "unit/building_worker");
			World.Add(awaitingWorker, new Worker());

			Entity movingWorker = SpawnUnit(Start, playerIndex: 1, definitionId: "unit/building_worker");
			World.Add(movingWorker, new Worker());

			Vector3 end = WalkThroughTheLine(movingWorker, out float drift, out float closestApproach);

			AssertCrossedTheBlocker(end);
			Assert.That(closestApproach, Is.LessThan(0.3f), $"A worker went around another player's worker instead of through it (closest approach {closestApproach:0.##}).");
			Assert.That(drift, Is.LessThan(LateralDriftThatProvesABlock),
				$"A worker was stopped by another player's worker (drift {drift:0.##}).");
		}
	}
}
