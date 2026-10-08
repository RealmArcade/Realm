using Arch.Core;
using Godot;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using System;

internal class UnitSpawnService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World EcsWorld => _ecsWorldAccessor.Current;

	public UnitSpawnService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	public string GetFallbackModelPath(string modelPathOrId, bool isBuilding)
	{
		if (string.IsNullOrWhiteSpace(modelPathOrId)) return "";

		if (modelPathOrId.StartsWith("res://") && Godot.FileAccess.FileExists(modelPathOrId))
		{
			return modelPathOrId;
		}

		if (System.IO.Path.IsPathRooted(modelPathOrId) && System.IO.File.Exists(modelPathOrId))
		{
			return modelPathOrId;
		}

		string resolvedPath = Realm.Godot.Utils.ModelCache.ResolveModelPath(modelPathOrId);
		if (!string.IsNullOrEmpty(resolvedPath) && (resolvedPath.StartsWith("res://") || System.IO.File.Exists(resolvedPath)))
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

	public Entity CreateEcsUnitEntity(
		string id, string name, float hp, float damage, float range, float armor, float speed, float scanRadius, bool isHero, float attackCooldown, int pathingFlags, Vector3 pos, Realm.Ecs.Common.PlayerEntity owner, Entity playerEntity, bool hasShieldsUpgrade, bool hasWeaponsUpgrade, string[]? targets = null,
		float hpRegen = 0f, float hpRegenCombatDelay = 0f, float maxMana = 0f, float manaRegen = 0f,
		float ratedArmor = 0f, string armorType = "unarmored", float flatArmorPen = 0f, float percentArmorPen = 0f,
		float damageVariance = 0f, string damageType = "normal", float critChance = 0f, float critMultiplier = 1f,
		string splashTypeStr = "None", float splashInnerRadius = 0f, float splashMediumRadius = 0f, float splashOuterRadius = 0f,
		float splashInnerRatio = 1f, float splashMediumRatio = 0.5f, float splashOuterRatio = 0.25f, bool friendlyFire = false,
		int pushPriority = 0, string movementType = "Ground")
	{
		var entity = EcsWorld.Create();
		EcsWorld.Add(entity, new DefinitionId(id));
		EcsWorld.Add(entity, new Name(name));
		EcsWorld.Add(entity, new Position(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z)));
		EcsWorld.Add(entity, new Owner(owner));

		ParseCombatTargeting(targets, out bool canTargetAir, out bool canTargetGround);
		EcsWorld.Add(entity, new CombatTargeting(canTargetAir, canTargetGround));

		if (isHero)
		{
			EcsWorld.Add(entity, new Realm.Ecs.Components.Tags.Hero());
			EcsWorld.Add(entity, new Realm.Ecs.Components.Meta.Level(1));
			EcsWorld.Add(entity, new Realm.Ecs.Components.Meta.Experience(0f));
		}

		EcsWorld.Add(entity, new Health(hp, hp, hpRegen, hpRegenCombatDelay));

		if (maxMana > 0f)
		{
			EcsWorld.Add(entity, new Mana(maxMana, maxMana, manaRegen));
			EcsWorld.Add(entity, new ManaRegen(manaRegen));
		}

		SplashType splashType = ParseSplashType(splashTypeStr);

		if (damage > 0)
		{
			EcsWorld.Add(entity, new Attack(
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

		EcsWorld.Add(entity, new Armor(armor, ratedArmor, string.IsNullOrEmpty(armorType) ? "unarmored" : armorType));
		EcsWorld.Add(entity, new CollisionScale(1.0f));
		EcsWorld.Add(entity, new ScanRadius(scanRadius));
		EcsWorld.Add(entity, new TargetIntel(scanRadius, scanRadius));

		var baseStatsDict = new System.Collections.Generic.Dictionary<Realm.Ecs.Common.StatId, float>
		{
			{ new Realm.Ecs.Common.StatId("Armor"), armor },
			{ new Realm.Ecs.Common.StatId("Attack"), damage },
			{ new Realm.Ecs.Common.StatId("MovementSpeed"), speed }
		};
		EcsWorld.Add(entity, new Realm.Ecs.Components.Stats.Stats(baseStatsDict));

		if (speed > 0)
		{
			EcsWorld.Add(entity, new MovementStats(speed, 0f, 10f, pushPriority, movementType));
			EcsWorld.Add(entity, new PathingFlags(pathingFlags));
			EcsWorld.Add(entity, new Realm.Ecs.Components.Tags.Movable());
			EcsWorld.Add(entity, new Inventory());
		}
		else
		{
			EcsWorld.Add(entity, new Building());
		}

		EcsWorld.Add(entity, new Realm.Ecs.Components.Stats.UnitAttributes());
		EcsWorld.Add(entity, new Realm.Ecs.Components.Stats.UnitBaseStats(
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
		AttributeStatCalculator.RecalculateEntityStats(EcsWorld, entity);

		return entity;
	}
}
