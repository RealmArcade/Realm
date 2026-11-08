﻿using System;
using System.Numerics;
using Realm.Client.Core;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Meta;

namespace Realm.Client.Services;

/// <summary>
///     Whether a spot on the map can hold a building: the terrain's buildable flag first, then the bodies
///     already standing there within the combined collision radii, and the ring search that finds the
///     nearest free spot when the exact position is taken.
/// </summary>
internal static class PlacementQueries
{
	private static EcsWorld World => GameContext.World
		?? throw new InvalidOperationException("PlacementQueries used before a world existed.");

	internal static bool TryPlaceBuilding(Vector3 position, float clearance)
	{
		return !IsAreaObstructed(position, clearance);
	}

	internal static bool IsAreaObstructed(Vector3 position, float clearance)
	{
		if (GameContext.World != null && World.IsAlive(GameContext.Simulation.WorldEntity) && World.Has<Realm.Ecs.Components.Terrain.TerrainState>(GameContext.Simulation.WorldEntity))
		{
			ref var terrain = ref World.Get<Realm.Ecs.Components.Terrain.TerrainState>(GameContext.Simulation.WorldEntity);
			if (terrain.PathingCodes != null && terrain.Width > 0 && terrain.Depth > 0 && terrain.QuadSize > 0f)
			{
				int cx = Math.Clamp((int)MathF.Floor(position.X / terrain.QuadSize + terrain.Width / 2.0f), 0, terrain.Width - 1);
				int cz = Math.Clamp((int)MathF.Floor(position.Z / terrain.QuadSize + terrain.Depth / 2.0f), 0, terrain.Depth - 1);
				if ((terrain.PathingCodes[cx, cz] & (int)Realm.Ecs.Components.Terrain.TerrainPathingFlags.Buildable) == 0)
				{
					return true;
				}
			}
		}

		bool obstructed = false;
		var query = QueryCache.AllPositionNoneDeadQuery;
		World.Query(in query, (Entity entity, ref Position pos) =>
		{
			if (obstructed) return;

			if (World.Has<DefinitionId>(entity))
			{
				string defId = World.Get<DefinitionId>(entity).Value;
				if (GameHost.TryGetUnitOrBuildingMetadata(defId, out var meta) &&
				    (meta.PathingType == 16 || string.Equals(meta.VisualMode, "GroundPlane", StringComparison.OrdinalIgnoreCase)))
				{
					return;
				}
			}
			else if (!World.Has<PropIdentity>(entity))
			{
				return;
			}

			float defaultRadius = World.Has<PropIdentity>(entity) ? GameplayConstants.DefaultPropCollisionRadius : GameplayConstants.DefaultCollisionRadius;
			float entityRadius = GameplayConstants.ResolveBodyRadius(World, entity, defaultRadius);
			float minDist = MathF.Max(clearance, entityRadius + 0.5f);
			float dx = position.X - pos.Value.X;
			float dz = position.Z - pos.Value.Z;
			if (dx * dx + dz * dz < minDist * minDist)
			{
				obstructed = true;
			}
		});
		return obstructed;
	}

	internal static bool IsPositionBlocked(Vector3 pos, float checkRadius, Entity ignoreEntity)
	{
		bool blocked = false;

		var query = QueryCache.AllPositionNoneDeadQuery;
		World.Query(in query, (Entity entity, ref Position entityPos) =>
		{
			if (entity == ignoreEntity) return;

			float r1 = checkRadius;
			float r2 = 1.2f;

			if (World.Has<DefinitionId>(entity))
			{
				string defId = World.Get<DefinitionId>(entity).Value;
				float scale = World.Has<ModelScale>(entity) ? World.Get<ModelScale>(entity).Value : 1.0f;
				r2 = GetPlacementRadius(defId, scale);
			}
			else if (World.Has<PropIdentity>(entity))
			{
				string propId = World.Get<PropIdentity>(entity).PropId;
				float scale = World.Has<ModelScale>(entity) ? World.Get<ModelScale>(entity).Value : 1.0f;
				r2 = GetPlacementRadius(propId, scale);
			}
			else
			{
				float scale = World.Has<ModelScale>(entity) ? World.Get<ModelScale>(entity).Value : 1.0f;
				r2 = scale * 1.2f;
			}

			float dx = entityPos.Value.X - pos.X;
			float dz = entityPos.Value.Z - pos.Z;
			float distXZ = (float)Math.Sqrt(dx * dx + dz * dz);

			if (distXZ < (r1 + r2) * 0.85f)
			{
				blocked = true;
			}
		});

		return blocked;
	}

	internal static float GetPlacementRadius(string placeId, float scale = 1.0f)
	{
		return GameContext.RegistryService.GetPlacementRadius(placeId, scale);
	}

	internal static Vector3? FindNearestFreePosition(Vector3 startPos, float checkRadius, float maxSearchDist = 20.0f, float terrainQuadSize = 2.0f, int terrainWidth = 256, int terrainDepth = 256)
	{
		if (!IsPositionBlocked(startPos, checkRadius, Entity.Null))
		{
			return startPos;
		}

		float stepDist = 1.0f;
		int numSteps = (int)Math.Ceiling(maxSearchDist / stepDist);

		for (int i = 1; i <= numSteps; i++)
		{
			float dist = i * stepDist;
			int numAngles = 8 + i * 4;
			for (int a = 0; a < numAngles; a++)
			{
				float angle = a * ((float)Math.PI * 2.0f) / numAngles;
				var testPos = new Vector3(
					startPos.X + dist * (float)Math.Cos(angle),
					startPos.Y,
					startPos.Z + dist * (float)Math.Sin(angle)
				);

				float halfW = (terrainWidth - 1) / 2.0f * terrainQuadSize;
				float halfD = (terrainDepth - 1) / 2.0f * terrainQuadSize;
				if (Math.Abs(testPos.X) > halfW || Math.Abs(testPos.Z) > halfD) continue;

				if (!IsPositionBlocked(testPos, checkRadius, Entity.Null))
				{
					return testPos;
				}
			}
		}

		return null;
	}
}
