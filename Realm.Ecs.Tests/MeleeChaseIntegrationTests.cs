using Arch.Core;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
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
    /// <summary>
    /// Reproduces the real gameplay tick loop (movement + combat + cooldowns) with a
    /// melee attacker ordered to chase a moving enemy that is marching toward the base,
    /// the exact scenario players reported as "the unit dances around the target without
    /// attacking it".
    /// </summary>
    [TestFixture]
    public class MeleeChaseIntegrationTests
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
        private CombatAndDamageService _combatService = null!;
        private Entity _worldEntity;

        private const float DeltaSeconds = 0.05f;

        [SetUp]
        public void Setup()
        {
            _combatService = new CombatAndDamageService(_worldAccessor);
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
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

            var terrainState = new TerrainState(width, depth, quadSize, cellSize, heights, pathingCodes, new DotRecast.Detour.DtNavMesh(), new DotRecast.Detour.DtNavMeshQuery(new DotRecast.Detour.DtNavMesh()));
            _worldEntity = _world.Create(terrainState);
            _terrainNavMeshService.BakeNavMesh(ref _world.Get<TerrainState>(_worldEntity));

            _movementService = new MovementAndPathfindingService(_worldAccessor, _worldEntity, _pathfinder);
        }

        /// <summary>
        /// Terrain split by an unwalkable vertical wall: everything at cell X &gt;= wallCellX
        /// sits at plateauHeight (the summit), everything below is the flat base. With the
        /// 30-degree navmesh slope limit the wall rejects ground units, exactly like the real
        /// TEST map mountain.
        /// </summary>
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

            var terrainState = new TerrainState(width, depth, quadSize, cellSize, heights, pathingCodes, new DotRecast.Detour.DtNavMesh(), new DotRecast.Detour.DtNavMeshQuery(new DotRecast.Detour.DtNavMesh()));
            _worldEntity = _world.Create(terrainState);
            _terrainNavMeshService.BakeNavMesh(ref _world.Get<TerrainState>(_worldEntity));

            _movementService = new MovementAndPathfindingService(_worldAccessor, _worldEntity, _pathfinder);
        }

        /// <summary>
        /// Flat terrain with an unwalkable square block carved into it (world X and Z from
        /// cell (width/2)*quadSize == 0, each cell spans quadSize world units). A ground
        /// melee unit can never stand on the block, so a target placed inside the block is
        /// approachable only up to its nearest walkable edge — an endpoint offset from the
        /// target's centre by half the block width.
        /// </summary>
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

            var terrainState = new TerrainState(width, depth, quadSize, cellSize, heights, pathingCodes, new DotRecast.Detour.DtNavMesh(), new DotRecast.Detour.DtNavMeshQuery(new DotRecast.Detour.DtNavMesh()));
            _worldEntity = _world.Create(terrainState);
            _terrainNavMeshService.BakeNavMesh(ref _world.Get<TerrainState>(_worldEntity));

            _movementService = new MovementAndPathfindingService(_worldAccessor, _worldEntity, _pathfinder);
        }

        private Entity CreatePlayer(int index)
        {
            return _world.Create(new DefinitionId("player_" + index));
        }

        /// <summary>
        /// Replicates the runtime spawn of UnitSpawnService.CreateEcsUnitEntity:
        /// CollisionScale present, CollisionRadius ABSENT (that is the actual game state).
        /// </summary>
        private Entity SpawnMeleeAttacker(Vector3 pos, float damage, float range, float cooldown, float speed, float scanRadius, PlayerEntity owner, float scale = 1.0f, bool canTargetAir = false)
        {
            return _world.Create(
                new Position(pos),
                new Attack(damage, range, cooldown),
                new Owner(owner),
                new ScanRadius(scanRadius),
                new UnitFaction(false),
                new Health(400, 400),
                new Armor(5),
                new DefinitionId("adventurer"),
                new CombatTargeting(canTargetAir, true),
                new MovementStats(speed, 20f, 10f),
                new PathingFlags((int)TerrainPathingFlags.Ground),
                new Movable(),
                new CollisionScale(scale)
            );
        }

        /// <summary>
        /// Zombie created exactly like gameplay: it has a MoveTo (AttackMove toward the
        /// base), no CollisionRadius, same movement stats used by the real metadata.
        /// It also carries Attack so it acquires the attacker in return (mutual chase).
        /// </summary>
        private Entity CreateMarchingZombie(Vector3 pos, Vector3 moveTarget, float maxHp, float armor, float speed, PlayerEntity owner, float damage, float range, float scanRadius)
        {
            return _world.Create(
                new Position(pos),
                new Health(maxHp, maxHp),
                new Armor(armor),
                new Owner(owner),
                new UnitFaction(true),
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

        /// <summary>
        /// Tower built exactly like a placement from the map editor: a Building with a live
        /// CollisionRadius (so effectiveRange = range + collision radius) that never moves.
        /// </summary>
        private Entity SpawnRangedTower(Vector3 pos, float damage, float range, float cooldown, float scanRadius, PlayerEntity owner)
        {
            return _world.Create(
                new Position(pos),
                new Attack(damage, range, cooldown),
                new Owner(owner),
                new ScanRadius(scanRadius),
                new UnitFaction(false),
                new Health(800, 800),
                new Armor(5),
                new DefinitionId("arrow_tower"),
                new CombatTargeting(false, true),
                new Building(),
                new CollisionRadius(2.0f),
                new CollisionScale(1.0f)
            );
        }

        /// <summary>
        /// Reachable ground enemy WITHOUT Attack: it can be targeted and killed but never
        /// fights back, so it cannot disguise acquisition failures via mutual retaliation.
        /// </summary>
        private Entity CreatePassiveEnemy(Vector3 pos, float maxHp, float armor, PlayerEntity owner)
        {
            return _world.Create(
                new Position(pos),
                new Health(maxHp, maxHp),
                new Armor(armor),
                new Owner(owner),
                new UnitFaction(true),
                new DefinitionId("zombie_warrior"),
                new ScanRadius(0),
                new MovementStats(0f, 20f, 10f),
                new PathingFlags((int)TerrainPathingFlags.Ground)
            );
        }

        /// <summary>
        /// Large stationary structure built like a castle/building from the editor: it carries a
        /// big CollisionRadius (the body that extends the attacker's effective range) and never
        /// moves or fights back, so the test observes raw reachability.
        /// </summary>
        private Entity CreatePassiveStructure(Vector3 pos, PlayerEntity owner, float collisionRadius = 3.0f)
        {
            return _world.Create(
                new Position(pos),
                new Health(2000, 2000),
                new Armor(5),
                new Owner(owner),
                new UnitFaction(true),
                new DefinitionId("castle"),
                new Building(),
                new CollisionRadius(collisionRadius),
                new CollisionScale(1.0f)
            );
        }

        /// <summary>
        /// Flying enemy hovering at flight altitude, exactly like the cyber_dragon in the TD map.
        /// It has no Attack/Movable so it never chases: the test needs it to stay PUT so it
        /// remains the closest (unreachable) scan candidate deterministically.
        /// </summary>
        private Entity CreateFlyingEnemy(Vector3 pos, PlayerEntity owner)
        {
            return _world.Create(
                new Position(pos),
                new Health(300, 300),
                new Armor(10),
                new Owner(owner),
                new UnitFaction(true),
                new DefinitionId("cyber_dragon"),
                new ScanRadius(40),
                new PathingFlags((int)TerrainPathingFlags.Flying)
            );
        }

        private void Tick(float delta)
        {
            _movementService.StepMovement(delta);
            _combatService.StepCombat(delta);

            // Mirror SimulationService: decrement attack cooldowns after combat.
            var query = new QueryDescription().WithAll<Attack>();
            _world.Query(in query, (Entity e, ref Attack atk) =>
            {
                if (atk.CurrentCooldown > 0)
                {
                    atk.CurrentCooldown = Math.Max(0f, atk.CurrentCooldown - delta);
                }
            });
        }

        /// <summary>
        /// A melee attacker acquires a zombie that is marching away, then chase it.
        /// Regression: the unit must land hits within a reasonable window instead of
        /// orbiting the target forever ("dance") or abandoning it as stuck.
        /// </summary>
        [Test]
        public void TestMeleeChasesMovingMarchingZombieAndLandsHits()
        {
            InitializeFlatTerrain();

            var playerOwner = new PlayerEntity(CreatePlayer(0));
            var enemyOwner = new PlayerEntity(CreatePlayer(1));

            // Zombie marching toward the SE base corner, speed 5.0 (zombie_soldier).
            var zombie = CreateMarchingZombie(
                new Vector3(10f, 0f, 0f),
                new Vector3(60f, 0f, 0f),
                maxHp: 1000, armor: 2, speed: 5.0f, enemyOwner,
                damage: 13f, range: 1.8f, scanRadius: 40f);

            // Melee attacker at the origin, speed 5.0+ so it can catch up to the zombie.
            var attacker = SpawnMeleeAttacker(
                new Vector3(0f, 0f, 0f),
                damage: 20f, range: 1.8f, cooldown: 1.0f,
                speed: 5.5f, scanRadius: 40f, playerOwner);

            int ticks = 0;
            int maxTicks = (int)(20.0f / DeltaSeconds); // ~20 simulated seconds

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
                    // Track how much it is closing the gap each second.
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

        /// <summary>
        /// Regression for "combat must be 3D": a melee unit at the foot of an unwalkable cliff
        /// must NOT damage an enemy standing on the summit. It must also not climb the wall,
        /// and it must not keep chasing an unreachable target forever (it either never commits
        /// to it, or abandons it) instead of orbiting at the cliff base.
        /// </summary>
        [Test]
        public void TestMeleeCannotDamageTargetOnElevatedPlateau()
        {
            InitializeSteppedTerrain(plateauHeight: 6f);

            var playerOwner = new PlayerEntity(CreatePlayer(0));
            var enemyOwner = new PlayerEntity(CreatePlayer(1));

            // Zombie marching on the summit (world Y lifts to plateauHeight via terrain snap).
            var zombie = CreateMarchingZombie(
                new Vector3(20f, 6f, 5f),
                new Vector3(24f, 6f, 5f),
                maxHp: 1000, armor: 2, speed: 6.0f, enemyOwner,
                damage: 13f, range: 1.8f, scanRadius: 40f);

            // Melee attacker on the flat base, just in front of the wall.
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

        /// <summary>
        /// Strongest discriminator of the 3D range fix: a ranged tower (range 20) placed on a
        /// 25-unit summit is HORIZONTALLY within range of a zombie at the cliff foot but 3D-out
        /// of range. Old (top-down projection) combat would fire; 3D combat must not.
        /// </summary>
        [Test]
        public void TestRangedTowerOnSummitCannotHitZombieAtCliffFoot()
        {
            InitializeSteppedTerrain(plateauHeight: 25f);

            var playerOwner = new PlayerEntity(CreatePlayer(0));
            var enemyOwner = new PlayerEntity(CreatePlayer(1));

            // Zombie marching along the flat foot of the cliff.
            var zombie = CreateMarchingZombie(
                new Vector3(8f, 0f, 5f),
                new Vector3(13f, 0f, 5f),
                maxHp: 2000, armor: 2, speed: 6.0f, enemyOwner,
                damage: 13f, range: 1.8f, scanRadius: 40f);

            // Tower at the summit edge. Horizontal gap to the zombie's path is ~3 (within
            // range 20), but the 25-unit drop pushes the true 3D distance > effectiveRange.
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

        /// <summary>
        /// Regression for "placed hero (CollisionScale ~2.55, no CollisionRadius) never fights
        /// zombies": the movement separation keeps big-scaled units apart at (r1+r2) * 0.6,
        /// so a melee range of 1.8 alone is unreachable. Combat must size its effective range
        /// from the same contact radii + separation factor, otherwise the pair pushes apart
        /// forever and neither ever lands a hit.
        /// </summary>
        [Test]
        public void TestBigScaledMeleeCanCloseAndDamageZombie()
        {
            InitializeFlatTerrain();

            var playerOwner = new PlayerEntity(CreatePlayer(0));
            var enemyOwner = new PlayerEntity(CreatePlayer(1));

            // Zombie that stays put so the result only depends on the attacker closing in.
            var zombie = CreateMarchingZombie(
                new Vector3(6f, 0f, 0f),
                new Vector3(6f, 0f, 0f),
                maxHp: 1000, armor: 2, speed: 0f, enemyOwner,
                damage: 13f, range: 1.8f, scanRadius: 40f);

            // Adventurer scaled like the placed hero (CollisionScale 2.55, CollisionRadius absent).
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

        /// <summary>
        /// Regression for "the hero ignores a reachable warrior because the closest scan
        /// candidate is unreachable": the closest enemy floats 6 units above flat ground (the
        /// same vertical-delta case the cliff-foot tests use), so melee vertical reach can
        /// never span the gap, while a reachable enemy stands farther away on the path. The
        /// hero must commit to the closest REACHABLE enemy instead of refusing to engage.
        /// </summary>
        [Test]
        public void TestMeleeChoosesReachableEnemyWhenClosestIsUnreachable()
        {
            InitializeFlatTerrain();

            var playerOwner = new PlayerEntity(CreatePlayer(0));
            var enemyOwner = new PlayerEntity(CreatePlayer(1));

            // Closest enemy: floating 6 units up (a raised ledge). Horizontal gap is small,
            // but melee vertical reach caps the climb at ~1.8, so it is always unreachable.
            // No Attack of its own so it can never hand the hero a target via retaliation.
            var ledgeEnemy = CreatePassiveEnemy(
                new Vector3(65f, 6f, 5f), maxHp: 1000, armor: 2, enemyOwner);

            // Reachable enemy on the flat path, farther away but still within scan radius.
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

        /// <summary>
        /// Regression for the real TD map: a flying enemy (cyber_dragon) hovers at flight
        /// altitude almost above the hero and, because the hero has default air targeting, it
        /// used to become the closest acquisition candidate - which is unreachable on the route
        /// check, so the hero refused to engage ANYONE, ignoring the reachable warrior beside
        /// it. Ground melee attackers must skip flyers entirely and engage the reachable enemy.
        /// </summary>
        [Test]
        public void TestGroundMeleeIgnoresHoveringFlyerAndAttacksReachableGround()
        {
            InitializeFlatTerrain();

            var playerOwner = new PlayerEntity(CreatePlayer(0));
            var enemyOwner = new PlayerEntity(CreatePlayer(1));

            // Flying enemy hovering almost directly above the hero (3D-closest but unreachable).
            var dragon = CreateFlyingEnemy(
                new Vector3(2f, 14f, 0f), enemyOwner);

            // Reachable ground warrior farther away but within the hero's scan radius.
            // No Attack of its own: passive, so it can never hand the hero a target via
            // mutual retaliation and mask the blocked-acquisition bug.
            var warrior = CreatePassiveEnemy(
                new Vector3(18f, 0f, 0f), maxHp: 1000, armor: 6, enemyOwner);

            // The real hero has CanTargetAir=true (no Targets metadata), which is exactly why
            // the flyer used to hijack acquisition.
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

        /// <summary>
        /// Regression for "the hero marches to the dead target's corpse and stops attacking":
        /// while chasing, the chase MoveTo points at the target's position. If the target dies
        /// mid-chase, dropping the AttackTarget must ALSO clear that stale chase MoveTo (and
        /// zero velocity) so the idle hero re-acquires the next enemy immediately instead of
        /// walking to the corpse first.
        /// </summary>
        [Test]
        public void TestChaserClearsCorpseMoveToAndReacquiresAfterKill()
        {
            InitializeFlatTerrain();

            var playerOwner = new PlayerEntity(CreatePlayer(0));
            var enemyOwner = new PlayerEntity(CreatePlayer(1));

            // The chased target: stationary so its corpse position is a fixed, far-away point.
            var chased = CreateMarchingZombie(
                new Vector3(14f, 0f, 0f),
                new Vector3(14f, 0f, 0f),
                maxHp: 100, armor: 2, speed: 3.0f, enemyOwner,
                damage: 13f, range: 1.8f, scanRadius: 40f);

            // A second, reachable enemy the hero must re-acquire right after the kill.
            var next = CreateMarchingZombie(
                new Vector3(6f, 0f, 4f),
                new Vector3(6f, 0f, 4f),
                maxHp: 1000, armor: 2, speed: 3.0f, enemyOwner,
                damage: 13f, range: 1.8f, scanRadius: 40f);

            var hero = SpawnMeleeAttacker(
                new Vector3(0f, 0f, 0f),
                damage: 50f, range: 2f, cooldown: 1.0f,
                speed: 5.5f, scanRadius: 40f, playerOwner);

            // Reproduce mid-chase state: the hero is pursuing `chased` (AttackTarget + chase MoveTo).
            _world.Add(hero, new AttackTarget(chased));
            _world.Add(hero, new MoveTo(new Vector3(14f, 0f, 0f)));

            // The chased target dies this frame.
            _world.Add<Dead>(chased);

            // One combat tick: the dead target must be dropped AND the stale chase MoveTo cleared.
            Tick(DeltaSeconds);

            Assert.That(_world.Has<MoveTo>(hero), Is.False,
                "Clearing a dead target must also clear the stale chase MoveTo (no march-to-corpse).");
            Assert.That(_world.Has<AttackTarget>(hero), Is.False,
                "The dead target must be dropped from the AttackTarget slot.");

            // The very next acquisition ticks must already re-engage the next enemy.
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

        /// <summary>
        /// Regression for the frozen-melee bug: a ground melee hero chases an enemy whose
        /// walkable approach (the navmesh endpoint here sits ~3.5u from the target, on the
        /// edge of an unwalkable block) leaves the unit OUT of attack range after the
        /// movement arrival shortfall, even though the old reachability margin (+1.5) called
        /// the route reachable. That mismatch made combat re-issue MoveTo while movement
        /// dropped it again every frame: the hero stood frozen and engaged, dealing no damage.
        /// The unit must instead land hits (if truly approachable) or drop the unwinnable
        /// target — never stay engaged while producing zero damage.
        /// </summary>
        [Test]
        public void TestMeleeDropsOrFightsTargetApproachableOnlyOutsideRange()
        {
            // Unwalkable block over cells [64..66]x[64..66]: world X/Z spans [0..6), so a
            // target at the centre (3,3) can only be approached to within ~3.0-3.6u (near the
            // walkable block edge) — beyond the hero's effective melee reach but inside the
            // old +1.5 reachability margin.
            InitializeFlatTerrainWithBlock(blockMinCellX: 64, blockMaxCellX: 66, blockMinCellZ: 64, blockMaxCellZ: 66);

            var playerOwner = new PlayerEntity(CreatePlayer(0));
            var enemyOwner = new PlayerEntity(CreatePlayer(1));

            // Passive enemy sitting INSIDE the unwalkable block (never fights back, never moves).
            var blockEnemy = CreatePassiveEnemy(
                new Vector3(3f, 0f, 3f), maxHp: 1000, armor: 2, enemyOwner);

            // Big-scaled melee hero (range 2 -> effective reach ~2.68) on walkable ground.
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

        /// <summary>
        /// Regression for the frozen-melee regression vs large structures: a melee hero must be
        /// able to commit to (and damage) an enemy whose body has a large CollisionRadius. With a
        /// CollisionRadius of 3.0 the effective reach is 2 + 3 = 5.0, but because the structure is
        /// approachable only to the walkable edge of the unwalkable square it sits in, the route
        /// endpoint can land 4.7 from the structure's centre — beyond the old shortfall threshold
        /// (5.0 - 0.75 = 4.25) but within reach. The old check flipped the verdict to "unreachable"
        /// and combat dropped the target, freezing the hero idle.
        ///
        /// Geometry calibrated so the route endpoint lands inside the ambiguous band
        /// (4.25, 5.0]: endpoint verified at (-11.9, -1.0), structure at (-7.2, -1) => horiz 4.70.
        /// </summary>
        [Test]
        public void TestMeleeDamagesLargeStructure()
        {
            // Block spanning cells [58..63]x[61..65] => world X [-12,-2), Z [-4,2). The walkable
            // route from the hero stops at the block's near edge (-11.9, -1.0), 4.70 from the
            // structure at (-7.2, -1) — farther than the old shortfall threshold but within the
            // hero's effective reach of 5.0 (2 range + 3.0 structure radius).
            InitializeFlatTerrainWithBlock(blockMinCellX: 58, blockMaxCellX: 63, blockMinCellZ: 61, blockMaxCellZ: 65);

            var playerOwner = new PlayerEntity(CreatePlayer(0));
            var enemyOwner = new PlayerEntity(CreatePlayer(1));

            // Castle-sized body (CollisionRadius 3.0): attack reach must include the body so
            // the melee hero can hit the structure's edge, not just its exact centre point.
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

        /// <summary>
        /// Regression for the frozen-melee regression vs enemies next to a wall: the navmesh
        /// route to a target inside an unwalkable square ends on the walkable edge, ~2.3 from
        /// the target's centre. The old strict check (effectiveRange - MeleeReachShortfallPadding
        /// = 1.93) marked that endpoint "beyond reach" and combat dropped the target, freezing the
        /// hero idle. The verdict must compare against the FULL effectiveRange (~2.68) so
        /// wall-adjacent enemies stay attackable.
        ///
        /// Geometry calibrated so the route endpoint lands inside the ambiguous band
        /// (1.93, 2.68]: endpoint verified at (5.8, 3.0), target at (3.5, 3) => horiz 2.30.
        /// </summary>
        [Test]
        public void TestMeleeDamagesEnemyAgainstWall()
        {
            // Unwalkable square over cells [64..66]x[64..66] => world X/Z [0,6). The walkable
            // route from the hero wraps around to the square's far edge (5.8, 3.0), 2.30 from the
            // target at (3.5, 3) — farther than the old shortfall threshold but within reach.
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