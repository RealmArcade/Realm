using Realm.Ecs.AI.Affordances;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Combat;
using System;
using System.Linq;
using System.Numerics;
using Arch.Core;
using Realm.Ecs.Components;

namespace Realm.Ecs.AI.Genres;

public static class AiGenreUtility
{
	public static int GetOwnerIndex(World world, Entity entity)
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

	public static int GetPlayerGold(World world, Entity playerEntity)
	{
		if (!world.IsAlive(playerEntity) || !world.Has<PlayerResources>(playerEntity)) return 1000;

		var resources = world.Get<PlayerResources>(playerEntity).Value;
		return resources.Count > 0 ? resources.Values.FirstOrDefault() : 1000;
	}

	public static void HandleCastIntent(World world, int playerIndex, in GenericAffordance aff, Action<int, string, Vector3, string>? customActionCallback)
	{
		if (!string.IsNullOrEmpty(aff.PayloadId) && world.Has<SpellCooldowns>(aff.SourceEntity))
		{
			ref var cooldowns = ref world.Get<SpellCooldowns>(aff.SourceEntity);
			cooldowns.Value[aff.PayloadId] = 5.0f;
		}

		customActionCallback?.Invoke(playerIndex, aff.PayloadId, aff.TargetPosition, "Cast");
	}
}
