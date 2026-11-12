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
			ApplyObstacleCollision(baseCx, baseCz, r1, ref nextPos);
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

						ResolveCollision(r1, other.CollisionRadius, other.Position, ref nextPos);
					}
				}
			}
		}

		private void ApplyObstacleCollision(int baseCx, int baseCz, float r1, ref Vector3 nextPos)
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
			Vector3 pushDir = otherDist < 0.001f
				? new Vector3(1f, 0f, 0f)
				: new Vector3(ox / otherDist, 0f, oz / otherDist);

			if (otherDist < 0.001f) otherDist = 1f;

			float overlap = minDist - otherDist;
			nextPos += pushDir * overlap;
		}
	}
}
