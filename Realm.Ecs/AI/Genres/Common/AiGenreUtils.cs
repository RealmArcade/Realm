using System;
using System.Numerics;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.AI.Affordances;

namespace Realm.Ecs.AI.Genres.Common;

public static class AiGenreUtils
{
	public static int GetOwnerIndex(EcsWorld world, Entity entity)
	{
		if (world.Has<UnitOwnerPlayer>(entity))
		{
			return world.Get<UnitOwnerPlayer>(entity).PlayerIndex;
		}

		if (!world.Has<Owner>(entity)) return -1;

		var pEnt = world.Get<Owner>(entity).PlayerEntity.Value;
		if (world.IsAlive(pEnt) && world.Has<UnitOwnerPlayer>(pEnt))
		{
			return world.Get<UnitOwnerPlayer>(pEnt).PlayerIndex;
		}

		return -1;
	}

	public static int GetPlayerGold(EcsWorld world, Entity playerEntity, int fallback = 1000)
	{
		if (!world.IsAlive(playerEntity) || !world.Has<PlayerResources>(playerEntity)) return fallback;

		return world.Get<PlayerResources>(playerEntity).Gold;
	}

	public static int GetPlayerGold(EcsWorld world, int playerIndex, in QueryDescription playerResourcesQuery, int fallback = 500)
	{
		Entity playerEntity = Entity.Null;
		world.Query(in playerResourcesQuery, (Entity pe) =>
		{
			if (world.Has<UnitOwnerPlayer>(pe) && world.Get<UnitOwnerPlayer>(pe).PlayerIndex == playerIndex)
			{
				playerEntity = pe;
			}
			else if (playerEntity == Entity.Null)
			{
				playerEntity = pe;
			}
		});

		if (world.IsAlive(playerEntity) && world.Has<PlayerResources>(playerEntity))
		{
			return world.Get<PlayerResources>(playerEntity).Gold;
		}

		return fallback;
	}

	public static void HandleCastIntent(EcsWorld world, int playerIndex, in GenericAffordance aff, Action<int, string, Vector3, string>? customActionCallback)
	{
		if (!string.IsNullOrEmpty(aff.PayloadId) && world.Has<SpellCooldowns>(aff.SourceEntity))
		{
			ref var cooldowns = ref world.Get<SpellCooldowns>(aff.SourceEntity);
			cooldowns.Set(aff.PayloadId, 5.0f, 5.0f);
		}

		customActionCallback?.Invoke(playerIndex, aff.PayloadId, aff.TargetPosition, "Cast");
	}

	public static float[] CreateFeatureVector(float f0, float f1, float f2, float f3, float f4, float f5, float f6, float f7, int featureCount = 8)
	{
		var fVec = new float[featureCount];
		if (featureCount > 0) fVec[0] = f0;
		if (featureCount > 1) fVec[1] = f1;
		if (featureCount > 2) fVec[2] = f2;
		if (featureCount > 3) fVec[3] = f3;
		if (featureCount > 4) fVec[4] = f4;
		if (featureCount > 5) fVec[5] = f5;
		if (featureCount > 6) fVec[6] = f6;
		if (featureCount > 7) fVec[7] = f7;
		return fVec;
	}
}
