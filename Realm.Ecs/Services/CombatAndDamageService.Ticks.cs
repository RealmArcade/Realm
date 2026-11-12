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
	/// <summary>the per tick passes: retaliation, deaths, reference clearing and chases.</summary>
	public partial class CombatAndDamageService
	{
		private void ProcessCombatTicks()
		{
			_tickActionsToAddLastAttacker.Clear();
			_tickActionsToRemoveTarget.Clear();
			_tickActionsToChase.Clear();
			_tickActionsToStopChasing.Clear();
			_tickUnitsToKill.Clear();

			_context.World.Query(in _combatQuery, _combatQueryDelegate);

			ProcessLastAttackers();
			ProcessUnitsToKill();
			ProcessTargetRemovals();
			ProcessChaseActions();
			ProcessStopChasingActions();
			ProcessRetaliationTargets();
		}

		private void ProcessRetaliationTargets()
		{
			if (!HasAuthority)
			{
				_tickNewAttackTargets.Clear();
				return;
			}

			for (int i = 0; i < _tickNewAttackTargets.Count; i++)
			{
				var (attacker, target) = _tickNewAttackTargets[i];
				if (_context.World.IsAlive(attacker))
				{
					_context.World.SetOrAdd(attacker, target);
				}
			}
			_tickNewAttackTargets.Clear();
		}

		private void RecordLastAttacker(Entity target, Entity attacker)
		{
			if (!HasAuthority) return;
			if (_context.World.Has<LastAttacker>(target)) _context.World.Set(target, new LastAttacker(attacker));
			else _tickActionsToAddLastAttacker.Add((target, attacker));
		}

		private void ProcessLastAttackers()
		{
			foreach (var (targetEnt, attackerEnt) in _tickActionsToAddLastAttacker)
			{
				if (_context.World.IsAlive(targetEnt))
				{
					_context.World.AddOrGet(targetEnt, new LastAttacker(attackerEnt)) = new LastAttacker(attackerEnt);
				}
			}
		}

		private void ProcessUnitsToKill()
		{
			if (!HasAuthority)
			{
				_tickUnitsToKill.Clear();
				return;
			}

			foreach (var targetEntity in _tickUnitsToKill)
			{
				ClearChaseTracking(targetEntity);
				_abandonedTargetCooldown.Remove(targetEntity);
				if (!_context.World.IsAlive(targetEntity) || _context.World.Has<Dead>(targetEntity)) continue;

				_context.World.Add<Dead>(targetEntity);
				ClearReferencesTo(targetEntity);
				OnKillUnitRequested?.Invoke(targetEntity);
			}
		}

		private void ClearReferencesTo(Entity deadEntity)
		{
			_clearedReferenceTarget = deadEntity;

			_clearedReferenceEntities.Clear();
			_context.World.Query(in _attackTargetReferenceQuery, _attackTargetReferenceDelegate);
			for (int index = 0; index < _clearedReferenceEntities.Count; index++) _context.World.Remove<AttackTarget>(_clearedReferenceEntities[index]);

			_clearedReferenceEntities.Clear();
			_context.World.Query(in _followReferenceQuery, _followReferenceDelegate);
			for (int index = 0; index < _clearedReferenceEntities.Count; index++) _context.World.Remove<Follow>(_clearedReferenceEntities[index]);

			_clearedReferenceEntities.Clear();
			_context.World.Query(in _lastAttackerReferenceQuery, _lastAttackerReferenceDelegate);
			for (int index = 0; index < _clearedReferenceEntities.Count; index++) _context.World.Remove<LastAttacker>(_clearedReferenceEntities[index]);
		}

		private void CollectAttackTargetOfClearedUnit(Entity entity, ref AttackTarget target)
		{
			if (target.Target != _clearedReferenceTarget) return;
			_clearedReferenceEntities.Add(entity);
		}

		private void CollectFollowOfClearedUnit(Entity entity, ref Follow follow)
		{
			if (follow.Target != _clearedReferenceTarget) return;
			_clearedReferenceEntities.Add(entity);
		}

		private void CollectLastAttackerOfClearedUnit(Entity entity, ref LastAttacker lastAttacker)
		{
			if (lastAttacker.Value != _clearedReferenceTarget) return;
			_clearedReferenceEntities.Add(entity);
		}

		private void ProcessTargetRemovals()
		{
			foreach (var ent in _tickActionsToRemoveTarget)
			{
				ClearChaseTracking(ent);
				if (!_context.World.IsAlive(ent)) continue;

				if (_context.World.Has<AttackTarget>(ent)) _context.World.Remove<AttackTarget>(ent);

				if (HandleAttackMoveRemoval(ent)) continue;
				if (HandlePatrolRemoval(ent)) continue;
				HandleMoveToRemoval(ent);
			}
		}

		private bool HandleAttackMoveRemoval(Entity ent)
		{
			if (!_context.World.Has<AttackMove>(ent)) return false;

			var am = _context.World.Get<AttackMove>(ent);
			if (!_context.World.Has<MoveTo>(ent)) _context.World.Add(ent, new MoveTo(am.Target));
			return true;
		}

		private bool HandlePatrolRemoval(Entity ent)
		{
			if (!_context.World.Has<Patrol>(ent)) return false;

			var patrol = _context.World.Get<Patrol>(ent);
			var destVec = patrol.GoingToB ? patrol.PointB : patrol.PointA;
			if (!_context.World.Has<MoveTo>(ent)) _context.World.Add(ent, new MoveTo(destVec));
			return true;
		}

		private void HandleMoveToRemoval(Entity ent)
		{
			if (!_context.World.Has<MoveTo>(ent)) return;

			_context.World.Remove<MoveTo>(ent);
			if (_context.World.Has<Velocity>(ent)) _context.World.Set(ent, new Velocity(Vector3.Zero));
		}

		private void ProcessChaseActions()
		{
			foreach (var (attacker, targetPos) in _tickActionsToChase)
			{
				if (!_context.World.IsAlive(attacker) || !_context.World.Has<AttackTarget>(attacker)) continue;

				bool needsRetarget = true;
				if (_context.World.Has<MoveTo>(attacker))
				{
					var existingTarget = _context.World.Get<MoveTo>(attacker).Target;
					if (Distance(existingTarget, targetPos) < ChaseRetargetDistance) needsRetarget = false;
				}

				if (needsRetarget) _context.World.SetOrAdd(attacker, new MoveTo(targetPos));
			}
		}

		private void ProcessStopChasingActions()
		{
			foreach (var attacker in _tickActionsToStopChasing)
			{
				if (!_context.World.IsAlive(attacker)) continue;

				if (_context.World.Has<MoveTo>(attacker)) _context.World.Remove<MoveTo>(attacker);
				if (_context.World.Has<Velocity>(attacker)) _context.World.Set(attacker, new Velocity(Vector3.Zero));
			}
		}
	}
}