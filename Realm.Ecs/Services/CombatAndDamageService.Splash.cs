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
	/// <summary>splash damage around an impact.</summary>
	public partial class CombatAndDamageService
	{
		private void EvaluateSplashDamage(Entity attacker, Entity primaryTarget, Vector3 impactPos, float baseDamage, Attack atk, Owner attackerOwner)
		{
			int attackerPlayer = _context.World.Has<UnitOwnerPlayer>(attacker) ? _context.World.Get<UnitOwnerPlayer>(attacker).PlayerIndex : 0;

			if (_currentMovementService != null)
			{
				_splashAttacker = attacker;
				_splashPrimaryTarget = primaryTarget;
				_splashImpactPos = impactPos;
				_splashBaseDamage = baseDamage;
				_splashAtk = atk;
				_splashAttackerOwner = attackerOwner;
				_splashAttackerPlayer = attackerPlayer;

				_currentMovementService.QueryUnitsInRadius(impactPos, atk.SplashOuterRadius, _splashCandidateCallback);
				return;
			}

			var splashQuery = QueryCache.AllPositionAndHealthAndOwnerNoneDeadQuery;

			_context.World.Query(in splashQuery, (Entity splashEnt, ref Position sPos, ref Health sHp, ref Owner sOwner) =>
			{
				if (splashEnt == primaryTarget || splashEnt == attacker) return;
				if (_context.World.Has<Invulnerable>(splashEnt)) return;

				if (!atk.FriendlyFire)
				{
					int splashPlayer = _context.World.Has<UnitOwnerPlayer>(splashEnt) ? _context.World.Get<UnitOwnerPlayer>(splashEnt).PlayerIndex : 0;
					if (!ArePlayerIndicesEnemies(attackerPlayer, splashPlayer) || sOwner.PlayerEntity == attackerOwner.PlayerEntity) return;
				}

				float distToImpact = Distance(impactPos, sPos.Value);
				float splashRatio = CombatResolver.CalculateSplashRatio(
					distToImpact, atk.SplashType,
					atk.SplashInnerRadius, atk.SplashMediumRadius, atk.SplashOuterRadius,
					atk.SplashInnerRatio, atk.SplashMediumRatio, atk.SplashOuterRatio
				);

				if (splashRatio > 0f)
				{
					ApplySplashDamage(attacker, splashEnt, splashRatio, baseDamage, ref atk, ref sHp);
				}
			});
		}

		private void OnSplashCandidateFound(in MovementAndPathfindingService.SpatialGridEntry entry)
		{
			Entity splashEnt = entry.Entity;
			if (splashEnt == _splashPrimaryTarget || splashEnt == _splashAttacker) return;
			if (!_context.World.IsAlive(splashEnt) || _context.World.Has<Dead>(splashEnt) || !_context.World.Has<Health>(splashEnt)) return;
			if (_context.World.Has<Invulnerable>(splashEnt)) return;

			Owner sOwner = _context.World.Has<Owner>(splashEnt) ? _context.World.Get<Owner>(splashEnt) : default;

			if (!_splashAtk.FriendlyFire)
			{
				int splashPlayer = _context.World.Has<UnitOwnerPlayer>(splashEnt) ? _context.World.Get<UnitOwnerPlayer>(splashEnt).PlayerIndex : 0;
				if (!ArePlayerIndicesEnemies(_splashAttackerPlayer, splashPlayer) || sOwner.PlayerEntity == _splashAttackerOwner.PlayerEntity) return;
			}

			float distToImpact = Distance(_splashImpactPos, entry.Position);
			float splashRatio = CombatResolver.CalculateSplashRatio(
				distToImpact, _splashAtk.SplashType,
				_splashAtk.SplashInnerRadius, _splashAtk.SplashMediumRadius, _splashAtk.SplashOuterRadius,
				_splashAtk.SplashInnerRatio, _splashAtk.SplashMediumRatio, _splashAtk.SplashOuterRatio
			);

			if (splashRatio > 0f)
			{
				ref var sHp = ref _context.World.Get<Health>(splashEnt);
				ApplySplashDamage(_splashAttacker, splashEnt, splashRatio, _splashBaseDamage, ref _splashAtk, ref sHp);
			}
		}

		private void ApplySplashDamage(Entity attacker, Entity splashEnt, float splashRatio, float baseDamage, ref Attack atk, ref Health sHp)
		{
			var sResistances = _context.World.Has<Resistances>(splashEnt) ? _context.World.Get<Resistances>(splashEnt) : default;

			bool sMagic = DamageTypes.IsMagic(atk.DamageType);
			var sResult = CombatResolver.ResolveDamage(
				baseDamage: baseDamage * splashRatio, damageVariance: atk.DamageVariance,
				critChance: atk.CritChance, critMultiplier: atk.CritMultiplier,
				physicalResistance: sResistances.Physical, magicResistance: sResistances.Magic,
				flatPenetration: ReadPenetration(_splashAttacker, sMagic, atk.FlatArmorPenetration),
				percentPenetration: ReadPenetrationPercent(_splashAttacker, sMagic, atk.PercentArmorPenetration),
				damageType: atk.DamageType, config: Config
			);

			float splashDamage = AbsorbDamage(splashEnt, sResult.FinalDamage);
			if (splashDamage <= 0f) return;

			if (HasAuthority)
			{
				sHp.Current = Math.Max(0, sHp.Current - splashDamage);
				sHp.TimeSinceLastDamage = 0f;
				_context.World.Set(splashEnt, sHp);
			}

			OnUnitDamagedCallback?.Invoke(splashEnt, attacker, splashDamage);

			RecordLastAttacker(splashEnt, attacker);

			if (HasAuthority && sHp.Current <= 0) _tickUnitsToKill.Add(splashEnt);
			else OnDamageFlashRequested?.Invoke(splashEnt);
		}
	}
}