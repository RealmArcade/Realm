using System;
using System.Collections.Generic;
using Godot;
using Realm.Client;
using Realm.Client.Core;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Components.Tags;
using Vector3 = System.Numerics.Vector3;
using Realm.Shared.Abilities;

namespace Realm.Client.Services;

public static class AbilityEffectService
{
	private static readonly List<Entity> Targets = new(256);
	private static readonly List<(Entity Entity, float Distance)> SortedTargets = new(256);

	public static bool HasEffects(AbilityDefinition? def) => def != null && def.Effects.Count > 0;

	public static void Execute(Entity casterEntity, Vector3 casterPosition, AbilityDefinition def, Vector3 targetPosition)
	{
		EcsWorld world = GameContext.World;
		if (world == null || def.Effects.Count == 0) return;

		bool casterAlive = casterEntity != Entity.Null && world.IsAlive(casterEntity);

		for (int effectIndex = 0; effectIndex < def.Effects.Count; effectIndex++)
		{
			AbilityEffectDefinition effect = def.Effects[effectIndex];
			if (effect.Kind == AbilityEffectKind.Teleport)
			{
				if (casterAlive) ApplyTeleport(world, casterEntity, effect, casterPosition, targetPosition);
				continue;
			}

			if (effect.Kind == AbilityEffectKind.Summon)
			{
				if (casterAlive) ApplySummon(world, casterEntity, effect, targetPosition);
				continue;
			}

			CollectTargets(world, casterEntity, casterAlive, effect, casterPosition, targetPosition);

			float attributeValue = effect.ScalingAttribute.HasValue
				? ReadAttribute(world, casterEntity, effect.ScalingAttribute.Value)
				: 0f;
			float amount = effect.Scale(effect.Amount, attributeValue);
			float duration = effect.ScaleDuration(effect.Duration, attributeValue);

			for (int targetIndex = 0; targetIndex < Targets.Count; targetIndex++)
			{
				ApplyToTarget(world, casterEntity, casterAlive, def, effectIndex, effect, Targets[targetIndex], amount, duration);
			}
		}
	}

	private static void ApplyToTarget(EcsWorld world, Entity casterEntity, bool casterAlive, AbilityDefinition def, int effectIndex, AbilityEffectDefinition effect, Entity target, float amount, float duration)
	{
		if (!world.IsAlive(target) || world.Has<Dead>(target)) return;

		switch (effect.Kind)
		{
			case AbilityEffectKind.Damage:
				if (casterAlive && amount > 0f)
					GameContext.SimulationService.DealResolvedDamage(casterEntity, target, amount, effect.DamageType);
				break;

			case AbilityEffectKind.Heal:
				if (amount > 0f)
					GameContext.SimulationService.HealSingle(casterAlive ? casterEntity : Entity.Null, target, amount);
				break;

			case AbilityEffectKind.Shield:
				if (amount <= 0f) return;
				if (!world.Has<AbsorbShield>(target)) world.Add(target, new AbsorbShield());
				world.Get<AbsorbShield>(target).Add($"{def.Id}#{effectIndex}", amount, duration);
				break;

			case AbilityEffectKind.Buff:
			case AbilityEffectKind.Debuff:
				ApplyStatus(world, target, effect, duration);
				break;

			case AbilityEffectKind.Knockback:
				if (amount > 0f)
					ApplyKnockback(world, casterEntity, casterAlive, target, amount);
				break;
		}
	}

	private static void ApplyStatus(EcsWorld world, Entity target, AbilityEffectDefinition effect, float duration)
	{
		Unit_WasmRuntime wrapper = GameContext.SimulationService.GetUnitWrapper(target);

		if (effect.BuffId != null)
		{
			wrapper.AddBuff(effect.BuffId, duration);
		}

		if (effect.BuffModifierKey != null)
		{
			wrapper.AddBuff(effect.BuffModifierKey, duration);
		}
	}

	private static void ApplyTeleport(EcsWorld world, Entity casterEntity, AbilityEffectDefinition effect, Vector3 casterPosition, Vector3 targetPosition)
	{
		Vector3 destination = targetPosition;

		if (effect.Amount > 0f)
		{
			Vector3 delta = targetPosition - casterPosition;
			float distance = delta.Length();
			if (distance <= 0.0001f) return;

			destination = distance <= effect.Amount
				? targetPosition
				: casterPosition + (delta / distance * effect.Amount);
		}

		GameContext.SimulationService.GetUnitWrapper(casterEntity).Teleport(destination);
	}

	private static void ApplyKnockback(EcsWorld world, Entity casterEntity, bool casterAlive, Entity target, float distance)
	{
		if (!world.Has<Position>(target)) return;

		Vector3 targetPosition = world.Get<Position>(target).Value;
		Vector3 origin = casterAlive && world.Has<Position>(casterEntity) ? world.Get<Position>(casterEntity).Value : targetPosition;
		Vector3 delta = targetPosition - origin;
		float length = delta.Length();
		if (length <= 0.0001f) return;

		float resistance = ReadDerivedStat(world, target, StatKeys.KnockbackResistance);
		float pushed = distance * (1f - Math.Clamp(resistance, 0f, 1f));
		if (pushed <= 0.0001f) return;

		GameContext.SimulationService.GetUnitWrapper(target).Teleport(targetPosition + (delta / length * pushed));
	}

	private static void ApplySummon(EcsWorld world, Entity casterEntity, AbilityEffectDefinition effect, Vector3 targetPosition)
	{
		if (effect.SummonUnitId == null) return;

		int ownerPlayerIndex = world.Has<UnitOwnerPlayer>(casterEntity) ? world.Get<UnitOwnerPlayer>(casterEntity).PlayerIndex : 0;
		int templateStringId = StringRegistry.GetOrAdd(effect.SummonUnitId);

		int alive = CountAliveSummons(world, casterEntity, templateStringId);
		int spawnCount = effect.SummonCount;

		if (effect.SummonCap > 0)
		{
			spawnCount = Math.Min(spawnCount, effect.SummonCap - alive);
		}

		for (int index = 0; index < spawnCount; index++)
		{
			float angle = (MathF.Tau / MathF.Max(1, spawnCount)) * index;
			var offset = new Vector3(MathF.Cos(angle) * 1.2f, 0f, MathF.Sin(angle) * 1.2f);
			UnitId spawned = GameContext.Host?.SpawnAbilitySummon(effect.SummonUnitId, targetPosition + offset, ownerPlayerIndex) ?? UnitId.Invalid;
			if (!spawned.IsValid) continue;
			if (!GameContext.SimulationService.TryGetEntity(spawned, out Entity summonEntity)) continue;
			if (!world.IsAlive(summonEntity)) continue;

			world.SetOrAdd(summonEntity, new SummonedUnit(effect.Duration, casterEntity.Id, ownerPlayerIndex, templateStringId));
		}
	}

	private static int CountAliveSummons(EcsWorld world, Entity casterEntity, int templateStringId)
	{
		int count = 0;
		var query = QueryCache.AllPositionNoneDeadQuery;
		world.Query(in query, (Entity entity, ref Position _) =>
		{
			if (!world.Has<SummonedUnit>(entity)) return;

			var summon = world.Get<SummonedUnit>(entity);
			if (summon.SourceEntityId == casterEntity.Id && summon.TemplateStringId == templateStringId) count++;
		});

		return count;
	}

	private static void CollectTargets(EcsWorld world, Entity casterEntity, bool casterAlive, AbilityEffectDefinition effect, Vector3 casterPosition, Vector3 targetPosition)
	{
		Targets.Clear();

		int casterPlayer = casterAlive ? GetPlayerIndexForEntity(world, casterEntity) : 0;

		if (effect.Delivery == AbilityEffectDelivery.Self || effect.Targets == AbilityTargetSide.Self)
		{
			if (casterAlive) Targets.Add(casterEntity);
			return;
		}

		Vector3 origin = effect.Delivery is AbilityEffectDelivery.Cone or AbilityEffectDelivery.Line or AbilityEffectDelivery.Aura
			? casterPosition
			: targetPosition;
		float facing = world.Has<RotationY>(casterEntity) ? world.Get<RotationY>(casterEntity).Value : 0f;
		Vector3 facingDirection = new Vector3(MathF.Cos(facing), 0f, MathF.Sin(facing));

		var query = QueryCache.AllPositionAndHealthNoneDeadQuery;
		world.Query(in query, (Entity entity, ref Position position, ref Health health) =>
		{
			if (health.Current <= 0f) return;
			if (casterAlive && entity.Id == casterEntity.Id && effect.Targets != AbilityTargetSide.Allies) return;
			if (!MatchesSide(world, casterPlayer, casterAlive, entity, effect.Targets)) return;

			Vector3 targetPositionOfEntity = position.Value;
			float distance;

			switch (effect.Delivery)
			{
				case AbilityEffectDelivery.Single:
					distance = Vector3.Distance(targetPositionOfEntity, targetPosition);
					break;

				case AbilityEffectDelivery.Radius:
					distance = Vector3.Distance(targetPositionOfEntity, targetPosition);
					if (distance > effect.Radius) return;
					break;

				case AbilityEffectDelivery.Cone:
					distance = Vector3.Distance(targetPositionOfEntity, casterPosition);
					if (distance > effect.Length) return;
					if (distance > 0.0001f)
					{
						Vector3 toTarget = Vector3.Normalize(targetPositionOfEntity - casterPosition);
						float dot = Vector3.Dot(toTarget, facingDirection);
						float cosHalf = MathF.Cos(effect.AngleDegrees * 0.5f * (MathF.PI / 180f));
						if (dot < cosHalf) return;
					}

					break;

				case AbilityEffectDelivery.Line:
					distance = DistanceToSegment(targetPositionOfEntity, casterPosition, casterPosition + (facingDirection * effect.Length), out float segmentDistance);
					if (segmentDistance > MathF.Max(effect.Radius, 0.5f)) return;
					break;

				case AbilityEffectDelivery.Aura:
					distance = Vector3.Distance(targetPositionOfEntity, casterPosition);
					if (distance > effect.Radius) return;
					break;

				default:
					distance = Vector3.Distance(targetPositionOfEntity, targetPosition);
					break;
			}

			SortedTargets.Add((entity, distance));
		});

		if (effect.Delivery == AbilityEffectDelivery.Single)
		{
			Entity closest = Entity.Null;
			float closestDistance = float.MaxValue;
			foreach (var (entity, distance) in SortedTargets)
			{
				if (distance >= closestDistance) continue;
				closest = entity;
				closestDistance = distance;
			}

			SortedTargets.Clear();
			if (closest != Entity.Null) Targets.Add(closest);
			return;
		}

		if (effect.MaxTargets <= 0)
		{
			foreach (var (entity, _) in SortedTargets) Targets.Add(entity);
		}
		else
		{
			SortedTargets.Sort(static (left, right) => left.Distance.CompareTo(right.Distance));
			for (int index = 0; index < SortedTargets.Count && index < effect.MaxTargets; index++)
			{
				Targets.Add(SortedTargets[index].Entity);
			}
		}

		SortedTargets.Clear();
	}

	private static bool MatchesSide(EcsWorld world, int casterPlayer, bool casterAlive, Entity entity, AbilityTargetSide side)
	{
		switch (side)
		{
			case AbilityTargetSide.Self:
				return false;
			case AbilityTargetSide.Allies:
				if (!casterAlive) return false;
				return !NetworkService.ArePlayerIndicesEnemies(casterPlayer, GetPlayerIndexForEntity(world, entity));
			case AbilityTargetSide.Enemies:
				if (!casterAlive) return true;
				return NetworkService.ArePlayerIndicesEnemies(casterPlayer, GetPlayerIndexForEntity(world, entity));
			default:
				return true;
		}
	}

	private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end, out float segmentDistance)
	{
		Vector3 segment = end - start;
		float lengthSquared = segment.LengthSquared();
		float t = lengthSquared > 0.0001f ? Math.Clamp(Vector3.Dot(point - start, segment) / lengthSquared, 0f, 1f) : 0f;
		Vector3 closest = start + (segment * t);
		Vector3 delta = point - closest;
		segmentDistance = delta.Length();
		return Vector3.Distance(point, start);
	}

	private static int GetPlayerIndexForEntity(EcsWorld world, Entity entity)
	{
		if (entity == Entity.Null || !world.IsAlive(entity)) return 0;
		if (world.Has<UnitOwnerPlayer>(entity)) return world.Get<UnitOwnerPlayer>(entity).PlayerIndex;

		if (world.Has<Owner>(entity))
		{
			Entity ownerEntity = world.Get<Owner>(entity).PlayerEntity.Value;
			if (world.IsAlive(ownerEntity) && world.Has<UnitOwnerPlayer>(ownerEntity))
				return world.Get<UnitOwnerPlayer>(ownerEntity).PlayerIndex;
		}

		return 0;
	}

	private static float ReadAttribute(EcsWorld world, Entity entity, AttributeType attribute)
	{
		if (world.Has<Stats>(entity))
		{
			ref var stats = ref world.Get<Stats>(entity);
			if (stats.TryGetValue(new StatId(attribute), out float value)) return value;
		}

		if (world.Has<UnitAttributes>(entity))
		{
			return world.Get<UnitAttributes>(entity).Get(attribute);
		}

		return 0f;
	}

	private static float ReadDerivedStat(EcsWorld world, Entity entity, string statKey)
	{
		if (world.Has<Stats>(entity))
		{
			ref var stats = ref world.Get<Stats>(entity);
			if (stats.TryGetValue(new StatId(statKey), out float value)) return value;
		}

		return 0f;
	}
}
