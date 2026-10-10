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
using Realm.Client.Services;
using System.Numerics;

namespace Realm.Ecs.Tests
{
    [TestFixture]
    public class CombatTests
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

		private CombatAndDamageService _combatService = null!;

        [SetUp]
        public void Setup()
        {
            _combatService = new CombatAndDamageService(_worldAccessor);
        }

        [TearDown]
        public void TearDown()
        {
            ServiceLocator.DisposeAndRecreateServices();
        }

        private Entity CreatePlayer(int index)
        {
            return _world.Create(new DefinitionId("player_" + index));
        }

        private Entity SpawnTower(Vector3 pos, float damage, float range, float cooldown, float scanRadius, PlayerEntity owner)
        {
            return _world.Create(
                new Position(pos),
                new Attack(damage, range, cooldown),
                new Owner(owner),
                new ScanRadius(scanRadius),
                new UnitFaction(false),
                new Health(500, 500),
                new Armor(5),
                new DefinitionId("tower"),
                new Building()
            );
        }

        private Entity SpawnEnemy(Vector3 pos, float maxHp, float armor, PlayerEntity owner)
        {
            return _world.Create(
                new Position(pos),
                new Health(maxHp, maxHp),
                new Armor(armor),
                new Owner(owner),
                new UnitFaction(true),
                new DefinitionId("zombie_soldier")
            );
        }

        private Entity SpawnFlyingEnemy(Vector3 pos, float maxHp, float armor, PlayerEntity owner)
        {
            var enemy = SpawnEnemy(pos, maxHp, armor, owner);
            _world.Add(enemy, new PathingFlags((int)TerrainPathingFlags.Flying));
            return enemy;
        }

        private Entity SpawnMeleeUnit(Vector3 pos, float damage, float range, float cooldown, float scanRadius, PlayerEntity owner)
        {
            return _world.Create(
                new Position(pos),
                new Attack(damage, range, cooldown),
                new Owner(owner),
                new ScanRadius(scanRadius),
                new UnitFaction(false),
                new Health(500, 500),
                new Armor(5),
                new DefinitionId("soldier"),
                new CombatTargeting(false, true)
            );
        }

        private Entity SpawnFlyingAttacker(Vector3 pos, float damage, float range, float scanRadius, PlayerEntity owner)
        {
            var attacker = SpawnMeleeUnit(pos, damage, range, 1.5f, scanRadius, owner);
            _world.Add(attacker, new PathingFlags((int)TerrainPathingFlags.Flying));
            return attacker;
        }

        [Test]
        public void TestDamageIsAttackMinusArmor()
        {
            var tower = SpawnTower(new Vector3(0f, 0f, 0f), 20f, 20f, 1f, 50f, new PlayerEntity(CreatePlayer(0)));
            var enemy = SpawnEnemy(new Vector3(5f, 0f, 0f), 100f, 5f, new PlayerEntity(CreatePlayer(1)));

            _combatService.StepCombat(0.1f);

            Assert.That(_world.Get<Health>(enemy).Current, Is.EqualTo(85f));
        }

        [Test]
        public void TestDamageHasMinimumOfOne()
        {
            var tower = SpawnTower(new Vector3(0f, 0f, 0f), 20f, 20f, 1f, 50f, new PlayerEntity(CreatePlayer(0)));
            var enemy = SpawnEnemy(new Vector3(5f, 0f, 0f), 100f, 30f, new PlayerEntity(CreatePlayer(1)));

            _combatService.StepCombat(0.1f);

            Assert.That(_world.Get<Health>(enemy).Current, Is.EqualTo(99f));
        }

        [Test]
        public void TestZeroArmorTakesFullDamage()
        {
            var tower = SpawnTower(new Vector3(0f, 0f, 0f), 20f, 20f, 1f, 50f, new PlayerEntity(CreatePlayer(0)));
            var enemy = SpawnEnemy(new Vector3(5f, 0f, 0f), 100f, 0f, new PlayerEntity(CreatePlayer(1)));

            _combatService.StepCombat(0.1f);

            Assert.That(_world.Get<Health>(enemy).Current, Is.EqualTo(80f));
        }

        [Test]
        public void TestAttackCooldownGatesDamage()
        {
            var tower = SpawnTower(new Vector3(0f, 0f, 0f), 20f, 20f, 1f, 50f, new PlayerEntity(CreatePlayer(0)));
            var enemy = SpawnEnemy(new Vector3(5f, 0f, 0f), 100f, 5f, new PlayerEntity(CreatePlayer(1)));

            _combatService.StepCombat(0.1f);
            Assert.That(_world.Get<Health>(enemy).Current, Is.EqualTo(85f));
            Assert.That(_world.Get<Attack>(tower).CurrentCooldown, Is.EqualTo(1f));

            _combatService.StepCombat(0.1f);
            Assert.That(_world.Get<Health>(enemy).Current, Is.EqualTo(85f), "Unit should not attack while on cooldown.");

            _world.Set(tower, new Attack(20f, 20f, 1f, 0f));
            _combatService.StepCombat(0.1f);
            Assert.That(_world.Get<Health>(enemy).Current, Is.EqualTo(70f), "Unit should attack again once cooldown expires.");
        }

        [Test]
        public void TestScanRadiusLimitsTargetAcquisition()
        {
            var tower = SpawnTower(new Vector3(0f, 0f, 0f), 20f, 20f, 1f, 10f, new PlayerEntity(CreatePlayer(0)));
            var enemy = SpawnEnemy(new Vector3(15f, 0f, 0f), 100f, 5f, new PlayerEntity(CreatePlayer(1)));

            _combatService.StepCombat(0.1f);
            Assert.That(_world.Has<AttackTarget>(tower), Is.False, "Enemy beyond scan radius should not be acquired.");
            Assert.That(_world.Get<Health>(enemy).Current, Is.EqualTo(100f));

            _world.Set(enemy, new Position(new Vector3(5f, 0f, 0f)));
            _combatService.StepCombat(0.1f);
            Assert.That(_world.Has<AttackTarget>(tower), Is.True, "Enemy within scan radius should be acquired.");
        }

        [Test]
        public void TestInitialHealthAndDeath()
        {
            var tower = SpawnTower(new Vector3(0f, 0f, 0f), 60f, 20f, 1f, 50f, new PlayerEntity(CreatePlayer(0)));
            var enemy = SpawnEnemy(new Vector3(5f, 0f, 0f), 50f, 0f, new PlayerEntity(CreatePlayer(1)));

            Assert.That(_world.Get<Health>(enemy).Current, Is.EqualTo(50f));
            Assert.That(_world.Get<Health>(enemy).Max, Is.EqualTo(50f));

            _combatService.StepCombat(0.1f);

            Assert.That(_world.Has<Dead>(enemy), Is.True, "Unit should die when its health reaches zero.");
        }

        [Test]
        public void TestBuildingDropsTargetOutOfRange()
        {
            var tower = SpawnTower(new Vector3(0f, 0f, 0f), 20f, 10f, 1f, 50f, new PlayerEntity(CreatePlayer(0)));
            var enemy = SpawnEnemy(new Vector3(20f, 0f, 0f), 100f, 5f, new PlayerEntity(CreatePlayer(1)));

            _combatService.StepCombat(0.1f);

            Assert.That(_world.Has<AttackTarget>(tower), Is.False, "Building should drop targets outside its range instead of chasing.");
            Assert.That(_world.Get<Health>(enemy).Current, Is.EqualTo(100f));
        }

        [Test]
        public void TestBuildingTowerOnHillDoesNotHitEnemyOutOf3DRange()
        {
            var tower = SpawnTower(new Vector3(0f, 8f, 0f), 20f, 20f, 1f, 30f, new PlayerEntity(CreatePlayer(0)));
            var enemy = SpawnEnemy(new Vector3(20f, 0f, 0f), 100f, 5f, new PlayerEntity(CreatePlayer(1)));

            _combatService.StepCombat(0.1f);

            // Horizontal gap (20) is inside range (20), but the 8-unit vertical drop pushes the
            // true 3D distance (~21.5) beyond effective range -> the tower must NOT fire.
            Assert.That(_world.Has<AttackTarget>(tower), Is.False, "Building should drop a target whose 3D distance exceeds range.");
            Assert.That(_world.Get<Health>(enemy).Current, Is.EqualTo(100f), "Tower out of 3D range must not damage the enemy.");
        }

        [Test]
        public void TestBuildingScanUses3DNotHorizontalRadius()
        {
            var tower = SpawnTower(new Vector3(0f, 8f, 0f), 20f, 20f, 1f, 21f, new PlayerEntity(CreatePlayer(0)));
            var enemy = SpawnEnemy(new Vector3(20f, 0f, 0f), 100f, 5f, new PlayerEntity(CreatePlayer(1)));

            _combatService.StepCombat(0.1f);

            // Horizontal distance (20) is inside the scan radius (21), but the 8-unit height delta
            // pushes the 3D scan distance (~21.5) beyond it -> the enemy is NOT acquired.
            Assert.That(_world.Has<AttackTarget>(tower), Is.False, "Enemy beyond the 3D scan radius should not be acquired.");
            Assert.That(_world.Get<Health>(enemy).Current, Is.EqualTo(100f));
        }

        [Test]
        public void TestMeleeDoesNotAcquireFlyingTarget()
        {
            var melee = SpawnMeleeUnit(new Vector3(0f, 0f, 0f), 20f, 2f, 1f, 50f, new PlayerEntity(CreatePlayer(0)));
            var flyingEnemy = SpawnFlyingEnemy(new Vector3(1f, 0f, 0f), 100f, 5f, new PlayerEntity(CreatePlayer(1)));

            _combatService.StepCombat(0.1f);

            Assert.That(_world.Has<AttackTarget>(melee), Is.False, "Melee should not acquire a flying target.");
            Assert.That(_world.Get<Health>(flyingEnemy).Current, Is.EqualTo(100f));
        }

        [Test]
        public void TestMeleeDropsExistingFlyingTarget()
        {
            var melee = SpawnMeleeUnit(new Vector3(0f, 0f, 0f), 20f, 2f, 1f, 50f, new PlayerEntity(CreatePlayer(0)));
            var flyingEnemy = SpawnFlyingEnemy(new Vector3(1f, 0f, 0f), 100f, 5f, new PlayerEntity(CreatePlayer(1)));
            _world.Add(melee, new AttackTarget(flyingEnemy));

            _combatService.StepCombat(0.1f);

            Assert.That(_world.Has<AttackTarget>(melee), Is.False, "Melee should drop an existing flying target.");
            Assert.That(_world.Get<Health>(flyingEnemy).Current, Is.EqualTo(100f), "Flying target should not take damage from melee.");
        }

        [Test]
        public void TestRangedAttackerHitsFlyingTarget()
        {
            var tower = SpawnTower(new Vector3(0f, 0f, 0f), 20f, 20f, 1f, 50f, new PlayerEntity(CreatePlayer(0)));
            var flyingEnemy = SpawnFlyingEnemy(new Vector3(5f, 0f, 0f), 100f, 5f, new PlayerEntity(CreatePlayer(1)));

            _combatService.StepCombat(0.1f);

            Assert.That(_world.Has<AttackTarget>(tower), Is.True, "Ranged tower should acquire a flying target.");
            Assert.That(_world.Get<Health>(flyingEnemy).Current, Is.EqualTo(85f), "Ranged tower should damage a flying target.");
        }

        [Test]
        public void TestFlyingUnitAcquiresGroundTarget()
        {
            var flyingAttacker = SpawnFlyingAttacker(new Vector3(0f, 0f, 0f), 20f, 20f, 50f, new PlayerEntity(CreatePlayer(0)));
            var groundEnemy = SpawnEnemy(new Vector3(3f, 0f, 0f), 100f, 5f, new PlayerEntity(CreatePlayer(1)));

            _combatService.StepCombat(0.1f);

            Assert.That(_world.Has<AttackTarget>(flyingAttacker), Is.True, "Flying unit should acquire a ground target.");
            Assert.That(_world.Get<Health>(groundEnemy).Current, Is.EqualTo(85f));
        }

        [Test]
        public void TestMeleeSwarmOverflowingCandidateBufferDoesNotThrow()
        {
            // More enemies than the internal acquisition candidate buffer can hold so the
            // closest survivors keep triggering the sorted-insert while the buffer is full.
            // This used to index one past the array end and throw IndexOutOfRangeException,
            // aborting the whole combat tick and freezing every unit.
            var melee = SpawnMeleeUnit(new Vector3(0f, 0f, 0f), 20f, 2f, 1f, 50f, new PlayerEntity(CreatePlayer(0)));
            var owner = new PlayerEntity(CreatePlayer(1));

            var closestEnemy = SpawnEnemy(new Vector3(1.5f, 0f, 0f), 100f, 5f, owner);
            SpawnEnemy(new Vector3(2.5f, 0f, 0f), 100f, 5f, owner);
            SpawnEnemy(new Vector3(8f, 0f, 0f), 100f, 5f, owner);
            SpawnEnemy(new Vector3(9f, 0f, 0f), 100f, 5f, owner);
            SpawnEnemy(new Vector3(10f, 0f, 0f), 100f, 5f, owner);
            SpawnEnemy(new Vector3(11f, 0f, 0f), 100f, 5f, owner);

            Assert.DoesNotThrow(() =>
            {
                for (int tick = 0; tick < 5; tick++)
                {
                    _combatService.StepCombat(0.1f);
                }
            });

            Assert.That(_world.Get<Health>(closestEnemy).Current, Is.LessThan(100f), "Melee should acquire and damage the closest enemy despite the crowded scan.");
        }
    }
}
