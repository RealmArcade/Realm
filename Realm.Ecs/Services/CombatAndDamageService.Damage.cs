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
	/// <summary>the public damage and heal entry points and the aoe passes behind them.</summary>
	public partial class CombatAndDamageService
	{
		/// <summary>
		/// Applies damage through the combat resolver so armor, penetration and the damage/armor type
		/// matrix apply. This is the single-target counterpart to <see cref="DealSpellDamageAOE"/>.
		/// </summary>
		public void DealResolvedDamage(Entity attacker, Entity target, float amount, string damageType)
		{
			if (amount <= 0f) return;
			if (!_context.World.IsAlive(target) || _context.World.Has<Dead>(target) || !_context.World.Has<Health>(target)) return;
			if (_context.World.Has<Invulnerable>(target)) return;

			_aoeKillList.Clear();
			_aoeLastAttackerList.Clear();

			UpdateAoeLastAttacker(target, attacker);

			ref var hp = ref _context.World.Get<Health>(target);
			var result = CombatResolver.ResolveAbilityDamage(_context.World, attacker, target, amount, damageType);
			ApplyResolvedDamage(target, ref hp, result.FinalDamage, attacker);

			ProcessAoeLastAttackers();
			ProcessAoeKillList();
		}

		public void DealSpellDamageAOE(Vector3 position, float radius, float damage, Entity casterEntity, bool enemyOnly = true, string damageType = "magic")
		{
			_aoeKillList.Clear();
			_aoeLastAttackerList.Clear();
			_aoeDamageType = damageType;

			int casterPlayer = GetPlayerIndexForEntity(casterEntity);

			if (_currentMovementService != null)
			{
				_aoePosition = position;
				_aoeRadius = radius;
				_aoeDamage = damage;
				_aoeCasterEntity = casterEntity;
				_aoeEnemyOnly = enemyOnly;
				_aoeCasterPlayer = casterPlayer;

				_currentMovementService.QueryUnitsInRadius(position, radius, _aoeDamageCandidateCallback);
			}
			else
			{
				var query = QueryCache.AllPositionAndHealthNoneDeadQuery;
				_context.World.Query(in query, (Entity entity, ref Position pos, ref Health hp) => { ProcessAoeTarget(entity, ref pos, ref hp, position, radius, damage, casterEntity, enemyOnly); });
			}

			ProcessAoeLastAttackers();
			ProcessAoeKillList();
		}

		private void OnAoeDamageCandidateFound(in MovementAndPathfindingService.SpatialGridEntry entry)
		{
			Entity entity = entry.Entity;
			if (!_context.World.IsAlive(entity) || _context.World.Has<Dead>(entity) || !_context.World.Has<Health>(entity)) return;

			ref var hp = ref _context.World.Get<Health>(entity);
			ProcessAoeTarget(entity, ref hp, _aoeDamage, _aoeCasterEntity, entry.Position, _aoePosition, _aoeRadius, _aoeEnemyOnly);
		}

		private void ProcessAoeTarget(Entity entity, ref Health hp, float damage, Entity casterEntity, Vector3 entityPos, Vector3 position, float radius, bool enemyOnly)
		{
			if (!IsAoeTargetValid(entity, entityPos, position, radius, enemyOnly, casterEntity)) return;

			UpdateAoeLastAttacker(entity, casterEntity);

			ApplyAoeDamage(entity, ref hp, damage, casterEntity);
		}

		private void ProcessAoeTarget(Entity entity, ref Position pos, ref Health hp, Vector3 position, float radius, float damage, Entity casterEntity, bool enemyOnly)
		{
			if (!IsAoeTargetValid(entity, pos.Value, position, radius, enemyOnly, casterEntity)) return;

			UpdateAoeLastAttacker(entity, casterEntity);

			ApplyAoeDamage(entity, ref hp, damage, casterEntity);
		}

		private bool IsAoeTargetValid(Entity entity, Vector3 entityPos, Vector3 aoePosition, float aoeRadius, bool enemyOnly, Entity casterEntity)
		{
			if (_context.World.Has<Invulnerable>(entity)) return false;

			if (enemyOnly)
			{
				int casterPlayer = GetPlayerIndexForEntity(casterEntity);
				int targetPlayer = GetPlayerIndexForEntity(entity);
				if (!ArePlayerIndicesEnemies(casterPlayer, targetPlayer)) return false;
			}

			if (Vector3.Distance(entityPos, aoePosition) > aoeRadius) return false;

			return true;
		}

		private void UpdateAoeLastAttacker(Entity entity, Entity casterEntity)
		{
			if (casterEntity != Entity.Null && _context.World.IsAlive(casterEntity))
			{
				if (!HasAuthority) return;
				if (_context.World.Has<LastAttacker>(entity)) _context.World.Set(entity, new LastAttacker(casterEntity));
				else _aoeLastAttackerList.Add((entity, casterEntity));
			}
		}

		private void ApplyAoeDamage(Entity entity, ref Health hp, float damage, Entity casterEntity)
		{
			var result = CombatResolver.ResolveAbilityDamage(_context.World, casterEntity, entity, damage, _aoeDamageType);
			ApplyResolvedDamage(entity, ref hp, result.FinalDamage, casterEntity);
		}

		/// <summary>
		///     Runs damage through the entity's absorb pools before health. Called by every damage
		///     source, so a shield is worth the same against an attack, a splash, a spell or an ability.
		/// </summary>
		private float AbsorbDamage(Entity entity, float damage)
		{
			if (damage <= 0f) return 0f;
			if (!_context.World.Has<AbsorbShield>(entity)) return damage;

			return _context.World.Get<AbsorbShield>(entity).Consume(damage);
		}

		/// <summary>Heals one unit, honouring the caster's healing multiplier.</summary>
		public void HealSingle(Entity casterEntity, Entity target, float healAmount)
		{
			if (healAmount <= 0f) return;
			if (!_context.World.IsAlive(target) || _context.World.Has<Dead>(target) || !_context.World.Has<Health>(target)) return;

			float scaled = healAmount * GetHealingMultiplier(casterEntity);
			ref var hp = ref _context.World.Get<Health>(target);
			float before = hp.Current;
			float after = Math.Min(hp.Max, before + scaled);
			float healed = after - before;
			if (healed <= 0f) return;

			if (HasAuthority) hp.Current = after;

			OnHealFlashRequested?.Invoke(target);
			OnUnitHealedCallback?.Invoke(target, casterEntity, healed);
		}

		private void ApplyResolvedDamage(Entity entity, ref Health hp, float finalDamage, Entity casterEntity)
		{
			finalDamage = AbsorbDamage(entity, finalDamage);
			if (finalDamage <= 0f) return;

			float newHp = Math.Max(0, hp.Current - finalDamage);
			if (HasAuthority) hp.Current = newHp;

			OnUnitDamagedCallback?.Invoke(entity, casterEntity, finalDamage);

			if (HasAuthority && newHp <= 0) _aoeKillList.Add(entity);
			else OnDamageFlashRequested?.Invoke(entity);
		}

		private void ProcessAoeLastAttackers()
		{
			foreach (var (targetEnt, attackerEnt) in _aoeLastAttackerList)
			{
				if (_context.World.IsAlive(targetEnt))
				{
					_context.World.AddOrGet(targetEnt, new LastAttacker(attackerEnt)) = new LastAttacker(attackerEnt);
				}
			}
		}

		private void ProcessAoeKillList()
		{
			if (!HasAuthority)
			{
				_aoeKillList.Clear();
				return;
			}

			foreach (var entity in _aoeKillList)
			{
				if (!_context.World.IsAlive(entity) || _context.World.Has<Dead>(entity)) continue;

				_context.World.Add<Dead>(entity);
				ClearReferencesTo(entity);
				OnKillUnitRequested?.Invoke(entity);
			}
		}

		public void HealAOE(Vector3 position, float radius, float healAmount, Entity casterEntity = default)
		{
			int casterPlayer = GetPlayerIndexForEntity(casterEntity);
			healAmount *= GetHealingMultiplier(casterEntity);

			if (_currentMovementService != null)
			{
				_healPosition = position;
				_healRadius = radius;
				_healAmount = healAmount;
				_healCasterEntity = casterEntity;
				_healCasterPlayer = casterPlayer;

				_currentMovementService.QueryUnitsInRadius(position, radius, _healCandidateCallback);
				return;
			}

			var query = QueryCache.AllPositionAndHealthNoneDeadQuery;
			_context.World.Query(in query, (Entity entity, ref Position pos, ref Health hp) =>
			{
				int targetPlayer = GetPlayerIndexForEntity(entity);
				if (ArePlayerIndicesEnemies(casterPlayer, targetPlayer)) return;

				if (Vector3.Distance(pos.Value, position) <= radius)
				{
					if (HasAuthority) hp.Current = Math.Min(hp.Max, hp.Current + healAmount);
					OnHealFlashRequested?.Invoke(entity);
					OnUnitHealedCallback?.Invoke(entity, casterEntity, healAmount);
				}
			});
		}

		private string _aoeDamageType = "magic";

		private void OnAoeHealCandidateFound(in MovementAndPathfindingService.SpatialGridEntry entry)
		{
			Entity entity = entry.Entity;
			if (!_context.World.IsAlive(entity) || _context.World.Has<Dead>(entity) || !_context.World.Has<Health>(entity)) return;

			int targetPlayer = GetPlayerIndexForEntity(entity);
			if (ArePlayerIndicesEnemies(_healCasterPlayer, targetPlayer)) return;

			if (Vector3.Distance(entry.Position, _healPosition) <= _healRadius)
			{
				if (HasAuthority)
				{
					ref var hp = ref _context.World.Get<Health>(entity);
					hp.Current = Math.Min(hp.Max, hp.Current + _healAmount);
				}

				OnHealFlashRequested?.Invoke(entity);
				OnUnitHealedCallback?.Invoke(entity, _healCasterEntity, _healAmount);
			}
		}
	}
}