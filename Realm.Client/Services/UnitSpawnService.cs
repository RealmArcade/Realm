using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
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
	public UnitSpawnService()
	{
	}

	public string GetFallbackModelPath(string modelPathOrId, bool isBuilding)
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

		string resolvedPath = ModelCache.ResolveModelPath(modelPathOrId);
		if (!string.IsNullOrEmpty(resolvedPath) && (resolvedPath.StartsWith("res://") || File.Exists(resolvedPath)))
		{
			return resolvedPath;
		}

		return modelPathOrId;
	}

	private static int GetFlagForPathingCapability(string cap)
	{
		return cap.ToLower() switch
		{
			"shallow_water" => 1,
			"deep_water" => 2,
			"flying" or "air" => 4,
			"ground" => 8,
			"buildable" => 32,
			_ => 0,
		};
	}

	public int GetUnitPathingFlags(UnitMetadata meta)
	{
		if (meta.PathingType != 0) return meta.PathingType;
		if (meta.PathingCapabilities == null || meta.PathingCapabilities.Length == 0) return 8;

		int flags = 0;
		foreach (var cap in meta.PathingCapabilities)
		{
			flags |= GetFlagForPathingCapability(cap);
		}

		return flags != 0 ? flags : 8;
	}

	public string GetEnemyUnitName(string unitTypeId, string defaultName)
	{
		if (GameHost.TryGetUnitOrBuildingMetadata(unitTypeId, out var meta) && !string.IsNullOrEmpty(meta.Name))
		{
			return meta.Name;
		}

		return defaultName;
	}

	private static SplashType ParseSplashType(string? splashType)
	{
		if (string.IsNullOrEmpty(splashType)) return SplashType.None;
		if (splashType.Equals("RadialStep", StringComparison.OrdinalIgnoreCase) || splashType.Equals("Radial_Step", StringComparison.OrdinalIgnoreCase)) return SplashType.RadialStep;
		if (splashType.Equals("RadialLinear", StringComparison.OrdinalIgnoreCase) || splashType.Equals("Radial_Linear", StringComparison.OrdinalIgnoreCase)) return SplashType.RadialLinear;
		return SplashType.None;
	}

	private static void ParseCombatTargeting(string[]? targets, out bool canTargetAir, out bool canTargetGround)
	{
		canTargetAir = true;
		canTargetGround = true;

		if (targets == null || targets.Length == 0) return;

		bool hasAir = false;
		bool hasGround = false;
		foreach (var targetType in targets)
		{
			string normalized = targetType.Trim().ToLowerInvariant();
			if (normalized == "air") hasAir = true;
			else if (normalized == "ground") hasGround = true;
		}

		canTargetAir = hasAir;
		canTargetGround = hasGround;
	}


	public void ProcessPropSpawnRequests(Action<string, System.Numerics.Vector3, float, float> spawnAction)
	{
		var propSpawnQuery = QueryCache.AllPropSpawnRequestQuery;
		var propRequests = new List<Entity>();
		GameContext.World.Query(in propSpawnQuery, (Entity entity) => propRequests.Add(entity));
		foreach (var reqEnt in propRequests)
		{
			ref var req = ref GameContext.World.Get<PropSpawnRequest>(reqEnt);
			spawnAction(req.PropId, req.Position, req.RotationY, req.Scale);
			GameContext.World.Destroy(reqEnt);
		}
	}

	public void ProcessDecalSpawnRequests(Action<string, System.Numerics.Vector3, System.Numerics.Vector3, float> spawnAction)
	{
		var decalSpawnQuery = QueryCache.AllDecalSpawnRequestQuery;
		var decalRequests = new List<Entity>();
		GameContext.World.Query(in decalSpawnQuery, (Entity entity) => decalRequests.Add(entity));
		foreach (var reqEnt in decalRequests)
		{
			ref var req = ref GameContext.World.Get<DecalSpawnRequest>(reqEnt);
			spawnAction(req.DecalId, req.Position, new System.Numerics.Vector3(req.RotationX, req.RotationY, req.RotationZ), req.Scale);
			GameContext.World.Destroy(reqEnt);
		}
	}

	public int ResolveSpawnParentPlayer(Entity buildingEntity, int localPlayerIndex, ref bool actualIsEnemy)
	{
		int parentPlayer = actualIsEnemy ? 1 : 0;
		if (GameContext.World.IsAlive(buildingEntity) && GameContext.World.Has<UnitOwnerPlayer>(buildingEntity))
		{
			parentPlayer = GameContext.World.Get<UnitOwnerPlayer>(buildingEntity).PlayerIndex;
			actualIsEnemy = NetworkService.ArePlayerIndicesEnemies(localPlayerIndex, parentPlayer);
		}

		return parentPlayer;
	}

	
	public Entity SpawnUnitDomain(string unitTypeId, string name, float maxHp, float damage, float range, float armor, float speed, System.Numerics.Vector3 position, PlayerEntity playerOwner, bool bypassPopulation, Entity playerEntity, bool hasShieldsUpgrade, bool hasWeaponsUpgrade)
	{
		Realm.Shared.Metadata.UnitMetadata regMeta = null;
		if (!GameContext.RegistryService.UnitRegistry.TryGetValue(unitTypeId, out regMeta)) GameContext.RegistryService.BuildingRegistry.TryGetValue(unitTypeId, out regMeta);

		var pos = new Godot.Vector3(position.X, position.Y, position.Z);
		Entity entity;

		if (regMeta != null)
		{
			entity = CreateEcsUnitEntity(
				unitTypeId, name, maxHp, damage, range, armor, speed,
				regMeta.ScanRadius > 0 ? regMeta.ScanRadius : 15.0f, regMeta.IsHero,
				regMeta.AttackCooldown > 0 ? regMeta.AttackCooldown : 1.5f, GetUnitPathingFlags(regMeta), pos, playerOwner,
				playerEntity, hasShieldsUpgrade, hasWeaponsUpgrade, regMeta.Targets,
				regMeta.HpRegen, regMeta.HpRegenCombatDelay, regMeta.MaxMana, regMeta.ManaRegen,
				regMeta.RatedArmor, !string.IsNullOrEmpty(regMeta.ArmorType) ? regMeta.ArmorType : "unarmored",
				regMeta.FlatArmorPenetration, regMeta.PercentArmorPenetration, regMeta.DamageVariance,
				!string.IsNullOrEmpty(regMeta.DamageType) ? regMeta.DamageType : (!string.IsNullOrEmpty(regMeta.AttackType) ? regMeta.AttackType : "normal"),
				regMeta.CritChance, regMeta.CritMultiplier > 0f ? regMeta.CritMultiplier : 1f,
				regMeta.SplashType, regMeta.SplashInnerRadius, regMeta.SplashMediumRadius, regMeta.SplashOuterRadius,
				regMeta.SplashInnerRatio, regMeta.SplashMediumRatio, regMeta.SplashOuterRatio, regMeta.FriendlyFire,
				regMeta.PushPriority, !string.IsNullOrEmpty(regMeta.MovementType) ? regMeta.MovementType : "Ground"
			);
			
			if (regMeta.Strength != 0f || regMeta.Agility != 0f || regMeta.Vitality != 0f || regMeta.Intelligence != 0f || regMeta.Wisdom != 0f || regMeta.Fortune != 0f)
			{
				GameContext.World.Set(entity, new UnitAttributes(regMeta.Strength, regMeta.Agility, regMeta.Vitality, regMeta.Intelligence, regMeta.Wisdom, regMeta.Fortune));
				AttributeStatCalculator.RecalculateEntityStats(GameContext.World, entity);
			}
		}
		else
		{
			entity = CreateEcsUnitEntity(unitTypeId, name, maxHp, damage, range, armor, speed, 15.0f, false, 1.5f, 8, pos, playerOwner, playerEntity, hasShieldsUpgrade, hasWeaponsUpgrade, null, 0f, 0f, 0f, 0f, 0f, "unarmored", 0f, 0f, 0f, "normal", 0f, 1f, "None", 0f, 0f, 0f, 1f, 0.5f, 0.25f, false, 0, "Ground");
		}

		if (bypassPopulation) GameContext.World.Add(entity, new BypassPopulationTag());
		return entity;
	}



	public Entity CreateEntityFromSnapshot(
		System.Numerics.Vector3 pos,
		System.Numerics.Vector3 vel,
		float rotationY,
		string unitId,
		string name,
		float maxHp,
		float currentHp,
		float damage,
		float range,
		float attackCooldown,
		float armor,
		float speed,
		PlayerEntity owner,
		bool isEnemy)
	{
		var entity = GameContext.World.Create();
		GameContext.World.Add(entity, new DefinitionId(unitId));
		GameContext.World.Add(entity, new Name(name));
		GameContext.World.Add(entity, new Position(pos));
		GameContext.World.Add(entity, new Owner(owner));
		GameContext.World.Add(entity, new Health(currentHp, maxHp));

		if (damage > 0 || unitId == "priest")
		{
			GameContext.World.Add(entity, new Attack(damage, range, attackCooldown));
		}

		GameContext.World.Add(entity, new Armor(armor));

		if (speed > 0)
		{
			GameContext.World.Add(entity, new MovementStats(speed, 20f, 10f));
			GameContext.World.Add(entity, new Movable());
			GameContext.World.Add(entity, new Inventory());
		}
		else
		{
			GameContext.World.Add(entity, new Building());
		}

		var target = new InterpolationTarget
		{
			Position = pos,
			Velocity = vel,
			RotationY = rotationY
		};
		GameContext.World.Add(entity, target);

		GameContext.World.Add(entity, new UnitFaction(isEnemy));

		return entity;
	}

	public Entity CreateEcsUnitEntity(
		string id, string name, float hp, float damage, float range, float armor, float speed, float scanRadius, bool isHero, float attackCooldown, int pathingFlags, Vector3 pos, PlayerEntity owner, Entity playerEntity, bool hasShieldsUpgrade, bool hasWeaponsUpgrade, string[]? targets = null,
		float hpRegen = 0f, float hpRegenCombatDelay = 0f, float maxMana = 0f, float manaRegen = 0f,
		float ratedArmor = 0f, string armorType = "unarmored", float flatArmorPen = 0f, float percentArmorPen = 0f,
		float damageVariance = 0f, string damageType = "normal", float critChance = 0f, float critMultiplier = 1f,
		string splashTypeStr = "None", float splashInnerRadius = 0f, float splashMediumRadius = 0f, float splashOuterRadius = 0f,
		float splashInnerRatio = 1f, float splashMediumRatio = 0.5f, float splashOuterRatio = 0.25f, bool friendlyFire = false,
		int pushPriority = 0, string movementType = "Ground")
	{
		var entity = GameContext.World.Create();
		GameContext.World.Add(entity, new DefinitionId(id));
		GameContext.World.Add(entity, new Name(name));
		GameContext.World.Add(entity, new Position(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z)));
		GameContext.World.Add(entity, new Owner(owner));

		ParseCombatTargeting(targets, out bool canTargetAir, out bool canTargetGround);
		GameContext.World.Add(entity, new CombatTargeting(canTargetAir, canTargetGround));

		if (isHero)
		{
			GameContext.World.Add(entity, new Hero());
			GameContext.World.Add(entity, new Level(1));
			GameContext.World.Add(entity, new Experience(0f));
		}

		GameContext.World.Add(entity, new Health(hp, hp, hpRegen, hpRegenCombatDelay));

		if (maxMana > 0f)
		{
			GameContext.World.Add(entity, new Mana(maxMana, maxMana, manaRegen));
			GameContext.World.Add(entity, new ManaRegen(manaRegen));
		}

		SplashType splashType = ParseSplashType(splashTypeStr);

		if (damage > 0)
		{
			GameContext.World.Add(entity, new Attack(
				Damage: damage,
				Range: range,
				Cooldown: attackCooldown,
				CurrentCooldown: 0f,
				DamageVariance: damageVariance,
				DamageType: string.IsNullOrEmpty(damageType) ? "normal" : damageType,
				FlatArmorPenetration: flatArmorPen,
				PercentArmorPenetration: percentArmorPen,
				CritChance: critChance,
				CritMultiplier: critMultiplier,
				SplashType: splashType,
				SplashInnerRadius: splashInnerRadius,
				SplashMediumRadius: splashMediumRadius,
				SplashOuterRadius: splashOuterRadius,
				SplashInnerRatio: splashInnerRatio,
				SplashMediumRatio: splashMediumRatio,
				SplashOuterRatio: splashOuterRatio,
				FriendlyFire: friendlyFire
			));
		}

		GameContext.World.Add(entity, new Armor(armor, ratedArmor, string.IsNullOrEmpty(armorType) ? "unarmored" : armorType));
		GameContext.World.Add(entity, new CollisionScale(1.0f));
		GameContext.World.Add(entity, new ScanRadius(scanRadius));
		GameContext.World.Add(entity, new TargetIntel(scanRadius, scanRadius));

		var baseStatsDict = new Dictionary<StatId, float>
		{
			{ new StatId("Armor"), armor },
			{ new StatId("Attack"), damage },
			{ new StatId("MovementSpeed"), speed }
		};
		GameContext.World.Add(entity, new Stats(baseStatsDict));

		if (speed > 0)
		{
			GameContext.World.Add(entity, new MovementStats(speed, 0f, 10f, pushPriority, movementType));
			GameContext.World.Add(entity, new PathingFlags(pathingFlags));
			GameContext.World.Add(entity, new Movable());
			GameContext.World.Add(entity, new Inventory());
		}
		else
		{
			GameContext.World.Add(entity, new Building());
		}

		GameContext.World.Add(entity, new UnitAttributes());
		GameContext.World.Add(entity, new UnitBaseStats(
			BaseMaxHp: hp,
			BaseHpRegen: hpRegen,
			BaseMaxMana: maxMana,
			BaseManaRegen: manaRegen,
			BaseArmor: armor,
			BaseDamage: damage,
			BaseAttackInterval: attackCooldown,
			BaseSpeed: speed,
			BaseCastPoint: 0.3f,
			BaseCritChance: critChance,
			BaseCritMultiplier: critMultiplier,
			BaseFlatArmorPenetration: flatArmorPen
		));
		AttributeStatCalculator.RecalculateEntityStats(GameContext.World, entity);

		return entity;
	}
	public void ProcessUnitSpawnRequestsDomain(Action<string, System.Numerics.Vector3, bool, float, float, int> spawnAction)
	{
		var unitSpawnQuery = QueryCache.AllUnitSpawnRequestQuery;
		var unitRequests = new List<Entity>();
		GameContext.World.Query(in unitSpawnQuery, (Entity entity) => unitRequests.Add(entity));
		foreach (var reqEnt in unitRequests)
		{
			ref var req = ref GameContext.World.Get<UnitSpawnRequest>(reqEnt);
			spawnAction(req.UnitId, req.Position, req.IsEnemy, req.RotationY, req.Scale, req.Player);
			GameContext.World.Destroy(reqEnt);
		}
	}
	public Arch.Core.Entity InstantiateEntityAndMapSnapshot(
		ref Realm.Ecs.Components.Core.NetworkMappingState mapping, 
		int entityId,
		System.Numerics.Vector3 position,
		System.Numerics.Vector3 velocity,
		float rotationY,
		string unitId,
		int ownerPlayerEntityId,
		float currentHp,
		float maxHp,
		bool isBuilding,
		Arch.Core.Entity ownerPlayerEntity, 
		bool isEnemy, 
		out string fallbackModelPath)
	{
		if (Realm.Client.Core.GameHost.TryGetUnitOrBuildingMetadata(unitId, out var meta))
		{
			var entity = CreateEntityFromSnapshot(
				position,
				velocity,
				rotationY,
				unitId,
				meta.Name,
				maxHp,
				currentHp,
				meta.Damage,
				meta.Range,
				meta.AttackCooldown,
				meta.Armor,
				meta.Speed,
				ownerPlayerEntity.AsPlayerEntity(GameContext.World),
				isEnemy
			);

			mapping.ServerToClientEntityMap[entityId] = entity;
			mapping.ClientToServerEntityMap[entity.Id] = entityId;

			string targetModel = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : unitId;
			fallbackModelPath = GetFallbackModelPath(targetModel, isBuilding);
			return entity;
		}

		fallbackModelPath = string.Empty;
		return Arch.Core.Entity.Null;
	}

}