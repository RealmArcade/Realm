using System.Numerics;
using DotRecast.Detour;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Client;
using Realm.Client.Services;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;

namespace Realm.Ecs.Tests
{
	[TestFixture]
	public class MeleeChaseIntegrationTests
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
		private CombatAndDamageService _combatService = null!;
		private Entity _worldEntity;

		private const float DeltaSeconds = 0.05f;

		[SetUp]
		public void Setup()
		{
			_combatService = new CombatAndDamageService(GameContext.Simulation);
		}

		[TearDown]
		public void TearDown()
		{
			GameContext.RecreateSimulation();
		}

		private void InitializeFlatTerrain(int width = 128, int depth = 128, float quadSize = 2.0f, float cellSize = 0.5f)
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

			var terrainState = new TerrainState(width, depth, quadSize, cellSize, heights, pathingCodes, new DtNavMesh(), new DtNavMeshQuery(new DtNavMesh()));
			_worldEntity = _world.Create(terrainState);
			GameContext.Simulation.WorldEntity = _worldEntity;
			_terrainNavMeshService.BakeNavMesh(ref _world.Get<TerrainState>(_worldEntity));

			_movementService = new MovementAndPathfindingService(GameContext.Simulation);
		}

		private void InitializeSteppedTerrain(float plateauHeight, int wallCellX = 72, int width = 128, int depth = 128, float quadSize = 2.0f, float cellSize = 0.5f)
		{
			float[,] heights = new float[width, depth];
			int[,] pathingCodes = new int[width, depth];
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					heights[x, z] = x >= wallCellX ? plateauHeight : 0f;
					pathingCodes[x, z] = 8 | 4;
				}
			}

			var terrainState = new TerrainState(width, depth, quadSize, cellSize, heights, pathingCodes, new DtNavMesh(), new DtNavMeshQuery(new DtNavMesh()));
			_worldEntity = _world.Create(terrainState);
			GameContext.Simulation.WorldEntity = _worldEntity;
			_terrainNavMeshService.BakeNavMesh(ref _world.Get<TerrainState>(_worldEntity));

			_movementService = new MovementAndPathfindingService(GameContext.Simulation);
		}

		private void InitializeFlatTerrainWithBlock(int blockMinCellX, int blockMaxCellX, int blockMinCellZ, int blockMaxCellZ, int width = 128, int depth = 128, float quadSize = 2.0f, float cellSize = 0.5f)
		{
			float[,] heights = new float[width, depth];
			int[,] pathingCodes = new int[width, depth];
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					bool inBlock = x >= blockMinCellX && x <= blockMaxCellX && z >= blockMinCellZ && z <= blockMaxCellZ;
					pathingCodes[x, z] = inBlock ? 0 : (8 | 4);
				}
			}

			var terrainState = new TerrainState(width, depth, quadSize, cellSize, heights, pathingCodes, new DtNavMesh(), new DtNavMeshQuery(new DtNavMesh()));
			_worldEntity = _world.Create(terrainState);
			GameContext.Simulation.WorldEntity = _worldEntity;
			_terrainNavMeshService.BakeNavMesh(ref _world.Get<TerrainState>(_worldEntity));

			_movementService = new MovementAndPathfindingService(GameContext.Simulation);
		}

		private Entity CreatePlayer(int index)
		{
			return _world.Create(new DefinitionId("player_" + index), new UnitOwnerPlayer(index));
		}

		private Entity SpawnMeleeAttacker(Vector3 pos, float damage, float range, float cooldown, float speed, float scanRadius, PlayerEntity owner, float scale = 1.0f, bool canTargetAir = false)
		{
			return _world.Create(
				new Position(pos),
				new Attack(damage, range, cooldown),
				new Owner(owner),
				new ScanRadius(scanRadius),
				new Health(400, 400),
				new Resistances(5),
				new DefinitionId("adventurer"),
				new CombatTargeting(canTargetAir, true),
				new MovementStats(speed, 20f, 10f),
				new PathingFlags((int)TerrainPathingFlags.Ground),
				new Movable(),
				new CollisionScale(scale)
			);
		}

		private Entity CreateMarchingZombie(Vector3 pos, Vector3 moveTarget, float maxHp, float armor, float speed, PlayerEntity owner, float damage, float range, float scanRadius)
		{
			return _world.Create(
				new Position(pos),
				new Health(maxHp, maxHp),
				new Resistances(armor),
				new Owner(owner),
				new DefinitionId("zombie_soldier"),
				new Attack(damage, range, 1.4f),
				new ScanRadius(scanRadius),
				new MovementStats(speed, 20f, 10f),
				new PathingFlags((int)TerrainPathingFlags.Ground),
				new Movable(),
				new CollisionScale(1.0f),
				new MoveTo(moveTarget)
			);
		}

		private Entity SpawnRangedTower(Vector3 pos, float damage, float range, float cooldown, float scanRadius, PlayerEntity owner)
		{
			return _world.Create(
				new Position(pos),
				new Attack(damage, range, cooldown),
				new Owner(owner),
				new ScanRadius(scanRadius),
				new Health(800, 800),
				new Resistances(5),
				new DefinitionId("arrow_tower"),
				new CombatTargeting(false, true),
				new Building(),
				new CollisionRadius(2.0f),
				new CollisionScale(1.0f)
			);
		}

		private Entity CreatePassiveEnemy(Vector3 pos, float maxHp, float armor, PlayerEntity owner)
		{
			return _world.Create(
				new Position(pos),
				new Health(maxHp, maxHp),
				new Resistances(armor),
				new Owner(owner),
				new DefinitionId("zombie_warrior"),
				new ScanRadius(0),
				new MovementStats(0f, 20f, 10f),
				new PathingFlags((int)TerrainPathingFlags.Ground)
			);
		}

		private Entity CreatePassiveStructure(Vector3 pos, PlayerEntity owner, float collisionRadius = 3.0f)
		{
			return _world.Create(
				new Position(pos),
				new Health(2000, 2000),
				new Resistances(5),
				new Owner(owner),
				new DefinitionId("castle"),
				new Building(),
				new CollisionRadius(collisionRadius),
				new CollisionScale(1.0f)
			);
		}

		private Entity CreateFlyingEnemy(Vector3 pos, PlayerEntity owner)
		{
			return _world.Create(
				new Position(pos),
				new Health(300, 300),
				new Resistances(10),
				new Owner(owner),
				new DefinitionId("cyber_dragon"),
				new ScanRadius(40),
				new PathingFlags((int)TerrainPathingFlags.Flying)
			);
		}

		private void Tick(float delta)
		{
			_movementService.StepMovement(delta);
			_combatService.StepCombat(delta);

			var query = new QueryDescription().WithAll<Attack>();
			_world.Query(in query, (Entity e, ref Attack atk) =>
			{
				if (atk.CurrentCooldown > 0)
				{
					atk.CurrentCooldown = Math.Max(0f, atk.CurrentCooldown - delta);
				}
			});
		}

		[Test]
		public void TestMeleeChasesMovingMarchingZombieAndLandsHits()
		{
			InitializeFlatTerrain();

			var playerOwner = new PlayerEntity(CreatePlayer(0));
			var enemyOwner = new PlayerEntity(CreatePlayer(1));

			var zombie = CreateMarchingZombie(
				new Vector3(10f, 0f, 0f),
				new Vector3(60f, 0f, 0f),
				maxHp: 1000, armor: 2, speed: 5.0f, enemyOwner,
				damage: 13f, range: 1.8f, scanRadius: 40f);

			var attacker = SpawnMeleeAttacker(
				new Vector3(0f, 0f, 0f),
				damage: 20f, range: 1.8f, cooldown: 1.0f,
				speed: 5.5f, scanRadius: 40f, playerOwner);

			int ticks = 0;
			int maxTicks = (int)(20.0f / DeltaSeconds);

			float minObservedDist = float.MaxValue;
			int attackCount = 0;
			float prevDist = float.MaxValue;

			while (ticks < maxTicks)
			{
				Tick(DeltaSeconds);
				ticks++;

				if (!_world.IsAlive(attacker) || !_world.IsAlive(zombie)) break;

				var dist = Vector3.Distance(_world.Get<Position>(attacker).Value, _world.Get<Position>(zombie).Value);
				if (dist < minObservedDist) minObservedDist = dist;

				if (_world.Has<AttackTarget>(attacker) && _world.Get<AttackTarget>(attacker).Target == zombie)
				{
					if (ticks % (int)(1.0f / DeltaSeconds) == 0)
					{
						float progress = prevDist - dist;
						if (progress < 0.01f) attackCount++;
						prevDist = dist;
					}
				}
			}

			var finalHealth = _world.Get<Health>(zombie).Current;
			Console.WriteLine($"MeleeChase - ticks={ticks} dist={Vector3.Distance(_world.Get<Position>(attacker).Value, _world.Get<Position>(zombie).Value):F2} minDist={minObservedDist:F2} hp={finalHealth} dead={_world.Has<Dead>(zombie)}");

			Assert.That(_world.Has<Dead>(zombie) || finalHealth < 1000f,
				"The melee attacker should have damaged the marching zombie.");
		}

		[Test]
		public void TestMeleeCannotDamageTargetOnElevatedPlateau()
		{
			InitializeSteppedTerrain(plateauHeight: 6f);

			var playerOwner = new PlayerEntity(CreatePlayer(0));
			var enemyOwner = new PlayerEntity(CreatePlayer(1));

			var zombie = CreateMarchingZombie(
				new Vector3(20f, 6f, 5f),
				new Vector3(24f, 6f, 5f),
				maxHp: 1000, armor: 2, speed: 6.0f, enemyOwner,
				damage: 13f, range: 1.8f, scanRadius: 40f);

			var attacker = SpawnMeleeAttacker(
				new Vector3(8f, 0f, 5f),
				damage: 20f, range: 1.8f, cooldown: 1.0f,
				speed: 5.5f, scanRadius: 40f, playerOwner);

			int maxTicks = (int)(20.0f / DeltaSeconds);
			bool lastHasTarget = false;
			int ticksWithTarget = 0;

			for (int ticks = 0; ticks < maxTicks; ticks++)
			{
				Tick(DeltaSeconds);
				if (!_world.IsAlive(attacker) || !_world.IsAlive(zombie)) break;

				bool hasTarget = _world.Has<AttackTarget>(attacker)
				                 && _world.Get<AttackTarget>(attacker).Target == zombie;
				if (hasTarget) ticksWithTarget++;
				lastHasTarget = hasTarget;
			}

			var zombieHp = _world.Get<Health>(zombie).Current;
			var attackerPos = _world.Get<Position>(attacker).Value;

			Assert.That(zombieHp, Is.EqualTo(1000f),
				"A melee attacker at the cliff base must NOT damage an enemy on the summit (vertical delta blocks range).");
			Assert.That(attackerPos.Y, Is.LessThan(5.0f),
				"The melee attacker must not climb past the 30-degree navmesh wall.");
			Assert.That(lastHasTarget, Is.False,
				"The melee attacker must not remain permanently engaged with an unreachable elevated target (no chase-forever loop).");
			Assert.That(ticksWithTarget, Is.LessThan(40),
				"The melee attacker must not spend the simulation repeatedly chasing the unreachable target.");
		}

		[Test]
		public void TestRangedTowerOnSummitCannotHitZombieAtCliffFoot()
		{
			InitializeSteppedTerrain(plateauHeight: 25f);

			var playerOwner = new PlayerEntity(CreatePlayer(0));
			var enemyOwner = new PlayerEntity(CreatePlayer(1));

			var zombie = CreateMarchingZombie(
				new Vector3(8f, 0f, 5f),
				new Vector3(13f, 0f, 5f),
				maxHp: 2000, armor: 2, speed: 6.0f, enemyOwner,
				damage: 13f, range: 1.8f, scanRadius: 40f);

			var tower = SpawnRangedTower(
				new Vector3(16f, 25f, 5f),
				damage: 40f, range: 20f, cooldown: 1.0f,
				scanRadius: 50f, playerOwner);

			int maxTicks = (int)(20.0f / DeltaSeconds);
			for (int ticks = 0; ticks < maxTicks; ticks++)
			{
				Tick(DeltaSeconds);
				if (!_world.IsAlive(zombie)) break;
			}

			var zombieHp = _world.Get<Health>(zombie).Current;
			Assert.That(zombieHp, Is.EqualTo(2000f),
				"A tower on a high summit must NOT hit an enemy at the cliff foot: vertical terrain delta counts toward range.");
		}

		[Test]
		public void TestBigScaledMeleeCanCloseAndDamageZombie()
		{
			InitializeFlatTerrain();

			var playerOwner = new PlayerEntity(CreatePlayer(0));
			var enemyOwner = new PlayerEntity(CreatePlayer(1));

			var zombie = CreateMarchingZombie(
				new Vector3(6f, 0f, 0f),
				new Vector3(6f, 0f, 0f),
				maxHp: 1000, armor: 2, speed: 0f, enemyOwner,
				damage: 13f, range: 1.8f, scanRadius: 40f);

			var attacker = SpawnMeleeAttacker(
				new Vector3(0f, 0f, 0f),
				damage: 20f, range: 1.8f, cooldown: 1.0f,
				speed: 5.5f, scanRadius: 40f, playerOwner, scale: 2.55f);

			int maxTicks = (int)(15.0f / DeltaSeconds);
			for (int ticks = 0; ticks < maxTicks; ticks++)
			{
				Tick(DeltaSeconds);
				if (!_world.IsAlive(zombie)) break;
			}

			var finalHealth = _world.Get<Health>(zombie).Current;
			var dist = Vector3.Distance(_world.Get<Position>(attacker).Value, _world.Get<Position>(zombie).Value);
			Console.WriteLine($"BigScaledMelee - dist={dist:F2} hp={finalHealth} dead={_world.Has<Dead>(zombie)}");

			Assert.That(dist, Is.LessThan(2.6f),
				"The scaled hero must be able to close within a distance where it can attack even though separation pushes it apart.");
			Assert.That(_world.Has<Dead>(zombie) || finalHealth < 1000f,
				"The big-scaled melee attacker must land hits on a stationary zombie.");
		}

		[Test]
		public void TestMeleeChoosesReachableEnemyWhenClosestIsUnreachable()
		{
			InitializeFlatTerrain();

			var playerOwner = new PlayerEntity(CreatePlayer(0));
			var enemyOwner = new PlayerEntity(CreatePlayer(1));

			var ledgeEnemy = CreatePassiveEnemy(
				new Vector3(65f, 6f, 5f), maxHp: 1000, armor: 2, enemyOwner);

			var pathZombie = CreatePassiveEnemy(
				new Vector3(30f, 0f, 5f), maxHp: 1000, armor: 2, enemyOwner);

			var hero = SpawnMeleeAttacker(
				new Vector3(60f, 0f, 5f),
				damage: 20f, range: 1.8f, cooldown: 1.0f,
				speed: 5.5f, scanRadius: 40f, playerOwner);

			int maxTicks = (int)(30.0f / DeltaSeconds);
			Entity? acquired = null;
			for (int ticks = 0; ticks < maxTicks; ticks++)
			{
				Tick(DeltaSeconds);
				if (_world.Has<AttackTarget>(hero))
				{
					acquired = _world.Get<AttackTarget>(hero).Target;
					break;
				}
			}

			Assert.That(acquired, Is.Not.Null,
				"The hero must acquire a target even when the closest enemy is unreachable.");
			Assert.That(acquired, Is.EqualTo(pathZombie),
				"The hero must pick the closest REACHABLE enemy, not the unreachable ledge enemy.");
		}

		[Test]
		public void TestGroundMeleeIgnoresHoveringFlyerAndAttacksReachableGround()
		{
			InitializeFlatTerrain();

			var playerOwner = new PlayerEntity(CreatePlayer(0));
			var enemyOwner = new PlayerEntity(CreatePlayer(1));

			var dragon = CreateFlyingEnemy(
				new Vector3(2f, 14f, 0f), enemyOwner);

			var warrior = CreatePassiveEnemy(
				new Vector3(18f, 0f, 0f), maxHp: 1000, armor: 6, enemyOwner);

			var hero = SpawnMeleeAttacker(
				new Vector3(0f, 0f, 0f),
				damage: 50f, range: 2f, cooldown: 1.0f,
				speed: 10f, scanRadius: 20f, playerOwner, scale: 2.55f, canTargetAir: true);

			int maxTicks = (int)(15.0f / DeltaSeconds);
			Entity? acquired = null;
			bool everTargetedFlying = false;
			for (int ticks = 0; ticks < maxTicks; ticks++)
			{
				Tick(DeltaSeconds);
				if (_world.Has<AttackTarget>(hero))
				{
					var t = _world.Get<AttackTarget>(hero).Target;
					if (t == dragon) everTargetedFlying = true;
					acquired = t;
					break;
				}
			}

			Assert.That(everTargetedFlying, Is.False,
				"A ground melee hero must never target a unit hovering at flight altitude.");
			Assert.That(acquired, Is.EqualTo(warrior),
				"The hero must engage the reachable ground enemy instead of being blocked by the hovering flyer.");
		}

		[Test]
		public void TestChaserClearsCorpseMoveToAndReacquiresAfterKill()
		{
			InitializeFlatTerrain();

			var playerOwner = new PlayerEntity(CreatePlayer(0));
			var enemyOwner = new PlayerEntity(CreatePlayer(1));

			var chased = CreateMarchingZombie(
				new Vector3(14f, 0f, 0f),
				new Vector3(14f, 0f, 0f),
				maxHp: 100, armor: 2, speed: 3.0f, enemyOwner,
				damage: 13f, range: 1.8f, scanRadius: 40f);

			var next = CreateMarchingZombie(
				new Vector3(6f, 0f, 4f),
				new Vector3(6f, 0f, 4f),
				maxHp: 1000, armor: 2, speed: 3.0f, enemyOwner,
				damage: 13f, range: 1.8f, scanRadius: 40f);

			var hero = SpawnMeleeAttacker(
				new Vector3(0f, 0f, 0f),
				damage: 50f, range: 2f, cooldown: 1.0f,
				speed: 5.5f, scanRadius: 40f, playerOwner);

			_world.Add(hero, new AttackTarget(chased));
			_world.Add(hero, new MoveTo(new Vector3(14f, 0f, 0f)));

			_world.Add<Dead>(chased);

			Tick(DeltaSeconds);

			Assert.That(_world.Has<MoveTo>(hero), Is.False,
				"Clearing a dead target must also clear the stale chase MoveTo (no march-to-corpse).");
			Assert.That(_world.Has<AttackTarget>(hero), Is.False,
				"The dead target must be dropped from the AttackTarget slot.");

			int maxTicks = 5;
			Entity? reacquired = null;
			for (int ticks = 0; ticks < maxTicks; ticks++)
			{
				Tick(DeltaSeconds);
				if (_world.Has<AttackTarget>(hero))
				{
					reacquired = _world.Get<AttackTarget>(hero).Target;
					break;
				}
			}

			Assert.That(reacquired, Is.EqualTo(next),
				"The hero must re-acquire the next reachable enemy immediately after the kill.");
		}

		[Test]
		public void TestMeleeDropsOrFightsTargetApproachableOnlyOutsideRange()
		{
			InitializeFlatTerrainWithBlock(blockMinCellX: 64, blockMaxCellX: 66, blockMinCellZ: 64, blockMaxCellZ: 66);

			var playerOwner = new PlayerEntity(CreatePlayer(0));
			var enemyOwner = new PlayerEntity(CreatePlayer(1));

			var blockEnemy = CreatePassiveEnemy(
				new Vector3(3f, 0f, 3f), maxHp: 1000, armor: 2, enemyOwner);

			var hero = SpawnMeleeAttacker(
				new Vector3(-8f, 0f, 3f),
				damage: 50f, range: 2f, cooldown: 1.0f,
				speed: 5.5f, scanRadius: 40f, playerOwner, scale: 2.55f);

			_world.Add(hero, new AttackTarget(blockEnemy));

			int maxTicks = (int)(10.0f / DeltaSeconds);
			for (int ticks = 0; ticks < maxTicks; ticks++)
			{
				Tick(DeltaSeconds);
				if (!_world.IsAlive(hero) || !_world.IsAlive(blockEnemy)) break;
			}

			var finalHealth = _world.Get<Health>(blockEnemy).Current;
			bool stillEngaged = _world.Has<AttackTarget>(hero)
			                    && _world.Get<AttackTarget>(hero).Target == blockEnemy;
			var heroPos = _world.Get<Position>(hero).Value;

			Console.WriteLine($"BlockTarget - heroPos=({heroPos.X:F2},{heroPos.Z:F2}) hp={finalHealth} engaged={stillEngaged}");

			Assert.That(!stillEngaged || finalHealth < 1000f,
				"The melee unit must either damage the target or drop it — never stay engaged while frozen without dealing damage.");
		}

		[Test]
		public void TestMeleeDamagesLargeStructure()
		{
			InitializeFlatTerrainWithBlock(blockMinCellX: 58, blockMaxCellX: 63, blockMinCellZ: 61, blockMaxCellZ: 65);

			var playerOwner = new PlayerEntity(CreatePlayer(0));
			var enemyOwner = new PlayerEntity(CreatePlayer(1));

			var structure = CreatePassiveStructure(
				new Vector3(-7.2f, 0f, -1f), enemyOwner, collisionRadius: 3.0f);

			var hero = SpawnMeleeAttacker(
				new Vector3(-16f, 0f, -1f),
				damage: 50f, range: 2f, cooldown: 1.0f,
				speed: 5.5f, scanRadius: 40f, playerOwner);

			int maxTicks = (int)(20.0f / DeltaSeconds);
			bool damaged = false;
			for (int ticks = 0; ticks < maxTicks; ticks++)
			{
				Tick(DeltaSeconds);
				if (!_world.IsAlive(hero) || !_world.IsAlive(structure)) break;
				damaged |= !_world.Has<Dead>(structure)
				           && _world.Get<Health>(structure).Current < 2000f;
			}

			var structureHp = _world.Get<Health>(structure).Current;
			Console.WriteLine($"LargeStructure - hp={structureHp} damaged={damaged}");

			Assert.That(structureHp, Is.LessThan(2000f),
				"The melee hero must be able to damage a large structure instead of abandoning it and freezing idle.");
		}

		[Test]
		public void TestMeleeDamagesEnemyAgainstWall()
		{
			InitializeFlatTerrainWithBlock(blockMinCellX: 64, blockMaxCellX: 66, blockMinCellZ: 64, blockMaxCellZ: 66);

			var playerOwner = new PlayerEntity(CreatePlayer(0));
			var enemyOwner = new PlayerEntity(CreatePlayer(1));

			var wallEnemy = CreatePassiveEnemy(
				new Vector3(3.5f, 0f, 3f), maxHp: 1000, armor: 2, enemyOwner);

			var hero = SpawnMeleeAttacker(
				new Vector3(-8f, 0f, 3f),
				damage: 50f, range: 2f, cooldown: 1.0f,
				speed: 5.5f, scanRadius: 40f, playerOwner, scale: 2.55f);

			_world.Add(hero, new AttackTarget(wallEnemy));

			int maxTicks = (int)(20.0f / DeltaSeconds);
			for (int ticks = 0; ticks < maxTicks; ticks++)
			{
				Tick(DeltaSeconds);
				if (!_world.IsAlive(hero) || !_world.IsAlive(wallEnemy)) break;
			}

			var wallEnemyHp = _world.Get<Health>(wallEnemy).Current;
			bool stillEngaged = _world.Has<AttackTarget>(hero)
			                    && _world.Get<AttackTarget>(hero).Target == wallEnemy;
			var heroPos = _world.Get<Position>(hero).Value;
			Console.WriteLine($"WallEnemy - heroPos=({heroPos.X:F2},{heroPos.Z:F2}) hp={wallEnemyHp} engaged={stillEngaged}");

			Assert.That(!stillEngaged || wallEnemyHp < 1000f,
				"A melee hero must damage a wall-adjacent enemy or drop it — never stay engaged while frozen without dealing damage.");
		}
	}
}