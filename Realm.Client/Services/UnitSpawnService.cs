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

	public int ResolveSpawnParentPlayer(Entity buildingEntity)
	{
		if (GameContext.World.IsAlive(buildingEntity) && GameContext.World.Has<UnitOwnerPlayer>(buildingEntity))
		{
			return GameContext.World.Get<UnitOwnerPlayer>(buildingEntity).PlayerIndex;
		}

		return 0;
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
			
			if (regMeta.Abilities != null && regMeta.Abilities.Length > 0)
			{
				GameContext.World.SetOrAdd(entity, new AbilityState(new List<string>(regMeta.Abilities)));
			}

			if (regMeta.StartingItems != null && regMeta.StartingItems.Length > 0)
			{
				foreach (var stItem in regMeta.StartingItems)
				{
					if (string.IsNullOrWhiteSpace(stItem)) continue;
					Realm.Client.Utils.ItemGrantingUtility.EquipItem(GameContext.World, entity, stItem);
				}
			}

			if (regMeta.Strength != 0f || regMeta.Agility != 0f || regMeta.Vitality != 0f || regMeta.Intelligence != 0f || regMeta.Wisdom != 0f || regMeta.Fortune != 0f)
			{
				GameContext.World.Set(entity, new UnitAttributes(regMeta.Strength, regMeta.Agility, regMeta.Vitality, regMeta.Intelligence, regMeta.Wisdom, regMeta.Fortune));
			}

			AttributeStatCalculator.RecalculateEntityStats(GameContext.World, entity);
		}
		else
		{
			entity = CreateEcsUnitEntity(unitTypeId, name, maxHp, damage, range, armor, speed, 15.0f, false, 1.5f, 8, pos, playerOwner, playerEntity, hasShieldsUpgrade, hasWeaponsUpgrade, null, 0f, 0f, 0f, 0f, 0f, "unarmored", 0f, 0f, 0f, "normal", 0f, 1f, "None", 0f, 0f, 0f, 1f, 0.5f, 0.25f, false, 0, "Ground");
		}

		if (bypassPopulation) GameContext.World.Add(entity, new BypassPopulationTag());
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

		ApplyPlayerUpgradesToNewUnit(entity, owner, playerEntity);

		AttributeStatCalculator.RecalculateEntityStats(GameContext.World, entity);

		return entity;
	}

	private void ApplyPlayerUpgradesToNewUnit(Entity entity, PlayerEntity owner, Entity playerEntity)
	{
		if (GameContext.TechTreeService == null) return;

		int playerIndex = -1;
		if (playerEntity != Entity.Null && GameContext.World.Has<UnitOwnerPlayer>(playerEntity))
		{
			playerIndex = GameContext.World.Get<UnitOwnerPlayer>(playerEntity).PlayerIndex;
		}
		else if (owner.Value != Entity.Null && GameContext.World.Has<UnitOwnerPlayer>(owner.Value))
		{
			playerIndex = GameContext.World.Get<UnitOwnerPlayer>(owner.Value).PlayerIndex;
		}

		if (playerIndex < 0) return;

		string unitDefId = GameContext.World.Has<DefinitionId>(entity) ? GameContext.World.Get<DefinitionId>(entity).Value : "";

		foreach (var kvp in GameContext.TechTreeService.PlayerTechLevels)
		{
			if (kvp.Key.PlayerIndex != playerIndex || kvp.Value <= 0) continue;

			string techId = kvp.Key.TechId;
			int level = kvp.Value;

			if (GameContext.RegistryService?.UpgradeRegistry?.TryGetValue(techId, out var upgradeDef) == true && upgradeDef != null)
			{
				if (upgradeDef.AffectedUnitIds != null && upgradeDef.AffectedUnitIds.Length > 0)
				{
					bool affected = false;
					for (int i = 0; i < upgradeDef.AffectedUnitIds.Length; i++)
					{
						if (string.Equals(upgradeDef.AffectedUnitIds[i], unitDefId, StringComparison.OrdinalIgnoreCase))
						{
							affected = true;
							break;
						}
					}
					if (!affected) continue;
				}

				TechTreeService.ApplyUpgradeModifiersToEntity(entity, techId, level, upgradeDef);
			}
		}
	}
	public void ProcessUnitSpawnRequestsDomain(Action<string, System.Numerics.Vector3, float, float, int> spawnAction)
	{
		var unitSpawnQuery = QueryCache.AllUnitSpawnRequestQuery;
		var unitRequests = new List<Entity>();
		GameContext.World.Query(in unitSpawnQuery, (Entity entity) => unitRequests.Add(entity));
		foreach (var reqEnt in unitRequests)
		{
			ref var req = ref GameContext.World.Get<UnitSpawnRequest>(reqEnt);
			spawnAction(req.UnitId, req.Position, req.RotationY, req.Scale, req.Player);
			GameContext.World.Destroy(reqEnt);
		}
	}
}