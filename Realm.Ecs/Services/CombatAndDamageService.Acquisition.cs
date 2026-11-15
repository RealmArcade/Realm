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
	/// <summary>picking a target for each attacker.</summary>
	public partial class CombatAndDamageService
	{
		private void ProcessTargetAcquisition()
		{
			if (!HasAuthority)
			{
				_attackerCount = 0;
				_targetCandidateCount = 0;
				return;
			}

			if (_currentMovementService == null)
			{
				_targetCandidateCount = 0;
				_context.World.Query(in _enemyQuery, (Entity potentialEnemy, ref Position enemyPos, ref Owner enemyOwner) =>
				{
					if (_context.World.Has<Invulnerable>(potentialEnemy) || _context.World.Has<Dormant>(potentialEnemy)) return;
					int pIdx = GetPlayerIndexForEntity(potentialEnemy);
					bool flying = IsFlying(potentialEnemy);
					float colRadius = GetTargetCollisionRadius(potentialEnemy);
					float contactRadius = GetUnitContactRadius(potentialEnemy);

					if (_targetCandidateCount >= _targetCandidateArray.Length)
						Array.Resize(ref _targetCandidateArray, _targetCandidateArray.Length * 2);

					_targetCandidateArray[_targetCandidateCount++] = new TargetCandidateSnapshot(potentialEnemy, enemyPos.Value, pIdx, flying, colRadius, contactRadius);
				});

				if (_targetCandidateCount == 0) return;
			}

			_attackerCount = 0;
			int timeOfDay = GetTimeOfDayIndex();
			_context.World.Query(in _targetAcquisitionQuery, (Entity entity, ref Position pos, ref Attack atk, ref Owner owner) =>
			{
				if (atk.Damage <= 0f || _context.World.Has<Invulnerable>(entity) || _context.World.Has<Dormant>(entity) || _context.World.Has<CommandsFrozen>(entity)) return;
				if (!ShouldSimulateEntity(entity)) return;

				bool isAutoCombat = _context.World.Has<AutoCombatState>(entity) && _context.World.Get<AutoCombatState>(entity).Enabled;
				bool isAttackMove = _context.World.Has<AttackMove>(entity);
				bool isPatrol = _context.World.Has<Patrol>(entity);
				bool isIdle = !_context.World.Has<MoveTo>(entity) && !isAttackMove;

				if (!isIdle && !isAttackMove && !isPatrol && !isAutoCombat) return;

				// Stagger target acquisition across ticks in simulation so idle entities do not all scan simultaneously on every 30Hz tick
				if (_currentMovementService != null && (entity.Id + _combatTickCount) % 4 != 0) return;

				float scanRadius = _context.World.Has<ScanRadius>(entity) ? _context.World.Get<ScanRadius>(entity).Value : 15.0f;
				if (timeOfDay == 2) scanRadius *= 0.7f;

				bool isFlying = IsFlying(entity);
				bool canTargetAir = !_context.World.Has<CombatTargeting>(entity) || _context.World.Get<CombatTargeting>(entity).CanTargetAir;
				bool isGroundMelee = !isFlying && atk.Range <= MeleeRangeThreshold;
				int includeFlags = _context.World.Has<TraversableTerrain>(entity) ? _context.World.Get<TraversableTerrain>(entity).Value : (int)TerrainPathingFlags.Ground;

				if (_attackerCount >= _attackerArray.Length)
					Array.Resize(ref _attackerArray, _attackerArray.Length * 2);

				_attackerArray[_attackerCount++] = new AttackerSnapshot
				{
					Entity = entity,
					Position = pos.Value,
					AttackRange = atk.Range,
					PlayerIndex = GetPlayerIndexForEntity(entity),
					IsFlying = isFlying,
					CanTargetAir = canTargetAir,
					IsGroundMelee = isGroundMelee,
					ScanMaxDistSq = scanRadius * scanRadius,
					TargetCollisionRadius = GetTargetCollisionRadius(entity),
					UnitContactRadius = GetUnitContactRadius(entity),
					IncludeFlags = includeFlags,
					SelectedTarget = Entity.Null
				};
			});

			if (_attackerCount == 0) return;

			if (_currentMovementService != null)
			{
				ParallelRunner.Execute(_attackerCount, _evaluateAttackerSpatialDelegate, _context.World);
			}
			else
			{
				ParallelRunner.Execute(_attackerCount, _evaluateAttackerFallbackDelegate, _context.World);
			}

			for (int i = 0; i < _attackerCount; i++)
			{
				ref readonly var attacker = ref _attackerArray[i];
				if (attacker.SelectedTarget != Entity.Null && _context.World.IsAlive(attacker.Entity))
				{
					_context.World.SetOrAdd(attacker.Entity, new AttackTarget(attacker.SelectedTarget));
				}
			}
		}

		private void EvaluateAttackerSpatialAction(int index)
		{
			EvaluateAttackerTargetWithSpatialGrid(ref _attackerArray[index]);
		}

		private void EvaluateAttackerFallbackAction(int index)
		{
			EvaluateAttackerTarget(ref _attackerArray[index], _targetCandidateArray, _targetCandidateCount);
		}

		private void EvaluateAttackerTargetWithSpatialGrid(ref AttackerSnapshot attacker)
		{
			var movementService = _currentMovementService;
			if (movementService == null)
				return;

			Span<LocalScanCandidate> localCandidates = stackalloc LocalScanCandidate[MaxAcquisitionCandidates];
			int candidateCount = 0;
			float scanRadius = MathF.Sqrt(attacker.ScanMaxDistSq);
			float cellSize = movementService.CollisionCellSize;
			var center = attacker.Position;
			var world = _context.World;

			int minCx = (int)Math.Floor((center.X - scanRadius) / cellSize);
			int maxCx = (int)Math.Floor((center.X + scanRadius) / cellSize);
			int minCz = (int)Math.Floor((center.Z - scanRadius) / cellSize);
			int maxCz = (int)Math.Floor((center.Z + scanRadius) / cellSize);

			for (int cx = minCx; cx <= maxCx; cx++)
			{
				for (int cz = minCz; cz <= maxCz; cz++)
				{
					if (!movementService.TryGetUnitGridCell(cx, cz, out var list) || list == null)
						continue;

					for (int idx = 0; idx < list.Count; idx++)
					{
						var entry = list[idx];
						Entity potentialEnemy = entry.Entity;
						if (potentialEnemy == attacker.Entity) continue;
						if (!world.IsAlive(potentialEnemy) || world.Has<Dead>(potentialEnemy) || world.Has<Invulnerable>(potentialEnemy) || world.Has<Dormant>(potentialEnemy)) continue;

						int candidatePlayer = GetPlayerIndexForEntity(potentialEnemy);
						if (!ArePlayerIndicesEnemies(attacker.PlayerIndex, candidatePlayer)) continue;

						bool flying = IsFlying(potentialEnemy);
						if (flying && (attacker.IsGroundMelee || !attacker.CanTargetAir)) continue;

						float distSq = (center - entry.Position).LengthSquared();
						if (distSq < attacker.ScanMaxDistSq)
						{
							float colRadius = GetTargetCollisionRadius(potentialEnemy);
							float contactRadius = GetUnitContactRadius(potentialEnemy);
							TargetCandidateSnapshot candidate = new TargetCandidateSnapshot(potentialEnemy, entry.Position, candidatePlayer, flying, colRadius, contactRadius);
							InsertLocalCandidate(localCandidates, ref candidateCount, candidate, distSq);
						}
					}
				}
			}

			EvaluateLocalCandidates(ref attacker, localCandidates, candidateCount);
		}

		private void EvaluateAttackerTarget(ref AttackerSnapshot attacker, TargetCandidateSnapshot[] candidates, int candidateCountTotal)
		{
			Span<LocalScanCandidate> localCandidates = stackalloc LocalScanCandidate[MaxAcquisitionCandidates];
			int candidateCount = 0;

			for (int c = 0; c < candidateCountTotal; c++)
			{
				ref readonly var candidate = ref candidates[c];
				if (ArePlayerIndicesEnemies(attacker.PlayerIndex, candidate.PlayerIndex))
				{
					if (candidate.IsFlying && (attacker.IsGroundMelee || !attacker.CanTargetAir))
					{
						continue;
					}

					float distSq = (attacker.Position - candidate.Position).LengthSquared();
					if (distSq < attacker.ScanMaxDistSq)
					{
						InsertLocalCandidate(localCandidates, ref candidateCount, candidate, distSq);
					}
				}
			}

			EvaluateLocalCandidates(ref attacker, localCandidates, candidateCount);
		}

		private void EvaluateLocalCandidates(ref AttackerSnapshot attacker, Span<LocalScanCandidate> localCandidates, int candidateCount)
		{
			for (int i = 0; i < candidateCount; i++)
			{
				ref readonly var cand = ref localCandidates[i];
				if (cand.Enemy == Entity.Null) continue;
				if (IsTargetStillAbandoned(attacker.Entity, cand.Enemy))
				{
					continue;
				}

				float reach = attacker.AttackRange + cand.TargetCollisionRadius + attacker.TargetCollisionRadius;
				bool isMelee = !attacker.IsFlying && !cand.IsFlying && attacker.AttackRange <= MeleeRangeThreshold;
				float meleeVerticalReach = Math.Max(attacker.AttackRange, DefaultMeleeVerticalReach);
				if (isMelee)
				{
					float contactReach = GetMinimumMeleeContactReach(attacker.UnitContactRadius, cand.UnitContactRadius);
					if (reach < contactReach)
					{
						reach = contactReach;
					}
				}

				bool reachable = IsReachableWithinRangeThreadSafe(attacker.Entity, cand.Enemy, attacker.Position, cand.Position, reach, isMelee, meleeVerticalReach, attacker.IsFlying, attacker.IncludeFlags);
				if (!reachable)
				{
					continue;
				}

				attacker.SelectedTarget = cand.Enemy;
				break;
			}
		}

		private static void InsertLocalCandidate(Span<LocalScanCandidate> candidates, ref int count, in TargetCandidateSnapshot target, float distSq)
		{
			int n = count;
			if (n >= MaxAcquisitionCandidates && distSq >= candidates[MaxAcquisitionCandidates - 1].DistSq)
			{
				return;
			}

			int i = n < MaxAcquisitionCandidates ? n : MaxAcquisitionCandidates - 1;
			while (i > 0 && candidates[i - 1].DistSq > distSq)
			{
				candidates[i] = candidates[i - 1];
				i--;
			}

			candidates[i] = new LocalScanCandidate
			{
				Enemy = target.Entity,
				Position = target.Position,
				TargetCollisionRadius = target.TargetCollisionRadius,
				UnitContactRadius = target.UnitContactRadius,
				IsFlying = target.IsFlying,
				DistSq = distSq
			};

			if (n < MaxAcquisitionCandidates)
			{
				count = n + 1;
			}
		}
	}
}