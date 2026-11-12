using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Realm.Ecs;
using DotRecast.Detour;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;

namespace Realm.Ecs.Services
{
	/// <summary>healer ticks, targets and the heal itself.</summary>
	public partial class CombatAndDamageService
	{
		private void ProcessHealingTicks()
		{
			_tickHealRemoveTargets.Clear();
			_tickHealChaseTargets.Clear();
			_tickHealStopChasing.Clear();
			_context.World.Query(in _healingExecutionQuery, _healingExecutionQueryDelegate);

			ProcessHealRemovals();
			ProcessHealChases();
			ProcessHealStopChasing();
		}

		private void ProcessHealRemovals()
		{
			foreach (var ent in _tickHealRemoveTargets)
			{
				if (_context.World.IsAlive(ent) && _context.World.Has<HealingTarget>(ent))
				{
					_context.World.Remove<HealingTarget>(ent);
				}
			}
		}

		private void ProcessHealChases()
		{
			foreach (var (healer, targetPos) in _tickHealChaseTargets)
			{
				if (!_context.World.IsAlive(healer)) continue;

				bool needsRetarget = true;
				if (_context.World.Has<MoveTo>(healer))
				{
					var existingTarget = _context.World.Get<MoveTo>(healer).Target;
					if (Distance(existingTarget, targetPos) < ChaseRetargetDistance) needsRetarget = false;
				}

				if (needsRetarget) _context.World.SetOrAdd(healer, new MoveTo(targetPos));
			}
		}

		private void ProcessHealStopChasing()
		{
			foreach (var healer in _tickHealStopChasing)
			{
				if (!_context.World.IsAlive(healer)) continue;

				if (_context.World.Has<MoveTo>(healer)) _context.World.Remove<MoveTo>(healer);
				if (_context.World.Has<Velocity>(healer)) _context.World.Set(healer, new Velocity(Vector3.Zero));
			}
		}

		private void HealingExecutionQueryAction(Entity entity, ref Position pos, ref Attack atk, ref HealingTarget target, ref Owner owner)
		{
			if (!IsValidHealingTarget(entity, target.Target, out var targetHealth)) return;

			if (HandleHealingDistance(entity, pos.Value, target.Target, atk.Range)) return;

			_tickHealStopChasing.Add(entity);

			if (atk.CurrentCooldown > 0) return;

			float healAmount = atk.Damage * GetHealingMultiplier(entity);
			ApplyHealing(target.Target, healAmount, targetHealth);

			atk.CurrentCooldown = GetHealCooldown(entity, atk.Cooldown);

			InvokeHealEvents(entity, pos.Value, target.Target, healAmount);
		}

		private bool IsValidHealingTarget(Entity entity, Entity targetEntity, out Health targetHealth)
		{
			targetHealth = default;
			if (!_context.World.IsAlive(targetEntity) || _context.World.Has<Dead>(targetEntity))
			{
				_tickHealRemoveTargets.Add(entity);
				return false;
			}

			targetHealth = _context.World.Get<Health>(targetEntity);
			if (targetHealth.Current >= targetHealth.Max)
			{
				_tickHealRemoveTargets.Add(entity);
				return false;
			}

			return true;
		}

		private float GetHealingMultiplier(Entity casterEntity)
		{
			if (_context.StatService == null) return 1f;
			float multiplier = _context.StatService.GetStatValue(casterEntity, new StatId(StatKeys.HealingMultiplier));
			return multiplier > 0f ? multiplier : 1f;
		}

		private void ApplyHealing(Entity targetEntity, float healAmount, Health targetHealth)
		{
			if (!HasAuthority) return;

			float newHp = Math.Min(targetHealth.Max, targetHealth.Current + healAmount);
			_context.World.Set(targetEntity, new Health(newHp, targetHealth.Max));
		}

		private float GetHealCooldown(Entity entity, float baseCooldown)
		{
			if (UnlimitedPowerProvider?.Invoke() == true) return 0f;
			return GetEffectiveAttackCooldown(entity, baseCooldown);
		}

		private void InvokeHealEvents(Entity entity, Vector3 pos, Entity targetEntity, float healAmount)
		{
			OnHealEffectRequested?.Invoke(pos, _context.World.Get<Position>(targetEntity).Value);
			OnHealFlashRequested?.Invoke(targetEntity);
			OnUnitHealedCallback?.Invoke(targetEntity, entity, healAmount);
		}

		private bool HandleHealingDistance(Entity entity, Vector3 currentPos, Entity targetEntity, float atkRange)
		{
			var targetPos = _context.World.Get<Position>(targetEntity).Value;
			float effectiveRange = Math.Max(atkRange + GetTargetCollisionRadius(targetEntity), GetMinimumMeleeContactReach(entity, targetEntity));

			if (Distance(currentPos, targetPos) <= effectiveRange) return false;

			if (!_context.World.Has<HoldPosition>(entity) && _context.World.Has<Movable>(entity))
			{
				_tickHealChaseTargets.Add((entity, targetPos));
			}

			return true;
		}
	}
}