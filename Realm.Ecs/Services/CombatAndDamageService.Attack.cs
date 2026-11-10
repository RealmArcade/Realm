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
using PathingFlags = Realm.Ecs.Components.Movement.PathingFlags;

namespace Realm.Ecs.Services
{
	/// <summary>resolving an attack, from range check to damage and projectiles.</summary>
	public partial class CombatAndDamageService
	{
		private void CombatQueryAction(Entity entity, ref Position pos, ref Attack atk, ref AttackTarget target, ref Owner owner)
		{
			if (!ShouldSimulateEntity(entity)) return;

			if (!_context.World.IsAlive(target.Target) || _context.World.Has<Dead>(target.Target) || _context.World.Has<Invulnerable>(target.Target))
			{
				_tickActionsToRemoveTarget.Add(entity);
				return;
			}

			if (!ArePlayerIndicesEnemies(GetPlayerIndexForEntity(entity), GetPlayerIndexForEntity(target.Target)))
			{
				_tickActionsToRemoveTarget.Add(entity);
				return;
			}

			if (IsFlying(target.Target) && !CanAttackTarget(entity, target.Target))
			{
				_tickActionsToRemoveTarget.Add(entity);
				return;
			}

			if (TryProcessCombatAction(entity, ref pos, ref atk, ref target, ref owner)) return;
		}

		private bool TryProcessCombatAction(Entity entity, ref Position pos, ref Attack atk, ref AttackTarget target, ref Owner owner)
		{
			var currentPos = pos.Value;
			var targetPos = _context.World.Get<Position>(target.Target).Value;
			float effectiveRange = GetEffectiveRange(entity, target.Target, atk.Range, out bool isMelee, out float meleeVerticalReach);

			bool withinRange = isMelee
				? IsWithinMeleeRange(currentPos, targetPos, effectiveRange, meleeVerticalReach)
				: Distance(currentPos, targetPos) <= effectiveRange;

			if (withinRange)
			{
				ClearChaseTracking(entity);
				_tickActionsToStopChasing.Add(entity);

				if (atk.CurrentCooldown > 0) return true;

				if (_context.World.Has<Invulnerable>(target.Target))
				{
					atk.CurrentCooldown = (UnlimitedPowerProvider?.Invoke() == true) ? 0f : GetEffectiveAttackCooldown(entity, atk.Cooldown);
					return true;
				}

				ExecuteAttack(entity, target.Target, ref atk, currentPos, targetPos, owner);
			}
			else
			{
				HandleOutOfRangeAction(entity, target.Target, currentPos, targetPos, effectiveRange, isMelee, meleeVerticalReach);
			}

			return true;
		}

		private bool IsWithinMeleeRange(Vector3 currentPos, Vector3 targetPos, float effectiveRange, float meleeVerticalReach)
		{
			float horizontalDist = new Vector2(currentPos.X - targetPos.X, currentPos.Z - targetPos.Z).Length();
			float verticalDist = Math.Abs(currentPos.Y - targetPos.Y);
			return horizontalDist <= effectiveRange && verticalDist <= meleeVerticalReach;
		}

		private void ExecuteAttack(Entity entity, Entity target, ref Attack atk, Vector3 currentPos, Vector3 targetPos, Owner owner)
		{
			float baseDamage = ResolveAttackDamage(entity, target, ref atk, out float finalDamage);
			ApplyDamageAndEffects(entity, target, finalDamage, baseDamage, ref atk, targetPos, owner);

			atk.CurrentCooldown = (UnlimitedPowerProvider?.Invoke() == true) ? 0f : GetEffectiveAttackCooldown(entity, atk.Cooldown);

			HandleProjectile(entity, target, currentPos, targetPos, atk.Range);
		}

		private float ResolveAttackDamage(Entity entity, Entity target, ref Attack atk, out float finalDamage)
		{
			var resistances = _context.World.Has<Resistances>(target) ? _context.World.Get<Resistances>(target) : default;
			float baseDamage = _context.StatService != null ? _context.StatService.GetStatValue(entity, new StatId(StatKeys.Attack)) : 0f;
			if (baseDamage <= 0) baseDamage = atk.Damage;

			bool magic = DamageTypes.IsMagic(atk.DamageType);
			var damageResult = CombatResolver.ResolveDamage(
				baseDamage: baseDamage, damageVariance: atk.DamageVariance,
				critChance: atk.CritChance, critMultiplier: atk.CritMultiplier,
				physicalResistance: resistances.Physical, magicResistance: resistances.Magic,
				flatPenetration: ReadPenetration(entity, magic, atk.FlatArmorPenetration),
				percentPenetration: ReadPenetrationPercent(entity, magic, atk.PercentArmorPenetration),
				damageType: atk.DamageType, config: Config
			);

			finalDamage = damageResult.FinalDamage;
			return baseDamage;
		}

		/// <summary>
		///     The flat penetration that matches the hit's damage type, read from the stat snapshot so
		///     items and buffs are honoured. The attack component's authored value is the fallback for
		///     an entity whose stats have never been recalculated.
		/// </summary>
		private float ReadPenetration(Entity entity, bool magic, float authored)
		{
			string key = magic ? StatKeys.FlatMagicPenetration : StatKeys.FlatArmorPenetration;
			return ReadPenetrationStat(entity, key, magic ? 0f : authored);
		}

		private float ReadPenetrationPercent(Entity entity, bool magic, float authored)
		{
			string key = magic ? StatKeys.PercentMagicPenetration : StatKeys.PercentArmorPenetration;
			return ReadPenetrationStat(entity, key, magic ? 0f : authored);
		}

		private float ReadPenetrationStat(Entity entity, string statKey, float fallback)
		{
			float value = _context.StatService != null ? _context.StatService.GetStatValue(entity, new StatId(statKey)) : 0f;
			return value > 0f ? value : fallback;
		}

		private void ApplyDamageAndEffects(Entity entity, Entity target, float damage, float baseDamage, ref Attack atk, Vector3 targetPos, Owner owner)
		{
			RecordLastAttacker(target, entity);

			OnUnitAttackedCallback?.Invoke(entity, target);

			float effectiveness = ResolveDamageEffectiveness(entity, target);
			float scaledDamage = damage * effectiveness;
			float dealt = AbsorbDamage(target, scaledDamage);
			if (dealt > 0f) OnUnitDamagedCallback?.Invoke(target, entity, dealt, baseDamage * effectiveness);

			float newHp = 0f;
			if (HasAuthority)
			{
				var targetHealth = _context.World.Get<Health>(target);
				newHp = Math.Max(0, targetHealth.Current - dealt);
				targetHealth.Current = newHp;
				targetHealth.TimeSinceLastDamage = 0f;
				_context.World.Set(target, targetHealth);
			}

			if (atk.SplashType != SplashType.None && atk.SplashOuterRadius > 0f)
				EvaluateSplashDamage(entity, target, targetPos, baseDamage, atk, owner);

			CheckCombatAlert(target);
			AcquireNewTargetFromAttacker(entity, target);

			if (HasAuthority && newHp <= 0) _tickUnitsToKill.Add(target);
			else OnDamageFlashRequested?.Invoke(target);
		}

		private void CheckCombatAlert(Entity target)
		{
			if (!_context.World.Has<DefinitionId>(target)) return;

			int targetPlayer = _context.World.Has<UnitOwnerPlayer>(target) ? _context.World.Get<UnitOwnerPlayer>(target).PlayerIndex : 0;
			string targetUnitId = _context.World.Get<DefinitionId>(target).Value;
			if (GetCombatAlertTimer() <= 0f)
			{
				SetCombatAlertTimer(UnderAttackAlertCooldown);
				OnUnderAttackAlertRequested?.Invoke(targetPlayer, targetUnitId);
			}
		}

		private void AcquireNewTargetFromAttacker(Entity entity, Entity target)
		{
			if (!_context.World.IsAlive(target) || _context.World.Has<Dead>(target) || _context.World.Has<AttackTarget>(target)) return;
			if (!_context.World.Has<Attack>(target)) return;

			bool hasMoveTo = _context.World.Has<MoveTo>(target);
			if (!hasMoveTo || _context.World.Has<AttackMove>(target))
			{
				if (IsFlying(entity) && !CanAttackTarget(target, entity)) return;
				_tickNewAttackTargets.Add((target, new AttackTarget(entity)));
			}
		}

		private void HandleProjectile(Entity entity, Entity target, Vector3 currentPos, Vector3 targetPos, float range)
		{
			if (range <= 3f) return;

			string? weaponId = null;
			if (_context.World.Has<Inventory>(entity))
			{
				var inv = _context.World.Get<Inventory>(entity);
				for (int itemIndex = 0; itemIndex < inv.Count; itemIndex++)
				{
					string? item = inv.GetItemId(itemIndex);
					if (item == null) continue;
					string[]? grantedWeapons = ItemGrantedWeaponsProvider?.Invoke(item);
					if (grantedWeapons != null && grantedWeapons.Length > 0)
					{
						weaponId = grantedWeapons[0];
						break;
					}
				}
			}

			if (weaponId == null && _context.World.Has<DefinitionId>(entity))
			{
				var defId = _context.World.Get<DefinitionId>(entity).Value;
				var weapons = UnitWeaponsProvider?.Invoke(defId);
				weaponId = (weapons != null && weapons.Length > 0) ? weapons[0] : defId;
			}

			OnWeaponProjectileRequested?.Invoke(currentPos, targetPos, weaponId, target, GetPlayerIndexForEntity(entity));
		}

		private void HandleOutOfRangeAction(Entity entity, Entity target, Vector3 currentPos, Vector3 targetPos, float effectiveRange, bool isMelee, float meleeVerticalReach)
		{
			if (_context.World.Has<Building>(entity))
			{
				_tickActionsToRemoveTarget.Add(entity);
				ClearChaseTracking(entity);
			}
			else if (!_context.World.Has<HoldPosition>(entity) && _context.World.Has<Movable>(entity))
			{
				if (!IsReachableWithinRange(entity, target, currentPos, targetPos, effectiveRange, isMelee, meleeVerticalReach))
				{
					RegisterAbandonedTarget(entity, target);
					_tickActionsToRemoveTarget.Add(entity);
					ClearChaseTracking(entity);
					return;
				}

				_tickActionsToChase.Add((entity, targetPos));
				UpdateChaseTracking(entity, Distance(currentPos, targetPos), target);
			}
			else
			{
				_tickActionsToRemoveTarget.Add(entity);
				ClearChaseTracking(entity);
			}
		}
	}
}