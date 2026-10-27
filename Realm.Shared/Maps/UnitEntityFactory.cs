using System;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
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

/// <summary>
///     Builds a unit or building entity from authored statistics. The authoritative process and the client both call
///     this, so a unit that the simulation moves and a unit that a client renders always carry the same components
///     with the same numbers.
/// </summary>
public static class UnitEntityFactory
{
	/// <summary>
	///     Creates the entity a map declares, reading every statistic and cost from the catalog. Returns
	///     <see cref="Entity.Null" /> when the map declares no such template.
	/// </summary>
	public static Entity CreateFromMetadata(
		EcsWorld world,
		DefinitionCatalog catalog,
		string templateId,
		Vector3 position,
		float rotationY,
		float scale,
		PlayerEntity owner,
		bool bypassPopulation)
	{
		UnitMetadata? metadata = catalog.FindUnitOrBuilding(templateId);
		if (metadata == null) return Entity.Null;

		return CreateFromMetadata(world, metadata, templateId, catalog.FindUnit(templateId) == null, position, rotationY, scale, owner, bypassPopulation);
	}

	/// <summary>
	///     Creates the entity for an already resolved template, which is how a caller that indexes its templates some
	///     other way still builds the same entity.
	/// </summary>
	public static Entity CreateFromMetadata(
		EcsWorld world,
		UnitMetadata metadata,
		string templateId,
		bool isBuilding,
		Vector3 position,
		float rotationY,
		float scale,
		PlayerEntity owner,
		bool bypassPopulation)
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
			!string.IsNullOrEmpty(metadata.MovementType) ? metadata.MovementType : "Ground");

		if (entity == Entity.Null) return Entity.Null;

		if (metadata.Abilities is { Length: > 0 })
		{
			world.SetOrAdd(entity, new AbilityState(metadata.Abilities));
		}

		if (metadata.Strength != 0f || metadata.Agility != 0f || metadata.Vitality != 0f || metadata.Intelligence != 0f || metadata.Wisdom != 0f || metadata.Fortune != 0f)
		{
			world.Set(entity, new UnitAttributes(metadata.Strength, metadata.Agility, metadata.Vitality, metadata.Intelligence, metadata.Wisdom, metadata.Fortune));
		}

		AttributeStatCalculator.RecalculateEntityStats(world, entity);

		float placedScale = scale <= 0.001f ? metadata.Scale : scale;
		world.SetOrAdd(entity, new RotationY(rotationY));
		world.SetOrAdd(entity, new ModelScale(placedScale));
		world.SetOrAdd(entity, new CollisionScale(placedScale));

		if (bypassPopulation) world.Add(entity, new BypassPopulationTag());
		if (isBuilding)
		{
			world.SetOrAdd(entity, new Building());
			if (BlocksMovement(metadata)) world.SetOrAdd(entity, new CollisionRadius(ResolveCollisionRadius(metadata)));
		}

		return entity;
	}

	/// <summary>
	///     True when a building stands on the ground and therefore blocks the units that walk around it. A template
	///     drawn as a flat ground-plane marker is a decoration the map lays on the floor, so nothing paths around it.
	///     Every process asks this, so a building blocks the same ground wherever it is simulated.
	/// </summary>
	public static bool BlocksMovement(UnitMetadata metadata)
	{
		return !string.Equals(metadata.VisualMode, GroundPlaneVisualMode, StringComparison.OrdinalIgnoreCase);
	}

	private const string GroundPlaneVisualMode = "GroundPlane";

	/// <summary>
	///     The radius a building blocks movement and routing with: the collision circle its template authors, or the
	///     shared default building radius when it authors none. Every consumer scales it by the building's placed
	///     collision scale, so a building placed at twice the size blocks twice as much ground.
	/// </summary>
	public static float ResolveCollisionRadius(UnitMetadata metadata)
	{
		return metadata.CollisionCircle > GameplayConstants.MinimumAuthoredCollisionRadius
			? metadata.CollisionCircle
			: GameplayConstants.DefaultBuildingCollisionRadius;
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
		string movementType = "Ground")
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
			world.Add(entity, new PathingFlags(pathingFlags));
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
			BaseDamage: damage,
			BaseAttackInterval: attackCooldown,
			BaseSpeed: speed,
			BaseCastPoint: 0.3f,
			BaseCritChance: critChance,
			BaseCritMultiplier: critMultiplier,
			BaseFlatArmorPenetration: flatArmorPen
		));

		return entity;
	}

	/// <summary>The terrain bits a unit may stand on, derived from what its template declares it can traverse.</summary>
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
