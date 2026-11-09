using System;
using System.Collections.Generic;
using Realm.Client.Core;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Replication;
using Realm.Shared.Distribution;
using Realm.Shared.Network;

namespace Realm.Client.Services;

/// <summary>
///     Holding the ground loot the host describes in step with what this screen has spawned: an entry the
///     snapshot still lists is refreshed in place (or spawned), and a loot entity it no longer lists is
///     destroyed, so a picked-up pile cannot linger on this screen after the host has removed it.
/// </summary>
public sealed class ReplicatedLootApplier
{
	private static EcsWorld Ecs => GameContext.World
		?? throw new InvalidOperationException("The loot applier was used before a world existed.");

	private readonly Action<NetworkMappingState, int> _retireReplica;
	private readonly HashSet<int> _describedLootServerIds = new();
	private readonly List<int> _staleLootServerIds = new();

	public ReplicatedLootApplier(Action<NetworkMappingState, int> retireReplica)
	{
		_retireReplica = retireReplica;
	}

	public void Apply(WorldSnapshot snapshot, NetworkMappingState mapping)
	{
		if (snapshot.Loot == null) return;

		_describedLootServerIds.Clear();
		for (int index = 0; index < snapshot.Loot.Count; index++)
		{
			LootSnapshot entry = snapshot.Loot[index];
			_describedLootServerIds.Add(entry.EntityId);
			ApplyLootEntry(in entry, mapping);
		}

		_staleLootServerIds.Clear();
		foreach (var kvp in mapping.ServerToClientEntityMap)
		{
			if (_describedLootServerIds.Contains(kvp.Key)) continue;
			if (!Ecs.Has<GroundLoot>(kvp.Value)) continue;
			_staleLootServerIds.Add(kvp.Key);
		}

		for (int index = 0; index < _staleLootServerIds.Count; index++)
		{
			int serverEntityId = _staleLootServerIds[index];
			if (!mapping.ServerToClientEntityMap.TryGetValue(serverEntityId, out Entity localEntity)) continue;
			ReplicatedLootLifecycle.Destroy(serverEntityId, localEntity, mapping, Ecs);
		}
	}

	private void ApplyLootEntry(in LootSnapshot entry, NetworkMappingState mapping)
	{
		var position = new System.Numerics.Vector3(entry.Position.X, entry.Position.Y, entry.Position.Z);			if (mapping.ServerToClientEntityMap.TryGetValue(entry.EntityId, out Entity localEntity)
				&& Ecs.IsAlive(localEntity)
				&& Ecs.Has<GroundLoot>(localEntity))
		{
			GroundLoot loot = Ecs.Get<GroundLoot>(localEntity);
			loot.UniqueId = entry.EntityId;
			loot.ItemTypeId = entry.ItemTypeId;
			loot.Charges = entry.Charges;
			loot.Lifetime = entry.Lifetime;
			loot.RemainingLifetime = entry.RemainingLifetime;
			loot.PickupRadius = entry.PickupRadius;
			loot.AutoPickup = entry.AutoPickup;
			loot.OwnerPlayerIndex = entry.OwnerPlayerIndex;

			Ecs.Set(localEntity, loot);
			Ecs.Set(localEntity, new Position(position));
			GameContext.Host?.SyncReplicatedLootNode(entry.EntityId, localEntity);
			return;
		}

		_retireReplica(mapping, entry.EntityId);
		localEntity = ReplicatedLootLifecycle.Create(entry.EntityId, mapping, Ecs);
		var described = new GroundLoot(entry.EntityId, entry.ItemTypeId, entry.Charges, entry.Lifetime, entry.PickupRadius, entry.AutoPickup, entry.OwnerPlayerIndex)
		{
			RemainingLifetime = entry.RemainingLifetime
		};

		Ecs.Set(localEntity, new Position(position));
		Ecs.Set(localEntity, described);
		GameContext.Host?.SyncReplicatedLootNode(entry.EntityId, localEntity);
	}
}
