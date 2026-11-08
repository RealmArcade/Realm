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
	/// <summary>the cached world and terrain lookups and the predicates the tick reads.</summary>
	public partial class CombatAndDamageService
	{
		private Entity _cachedWorldEntity = Entity.Null;

		private Entity ActiveWorldEntity
		{
			get
			{
				if (_cachedWorldEntity == Entity.Null || !_context.World.IsAlive(_cachedWorldEntity))
				{
					if (_context.WorldEntity != Entity.Null && _context.World.IsAlive(_context.WorldEntity))
					{
						_cachedWorldEntity = _context.WorldEntity;
					}
					else
					{
						var query = QueryCache.AllWorldStateQuery;
						_context.World.Query(in query, (Entity entity) => _cachedWorldEntity = entity);
					}
				}
				return _cachedWorldEntity;
			}
		}

		private Entity _cachedTerrainEntity = Entity.Null;

		private Entity ActiveTerrainEntity
		{
			get
			{
				if (_cachedTerrainEntity == Entity.Null || !_context.World.IsAlive(_cachedTerrainEntity))
				{
					var we = ActiveWorldEntity;
					if (we != Entity.Null && _context.World.Has<TerrainState>(we))
					{
						_cachedTerrainEntity = we;
					}
					else
					{
						var query = QueryCache.AllTerrainStateQuery;
						_context.World.Query(in query, (Entity entity) => _cachedTerrainEntity = entity);
					}
				}
				return _cachedTerrainEntity;
			}
		}

		private Entity FindWorldEntity() => ActiveWorldEntity;

		private Entity FindTerrainEntity() => ActiveTerrainEntity;

		private int GetTimeOfDayIndex()
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && _context.World.Has<WorldState>(worldEntity))
			{
				return _context.World.Get<WorldState>(worldEntity).TimeOfDayIndex;
			}

			return 0;
		}

		private float GetCombatAlertTimer()
		{
			Entity worldEntity = ActiveWorldEntity;
			if (worldEntity != Entity.Null && _context.World.Has<CombatAlertState>(worldEntity))
			{
				return _context.World.Get<CombatAlertState>(worldEntity).UnderAttackAlertTimer;
			}

			return 0f;
		}

		private void SetCombatAlertTimer(float value)
		{
			Entity worldEntity = ActiveWorldEntity;
			if (worldEntity != Entity.Null && _context.World.Has<CombatAlertState>(worldEntity))
			{
				ref var state = ref _context.World.Get<CombatAlertState>(worldEntity);
				state.UnderAttackAlertTimer = value;
			}
		}

		private void TickCombatAlertTimer(float fDelta)
		{
			Entity worldEntity = ActiveWorldEntity;
			if (worldEntity != Entity.Null && _context.World.Has<CombatAlertState>(worldEntity))
			{
				ref var state = ref _context.World.Get<CombatAlertState>(worldEntity);
				if (state.UnderAttackAlertTimer > 0f)
				{
					state.UnderAttackAlertTimer = Math.Max(0f, state.UnderAttackAlertTimer - fDelta);
				}
			}
		}

		private int GetPlayerIndexForEntity(Entity entity)
		{
			if (entity == Entity.Null || !_context.World.IsAlive(entity)) return 0;
			if (_context.World.Has<UnitOwnerPlayer>(entity))
				return _context.World.Get<UnitOwnerPlayer>(entity).PlayerIndex;
			if (_context.World.Has<Owner>(entity))
			{
				var pEnt = _context.World.Get<Owner>(entity).PlayerEntity.Value;
				if (_context.World.IsAlive(pEnt) && _context.World.Has<UnitOwnerPlayer>(pEnt))
					return _context.World.Get<UnitOwnerPlayer>(pEnt).PlayerIndex;
			}
			return 0;
		}

		private bool ArePlayerIndicesEnemies(int playerIndex, int otherPlayerIndex)
		{
			if (playerIndex == otherPlayerIndex) return false;

			Entity worldEntity = _context.WorldEntity;
			if (worldEntity != Entity.Null && _context.World.IsAlive(worldEntity) && _context.World.Has<PlayerRelationshipMatrix>(worldEntity))
			{
				// Only a pair the map actually decided about is answered by the table. A pair it never marked is neutral,
				// which is the state of two players who were never allied, and those two fight: treating neutral as
				// peaceful made every player in a match that declares no alliances ignore every other player.
				PlayerRelationship relationship = _context.World.Get<PlayerRelationshipMatrix>(worldEntity).GetRelationship(playerIndex, otherPlayerIndex);
				if ((relationship & PlayerRelationship.Ally) != 0) return false;
				if ((relationship & PlayerRelationship.Enemy) != 0) return true;
			}

			return EnemyPredicate?.Invoke(playerIndex, otherPlayerIndex) ?? playerIndex != otherPlayerIndex;
		}

		private bool HasAuthority => AuthoritativeProvider?.Invoke() ?? true;

		private bool ShouldSimulateEntity(Entity entity) => SimulateEntityFilter == null || SimulateEntityFilter(entity);
	}
}