using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using Realm.MapAPI;
using Realm.Shared.Abilities;

namespace Realm.ListenServer;

/// <summary>
///     Runs an ability's effects on the authority. One executor, one target query, every kind: damage goes through the
///     combat resolver, heals through the healing pipeline, buffs through the buff pipeline, and shields and summons
///     through their own components. A cast is simulated here and nowhere else, so what a spell did is the state every
///     client is told about rather than something each client works out for itself.
/// </summary>
public class AbilityEffectRunner
{
	// Reused between casts: a cast resolves at most a few hundred targets and never runs per tick,
	// so one buffer keeps the path allocation-free without pooling machinery.
	private readonly List<Entity> _targets = new(256);
	private readonly List<(Entity Entity, float Distance)> _sortedTargets = new(256);

	private readonly SimulationContext _context;
	private readonly Func<int, int, bool> _areAllied;
	private readonly Func<string, Vector3, int, UnitId> _summon;
	private int _castingPlayerIndex;
	private Entity _castingEntity;

	public AbilityEffectRunner(SimulationContext context, Func<int, int, bool> areAllied, Func<string, Vector3, int, UnitId> summon)
	{
		_context = context;
		_areAllied = areAllied;
		_summon = summon;
	}

	/// <summary>Whether an ability does anything by itself, or is a scripted one the map answers for.</summary>
	public static bool HasEffects(AbilityDefinition? definition) => definition != null && definition.Effects.Count > 0;

	/// <summary>Applies every effect of the ability in authored order.</summary>
	public void Execute(Entity casterEntity, Vector3 casterPosition, AbilityDefinition definition, Vector3 targetPosition)
	{
		if (definition.Effects.Count == 0) return;

		_castingEntity = casterEntity;
		_castingPlayerIndex = PlayerIndexOf(casterEntity);
		bool casterAlive = casterEntity != Entity.Null && _context.World.IsAlive(casterEntity);

		for (int effectIndex = 0; effectIndex < definition.Effects.Count; effectIndex++)
		{
			AbilityEffectDefinition effect = definition.Effects[effectIndex];

			if (effect.Kind == AbilityEffectKind.Teleport)
			{
				if (casterAlive) ApplyTeleport(casterEntity, effect, casterPosition, targetPosition);
				continue;
			}

			if (effect.Kind == AbilityEffectKind.Summon)
			{
				if (casterAlive) ApplySummon(casterEntity, effect, targetPosition);
				continue;
			}

			CollectTargets(casterEntity, casterAlive, effect, casterPosition, targetPosition);

			float attributeValue = effect.ScalingAttribute.HasValue ? ReadAttribute(casterEntity, effect.ScalingAttribute.Value) : 0f;
			float amount = effect.Scale(effect.Amount, attributeValue);
			float duration = effect.ScaleDuration(effect.Duration, attributeValue);

			for (int targetIndex = 0; targetIndex < _targets.Count; targetIndex++)
			{
				ApplyToTarget(casterEntity, casterAlive, definition, effectIndex, effect, _targets[targetIndex], amount, duration);
			}
		}

		_castingEntity = Entity.Null;
	}

	private void ApplyToTarget(Entity casterEntity, bool casterAlive, AbilityDefinition definition, int effectIndex, AbilityEffectDefinition effect, Entity target, float amount, float duration)
	{
		EcsWorld world = _context.World;
		if (!world.IsAlive(target) || world.Has<Dead>(target)) return;

		switch (effect.Kind)
		{
			case AbilityEffectKind.Damage:
				if (casterAlive && amount > 0f) _context.CombatDamageService.DealResolvedDamage(casterEntity, target, amount, effect.DamageType);
				break;

			case AbilityEffectKind.Heal:
				if (amount > 0f) _context.CombatDamageService.HealSingle(casterAlive ? casterEntity : Entity.Null, target, amount);
				break;

			case AbilityEffectKind.Shield:
				if (amount <= 0f) return;
				if (!world.Has<AbsorbShield>(target)) world.Add(target, new AbsorbShield());
				world.Get<AbsorbShield>(target).Add($"{definition.Id}#{effectIndex}", amount, duration);
				break;

			case AbilityEffectKind.Buff:
			case AbilityEffectKind.Debuff:
				ApplyStatus(target, effect, duration);
				break;

			case AbilityEffectKind.Knockback:
				if (amount > 0f) ApplyKnockback(casterEntity, casterAlive, target, amount);
				break;
		}
	}

	private void ApplyStatus(Entity target, AbilityEffectDefinition effect, float duration)
	{
		if (effect.BuffId != null) UnitOrderService.ApplyBuff(_context.World, target, effect.BuffId, duration);
		if (effect.BuffModifierKey != null) UnitOrderService.ApplyBuff(_context.World, target, effect.BuffModifierKey, duration);
	}

	private void ApplyTeleport(Entity casterEntity, AbilityEffectDefinition effect, Vector3 casterPosition, Vector3 targetPosition)
	{
		Vector3 destination = targetPosition;

		if (effect.Amount > 0f)
		{
			Vector3 delta = targetPosition - casterPosition;
			float distance = delta.Length();
			if (distance <= 0.0001f) return;

			destination = distance <= effect.Amount ? targetPosition : casterPosition + (delta / distance * effect.Amount);
		}

		MoveUnitWithoutWalking(casterEntity, destination);
	}

	private void ApplyKnockback(Entity casterEntity, bool casterAlive, Entity target, float distance)
	{
		EcsWorld world = _context.World;
		if (!world.Has<Position>(target)) return;

		Vector3 targetPosition = world.Get<Position>(target).Value;
		Vector3 origin = casterAlive && world.Has<Position>(casterEntity) ? world.Get<Position>(casterEntity).Value : targetPosition;
		Vector3 delta = targetPosition - origin;
		float length = delta.Length();
		if (length <= 0.0001f) return;

		float resistance = ReadDerivedStat(target, StatKeys.KnockbackResistance);
		float pushed = distance * (1f - Math.Clamp(resistance, 0f, 1f));
		if (pushed <= 0.0001f) return;

		MoveUnitWithoutWalking(target, targetPosition + (delta / length * pushed));
	}

	private void ApplySummon(Entity casterEntity, AbilityEffectDefinition effect, Vector3 targetPosition)
	{
		if (effect.SummonUnitId == null) return;

		EcsWorld world = _context.World;
		int ownerPlayerIndex = world.Has<UnitOwnerPlayer>(casterEntity) ? world.Get<UnitOwnerPlayer>(casterEntity).PlayerIndex : 0;
		int templateStringId = StringRegistry.GetOrAdd(effect.SummonUnitId);

		int alive = CountAliveSummons(casterEntity, templateStringId);
		int spawnCount = effect.SummonCount;
		if (effect.SummonCap > 0) spawnCount = Math.Min(spawnCount, effect.SummonCap - alive);

		for (int index = 0; index < spawnCount; index++)
		{
			// Summons ring the cast point so several of them never occupy one another's space.
			float angle = (MathF.Tau / MathF.Max(1, spawnCount)) * index;
			var offset = new Vector3(MathF.Cos(angle) * 1.2f, 0f, MathF.Sin(angle) * 1.2f);
			UnitId spawned = _summon(effect.SummonUnitId, targetPosition + offset, ownerPlayerIndex);
			if (!spawned.IsValid) continue;

			var summonEntity = new Entity(spawned.Value);
			if (!world.IsAlive(summonEntity)) continue;

			world.SetOrAdd(summonEntity, new SummonedUnit(effect.Duration, casterEntity.Id, ownerPlayerIndex, templateStringId));
		}
	}

	private int CountAliveSummons(Entity casterEntity, int templateStringId)
	{
		EcsWorld world = _context.World;
		int count = 0;

		foreach (Entity entity in world.Query<SummonedUnit>())
		{
			SummonedUnit summon = world.Get<SummonedUnit>(entity);
			if (summon.SourceEntityId != casterEntity.Id || summon.TemplateStringId != templateStringId) continue;

			count++;
		}

		return count;
	}

	/// <summary>
	///     Gathers the entities one effect lands on. Every delivery shape resolves here, so a new shape cannot end up with
	///     its own subtly different ally test.
	/// </summary>
	private void CollectTargets(Entity casterEntity, bool casterAlive, AbilityEffectDefinition effect, Vector3 casterPosition, Vector3 targetPosition)
	{
		_targets.Clear();
		_sortedTargets.Clear();

		if (effect.Delivery == AbilityEffectDelivery.Self || effect.Targets == AbilityTargetSide.Self)
		{
			if (casterAlive) _targets.Add(casterEntity);
			return;
		}

		EcsWorld world = _context.World;
		float facing = world.Has<RotationY>(casterEntity) ? world.Get<RotationY>(casterEntity).Value : 0f;
		var facingDirection = new Vector3(MathF.Cos(facing), 0f, MathF.Sin(facing));

		foreach (Entity entity in world.Query<Position, Health>())
		{
			Health health = world.Get<Health>(entity);
			if (health.Current <= 0f) continue;
			if (world.Has<Dead>(entity)) continue;
			if (casterAlive && entity.Id == casterEntity.Id && effect.Targets != AbilityTargetSide.Allies) continue;
			if (!MatchesSide(casterAlive, entity, effect.Targets)) continue;

			Vector3 entityPosition = world.Get<Position>(entity).Value;
			float distance;

			switch (effect.Delivery)
			{
				case AbilityEffectDelivery.Radius:
					distance = Vector3.Distance(entityPosition, targetPosition);
					if (distance > effect.Radius) continue;
					break;

				case AbilityEffectDelivery.Cone:
					distance = Vector3.Distance(entityPosition, casterPosition);
					if (distance > effect.Length) continue;
					if (distance > 0.0001f)
					{
						Vector3 toTarget = Vector3.Normalize(entityPosition - casterPosition);
						float cosHalf = MathF.Cos(effect.AngleDegrees * 0.5f * (MathF.PI / 180f));
						if (Vector3.Dot(toTarget, facingDirection) < cosHalf) continue;
					}

					break;

				case AbilityEffectDelivery.Line:
					distance = DistanceToSegment(entityPosition, casterPosition, casterPosition + (facingDirection * effect.Length), out float segmentDistance);
					if (segmentDistance > MathF.Max(effect.Radius, 0.5f)) continue;
					break;

				case AbilityEffectDelivery.Aura:
					distance = Vector3.Distance(entityPosition, casterPosition);
					if (distance > effect.Radius) continue;
					break;

				default:
					distance = Vector3.Distance(entityPosition, targetPosition);
					break;
			}

			_sortedTargets.Add((entity, distance));
		}

		if (effect.Delivery == AbilityEffectDelivery.Single)
		{
			Entity closest = Entity.Null;
			float closestDistance = float.MaxValue;

			for (int index = 0; index < _sortedTargets.Count; index++)
			{
				(Entity entity, float distance) = _sortedTargets[index];
				if (distance >= closestDistance) continue;

				closest = entity;
				closestDistance = distance;
			}

			if (closest != Entity.Null) _targets.Add(closest);
			return;
		}

		if (effect.MaxTargets <= 0)
		{
			for (int index = 0; index < _sortedTargets.Count; index++) _targets.Add(_sortedTargets[index].Entity);
			return;
		}

		_sortedTargets.Sort(static (left, right) => left.Distance.CompareTo(right.Distance));
		for (int index = 0; index < _sortedTargets.Count && index < effect.MaxTargets; index++) _targets.Add(_sortedTargets[index].Entity);
	}

	/// <summary>
	///     Whether an effect reaches a unit: whoever cast it counts as an ally of itself, and everyone else is answered by
	///     the match's own relationship table, so a spell's allies are the caster's allies and not whoever happens to be
	///     standing nearby.
	/// </summary>
	private bool MatchesSide(bool casterAlive, Entity entity, AbilityTargetSide side)
	{
		switch (side)
		{
			case AbilityTargetSide.Self:
				return false;
			case AbilityTargetSide.Allies:
				if (!casterAlive) return false;
				return _areAllied(_castingPlayerIndex, PlayerIndexOf(entity));
			case AbilityTargetSide.Enemies:
				if (!casterAlive) return true;
				return !_areAllied(_castingPlayerIndex, PlayerIndexOf(entity));
			default:
				// "All" deliberately skips the caster, which the caller filters: an ability that should reach
				// its own caster says Self.
				return true;
		}
	}

	private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end, out float segmentDistance)
	{
		Vector3 segment = end - start;
		float lengthSquared = segment.LengthSquared();
		float t = lengthSquared > 0.0001f ? Math.Clamp(Vector3.Dot(point - start, segment) / lengthSquared, 0f, 1f) : 0f;
		Vector3 delta = point - (start + (segment * t));
		segmentDistance = delta.Length();
		return Vector3.Distance(point, start);
	}

	/// <summary>Puts a unit somewhere without asking the movement step to walk it there.</summary>
	private void MoveUnitWithoutWalking(Entity entity, Vector3 position)
	{
		_context.World.Set(entity, new Position(position));
		_context.World.Set(entity, new PreviousPosition(position));
		_context.World.Remove<PathFollow>(entity);
	}

	private int PlayerIndexOf(Entity entity)
	{
		if (entity == Entity.Null || !_context.World.IsAlive(entity)) return 0;
		if (_context.World.TryGet<UnitOwnerPlayer>(entity, out UnitOwnerPlayer owner)) return owner.PlayerIndex;

		return _context.PlayerSlotForEntity(_context.World.TryGet<Owner>(entity, out Owner entityOwner) ? entityOwner.PlayerEntity.Value : Entity.Null) is int slot && slot >= 0 ? slot : 0;
	}

	private float ReadAttribute(Entity entity, AttributeType attribute)
	{
		EcsWorld world = _context.World;
		if (world.Has<Stats>(entity))
		{
			ref Stats stats = ref world.Get<Stats>(entity);
			if (stats.TryGetValue(new StatId(attribute), out float value)) return value;
		}

		if (world.Has<UnitAttributes>(entity)) return world.Get<UnitAttributes>(entity).Get(attribute);

		return 0f;
	}

	private float ReadDerivedStat(Entity entity, string statKey)
	{
		EcsWorld world = _context.World;
		if (world.Has<Stats>(entity))
		{
			ref Stats stats = ref world.Get<Stats>(entity);
			if (stats.TryGetValue(new StatId(statKey), out float value)) return value;
		}

		return 0f;
	}
}
