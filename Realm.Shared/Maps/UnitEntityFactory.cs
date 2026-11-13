using System;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Abilities;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Metadata;

namespace Realm.Shared.Maps;

public static class UnitEntityFactory
{
	public static Entity CreateFromMetadata(
		EcsWorld world,
		DefinitionCatalog catalog,
		string templateId,
		Vector3 position,
		float rotationY,
		float scale,
		PlayerEntity owner,
		bool bypassPopulation,
		int level = 0)
	{
		UnitMetadata? metadata = catalog.FindUnitOrBuilding(templateId);
		if (metadata == null) return Entity.Null;

		return CreateFromMetadata(world, catalog, metadata, templateId, catalog.FindUnit(templateId) == null, position, rotationY, scale, owner, bypassPopulation, level);
	}

	private static Entity CreateFromMetadata(
		EcsWorld world,
		DefinitionCatalog catalog,
		UnitMetadata metadata,
		string templateId,
		bool isBuilding,
		Vector3 position,
		float rotationY,
		float scale,
		PlayerEntity owner,
		bool bypassPopulation,
		int level)
	{
		Entity entity = CreateCore(
			world,
			templateId,
			string.IsNullOrEmpty(metadata.Name) ? templateId : metadata.Name,
			metadata.MaxHp,
			metadata.Damage,
			metadata.Range,
			metadata.Armor,
			metadata.Speed,
			metadata.ScanRadius > 0f ? metadata.ScanRadius : 15.0f,
			metadata.IsHero,
			metadata.AttackCooldown > 0f ? metadata.AttackCooldown : 1.5f,
			GetPathingFlags(metadata),
			position,
			owner,
			metadata.Targets,
			metadata.HpRegen,
			metadata.HpRegenCombatDelay,
			metadata.MaxMana,
			metadata.ManaRegen,
			metadata.MagicResistance,
			metadata.FlatArmorPenetration,
			metadata.PercentArmorPenetration,
			metadata.DamageVariance,
			string.IsNullOrEmpty(metadata.DamageType) ? DamageTypes.Physical : metadata.DamageType,
			metadata.CritChance,
			metadata.CritMultiplier > 0f ? metadata.CritMultiplier : 1f,
			metadata.SplashType,
			metadata.SplashInnerRadius,
			metadata.SplashMediumRadius,
			metadata.SplashOuterRadius,
			metadata.SplashInnerRatio,
			metadata.SplashMediumRatio,
			metadata.SplashOuterRatio,
			metadata.FriendlyFire,
			metadata.PushPriority,
			!string.IsNullOrEmpty(metadata.MovementType) ? metadata.MovementType : "Ground",
			metadata.FlatMagicPenetration);

		if (entity == Entity.Null) return Entity.Null;

		if (metadata.Abilities is { Length: > 0 })
		{
			world.SetOrAdd(entity, new AbilityState(metadata.Abilities));
			uint autoCastSlots = BuildAutoCastSlots(catalog, metadata.Abilities);
			if (autoCastSlots != 0) world.SetOrAdd(entity, new AutoCastState(true, autoCastSlots));
		}

		if (metadata.Strength != 0f || metadata.Agility != 0f || metadata.Vitality != 0f || metadata.Intelligence != 0f || metadata.Wisdom != 0f || metadata.Fortune != 0f)
		{
			world.Set(entity, new UnitAttributes(metadata.Strength, metadata.Agility, metadata.Vitality, metadata.Intelligence, metadata.Wisdom, metadata.Fortune));
		}

		AttributeStatCalculator.RecalculateEntityStats(world, entity);
		ApplySpawnLevel(world, entity, metadata, level);

		float modelScale = scale <= 0.001f ? metadata.Scale : scale;
		float placedScale = scale <= 0.001f ? 1f : scale;
		world.SetOrAdd(entity, new RotationY(rotationY));
		world.SetOrAdd(entity, new ModelScale(modelScale));
		world.SetOrAdd(entity, new CollisionScale(placedScale));
		if (metadata.IncludeInDefaultHotkeySelection) world.SetOrAdd(entity, new IncludeInDefaultHotkeySelection());

		if (bypassPopulation) world.Add(entity, new BypassPopulationTag());
		if (isBuilding)
		{
			world.SetOrAdd(entity, new Building());
			if (BlocksMovement(metadata)) world.SetOrAdd(entity, new CollisionRadius(ResolveCollisionRadius(metadata)));
		}
		else
		{
			world.SetOrAdd(entity, new CollisionRadius(ResolveUnitCollisionRadius(metadata)));
		}

		return entity;
	}

	/// <summary>
	///     Gives a unit that already stands another template: it keeps its identity, its position, its owner and the share
	///     of its health it had, and everything its template states — name, health, resistances, attack, mana, abilities,
	///     model scale and whether it is a building — is written from the new template. This is how a site that has not
	///     said what it is yet becomes the building it was raised as, without a screen ever seeing it leave the board.
	/// </summary>
	/// <param name="world">The world the unit belongs to.</param>
	/// <param name="catalog">The templates the map defines.</param>
	/// <param name="entity">The unit to write the template onto.</param>
	/// <param name="templateId">The template the unit becomes.</param>
	/// <returns>True when the unit now stands as that template, or false when the template is not one the map defines.</returns>
	public static bool ApplyTemplate(EcsWorld world, DefinitionCatalog catalog, Entity entity, string templateId)
	{
		if (world == null || !world.IsAlive(entity) || string.IsNullOrEmpty(templateId)) return false;

		UnitMetadata? metadata = catalog.FindUnitOrBuilding(templateId);
		if (metadata == null) return false;

		bool isBuilding = catalog.FindUnit(templateId) == null;
		float healthShare = 1f;
		if (world.TryGet<Health>(entity, out Health previousHealth) && previousHealth.Max > 0f)
		{
			healthShare = Math.Clamp(previousHealth.Current / previousHealth.Max, 0f, 1f);
		}

		world.SetOrAdd(entity, new DefinitionId(templateId));
		world.SetOrAdd(entity, new Name(string.IsNullOrEmpty(metadata.Name) ? templateId : metadata.Name));

		ParseCombatTargeting(metadata.Targets, out bool canTargetAir, out bool canTargetGround);
		world.SetOrAdd(entity, new CombatTargeting(canTargetAir, canTargetGround));

		float scanRadius = metadata.ScanRadius > 0f ? metadata.ScanRadius : 15.0f;
		world.SetOrAdd(entity, new ScanRadius(scanRadius));
		world.SetOrAdd(entity, new TargetIntel(scanRadius, scanRadius));

		if (metadata.Damage > 0f)
		{
			world.SetOrAdd(entity, new Attack(
				damage: metadata.Damage,
				range: metadata.Range,
				cooldown: metadata.AttackCooldown > 0f ? metadata.AttackCooldown : 1.5f,
				currentCooldown: 0f,
				damageVariance: metadata.DamageVariance,
				damageType: string.IsNullOrEmpty(metadata.DamageType) ? DamageTypes.Physical : metadata.DamageType,
				flatArmorPenetration: metadata.FlatArmorPenetration,
				percentArmorPenetration: metadata.PercentArmorPenetration,
				critChance: metadata.CritChance,
				critMultiplier: metadata.CritMultiplier > 0f ? metadata.CritMultiplier : 1f,
				splashType: ParseSplashType(metadata.SplashType),
				splashInnerRadius: metadata.SplashInnerRadius,
				splashMediumRadius: metadata.SplashMediumRadius,
				splashOuterRadius: metadata.SplashOuterRadius,
				splashInnerRatio: metadata.SplashInnerRatio,
				splashMediumRatio: metadata.SplashMediumRatio,
				splashOuterRatio: metadata.SplashOuterRatio,
				friendlyFire: metadata.FriendlyFire));
		}
		else if (world.Has<Attack>(entity))
		{
			world.Remove<Attack>(entity);
		}

		world.SetOrAdd(entity, new UnitAttributes(
			metadata.Strength, metadata.Agility, metadata.Vitality,
			metadata.Intelligence, metadata.Wisdom, metadata.Fortune));

		world.SetOrAdd(entity, new UnitBaseStats(
			BaseMaxHp: metadata.MaxHp,
			BaseHpRegen: metadata.HpRegen,
			BaseMaxMana: metadata.MaxMana,
			BaseManaRegen: metadata.ManaRegen,
			BaseArmor: metadata.Armor,
			BaseMagicResistance: metadata.MagicResistance,
			BaseDamage: metadata.Damage,
			BaseAttackInterval: metadata.AttackCooldown > 0f ? metadata.AttackCooldown : 1.5f,
			BaseSpeed: metadata.Speed,
			BaseCastPoint: 0.3f,
			BaseCritChance: metadata.CritChance,
			BaseCritMultiplier: metadata.CritMultiplier > 0f ? metadata.CritMultiplier : 1f,
			BaseFlatArmorPenetration: metadata.FlatArmorPenetration,
			BasePercentArmorPenetration: metadata.PercentArmorPenetration,
			BaseFlatMagicPenetration: metadata.FlatMagicPenetration));

		if (metadata.MaxMana > 0f)
		{
			world.SetOrAdd(entity, new Mana(metadata.MaxMana, metadata.MaxMana, metadata.ManaRegen));
			world.SetOrAdd(entity, new ManaRegen(metadata.ManaRegen));
		}
		else
		{
			if (world.Has<Mana>(entity)) world.Remove<Mana>(entity);
			if (world.Has<ManaRegen>(entity)) world.Remove<ManaRegen>(entity);
		}

		if (metadata.Abilities is { Length: > 0 })
		{
			world.SetOrAdd(entity, new AbilityState(metadata.Abilities));
			uint autoCastSlots = BuildAutoCastSlots(catalog, metadata.Abilities);
			if (autoCastSlots != 0) world.SetOrAdd(entity, new AutoCastState(true, autoCastSlots));
			else if (world.Has<AutoCastState>(entity)) world.Remove<AutoCastState>(entity);
		}
		else
		{
			world.SetOrAdd(entity, new AbilityState(null));
			if (world.Has<AutoCastState>(entity)) world.Remove<AutoCastState>(entity);
		}

		world.SetOrAdd(entity, new ModelScale(metadata.Scale));
		world.SetOrAdd(entity, new CollisionScale(1f));

		if (metadata.IncludeInDefaultHotkeySelection) world.SetOrAdd(entity, new IncludeInDefaultHotkeySelection());
		else if (world.Has<IncludeInDefaultHotkeySelection>(entity)) world.Remove<IncludeInDefaultHotkeySelection>(entity);

		if (isBuilding)
		{
			world.SetOrAdd(entity, new Building());
			if (BlocksMovement(metadata)) world.SetOrAdd(entity, new CollisionRadius(ResolveCollisionRadius(metadata)));
			else if (world.Has<CollisionRadius>(entity)) world.Remove<CollisionRadius>(entity);

			if (world.Has<MovementStats>(entity)) world.Remove<MovementStats>(entity);
			if (world.Has<Movable>(entity)) world.Remove<Movable>(entity);
			if (world.Has<TraversableTerrain>(entity)) world.Remove<TraversableTerrain>(entity);
			if (world.Has<Inventory>(entity)) world.Remove<Inventory>(entity);
		}
		else
		{
			if (world.Has<Building>(entity)) world.Remove<Building>(entity);

			world.SetOrAdd(entity, new CollisionRadius(ResolveUnitCollisionRadius(metadata)));
			world.SetOrAdd(entity, new MovementStats(
				metadata.Speed, 0f, 10f, metadata.PushPriority,
				!string.IsNullOrEmpty(metadata.MovementType) ? metadata.MovementType : "Ground"));
			world.SetOrAdd(entity, new TraversableTerrain(GetPathingFlags(metadata)));
			world.SetOrAdd(entity, new Movable());
			world.SetOrAdd(entity, new Inventory());
		}

		AttributeStatCalculator.RecalculateEntityStats(world, entity);

		if (world.TryGet<Health>(entity, out Health health) && health.Max > 0f)
		{
			health.Current = health.Max * healthShare;
			world.Set(entity, health);
		}

		return true;
	}

	/// <summary>
	///     Gives a unit the level it is placed at and the share of strength, vitality and the other attributes that level
	///     earns. A placement states its own level, so two instances of one template can start at different levels.
	/// </summary>
	/// <param name="world">The world the unit belongs to.</param>
	/// <param name="entity">The unit to level.</param>
	/// <param name="metadata">The template the unit stands as, which states the growth a level earns.</param>
	/// <param name="level">The level the unit is placed at, or zero to stand at the level its template states.</param>
	public static void ApplySpawnLevel(EcsWorld world, Entity entity, UnitMetadata metadata, int level = 0)
	{
		bool statedLevel = level > 0;
		LevelGrowth growth = metadata.LevelGrowth?.ToLevelGrowth() ?? LevelGrowth.None;
		int spawnLevel = Math.Max(1, statedLevel ? level : metadata.Level);

		// A placement that states a level keeps the statement even when it earns no growth from it, so the level it was
		// placed at is still on the unit when the map is saved. Only a level that changes the unit's stats is applied.
		if (!statedLevel && spawnLevel <= 1 && growth.IsZero) return;

		world.SetOrAdd(entity, new Level(spawnLevel));
		if (spawnLevel > 1 || !growth.IsZero) LevelService.Apply(world, entity, spawnLevel, growth);
	}

	private static uint BuildAutoCastSlots(DefinitionCatalog catalog, string[] abilities)
	{
		uint slots = 0;
		int limit = Math.Min(abilities.Length, 32);
		for (int index = 0; index < limit; index++)
		{
			AbilityMetadata? ability = catalog.FindAbility(abilities[index]);
			if (ability?.AutoCast is not { Enabled: true }) continue;

			slots |= 1u << index;
		}

		return slots;
	}

	public static bool BlocksMovement(UnitMetadata metadata)
	{
		return !string.Equals(metadata.VisualMode, GroundPlaneVisualMode, StringComparison.OrdinalIgnoreCase);
	}

	private const string GroundPlaneVisualMode = "GroundPlane";

	public static float ResolveCollisionRadius(UnitMetadata metadata)
	{
		return GameplayConstants.ResolveContactRadius(metadata.CollisionCircle, 1f, GameplayConstants.DefaultBuildingCollisionRadius);
	}

	public static float ResolveUnitCollisionRadius(UnitMetadata metadata)
	{
		return GameplayConstants.ResolveContactRadius(metadata.CollisionCircle, 1f, GameplayConstants.DefaultCollisionRadius);
	}

	public static Entity CreateCore(
		EcsWorld world,
		string id,
		string name,
		float hp,
		float damage,
		float range,
		float armor,
		float speed,
		float scanRadius,
		bool isHero,
		float attackCooldown,
		int pathingFlags,
		Vector3 position,
		PlayerEntity owner,
		string[]? targets = null,
		float hpRegen = 0f,
		float hpRegenCombatDelay = 0f,
		float maxMana = 0f,
		float manaRegen = 0f,
		float magicResistance = 0f,
		float flatArmorPen = 0f,
		float percentArmorPen = 0f,
		float damageVariance = 0f,
		string damageType = "physical",
		float critChance = 0f,
		float critMultiplier = 1f,
		string splashTypeStr = "None",
		float splashInnerRadius = 0f,
		float splashMediumRadius = 0f,
		float splashOuterRadius = 0f,
		float splashInnerRatio = 1f,
		float splashMediumRatio = 0.5f,
		float splashOuterRatio = 0.25f,
		bool friendlyFire = false,
		int pushPriority = 0,
		string movementType = "Ground",
		float flatMagicPen = 0f)
	{
		Entity entity = world.Create();
		world.Add(entity, new DefinitionId(id));
		world.Add(entity, new Name(name));
		world.Add(entity, new Position(position));
		world.Add(entity, new PreviousPosition(position));
		world.Add(entity, new Owner(owner));

		ParseCombatTargeting(targets, out bool canTargetAir, out bool canTargetGround);
		world.Add(entity, new CombatTargeting(canTargetAir, canTargetGround));

		if (isHero)
		{
			world.Add(entity, new Hero());
			world.Add(entity, new Level(1));
			world.Add(entity, new Experience(0f));
		}

		world.Add(entity, new Health(hp, hp, hpRegen, hpRegenCombatDelay));

		if (maxMana > 0f)
		{
			world.Add(entity, new Mana(maxMana, maxMana, manaRegen));
			world.Add(entity, new ManaRegen(manaRegen));
		}

		if (damage > 0)
		{
			world.Add(entity, new Attack(
				damage: damage,
				range: range,
				cooldown: attackCooldown,
				currentCooldown: 0f,
				damageVariance: damageVariance,
				damageType: string.IsNullOrEmpty(damageType) ? "normal" : damageType,
				flatArmorPenetration: flatArmorPen,
				percentArmorPenetration: percentArmorPen,
				critChance: critChance,
				critMultiplier: critMultiplier,
				splashType: ParseSplashType(splashTypeStr),
				splashInnerRadius: splashInnerRadius,
				splashMediumRadius: splashMediumRadius,
				splashOuterRadius: splashOuterRadius,
				splashInnerRatio: splashInnerRatio,
				splashMediumRatio: splashMediumRatio,
				splashOuterRatio: splashOuterRatio,
				friendlyFire: friendlyFire
			));
		}

		world.Add(entity, new Resistances(armor, magicResistance));
		world.Add(entity, new CollisionScale(1.0f));
		world.Add(entity, new ScanRadius(scanRadius));
		world.Add(entity, new TargetIntel(scanRadius, scanRadius));

		var baseStats = new Dictionary<StatId, float>
		{
			{ new StatId("Armor"), armor },
			{ new StatId("MagicResistance"), magicResistance },
			{ new StatId("Attack"), damage },
			{ new StatId("MovementSpeed"), speed }
		};
		world.Add(entity, new Stats(baseStats));

		if (speed > 0)
		{
			world.Add(entity, new MovementStats(speed, 0f, 10f, pushPriority, movementType));
			world.Add(entity, new TraversableTerrain(pathingFlags));
			world.Add(entity, new Movable());
			world.Add(entity, new Inventory());
		}
		else
		{
			world.Add(entity, new Building());
		}

		world.Add(entity, new UnitAttributes());
		world.Add(entity, new UnitBaseStats(
			BaseMaxHp: hp,
			BaseHpRegen: hpRegen,
			BaseMaxMana: maxMana,
			BaseManaRegen: manaRegen,
			BaseArmor: armor,
			BaseMagicResistance: magicResistance,
			BaseDamage: damage,
			BaseAttackInterval: attackCooldown,
			BaseSpeed: speed,
			BaseCastPoint: 0.3f,
			BaseCritChance: critChance,
			BaseCritMultiplier: critMultiplier,
			BaseFlatArmorPenetration: flatArmorPen,
			BasePercentArmorPenetration: percentArmorPen,
			BaseFlatMagicPenetration: flatMagicPen
		));

		return entity;
	}

	public static int GetPathingFlags(UnitMetadata metadata)
	{
		if (metadata.PathingType != 0) return metadata.PathingType;
		if (metadata.PathingCapabilities == null || metadata.PathingCapabilities.Length == 0) return (int)TerrainPathingFlags.Ground;

		int flags = 0;
		for (int index = 0; index < metadata.PathingCapabilities.Length; index++)
		{
			flags |= GetPathingFlagForCapability(metadata.PathingCapabilities[index]);
		}

		return flags != 0 ? flags : (int)TerrainPathingFlags.Ground;
	}

	private static int GetPathingFlagForCapability(string capability)
	{
		return capability.ToLowerInvariant() switch
		{
			"shallow_water" => (int)TerrainPathingFlags.ShallowWater,
			"deep_water" => (int)TerrainPathingFlags.DeepWater,
			"flying" or "air" => (int)TerrainPathingFlags.Flying,
			"ground" => (int)TerrainPathingFlags.Ground,
			"buildable" => (int)TerrainPathingFlags.Buildable,
			_ => 0,
		};
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
		for (int index = 0; index < targets.Length; index++)
		{
			string normalized = targets[index].Trim().ToLowerInvariant();
			if (normalized == "air") hasAir = true;
			else if (normalized == "ground") hasGround = true;
		}

		canTargetAir = hasAir;
		canTargetGround = hasGround;
	}
}
