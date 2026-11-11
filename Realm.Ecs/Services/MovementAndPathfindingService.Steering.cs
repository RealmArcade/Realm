// Extracted from MovementAndPathfindingService.cs (task 6 decomposition).
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Realm.Ecs;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;

namespace Realm.Ecs.Services
{
	public partial class MovementAndPathfindingService
	{
		private void AccumulateSteeringForces(Entity entity, Vector3 current, long key, ref Vector3 cohesion, ref Vector3 alignment, ref Vector3 separation, ref int neighborCount)
		{
			if (!_unitGrid.TryGetValue(key, out var list)) return;

			for (int i = 0; i < list.Count; i++)
			{
				var other = list[i];
				ProcessNeighborSteering(entity, current, in other, ref cohesion, ref alignment, ref separation, ref neighborCount);
			}
		}

		private void ProcessNeighborSteering(Entity entity, Vector3 current, in SpatialGridEntry other, ref Vector3 cohesion, ref Vector3 alignment, ref Vector3 separation, ref int neighborCount)
		{
			if (other.Entity == entity) return;
			if (GhostsUnitCollision(entity, other.Entity)) return;

			Vector3 otherPos = other.Position;
			Vector3 diff = current - otherPos;
			float distSq = diff.LengthSquared();
			if (distSq <= 0f || distSq >= 16.0f) return;

			cohesion += otherPos;
			if (other.HasVelocity)
			{
				alignment += other.Velocity;
			}

			separation += diff / distSq;
			neighborCount++;
		}

		private Vector3 FinalizeSteeringForce(Vector3 current, Vector3 desiredVelocity, float actualSpeed, bool stuck, Vector3 cohesion, Vector3 alignment, Vector3 separation, int neighborCount)
		{
			cohesion = (cohesion / neighborCount) - current;
			if (cohesion.LengthSquared() > 0.001f) cohesion = Vector3.Normalize(cohesion) * actualSpeed;

			alignment = alignment / neighborCount;
			if (alignment.LengthSquared() > 0.001f) alignment = Vector3.Normalize(alignment) * actualSpeed;

			if (separation.LengthSquared() > 0.001f) separation = Vector3.Normalize(separation) * actualSpeed;

			float desiredWeight = stuck ? 0.75f : 0.90f;
			float separationWeight = stuck ? 0.30f : 0.04f;
			Vector3 steering = desiredVelocity * desiredWeight + separation * separationWeight + cohesion * 0.04f + alignment * 0.03f;

			return steering.LengthSquared() > 0.001f ? Vector3.Normalize(steering) * actualSpeed : steering;
		}

		private void ApplyCollisionAvoidance(Entity entity, Vector3 current, ref Vector3 nextPos)
		{
			float scale1 = _context.World.Has<CollisionScale>(entity) ? _context.World.Get<CollisionScale>(entity).Value : 1.0f;
			float authoredRadius = _context.World.Has<CollisionRadius>(entity) ? _context.World.Get<CollisionRadius>(entity).Value : 0f;
			float r1 = GameplayConstants.ResolveUnitContactRadius(authoredRadius, scale1);

			int baseCx = (int)Math.Floor(nextPos.X / _collisionCellSize);
			int baseCz = (int)Math.Floor(nextPos.Z / _collisionCellSize);

			ApplyUnitCollision(entity, baseCx, baseCz, r1, ref nextPos);
			ApplyObstacleCollision(entity, baseCx, baseCz, r1, ref nextPos);
		}

		/// <summary>
		///     Answers whether the moving body passes through the other body instead of being stopped by it, which is what a
		///     hero does to every soldier and what a worker does to every worker of another player.
		/// </summary>
		/// <remarks>
	///     A hero is not body-blocked by soldiers, so a hero's retreat is never walled in by its own army. The rule is
	///     one-directional in effect: a soldier still resolves against a hero, so the soldier steps aside while the hero
	///     keeps its line. Two heroes, and two workers of the same player, resolve against each other, so a gathering
	///     crew keeps its shape while a rival crew walks through it.
		/// </remarks>
		private bool GhostsUnitCollision(Entity mover, Entity other)
		{
			var world = _context.World;
			if (world.Has<Hero>(mover)) return !world.Has<Hero>(other);

			if (!world.Has<Worker>(mover) || !world.Has<Worker>(other)) return false;
			return PlayerIndexOf(mover) != PlayerIndexOf(other);
		}

		/// <summary>
		///     Answers whether a hero passes through a static obstacle: allied buildings, enemy buildings still in their
		///     construction silhouette, and enemy buildings that were reduced to zero health and wait to be repaired.
		/// </summary>
		/// <remarks>
		///     Terrain props always block, so a hero cannot walk through a rock or a tree line. An enemy building that is
		///     functional or merely damaged still blocks, which is what makes a building worth defending.
		/// </remarks>
		private bool GhostsObstacleCollision(Entity mover, Entity obstacle)
		{
			var world = _context.World;
			if (!world.Has<Hero>(mover)) return false;
			if (!world.Has<Building>(obstacle)) return false;

			if (AreAlliedPlayers(PlayerIndexOf(mover), PlayerIndexOf(obstacle))) return true;
			return world.Has<UnderConstruction>(obstacle) || world.Has<Dormant>(obstacle);
		}

		private int PlayerIndexOf(Entity entity)
		{
			var world = _context.World;
			if (entity == Entity.Null || !world.IsAlive(entity)) return 0;
			if (world.Has<UnitOwnerPlayer>(entity)) return world.Get<UnitOwnerPlayer>(entity).PlayerIndex;
			if (world.Has<Owner>(entity))
			{
				Entity playerEntity = world.Get<Owner>(entity).PlayerEntity.Value;
				if (world.IsAlive(playerEntity) && world.Has<UnitOwnerPlayer>(playerEntity))
					return world.Get<UnitOwnerPlayer>(playerEntity).PlayerIndex;
			}

			return 0;
		}

		private bool AreAlliedPlayers(int playerIndex, int otherPlayerIndex)
		{
			if (playerIndex == otherPlayerIndex) return true;

			Entity worldEntity = _context.WorldEntity;
			if (worldEntity == Entity.Null || !_context.World.IsAlive(worldEntity)) return false;
			if (!_context.World.Has<PlayerRelationshipMatrix>(worldEntity)) return false;

			return _context.World.Get<PlayerRelationshipMatrix>(worldEntity).AreAllied(playerIndex, otherPlayerIndex);
		}

		private void ApplyUnitCollision(Entity entity, int baseCx, int baseCz, float r1, ref Vector3 nextPos)
		{
			for (int dx = -1; dx <= 1; dx++)
			{
				for (int dz = -1; dz <= 1; dz++)
				{
					long key = ((long)(baseCx + dx) << 32) | (uint)(baseCz + dz);
					if (!_unitGrid.TryGetValue(key, out var list)) continue;

					for (int i = 0; i < list.Count; i++)
					{
						var other = list[i];
						if (other.Entity == entity) continue;
						if (GhostsUnitCollision(entity, other.Entity)) continue;

						ResolveCollision(r1, other.CollisionRadius, other.Position, ref nextPos);
					}
				}
			}
		}

		private void ApplyObstacleCollision(Entity entity, int baseCx, int baseCz, float r1, ref Vector3 nextPos)
		{
			for (int dx = -1; dx <= 1; dx++)
			{
				for (int dz = -1; dz <= 1; dz++)
				{
					long key = ((long)(baseCx + dx) << 32) | (uint)(baseCz + dz);
					if (!_obstacleGrid.TryGetValue(key, out var list)) continue;

					for (int i = 0; i < list.Count; i++)
					{
						var obstacle = list[i];
						if (GhostsObstacleCollision(entity, obstacle.Entity)) continue;

						ResolveCollision(r1, obstacle.CollisionRadius, obstacle.Position, ref nextPos);
					}
				}
			}
		}

		private void ResolveCollision(float r1, float r2, Vector3 otherPos, ref Vector3 nextPos)
		{
			float minDist = (r1 + r2) * GameplayConstants.CollisionSeparationFactor;
			float ox = nextPos.X - otherPos.X;
			float oz = nextPos.Z - otherPos.Z;
			float distSq = ox * ox + oz * oz;

			if (distSq >= minDist * minDist) return;

			float otherDist = (float)Math.Sqrt(distSq);
			if (otherDist < 0.001f)
			{
				nextPos += new Vector3(minDist, 0f, 0f);
				return;
			}

			float lateralFraction = oz / otherDist;

			// A body pressed exactly head-on has no sideways component to slide along, so pushing it straight back
			// would cancel its own step every tick and leave it pressed against the other body forever. Giving the
			// push a fixed sideways lean turns that deadlock into a slide around the contact.
			if (MathF.Abs(lateralFraction) < HeadOnLateralLeanThreshold) lateralFraction = HeadOnLateralLean;

			Vector3 pushDir = Vector3.Normalize(new Vector3(ox / otherDist, 0f, lateralFraction));
			float overlap = minDist - otherDist;
			nextPos += pushDir * overlap;
		}
	}
}
