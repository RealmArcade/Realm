using System;
using System.Collections.Generic;
using System.IO;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using FileAccess = Godot.FileAccess;

namespace Realm.Client.Services;

public class UnitSpawnService
{
	public string GetFallbackModelPath(string? modelPathOrId, bool isBuilding)
	{
		if (string.IsNullOrWhiteSpace(modelPathOrId)) return "";

		if (modelPathOrId.StartsWith("res://") && FileAccess.FileExists(modelPathOrId))
		{
			return modelPathOrId;
		}

		if (Path.IsPathRooted(modelPathOrId) && File.Exists(modelPathOrId))
		{
			return modelPathOrId;
		}

		string? resolvedPath = ModelCache.ResolveModelPath(modelPathOrId);
		if (!string.IsNullOrEmpty(resolvedPath) && (resolvedPath.StartsWith("res://") || File.Exists(resolvedPath)))
		{
			return resolvedPath;
		}

		return modelPathOrId;
	}

	public int GetUnitPathingFlags(UnitMetadata meta) => UnitEntityFactory.GetPathingFlags(meta);

	public string GetEnemyUnitName(string unitTypeId, string defaultName)
	{
		if (GameHost.TryGetUnitOrBuildingMetadata(unitTypeId, out var meta) && !string.IsNullOrEmpty(meta.Name))
		{
			return meta.Name;
		}

		return defaultName;
	}


	public void ProcessPropSpawnRequests(Action<string, System.Numerics.Vector3, float, float, int> spawnAction)
	{
		var world = GameContext.World;
		if (world == null) return;

		var propSpawnQuery = QueryCache.AllPropSpawnRequestQuery;
		var propRequests = new List<Entity>();
		world.Query(in propSpawnQuery, (Entity entity) => propRequests.Add(entity));
		foreach (var reqEnt in propRequests)
		{
			ref var req = ref world.Get<PropSpawnRequest>(reqEnt);
			spawnAction(req.PropId, req.Position, req.RotationY, req.Scale, req.Level);
			world.Destroy(reqEnt);
		}
	}

	public void ProcessDecalSpawnRequests(Action<string, System.Numerics.Vector3, System.Numerics.Vector3, float> spawnAction)
	{
		var world = GameContext.World;
		if (world == null) return;

		var decalSpawnQuery = QueryCache.AllDecalSpawnRequestQuery;
		var decalRequests = new List<Entity>();
		world.Query(in decalSpawnQuery, (Entity entity) => decalRequests.Add(entity));
		foreach (var reqEnt in decalRequests)
		{
			ref var req = ref world.Get<DecalSpawnRequest>(reqEnt);
			spawnAction(req.DecalId, req.Position, new System.Numerics.Vector3(req.RotationX, req.RotationY, req.RotationZ), req.Scale);
			world.Destroy(reqEnt);
		}
	}

	public int ResolveSpawnParentPlayer(Entity buildingEntity)
	{
		var world = GameContext.World;
		if (world != null && world.IsAlive(buildingEntity) && world.Has<UnitOwnerPlayer>(buildingEntity))
		{
			return world.Get<UnitOwnerPlayer>(buildingEntity).PlayerIndex;
		}

		return 0;
	}

	
	public Entity SpawnUnitDomain(string unitTypeId, string name, float maxHp, float damage, float range, float armor, float speed, System.Numerics.Vector3 position, PlayerEntity playerOwner, bool bypassPopulation, Entity playerEntity, bool hasShieldsUpgrade, bool hasWeaponsUpgrade)
	{
		var world = GameContext.World;
		if (world == null) return Entity.Null;

		GameContext.RegistryService.Placeables.TryGetUnitOrBuilding(unitTypeId, out Realm.Shared.Metadata.UnitMetadata? regMeta);

		var pos = new Godot.Vector3(position.X, position.Y, position.Z);
		Entity entity;

		if (regMeta != null)
		{
			entity = CreateEcsUnitEntity(
				unitTypeId, name, maxHp, damage, range, armor, speed,
				regMeta.ScanRadius > 0 ? regMeta.ScanRadius : 15.0f, regMeta.IsHero,
				regMeta.AttackCooldown > 0 ? regMeta.AttackCooldown : 1.5f, GetUnitPathingFlags(regMeta), pos, playerOwner,
				playerEntity, hasShieldsUpgrade, hasWeaponsUpgrade,				regMeta.Targets,
				regMeta.HpRegen, regMeta.HpRegenCombatDelay, regMeta.MaxMana, regMeta.ManaRegen,
				regMeta.MagicResistance,
				regMeta.FlatArmorPenetration, regMeta.PercentArmorPenetration, regMeta.DamageVariance,
				string.IsNullOrEmpty(regMeta.DamageType) ? DamageTypes.Physical : regMeta.DamageType,
				regMeta.CritChance, regMeta.CritMultiplier > 0f ? regMeta.CritMultiplier : 1f,
				regMeta.SplashType, regMeta.SplashInnerRadius, regMeta.SplashMediumRadius, regMeta.SplashOuterRadius,
				regMeta.SplashInnerRatio, regMeta.SplashMediumRatio, regMeta.SplashOuterRatio, regMeta.FriendlyFire,
				regMeta.PushPriority, !string.IsNullOrEmpty(regMeta.MovementType) ? regMeta.MovementType : "Ground",
				regMeta.FlatMagicPenetration
			);
			
			if (regMeta.Abilities != null && regMeta.Abilities.Length > 0)
			{
				world.SetOrAdd(entity, new AbilityState(regMeta.Abilities));
			}

			if (regMeta.StartingItems != null && regMeta.StartingItems.Length > 0)
			{
				foreach (var stItem in regMeta.StartingItems)
				{
					if (string.IsNullOrWhiteSpace(stItem)) continue;
					Realm.Client.Utils.ItemGrantingUtility.EquipItem(world, entity, stItem);
				}
			}

			if (regMeta.Strength != 0f || regMeta.Agility != 0f || regMeta.Vitality != 0f || regMeta.Intelligence != 0f || regMeta.Wisdom != 0f || regMeta.Fortune != 0f)
			{
				world.Set(entity, new UnitAttributes(regMeta.Strength, regMeta.Agility, regMeta.Vitality, regMeta.Intelligence, regMeta.Wisdom, regMeta.Fortune));
			}

			AttributeStatCalculator.RecalculateEntityStats(world, entity);

			world.SetOrAdd(entity, new CollisionRadius(UnitEntityFactory.ResolveUnitCollisionRadius(regMeta)));
		}
		else
		{
			entity = CreateEcsUnitEntity(unitTypeId, name, maxHp, damage, range, armor, speed, 15.0f, false, 1.5f, 8, pos, playerOwner, playerEntity, hasShieldsUpgrade, hasWeaponsUpgrade, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, DamageTypes.Physical, 0f, 1f, "None", 0f, 0f, 0f, 1f, 0.5f, 0.25f, false, 0, "Ground");
		}

		if (bypassPopulation) world.Add(entity, new BypassPopulationTag());
		return entity;
	}



	public Entity CreateEcsUnitEntity(
		string id, string name, float hp, float damage, float range, float armor, float speed, float scanRadius, bool isHero, float attackCooldown, int pathingFlags, Vector3 pos, PlayerEntity owner, Entity playerEntity, bool hasShieldsUpgrade, bool hasWeaponsUpgrade, string[]? targets = null,
		float hpRegen = 0f, float hpRegenCombatDelay = 0f, float maxMana = 0f, float manaRegen = 0f,
		float magicResistance = 0f, float flatArmorPen = 0f, float percentArmorPen = 0f,
		float damageVariance = 0f, string damageType = "physical", float critChance = 0f, float critMultiplier = 1f,
		string splashTypeStr = "None", float splashInnerRadius = 0f, float splashMediumRadius = 0f, float splashOuterRadius = 0f,
		float splashInnerRatio = 1f, float splashMediumRatio = 0.5f, float splashOuterRatio = 0.25f, bool friendlyFire = false,
		int pushPriority = 0, string movementType = "Ground", float flatMagicPen = 0f)
	{
		var world = GameContext.World;
		if (world == null) return Entity.Null;

		Entity entity = UnitEntityFactory.CreateCore(
			world, id, name, hp, damage, range, armor, speed, scanRadius, isHero, attackCooldown, pathingFlags,
			new System.Numerics.Vector3(pos.X, pos.Y, pos.Z), owner, targets, hpRegen, hpRegenCombatDelay, maxMana, manaRegen,
			magicResistance, flatArmorPen, percentArmorPen, damageVariance, damageType, critChance, critMultiplier,
			splashTypeStr, splashInnerRadius, splashMediumRadius, splashOuterRadius, splashInnerRatio, splashMediumRatio,
			splashOuterRatio, friendlyFire, pushPriority, movementType, flatMagicPen);

		AttributeStatCalculator.RecalculateEntityStats(world, entity);
		return entity;
	}


	public void ProcessUnitSpawnRequestsDomain(Action<string, System.Numerics.Vector3, float, float, int, int> spawnAction)
	{
		var world = GameContext.World;
		if (world == null) return;

		var unitSpawnQuery = QueryCache.AllUnitSpawnRequestQuery;
		var unitRequests = new List<Entity>();
		world.Query(in unitSpawnQuery, (Entity entity) => unitRequests.Add(entity));
		foreach (var reqEnt in unitRequests)
		{
			ref var req = ref world.Get<UnitSpawnRequest>(reqEnt);
			spawnAction(req.UnitId, req.Position, req.RotationY, req.Scale, req.Player, req.Level);
			world.Destroy(reqEnt);
		}
	}
}