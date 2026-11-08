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
	/// <summary>the abandoned target cooldown.</summary>
	public partial class CombatAndDamageService
	{
		private bool IsTargetStillAbandoned(Entity attacker, Entity target)
		{
			return _abandonedTargetCooldown.TryGetValue(attacker, out var entry)
			       && entry.Remaining > 0f
			       && entry.Target == target;
		}

		private void RegisterAbandonedTarget(Entity attacker, Entity target)
		{
			RegisterAbandonedTarget(attacker, target, AbandonedTargetCooldownSeconds);
		}

		private void RegisterAbandonedTarget(Entity attacker, Entity target, float cooldownSeconds)
		{
			_abandonedTargetCooldown[attacker] = (target, cooldownSeconds);
		}

		private void ClearChaseTracking(Entity entity)
		{
			_chaseStuckTime.Remove(entity);
			_lastChaseDist.Remove(entity);
		}
	}
}