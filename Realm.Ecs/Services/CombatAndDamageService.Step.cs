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
	/// <summary>the caches and cooldowns the step ages out.</summary>
	public partial class CombatAndDamageService
	{
		public void StepCombat(float delta, MovementAndPathfindingService? movementService = null)
		{
			_currentMovementService = movementService;
			_combatTickCount++;
			_combatDelta = delta;
			_combatTotalTime += delta;
			TickCombatAlertTimer(delta);
			TickAbandonedTargetCooldowns(delta);

			if (_combatTickCount % 30 == 0)
			{
				PruneRouteReachabilityCache();
			}

			if (!TryGetNavMeshQuery(out _currentNavMeshQuery, out _currentNavMesh))
			{
				_currentNavMeshQuery = null;
				_currentNavMesh = null;
			}

			ProcessTargetAcquisition();
			ProcessCombatTicks();
			ProcessHealingTicks();
		}

		private void PruneRouteReachabilityCache()
		{
			if (_routeReachabilityCache.Count == 0) return;
			_staleRouteReachabilityEntries.Clear();
			foreach (var kvp in _routeReachabilityCache)
			{
				if (!_context.World.IsAlive(kvp.Key.Attacker) || !_context.World.IsAlive(kvp.Key.Target))
				{
					_staleRouteReachabilityEntries.Add(kvp.Key);
					continue;
				}

				float maxAge = kvp.Value.Reachable ? RouteCacheMaxAge : RouteCacheNegativeMaxAge;
				if (_combatTotalTime - kvp.Value.Timestamp > maxAge)
				{
					_staleRouteReachabilityEntries.Add(kvp.Key);
				}
			}

			for (int i = 0; i < _staleRouteReachabilityEntries.Count; i++)
			{
				_routeReachabilityCache.Remove(_staleRouteReachabilityEntries[i]);
			}
		}

		private void TickAbandonedTargetCooldowns(float delta)
		{
			if (_abandonedTargetCooldown.Count == 0) return;
			_tickExpiredAbandonedCooldowns.Clear();
			foreach (var kvp in _abandonedTargetCooldown)
			{
				var entry = kvp.Value;
				entry.Remaining -= delta;
				if (entry.Remaining <= 0f)
				{
					_tickExpiredAbandonedCooldowns.Add(kvp.Key);
				}
				else
				{
					_abandonedTargetCooldown[kvp.Key] = entry;
				}
			}

			for (int i = 0; i < _tickExpiredAbandonedCooldowns.Count; i++)
			{
				_abandonedTargetCooldown.Remove(_tickExpiredAbandonedCooldowns[i]);
			}
		}
	}
}